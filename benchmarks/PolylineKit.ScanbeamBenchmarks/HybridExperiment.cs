using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

internal static partial class Program
{
    private sealed record HybridCase(Method[] Methods, HybridClosedArea Hybrid, HybridClosedAreaV2 HybridV2,
        GuardedDoubleSweep Double);
    private sealed record HybridValidationRow(string Input, string Family, string Split, string InputHash,
        int Vertices, PathFillRule Rule, HybridSelection Selection, HybridSelection SelectionV2,
        double Winding, double Hybrid, double HybridV2,
        double? Integer, double Double, bool DoubleFallback, string? DoubleReason, double? DoubleErrorBound,
        long DoubleWork, int DoubleBands, long DoubleEvents, double Clipper,
        double ClipperMinusWinding, double ClipperRelativeError, bool ClipperAreaNonnegative);
    private sealed record HybridRow(string Input, string Family, string Split, string InputHash,
        int Vertices, PathFillRule Rule, string Method, double Value, double MedianNs, double MedianBytes, Sample[] Samples);
    private sealed record HybridRun(int Run, string Revision, DateTimeOffset Utc, string Runtime,
        string OS, string Architecture, string? Cpu, int LogicalProcessors, string? TieredCompilation,
        string HarnessHash, string WindingHash, string ClipperHash, HybridRow[] Rows);
    private sealed record HybridAggregate(string Input, string Family, string Split, string InputHash,
        int Vertices, PathFillRule Rule, string Method, double Value, double MedianNs,
        double MinNs, double MaxNs, double MedianBytes);

