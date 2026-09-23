using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>A frozen public-API harness shared by every optimization variant.</summary>
internal static class OptimizationBenchmarks
{
    private const string Protocol = "polylinekit-optimization-v1";
    private static double sink;
    private sealed record Sample(int Iterations, double NanosecondsPerOperation, double BytesPerOperation);
    private sealed record Measurement(string Case, int Vertices, string Family, string Stage,
        bool Primary, int? AlignmentSamples, double MedianNanoseconds, double MedianBytes,
        double Value, Sample[] Samples);
    private sealed record Fixture(int Vertices, Point2[] OpenReference, Point2[] OpenMoving,
        Point2[] OpenPerturbed, Point2[] ClosedReference, Point2[] ClosedMoving,
        Point2[] GraphReference, Point2[] GraphMoving);
    private sealed record Case(string Name, string Family, string Stage, bool Primary,
        int? Samples, Func<double> Invoke);

    public static void Run(string directory, int run, string variant, string coreRevision, string harnessRevision)
    {
        if (run < 1) throw new ArgumentOutOfRangeException(nameof(run));
        Fixture[] fixtures = CreateFixtures();
        string inputHash = SaveInputs(directory, fixtures);
        var results = new List<Measurement>();
        foreach (Fixture fixture in fixtures)
        {
            Case[] cases = CreateCases(fixture);
            // The same order is used by every variant within a run. Rotate between runs.
            int startCase = (run - 1) % cases.Length;
            for (int j = 0; j < cases.Length; j++)
            {
                Case item = cases[(startCase + j) % cases.Length];
                double value = item.Invoke();
                RequireFinite(value, item.Name);
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 60) sink = item.Invoke();
                int iterations = 1;
                while (true)
                {
                    timer.Restart();
                    for (int k = 0; k < iterations; k++) sink = item.Invoke();
                    if (timer.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
                    iterations *= 2;
                }
                var samples = new Sample[9];
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), tick = Stopwatch.GetTimestamp();
                    for (int k = 0; k < iterations; k++) sink = item.Invoke();
                    long elapsed = Stopwatch.GetTimestamp() - tick;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    samples[sample] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations,
                        (double)allocated / iterations);
                }
                // Validate determinism outside timing; also ensures the result is observed.
                double finalValue = item.Invoke();
                RequireFinite(finalValue, item.Name);
                if (BitConverter.DoubleToInt64Bits(value) != BitConverter.DoubleToInt64Bits(finalValue))
                    throw new InvalidOperationException($"Nondeterministic result: {fixture.Vertices}/{item.Name}.");
                results.Add(new(item.Name, fixture.Vertices, item.Family, item.Stage, item.Primary,
                    item.Samples, samples.Select(x => x.NanosecondsPerOperation).Order().ElementAt(4),
                    samples.Select(x => x.BytesPerOperation).Order().ElementAt(4), value, samples));
            }
            Console.WriteLine($"optimization {variant}, run {run}: {fixture.Vertices} vertices complete");
        }
        WriteJson(Path.Combine(directory, $"{variant}-run-{run}.json"), new
        {
            Metadata = Metadata(variant, coreRevision, harnessRevision, inputHash),
            Run = run, Measurements = results
        });
        GC.KeepAlive(sink);
    }

    /// <summary>Calls each case once, recording values and exact inputs without timing.</summary>
    public static void Probe(string directory, string variant, string coreRevision, string harnessRevision)
    {
        Fixture[] fixtures = CreateFixtures();
        string inputHash = SaveInputs(directory, fixtures);
        var values = fixtures.SelectMany(f => CreateCases(f).Select(c =>
        {
            double value = c.Invoke();
            RequireFinite(value, c.Name);
            return new { c.Name, f.Vertices, c.Family, c.Stage, c.Primary, AlignmentSamples = c.Samples, Value = value };
        })).ToArray();
        WriteJson(Path.Combine(directory, $"{variant}-probe.json"), new
        {
            Metadata = Metadata(variant, coreRevision, harnessRevision, inputHash), Values = values
        });
        Console.WriteLine($"optimization {variant}: {values.Length} preflight values recorded");
    }

    /// <summary>Repeats one frozen workload for an external sampling profiler.</summary>
    public static void Profile(string stage, int vertices, int milliseconds)
    {
        if (milliseconds < 1 || milliseconds > 600_000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Fixture fixture = CreateFixtures().SingleOrDefault(f => f.Vertices == vertices)
            ?? throw new ArgumentOutOfRangeException(nameof(vertices));
        Case item = CreateCases(fixture).SingleOrDefault(c => c.Name == stage)
            ?? throw new ArgumentException("Stage must be an exact case name from the frozen protocol.", nameof(stage));
        RequireFinite(item.Invoke(), item.Name);
        Console.WriteLine($"profile start: {stage}, {vertices} vertices, {milliseconds} ms");
        var timer = Stopwatch.StartNew();
        long operations = 0;
        while (timer.ElapsedMilliseconds < milliseconds) { sink = item.Invoke(); operations++; }
        RequireFinite(sink, item.Name);
        Console.WriteLine($"profile end: {operations} operations, observed value {sink:R}");
        GC.KeepAlive(sink);
    }

    private static Case[] CreateCases(Fixture f)
    {
        var closed64 = new AlignmentOptions { Closed = true, SampleCount = 64 };
        var closed256 = new AlignmentOptions { Closed = true, SampleCount = 256 };
        AffineTransform2D affine = AffineTransform2D.Scaling(1.3, .7)
            .Then(AffineTransform2D.Rotation(.23)).Then(AffineTransform2D.Translation(-2, 3));
        return
        [
            new("exact/normalize-align-area", "open-exact", "NormalizeAlignArea", true, 64,
                () => Pipeline(f.OpenReference, f.OpenMoving)),
            new("perturbed/normalize-align-area", "open-perturbed", "NormalizeAlignArea", true, 64,
                () => Pipeline(f.OpenReference, f.OpenPerturbed)),
            new("graph/integral", "graph", "BetweenGraphs", false, null,
                () => PolylineArea.BetweenGraphs(f.GraphReference, f.GraphMoving)),
            new("exact/normalize", "open-exact", "NormalizeOnePath", false, null,
                () => NormalizationValue(PolylineNormalization.ToUnitBounds(f.OpenMoving))),
            new("exact/apply", "open-exact", "AffineApply", false, null,
                () => PointValue(affine.Apply(f.OpenMoving))),
            new("exact/fit64", "open-exact", "OpenFit", false, 64,
                () => PolylineAlignment.FitSimilarity(f.OpenMoving, f.OpenReference).RmsError),
            new("perturbed/fit64", "open-perturbed", "OpenFit", false, 64,
                () => PolylineAlignment.FitSimilarity(f.OpenPerturbed, f.OpenReference).RmsError),
            new("closed/fit64", "closed-asymmetric", "ClosedPhaseFit", false, 64,
                () => PolylineAlignment.FitSimilarity(f.ClosedMoving, f.ClosedReference, closed64).RmsError),
            new("closed/fit256", "closed-asymmetric", "ClosedPhaseFit", false, 256,
                () => PolylineAlignment.FitSimilarity(f.ClosedMoving, f.ClosedReference, closed256).RmsError),
            new("exact/bridge", "open-exact", "EndpointBridgedArea", false, null,
                () => PolylineComparison.EndpointBridgedArea(f.OpenReference, f.OpenMoving).RawArea),
            new("perturbed/bridge", "open-perturbed", "EndpointBridgedArea", false, null,
                () => PolylineComparison.EndpointBridgedArea(f.OpenReference, f.OpenPerturbed).RawArea),
            new("closed/xor", "closed-asymmetric", "FilledRegionDifference", false, null,
                () => PolylineComparison.FilledRegionDifference(f.ClosedReference, f.ClosedMoving).RawArea)
        ];
    }

    private static double Pipeline(Point2[] reference, Point2[] moving)
    {
        var p = PolylineNormalization.ToUnitBounds(reference);
        var q = PolylineNormalization.ToUnitBounds(moving);
        var fit = PolylineAlignment.FitSimilarity(q.Points, p.Points);
        return PolylineComparison.EndpointBridgedArea(p.Points, fit.AlignedPoints).RawArea;
    }

    private static double NormalizationValue(NormalizationResult result) =>
        result.Bounds.Width + result.Bounds.Height + PointValue(result.Points);

    private static double PointValue(IReadOnlyList<Point2> points) =>
        points[0].X + 2 * points[0].Y + 3 * points[points.Count / 2].X +
        5 * points[points.Count / 2].Y + 7 * points[points.Count - 1].X + 11 * points[points.Count - 1].Y;

    private static Fixture[] CreateFixtures() => [.. new[] { 16, 64, 256, 1024 }.Select(n =>
    {
        var reference = new Point2[n]; var perturbedSource = new Point2[n];
        var closed = new Point2[n]; var graph = new Point2[n]; var graphMoving = new Point2[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / (n - 1);
            reference[i] = new Point2(3 * t + .5 * Math.Sin(9 * t), Math.Sin(5 * t) + t);
            perturbedSource[i] = new Point2(reference[i].X + .025 * Math.Sin(17 * t),
                reference[i].Y + .04 * Math.Sin(13 * t) + .015 * t);
            double theta = 2 * Math.PI * i / n;
            double radius = 1 + .2 * Math.Cos(3 * theta) + .08 * Math.Sin(5 * theta);
            closed[i] = new Point2(1.3 * radius * Math.Cos(theta) + .12 * Math.Sin(2 * theta),
                .8 * radius * Math.Sin(theta));
            graph[i] = new Point2(t, Math.Sin(5 * t));
            graphMoving[i] = new Point2(t, Math.Sin(5 * t) + .05 * Math.Sin(17 * t) + .01);
        }
        // Fixture generation deliberately uses scalar arithmetic independent of the tested assembly.
        Point2[] moving = Transform(reference, 2, .37, 7, -4);
        Point2[] perturbed = Transform(perturbedSource, 2, .37, 7, -4);
        Point2[] shiftedClosed = Enumerable.Range(0, n).Select(i => closed[(i + n / 8) % n]).ToArray();
        return new Fixture(n, reference, moving, perturbed, closed,
            Transform(shiftedClosed, 1.7, -.43, 3, -2), graph, graphMoving);
    })];

    private static Point2[] Transform(Point2[] source, double scale, double angle, double tx, double ty)
    {
        // Matches Scaling(scale).Then(Rotation(angle)).Then(Translation(tx,ty)).Apply(source).
        double c = Math.Cos(angle) * scale, s = Math.Sin(angle) * scale;
        return [.. source.Select(p => new Point2(c * p.X + -s * p.Y + tx, s * p.X + c * p.Y + ty))];
    }

    private static object Metadata(string variant, string coreRevision, string harnessRevision, string inputHash)
    {
        Assembly core = typeof(Point2).Assembly;
        return new
        {
            Protocol, Variant = variant, CoreRevision = coreRevision, HarnessRevision = harnessRevision,
            CoreTarget = core.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
            CoreDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core.Location))).ToLowerInvariant(),
            InputsSha256 = inputHash, Utc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), CPU = CpuDescription(),
            Processors = Environment.ProcessorCount, StopwatchFrequency = Stopwatch.Frequency,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            ReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
            EnableHWIntrinsic = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),
            ForceScalar = Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR"),
            Hardware = new { Vector128 = System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated,
                Vector256 = System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated,
                Sse2 = Sse2.IsSupported, Avx = Avx.IsSupported, Avx2 = Avx2.IsSupported,
                Fma = Fma.IsSupported, AdvSimd = AdvSimd.IsSupported },
            WarmupMilliseconds = 60, MinimumCalibrationMilliseconds = 20, Batches = 9,
            Cases = 48, OpenSamples = 64, ClosedSamples = new[] { 64, 256 },
            Area = new { DecimalPrecision = 6, FillRule = "NonZero", IncludeContours = false },
            Correspondence = new { AllowScaling = true, AllowReversal = false, SearchClosedPhase = true }
        };
    }

    private static string CpuDescription()
    {
        string? windows = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(windows)) return windows;
        if (File.Exists("/proc/cpuinfo"))
        {
            string? model = File.ReadLines("/proc/cpuinfo").FirstOrDefault(x => x.StartsWith("model name", StringComparison.Ordinal));
            if (model is not null) return model[(model.IndexOf(':') + 1)..].Trim();
        }
        return "Unavailable (see runtime, architecture and ISA flags)";
    }

    private static string SaveInputs(string directory, Fixture[] fixtures)
    {
        Directory.CreateDirectory(directory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { Protocol, Fixtures = fixtures }, Evidence.JsonOptions);
        string path = Path.Combine(directory, "inputs.json");
        if (File.Exists(path) && !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException("Existing inputs differ from this frozen protocol.");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new InvalidOperationException($"Nonfinite observation in {name}.");
    }

    private static void WriteJson(string path, object value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Evidence.JsonOptions) + "\n");
}
