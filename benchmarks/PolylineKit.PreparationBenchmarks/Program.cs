using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.PreparationBenchmarks;

internal static class Program
{
    private static double sink;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly record struct Value(double First, double Second, double Intersection, double Union, double Xor)
    {
        internal double Sum => First + Second + Intersection + Union + Xor;
        internal static Value Of(RegionOverlapResult r) => new(r.FirstArea, r.SecondArea, r.IntersectionArea, r.UnionArea, r.SymmetricDifferenceArea);
    }
    private sealed record Cell(string Input, int Vertices, int Rings, string Scope, string Factory, int RegionsPrepared,
        int Queries, Func<Value> Invoke);
    private sealed record Sample(int Iterations, double NanosecondsPerOperation, double BytesPerOperation);
    private sealed record Row(string Input, int Vertices, int Rings, string Scope, string Factory, int RegionsPrepared,
        int Queries, Value Value, double MedianNanoseconds, double MedianBytes, Sample[] Samples);
    private sealed record SourceIdentity(string Location, string Sha256, string InformationalVersion, string AssemblyVersion);
    private sealed record CheckEvidence(int Assertions, int SyntheticCases, int GisFeatures, int GisPairs,
        int BoundsCandidates, int TimedCellsChecked, double MaximumFactoryAreaDifference, double MaximumFactoryIouDifference);

