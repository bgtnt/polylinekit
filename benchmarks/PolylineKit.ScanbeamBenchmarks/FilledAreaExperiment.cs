using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

internal static partial class Program
{
    private sealed record FilledAreaValidation(string Input, string Family, string Split, string InputHash,
        int Vertices, PathFillRule Rule, HybridSelection Selection, bool PublicIntegerRoute,
        double PublicArray, double PublicList, double HybridV3, double Winding, double Clipper,
        double ClipperMinusWinding, double ClipperRelativeError, bool ClipperAreaNonnegative);

    private static Func<Point2[]?, bool> PublicFilledSelector() =>
        typeof(WindingArea).Assembly.GetType("PolylineKit.FilledAreaSelector", throwOnError: true)!
            .GetMethod("ShouldUseIntegerSweep", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Point2[]?, bool>>();

    private static Method[] FilledAreaMethods(Point2[] points, PathFillRule rule)
    {
        IReadOnlyList<Point2> list = Array.AsReadOnly(points);
        HybridCase retained = HybridMethods(points, rule);
        return [
            new("Public-array", () => WindingArea.FilledArea(points, rule)),
            new("Public-list", () => WindingArea.FilledArea(list, rule)),
            new("Hybrid-v3", retained.Methods.Single(m => m.Name == "Hybrid").Invoke),
            retained.Methods.Single(m => m.Name == "Winding"),
            retained.Methods.Single(m => m.Name == "Clipper-full-input"),
            retained.Methods.Single(m => m.Name == "Clipper-preloaded")
        ];
    }

    private static void CheckFilledArea(string directory)
    {
        HybridSelectorChecks.Run(PublicFilledSelector());
        ValidateFilledArea(directory);
    }

    private static FilledAreaValidation[] ValidateFilledArea(string? directory)
    {
        HybridInput[] inputs = HybridInputs.Create();
        Require(inputs.Length == 34 && inputs.Count(p => p.Split == "development") == 9 &&
            inputs.Count(p => p.Split == "held-out") == 13 && inputs.Count(p => p.Split == "confirmation") == 6 &&
            inputs.Count(p => p.Split == "confirmation-v3") == 6 &&
            inputs.Select(p => p.Name).Distinct().Count() == inputs.Length, "Filled-area fixture matrix changed.");
        Func<Point2[]?, bool> select = PublicFilledSelector();
        var rows = new List<FilledAreaValidation>();
        foreach (HybridInput input in inputs)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            string originalHash = input.Hash;
            Method[] methods = FilledAreaMethods(input.Points, rule);
            var values = methods.ToDictionary(m => m.Name, m => m.Invoke());
            HybridSelection selection = HybridClosedArea.Select(input.Points);
            bool publicInteger = select(input.Points);
            Require(publicInteger == (selection.Backend == "IntegerScanbeam"), "Public selector changed the frozen route.");
            Require(Bits(values["Public-array"]) == Bits(values["Hybrid-v3"]), "Public array output differs from hybrid v3.");
            Require(Bits(values["Public-list"]) == Bits(values["Winding"]), "Public list output differs from Winding.");
            Require(Bits(values["Clipper-full-input"]) == Bits(values["Clipper-preloaded"]), "Clipper preparation changed area.");
            Near(values["Winding"], values["Public-array"], input.Name + "/public-array");
            foreach (Method method in methods)
            {
                Require(double.IsFinite(values[method.Name]) &&
                    (method.Name.StartsWith("Clipper-", StringComparison.Ordinal) || values[method.Name] >= 0),
                    input.Name + "/" + method.Name + ": invalid area.");
                Require(Bits(values[method.Name]) == Bits(method.Invoke()), "Repeated filled-area workload changed output.");
            }
            Require(input.Hash == originalHash, "Filled-area workload mutated caller coordinates.");
            double winding = values["Winding"], clipper = values["Clipper-full-input"];
            rows.Add(new(input.Name, input.Family, input.Split, originalHash, input.Points.Length, rule,
                selection, publicInteger, values["Public-array"], values["Public-list"], values["Hybrid-v3"],
                winding, clipper, clipper - winding,
                winding == 0 ? Math.Abs(clipper) : Math.Abs((clipper - winding) / winding), clipper >= 0));
        }
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(rows, Json) + "\n");
            File.WriteAllText(Path.Combine(directory, "inputs.json"), HybridInputsJson(inputs));
        }
        Console.WriteLine($"Public filled area: {rows.Count} cases, {rows.Count(r => r.PublicIntegerRoute)} integer routes; array/hybrid and list/Winding bit parity.");
        return rows.ToArray();
    }

    private static void RunFilledArea(string directory, int run, string revision)
    {
        VerifyHybridRevision(revision);
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0",
            "Use run1..3 with DOTNET_TieredCompilation=0.");
        ValidateFilledArea(directory);
        var rows = new List<HybridRow>();
        foreach (HybridInput input in HybridInputs.Create())
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Method[] methods = FilledAreaMethods(input.Points, rule);
            int offset = (run - 1) * 2 % methods.Length;
            foreach (Method method in methods.Skip(offset).Concat(methods.Take(offset)))
            {
                double value = method.Invoke();
                Sample[] samples = Measure(method.Invoke);
                Require(Bits(value) == Bits(method.Invoke()), "Timed filled-area output changed.");
                rows.Add(new(input.Name, input.Family, input.Split, input.Hash, input.Points.Length, rule,
                    method.Name, value, Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
            }
            Console.WriteLine($"filled-area run{run}: {input.Name}/{rule}");
        }
        var data = new HybridRun(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), AssemblyHash(typeof(Program)),
            AssemblyHash(typeof(WindingArea)), AssemblyHash(typeof(Clipper64)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(data, Json) + "\n");
        GC.KeepAlive(sink);
    }

    private static void SummarizeFilledArea(string directory)
    {
        string[] paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, $"run-{i}.json")).ToArray();
        HybridRun[] runs = paths.Select(p => JsonSerializer.Deserialize<HybridRun>(File.ReadAllText(p))!).ToArray();
        VerifyHybridRevision(runs[0].Revision);
        string Identity(HybridRun run) => JsonSerializer.Serialize(run with { Run = 0, Utc = default, Rows = [] });
        string Key(string input, PathFillRule rule, string method) => $"{input}|{rule}|{method}";
        HybridInput[] inputs = HybridInputs.Create();
        FilledAreaValidation[] validation = ValidateFilledArea(null);
        Require(File.ReadAllText(Path.Combine(directory, "validation.json")) == JsonSerializer.Serialize(validation, Json) + "\n",
            "Filled-area validation differs from recomputation.");
        Require(File.ReadAllText(Path.Combine(directory, "inputs.json")) == HybridInputsJson(inputs), "Filled-area input manifest differs.");
        var outputs = new Dictionary<string, double>();
        var matrix = new List<(HybridInput Input, PathFillRule Rule, Method[] Methods)>();
        foreach (HybridInput input in inputs)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Method[] methods = FilledAreaMethods(input.Points, rule);
            matrix.Add((input, rule, methods));
            foreach (Method method in methods) outputs.Add(Key(input.Name, rule, method.Name), method.Invoke());
        }
        for (int index = 0; index < runs.Length; index++)
        {
            HybridRun run = runs[index];
            Require(run.Run == index + 1 && Identity(run) == Identity(runs[0]) && run.TieredCompilation == "0",
                "Filled-area run identity differs.");
            Require(run.HarnessHash == AssemblyHash(typeof(Program)) && run.WindingHash == AssemblyHash(typeof(WindingArea)) &&
                run.ClipperHash == AssemblyHash(typeof(Clipper64)), "Use frozen measured binaries to summarize filled-area evidence.");
            string[] order = matrix.SelectMany(x =>
            {
                int offset = index * 2 % x.Methods.Length;
                return x.Methods.Skip(offset).Concat(x.Methods.Take(offset)).Select(m => Key(x.Input.Name, x.Rule, m.Name));
            }).ToArray();
            Require(run.Rows.Select(r => Key(r.Input, r.Rule, r.Method)).SequenceEqual(order), "Filled-area row order/matrix differs.");
            foreach (HybridRow row in run.Rows)
            {
                HybridInput input = inputs.Single(p => p.Name == row.Input);
                Require(row.Family == input.Family && row.Split == input.Split && row.InputHash == input.Hash &&
                    row.Vertices == input.Points.Length, "Filled-area input identity differs.");
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) &&
                    s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid filled-area samples.");
                int iterations = row.Samples[0].Iterations;
                Require(iterations <= 1_048_576 && (iterations & (iterations - 1)) == 0 &&
                    row.Samples.All(s => s.Iterations == iterations), "Filled-area calibrated iteration count differs.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) &&
                    row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Filled-area sample median differs.");
                Require(Bits(row.Value) == Bits(outputs[Key(row.Input, row.Rule, row.Method)]), "Filled-area recorded output differs.");
            }
        }
        HybridAggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(r => Key(r.Input, r.Rule, r.Method)).Select(g =>
        {
            HybridRow first = g.First();
            Require(g.Count() == 3 && g.All(r => Bits(r.Value) == Bits(first.Value)), "Filled-area cross-process output differs.");
            return new HybridAggregate(first.Input, first.Family, first.Split, first.InputHash, first.Vertices, first.Rule,
                first.Method, first.Value, Median(g.Select(r => r.MedianNs)), g.Min(r => r.MedianNs),
                g.Max(r => r.MedianNs), Median(g.Select(r => r.MedianBytes)));
        }).ToArray();
        var decisions = aggregates.GroupBy(r => (r.Input, r.Rule)).Select(group =>
        {
            double Time(string method) => group.Single(r => r.Method == method).MedianNs;
            var reference = validation.Single(v => v.Input == group.Key.Input && v.Rule == group.Key.Rule);
            double actual = Time("Public-array"), list = Time("Public-list"), hybrid = Time("Hybrid-v3"),
                winding = Time("Winding"), clipper = Time("Clipper-full-input"), preloaded = Time("Clipper-preloaded");
            bool target = reference.Family is "dense-grid" or "retraced";
            return new { group.Key.Input, group.Key.Rule, reference.Family, reference.Split, reference.Selection,
                Target = target, PublicArrayNs = actual, PublicListNs = list, HybridV3Ns = hybrid, WindingNs = winding,
                ClipperNs = clipper, PreloadedClipperNs = preloaded, PublicOverWinding = actual / winding,
                PublicOverHybrid = actual / hybrid, PublicOverClipper = actual / clipper,
                PublicOverPreloadedClipper = actual / preloaded, PublicListOverWinding = list / winding,
                TargetPass = !target || (reference.ClipperAreaNonnegative && actual <= .8 * winding && actual <= .8 * clipper),
                PreservationPass = target || actual <= 1.10 * winding, IntegrationPass = actual <= 1.10 * hybrid };
        }).ToArray();
        var evidence = new
        {
            Protocol = "benchmarks/PolylineKit.ScanbeamBenchmarks/FILLED-AREA-PROTOCOL.md",
            Environment = runs[0] with { Rows = [] }, RowsPerProcess = runs[0].Rows.Length,
            SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)),
            Pass = decisions.All(d => d.TargetPass && d.PreservationPass && d.IntegrationPass),
            RawFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }),
            ValidationSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "validation.json"))),
            InputsSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "inputs.json"))),
            Validation = validation, Decisions = decisions, Measurements = aggregates
        };
        // No derived file is written until source, binaries, the full matrix and every output/sample pass.
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(evidence, Json) + "\n");
        var report = new StringBuilder($"# Public FilledArea integration\n\nGate: **{(evidence.Pass ? "PASS" : "FAIL")}**.\n\n");
        report.AppendLine("Times are medians of three process medians; observed ranges are not confidence intervals. Public-list is a correctness and timing reference, without a speed gate.\n");
        report.AppendLine("| Input | Fill | Route | Public array us | Hybrid v3 us | Winding us | Full Clipper us | Public/hybrid | Target | Preserve | Integrate |\n|---|---|---|---:|---:|---:|---:|---:|---|---|---|");
        foreach (var d in decisions) report.AppendLine(FormattableString.Invariant(
            $"| {d.Input} | {d.Rule} | {d.Selection.Backend} | {d.PublicArrayNs / 1000:F3} | {d.HybridV3Ns / 1000:F3} | {d.WindingNs / 1000:F3} | {d.ClipperNs / 1000:F3} | {d.PublicOverHybrid:F3} | {d.TargetPass} | {d.PreservationPass} | {d.IntegrationPass} |"));
        report.AppendLine("\n## Every method\n\n| Input | Fill | Method | us | Range us | B/op |\n|---|---|---|---:|---:|---:|");
        foreach (HybridAggregate row in aggregates) report.AppendLine(FormattableString.Invariant(
            $"| {row.Input} | {row.Rule} | {row.Method} | {row.MedianNs / 1000:F3} | {row.MinNs / 1000:F3}–{row.MaxNs / 1000:F3} | {row.MedianBytes:F0} |"));
        report.AppendLine("\n## Numerical findings\n\nNegative Clipper filled areas remain visible and cannot satisfy a targeted performance gate. Their cause is not isolated here.\n\n| Input | Fill | Winding | Clipper | Difference |\n|---|---|---:|---:|---:|");
        foreach (FilledAreaValidation row in validation.Where(v => !v.ClipperAreaNonnegative))
            report.AppendLine(FormattableString.Invariant($"| {row.Input} | {row.Rule} | {row.Winding:G17} | {row.Clipper:G17} | {row.ClipperMinusWinding:G17} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Public filled area: {evidence.RowsPerProcess} rows/process, {evidence.SampleCount} samples; gate={evidence.Pass}.");
    }
}