    private static bool IntegerAdmissible(Point2[] points) => points.Length is >= 3 and <= 8192 &&
        points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= 524288 &&
            Math.Abs(p.Y) <= 524288 && p.X == Math.Truncate(p.X) && p.Y == Math.Truncate(p.Y));

    private static HybridCase HybridMethods(Point2[] points, PathFillRule rule)
    {
        var hybrid = new HybridClosedArea();
        var hybridV2 = new HybridClosedAreaV2();
        var guarded = new GuardedDoubleSweep(restrictToCommonY: true, cacheEndpointX: true,
            scalarOrderFilter: true, optimizeAreaArithmetic: true, coalesceGaps: true,
            optimizeActivePasses: true, filterBeforeSupport: true);
        var integers = new IntegerScanbeam();
        var full = new Clipper64();
        var preloaded = new Clipper64();
        preloaded.AddSubject(Convert(points, Scale));
        var fullOutput = new Paths64(); var fullOpen = new Paths64();
        var preloadedOutput = new Paths64(); var preloadedOpen = new Paths64();
        FillRule fill = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
        var methods = new List<Method>
        {
            new("Winding", () => HybridClosedArea.SelectedWinding(points, rule)),
            new("Hybrid", () => hybrid.Measure(points, rule)),
            new("Hybrid-v2", () => hybridV2.Measure(points, rule)),
            new("Double-sweep", () => guarded.MeasureClosedPath(points, rule)),
            new("Clipper-full-input", () =>
            {
                // Fresh coordinate conversion and Add belong to this complete raw-input operation.
                // Engines and output containers remain warm for every implementation.
                full.Clear(); full.AddSubject(Convert(points, Scale));
                return Execute(full, fill, fullOutput, fullOpen, Scale);
            }),
            new("Clipper-preloaded", () => Execute(preloaded, fill, preloadedOutput, preloadedOpen, Scale))
        };
        if (IntegerAdmissible(points)) methods.Add(new("Integer-sweep", () => integers.Measure(points, rule)));
        methods.Add(new("Selector-only", () =>
        {
            HybridSelection selected = HybridClosedArea.Select(points);
            return selected.SuppliedVertices + selected.ActiveBandSpans + selected.SampleCrossings;
        }));
        methods.Add(new("Selector-v2", () =>
        {
            HybridSelection selected = HybridClosedAreaV2.Select(points);
            return selected.SuppliedVertices + selected.ActiveBandSpans + selected.SampleCrossings;
        }));
        return new(methods.ToArray(), hybrid, hybridV2, guarded);
    }

    private static HybridValidationRow[] ValidateHybrid(string? directory)
    {
        HybridInput[] inputs = HybridInputs.Create();
        Require(inputs.Length == 34 && inputs.Count(p => p.Split == "development") == 9 &&
            inputs.Count(p => p.Split == "held-out") == 13 && inputs.Count(p => p.Split == "confirmation") == 6 &&
            inputs.Count(p => p.Split == "confirmation-v3") == 6 &&
            inputs.Select(p => p.Name).Distinct().Count() == inputs.Length, "Hybrid fixture matrix changed.");
        var rows = new List<HybridValidationRow>();
        foreach (HybridInput input in inputs)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            string originalHash = input.Hash;
            HybridCase test = HybridMethods(input.Points, rule);
            var values = test.Methods.ToDictionary(m => m.Name, m => m.Invoke());
            double winding = values["Winding"], hybrid = values["Hybrid"], guarded = values["Double-sweep"];
            HybridSelection selection = test.Hybrid.LastSelection;
            HybridSelection selectionV2 = test.HybridV2.LastSelection;
            double? integer = values.TryGetValue("Integer-sweep", out double value) ? value : null;
            Near(winding, hybrid, input.Name + "/hybrid");
            Near(winding, guarded, input.Name + "/double");
            if (integer.HasValue) Near(winding, integer.Value, input.Name + "/integer");
            double chosen = selection.Backend == "IntegerScanbeam" ? integer!.Value : winding;
            Require(Bits(hybrid) == Bits(chosen), "Hybrid did not return exactly its selected backend's area.");
            Require(selection == HybridClosedArea.Select(input.Points), "Selector changed during operation.");
            Require(selectionV2 == HybridClosedAreaV2.Select(input.Points) && selection == selectionV2,
                "Optimized selector changed the retained v2 selection or diagnostics.");
            Require(Bits(hybrid) == Bits(values["Hybrid-v2"]), "Optimized hybrid changed the retained v2 output.");
            Require(!test.Double.LastUsedFallback || Bits(guarded) == Bits(winding), "Closed fallback changed operation.");
            Require(test.Double.LastUsedFallback || (double.IsFinite(test.Double.LastErrorBound) &&
                test.Double.LastErrorBound >= 0 && test.Double.LastErrorBound <= Math.Min(.25, 1e-10 * Math.Abs(guarded))),
                "Certified closed-area error bound violates its budget.");
            Require(Bits(values["Clipper-full-input"]) == Bits(values["Clipper-preloaded"]), "Clipper preparation changed area.");
            foreach (Method method in test.Methods)
            {
                Require(double.IsFinite(values[method.Name]) &&
                    (method.Name.StartsWith("Clipper-", StringComparison.Ordinal) || values[method.Name] >= 0),
                    input.Name + "/" + method.Name + ": invalid area or selector sink.");
                Require(Bits(values[method.Name]) == Bits(method.Invoke()), "Repeated hybrid workload changed output.");
            }
            Require(input.Hash == originalHash, "Hybrid workload mutated caller coordinates.");
            double clipper = values["Clipper-full-input"];
            rows.Add(new(input.Name, input.Family, input.Split, originalHash, input.Points.Length, rule,
                selection, selectionV2, winding, hybrid, values["Hybrid-v2"], integer, guarded,
                test.Double.LastUsedFallback, test.Double.LastFallbackReason,
                test.Double.LastUsedFallback ? null : test.Double.LastErrorBound,
                test.Double.WorkCount, test.Double.BandCount, test.Double.EventCount, clipper,
                clipper - winding, winding == 0 ? Math.Abs(clipper) : Math.Abs((clipper - winding) / winding), clipper >= 0));
        }
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(rows, Json) + "\n");
            File.WriteAllText(Path.Combine(directory, "inputs.json"), HybridInputsJson(inputs));
        }
        Console.WriteLine($"Hybrid closed fills: {rows.Count} cases, {rows.Count(r => r.Selection.Backend == "IntegerScanbeam")} integer routes; " +
            $"forced double {rows.Count(r => !r.DoubleFallback)} certified / {rows.Count(r => r.DoubleFallback)} fallback.");
        return rows.ToArray();
    }

    private static string HybridInputsJson(HybridInput[] inputs) => JsonSerializer.Serialize(inputs.Select(p =>
        new { p.Name, p.Family, p.Split, p.Hash, p.Points }), Json) + "\n";

    private static void RunHybrid(string directory, int run, string revision)
    {
        VerifyHybridRevision(revision);
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0",
            "Use run1..3 with DOTNET_TieredCompilation=0.");
        ValidateHybrid(directory);
        var rows = new List<HybridRow>();
        foreach (HybridInput input in HybridInputs.Create())
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            HybridCase test = HybridMethods(input.Points, rule);
            int offset = (run - 1) * 2 % test.Methods.Length;
            foreach (Method method in test.Methods.Skip(offset).Concat(test.Methods.Take(offset)))
            {
                double value = method.Invoke();
                Sample[] samples = Measure(method.Invoke);
                Require(Bits(value) == Bits(method.Invoke()), "Timed hybrid result changed.");
                rows.Add(new(input.Name, input.Family, input.Split, input.Hash, input.Points.Length, rule,
                    method.Name, value, Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
            }
            Console.WriteLine($"hybrid run{run}: {input.Name}/{rule}");
        }
        var data = new HybridRun(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), AssemblyHash(typeof(Program)),
            AssemblyHash(typeof(WindingArea)), AssemblyHash(typeof(Clipper64)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(data, Json) + "\n");
        GC.KeepAlive(sink);
    }

    private static void SummarizeHybrid(string directory)
    {
        string[] paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, $"run-{i}.json")).ToArray();
        HybridRun[] runs = paths.Select(p => JsonSerializer.Deserialize<HybridRun>(File.ReadAllText(p))!).ToArray();
        VerifyHybridRevision(runs[0].Revision);
        string Identity(HybridRun run) => JsonSerializer.Serialize(run with { Run = 0, Utc = default, Rows = [] });
        string Key(string input, PathFillRule rule, string method) => $"{input}|{rule}|{method}";
        HybridInput[] inputs = HybridInputs.Create();
        HybridValidationRow[] validation = ValidateHybrid(null);
        Require(File.ReadAllText(Path.Combine(directory, "validation.json")) == JsonSerializer.Serialize(validation, Json) + "\n",
            "Hybrid validation differs from recomputation.");
        Require(File.ReadAllText(Path.Combine(directory, "inputs.json")) == HybridInputsJson(inputs), "Hybrid input manifest differs.");
        var outputs = new Dictionary<string, double>();
        var matrix = new List<(HybridInput Input, PathFillRule Rule, Method[] Methods)>();
        foreach (HybridInput input in inputs)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Method[] methods = HybridMethods(input.Points, rule).Methods;
            matrix.Add((input, rule, methods));
            foreach (Method method in methods) outputs.Add(Key(input.Name, rule, method.Name), method.Invoke());
        }
        for (int index = 0; index < runs.Length; index++)
        {
            HybridRun run = runs[index];
            Require(run.Run == index + 1 && Identity(run) == Identity(runs[0]) && run.TieredCompilation == "0",
                "Hybrid run identity differs.");
            Require(run.HarnessHash == AssemblyHash(typeof(Program)) && run.WindingHash == AssemblyHash(typeof(WindingArea)) &&
                run.ClipperHash == AssemblyHash(typeof(Clipper64)), "Use the frozen measured binaries to summarize hybrid evidence.");
            string[] order = matrix.SelectMany(x =>
            {
                int offset = index * 2 % x.Methods.Length;
                return x.Methods.Skip(offset).Concat(x.Methods.Take(offset)).Select(m => Key(x.Input.Name, x.Rule, m.Name));
            }).ToArray();
            Require(run.Rows.Select(r => Key(r.Input, r.Rule, r.Method)).SequenceEqual(order), "Hybrid row order/matrix differs.");
            foreach (HybridRow row in run.Rows)
            {
                HybridInput input = inputs.Single(p => p.Name == row.Input);
                Require(row.Family == input.Family && row.Split == input.Split && row.InputHash == input.Hash &&
                    row.Vertices == input.Points.Length, "Hybrid input identity differs.");
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) &&
                    s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid hybrid samples.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) &&
                    row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Hybrid sample median differs.");
                Require(Bits(row.Value) == Bits(outputs[Key(row.Input, row.Rule, row.Method)]), "Hybrid recorded output differs.");
            }
        }
        HybridAggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(r => Key(r.Input, r.Rule, r.Method)).Select(g =>
        {
            HybridRow first = g.First();
            Require(g.Count() == 3 && g.All(r => Bits(r.Value) == Bits(first.Value)), "Hybrid cross-process output differs.");
            return new HybridAggregate(first.Input, first.Family, first.Split, first.InputHash, first.Vertices, first.Rule,
                first.Method, first.Value, Median(g.Select(r => r.MedianNs)), g.Min(r => r.MedianNs),
                g.Max(r => r.MedianNs), Median(g.Select(r => r.MedianBytes)));
        }).ToArray();
        var decisions = aggregates.GroupBy(r => (r.Input, r.Rule)).Select(group =>
        {
            HybridAggregate Get(string method) => group.Single(r => r.Method == method);
            var reference = validation.Single(v => v.Input == group.Key.Input && v.Rule == group.Key.Rule);
            double winding = Get("Winding").MedianNs, hybrid = Get("Hybrid").MedianNs,
                hybridV2 = Get("Hybrid-v2").MedianNs, selectorV2 = Get("Selector-v2").MedianNs,
                clipper = Get("Clipper-full-input").MedianNs, preloaded = Get("Clipper-preloaded").MedianNs,
                selector = Get("Selector-only").MedianNs;
            string backendMethod = reference.Selection.Backend == "IntegerScanbeam" ? "Integer-sweep" : "Winding";
            double chosen = Get(backendMethod).MedianNs;
            double bestAvailable = group.Where(r => r.Method is "Winding" or "Double-sweep" or "Integer-sweep").Min(r => r.MedianNs);
            bool target = reference.Family is "dense-grid" or "retraced";
            return new { group.Key.Input, group.Key.Rule, reference.Family, reference.Split, reference.Selection,
                Target = target, WindingNs = winding, HybridNs = hybrid, ClipperNs = clipper, PreloadedClipperNs = preloaded,
                HybridV2Ns = hybridV2, SelectorV2Ns = selectorV2,
                SelectorNs = selector, ChosenBackendNs = chosen, BestAvailableBackendNs = bestAvailable,
                HybridOverV2 = hybrid / hybridV2, SelectorOverV2 = selector / selectorV2,
                HybridOverWinding = hybrid / winding, HybridOverChosen = hybrid / chosen,
                HybridOverBestAvailable = hybrid / bestAvailable, HybridOverClipper = hybrid / clipper,
                HybridOverPreloadedClipper = hybrid / preloaded,
                TargetPass = !target || (reference.ClipperAreaNonnegative && hybrid <= .8 * winding && hybrid <= .8 * clipper),
                PreservationPass = target || hybrid <= 1.10 * winding };
        }).ToArray();
        var evidence = new
        {
            Protocol = "benchmarks/PolylineKit.ScanbeamBenchmarks/HYBRID-V3-PROTOCOL.md",
            Environment = runs[0] with { Rows = [] }, RowsPerProcess = runs[0].Rows.Length,
            SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)),
            Pass = decisions.All(d => d.TargetPass && d.PreservationPass),
            RawFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }),
            ValidationSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "validation.json"))),
            InputsSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "inputs.json"))),
            Validation = validation, Decisions = decisions, Measurements = aggregates
        };
        // Write only after every submitted row, metadata field, output and diagnostic has validated.
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(evidence, Json) + "\n");
        var report = new StringBuilder($"# Adaptive closed-area experiment\n\nGate: **{(evidence.Pass ? "PASS" : "FAIL")}**.\n\n");
        report.AppendLine("Times are medians of three process medians; ranges are observed, not confidence intervals.\n");
        report.AppendLine("Hybrid-v2 and Selector-v2 retain the previous selector in this same binary. Every result and selection diagnostic must match the optimized variants exactly.\n");
        report.AppendLine("| Input | Fill | Route | Winding us | Hybrid us | Full Clipper us | Preloaded Clipper us | Selector us | Hybrid/chosen | Hybrid/best | Gate |\n|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var d in decisions) report.AppendLine(FormattableString.Invariant(
            $"| {d.Input} | {d.Rule} | {d.Selection.Backend} | {d.WindingNs / 1000:F3} | {d.HybridNs / 1000:F3} | {d.ClipperNs / 1000:F3} | {d.PreloadedClipperNs / 1000:F3} | {d.SelectorNs / 1000:F3} | {d.HybridOverChosen:F3} | {d.HybridOverBestAvailable:F3} | {d.TargetPass && d.PreservationPass} |"));
        report.AppendLine("\n## Same-binary selector comparison\n\n| Input | Fill | Hybrid v2 us | Hybrid v3 us | v3/v2 | Selector v2 us | Selector v3 us | v3/v2 |\n|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var d in decisions) report.AppendLine(FormattableString.Invariant(
            $"| {d.Input} | {d.Rule} | {d.HybridV2Ns / 1000:F3} | {d.HybridNs / 1000:F3} | {d.HybridOverV2:F3} | {d.SelectorV2Ns / 1000:F3} | {d.SelectorNs / 1000:F3} | {d.SelectorOverV2:F3} |"));
        report.AppendLine("\n## Every forced method\n\n| Input | Fill | Method | us | Range us | B/op |\n|---|---|---|---:|---:|---:|");
        foreach (HybridAggregate row in aggregates) report.AppendLine(FormattableString.Invariant(
            $"| {row.Input} | {row.Rule} | {row.Method} | {row.MedianNs / 1000:F3} | {row.MinNs / 1000:F3}–{row.MaxNs / 1000:F3} | {row.MedianBytes:F0} |"));
        report.AppendLine("\n## Numerical findings\n\nThe following Clipper outputs are negative filled areas and must not be read as valid area comparisons. Values are retained without clamping; their cause is not isolated by this experiment.\n\n| Input | Fill | Winding | Clipper | Clipper minus Winding |\n|---|---|---:|---:|---:|");
        foreach (HybridValidationRow row in validation.Where(v => !v.ClipperAreaNonnegative))
            report.AppendLine(FormattableString.Invariant($"| {row.Input} | {row.Rule} | {row.Winding:G17} | {row.Clipper:G17} | {row.ClipperMinusWinding:G17} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Hybrid: {evidence.RowsPerProcess} rows/process, {evidence.SampleCount} samples; gate={evidence.Pass}.");
    }

    private static void VerifyHybridRevision(string revision)
    {
        string? version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Require(revision.Length == 40 && revision.All(Uri.IsHexDigit) &&
            version is not null && version.Contains(revision, StringComparison.OrdinalIgnoreCase),
            "Requested source revision must match the measured binary's embedded commit.");
    }
}
