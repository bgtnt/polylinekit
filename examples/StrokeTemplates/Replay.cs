using PolylineKit;
using PolylineKit.Recognition;

namespace StrokeTemplates;

internal sealed class ReplayOptions
{
    public const string Usage = """
        StrokeTemplates: replay a supported open unistroke against a frozen template bank.

        --check
        --demo <output.html> [--method rms|area|combined|protractor|dtw] [--contours] [--export <sample.json>]
        --data <dataDir> --freeze <freeze.json> --sample <sampleId> [--dataset dollar|pendigits]
          [--seed 1729] [--method rms|area|combined|protractor|dtw] --output <output.html>
          [--contours] [--export <sample.json>]
        --data <dataDir> --freeze <freeze.json> --query <sample.json> [--dataset <name>]
          [--seed 1729] [--method <name>] --output <output.html> [--export <sample.json>]

        Standalone JSON uses the shared StrokeRecord schema, including supported=true and exactly
        one strokes array of ordered {x,y,t?} points. Export a demo or replayed query for an example.
        Standalone queries use the first frozen bank for their dataset/seed, ordered by held-out
        writer; the selected bank is printed. No new recognition configuration is inferred.
        Scores are distances, not confidence percentages. RMS/area overlays for native baselines
        are separate diagnostics. This tool does not measure matching latency.
        """;
    public bool Help { get; private set; }
    public bool Check { get; private set; }
    public bool Demo { get; private set; }
    public string? Data { get; private set; }
    public string? Freeze { get; private set; }
    public string? Sample { get; private set; }
    public string? Query { get; private set; }
    public string? Dataset { get; private set; }
    public string? Output { get; private set; }
    public string? Export { get; private set; }
    public string Method { get; private set; } = "rms";
    public int Seed { get; private set; } = 1729;
    public bool Contours { get; private set; }

    public void ValidateOutputPaths(string selectedDataset)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string output = Path.GetFullPath(Output ?? throw new ArgumentException("An HTML output path is required."));
        string? export = Export is null ? null : Path.GetFullPath(Export);
        if (export is not null && string.Equals(output, export, comparison))
            throw new ArgumentException("HTML output and query export must use different file paths.");
        var inputs = new List<string>();
        if (Freeze is not null) inputs.Add(Path.GetFullPath(Freeze));
        if (Query is not null) inputs.Add(Path.GetFullPath(Query));
        if (Data is not null) inputs.Add(Path.GetFullPath(Path.Combine(Data, selectedDataset + ".jsonl")));
        foreach (string input in inputs)
            if (string.Equals(output, input, comparison) || (export is not null && string.Equals(export, input, comparison)))
                throw new ArgumentException($"Output files must not overwrite a query, frozen configuration or dataset input: {input}");
    }

    public static ReplayOptions Parse(string[] args)
    {
        var result = new ReplayOptions();
        if (args.Length == 0) { result.Help = true; return result; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            if (!seen.Add(flag)) throw new ArgumentException($"Repeated option: {flag}");
            if (flag is "--help" or "-h") { result.Help = true; continue; }
            if (flag == "--check") { result.Check = true; continue; }
            if (flag == "--contours") { result.Contours = true; continue; }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {flag}.");
            string value = args[i];
            switch (flag)
            {
                case "--demo": result.Demo = true; result.Output = value; break;
                case "--data": result.Data = value; break;
                case "--freeze": result.Freeze = value; break;
                case "--sample": result.Sample = value; break;
                case "--query": result.Query = value; break;
                case "--dataset": result.Dataset = value; break;
                case "--output": result.Output = value; break;
                case "--export": result.Export = value; break;
                case "--method": result.Method = value; break;
                case "--seed":
                    if (!int.TryParse(value, out int seed)) throw new ArgumentException("Seed must be an integer.");
                    result.Seed = seed; break;
                default: throw new ArgumentException($"Unknown option: {flag}");
            }
        }
        if (result.Help) return result;
        if (result.Check)
        {
            if (args.Length != 1) throw new ArgumentException("--check is a standalone command.");
            return result;
        }
        if (!new[] { "rms", "area", "combined", "protractor", "dtw" }.Contains(result.Method, StringComparer.Ordinal))
            throw new ArgumentException("Unknown recognition method.");
        if (string.IsNullOrWhiteSpace(result.Output)) throw new ArgumentException("An HTML output path is required.");
        if (result.Demo)
        {
            if (result.Data is not null || result.Freeze is not null || result.Sample is not null || result.Query is not null)
                throw new ArgumentException("Demo fixtures cannot be mixed with dataset replay options.");
            if (result.Dataset is not null || seen.Contains("--seed") || seen.Contains("--output"))
                throw new ArgumentException("Demo uses its fixed synthetic dataset/seed and the output path immediately after --demo.");
        }
        else if (result.Data is null || result.Freeze is null || (result.Sample is null) == (result.Query is null))
            throw new ArgumentException("Replay requires --data, --freeze and exactly one of --sample or --query.");
        return result;
    }
}

