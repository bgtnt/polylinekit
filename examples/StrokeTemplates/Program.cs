using PolylineKit;
using PolylineKit.Recognition;
using StrokeTemplates;

if (Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1")
    AppContext.SetSwitch("PolylineKit.DisableSimd", true);

try
{
    var options = ReplayOptions.Parse(args);
    if (options.Help) { Console.WriteLine(ReplayOptions.Usage); return 0; }
    if (options.Check) { ConsumerChecks.Run(); return 0; }
    ReplayInput input = options.Demo ? DemoFixtures.Create() : ReplayInput.Load(options);
    ReplayResult result = ReplayMatcher.Match(input, options.Method);
    if (options.Demo)
    {
        // A real input-to-ranking smoke check: fresh fixtures enter the same engine used by evaluation.
        if (result.TopClasses.Length != 3 || result.TopClasses[0].Template.SampleId != "demo-wave-a" ||
            result.TopClasses.Select(p => p.Template.Label).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidOperationException("The synthetic consumer wiring check failed.");
    }
    InspectionPage.Write(options.Output!, input, result, options.Contours);
    Console.WriteLine($"Query: {input.Query.SampleId}; dataset: {input.Query.Dataset}; method: {options.Method}; seed: {input.Bank.Seed}");
    Console.WriteLine($"Bank: {input.Bank.Split}; held-out writer: {input.Bank.HeldOutWriter ?? "unspecified"}; templates: {input.Templates.Length}");
    foreach (RankedTemplate item in result.TopClasses)
        Console.WriteLine($"{item.Template.Label}\t{item.Score:G17}\t{item.Template.SampleId}");
    Console.WriteLine($"Inspection: {Path.GetFullPath(options.Output!)}");
    if (options.Export is not null)
    {
        RecognitionFiles.Write(options.Export, input.Query);
        StrokeRecord roundTrip = RecognitionFiles.Read<StrokeRecord>(options.Export);
        if (roundTrip.SampleId != input.Query.SampleId || roundTrip.Strokes.Count != input.Query.Strokes.Count ||
            !roundTrip.GetPoints().SequenceEqual(input.Query.GetPoints()))
            throw new InvalidDataException("Exported query failed its round-trip check.");
        Console.WriteLine($"Query JSON: {Path.GetFullPath(options.Export)}");
    }
    return 0;
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or ArithmeticException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"StrokeTemplates: {error.Message}");
    return 1;
}
