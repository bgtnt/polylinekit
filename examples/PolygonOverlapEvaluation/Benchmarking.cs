using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using Clipper2Lib;
using NetTopologySuite.Geometries;
using PolylineKit;

namespace PolygonOverlapEvaluation;

internal static class Benchmarking
{
    private const int Cap = 65536;
    private static ulong sink;
    private sealed record Binary(string Name, string Sha256, string Version);
    private sealed record ScheduledPair(int PredictionIndex, int TruthIndex, bool BothConvex,
        bool Positive, bool Intersects, int SuppliedVertices);
    private sealed record Stratum(string Name, int[] PairIndices);
    private sealed record Sample(int Iterations, long Ticks, long AllocatedBytes, string Checksum);
    private sealed record Row(string Name, string Backend, int PairCount, int FallbackPairs,
        double Value, string ValueBits, double MedianNanoseconds, double MedianBytes, Sample[] Samples);

    internal static void Run(string inputPath, string outputFile, int run, string revision, bool smoke = false)
    {
        if (run is < 1 or > 3 || revision.Length != 40 || revision.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Provide process number 1..3 and a full source revision.");
        if (File.Exists(outputFile)) throw new IOException("Original measurement files must not be overwritten.");
        string protocol = Path.Combine(AppContext.BaseDirectory, "PROTOCOL.md");
        Binary[] binaries = [Describe(typeof(Benchmarking).Assembly), Describe(typeof(PolylineArea).Assembly),
            Describe(typeof(Clipper64).Assembly), Describe(typeof(Geometry).Assembly)];
        if (!smoke && binaries.Take(2).Any(b => !b.Version.EndsWith("+" + revision, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Harness and Core must embed the measured source revision.");
        if (!smoke && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0")
            throw new InvalidOperationException("Frozen protocol requires DOTNET_TieredCompilation=0.");
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string fixtureHash = Corpus.Hash(inputPath), protocolHash = Corpus.Hash(protocol);
        if (fixtureHash != Corpus.SolarisFixtureHash) throw new InvalidDataException("Benchmark input is not the frozen Solaris population.");
        Corpus corpus = Corpus.Load(inputPath);
        var reference = new PreparedEvaluation(corpus, GeometryBackends.Create("nts"));
        EvaluationResult result = reference.Run();
        VerifyResult(corpus, result, result);
        ScheduledPair[] schedule = result.Pairs.Select(p => new ScheduledPair(p.PredictionIndex, p.TruthIndex,
            reference.Predictions[p.PredictionIndex].IsConvexSimple && reference.Truth[p.TruthIndex].IsConvexSimple,
            p.Intersection > 0, p.Intersects, VertexCount(corpus.Predictions[p.PredictionIndex]) + VertexCount(corpus.Truth[p.TruthIndex]))).ToArray();
        Stratum[] strata = [Set("all", _ => true), Set("both-convex", p => p.BothConvex),
            Set("has-concavity", p => !p.BothConvex), Set("positive", p => p.Positive), Set("zero", p => !p.Positive),
            Set("vertices-0-32", p => p.SuppliedVertices <= 32), Set("vertices-33-64", p => p.SuppliedVertices is >= 33 and <= 64),
            Set("vertices-65-plus", p => p.SuppliedVertices > 64)];
        strata = strata.Where(s => s.PairIndices.Length > 0).ToArray();
        string[] order = Enumerable.Range(0, 4).Select(i => GeometryBackends.Names[(i + run - 1) % 4]).ToArray();
        var rows = new List<Row>();
        foreach (string backend in order)
        {
            var prepared = new PreparedEvaluation(corpus, GeometryBackends.Create(backend));
            EvaluationResult actual = prepared.Run(); VerifyResult(corpus, result, actual);
            foreach (Stratum stratum in strata)
            {
                PairKey[] keys = stratum.PairIndices.Select(i => new PairKey(schedule[i].PredictionIndex, schedule[i].TruthIndex)).ToArray();
                int fallbacks = keys.Count(key => prepared.Pair(key).UsedFallback);
                rows.Add(Measure("kernel/" + stratum.Name, backend, keys.Length, fallbacks, () =>
                {
                    double value = 0;
                    foreach (PairKey key in keys)
                    {
                        PairScore p = prepared.Pair(key); // All intersections and needed IoU arithmetic are recomputed.
                        value += p.Intersection + p.Union + p.IoU + p.MissingArea + p.ExcessArea;
                    }
                    return value;
                }, smoke));
            }
            rows.Add(Measure("prepare/all", backend, 0, 0, () =>
            {
                var fresh = new PreparedEvaluation(corpus, GeometryBackends.Create(backend));
                double value = fresh.Images.Length;
                foreach (PreparedPolygon p in fresh.Truth) value += p.Area;
                foreach (PreparedPolygon p in fresh.Predictions) value += p.Area;
                foreach (int[] indices in fresh.ImageTruth) value += indices.Length;
                foreach (int[] indices in fresh.ImagePredictions) value += indices.Length;
                return value;
            }, smoke));
            rows.Add(Measure("evaluator/all", backend, schedule.Length, actual.FallbackPairs, () =>
            {
                // Parsing, validation, all preparation, matching and result objects are inside the timer.
                EvaluationResult fresh = new PreparedEvaluation(Corpus.Load(inputPath), GeometryBackends.Create(backend)).Run();
                double value = fresh.EligibleTruth + fresh.EligiblePredictions + fresh.BoundsRejected;
                foreach (BuildingScore building in fresh.Buildings) value += building.IoU;
                foreach (PredictionScore prediction in fresh.Predictions) value += prediction.BestIoU + prediction.AssignedScore;
                foreach (ImageScore image in fresh.Images) value += image.TruePos + image.FalsePos + image.FalseNeg + image.F1Score;
                foreach (PairScore pair in fresh.Pairs) value += pair.Intersection + pair.IoU;
                return value;
            }, smoke));
        }
        if (Corpus.Hash(inputPath) != fixtureHash || Corpus.Hash(protocol) != protocolHash ||
            !binaries.SequenceEqual(new[] { Describe(typeof(Benchmarking).Assembly), Describe(typeof(PolylineArea).Assembly),
                Describe(typeof(Clipper64).Assembly), Describe(typeof(Geometry).Assembly) }))
            throw new InvalidOperationException("Input, protocol or measured assembly changed during the run.");
        Corpus.Write(outputFile, new
        {
            SchemaVersion = 1, Kind = "solaris-polygon-overlap", Run = run, Revision = revision.ToLowerInvariant(), Smoke = smoke,
            StartedUtc = started, FinishedUtc = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), LogicalProcessors = Environment.ProcessorCount,
            VectorHardwareAccelerated = Vector.IsHardwareAccelerated, Avx2 = Avx2.IsSupported, Fma = Fma.IsSupported,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), StopwatchFrequency = Stopwatch.Frequency,
            FixtureHash = fixtureHash, ProtocolHash = protocolHash, ClipperVersion = "2.0.0", ClipperScale = GeometryBackends.ClipperScale,
            NtsVersion = "2.6.0", Binaries = binaries, BackendOrder = order, Schedule = schedule, Strata = strata,
            Reference = new { result.EligiblePredictions, result.EligibleTruth, result.BoundsRejected,
                BoundsFalsePositives = schedule.Count(p => !p.Intersects), Positive = schedule.Count(p => p.Positive),
                TruePos = result.Images.Sum(i => i.TruePos), FalsePos = result.Images.Sum(i => i.FalsePos), FalseNeg = result.Images.Sum(i => i.FalseNeg) },
            Rows = rows
        });
        Console.WriteLine($"{(smoke ? "Smoke" : "Measured")} process {run}: {schedule.Length} candidates, {rows.Count} cells, {rows.Sum(r => r.Samples.Length)} samples.");

        Stratum Set(string name, Func<ScheduledPair, bool> select) =>
            new(name, Enumerable.Range(0, schedule.Length).Where(i => select(schedule[i])).ToArray());
    }

    private static int VertexCount(InputRow row) => row.Polygons.Sum(p => p.Sum(r => r.Length));

    private static Binary Describe(Assembly assembly) => new(assembly.GetName().Name!, Corpus.Hash(assembly.Location),
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");

    private static void VerifyResult(Corpus corpus, EvaluationResult expected, EvaluationResult actual)
    {
        var expectedBuildings = corpus.Expected.OrderBy(r => r.ImageId, StringComparer.Ordinal).ThenBy(r => r.BuildingId).ToArray();
        if (actual.Buildings.Length != expectedBuildings.Length || actual.Buildings.Where((b, i) =>
                b.ImageId != expectedBuildings[i].ImageId || b.BuildingId != expectedBuildings[i].BuildingId ||
                Math.Abs(b.IoU - expectedBuildings[i].IoU) >= 1e-9).Any() ||
            !actual.Pairs.Select(p => new PairKey(p.PredictionIndex, p.TruthIndex)).SequenceEqual(expected.Pairs.Select(p => new PairKey(p.PredictionIndex, p.TruthIndex))) ||
            !actual.Images.Select(i => (i.ImageId, i.TruePos, i.FalsePos, i.FalseNeg)).SequenceEqual(expected.Images.Select(i => (i.ImageId, i.TruePos, i.FalsePos, i.FalseNeg))) ||
            !actual.Predictions.Select(p => (p.ImageId, p.BuildingId, p.BestTruthBuildingId, p.Matched)).SequenceEqual(expected.Predictions.Select(p => (p.ImageId, p.BuildingId, p.BestTruthBuildingId, p.Matched))))
            throw new InvalidOperationException("Reference scores, pair schedule or matching decisions differ before timing.");
    }

    private static Row Measure(string name, string backend, int pairs, int fallbacks, Func<double> operation, bool smoke)
    {
        double value = operation();
        if (!double.IsFinite(value)) throw new InvalidOperationException("Non-finite benchmark output.");
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        int iterations = 1;
        if (!smoke)
        {
            long warm = Stopwatch.GetTimestamp();
            do { sink = (ulong)BitConverter.DoubleToInt64Bits(operation()); }
            while (Stopwatch.GetElapsedTime(warm).TotalMilliseconds < 40);
            while (true)
            {
                Sample calibration = SampleBatch(operation, iterations);
                if (calibration.Ticks >= Stopwatch.Frequency * .02 || iterations == Cap) break;
                iterations *= 2;
            }
        }
        Sample[] samples = Enumerable.Range(0, smoke ? 1 : 5).Select(_ => SampleBatch(operation, iterations)).ToArray();
        ulong expected = 14695981039346656037UL;
        for (int i = 0; i < iterations; i++) expected = unchecked((expected ^ bits) * 1099511628211UL);
        if (samples.Any(s => s.Checksum != expected.ToString("x16")))
            throw new InvalidOperationException("Repeated operation output changed during measurement.");
        return new(name, backend, pairs, fallbacks, value, bits.ToString("x16"),
            samples.Select(s => s.Ticks * (1e9 / Stopwatch.Frequency) / s.Iterations).Order().ElementAt(samples.Length / 2),
            samples.Select(s => (double)s.AllocatedBytes / s.Iterations).Order().ElementAt(samples.Length / 2), samples);
    }

    private static Sample SampleBatch(Func<double> operation, int iterations)
    {
        ulong checksum = 14695981039346656037UL;
        long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
            checksum = unchecked((checksum ^ (ulong)BitConverter.DoubleToInt64Bits(operation())) * 1099511628211UL);
        long ticks = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; // Before allocating the Sample or checksum string.
        sink = checksum;
        return new(iterations, ticks, allocated, checksum.ToString("x16"));
    }
}
