using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PolylineKit;
using PolylineKit.Recognition;

namespace StrokeTemplates;

internal static class ConsumerChecks
{
    public static void Run()
    {
        int passed = 0;
        void True(string name, bool condition)
        { if (!condition) throw new InvalidOperationException("Consumer check: " + name); passed++; }
        void Reject(string name, Action action)
        {
            try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException)
            { passed++; return; }
            throw new InvalidOperationException("Consumer accepted: " + name);
        }
        ReplayInput demo = DemoFixtures.Create();
        foreach (string method in new[] { "rms", "area", "combined", "protractor", "dtw" })
        {
            ReplayResult result = ReplayMatcher.Match(demo, method);
            True(method + " top-three distinct classes", result.TopClasses.Length == 3 &&
                result.TopClasses.Select(t => t.Template.Label).Distinct(StringComparer.Ordinal).Count() == 3);
            True(method + " winning label", result.TopClasses[0].Template.Label == "wave");
            True(method + " exact ordinal template tie", result.TopClasses[0].Template.SampleId == "demo-wave-a");
            RankedTemplate winner = result.TopClasses[0];
            RecognitionConfiguration c = demo.Configuration;
            double directScore = method switch
            {
                "rms" => RecognitionEngine.ScoreRms(result.Query, winner.Prepared, c.TiltDegrees),
                "area" => RecognitionEngine.ScoreArea(result.Query, winner.Prepared, c.TiltDegrees),
                "combined" => RecognitionEngine.ScoreCombined(result.Query, winner.Prepared, c.AreaWeight, c.RmsScale, c.AreaScale, c.TiltDegrees),
                "protractor" => RecognitionEngine.ScoreProtractor(RecognitionEngine.PrepareProtractor(demo.Query.GetPoints(), c.ProtractorOrientationSensitive),
                    RecognitionEngine.PrepareProtractor(winner.Template.GetPoints(), c.ProtractorOrientationSensitive)),
                _ => RecognitionEngine.ScoreDtw(result.Query, winner.Prepared, c.DtwWindow)
            };
            True(method + " uses the real scorer unchanged", BitConverter.DoubleToInt64Bits(directScore) == BitConverter.DoubleToInt64Bits(winner.Score));
        }

