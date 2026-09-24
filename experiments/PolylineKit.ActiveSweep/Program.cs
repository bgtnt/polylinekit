using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PolylineKit.ActiveSweep;

internal static class Program
{
    private static double sink;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "check") return Checks.Run();
        if (args.Length != 4 || args[0] != "bench")
        {
            Console.Error.WriteLine("ActiveSweep check | bench <inputs.json> <output.json> <run-number>");
            return 2;
        }
        int run = int.Parse(args[3]);
        var rows = new List<object>();
        foreach (var fixture in Fixtures.Performance(args[1]))
        {
            bool accepted = SimpleSweep.TryArea(fixture.Path, out double direct, out var statistics);
            double expected = WindingArea.ClosedPath(fixture.Path).NonZero;
            if (accepted && Math.Abs(direct - expected) > Math.Max(Math.Abs(expected), 1) * 1e-11)
                throw new InvalidOperationException($"Benchmark mismatch: {fixture.Name} {fixture.Path.Length}: {direct:R} != {expected:R}");
            var methods = new Func<double>[] { () => WindingArea.ClosedPath(fixture.Path).NonZero,
                () => Hybrid(fixture.Path), () => Guarded(fixture.Path) };
            var measured = new Sample[3];
            for (int i = 0; i < 3; i++)
            {
                int method = (i + run - 1) % 3;
                measured[method] = Measure(methods[method]);
            }
            Sample baseline = measured[0], hybrid = measured[1], guarded = measured[2];
            rows.Add(new { fixture.Name, Vertices = fixture.Path.Length, InputSha256 = fixture.Hash(),
                Accepted = accepted, Value = expected,
                Statistics = new { statistics.Vertices, statistics.Comparisons, statistics.NeighborChecks,
                    statistics.ExactPredicates, statistics.PeakActive, statistics.SignedArea },
                Selected = SweepPolicy.ShouldTry(fixture.Path), Baseline = baseline, Hybrid = hybrid, Guarded = guarded });
            Console.WriteLine($"{fixture.Name}/{fixture.Path.Length} accepted={accepted} {baseline.MedianUs:F3} -> {hybrid.MedianUs:F3} / guarded {guarded.MedianUs:F3} us");
        }
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), LogicalProcessors = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            HardwareIntrinsics = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),
            Run = run, Utc = DateTimeOffset.UtcNow, Measurements = rows
        }, Json));
        GC.KeepAlive(sink);
        return 0;
    }

    internal static double Hybrid(IReadOnlyList<Point2> path) =>
        SimpleSweep.TryArea(path, out double area, out _) ? area : WindingArea.ClosedPath(path).NonZero;

    internal static double Guarded(IReadOnlyList<Point2> path) =>
        SweepPolicy.ShouldTry(path) ? Hybrid(path) : WindingArea.ClosedPath(path).NonZero;

    private static Sample Measure(Func<double> invoke)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 40) sink = invoke();
        int iterations = 1;
        while (true)
        {
            timer.Restart();
            for (int i = 0; i < iterations; i++) sink = invoke();
            if (timer.ElapsedMilliseconds >= 30 || iterations >= 1_048_576) break;
            iterations *= 2;
        }
        var us = new double[5];
        var bytes = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = invoke();
            long elapsed = Stopwatch.GetTimestamp() - start, endAllocated = GC.GetAllocatedBytesForCurrentThread();
            us[sample] = elapsed * 1e6 / Stopwatch.Frequency / iterations;
            bytes[sample] = (double)(endAllocated - allocated) / iterations;
        }
        return new(iterations, us.Order().ElementAt(2), bytes.Order().ElementAt(2), us, bytes);
    }

    private sealed record Sample(int Iterations, double MedianUs, double MedianBytes, double[] SamplesUs, double[] SamplesBytes);
}
