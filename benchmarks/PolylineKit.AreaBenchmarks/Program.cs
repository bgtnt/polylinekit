using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.AreaBenchmarks;

internal static partial class Program
{
    private const double Scale = 1e6;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static double sink;
    private sealed record Method(string Name, Func<double> Invoke);
    private sealed record Cell(string Input, PathFillRule Rule, Method[] Methods);
    private sealed record Validation(string Input, string County, string CoordinateHash, int SuppliedVertices,
        PathFillRule Rule, Corpus.GeometryCheck Geometry, double Public, double Winding, double Clipper,
        double? PublicOracleError, double? ClipperOracleError, double? ClipperTolerance, bool? ClipperOraclePass);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Row(string Input, PathFillRule Rule, string Method, double Value,
        double MedianNs, double MedianBytes, Sample[] Samples);
    private sealed record Run(int Number, string Revision, DateTimeOffset Utc, string Runtime, string OS,
        string Architecture, string? Cpu, int LogicalProcessors, string? TieredCompilation,
        string SourceHash, string HarnessHash, string LibraryHash, string ClipperHash, Row[] Rows);
    private sealed record Launch(int Run, string Revision, string HarnessHash, DateTimeOffset StartedUtc,
        DateTimeOffset FinishedUtc, int ExitCode);
    private sealed record Aggregate(string Input, PathFillRule Rule, string Method, double Value,
        double MedianNs, double MinNs, double MaxNs, double MedianBytes);

