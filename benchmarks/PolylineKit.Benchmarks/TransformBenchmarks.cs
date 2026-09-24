using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.Experiments;

internal static class TransformBenchmarks
{
    private static double sink;
    private sealed record Sample(int Iterations, double NanosecondsPerOperation, double BytesPerOperation);
    private sealed record Measurement(int Vertices, string Stage, double MedianNanoseconds, double MedianBytes, double Value, Sample[] Samples);

    public static void Run(string directory, int run, string revision)
    {
        Directory.CreateDirectory(directory);
        List<Measurement> results = [];
        List<object> inputs = [];
        foreach (int n in new[] { 16, 64, 256, 1024 })
        {
            Point2[] reference = Enumerable.Range(0, n).Select(i =>
            {
                double t = (double)i / (n - 1);
                return new Point2(3 * t + .5 * Math.Sin(9 * t), Math.Sin(5 * t) + t);
            }).ToArray();
            Point2[] moving = AffineTransform2D.Scaling(2).Then(AffineTransform2D.Rotation(.37))
                .Then(AffineTransform2D.Translation(7, -4)).Apply(reference);
            inputs.Add(new { Vertices = n, Reference = reference, Moving = moving });
            var methods = new (string Name, Func<double> Invoke)[]
            {
                ("EndpointBridgedArea", () => PolylineComparison.EndpointBridgedArea(reference, moving).RawArea),
                ("NormalizeOnePath", () => PolylineNormalization.ToUnitBounds(moving).Bounds.Width),
                ("NormalizeAndCompare", () => PolylineComparison.CompareNormalized(reference, moving, AreaComparisonKind.EndpointBridged).Comparison.RawArea),
                ("SimilarityFit64", () => PolylineAlignment.FitSimilarity(moving, reference).RmsError),
                ("NormalizeAlignCompare", Pipeline)
            };
            foreach (var method in methods.Skip((run - 1) % methods.Length).Concat(methods.Take((run - 1) % methods.Length)))
            {
                double value = method.Invoke();
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 60) sink = method.Invoke();
                int iterations = 1;
                while (true)
                {
                    timer.Restart();
                    for (int k = 0; k < iterations; k++) sink = method.Invoke();
                    if (timer.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
                    iterations *= 2;
                }
                var samples = new Sample[9];
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    for (int k = 0; k < iterations; k++) sink = method.Invoke();
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    samples[sample] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations,
                        (double)(GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations);
                }
                results.Add(new(n, method.Name, samples.Select(x => x.NanosecondsPerOperation).Order().ElementAt(4),
                    samples.Select(x => x.BytesPerOperation).Order().ElementAt(4), value, samples));
            }
            Console.WriteLine($"transform run {run}: {n} vertices complete");

            double Pipeline()
            {
                var p = PolylineNormalization.ToUnitBounds(reference);
                var q = PolylineNormalization.ToUnitBounds(moving);
                var fit = PolylineAlignment.FitSimilarity(q.Points, p.Points);
                return PolylineComparison.EndpointBridgedArea(p.Points, fit.AlignedPoints).RawArea;
            }
        }
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(new
        {
            Revision = revision, Run = run, Utc = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, CPU = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), SampleCount = 64,
            Measurements = results
        }, BenchmarkJson.Options) + "\n");
        File.WriteAllText(Path.Combine(directory, "inputs.json"), JsonSerializer.Serialize(inputs, BenchmarkJson.Options) + "\n");
        GC.KeepAlive(sink);
    }
}
