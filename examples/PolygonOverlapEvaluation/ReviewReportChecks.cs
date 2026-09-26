using System.Text.Json;

namespace PolygonOverlapEvaluation;

internal static class ReviewReportChecks
{
    internal static int Run(Corpus? solaris = null)
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Review report: " + name);
        }
        void Near(double expected, double actual, string name) =>
            Check(double.IsFinite(actual) && Math.Abs(expected - actual) <= 1e-12, name);
        void Throws<T>(Action action, string name) where T : Exception
        {
            try { action(); }
            catch (T) { checks++; return; }
            throw new InvalidOperationException("Review report: expected " + typeof(T).Name + ": " + name);
        }
        const string attack = "</script><img src=x onerror=alert(1)>\"&\u2028";
        var truth = new[]
        {
            Rect(10, "review", 0, 0, 10, 10), Rect(11, "review", 30, 0, 10, 10),
            Rect(12, "review", 80, 0, 10, 10), Rect(13, "review", -100, 0, 1, 1),
            Rect(14, "review", 150, 0, 10, 10), Empty(-1, "empty"),
            Rect(50, "truth-only", 0, 0, 10, 10), Rect(60, attack, 0, 0, 10, 10),
            new InputRow(0, "holes", 70, null, false, [[Ring(0, 0, 20, 20), Ring(5, 5, 5, 5)]]),
            new InputRow(0, "multipart", 80, null, false, [[Ring(0, 0, 10, 10)], [Ring(20, 0, 10, 10)]])
        };
        var predictions = new[]
        {
            Rect(20, "review", 0, 0, 10, 10, 9), Rect(21, "review", 30, 0, 4, 10, 8),
            Rect(22, "review", 90, 0, 10, 10, 7), Rect(23, "review", 105, 0, 4, 5, 6),
            Rect(24, "review", 150, 0, 10, 10, 5), Rect(25, "review", 150, 0, 10, 10, 4),
            Empty(-1, "empty", 1), Rect(50, "prediction-only", 0, 0, 10, 10, 1),
            Rect(60, attack, 0, 0, 10, 10, 1), Rect(70, "holes", 0, 0, 20, 20, 1),
            Rect(80, "multipart", 0, 0, 10, 10, 1)
        };
        var corpus = new Corpus(1, "custom-source-" + attack,
            predictions.Select((r, i) => r with { SourceIndex = i }).ToArray(),
            truth.Select((r, i) => r with { SourceIndex = i }).ToArray(), []);
        var prepared = new PreparedEvaluation(corpus, GeometryBackends.Create("core"));
        EvaluationResult result = prepared.Run();
        ReviewReportPayload report = ReviewReport.Create(prepared, result, "custom-input-identity");
        Check(report.SchemaVersion == 1 && report.InputSHA256 == "custom-input-identity" && report.SourceCommit == corpus.SourceCommit,
            "custom input needs no Solaris hash and preserves provenance");
        Check(report.Backend == "core" && report.MinArea == 20 && report.Threshold == .5, "backend and policy identified");
        Check(report.Images.Sum(i => i.Truth.Length) == truth.Length && report.Images.Sum(i => i.Predictions.Length) == predictions.Length,
            "every original row remains in the report");
        Check(report.Images.Length == 7, "union of original image keys retained");
        Check(report.Images.SelectMany(i => i.Truth).Select(t => t.Index).Order().SequenceEqual(Enumerable.Range(0, truth.Length)),
            "global truth source indices retained");
        Check(report.Images.SelectMany(i => i.Predictions).Select(p => p.Index).Order().SequenceEqual(Enumerable.Range(0, predictions.Length)),
            "global prediction source indices retained");
        ReviewImage main = report.Images.Single(i => i.ImageId == "review");
        Check(main.TruePos == 2 && main.FalsePos == 3 && main.FalseNeg == 2, "review metrics copied from actual matching");
        Check(main.Truth.Select(t => t.BuildingId).SequenceEqual([10, 11, 12, 13, 14]), "truth source row order retained");
        Check(main.Predictions.Select(p => p.BuildingId).SequenceEqual([20, 21, 22, 23, 24, 25]), "prediction source row order retained");
        ReviewTruth matched = main.Truth.Single(t => t.BuildingId == 10);
        Check(matched.Status == "matched" && matched.MatchedPredictionId == 20, "accepted prediction identifies matched truth");
        ReviewTruth missed = main.Truth.Single(t => t.BuildingId == 11);
        Check(missed.Status == "false-negative" && missed.MatchedPredictionId is null, "subthreshold positive score is still a false negative");
        Near(.4, missed.RetainedIoU, "subthreshold truth score is visible");
        ReviewPrediction partial = main.Predictions.Single(p => p.BuildingId == 21);
        Check(partial.Status == "false-positive" && partial.BestTruthBuildingId == 11 && partial.Pair is not null,
            "rejected prediction still exposes its real best available candidate");
        Near(.4, partial.BestIoU, "best IoU retained for a false positive");
        Near(0, partial.AssignedScore, "rejected prediction assigned score remains zero");
        Near(40, partial.Pair!.Value.Intersection, "selected pair intersection copied");
        Near(60, partial.Pair.Value.MissingArea, "selected pair missing area copied");
        Check(partial.Pair == result.Pairs.Single(p => p.PredictionBuildingId == 21), "pair is the exact recorded value");
        ReviewPrediction touching = main.Predictions.Single(p => p.BuildingId == 22);
        Check(touching.BestTruthBuildingId == 12 && touching.Pair is { Intersects: true, Intersection: 0 },
            "touching best candidate is distinguishable from no candidate");
        ReviewPrediction consumed = main.Predictions.Single(p => p.BuildingId == 25);
        Check(consumed.Status == "false-positive" && consumed.BestTruthBuildingId is null && consumed.Pair is null,
            "previously consumed truth is not recomputed or offered as a candidate");
        Check(main.Truth.Single(t => t.BuildingId == 13).Status == "excluded" &&
              main.Predictions.Single(p => p.BuildingId == 23) is { Status: "excluded", Pair: null, BestTruthBuildingId: null },
            "excluded small truth and exact-area-20 prediction remain visible");
        Check(main.ExcludedInputs.Length == 2 && main.ExcludedInputs.All(e => e.OriginalArea <= 20), "original exclusion records copied");
        Check(main.Bounds.SequenceEqual([-100d, 0, 260, 10]), "bounds use all original coordinates including excluded input");
        Check(main.Truth[0].Polygons[0][0][0].SequenceEqual([0d, 0]) &&
              main.Truth[0].Polygons[0][0][^1].SequenceEqual([0d, 0]), "original closure and XY preserved");
        ReviewImage empty = report.Images.Single(i => i.ImageId == "empty");
        Check(empty.Bounds.SequenceEqual([0d, 0, 1, 1]) && empty.TruePos == 0 && empty.FalsePos == 0 && empty.FalseNeg == 0,
            "empty image has finite unit viewport and zero metrics");
        Check(empty.Truth.Single().Status == "excluded" && empty.Predictions.Single().Status == "excluded" &&
              empty.Truth.Single().Polygons.Length == 0 && empty.ExcludedInputs.Length == 2, "both empty sentinels retained");
        Check(report.Images.Single(i => i.ImageId == "truth-only").Truth.Single().Status == "false-negative", "truth-only image retained");
        Check(report.Images.Single(i => i.ImageId == "prediction-only").Predictions.Single() is { Status: "false-positive", Pair: null },
            "prediction-only image retained");
        Check(report.Images.Single(i => i.ImageId == "holes").Truth.Single().Polygons[0].Length == 2, "hole ring survives report projection");
        Check(report.Images.Single(i => i.ImageId == "multipart").Truth.Single().Polygons.Length == 2, "multipart components survive report projection");
        Check(report.FallbackPairs == 2 && report.Images.Where(i => i.ImageId is "holes" or "multipart")
            .All(i => i.Predictions.Single().Pair is { UsedFallback: true }), "fallback disclosure follows actual pair records");
        using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(report, Corpus.Json)))
        {
            Check(json.RootElement.GetProperty("Images").EnumerateArray().Single(i => i.GetProperty("ImageId").GetString() == "holes")
                .GetProperty("Truth")[0].GetProperty("Polygons")[0].GetArrayLength() == 2, "JSON preserves holes, not merely in-memory DTO");
            Check(json.RootElement.GetProperty("Images").EnumerateArray().Any(i => i.GetProperty("ImageId").GetString() == attack),
                "escaped malicious identifier still round-trips as plain text");
        }
        string temporary = Path.GetTempFileName();
        try
        {
            ReviewReport.Write(prepared, result, "custom-input-identity", temporary);
            string html = File.ReadAllText(temporary);
            Check(!html.Contains(attack, StringComparison.Ordinal) && html.Contains("\\u003C/script\\u003E", StringComparison.Ordinal),
                "malicious identifier cannot terminate the data script");
            Check(!html.Contains("/*__REPORT_DATA__*/null", StringComparison.Ordinal), "template marker replaced exactly once");
            string before = html;
            double old = corpus.Truth[0].Polygons[0][0][0][0];
            corpus.Truth[0].Polygons[0][0][0][0] = double.PositiveInfinity;
            Throws<InvalidDataException>(() => ReviewReport.Write(prepared, result, "custom-input-identity", temporary), "non-finite source coordinates rejected");
            Check(File.ReadAllText(temporary) == before, "failed report validation does not overwrite an existing file");
            corpus.Truth[0].Polygons[0][0][0][0] = old;
            double other = corpus.Truth[0].Polygons[0][0][1][0];
            corpus.Truth[0].Polygons[0][0][0][0] = -double.MaxValue;
            corpus.Truth[0].Polygons[0][0][1][0] = double.MaxValue;
            Throws<InvalidDataException>(() => ReviewReport.Create(prepared, result, "custom-input-identity"), "finite source span overflow rejected");
            corpus.Truth[0].Polygons[0][0][0][0] = old; corpus.Truth[0].Polygons[0][0][1][0] = other;
        }
        finally { File.Delete(temporary); }
        Throws<ArgumentException>(() => ReviewReport.Create(prepared, result with { Complete = false }, "custom-input-identity"), "partial result cannot masquerade as complete");
        Throws<ArgumentException>(() => ReviewReport.Create(prepared, result with { Pairs = [] }, "custom-input-identity"), "best candidate must be a recorded visited pair");

        if (solaris is not null)
        {
            var realPrepared = new PreparedEvaluation(solaris, GeometryBackends.Create("core"));
            var realResult = realPrepared.Run();
            var real = ReviewReport.Create(realPrepared, realResult, "caller-provided-hash");
            var gt = real.Images.SelectMany(i => i.Truth).ToArray(); var pr = real.Images.SelectMany(i => i.Predictions).ToArray();
            Check(real.Images.Length == 6 && gt.Length == 172 && pr.Length == 145, "Solaris complete original population");
            Check(gt.Count(x => x.Status == "matched") == 87 && gt.Count(x => x.Status == "false-negative") == 82 &&
                  gt.Count(x => x.Status == "excluded") == 3, "Solaris truth status partition");
            Check(pr.Count(x => x.Status == "matched") == 87 && pr.Count(x => x.Status == "false-positive") == 57 &&
                  pr.Count(x => x.Status == "excluded") == 1, "Solaris prediction status partition");
            Check(real.EligibleTruth == 169 && real.EligiblePredictions == 144 && real.FallbackPairs == 0, "Solaris eligibility/fallback counts");
            Check(gt.Count(x => x.Status == "false-negative" && x.RetainedIoU > 0) == 34, "Solaris positive unmatched truth scores preserved");
            Check(pr.All(p => p.BestTruthBuildingId is null ? p.Pair is null : p.Pair is { } pair &&
                  pair.PredictionIndex == p.Index && pair.TruthBuildingId == p.BestTruthBuildingId), "Solaris actual selected pair identity");
        }
        return checks;
    }

    private static InputRow Rect(int id, string image, double x, double y, double width, double height, double? confidence = null) =>
        new(0, image, id, confidence, false, [[Ring(x, y, width, height)]]);
    private static double[][] Ring(double x, double y, double width, double height) =>
        [[x, y], [x + width, y], [x + width, y + height], [x, y + height], [x, y]];
    private static InputRow Empty(int id, string image, double? confidence = null) => new(0, image, id, confidence, true, []);
}
