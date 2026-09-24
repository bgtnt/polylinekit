using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

return AreaChange.Run(args);

internal static class AreaChange
{
    private const double GridScale = 1e8;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static double sink;
    private sealed record SourceFile(string File, string Sha256, int Vertices);
    private sealed record Manifest(string Source, string SourceSha256, string License, SourceFile[] Contours);
    private sealed record ShapeFile(string Code, string Name, double[][] Points);
    private sealed record Shape(string Code, string Name, Point2[] Points);
    private sealed record Pair(Shape Shape, double Tolerance, Point2[] Simplified)
    {
        internal string Key => $"{Shape.Code}-{Tolerance.ToString(CultureInfo.InvariantCulture)}";
    }
    private readonly record struct Areas(double Xor, double Union)
    {
        public double? Jaccard => Union > 0 ? Xor / Union : null;
    }
    private sealed record Accuracy(string Key, string Name, double Tolerance, int OriginalVertices, int SimplifiedVertices,
        int RemovedVertices, Areas Winding, Areas Clipper8, Areas Clipper7, double XorDelta, double UnionDelta);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Measurement(string Key, string Method, string Scope, Areas Value, double MedianNanoseconds,
        double MedianBytes, Sample[] Samples);
    private sealed record BenchmarkRun(int Run, string Revision, DateTimeOffset Utc, string Runtime, string OS, string Architecture,
        string? CPU, string? TieredCompilation, string DataSha256, string PairSha256, string WindingSha256,
        string ClipperSha256, string HarnessSha256, Measurement[] Measurements);

