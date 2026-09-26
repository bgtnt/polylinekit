using System.Text.Json;

namespace PolygonOverlapEvaluation;

internal sealed record ReviewReportPayload(int SchemaVersion, string InputSHA256, string SourceCommit,
    string Backend, double MinArea, double Threshold, ReviewImage[] Images,
    int FallbackPairs, int EligibleTruth, int EligiblePredictions);
internal sealed record ReviewImage(string ImageId, int TruePos, int FalsePos, int FalseNeg,
    double Precision, double Recall, double F1Score, double[] Bounds, ReviewTruth[] Truth,
    ReviewPrediction[] Predictions, ExcludedInput[] ExcludedInputs);
internal sealed record ReviewTruth(int Index, int BuildingId, string Status, double RetainedIoU,
    int? MatchedPredictionId, double Area, double[][][][] Polygons);
internal sealed record ReviewPrediction(int Index, int BuildingId, string Status, double? Confidence,
    int? BestTruthBuildingId, double BestIoU, double AssignedScore, double Area,
    PairScore? Pair, double[][][][] Polygons);

/// <summary>Projects an existing evaluation into a local visual review; performs no geometry operations.</summary>
internal static class ReviewReport
{
    private const string Marker = "/*__REPORT_DATA__*/null";

    internal static ReviewReportPayload Create(PreparedEvaluation prepared, EvaluationResult result, string inputHash)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(inputHash);
        if (!result.Complete || result.Backend != prepared.Backend.Name)
            throw new ArgumentException("Review needs a complete result from the supplied prepared backend.", nameof(result));
        Corpus corpus = prepared.Corpus;
        var buildings = result.Buildings.ToDictionary(b => (b.ImageId, b.BuildingId));
        var predictions = result.Predictions.ToDictionary(p => (p.ImageId, p.BuildingId));
        var pairs = result.Pairs.ToDictionary(p => new PairKey(p.PredictionIndex, p.TruthIndex));
        var truthIndices = corpus.Truth.Select((row, index) => (row, index))
            .ToDictionary(x => (x.row.ImageId, x.row.BuildingId), x => x.index);
        var matched = new Dictionary<(string ImageId, int BuildingId), int>();
        foreach (PredictionScore prediction in result.Predictions.Where(p => p.Matched))
        {
            if (prediction.BestTruthBuildingId is not int truthId ||
                !truthIndices.ContainsKey((prediction.ImageId, truthId)) ||
                !matched.TryAdd((prediction.ImageId, truthId), prediction.BuildingId))
                throw new ArgumentException("Matched truth is missing or reused in the evaluation result.", nameof(result));
        }
        var excluded = result.ExcludedInputs.Select(x => (x.Role, x.ImageId, x.BuildingId)).ToHashSet();
        if (!buildings.Keys.ToHashSet().SetEquals(truthIndices.Keys) || result.Images.Length != prepared.Images.Length ||
            !predictions.Keys.ToHashSet().SetEquals(corpus.Predictions.Where(p => !excluded.Contains(("prediction", p.ImageId, p.BuildingId)))
                .Select(p => (p.ImageId, p.BuildingId))) ||
            !result.Images.Select(x => x.ImageId).ToHashSet(StringComparer.Ordinal).SetEquals(prepared.Images))
            throw new ArgumentException("Review result does not cover the supplied corpus keys.", nameof(result));

