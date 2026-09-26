using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.AreaBenchmarks;

internal static partial class Program
{
    private const string GridHash = "548c4085dc2e69b087d4f5a23aeb8903f12428eceb84a79eb818f7b3c35c6c31";
    private const string SquareHash = "3c5e8c9b3c8ec884dc8a62f756d30df73a40657b9606987512c0da2371cf707e";
    private static string SyntheticFixture => Path.Combine(AppContext.BaseDirectory, "data/synthetic-grid.json");
    private sealed record SyntheticInput(string Name, string Classification, Point2[] Points, PathFillRule Rule, double? AnalyticArea)
    {
        internal string CoordinateHash => Corpus.Hash(JsonSerializer.SerializeToUtf8Bytes(Points));
    }
    private sealed record SyntheticValidation(string Input, string Classification, PathFillRule Rule,
        int SuppliedVertices, string CoordinateHash, int CrossingCount, double? AnalyticArea,
        double Public, double ClosedPath, double Clipper, double ClipperMinusPublic);
    private sealed record SyntheticAggregate(string Input, PathFillRule Rule, string Method, double Value,
        double MedianNs, double MinNs, double MaxNs, double MedianBytes, double MinBytes, double MaxBytes);

    private static SyntheticInput[] SyntheticInputs()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(SyntheticFixture));
        Require(document.RootElement.GetProperty("Name").GetString() == "frozen-grid-256", "Unexpected synthetic fixture.");
        Point2[] grid = document.RootElement.GetProperty("Points").EnumerateArray()
            .Select(p => new Point2(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();
        Require(grid.Length == 512 && Corpus.Hash(JsonSerializer.SerializeToUtf8Bytes(grid)) == GridHash, "Frozen grid coordinates changed.");
        const int n = 4096;
        Point2[] star = Enumerable.Range(0, n).Select(i =>
        {
            double angle = 2 * Math.PI * i / n, radius = i % 2 == 0 ? 1 : .15;
            return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToArray();
        Point2[] corners = [new(0, 0), new(7, 0), new(7, 7), new(0, 7)];
        Point2[] square = Enumerable.Range(0, 512).Select(i => corners[i % 4]).ToArray();
        Require(Corpus.Hash(JsonSerializer.SerializeToUtf8Bytes(square)) == SquareHash, "Repeated-square coordinates changed.");
        return [
            new("simple-spiky-star-4096", "simple radial star; no self intersections", star, PathFillRule.NonZero, n * .15 * Math.Sin(2 * Math.PI / n) / 2),
            new("frozen-grid-256", "dense integer grid walk; 512 supplied vertices", grid, PathFillRule.NonZero, null),
            new("frozen-grid-256", "dense integer grid walk; 512 supplied vertices", grid, PathFillRule.EvenOdd, null),
            new("repeated-square-512", "128 coincident traversals of a 7 by 7 square", square, PathFillRule.NonZero, 49),
            new("repeated-square-512", "128 coincident traversals of a 7 by 7 square", square, PathFillRule.EvenOdd, 0)
        ];
    }

    private static Cell[] SyntheticCells(SyntheticInput[] inputs) => inputs.Select(input =>
    {
        var loaded = new Clipper64(); loaded.AddSubject(SyntheticConvert(input.Points));
        var full = new Clipper64();
        var loadedOutput = new Paths64(); var loadedOpen = new Paths64();
        var fullOutput = new Paths64(); var fullOpen = new Paths64();
        FillRule fill = input.Rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
        Method[] methods = [
            new("Public", () => PolylineArea.FilledArea(input.Points, input.Rule)),
            new("ClosedPath", () =>
            {
                WindingAreaResult result = WindingArea.ClosedPath(input.Points);
                return input.Rule == PathFillRule.NonZero ? result.NonZero : result.EvenOdd;
            }),
            new("Clipper-preloaded", () => SyntheticExecute(loaded, fill, loadedOutput, loadedOpen)),
            new("Clipper-full", () =>
            {
                full.Clear(); full.AddSubject(SyntheticConvert(input.Points));
                return SyntheticExecute(full, fill, fullOutput, fullOpen);
            })
        ];
        return new Cell(input.Name, input.Rule, methods);
    }).ToArray();

    // Preserve the earlier synthetic probe's Point64(double) rounding, not the real corpus's cast.
    private static Paths64 SyntheticConvert(Point2[] points) =>
        new() { new Path64(points.Select(p => new Point64(p.X * Scale, p.Y * Scale))) };

    private static double SyntheticExecute(Clipper64 engine, FillRule rule, Paths64 closed, Paths64 open)
    {
        Require(engine.Execute(ClipType.Union, rule, closed, open), "Clipper execution failed.");
        return Clipper.Area(closed) / (Scale * Scale);
    }

    private static SyntheticValidation[] ValidateSynthetic(SyntheticInput[] inputs, string? directory)
    {
        var rows = new List<SyntheticValidation>();
        Cell[] cells = SyntheticCells(inputs);
        for (int i = 0; i < inputs.Length; i++)
        {
            SyntheticInput input = inputs[i];
            string hash = input.CoordinateHash;
            WindingAreaResult general = WindingArea.ClosedPath(input.Points);
            double[] values = cells[i].Methods.Select(m => m.Invoke()).ToArray();
            Require(Math.Abs(values[0] - values[1]) <= 1e-12 * Math.Max(1, Math.Abs(values[1])), "Selected/general area disagreement.");
            Require(Bits(values[2]) == Bits(values[3]), "Clipper preparation variants differ.");
            for (int j = 0; j < values.Length; j++)
                Require(double.IsFinite(values[j]) && values[j] >= 0 && Bits(values[j]) == Bits(cells[i].Methods[j].Invoke()), "Invalid or unstable synthetic area.");
            if (input.AnalyticArea is double expected)
            {
                Require(Math.Abs(values[0] - expected) <= 1e-12 && Math.Abs(values[1] - expected) <= 1e-12, "Analytic area differs.");
                if (input.Name == "repeated-square-512") Require(values[2] == expected, "Clipper square area differs.");
            }
            if (input.Name == "simple-spiky-star-4096") Require(general.CrossingCount == 0, "Radial star must have no crossings.");
            Require(hash == input.CoordinateHash, "Validation mutated input.");
            rows.Add(new(input.Name, input.Classification, input.Rule, input.Points.Length, hash,
                general.CrossingCount, input.AnalyticArea, values[0], values[1], values[2], values[2] - values[0]));
        }
        // Agreement with the boundary engine is not an independent exact oracle for the grid.
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            WriteSyntheticEvidence(Path.Combine(directory, "validation.json"), Serialize(rows));
        }
        Console.WriteLine($"Synthetic check: {rows.Count} fill cases; analytic star/square, frozen grid and stable outputs verified.");
        return rows.ToArray();
    }

    private static void WriteSyntheticEvidence(string path, string text)
    {
        if (File.Exists(path)) Require(File.ReadAllText(path) == text, $"Existing evidence must not be overwritten: {path}");
        else File.WriteAllText(path, text);
    }

    private static void MeasureSyntheticRun(string directory, int run, string revision)
    {
        VerifyRevision(revision);
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Use run1..3 with tiering disabled.");
        string path = Path.Combine(directory, $"run-{run}.json");
        Require(!File.Exists(path), "Existing measured run must not be overwritten.");
        SyntheticInput[] inputs = SyntheticInputs();
        ValidateSynthetic(inputs, directory);
        string[] hashes = inputs.Select(c => c.CoordinateHash).ToArray();
        var rows = new List<Row>();
        foreach (Cell cell in SyntheticCells(inputs))
        {
            int offset = run - 1;
            foreach (Method method in cell.Methods.Skip(offset).Concat(cell.Methods.Take(offset)))
            {
                double value = method.Invoke();
                Sample[] samples = MeasureSynthetic(method.Invoke);
                Require(Bits(value) == Bits(method.Invoke()), "Timed area changed.");
                rows.Add(new(cell.Input, cell.Rule, method.Name, value,
                    Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
            }
            Console.WriteLine($"run{run}: {cell.Input}/{cell.Rule}");
        }
        Require(inputs.Select(c => c.CoordinateHash).SequenceEqual(hashes), "Timing mutated input.");
        var data = new Run(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), Corpus.Hash(File.ReadAllBytes(SyntheticFixture)),
            AssemblyHash(typeof(Program)), AssemblyHash(typeof(PolylineArea)), AssemblyHash(typeof(Clipper64)), rows.ToArray());
        WriteSyntheticEvidence(path, Serialize(data));
        GC.KeepAlive(sink);
    }

    private static Sample[] MeasureSynthetic(Func<double> invoke)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 80) sink = invoke();
        int iterations = 1;
        while (true)
        {
            timer.Restart();
            for (int i = 0; i < iterations; i++) sink = invoke();
            if (timer.ElapsedMilliseconds >= 30 || iterations >= 1_048_576) break;
            iterations *= 2;
        }
        var samples = new Sample[7];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = invoke();
            long elapsed = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)allocated / iterations);
        }
        return samples;
    }

    private static void SummarizeSynthetic(string directory)
    {
        Run[] runs = Enumerable.Range(1, 3).Select(i => JsonSerializer.Deserialize<Run>(File.ReadAllText(Path.Combine(directory, $"run-{i}.json")))!).ToArray();
        VerifyRevision(runs[0].Revision);
        string launchPath = Path.Combine(directory, "process-order.json"), validationPath = Path.Combine(directory, "validation.json");
        Launch[] launches = JsonSerializer.Deserialize<Launch[]>(File.ReadAllText(launchPath))!;
        Require(launches.Length == 3, "Expected three sequential launch records.");
        SyntheticInput[] inputs = SyntheticInputs();
        SyntheticValidation[] validation = ValidateSynthetic(inputs, null);
        Require(File.ReadAllText(validationPath) == Serialize(validation), "Validation differs from recomputation.");
        Cell[] cells = SyntheticCells(inputs);
        string Key(string input, PathFillRule rule, string method) => $"{input}/{rule}/{method}";
        var values = cells.SelectMany(c => c.Methods.Select(m => (Key: Key(c.Input, c.Rule, m.Name), Value: m.Invoke()))).ToDictionary(x => x.Key, x => x.Value);
        string Identity(Run r) => Serialize(r with { Number = 0, Utc = default, Rows = [] });
        for (int i = 0; i < runs.Length; i++)
        {
            Run run = runs[i]; Launch launch = launches[i];
            Require(launch.Run == i + 1 && launch.Revision == run.Revision && launch.HarnessHash == run.HarnessHash &&
                launch.ExitCode == 0 && launch.StartedUtc < launch.FinishedUtc &&
                launch.StartedUtc <= run.Utc && run.Utc <= launch.FinishedUtc &&
                (i == 0 || launches[i - 1].FinishedUtc <= launch.StartedUtc), "Launch identity, status or sequential timing differs.");
            Require(run.Number == i + 1 && Identity(run) == Identity(runs[0]) && run.TieredCompilation == "0" &&
                run.SourceHash == Corpus.Hash(File.ReadAllBytes(SyntheticFixture)), "Run/environment/source identity differs.");
            Require(run.HarnessHash == AssemblyHash(typeof(Program)) && run.LibraryHash == AssemblyHash(typeof(PolylineArea)) &&
                run.ClipperHash == AssemblyHash(typeof(Clipper64)), "Summarize with the frozen measured binaries.");
            string[] order = cells.SelectMany(c => c.Methods.Skip(i).Concat(c.Methods.Take(i)).Select(m => Key(c.Input, c.Rule, m.Name))).ToArray();
            Require(run.Rows.Select(r => Key(r.Input, r.Rule, r.Method)).SequenceEqual(order), "Incomplete or reordered method matrix.");
            foreach (Row row in run.Rows)
            {
                Require(Bits(row.Value) == Bits(values[Key(row.Input, row.Rule, row.Method)]), "Recorded area differs.");
                Require(row.Samples.Length == 7, "Expected seven samples.");
                int iterations = row.Samples[0].Iterations;
                Require(iterations > 0 && iterations <= 1_048_576 && (iterations & (iterations - 1)) == 0 &&
                    row.Samples.All(s => s.Iterations == iterations && double.IsFinite(s.Nanoseconds) && s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0),
                    "Invalid sample values or calibration count.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) && row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Sample median differs.");
            }
        }
        SyntheticAggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(r => Key(r.Input, r.Rule, r.Method)).Select(g =>
        {
            Row first = g.First();
            Require(g.Count() == 3 && g.All(r => Bits(r.Value) == Bits(first.Value)), "Cross-process area differs.");
            return new SyntheticAggregate(first.Input, first.Rule, first.Method, first.Value, Median(g.Select(r => r.MedianNs)),
                g.Min(r => r.MedianNs), g.Max(r => r.MedianNs), Median(g.Select(r => r.MedianBytes)), g.Min(r => r.MedianBytes), g.Max(r => r.MedianBytes));
        }).ToArray();
        var comparisons = aggregates.GroupBy(r => (r.Input, r.Rule)).Select(g =>
        {
            double Time(string method) => g.Single(r => r.Method == method).MedianNs;
            return new { g.Key.Input, g.Key.Rule, PublicNs = Time("Public"),
                PreloadedClipperOverPublic = Time("Clipper-preloaded") / Time("Public"),
                FullClipperOverPublic = Time("Clipper-full") / Time("Public"),
                ClosedPathOverPublic = Time("ClosedPath") / Time("Public") };
        }).ToArray();
        var evidence = new { Suite = "Synthetic selected fill", Protocol = "benchmarks/PolylineKit.AreaBenchmarks/SYNTHETIC.md",
            Environment = runs[0] with { Rows = [] }, ClipperVersion = typeof(Clipper64).Assembly.GetName().Version!.ToString(),
            Scale, Quantization = "Point64(double), rounding as provided by Clipper2 2.0.0", WarmupMs = 80, CalibrationMs = 30,
            Batches = 7, ForcedGcBeforeEachBatch = true, Cases = validation.Length, RowsPerProcess = runs[0].Rows.Length,
            SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)), Validation = validation,
            ProcessOrder = launches, ProcessOrderHash = Corpus.Hash(File.ReadAllBytes(launchPath)),
            ValidationHash = Corpus.Hash(File.ReadAllBytes(validationPath)),
            RawFiles = Enumerable.Range(1, 3).Select(i => new { File = $"run-{i}.json", Sha256 = Corpus.Hash(File.ReadAllBytes(Path.Combine(directory, $"run-{i}.json"))) }),
            Comparisons = comparisons, Measurements = aggregates };
        WriteSyntheticEvidence(Path.Combine(directory, "evidence.json"), Serialize(evidence));
        var report = new StringBuilder("# Synthetic selected-fill benchmark\n\nTimes are medians of three process medians; ranges are observed, not confidence intervals. These selected inputs are not a population benchmark or a universal speed guarantee.\n\n");
        report.AppendLine("The star is simple, the grid has dense crossings, and the square retraces coincident edges 128 times. The grid check compares two library engines; it is not an independent exact oracle. Clipper quantizes coordinates and constructs output polygons.\n");
        report.AppendLine("| Input | Fill | Public us | Preloaded Clipper / public | Full Clipper / public | ClosedPath / public |\n|---|---|---:|---:|---:|---:|");
        foreach (var c in comparisons) report.AppendLine(FormattableString.Invariant($"| {c.Input} | {c.Rule} | {c.PublicNs / 1000:F3} | {c.PreloadedClipperOverPublic:F3} | {c.FullClipperOverPublic:F3} | {c.ClosedPathOverPublic:F3} |"));
        report.AppendLine("\n## Every measured method\n\n| Input | Fill | Method | us | Process range us | B/op | Process range B/op |\n|---|---|---|---:|---:|---:|---:|");
        foreach (SyntheticAggregate a in aggregates) report.AppendLine(FormattableString.Invariant($"| {a.Input} | {a.Rule} | {a.Method} | {a.MedianNs / 1000:F3} | {a.MinNs / 1000:F3}–{a.MaxNs / 1000:F3} | {a.MedianBytes:F0} | {a.MinBytes:F0}–{a.MaxBytes:F0} |"));
        WriteSyntheticEvidence(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Synthetic evidence: {evidence.Cases} cases, {evidence.RowsPerProcess} rows/process, {evidence.SampleCount} samples verified.");
    }
}
