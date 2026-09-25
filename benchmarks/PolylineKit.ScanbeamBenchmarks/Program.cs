using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

internal static class Program
{
    private const double Scale = 1e6;
    private static double sink;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Method(string Name, Func<double> Invoke);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Row(string Input, int Vertices, string InputHash, bool Gate, PathFillRule Rule,
        string Method, double Area, double WindingArea, double UnscaledClipperArea,
        double MedianNs, double MedianBytes, Sample[] Samples);
    private sealed record RunFile(int Run, string Revision, DateTimeOffset Utc, string Runtime,
        string OS, string Architecture, string? Cpu, int LogicalProcessors, string? TieredCompilation,
        string HarnessHash, string WindingHash, string ClipperHash, Row[] Rows);
    private sealed record Aggregate(string Input, int Vertices, string InputHash, bool Gate,
        PathFillRule Rule, string Method, double Area, double WindingArea, double UnscaledClipperArea,
        double MedianNs, double MinNs, double MaxNs, double MedianBytes);

    internal static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "check" when args.Length == 1:
                Checks.Run();
                CheckInputs();
                return 0;
            case "benchmark" when args.Length == 4:
                Run(args[1], int.Parse(args[2]), args[3]);
                return 0;
            case "summarize" when args.Length == 2:
                Summarize(args[1]);
                return 0;
            case "inspect" when args.Length == 2:
                Inspect(args[1]);
                return 0;
            default:
                Console.Error.WriteLine("check | benchmark <directory> <run 1..3> <revision> | summarize <directory> | inspect <file.json>");
                return 2;
        }
    }

    private static void CheckInputs()
    {
        int count = 0;
        foreach (Input input in Inputs.Create())
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            var methods = Methods(input.Points, rule);
            double expected = methods[0].Invoke();
            Near(expected, methods[1].Invoke(), $"{input.Name}/{rule}");
            foreach (Method method in methods)
            {
                double area = method.Invoke();
                Require(double.IsFinite(area) && area >= 0, $"Invalid {method.Name} area.");
                Require(area == method.Invoke(), $"Unstable {method.Name} area.");
                count++;
            }
        }
        Console.WriteLine($"Scanbeam complete benchmark inputs: {count} method/fill checks.");
    }

    private static void Inspect(string path)
    {
        var engine = new IntegerScanbeam();
        var rows = new List<object>();
        foreach (Input input in Inputs.Create())
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            double area = engine.Measure(input.Points, rule);
            rows.Add(new { input.Name, Vertices = input.Points.Length, input.Hash, Rule = rule, Area = area,
                engine.BandCount, engine.EventCount, engine.EventGroupCount, engine.PeakActiveCount });
        }
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(new { HarnessHash = AssemblyHash(typeof(Program)), Rows = rows }, Json) + "\n");
        Console.WriteLine($"Algorithm counts for {rows.Count} input/fill pairs written without timings.");
    }

    private static Method[] Methods(Point2[] points, PathFillRule rule)
    {
        var prototype = new IntegerScanbeam();
        var integers = Convert(points, Scale);
        var reused = new Clipper64();
        var prepared = new Clipper64();
        prepared.AddSubject(integers);
        var output = new Paths64(); var open = new Paths64();
        var preparedOutput = new Paths64(); var preparedOpen = new Paths64();
        FillRule fill = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
        return [
            new("WindingArea", () => { var a = WindingArea.ClosedPath(points); return rule == PathFillRule.NonZero ? a.NonZero : a.EvenOdd; }),
            new("IntegerScanbeam", () => prototype.Measure(points, rule)),
            new("Clipper64-reused-p6", () => { reused.Clear(); reused.AddSubject(integers); return Execute(reused, fill, output, open, Scale); }),
            new("Clipper64-preloaded-p6", () => Execute(prepared, fill, preparedOutput, preparedOpen, Scale))
        ];
    }

    private static Paths64 Convert(Point2[] points, double scale) => new()
    {
        new Path64(points.Select(p => new Point64(checked((long)(p.X * scale)), checked((long)(p.Y * scale)))))
    };

    private static double Execute(Clipper64 engine, FillRule rule, Paths64 output, Paths64 open, double scale)
    {
        if (!engine.Execute(ClipType.Union, rule, output, open)) throw new InvalidOperationException("Clipper failed.");
        return Clipper.Area(output) / (scale * scale);
    }

    private static double Unscaled(Point2[] points, PathFillRule rule) => Clipper.Area(Clipper.Union(
        Convert(points, 1), rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd));

    private static void Run(string directory, int run, string revision)
    {
        Require(run is >= 1 and <= 3, "Run must be 1..3.");
        Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Set DOTNET_TieredCompilation=0.");
        Directory.CreateDirectory(directory);
        var rows = new List<Row>();
        foreach (Input input in Inputs.Create())
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Method[] methods = Methods(input.Points, rule);
            double winding = methods[0].Invoke(), integer = methods[1].Invoke(), unscaled = Unscaled(input.Points, rule);
            Near(winding, integer, input.Name + "/" + rule);
            int offset = (run - 1) % methods.Length;
            foreach (Method method in methods.Skip(offset).Concat(methods.Take(offset)))
            {
                double area = method.Invoke();
                Require(double.IsFinite(area) && area >= 0, "Invalid area.");
                Sample[] samples = Measure(method.Invoke);
                Require(area == method.Invoke(), "Operation changed its result.");
                rows.Add(new(input.Name, input.Points.Length, input.Hash, input.Gate, rule, method.Name,
                    area, winding, unscaled, Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
            }
            Console.WriteLine($"run {run}: {input.Name}/{rule}");
        }
        var data = new RunFile(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), AssemblyHash(typeof(Program)),
            AssemblyHash(typeof(WindingArea)), AssemblyHash(typeof(Clipper64)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(data, Json) + "\n");
        File.WriteAllText(Path.Combine(directory, "inputs.json"), JsonSerializer.Serialize(Inputs.Create(), Json) + "\n");
        GC.KeepAlive(sink);
    }

    private static Sample[] Measure(Func<double> operation)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 40) sink = operation();
        int iterations = 1;
        while (true)
        {
            timer.Restart();
            for (int i = 0; i < iterations; i++) sink = operation();
            if (timer.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
            iterations *= 2;
        }
        var samples = new Sample[5];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = operation();
            long elapsed = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)allocated / iterations);
        }
        return samples;
    }

    private static void Summarize(string directory)
    {
        var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, $"run-{i}.json")).ToArray();
        RunFile[] runs = paths.Select(p => JsonSerializer.Deserialize<RunFile>(File.ReadAllText(p))!).ToArray();
        string Identity(RunFile r) => JsonSerializer.Serialize(r with { Run = 0, Utc = default, Rows = [] });
        var expected = Inputs.Create().SelectMany(input => Enum.GetValues<PathFillRule>().SelectMany(rule =>
            new[] { "WindingArea", "IntegerScanbeam", "Clipper64-reused-p6", "Clipper64-preloaded-p6" }
            .Select(method => $"{input.Name}|{rule}|{method}"))).Order().ToArray();
        string Key(Row r) => $"{r.Input}|{r.Rule}|{r.Method}";
        var inputs = Inputs.Create().ToDictionary(i => i.Name);
        var outputs = inputs.Values.SelectMany(input => Enum.GetValues<PathFillRule>().SelectMany(rule =>
        {
            Method[] methods = Methods(input.Points, rule);
            double winding = methods[0].Invoke(), unscaled = Unscaled(input.Points, rule);
            return methods.Select(method => (Key: $"{input.Name}|{rule}|{method.Name}", Area: method.Invoke(), Winding: winding, Unscaled: unscaled));
        })).ToDictionary(v => v.Key);
        for (int i = 0; i < runs.Length; i++)
        {
            RunFile run = runs[i];
            Require(run.Run == i + 1 && Identity(run) == Identity(runs[0]), "Run identity differs.");
            Require(run.TieredCompilation == "0", "Measured tiering setting differs from the protocol.");
            Require(run.HarnessHash == AssemblyHash(typeof(Program)) && run.WindingHash == AssemblyHash(typeof(WindingArea)) &&
                run.ClipperHash == AssemblyHash(typeof(Clipper64)), "Use the measured binaries to summarize.");
            Require(run.Rows.Select(Key).Order().SequenceEqual(expected), "Missing or duplicate rows.");
            foreach (Row row in run.Rows)
            {
                Input input = inputs[row.Input];
                Require(row.InputHash == input.Hash && row.Vertices == input.Points.Length && row.Gate == input.Gate, "Input identity differs.");
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) && s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid samples.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) && row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Median differs.");
                Require(double.IsFinite(row.Area) && row.Area >= 0 && double.IsFinite(row.WindingArea) && row.WindingArea >= 0 && double.IsFinite(row.UnscaledClipperArea), "Invalid recorded output.");
                var output = outputs[Key(row)];
                Require(row.Area == output.Area && row.WindingArea == output.Winding && row.UnscaledClipperArea == output.Unscaled, "Recomputed output differs.");
                if (row.Method == "IntegerScanbeam") Near(row.WindingArea, row.Area, "Recorded prototype accuracy.");
            }
        }
        var aggregates = new List<Aggregate>();
        foreach (var group in runs.SelectMany(r => r.Rows).GroupBy(Key).OrderBy(g => g.Key))
        {
            Row a = group.First();
            Require(group.Count() == 3 && group.All(r => r.Area == a.Area && r.WindingArea == a.WindingArea && r.UnscaledClipperArea == a.UnscaledClipperArea), "Output identity differs.");
            aggregates.Add(new(a.Input, a.Vertices, a.InputHash, a.Gate, a.Rule, a.Method, a.Area, a.WindingArea,
                a.UnscaledClipperArea, Median(group.Select(r => r.MedianNs)), group.Min(r => r.MedianNs), group.Max(r => r.MedianNs), Median(group.Select(r => r.MedianBytes))));
        }
        var gates = aggregates.Where(r => r.Gate).GroupBy(r => (r.Input, r.Rule)).Select(g =>
        {
            double candidate = g.Single(r => r.Method == "IntegerScanbeam").MedianNs;
            double clipper = g.Where(r => r.Method.StartsWith("Clipper", StringComparison.Ordinal)).Min(r => r.MedianNs);
            return new { g.Key.Input, g.Key.Rule, ClipperOverPrototype = clipper / candidate, Pass = clipper / candidate >= 1.25 };
        }).ToArray();
        Require(gates.Length == 4, "Expected four gates.");
        var evidence = new { Protocol = "benchmarks/PolylineKit.ScanbeamBenchmarks/PROTOCOL.md", Environment = runs[0] with { Rows = [] },
            RawFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }),
            SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)), Pass = gates.All(g => g.Pass), Gates = gates, Measurements = aggregates };
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(evidence, Json) + "\n");
        var report = new StringBuilder("# Integer scanbeam timing summary\n\nMedian of three process medians; range is their minimum–maximum. Warm complete calls, managed allocations only. See PROTOCOL.md for different preparation and numerical contracts.\n\n");
        report.AppendLine($"Predeclared gate: **{(gates.All(g => g.Pass) ? "PASS" : "FAIL")}**.\n");
        report.AppendLine("| Input | Fill | Method | us | Range us | B/op | Area minus Winding |\n|---|---|---|---:|---:|---:|---:|");
        foreach (Aggregate r in aggregates)
            report.AppendLine(FormattableString.Invariant($"| {r.Input} | {r.Rule} | {r.Method} | {r.MedianNs / 1000:F3} | {r.MinNs / 1000:F3}–{r.MaxNs / 1000:F3} | {r.MedianBytes:F0} | {r.Area - r.WindingArea:G6} |"));
        report.AppendLine("\n| Gate | Fill | Clipper/prototype | Pass |\n|---|---|---:|---|");
        foreach (var gate in gates) report.AppendLine(FormattableString.Invariant($"| {gate.Input} | {gate.Rule} | {gate.ClipperOverPrototype:F3} | {gate.Pass} |"));
        report.AppendLine("\nUnscaled Clipper64 diagnostic (not timed, different crossing grid):\n\n| Input | Fill | Winding | Clipper scale 1 |\n|---|---|---:|---:|");
        foreach (Aggregate r in aggregates.Where(r => r.Method == "WindingArea"))
            report.AppendLine(FormattableString.Invariant($"| {r.Input} | {r.Rule} | {r.Area:G17} | {r.UnscaledClipperArea:G17} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Validated {expected.Length} rows/process, {evidence.SampleCount} samples; gate {(evidence.Pass ? "PASS" : "FAIL")}.");
    }

    private static string AssemblyHash(Type t) => Hash(File.ReadAllBytes(t.Assembly.Location));
    private static string Hash(byte[] bytes) => System.Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static double Median(IEnumerable<double> values) { double[] sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
    private static void Near(double expected, double actual, string name) => Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-10 * Math.Max(1, Math.Abs(expected)), $"{name}: {actual:R} != {expected:R}");
    private static void Require(bool success, string name) { if (!success) throw new InvalidDataException(name); }
}
