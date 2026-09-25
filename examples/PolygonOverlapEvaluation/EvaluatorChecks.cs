using System.Text.Json;

namespace PolygonOverlapEvaluation;

/// <summary>Small protocol controls independent of the published fixture's expected numbers.</summary>
internal static class EvaluatorChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Equal<T>(T expected, T actual, string name) where T : IEquatable<T>
        {
            checks++;
            if (!expected.Equals(actual)) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
        }
        void Near(double expected, double actual, string name)
        {
            checks++;
            if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-12)
                throw new InvalidOperationException($"{name}: expected {expected:R}, got {actual:R}.");
        }
        void Require(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(name);
        }
        void Metrics(EvaluationResult result, int tp, int fp, int fn, string image = "image")
        {
            var row = result.Images.Single(x => x.ImageId == image);
            Equal(tp, row.TruePos, "True positives"); Equal(fp, row.FalsePos, "False positives");
            Equal(fn, row.FalseNeg, "False negatives");
            double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0;
            double recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0;
            Near(precision, row.Precision, "Precision"); Near(recall, row.Recall, "Recall");
            Near(precision * recall > 0 ? 2 * precision * recall / (precision + recall) : 0, row.F1Score, "F1");
        }
        EvaluationResult Evaluate(InputRow[] truth, InputRow[] predictions) =>
            new PreparedEvaluation(MakeCorpus(truth, predictions), GeometryBackends.Create("nts")).Run();

        // Filtering is asymmetric at exactly 20 square pixels, and the truth output retains
        // filtered rows and empty sentinels. An IoU of exactly 0.5 is not an accepted match.
        var filtered = Evaluate(
            [Rect(0, 11, 0, 0, 4, 5), Rect(1, 12, 100, 0, 19, 1), Empty(2, -1)],
            [Rect(0, 21, 0, 0, 4, 5, 2), Rect(1, 22, 0, 0, 4, 10, 1), Empty(2, -1, 0)]);
        Equal(1, filtered.EligibleTruth, "Area 20 truth included");
        Equal(1, filtered.EligiblePredictions, "Area 20 prediction excluded");
        Equal(3, filtered.Buildings.Length, "Every original truth key retained");
        Near(.5, filtered.Buildings.Single(x => x.BuildingId == 11).IoU, "Threshold score retained");
        Near(0, filtered.Buildings.Single(x => x.BuildingId == 12).IoU, "Small truth score zero");
        Near(0, filtered.Buildings.Single(x => x.BuildingId == -1).IoU, "Empty truth score zero");
        Near(0, filtered.Predictions.Single().AssignedScore, "Rejected prediction assigned zero");
        Metrics(filtered, 0, 1, 1);

        // A rejected prediction must not consume its winning truth row.
        var retained = Evaluate([Rect(0, 1, 0, 0, 10, 10)],
            [Rect(0, 1, 0, 0, 5, 10, 2), Rect(1, 2, 0, 0, 6, 10, 1)]);
        Near(.5, retained.Predictions[0].BestIoU, "First threshold IoU");
        Require(!retained.Predictions[0].Matched, "Threshold equality is rejected");
        Require(retained.Predictions[1].Matched, "Later prediction can match retained truth");
        Near(.6, retained.Buildings.Single().IoU, "Later accepted IoU replaces earlier score");
        Metrics(retained, 1, 1, 0);

        // Confidence controls matching; a later, geometrically better prediction cannot reuse
        // an accepted truth. This is deliberately not independent maximum IoU per building.
        var consumed = Evaluate([Rect(0, 1, 0, 0, 10, 10)],
            [Rect(0, 91, 0, 0, 9, 10, 1), Rect(1, 61, 0, 0, 6, 10, 2)]);
        Equal(61, consumed.Predictions[0].BuildingId, "Descending confidence before source order");
        Equal(91, consumed.Predictions[1].BuildingId, "Lower confidence processed second");
        Near(.6, consumed.Buildings.Single().IoU, "Accepted truth cannot later rise to 0.9");
        Require(consumed.Predictions[1].BestTruthBuildingId is null, "Consumed truth no longer a candidate");
        Metrics(consumed, 1, 1, 0);

        // Only a prediction's selected maximum updates the per-truth output.
        var maximum = Evaluate([Rect(0, 1, 0, 0, 20, 10), Rect(1, 2, 0, 0, 10, 10)],
            [Rect(0, 1, 0, 0, 10, 10, 1)]);
        Near(0, maximum.Buildings.Single(x => x.BuildingId == 1).IoU, "Unselected intersection is not retained");
        Near(1, maximum.Buildings.Single(x => x.BuildingId == 2).IoU, "Maximum intersection retained");
        Metrics(maximum, 1, 0, 1);

        var truthTie = Evaluate([Rect(0, 99, 0, 0, 10, 10), Rect(1, 1, 0, 0, 10, 10)],
            [Rect(0, 1, 0, 0, 10, 10, 1)]);
        Require(truthTie.Predictions.Single().BestTruthBuildingId == 99, "Equal IoU uses truth source order, not BuildingId");
        Near(1, truthTie.Buildings.Single(x => x.BuildingId == 99).IoU, "First tied truth selected");
        Near(0, truthTie.Buildings.Single(x => x.BuildingId == 1).IoU, "Second tied truth untouched");

        // Solaris does not specify confidence ties. The example explicitly defines source order
        // for this extension; the pinned fixture has no confidence ties within any image.
        var confidenceTie = Evaluate([Rect(0, 1, 0, 0, 10, 10)],
            [Rect(0, 99, 0, 0, 6, 10, 1), Rect(1, 1, 0, 0, 9, 10, 1)]);
        Equal(99, confidenceTie.Predictions[0].BuildingId, "Documented confidence-tie extension");
        Near(.6, confidenceTie.Buildings.Single().IoU, "Confidence tie selects first source row");

        var touching = Evaluate([Rect(0, 1, 0, 0, 10, 10)], [Rect(0, 2, 10, 0, 10, 10, 1)]);
        Require(touching.Pairs.Single().Intersects, "Boundary touch is a geometric candidate");
        Require(touching.Predictions.Single().BestTruthBuildingId == 1, "Touch retains a best candidate with zero IoU");
        Near(0, touching.Pairs.Single().Intersection, "Touch intersection area zero");
        Metrics(touching, 0, 1, 1);

        var disjoint = Evaluate([Polygon(0, 1, null, (0, 0), (12, 0), (0, 12), (0, 0))],
            [Polygon(0, 2, 1, (12, 12), (12, 4), (4, 12), (12, 12))]);
        Equal(1, disjoint.Pairs.Length, "Bounding boxes overlap for disjoint triangles");
        Require(!disjoint.Pairs.Single().Intersects, "False bounding-box candidate rejected geometrically");
        Require(disjoint.Predictions.Single().BestTruthBuildingId is null, "Disjoint polygons have no best truth");
        Metrics(disjoint, 0, 1, 1);

        // Empty and one-sided images still occur in the report and do not share matching state.
        var independentImages = Evaluate(
            [Rect(0, 1, 0, 0, 10, 10) with { ImageId = "truth-only" }, Empty(1, -1) with { ImageId = "empty" }],
            [Rect(0, 1, 0, 0, 10, 10, 1) with { ImageId = "prediction-only" }, Empty(1, -1, 1) with { ImageId = "empty" }]);
        Equal(3, independentImages.Images.Length, "Union of original image IDs retained");
        Metrics(independentImages, 0, 0, 1, "truth-only");
        Metrics(independentImages, 0, 1, 0, "prediction-only");
        Metrics(independentImages, 0, 0, 0, "empty");

        var repeat = new PreparedEvaluation(MakeCorpus([Rect(0, 1, 0, 0, 10, 10)],
            [Rect(0, 1, 0, 0, 10, 10, 1)]), GeometryBackends.Create("nts"));
        Equal(JsonSerializer.Serialize(repeat.Run()), JsonSerializer.Serialize(repeat.Run()), "Every run resets matching state");

        // Validity policy is deliberately narrower than historical prediction buffer(0).
        var invalid = MakeCorpus([Rect(0, 1, 0, 0, 10, 10)],
            [Polygon(0, 17, 1, (0, 0), (10, 10), (0, 10), (10, 0), (0, 0))]);
        try { _ = new PreparedEvaluation(invalid, GeometryBackends.Create("nts")); throw new InvalidOperationException("Invalid polygon silently accepted."); }
        catch (NotSupportedException e) { Require(e.Message.Contains("prediction:image/17", StringComparison.Ordinal), "Unsupported geometry identifies its key"); }

        // Exact expected key sets and cardinalities are required; prefix-only comparisons are unsafe.
        var corpus = MakeCorpus([Rect(0, 1, 0, 0, 10, 10), Rect(1, 2, 20, 0, 10, 10)],
            [Rect(0, 1, 0, 0, 10, 10, 1)]);
        string temporary = Path.GetTempFileName();
        try
        {
            Corpus.Write(temporary, corpus);
            Equal(2, Corpus.Load(temporary).Expected.Length, "Valid schema load");
            Reject(corpus with { Expected = [corpus.Expected[0]] }, "Missing expected row");
            Reject(corpus with { Expected = [.. corpus.Expected, new(2, "image", 3, 0)] }, "Extra expected row");
            Reject(corpus with { Expected = [corpus.Expected[0], corpus.Expected[0]] }, "Duplicate expected key");
            Reject(corpus with { Expected = [corpus.Expected[0], corpus.Expected[1] with { BuildingId = 3 }] }, "Wrong expected key with equal count");
            Reject(corpus with { Truth = [corpus.Truth[0], corpus.Truth[1] with { BuildingId = 1 }] }, "Duplicate truth key");
            Reject(corpus with { Predictions = [corpus.Predictions[0] with { SourceIndex = 1 }] }, "Source order mismatch");
            Reject(corpus with { Predictions = [corpus.Predictions[0] with { Confidence = null }] }, "Missing confidence");
            Reject(corpus with { Truth = [corpus.Truth[0] with { IsEmpty = true }, corpus.Truth[1]] }, "Empty sentinel mismatch");
            void Reject(Corpus bad, string name)
            {
                Corpus.Write(temporary, bad);
                try { _ = Corpus.Load(temporary); throw new InvalidOperationException(name + " was accepted."); }
                catch (InvalidDataException) { checks++; }
            }
        }
        finally { File.Delete(temporary); }
        return checks;
    }

    private static Corpus MakeCorpus(InputRow[] truth, InputRow[] predictions) =>
        new(1, Corpus.SolarisCommit, predictions, truth,
            truth.Select(row => new ExpectedRow(row.SourceIndex, row.ImageId, row.BuildingId, 0)).ToArray());
    private static InputRow Empty(int source, int id, double? confidence = null) => new(source, "image", id, confidence, true, []);
    private static InputRow Rect(int source, int id, double x, double y, double width, double height, double? confidence = null) =>
        Polygon(source, id, confidence, (x, y), (x + width, y), (x + width, y + height), (x, y + height), (x, y));
    private static InputRow Polygon(int source, int id, double? confidence, params (double X, double Y)[] ring) =>
        new(source, "image", id, confidence, false, [[ring.Select(p => new[] { p.X, p.Y }).ToArray()]]);
}