internal sealed record ReplayInput(StrokeRecord Query, StrokeRecord[] Templates, RecognitionConfiguration Configuration,
    BankDefinition Bank, string SourceRevision, bool IsDemo, bool IsExternalQuery)
{
    public static ReplayInput Load(ReplayOptions options)
    {
        FrozenEvaluation freeze = RecognitionFiles.Read<FrozenEvaluation>(options.Freeze!);
        if (freeze.Protocol != RecognitionFiles.Protocol) throw new InvalidDataException("Unsupported frozen protocol.");
        if (freeze.Banks is null || freeze.Configurations is null || freeze.InputHashes is null ||
            freeze.Banks.Any(b => b is null || b.TemplateIds is null || b.QueryIds is null || string.IsNullOrWhiteSpace(b.Dataset)) ||
            freeze.Configurations.Any(c => c is null || string.IsNullOrWhiteSpace(c.Dataset)))
            throw new InvalidDataException("Frozen configuration is missing required bank, dataset or configuration fields.");
        StrokeRecord? external = options.Query is null ? null : RecognitionFiles.Read<StrokeRecord>(options.Query);
        if (external is not null && (string.IsNullOrWhiteSpace(external.SampleId) || string.IsNullOrWhiteSpace(external.Dataset) ||
            external.Strokes is null || external.Strokes.Any(stroke => stroke is null || stroke.Any(point => point is null))))
            throw new InvalidDataException("Standalone query requires sampleId, dataset and non-null ordered stroke points.");
        string? dataset = options.Dataset ?? external?.Dataset;
        BankDefinition[] banks = freeze.Banks.Where(b => b.Seed == options.Seed &&
            (dataset is null || b.Dataset == dataset) &&
            (external is not null || b.QueryIds.Contains(options.Sample!, StringComparer.Ordinal)))
            .OrderBy(b => b.Dataset, StringComparer.Ordinal).ThenBy(b => b.HeldOutWriter, StringComparer.Ordinal)
            .ThenBy(b => b.Split, StringComparer.Ordinal).ToArray();
        if (banks.Length == 0) throw new InvalidDataException("No matching frozen bank for the query, dataset and seed.");
        if (external is null && banks.Length != 1) throw new InvalidDataException("The sample identifies multiple banks; specify its dataset.");
        BankDefinition bank = banks[0];
        string dataPath = Path.Combine(options.Data!, bank.Dataset + ".jsonl");
        if (!freeze.InputHashes.TryGetValue(bank.Dataset, out string? expectedHash) ||
            !string.Equals(RecognitionFiles.Hash(dataPath), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected dataset does not match its frozen input hash.");
        StrokeRecord[] records = RecognitionFiles.Load(options.Data!, bank.Dataset);
        var byId = records.ToDictionary(p => p.SampleId, StringComparer.Ordinal);
        RecognitionSplits.ValidateBank(bank, byId);
        StrokeRecord query = external ?? (byId.TryGetValue(options.Sample!, out StrokeRecord? selected)
            ? selected : throw new InvalidDataException("Query sample is absent from the dataset."));
        if (query.Dataset != bank.Dataset) throw new InvalidDataException("Standalone query and bank datasets must match.");
        if (bank.TemplateIds.Contains(query.SampleId, StringComparer.Ordinal))
            throw new InvalidDataException("A query must not also be a template in its selected bank.");
        _ = query.GetPoints();
        RecognitionConfiguration[] configurations = freeze.Configurations.Where(c => c.Dataset == bank.Dataset).ToArray();
        if (configurations.Length != 1) throw new InvalidDataException("Expected one frozen configuration for the selected dataset.");
        RecognitionConfiguration configuration = configurations[0];
        if (configuration.TiltDegrees is not (0 or 15) || configuration.DtwWindow is not (8 or 16 or 63) ||
            !double.IsFinite(configuration.AreaWeight) || configuration.AreaWeight < 0 || configuration.AreaWeight > 1 ||
            !double.IsFinite(configuration.RmsScale) || configuration.RmsScale <= 0 ||
            !double.IsFinite(configuration.AreaScale) || configuration.AreaScale <= 0)
            throw new InvalidDataException("Frozen recognition settings violate the declared protocol.");
        if (!RecognitionFiles.Methods(bank.Dataset).Contains(options.Method, StringComparer.Ordinal))
            throw new ArgumentException("This method is not included in the selected dataset's frozen protocol.");
        return new ReplayInput(query, bank.TemplateIds.Select(id => byId[id]).ToArray(), configuration, bank,
            freeze.SourceRevision, false, external is not null);
    }
}

internal sealed record RankedTemplate(StrokeRecord Template, PreparedStroke Prepared, double Score);
internal sealed record ReplayResult(string Method, PreparedStroke Query, RankedTemplate[] TopClasses);

internal static class ReplayMatcher
{
    public static ReplayResult Match(ReplayInput input, string method)
    {
        RecognitionConfiguration config = input.Configuration;
        PreparedStroke query = RecognitionEngine.Prepare(input.Query.GetPoints());
        PreparedProtractor? nativeQuery = method == "protractor"
            ? RecognitionEngine.PrepareProtractor(input.Query.GetPoints(), config.ProtractorOrientationSensitive) : null;
        var matches = new List<RankedTemplate>(input.Templates.Length);
        foreach (StrokeRecord template in input.Templates)
        {
            PreparedStroke prepared = RecognitionEngine.Prepare(template.GetPoints());
            double score = method switch
            {
                "rms" => RecognitionEngine.ScoreRms(query, prepared, config.TiltDegrees),
                "area" => RecognitionEngine.ScoreArea(query, prepared, config.TiltDegrees),
                "combined" => RecognitionEngine.ScoreCombined(query, prepared, config.AreaWeight, config.RmsScale, config.AreaScale, config.TiltDegrees),
                "protractor" => RecognitionEngine.ScoreProtractor(nativeQuery!,
                    RecognitionEngine.PrepareProtractor(template.GetPoints(), config.ProtractorOrientationSensitive)),
                "dtw" => RecognitionEngine.ScoreDtw(query, prepared, config.DtwWindow),
                _ => throw new ArgumentException("Unknown recognition method.")
            };
            if (!double.IsFinite(score)) throw new InvalidDataException($"Nonfinite score for template {template.SampleId}.");
            matches.Add(new RankedTemplate(template, prepared, score));
        }
        matches.Sort((a, b) => RecognitionEngine.CompareRank(a.Score, a.Template.SampleId, b.Score, b.Template.SampleId));
        RankedTemplate[] top = matches.DistinctBy(m => m.Template.Label, StringComparer.Ordinal).Take(3).ToArray();
        if (top.Length == 0) throw new InvalidDataException("The selected template bank is empty.");
        return new ReplayResult(method, query, top);
    }
}

internal static class DemoFixtures
{
    public static ReplayInput Create()
    {
        // Fresh analytic shapes created for this consumer; no legacy/private template coordinates.
        Point2[] wave = Enumerable.Range(0, 33).Select(i => new Point2(i / 32.0, .25 * Math.Sin(2 * Math.PI * i / 32))).ToArray();
        Point2[] arch = Enumerable.Range(0, 33).Select(i => new Point2(i / 32.0, .4 * Math.Sin(Math.PI * i / 32))).ToArray();
        Point2[] corner = [new(0, 0), new(.2, 0), new(.2, .7), new(1, .7)];
        StrokeRecord Record(string id, string label, Point2[] points) => new()
        {
            SampleId = id, Dataset = "synthetic", Split = "demo", Label = label, Supported = true,
            Strokes = [points.Select(p => new RecordedPoint { X = p.X, Y = p.Y }).ToList()]
        };
        StrokeRecord[] templates = [Record("demo-wave-b", "wave", wave), Record("demo-corner", "corner", corner),
            Record("demo-arch", "arch", arch), Record("demo-wave-a", "wave", wave)];
        StrokeRecord query = Record("demo-query", "wave", AffineTransform2D.Scaling(3).Then(AffineTransform2D.Translation(8, -5)).Apply(wave));
        return new ReplayInput(query, templates, new RecognitionConfiguration("synthetic", 0, .5, 1, 1, true, 8),
            new BankDefinition("synthetic", "demo", null, 1729, templates.Select(t => t.SampleId).ToArray(), [query.SampleId]),
            RecognitionEngine.ImplementationId, true, false);
    }
}
