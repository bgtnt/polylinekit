using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Clipper2-based and boundary-winding area operations on deterministic workload families.</summary>
internal static class WindingBenchmarks
{
    private static double sink;
    private sealed record Sample(int Iterations, double NanosecondsPerOperation, double BytesPerOperation);
    private sealed record Measurement(string Workload, int Vertices, string Method, string InputSha256, double Value,
        double MedianNanoseconds, double MedianBytes, Sample[] Samples);
    private sealed record Workload(string Name, int Vertices, Point2[] First, Point2[] Second, (string Name, Func<double> Invoke)[] Methods);

    public static void Run(string directory, int run, string revision)
    {
        Directory.CreateDirectory(directory);
        List<Measurement> results = [];
        foreach (Workload workload in Workloads())
        {
            string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { workload.First, workload.Second }, Evidence.JsonOptions)));
            var methods = workload.Methods;
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
                    // Read the counter before constructing the record, which would otherwise count its own 40 bytes.
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    samples[sample] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)allocated / iterations);
                }
                results.Add(new(workload.Name, workload.Vertices, method.Name, hash, value,
                    samples.Select(x => x.NanosecondsPerOperation).Order().ElementAt(4),
                    samples.Select(x => x.BytesPerOperation).Order().ElementAt(4), samples));
            }
            Console.WriteLine($"winding run {run}: {workload.Name} {workload.Vertices} complete");
        }
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(new
        {
            Revision = revision, Run = run, Utc = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CPU = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), LogicalProcessors = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), Measurements = results
        }, Evidence.JsonOptions) + "\n");
        File.WriteAllText(Path.Combine(directory, "inputs.json"), JsonSerializer.Serialize(
            Workloads().Select(w => new { w.Name, w.Vertices, w.First, w.Second }), Evidence.JsonOptions) + "\n");
        GC.KeepAlive(sink);
    }

    /// <summary>Median of the three process medians per workload/method, with range, bytes and time ratios.</summary>
    public static void Summarize(string directory)
    {
        var runs = Enumerable.Range(1, 3).Select(r => JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"run-{r}.json"))).RootElement).ToArray();
        if (runs.Select(r => r.GetProperty("Revision").GetString()).Distinct().Count() != 1) throw new InvalidDataException("Source revisions differ.");
        var rows = runs.SelectMany(r => r.GetProperty("Measurements").EnumerateArray()).Select(m => new
        {
            Workload = m.GetProperty("Workload").GetString()!, Vertices = m.GetProperty("Vertices").GetInt32(),
            Method = m.GetProperty("Method").GetString()!, Hash = m.GetProperty("InputSha256").GetString()!,
            Value = m.GetProperty("Value").GetDouble(), Ns = m.GetProperty("MedianNanoseconds").GetDouble(),
            Bytes = m.GetProperty("MedianBytes").GetDouble()
        }).ToArray();
        var summary = rows.GroupBy(r => (r.Workload, r.Vertices, r.Method)).Select(g =>
        {
            if (g.Count() != 3 || g.Select(x => x.Hash).Distinct().Count() != 1) throw new InvalidDataException("Incomplete or inconsistent measurement group.");
            double[] us = g.Select(x => x.Ns / 1000).Order().ToArray();
            return new { g.Key.Workload, g.Key.Vertices, g.Key.Method, MedianUs = us[1], MinUs = us[0], MaxUs = us[2],
                Bytes = g.Select(x => x.Bytes).Order().ElementAt(1), Values = g.Select(x => x.Value).Distinct().ToArray() };
        }).OrderBy(x => x.Workload, StringComparer.Ordinal).ThenBy(x => x.Vertices).ThenBy(x => x.Method, StringComparer.Ordinal).ToArray();
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, Evidence.JsonOptions) + "\n");

        var culture = CultureInfo.InvariantCulture;
        var md = new StringBuilder();
        md.AppendLine("# Winding-area benchmark summary").AppendLine();
        md.AppendLine("Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.").AppendLine();
        md.AppendLine($"Measured source: `{runs[0].GetProperty("Revision").GetString()}`; runtime {runs[0].GetProperty("Runtime").GetString()}; {runs[0].GetProperty("OS").GetString()}; {runs[0].GetProperty("CPU").GetString()}.").AppendLine();
        md.AppendLine("| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |");
        md.AppendLine("|:---|---:|:---|---:|---:|---:|");
        foreach (var group in summary.GroupBy(s => (s.Workload, s.Vertices)))
        {
            var winding = group.Single(s => s.Method.StartsWith("Winding", StringComparison.Ordinal));
            foreach (var s in group)
            {
                string ratio = s.Method.StartsWith("Clipper", StringComparison.Ordinal) ? (s.MedianUs / winding.MedianUs).ToString("F2", culture) : "";
                md.AppendLine(string.Create(culture, $"| {s.Workload} | {s.Vertices} | {s.Method} | {s.MedianUs:F2} ({s.MinUs:F2}–{s.MaxUs:F2}) | {s.Bytes:N0} | {ratio} |"));
            }
        }
        File.WriteAllText(Path.Combine(directory, "summary.md"), md.ToString());
    }

    private static IEnumerable<Workload> Workloads()
    {
        foreach (int n in new[] { 64, 256, 1024 })
        {
            // Two similar open strokes that cross repeatedly: the recognition-style bridged comparison.
            Point2[] a = Enumerable.Range(0, n).Select(i => { double t = (double)i / (n - 1); return new Point2(3 * t + .5 * Math.Sin(9 * t), Math.Sin(5 * t) + t); }).ToArray();
            Point2[] b = Enumerable.Range(0, n).Select(i => { double t = (double)i / (n - 1); return new Point2(3 * t + .5 * Math.Sin(9 * t) + .05 * Math.Sin(31 * t), Math.Sin(5 * t) + t + .08 * Math.Cos(23 * t)); }).ToArray();
            yield return Bridged("similar-strokes", n, a, b);

            var graph = Fixtures.Benchmark(n, "dense");
            yield return new Workload("dense-graph", n, graph.P, graph.Q,
            [
                ("GraphIntegral", () => PolylineArea.BetweenGraphs(graph.P, graph.Q)),
                ("ClipperEndpointBridged", () => PolylineComparison.EndpointBridgedArea(graph.P, graph.Q).RawArea),
                ("WindingEndpointBridged", () => WindingArea.EndpointBridged(graph.P, graph.Q).NonZero)
            ]);

            Random random = new(n);
            Point2[] Walk() { double x = 0, y = 0; return Enumerable.Range(0, n).Select(_ => new Point2(x += random.NextDouble() * 2 - 1, y += random.NextDouble() * 2 - 1)).ToArray(); }
            yield return Bridged("random-walks", n, Walk(), Walk());

            Point2[] Star(double phase, double dx) => Enumerable.Range(0, n).Select(i =>
            {
                double angle = 2 * Math.PI * i / n, r = 1 + .3 * Math.Sin(5 * angle + phase);
                return new Point2(r * Math.Cos(angle) + dx, r * Math.Sin(angle));
            }).ToArray();
            Point2[] s1 = Star(0, 0), s2 = Star(.7, .25);
            yield return new Workload("filled-regions", n, s1, s2,
            [
                ("ClipperFilledRegionOverlap", () => PolylineComparison.FilledRegionOverlap(s1, s2).SymmetricDifferenceArea),
                ("WindingFilledRegions", () => WindingArea.FilledRegions(s1, s2).SymmetricDifferenceArea)
            ]);
        }
        foreach (int n in new[] { 64, 256 })
        {
            // Integer grid points: shared vertices, T-junctions and collinear overlap exercise exact and symbolic predicates.
            Random random = new(1000 + n);
            Point2[] Grid() => Enumerable.Range(0, n).Select(_ => new Point2(random.Next(0, 8), random.Next(0, 8))).ToArray();
            yield return Bridged("degenerate-grid", n, Grid(), Grid());
        }

        static Workload Bridged(string name, int n, Point2[] p, Point2[] q) => new(name, n, p, q,
        [
            ("ClipperEndpointBridged", () => PolylineComparison.EndpointBridgedArea(p, q).RawArea),
            ("WindingEndpointBridged", () => WindingArea.EndpointBridged(p, q).NonZero)
        ]);
    }
}