    internal static int Run(string[] args)
    {
        if (args.Length == 0) args = ["check"];
        switch (args[0])
        {
            case "check" when args.Length == 1:
                var pairs = LoadPairs(); CheckControls(); var rows = CheckPairs(pairs);
                Console.WriteLine($"AreaChange: analytic controls and {rows.Length} real simplification pairs passed; no timing.");
                return 0;
            case "run" when args.Length == 2:
                WriteExample(args[1]); return 0;
            case "benchmark" when args.Length == 4:
                Benchmark(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3]); return 0;
            case "summarize" when args.Length == 2:
                Summarize(args[1]); return 0;
            default:
                Console.Error.WriteLine("Commands: check | run <output> | benchmark <output> <run:1..3> <revision> | summarize <output>");
                return 2;
        }
    }

    private static Pair[] LoadPairs()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "data");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")))!;
        var pairs = new List<Pair>();
        foreach (var entry in manifest.Contours)
        {
            byte[] raw = File.ReadAllBytes(Path.Combine(directory, entry.File));
            if (Hash(raw) != entry.Sha256) throw new InvalidDataException($"Frozen contour hash differs: {entry.File}");
            var data = JsonSerializer.Deserialize<ShapeFile>(raw)!;
            var points = data.Points.Select(p => new Point2(p[0], p[1])).ToArray();
            if (points.Length != entry.Vertices || points.Length < 3 ||
                (points[0].X == points[^1].X && points[0].Y == points[^1].Y))
                throw new InvalidDataException($"Unexpected contour shape: {entry.File}");
            var shape = new Shape(data.Code, data.Name, points);
            foreach (double tolerance in new[] { 1.0, 4.0, 12.0 })
                pairs.Add(new(shape, tolerance, Simplify(points, tolerance)));
        }
        return pairs.ToArray();
    }

    // The example uses the pinned Clipper2 simplifier; there is no new simplification algorithm here.
    private static Point2[] Simplify(Point2[] input, double tolerance) =>
        Clipper.SimplifyPath(ToPath(input), tolerance, isClosedPath: true)
            .Select(p => new Point2(p.x, p.y)).ToArray();

    private static PathD ToPath(Point2[] points)
    {
        var path = new PathD(points.Length);
        foreach (var p in points) path.Add(new PointD(p.X, p.Y));
        return path;
    }

    private static Areas Winding(Point2[] first, Point2[] second, PathFillRule rule = PathFillRule.NonZero)
    {
        var result = WindingArea.FilledRegions(first, second, rule);
        return new(result.SymmetricDifferenceArea, result.UnionArea);
    }

    // The consumer requests XOR and union, not output contours. Both clipping operations are charged,
    // including input conversion and quantization, but output vertices stay integer for area summation.
    private static Areas Clipping(Point2[] first, Point2[] second, double scale = GridScale, FillRule rule = FillRule.NonZero)
    {
        var clipper = new Clipper64();
        clipper.AddSubject(new Paths64 { Clipper.ScalePath64(ToPath(first), scale) });
        clipper.AddClip(new Paths64 { Clipper.ScalePath64(ToPath(second), scale) });
        var solution = new Paths64();
        if (!clipper.Execute(ClipType.Xor, rule, solution)) throw new InvalidOperationException("Clipper XOR failed.");
        double xor = Math.Abs(Clipper.Area(solution)) / (scale * scale);
        if (!clipper.Execute(ClipType.Union, rule, solution)) throw new InvalidOperationException("Clipper union failed.");
        return new(xor, Math.Abs(Clipper.Area(solution)) / (scale * scale));
    }

    private static Areas Consume(Pair pair, bool clipping)
    {
        Point2[] simplified = Simplify(pair.Shape.Points, pair.Tolerance);
        return clipping ? Clipping(pair.Shape.Points, simplified) : Winding(pair.Shape.Points, simplified);
    }

    private static void CheckControls()
    {
        Point2[] square = [new(0,0), new(2,0), new(2,2), new(0,2)];
        Point2[] shifted = [new(1,0), new(3,0), new(3,2), new(1,2)];
        Point2[] cut = [new(0,0), new(2,0), new(2,1), new(1,2), new(0,2)];
        Point2[] inner = [new(.5,.5), new(1.5,.5), new(1.5,1.5), new(.5,1.5)];
        foreach (bool evenOdd in new[] { false, true })
        foreach (var (second, expected) in new[] { (square, new Areas(0,4)), (shifted, new Areas(4,6)),
            (cut, new Areas(.5,4)), (inner, new Areas(3,4)) })
        {
            var windingRule = evenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero;
            var clipperRule = evenOdd ? FillRule.EvenOdd : FillRule.NonZero;
            Require(Winding(square, second, windingRule) == expected, "Analytic Winding areas differ.");
            Require(Clipping(square, second, rule: clipperRule) == expected, "Analytic Clipper areas differ.");
        }
        Point2[] subdivided = [new(0,0), new(1,0), new(2,0), new(2,1), new(2,2), new(1,2), new(0,2), new(0,1)];
        Point2[] simplified = Simplify(subdivided, .01);
        Require(simplified.Length == 4 && Winding(subdivided, simplified) == new Areas(0,4),
            "Collinear simplification control differs.");
    }

    private static Accuracy[] CheckPairs(Pair[] pairs)
    {
        var rows = new List<Accuracy>();
        foreach (var pair in pairs)
        {
            Require(pair.Simplified.Length >= 3 && pair.Simplified.Length <= pair.Shape.Points.Length, "Invalid simplified contour.");
            Areas winding = Winding(pair.Shape.Points, pair.Simplified);
            Areas clip8 = Clipping(pair.Shape.Points, pair.Simplified);
            Areas clip7 = Clipping(pair.Shape.Points, pair.Simplified, 1e7);
            Require(winding.Xor >= 0 && winding.Union > 0 && winding.Xor <= winding.Union, "Invalid winding area.");
            // This is a declared comparison acceptance threshold, not a proved error bound.
            // p7 is a sensitivity check; neither grid nor Clipper is treated as an exact oracle.
            double tolerance = Math.Max(1e-5, 1e-7 * winding.Xor);
            Require(Math.Abs(winding.Xor - clip8.Xor) <= tolerance &&
                Math.Abs(winding.Union - clip8.Union) <= tolerance &&
                Math.Abs(clip7.Xor - clip8.Xor) <= 10 * tolerance &&
                Math.Abs(clip7.Union - clip8.Union) <= 10 * tolerance,
                $"Consequential clipping disagreement: {pair.Key}; investigate before publishing this example.");
            Require(Consume(pair, false) == winding && Consume(pair, true) == clip8,
                "The complete consumer must use the same simplified geometry as the isolated scorer.");
            rows.Add(new(pair.Key, pair.Shape.Name, pair.Tolerance, pair.Shape.Points.Length,
                pair.Simplified.Length, pair.Shape.Points.Length - pair.Simplified.Length,
                winding, clip8, clip7, Math.Abs(winding.Xor - clip8.Xor), Math.Abs(winding.Union - clip8.Union)));
        }
        return rows.ToArray();
    }

    private static void WriteExample(string directory)
    {
        var pairs = LoadPairs(); CheckControls(); Accuracy[] rows = CheckPairs(pairs);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "accuracy.json"), JsonSerializer.Serialize(rows, Json) + "\n");
        File.WriteAllText(Path.Combine(directory, "pairs.json"), PairJson(pairs));
        string overlays = Path.Combine(directory, "overlays"); Directory.CreateDirectory(overlays);
        foreach (var (pair, row) in pairs.Zip(rows))
            File.WriteAllText(Path.Combine(overlays, pair.Key + ".svg"), Overlay(pair, row));
        Console.WriteLine($"Wrote {rows.Length} checked pairs and overlays to {Path.GetFullPath(directory)}.");
    }

    private static string PairJson(Pair[] pairs) => JsonSerializer.Serialize(pairs.Select(p => new
    {
        p.Key, p.Shape.Name, p.Tolerance,
        Original = p.Shape.Points.Select(v => new[] { v.X, v.Y }),
        Simplified = p.Simplified.Select(v => new[] { v.X, v.Y })
    }), Json) + "\n";

    private static string Overlay(Pair pair, Accuracy row)
    {
        string PathText(Point2[] points) => string.Join(" ", points.Select((p, i) =>
            FormattableString.Invariant($"{(i == 0 ? "M" : "L")}{p.X:R},{p.Y:R}"))) + " Z";
        return FormattableString.Invariant($"""
            <svg xmlns="http://www.w3.org/2000/svg" width="820" height="900" viewBox="0 0 820 900">
            <rect width="820" height="900" fill="#fafafa"/>
            <g font-family="system-ui,sans-serif" fill="#152238">
            <text x="40" y="42" font-size="24">{WebUtility.HtmlEncode(pair.Shape.Name)} · tolerance {pair.Tolerance}</text>
            <text x="40" y="75" font-size="16">{row.OriginalVertices} → {row.SimplifiedVertices} vertices; removed {row.RemovedVertices}</text>
            <text x="40" y="103" font-size="16">Changed area {row.Winding.Xor:G8}; Jaccard change {row.Winding.Jaccard:P4}</text>
            <text x="40" y="131" font-size="14">Original: blue · simplified: orange · local normalized map plane</text>
            </g>
            <g transform="translate(75 850) scale(.65 -.65)" fill-rule="nonzero" stroke-width="2.5" stroke-linejoin="round">
            <path d="{PathText(pair.Shape.Points)}" fill="#2666ad" fill-opacity=".12" stroke="#2666ad"/>
            <path d="{PathText(pair.Simplified)}" fill="#ef7d25" fill-opacity=".12" stroke="#db6d16"/>
            </g>
            </svg>
            """) + "\n";
    }

    private static void Benchmark(string directory, int run, string revision)
    {
        if (run is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(run));
        Pair[] pairs = LoadPairs(); CheckControls(); CheckPairs(pairs);
        var rows = new List<Measurement>();
        foreach (var pair in pairs)
        {
            (string Name, string Scope, Func<Areas> Invoke)[] methods =
            [
                ("winding", "isolated-area", () => Winding(pair.Shape.Points, pair.Simplified)),
                ("clipper64-p8", "isolated-area", () => Clipping(pair.Shape.Points, pair.Simplified)),
                ("winding", "complete-consumer", () => Consume(pair, false)),
                ("clipper64-p8", "complete-consumer", () => Consume(pair, true))
            ];
            // Rotate order between independent processes, as in the repository benchmark runner.
            foreach (var method in methods.Skip(run - 1).Concat(methods.Take(run - 1)))
            {
                Areas value = method.Invoke(); var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 40) Keep(method.Invoke());
                int iterations = 1;
                while (true)
                {
                    watch.Restart();
                    for (int i = 0; i < iterations; i++) Keep(method.Invoke());
                    if (watch.ElapsedMilliseconds >= 15 || iterations >= 131072) break;
                    iterations *= 2;
                }
                var samples = new Sample[5];
                for (int batch = 0; batch < samples.Length; batch++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < iterations; i++) Keep(method.Invoke());
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)bytes / iterations);
                }
                Require(value == method.Invoke(), "Unstable measured value.");
                rows.Add(new(pair.Key, method.Name, method.Scope, value, samples.Select(s => s.Nanoseconds).Order().ElementAt(2),
                    samples.Select(s => s.Bytes).Order().ElementAt(2), samples));
            }
            Console.WriteLine($"AreaChange run {run}: {pair.Key}");
        }
        Directory.CreateDirectory(directory);
        var result = new BenchmarkRun(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "manifest.json"))),
            Hash(Encoding.UTF8.GetBytes(PairJson(pairs))), Hash(File.ReadAllBytes(typeof(WindingArea).Assembly.Location)),
            Hash(File.ReadAllBytes(typeof(Clipper).Assembly.Location)), Hash(File.ReadAllBytes(typeof(AreaChange).Assembly.Location)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(result, Json) + "\n");
        GC.KeepAlive(sink);
    }

    private static void Summarize(string directory)
    {
        var runs = Enumerable.Range(1, 3).Select(i => JsonSerializer.Deserialize<BenchmarkRun>(
            File.ReadAllText(Path.Combine(directory, $"run-{i}.json")))!).ToArray();
        var maps = runs.Select(r => r.Measurements.ToDictionary(m => (m.Key, m.Method, m.Scope))).ToArray();
        string Identity(BenchmarkRun r) => JsonSerializer.Serialize(new { r.Revision, r.Runtime, r.OS, r.Architecture,
            r.CPU, r.TieredCompilation, r.DataSha256, r.PairSha256, r.WindingSha256, r.ClipperSha256, r.HarnessSha256 });
        for (int i = 0; i < runs.Length; i++)
            Require(runs[i].Run == i + 1 && Identity(runs[i]) == Identity(runs[0]) && maps[i].Count == 48 &&
                maps[i].Keys.Order().SequenceEqual(maps[0].Keys.Order()), "Incomplete or mismatched independent runs.");
        Pair[] currentPairs = LoadPairs();
        Require(Hash(Encoding.UTF8.GetBytes(PairJson(currentPairs))) == runs[0].PairSha256 &&
            Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "manifest.json"))) == runs[0].DataSha256 &&
            Hash(File.ReadAllBytes(typeof(WindingArea).Assembly.Location)) == runs[0].WindingSha256 &&
            Hash(File.ReadAllBytes(typeof(Clipper).Assembly.Location)) == runs[0].ClipperSha256 &&
            Hash(File.ReadAllBytes(typeof(AreaChange).Assembly.Location)) == runs[0].HarnessSha256,
            "Run the summarizer with the original measured assemblies and frozen inputs.");
        Accuracy[] accuracy = CheckPairs(currentPairs);
        foreach (var row in accuracy)
        foreach (var map in maps)
        foreach (string scope in new[] { "isolated-area", "complete-consumer" })
            Require(map[(row.Key, "winding", scope)].Value == row.Winding &&
                map[(row.Key, "clipper64-p8", scope)].Value == row.Clipper8,
                "Measured values differ from the current verified pair; existing evidence was not overwritten.");
        WriteExample(directory);
        var md = new StringBuilder("# Filled-area change after contour simplification\n\n");
        md.AppendLine($"Source revision: `{runs[0].Revision}`. Measured {runs[0].Utc:yyyy-MM-dd} UTC. {runs[0].Runtime}; {runs[0].OS}; {runs[0].CPU}. Three independent processes, five batches each; median of process medians, with min–max process medians. No statistical significance claim.\n");
        md.AppendLine("Four public-domain Natural Earth contours; each source feature has one ring. Longest map-plane bound is 1000. Clipper2 2.0.0 SimplifyPath uses tolerances 1, 4 and 12. The area unit is the squared normalized map-plane unit, not square kilometres.\n");
        md.AppendLine("| Shape | Tolerance | Vertices before → after | Removed | XOR area | Jaccard change | p8 XOR absolute delta | Overlay |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var row in accuracy)
            md.AppendLine(FormattableString.Invariant($"| {row.Name} | {row.Tolerance} | {row.OriginalVertices} → {row.SimplifiedVertices} | {row.RemovedVertices} | {row.Winding.Xor:G9} | {row.Winding.Jaccard:P4} | {row.XorDelta:G4} | [view](overlays/{row.Key}.svg) |"));
        md.AppendLine("\nBoth isolated scorers start from the same two double arrays and request XOR plus union for Jaccard. Direct Clipper64 includes conversion to a 10^-8 grid, two Boolean operations, and area summation without converting output vertices back to doubles. Winding returns its complete overlap result. p7 and p8 are compared in accuracy.json; neither clipping result is claimed to be exact.\n");
        foreach (string scope in new[] { "isolated-area", "complete-consumer" })
        {
            md.AppendLine($"## {scope}\n");
            md.AppendLine("| Pair | Winding µs (min–max) | Clipper64 p8 µs (min–max) | Clipper / Winding | Winding B/op | Clipper B/op |");
            md.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (var row in accuracy)
            {
                var a = maps.Select(m => m[(row.Key, "winding", scope)]).ToArray();
                var b = maps.Select(m => m[(row.Key, "clipper64-p8", scope)]).ToArray();
                foreach (var value in a) Require(value.Value == a[0].Value, "Winding numeric result changed across runs.");
                foreach (var value in b) Require(value.Value == b[0].Value, "Clipper numeric result changed across runs.");
                double ma = a.Select(v => v.MedianNanoseconds).Order().ElementAt(1);
                double mb = b.Select(v => v.MedianNanoseconds).Order().ElementAt(1);
                string Time(Measurement[] values) => FormattableString.Invariant($"{values.Select(v => v.MedianNanoseconds).Order().ElementAt(1)/1000:F2} ({values.Min(v => v.MedianNanoseconds)/1000:F2}–{values.Max(v => v.MedianNanoseconds)/1000:F2})");
                md.AppendLine(FormattableString.Invariant($"| {row.Key} | {Time(a)} | {Time(b)} | {mb/ma:F2} | {a.Select(v => v.MedianBytes).Order().ElementAt(1):F0} | {b.Select(v => v.MedianBytes).Order().ElementAt(1):F0} |"));
            }
        }
        md.AppendLine("\nThe complete consumer includes conversion for SimplifyPath, simplification, conversion back to Point2[], and the scorer. Frozen-file loading, provenance verification, and SVG/report writing are outside the timed operation. These warm numbers exclude first use and retained workspace. Area change measures changed filled region; it does not bound the largest contour displacement.\n");
        md.AppendLine("Raw process records include assembly hashes, exact values, batch iteration counts, times, and allocated bytes. This small proposed use case demonstrates measurable simplification error; it establishes neither demand from customers nor recognition accuracy.");
        File.WriteAllText(Path.Combine(directory, "summary.md"), md.ToString());
        Console.WriteLine($"Wrote {Path.GetFullPath(Path.Combine(directory, "summary.md"))}");
    }

    // Request Jaccard inside the timed batch as well: returning its inputs alone is not the consumer.
    private static void Keep(Areas value) => sink = value.Xor + value.Union + value.Jaccard.GetValueOrDefault();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