    internal static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "check" when args.Length == 2: Validate(Corpus.Load(), args[1]); return 0;
            case "run" when args.Length == 4: MeasureRun(args[1], int.Parse(args[2]), args[3]); return 0;
            case "summarize" when args.Length == 2: Summarize(args[1]); return 0;
            case "synthetic-check" when args.Length == 2: ValidateSynthetic(SyntheticInputs(), args[1]); return 0;
            case "synthetic-run" when args.Length == 4: MeasureSyntheticRun(args[1], int.Parse(args[2]), args[3]); return 0;
            case "synthetic-summarize" when args.Length == 2: SummarizeSynthetic(args[1]); return 0;
            default:
                Console.Error.WriteLine("check <output> | run <output> <1..3> <commit> | summarize <output> (prefix commands with synthetic- for synthetic cases)");
                return 2;
        }
    }

    private static Method[] Methods(Point2[] points, PathFillRule rule)
    {
        var full = new Clipper64(); var loaded = new Clipper64();
        loaded.AddSubject(Convert(points));
        var fullOutput = new Paths64(); var fullOpen = new Paths64();
        var loadedOutput = new Paths64(); var loadedOpen = new Paths64();
        FillRule fill = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
        return [
            new("Public", () => PolylineArea.FilledArea(points, rule)),
            new("ClosedPath", () =>
            {
                WindingAreaResult result = WindingArea.ClosedPath(points);
                return rule == PathFillRule.NonZero ? result.NonZero : result.EvenOdd;
            }),
            new("Clipper-full", () =>
            {
                full.Clear(); full.AddSubject(Convert(points));
                return Execute(full, fill, fullOutput, fullOpen);
            }),
            new("Clipper-preloaded", () => Execute(loaded, fill, loadedOutput, loadedOpen))
        ];
    }

    private static Path64 Convert(Point2[] points)
    {
        var result = new Path64(points.Length);
        foreach (Point2 point in points) result.Add(new Point64(checked((long)(point.X * Scale)), checked((long)(point.Y * Scale))));
        return result;
    }

    private static double Execute(Clipper64 engine, FillRule rule, Paths64 closed, Paths64 open)
    {
        closed.Clear(); open.Clear();
        Require(engine.Execute(ClipType.Union, rule, closed, open), "Clipper execution failed.");
        return Clipper.Area(closed) / (Scale * Scale);
    }

    private static Cell[] Cells(Contour[] contours)
    {
        var cells = new List<Cell>();
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Cell[] perContour = contours.Select(c => new Cell(c.Id, rule, Methods(c.Points, rule))).ToArray();
            cells.AddRange(perContour);
            // A batch is one call for every ring, in source order, with no filtering or data preparation.
            Method[] batch = Enumerable.Range(0, perContour[0].Methods.Length).Select(index =>
                new Method(perContour[0].Methods[index].Name, () =>
                {
                    double sum = 0;
                    foreach (Cell cell in perContour) sum += cell.Methods[index].Invoke();
                    return sum;
                })).ToArray();
            cells.Add(new("all-rings-batch", rule, batch));
        }
        return cells.ToArray();
    }

    private static Validation[] Validate(Contour[] contours, string? directory)
    {
        Corpus.CheckRounding();
        var rows = new List<Validation>();
        foreach (Contour contour in contours)
        {
            string hash = contour.CoordinateHash;
            Corpus.GeometryCheck geometry = Corpus.Check(contour.Points);
            if (!geometry.Simple) Console.WriteLine($"Geometry note: {contour.Id}: {geometry.InvalidReason}; no simple-area oracle is claimed.");
            else Require(Math.Abs(geometry.TranslatedShoelace - geometry.ExactDyadicShoelace) <=
                1e-12 * Math.Max(1, geometry.ExactDyadicShoelace), "Translated/exact shoelace oracle disagreement.");
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Method[] methods = Methods(contour.Points, rule);
                double[] values = methods.Select(m => m.Invoke()).ToArray();
                Require(Bits(values[0]) == Bits(values[1]), "Real-coordinate public/ClosedPath fallback parity failed.");
                Require(Bits(values[2]) == Bits(values[3]), "Clipper preparation variants differ.");
                for (int i = 0; i < methods.Length; i++)
                    Require(double.IsFinite(values[i]) && Bits(values[i]) == Bits(methods[i].Invoke()), "Nonfinite/unstable area.");
                Require(values[0] >= 0 && contour.CoordinateHash == hash, "Negative public area or mutated input.");
                double? error = geometry.Simple ? values[0] - geometry.ExactDyadicShoelace : null;
                if (error.HasValue) Require(Math.Abs(error.Value) <= 1e-10 * Math.Max(1, geometry.ExactDyadicShoelace), "Public area differs from independent oracle.");
                double? clipperError = geometry.Simple ? values[2] - geometry.ExactDyadicShoelace : null;
                // Grid displacement allowance plus a separate floating-point comparison allowance.
                // This diagnostic is not a universal bound for arbitrary quantized self intersections.
                double? clipperTolerance = geometry.Simple ? 2 * geometry.Perimeter / Scale +
                    4 * contour.Points.Length / (Scale * Scale) + 1e-10 * Math.Max(1, geometry.ExactDyadicShoelace) : null;
                bool? clipperPass = clipperError.HasValue ? values[2] >= 0 && Math.Abs(clipperError.Value) <= clipperTolerance!.Value : null;
                rows.Add(new(contour.Id, contour.County, hash, contour.Points.Length, rule, geometry,
                    values[0], values[1], values[2], error, clipperError, clipperTolerance, clipperPass));
            }
        }
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "validation.json"), text = Serialize(rows);
            if (File.Exists(path)) Require(File.ReadAllText(path) == text, "Existing validation evidence differs.");
            else File.WriteAllText(path, text);
        }
        Console.WriteLine($"Real contours: {contours.Length} rings, {rows.Count} fill cases; {rows.Count(r => r.Geometry.Simple)} simple-oracle cases; input/output parity verified.");
        return rows.ToArray();
    }

    private static void MeasureRun(string directory, int run, string revision)
    {
        VerifyRevision(revision);
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Use run1..3 with tiering disabled.");
        string path = Path.Combine(directory, $"run-{run}.json");
        Require(!File.Exists(path), "Existing measured run must not be overwritten.");
        Contour[] contours = Corpus.Load();
        Validate(contours, directory);
        string[] coordinateHashes = contours.Select(c => c.CoordinateHash).ToArray();
        var rows = new List<Row>();
        foreach (Cell cell in Cells(contours))
        {
            int offset = run - 1; // Three distinct order rotations for four methods.
            foreach (Method method in cell.Methods.Skip(offset).Concat(cell.Methods.Take(offset)))
            {
                double value = method.Invoke();
                Sample[] samples = Measure(method.Invoke);
                Require(Bits(value) == Bits(method.Invoke()), "Timed area changed.");
                rows.Add(new(cell.Input, cell.Rule, method.Name, value,
                    Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
            }
            Console.WriteLine($"run{run}: {cell.Input}/{cell.Rule}");
        }
        Require(contours.Select(c => c.CoordinateHash).SequenceEqual(coordinateHashes), "Timing mutated input coordinates.");
        var data = new Run(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), Corpus.FileHash,
            AssemblyHash(typeof(Program)), AssemblyHash(typeof(PolylineArea)), AssemblyHash(typeof(Clipper64)), rows.ToArray());
        File.WriteAllText(path, Serialize(data));
        GC.KeepAlive(sink);
    }

    // Same calibration/allocation-counter semantics as the archived adaptive-area experiment.
    private static Sample[] Measure(Func<double> invoke)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 40) sink = invoke();
        int iterations = 1;
        while (true)
        {
            timer.Restart();
            for (int i = 0; i < iterations; i++) sink = invoke();
            if (timer.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
            iterations *= 2;
        }
        var samples = new Sample[5];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = invoke();
            long elapsed = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)allocated / iterations);
        }
        return samples;
    }

    private static void Summarize(string directory)
    {
        Run[] runs = Enumerable.Range(1, 3).Select(i => JsonSerializer.Deserialize<Run>(File.ReadAllText(Path.Combine(directory, $"run-{i}.json")))!).ToArray();
        VerifyRevision(runs[0].Revision);
        string launchPath = Path.Combine(directory, "process-order.json");
        Launch[] launches = JsonSerializer.Deserialize<Launch[]>(File.ReadAllText(launchPath))!;
        Require(launches.Length == 3, "Expected three sequential launch records.");
        Contour[] contours = Corpus.Load();
        Validation[] validation = Validate(contours, null);
        Require(File.ReadAllText(Path.Combine(directory, "validation.json")) == Serialize(validation), "Validation differs from recomputation.");
        Cell[] cells = Cells(contours);
        string Key(string input, PathFillRule rule, string method) => $"{input}/{rule}/{method}";
        var values = cells.SelectMany(c => c.Methods.Select(m => (Key: Key(c.Input, c.Rule, m.Name), Value: m.Invoke()))).ToDictionary(x => x.Key, x => x.Value);
        string Identity(Run r) => Serialize(r with { Number = 0, Utc = default, Rows = [] });
        for (int i = 0; i < runs.Length; i++)
        {
            Run run = runs[i];
            Launch launch = launches[i];
            Require(launch.Run == i + 1 && launch.Revision == run.Revision && launch.HarnessHash == run.HarnessHash &&
                launch.ExitCode == 0 && launch.StartedUtc < launch.FinishedUtc &&
                launch.StartedUtc <= run.Utc && run.Utc <= launch.FinishedUtc &&
                (i == 0 || launches[i - 1].FinishedUtc <= launch.StartedUtc), "Launch identity, status or sequential timing differs.");
            Require(run.Number == i + 1 && Identity(run) == Identity(runs[0]) && run.TieredCompilation == "0" && run.SourceHash == Corpus.FileHash,
                "Run/environment/source identity differs.");
            Require(run.HarnessHash == AssemblyHash(typeof(Program)) && run.LibraryHash == AssemblyHash(typeof(PolylineArea)) &&
                run.ClipperHash == AssemblyHash(typeof(Clipper64)), "Summarize with the frozen measured binaries.");
            string[] order = cells.SelectMany(c => c.Methods.Skip(i).Concat(c.Methods.Take(i)).Select(m => Key(c.Input, c.Rule, m.Name))).ToArray();
            Require(run.Rows.Select(r => Key(r.Input, r.Rule, r.Method)).SequenceEqual(order), "Incomplete or reordered method matrix.");
            foreach (Row row in run.Rows)
            {
                Require(Bits(row.Value) == Bits(values[Key(row.Input, row.Rule, row.Method)]), "Recorded area differs.");
                Require(row.Samples.Length == 5, "Expected five samples.");
                int iterations = row.Samples[0].Iterations;
                Require(iterations > 0 && iterations <= 1_048_576 && (iterations & (iterations - 1)) == 0 &&
                    row.Samples.All(s => s.Iterations == iterations && double.IsFinite(s.Nanoseconds) && s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0),
                    "Invalid sample values or calibration count.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) && row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Sample median differs.");
            }
        }
        Aggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(r => Key(r.Input, r.Rule, r.Method)).Select(g =>
        {
            Row first = g.First();
            Require(g.Count() == 3 && g.All(r => Bits(r.Value) == Bits(first.Value)), "Cross-process area differs.");
            return new Aggregate(first.Input, first.Rule, first.Method, first.Value, Median(g.Select(r => r.MedianNs)),
                g.Min(r => r.MedianNs), g.Max(r => r.MedianNs), Median(g.Select(r => r.MedianBytes)));
        }).ToArray();
        var decisions = aggregates.GroupBy(r => (r.Input, r.Rule)).Select(g =>
        {
            double Time(string method) => g.Single(r => r.Method == method).MedianNs;
            double ratio = Time("Public") / Time("ClosedPath");
            return new { g.Key.Input, g.Key.Rule, PublicNs = Time("Public"), ClosedPathNs = Time("ClosedPath"),
                PublicOverClosedPath = ratio, WithinTenPercent = ratio <= 1.10, PrimaryGate = g.Key.Input == "all-rings-batch" };
        }).ToArray();
        bool pass = decisions.Where(d => d.PrimaryGate).All(d => d.WithinTenPercent);
        var evidence = new { Protocol = "benchmarks/PolylineKit.AreaBenchmarks/PROTOCOL.md", Environment = runs[0] with { Rows = [] },
            Pass = pass, Cases = validation.Length, RowsPerProcess = runs[0].Rows.Length,
            SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)), Validation = validation,
            ProcessOrder = launches, ProcessOrderHash = Corpus.Hash(File.ReadAllBytes(launchPath)),
            ValidationHash = Corpus.Hash(File.ReadAllBytes(Path.Combine(directory, "validation.json"))),
            RawFiles = Enumerable.Range(1, 3).Select(i => new { File = $"run-{i}.json", Sha256 = Corpus.Hash(File.ReadAllBytes(Path.Combine(directory, $"run-{i}.json"))) }),
            Decisions = decisions, Measurements = aggregates };
        File.WriteAllText(Path.Combine(directory, "evidence.json"), Serialize(evidence));
        var report = new StringBuilder($"# Real contour benchmark\n\nComplete-batch gate: **{(pass ? "PASS" : "FAIL")}**. Times are medians of three process medians; ranges are observed, not confidence intervals.\n\n");
        report.AppendLine("| Ring/batch | Fill | Public us | ClosedPath us | Public/ClosedPath | Within 10% |\n|---|---|---:|---:|---:|---|");
        foreach (var d in decisions) report.AppendLine(FormattableString.Invariant($"| {d.Input} | {d.Rule} | {d.PublicNs / 1000:F3} | {d.ClosedPathNs / 1000:F3} | {d.PublicOverClosedPath:F4} | {d.WithinTenPercent} |"));
        report.AppendLine("\n## Every measured method\n\n| Ring/batch | Fill | Method | us | Range us | B/op |\n|---|---|---|---:|---:|---:|");
        foreach (Aggregate a in aggregates) report.AppendLine(FormattableString.Invariant($"| {a.Input} | {a.Rule} | {a.Method} | {a.MedianNs / 1000:F3} | {a.MinNs / 1000:F3}–{a.MaxNs / 1000:F3} | {a.MedianBytes:F0} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Complete-batch gate={pass}; {evidence.SampleCount} samples, {decisions.Count(d => !d.WithinTenPercent)} per-case/batch deviations above10%.");
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json) + "\n";
    private static string AssemblyHash(Type type) => Corpus.Hash(File.ReadAllBytes(type.Assembly.Location));
    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static double Median(IEnumerable<double> values) { double[] sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void VerifyRevision(string revision)
    {
        Require(revision.Length == 40 && revision.All(Uri.IsHexDigit), "Expected a full source revision.");
        foreach (Type type in new[] { typeof(Program), typeof(PolylineArea) })
        {
            string? version = type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Require(version is not null && version.Contains(revision, StringComparison.OrdinalIgnoreCase),
                $"Requested source revision must match {type.Assembly.GetName().Name}'s embedded commit.");
        }
    }
}