        ReviewImage[] images = result.Images.Select(image =>
        {
            var truthRows = corpus.Truth.Select((row, index) => (row, index)).Where(x => x.row.ImageId == image.ImageId).ToArray();
            var predictionRows = corpus.Predictions.Select((row, index) => (row, index)).Where(x => x.row.ImageId == image.ImageId).ToArray();
            ReviewTruth[] truth = truthRows.Select(x =>
            {
                var key = (x.row.ImageId, x.row.BuildingId);
                bool isExcluded = excluded.Contains(("truth", key.ImageId, key.BuildingId));
                bool isMatched = matched.TryGetValue(key, out int predictionId);
                return new ReviewTruth(x.index, x.row.BuildingId,
                    isExcluded ? "excluded" : isMatched ? "matched" : "false-negative",
                    buildings[key].IoU, isMatched ? predictionId : null, prepared.Truth[x.index].Area, x.row.Polygons);
            }).ToArray();
            ReviewPrediction[] preds = predictionRows.Select(x =>
            {
                var key = (x.row.ImageId, x.row.BuildingId);
                if (excluded.Contains(("prediction", key.ImageId, key.BuildingId)))
                {
                    if (predictions.ContainsKey(key))
                        throw new ArgumentException("An excluded prediction also has a matching result.", nameof(result));
                    return new ReviewPrediction(x.index, x.row.BuildingId, "excluded", x.row.Confidence,
                        null, 0, 0, prepared.Predictions[x.index].Area, null, x.row.Polygons);
                }
                if (!predictions.TryGetValue(key, out PredictionScore? score))
                    throw new ArgumentException("Eligible prediction is missing its matching result.", nameof(result));
                PairScore? pair = null;
                if (score.BestTruthBuildingId is int truthId)
                {
                    if (!truthIndices.TryGetValue((x.row.ImageId, truthId), out int truthIndex) ||
                        !pairs.TryGetValue(new PairKey(x.index, truthIndex), out PairScore selected) ||
                        !selected.Intersects || selected.ImageId != x.row.ImageId ||
                        selected.PredictionBuildingId != x.row.BuildingId || selected.TruthBuildingId != truthId)
                        throw new ArgumentException("Best candidate has no corresponding visited intersection pair.", nameof(result));
                    pair = selected;
                }
                return new ReviewPrediction(x.index, x.row.BuildingId, score.Matched ? "matched" : "false-positive",
                    x.row.Confidence, score.BestTruthBuildingId, score.BestIoU, score.AssignedScore,
                    prepared.Predictions[x.index].Area, pair, x.row.Polygons);
            }).ToArray();
            return new ReviewImage(image.ImageId, image.TruePos, image.FalsePos, image.FalseNeg,
                image.Precision, image.Recall, image.F1Score, Bounds(truthRows.Select(x => x.row).Concat(predictionRows.Select(x => x.row))),
                truth, preds, result.ExcludedInputs.Where(x => x.ImageId == image.ImageId).ToArray());
        }).ToArray();
        return new(1, inputHash, corpus.SourceCommit, result.Backend, PreparedEvaluation.MinArea,
            PreparedEvaluation.Threshold, images, result.FallbackPairs, result.EligibleTruth, result.EligiblePredictions);
    }

    internal static void Write(PreparedEvaluation prepared, EvaluationResult result, string inputHash, string outputPath)
    {
        ReviewReportPayload payload = Create(prepared, result, inputHash);
        string template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ReviewReport.html"));
        int first = template.IndexOf(Marker, StringComparison.Ordinal);
        if (first < 0 || template.IndexOf(Marker, first + Marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("ReviewReport.html must contain exactly one report-data marker.");
        // The default JSON encoder escapes HTML-sensitive identifiers, including </script>.
        // Do not substitute UnsafeRelaxedJsonEscaping or interpolate identifiers into markup.
        string json = JsonSerializer.Serialize(payload, Corpus.Json);
        string html = template[..first] + json + template[(first + Marker.Length)..];
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, html); // All input/template/serialization validation precedes writing.
    }

    private static double[] Bounds(IEnumerable<InputRow> rows)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (double[] point in rows.SelectMany(r => r.Polygons).SelectMany(p => p).SelectMany(r => r))
        {
            if (point.Length != 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1]))
                throw new InvalidDataException("Review bounds need finite source XY coordinates.");
            minX = Math.Min(minX, point[0]); maxX = Math.Max(maxX, point[0]);
            minY = Math.Min(minY, point[1]); maxY = Math.Max(maxY, point[1]);
        }
        if (double.IsPositiveInfinity(minX)) return [0, 0, 1, 1];
        double width = maxX - minX, height = maxY - minY;
        if (!double.IsFinite(width) || !double.IsFinite(height))
            throw new InvalidDataException("Source coordinate span overflows finite SVG bounds.");
        // A nonzero viewport extent is needed for SVG; source coordinates remain untouched.
        return [minX, minY, width == 0 ? 1 : width, height == 0 ? 1 : height];
    }
}
