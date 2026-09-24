using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

internal static class Benchmarks
{
    private static double sink;
    internal sealed record Sample(int Index, int Iterations, double NanosecondsPerOperation, double BytesPerOperation);
    internal sealed record Measurement(string Fixture, int VerticesPerPath, string Density, int Lobes, string InputSha256,
        string Method, double Score, double MedianNanoseconds, double MedianBytes, List<Sample> Samples);

    public static void Run(string directory, int run, string revision)
    {
        Directory.CreateDirectory(directory);
        List<Measurement> measurements = [];
        foreach (int n in new[] { 16, 64, 256, 1024 })
        foreach (string density in new[] { "none", "sparse", "dense" })
        {
            var f = Fixtures.Benchmark(n, density);
            List<Lobe> lobes = []; LipGraphs.Measure(f.P, f.Q, lobes);
            string inputHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(f, BenchmarkJson.Options)));
            PathsD prepared = new() { new PathD(f.P.Concat(f.Q.Reverse()).Select(p => new PointD(p.X, p.Y))) };
            PathsD transposed = new() { new PathD(f.P.Concat(f.Q.Reverse()).Select(p => new PointD(p.Y, p.X))) };
            var methods = new (string Name, Func<double> Invoke)[]
            {
                ("UnsignedGraphArea", () => PolylineArea.BetweenGraphs(f.P, f.Q)),
                ("LipGraphSweep", () => LipGraphs.Measure(f.P, f.Q)),
                ("GenLipP0", () => GenLip.Measure(f.P, f.Q).Score),
                ("ClipperFull", () => ClipperOracle.Between(f.P, f.Q)),
                ("ClipperPrepared", () => Math.Abs(Clipper.Area(Clipper.Union(prepared, new PathsD(), FillRule.NonZero, ClipperOracle.Precision)))),
                ("ClipperPreparedTransposed", () => Math.Abs(Clipper.Area(Clipper.Union(transposed, new PathsD(), FillRule.NonZero, ClipperOracle.Precision))))
            };
            // Rotate method order by run to reduce consistent order effects.
            foreach (var method in methods.Skip((run - 1) % methods.Length).Concat(methods.Take((run - 1) % methods.Length)))
            {
                double score = method.Invoke();
                var warm = Stopwatch.StartNew();
                while (warm.ElapsedMilliseconds < 60) sink = method.Invoke();
                int iterations = 1;
                while (true)
                {
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < iterations; i++) sink = method.Invoke();
                    if (watch.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
                    iterations *= 2;
                }
                List<Sample> samples = [];
                for (int sample = 0; sample < 9; sample++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                    long beforeTime = Stopwatch.GetTimestamp();
                    for (int i = 0; i < iterations; i++) sink = method.Invoke();
                    long ticks = Stopwatch.GetTimestamp() - beforeTime;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                    samples.Add(new(sample, iterations, ticks * 1e9 / Stopwatch.Frequency / iterations, (double)bytes / iterations));
                }
                measurements.Add(new(f.Name, n, density, lobes.Count(l => l.Area > 0), inputHash, method.Name, score,
                    samples.Select(s => s.NanosecondsPerOperation).Order().ElementAt(4),
                    samples.Select(s => s.BytesPerOperation).Order().ElementAt(4), samples));
            }
            Console.WriteLine($"run {run}: {f.Name} complete");
        }
        var report = new
        {
            Run = run, Revision = revision, Utc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount,
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Stopwatch.Frequency, Measurements = measurements
        };
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(report, BenchmarkJson.Options) + "\n");
        StringBuilder csv = new("run,vertices,density,lobes,method,score,median_ns,median_bytes,input_sha256\n");
        foreach (var m in measurements)
            csv.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{run},{m.VerticesPerPath},{m.Density},{m.Lobes},{m.Method},{m.Score:R},{m.MedianNanoseconds:R},{m.MedianBytes:R},{m.InputSha256}"));
        File.WriteAllText(Path.Combine(directory, $"run-{run}.csv"), csv.ToString());
        GC.KeepAlive(sink);
    }
}