    internal static int Main(string[] args)
    {
        if (args.Length < 3 || args[0] is not ("check" or "run") || args[0] == "check" && args.Length != 3 ||
            args[0] == "run" && (args.Length < 4 || args.Length > 6))
        {
            Console.Error.WriteLine("Usage: check <repository-root> <output.json> | run <repository-root> <output.json> <run:1..3> [target-ms:12] [samples:5]");
            return 2;
        }
        Corpus corpus = Corpus.Load(Path.GetFullPath(args[1]));
        var identity = Identity();
        Cell[] cells = Cells(corpus).ToArray();
        CheckEvidence check = Check(corpus, cells);
        if (args[0] == "check")
        {
            Save(args[2], new { SchemaVersion = 1, Utc = DateTimeOffset.UtcNow, identity.Library, identity.Harness, Sources = corpus.Sources,
                InputHash = RegionInput.Hash(corpus.Synthetic.Concat(corpus.Counties).Concat(corpus.Districts)), Check = check });
            Console.WriteLine($"PASS: {check.Assertions} factory assertions; {check.GisFeatures} complete Census regions, {check.GisPairs} pairs.");
            return 0;
        }
        int run = int.Parse(args[3], CultureInfo.InvariantCulture);
        double targetMs = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 12;
        int samples = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 5;
        Require(run is >= 1 and <= 3 && targetMs is >= 1 and <= 1000 && samples is >= 3 and <= 21,
            "Run/sample/timing settings outside the recorded protocol.");
        var rows = new List<Row>();
        foreach (Cell cell in cells)
        {
            Row row = Measure(cell, targetMs, samples); rows.Add(row);
            Console.WriteLine($"{cell.Input}/{cell.Scope}/{cell.Factory}: {row.MedianNanoseconds / 1000:F3} us, {row.MedianBytes:F0} B/op");
        }
        Save(args[2], new
        {
            SchemaVersion = 1, Run = run, ProcessId = Environment.ProcessId, Utc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Processors = Environment.ProcessorCount,
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Stopwatch.Frequency,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            TieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            HardwareIntrinsics = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),
            identity.Library, identity.Harness, Sources = corpus.Sources,
            InputHash = RegionInput.Hash(corpus.Synthetic.Concat(corpus.Counties).Concat(corpus.Districts)),
            FillRule = "NonZero", TargetMillisecondsPerSample = targetMs, SamplesPerCell = samples,
            MaximumIterations = 2048, WarmupInvocations = 4,
            Scope = "Factory construction only, or construction plus the same query. Parsing, input orientation, delegates, Sample objects and JSON serialization are outside timing/allocation counters. Managed allocations include prepared snapshots; retained thread-local query workspace is not a per-call allocation.",
            Check = check, Rows = rows, Sink = Volatile.Read(ref sink)
        });
        return 0;
    }

    private static (SourceIdentity Library, SourceIdentity Harness) Identity() => (Identify(typeof(PreparedRegion).Assembly), Identify(typeof(Program).Assembly));
    private static SourceIdentity Identify(Assembly assembly) => new(assembly.Location,
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable",
        assembly.GetName().Version?.ToString() ?? "unavailable");

    private static CheckEvidence Check(Corpus corpus, Cell[] cells)
    {
        int assertions = 0; double maxArea = 0, maxIou = 0;
        void Equal(double actual, double expected, double budget, string input)
        {
            assertions++; double error = Math.Abs(actual - expected);
            Require(double.IsFinite(actual) && double.IsFinite(expected) && error <= budget,
                $"{input}: {actual:R} vs {expected:R}, budget {budget:R}.");
        }
        void EqualResult(RegionOverlapResult actual, RegionOverlapResult expected, double budget, string input)
        {
            Value a = Value.Of(actual), b = Value.Of(expected);
            double[] actualAreas = [a.First, a.Second, a.Intersection, a.Union, a.Xor];
            double[] expectedAreas = [b.First, b.Second, b.Intersection, b.Union, b.Xor];
            for (int i = 0; i < actualAreas.Length; i++)
            {
                maxArea = Math.Max(maxArea, Math.Abs(actualAreas[i] - expectedAreas[i]));
                Equal(actualAreas[i], expectedAreas[i], budget, input + "/area-" + i);
            }
            Require(actual.IntersectionOverUnion.HasValue && expected.IntersectionOverUnion.HasValue, "Nonempty union required.");
            double actualIou = actual.IntersectionOverUnion!.Value, expectedIou = expected.IntersectionOverUnion!.Value;
            double difference = Math.Abs(actualIou - expectedIou);
            maxIou = Math.Max(maxIou, difference);
            Equal(actualIou, expectedIou, 1e-8, input + "/iou");
        }
        Point2[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Point2[] hole = [new(3, 3), new(7, 3), new(7, 7), new(3, 7)];
        foreach (bool reverseShell in new[] { false, true })
        foreach (bool reverseHole in new[] { false, true })
        {
            Point2[] shellCopy = reverseShell ? square.Reverse().ToArray() : square;
            Point2[] holeCopy = reverseHole ? hole.Reverse().ToArray() : hole;
            var region = PreparedRegion.FromPolygon(shellCopy, new IReadOnlyList<Point2>[] { holeCopy });
            Equal(RegionArea.FilledArea(region), 84, 0, "analytic shell-hole orientations");
        }
        foreach (RegionInput input in corpus.Synthetic.Concat(corpus.Counties).Concat(corpus.Districts))
        {
            double budget = input.Id.Contains('-', StringComparison.Ordinal) ? 1e-7 : 1;
            PreparedRegion raw = input.Prepare("FromRings");
            // An identical region is the fixed comparison for the factory-equivalence check.
            RegionOverlapResult reference = RegionArea.Compare(raw, raw);
            foreach (string method in input.Methods)
            {
                PreparedRegion candidate = input.Prepare(method);
                Equal(candidate.VertexCount, raw.VertexCount, 0, input.Id + "/vertices");
                Equal(candidate.RingCount, raw.RingCount, 0, input.Id + "/rings");
                EqualResult(RegionArea.Compare(candidate, raw), reference, budget, input.Id + "/" + method);
            }
        }
        PreparedRegion[] counties = corpus.Counties.Select(r => r.Prepare("FromRings")).ToArray();
        PreparedRegion[] districts = corpus.Districts.Select(r => r.Prepare("FromRings")).ToArray();
        PreparedRegion[] roleCounties = corpus.Counties.Select(Recommended).ToArray();
        PreparedRegion[] roleDistricts = corpus.Districts.Select(Recommended).ToArray();
        int candidates = 0, pairs = 0;
        for (int c = 0; c < counties.Length; c++)
        for (int d = 0; d < districts.Length; d++)
        {
            pairs++;
            Bounds2D a = counties[c].Bounds!.Value, b = districts[d].Bounds!.Value;
            if (!(a.MaxX < b.MinX || b.MaxX < a.MinX || a.MaxY < b.MinY || b.MaxY < a.MinY)) candidates++;
            RegionOverlapResult reference = RegionArea.Compare(counties[c], districts[d]);
            EqualResult(RegionArea.Compare(roleCounties[c], roleDistricts[d]), reference, 1, $"GIS/{c}/{d}");
        }
        Require(pairs == 1400 && candidates == 285, "Frozen pair inventory changed.");
        // Check the exact timed delegates, including fixed translated synthetic queries and GIS sums.
        // Every factory sees the same role-oriented geometry, so these digests must be identical.
        foreach (var group in cells.GroupBy(c => (c.Input, c.Scope)))
        {
            Value reference = group.First().Invoke();
            foreach (Cell cell in group)
            {
                assertions++;
                Require(cell.Invoke() == reference, "Timed operation differs across factories: " + cell.Input + "/" + cell.Scope);
            }
        }
        return new(assertions, corpus.Synthetic.Length, counties.Length + districts.Length, pairs, candidates, cells.Length, maxArea, maxIou);
    }

    private static PreparedRegion Recommended(RegionInput input) => input.Prepare(input.Polygons.Length == 1 ? "FromPolygon" : "FromPolygons");

    private static IEnumerable<Cell> Cells(Corpus corpus)
    {
        foreach (RegionInput input in corpus.Synthetic)
        {
            PreparedRegion query = Recommended(input.Translate(input.Id + "/fixed-query", 3, 5));
            foreach (string method in input.Methods)
            {
                yield return new(input.Id, input.VertexCount, input.Rings.Length, "prepare-only", method, 1, 0, () =>
                {
                    PreparedRegion p = input.Prepare(method); return new(p.VertexCount, p.RingCount, 0, 0, 0);
                });
                yield return new(input.Id, input.VertexCount, input.Rings.Length, "prepare-one+compare-fixed-query", method, 1, 1,
                    () => Value.Of(RegionArea.Compare(input.Prepare(method), query)));
            }
        }
        RegionInput[] all = corpus.Counties.Concat(corpus.Districts).ToArray();
        foreach (string method in new[] { "FromRings", "PolygonFactories", "FromPolygons" })
        {
            PreparedRegion Prepare(RegionInput r) => method == "PolygonFactories" ? Recommended(r) : r.Prepare(method);
            yield return new("Census-all-114", all.Sum(r => r.VertexCount), all.Sum(r => r.Rings.Length), "prepare-only-batch",
                method, all.Length, 0, () =>
                {
                    int vertices = 0, rings = 0;
                    foreach (RegionInput r in all) { PreparedRegion p = Prepare(r); vertices += p.VertexCount; rings += p.RingCount; }
                    return new(vertices, rings, 0, 0, 0);
                });
            foreach (bool reverse in new[] { false, true })
            {
                yield return new(reverse ? "Census-districts-to-counties" : "Census-counties-to-districts",
                    all.Sum(r => r.VertexCount), all.Sum(r => r.Rings.Length), "prepare-both-layers+1400-intersection-queries",
                    method, all.Length, 1400, () =>
                    {
                        PreparedRegion[] a = corpus.Counties.Select(Prepare).ToArray(), b = corpus.Districts.Select(Prepare).ToArray();
                        double sum = 0;
                        foreach (PreparedRegion county in a)
                        foreach (PreparedRegion district in b)
                            sum += reverse ? RegionArea.IntersectionArea(district, county) : RegionArea.IntersectionArea(county, district);
                        return new(a.Length, b.Length, sum, 0, 0);
                    });
            }
        }
    }

    private static Row Measure(Cell cell, double targetMs, int sampleCount)
    {
        for (int i = 0; i < 4; i++) Volatile.Write(ref sink, cell.Invoke().Sum);
        int iterations = 1;
        while (iterations < 2048)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) Volatile.Write(ref sink, cell.Invoke().Sum);
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (elapsedMs >= targetMs) break;
            iterations = Math.Min(2048, iterations * 2);
        }
        Value expected = cell.Invoke();
        var samples = new Sample[sampleCount];
        for (int sample = 0; sample < samples.Length; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            Value actual = default;
            for (int i = 0; i < iterations; i++) actual = cell.Invoke();
            long finish = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            // Checks and Sample construction occur after both counters have been captured.
            Require(actual == expected, "Nondeterministic timed result: " + cell.Input + "/" + cell.Factory);
            Volatile.Write(ref sink, actual.Sum);
            samples[sample] = new(iterations, (finish - start) * (1e9 / Stopwatch.Frequency) / iterations, (double)allocated / iterations);
        }
        return new(cell.Input, cell.Vertices, cell.Rings, cell.Scope, cell.Factory, cell.RegionsPrepared, cell.Queries,
            expected, Median(samples.Select(s => s.NanosecondsPerOperation)), Median(samples.Select(s => s.BytesPerOperation)), samples);
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray(); return sorted[sorted.Length / 2];
    }
    private static void Save(string output, object report)
    {
        string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, Json) + "\n");
    }
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