        StrokeRecord rotated = Copy(demo.Query);
        Point2[] rotatedRaw = AffineTransform2D.Rotation(7 * Math.PI / 180).Apply(rotated.GetPoints());
        rotated.Strokes = [rotatedRaw.Select(p => new RecordedPoint { X = p.X, Y = p.Y }).ToList()];
        ReplayInput tilted = demo with { Query = rotated, Configuration = demo.Configuration with { TiltDegrees = 15 } };
        ReplayResult fitted = ReplayMatcher.Match(tilted, "rms");
        PairScores diagnostics = RecognitionEngine.ScorePair(fitted.Query, fitted.TopClasses[0].Prepared, 15);
        True("bounded tilt uses a nonzero actual engine rotation", diagnostics.RotationDegrees != 0);
        Point2[] composed = fitted.Query.NormalizationTransform.Then(diagnostics.Transform).Apply(fitted.Query.Original);
        Point2[] sequential = diagnostics.Transform.Apply(fitted.Query.NormalizationTransform.Apply(fitted.Query.Original));
        for (int i = 0; i < composed.Length; i++)
            True("raw overlay map agrees with sequential preparation", Math.Abs(composed[i].X - sequential[i].X) < 1e-12 && Math.Abs(composed[i].Y - sequential[i].Y) < 1e-12);
        Point2[] samples = diagnostics.Transform.Apply(fitted.Query.Samples64);
        True("displayed 64-point area equals engine diagnostic", diagnostics.Area ==
            PolylineComparison.EndpointBridgedArea(fitted.TopClasses[0].Prepared.Samples64, samples, PathFillRule.NonZero, 6).RawArea);

        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string directory = Path.GetFullPath(Path.Combine(temporaryRoot, "polylinekit-consumer-check-" + Guid.NewGuid().ToString("N")));
        if (!directory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid temporary check directory.");
        Directory.CreateDirectory(directory);
        try
        {
            // Synthetic records exercise the frozen-file schema. They are not official dataset samples.
            StrokeRecord query = Copy(demo.Query);
            query.Dataset = "pendigits"; query.Split = "test"; query.SampleId = "synthetic-consumer-query"; query.Label = "0";
            for (int i = 0; i < query.Strokes[0].Count; i++) query.Strokes[0][i].T = i * 10;
            var templates = new List<StrokeRecord>();
            for (int label = 0; label < 10; label++)
            for (int variation = 0; variation < 5; variation++)
            {
                StrokeRecord template = Copy(query);
                template.SampleId = $"synthetic-consumer-template-{label}-{variation}";
                template.Label = label.ToString(System.Globalization.CultureInfo.InvariantCulture); template.Split = "train";
                templates.Add(template);
            }
            string dataPath = Path.Combine(directory, "pendigits.jsonl");
            File.WriteAllLines(dataPath, templates.Append(query).Select(r => JsonSerializer.Serialize(r, RecognitionFiles.Json)), new UTF8Encoding(false));
            var bank = new BankDefinition("pendigits", "test", null, 1729, templates.Select(t => t.SampleId).ToArray(), [query.SampleId]);
            var freeze = new FrozenEvaluation(RecognitionFiles.Protocol, "synthetic-consumer-check",
                new Dictionary<string, string> { ["pendigits"] = RecognitionFiles.Hash(dataPath) },
                [new RecognitionConfiguration("pendigits", 0, .5, 1, 1, true, 8)], [bank]);
            string frozenPath = Path.Combine(directory, "freeze.json"), queryPath = Path.Combine(directory, "query.json");
            RecognitionFiles.Write(frozenPath, freeze); RecognitionFiles.Write(queryPath, query);
            StrokeRecord roundTrip = RecognitionFiles.Read<StrokeRecord>(queryPath);
            True("query JSON round-trip includes time and provenance", JsonSerializer.Serialize(roundTrip, RecognitionFiles.Json) == JsonSerializer.Serialize(query, RecognitionFiles.Json));
            string output = Path.Combine(directory, "inspection.html");
            string[] ReplayArgs(params string[] tail) => ["--data", directory, "--freeze", frozenPath, "--output", output, .. tail];
            ReplayOptions.Parse(ReplayArgs("--sample", query.SampleId)).ValidateOutputPaths("pendigits");
            True("distinct output and inputs are accepted", true);
            foreach (string inputPath in new[] { frozenPath, queryPath, dataPath })
            {
                string equivalentInput = Path.Combine(Path.GetDirectoryName(inputPath)!, ".", Path.GetFileName(inputPath));
                Reject("HTML cannot overwrite input " + Path.GetFileName(inputPath), () => ReplayOptions.Parse(
                    ["--data", directory, "--freeze", frozenPath, "--query", queryPath, "--output", equivalentInput]).ValidateOutputPaths("pendigits"));
                Reject("export cannot overwrite input " + Path.GetFileName(inputPath), () => ReplayOptions.Parse(
                    ReplayArgs("--query", queryPath, "--export", equivalentInput)).ValidateOutputPaths("pendigits"));
            }
            Reject("HTML and export cannot alias", () => ReplayOptions.Parse(
                ReplayArgs("--query", queryPath, "--export", Path.Combine(directory, ".", "inspection.html"))).ValidateOutputPaths("pendigits"));
            Reject("demo HTML and export cannot alias", () => ReplayOptions.Parse(
                ["--demo", output, "--export", Path.Combine(directory, ".", "inspection.html")]).ValidateOutputPaths("synthetic"));
            ReplayOptions caseVariant = ReplayOptions.Parse(
                ["--data", directory, "--freeze", frozenPath, "--query", queryPath, "--output", queryPath.ToUpperInvariant()]);
            if (OperatingSystem.IsWindows()) Reject("Windows path matching ignores case", () => caseVariant.ValidateOutputPaths("pendigits"));
            else { caseVariant.ValidateOutputPaths("pendigits"); True("Unix path matching preserves case", true); }
            ReplayInput loaded = ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--sample", query.SampleId)));
            True("dataset-ID replay uses the frozen bank", loaded.Templates.Length == 50 && loaded.Query.SampleId == query.SampleId);
            ReplayResult replay = ReplayMatcher.Match(loaded, "rms");
            True("all-tie bank ranks canonical ID", replay.TopClasses[0].Template.SampleId == "synthetic-consumer-template-0-0");
            True("all-tie bank shows distinct labels", replay.TopClasses.Select(t => t.Template.Label).SequenceEqual(new[] { "0", "1", "2" }));
            ReplayInput imported = ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--query", queryPath)));
            True("standalone JSON replay identified", imported.IsExternalQuery && imported.Query.SampleId == query.SampleId);
            True("standalone replay uses the same bank", imported.Bank.TemplateIds.SequenceEqual(bank.TemplateIds));

            const string unsafeLabel = "<script>alert(\"label\")</script>&";
            foreach (StrokeRecord template in loaded.Templates.Where(t => t.Label == "0")) template.Label = unsafeLabel;
            ReplayResult escaping = ReplayMatcher.Match(loaded, "rms");
            InspectionPage.Write(output, loaded, escaping, true);
            string html = File.ReadAllText(output);
            True("HTML escapes untrusted labels", html.Contains(WebUtility.HtmlEncode(unsafeLabel), StringComparison.Ordinal) && !html.Contains(unsafeLabel, StringComparison.Ordinal));
            True("HTML avoids external assets", !html.Contains("<script", StringComparison.OrdinalIgnoreCase) && !html.Contains("src=", StringComparison.OrdinalIgnoreCase));
            MatchCollection paths = Regex.Matches(html, "<polyline\\b[^>]*\\bpoints=\"([^\"]*)\"");
            True("top-three overlays contain four paths each", paths.Count == 12);
            for (int card = 0; card < 3; card++)
            for (int line = 2; line < 4; line++)
                True("solid overlay contains the actual 64 samples", paths[card * 4 + line].Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 64);
            InspectionPage.Write(output, loaded, ReplayMatcher.Match(loaded, "protractor"), false);
            True("native baseline overlay is labeled as separate diagnostics", File.ReadAllText(output).Contains("separate 64-point diagnostics", StringComparison.Ordinal));

            var changedHashes = new Dictionary<string, string>(freeze.InputHashes) { ["pendigits"] = new string('0', 64) };
            RecognitionFiles.Write(frozenPath, freeze with { InputHashes = changedHashes });
            Reject("frozen data hash mismatch", () => ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--sample", query.SampleId))));
            RecognitionFiles.Write(frozenPath, freeze);
            StrokeRecord unsupported = Copy(query); unsupported.Strokes.Add(unsupported.Strokes[0]);
            RecognitionFiles.Write(queryPath, unsupported);
            Reject("multistroke input", () => ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--query", queryPath))));
            unsupported = Copy(query); unsupported.Supported = false; RecognitionFiles.Write(queryPath, unsupported);
            Reject("unsupported query", () => ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--query", queryPath))));
            unsupported = Copy(query); unsupported.SampleId = bank.TemplateIds[0]; RecognitionFiles.Write(queryPath, unsupported);
            Reject("query-template ID leakage", () => ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--query", queryPath))));
            RecognitionFiles.Write(frozenPath, freeze with { Configurations = [freeze.Configurations[0] with { RmsScale = 0 }] });
            Reject("invalid frozen settings", () => ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--sample", query.SampleId))));
            RecognitionFiles.Write(frozenPath, new { protocol = RecognitionFiles.Protocol });
            Reject("incomplete frozen JSON", () => ReplayInput.Load(ReplayOptions.Parse(ReplayArgs("--sample", query.SampleId))));
        }
        finally
        {
            // Only this unique, newly created child of the resolved temporary root is removed.
            if (directory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(directory).StartsWith("polylinekit-consumer-check-", StringComparison.Ordinal))
                Directory.Delete(directory, recursive: true);
        }
        Reject("unknown CLI method", () => ReplayOptions.Parse(["--demo", "unused.html", "--method", "unknown"]));
        Reject("mixed demo and data", () => ReplayOptions.Parse(["--demo", "unused.html", "--data", "unused"]));
        Reject("missing query selector", () => ReplayOptions.Parse(["--data", "unused", "--freeze", "unused", "--output", "unused"]));
        Reject("ambiguous query selector", () => ReplayOptions.Parse(["--data", "unused", "--freeze", "unused", "--sample", "q", "--query", "q.json", "--output", "unused"]));
        Reject("repeated CLI option", () => ReplayOptions.Parse(["--demo", "unused.html", "--method", "rms", "--method", "area"]));
        Reject("check mixed with replay", () => ReplayOptions.Parse(["--check", "--demo", "unused.html"]));
        Console.WriteLine($"PASS: {passed} consumer wiring, schema, transformation, rendering and rejection checks.");
    }

    private static StrokeRecord Copy(StrokeRecord record) => JsonSerializer.Deserialize<StrokeRecord>(
        JsonSerializer.Serialize(record, RecognitionFiles.Json), RecognitionFiles.Json)!;
}
