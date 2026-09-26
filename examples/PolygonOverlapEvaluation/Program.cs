using System.Text.Json;
using PolygonOverlapEvaluation;

try
{
    string command = args.Length > 0 ? args[0] : "help";
    const string defaultInput = "examples/PolygonOverlapEvaluation/data/generated/solaris.json";
    switch (command)
    {
        case "verify-reference":
        {
            string input = args.Length > 1 ? args[1] : defaultInput;
            string output = args.Length > 2 ? args[2] : "artifacts/polygon-overlap/validation.json";
            object verification = Verification.Run(input);
            Corpus.Write(output, verification);
            Console.WriteLine(JsonSerializer.Serialize(verification, Corpus.Json));
            break;
        }
        case "run-example":
        {
            string input = args.Length > 1 ? args[1] : defaultInput;
            string backend = args.Length > 2 ? args[2] : "core";
            string output = args.Length > 3 ? args[3] : "artifacts/polygon-overlap/example.json";
            var prepared = new PreparedEvaluation(Corpus.Load(input), GeometryBackends.Create(backend));
            var result = prepared.Run();
            Corpus.Write(output, new { InputSHA256 = Corpus.Hash(input), Units = "square pixels", Result = result });
            Console.WriteLine($"{backend}: {result.Buildings.Length} truth rows, TP={result.Images.Sum(x => x.TruePos)}, FP={result.Images.Sum(x => x.FalsePos)}, FN={result.Images.Sum(x => x.FalseNeg)}, fallback pairs={result.FallbackPairs}. Output: {output}");
            break;
        }
        case "review-report":
        {
            if (args.Length > 4) throw new ArgumentException("review-report [input.json] [core|clipper|nts|convex] [output.html]");
            string input = args.Length > 1 ? args[1] : defaultInput;
            string backend = args.Length > 2 ? args[2] : "core";
            string output = args.Length > 3 ? args[3] : "artifacts/polygon-overlap/review.html";
            if (!string.Equals(Path.GetExtension(output), ".html", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Choose an .html output distinct from the input document.");
            using (var metadata = JsonDocument.Parse(File.ReadAllText(input)))
                if (metadata.RootElement.TryGetProperty("CoordinateSystem", out var coordinates) &&
                    (coordinates.ValueKind != JsonValueKind.String || coordinates.GetString() != "ImagePixelXY"))
                    throw new ArgumentException("The annotation review expects CoordinateSystem ImagePixelXY; no coordinate conversion is performed.");
            var prepared = new PreparedEvaluation(Corpus.Load(input), GeometryBackends.Create(backend));
            var result = prepared.Run();
            ReviewReport.Write(prepared, result, Corpus.Hash(input), output);
            Console.WriteLine($"{backend}: local review for {result.Images.Length} images. Output: {Path.GetFullPath(output)}");
            break;
        }
        case "benchmark":
        case "benchmark-smoke":
            if (args.Length != 5) throw new ArgumentException("benchmark <input.json> <output.json> <process-index 1..3> <source-commit>");
            Benchmarking.Run(args[1], args[2], int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), args[4], command == "benchmark-smoke");
            break;
        default:
            Console.WriteLine("prepare: python examples/PolygonOverlapEvaluation/data/prepare-data.py\nverify-reference [input.json] [output.json]\nrun-example [input.json] [core|clipper|nts|convex] [output.json]\nreview-report [input.json] [core|clipper|nts|convex] [output.html]\nbenchmark <input.json> <output.json> <process-index> <source-commit>\nsummarize: python examples/PolygonOverlapEvaluation/summarize.py <raw-directory>");
            return command == "help" ? 0 : 2;
    }
    return 0;
}
catch (Exception error)
{
    // Preserve a machine-readable diagnostic rather than inventing a partial complete result.
    Console.Error.WriteLine(JsonSerializer.Serialize(new { Error = error.GetType().Name, error.Message }));
    return 1;
}
