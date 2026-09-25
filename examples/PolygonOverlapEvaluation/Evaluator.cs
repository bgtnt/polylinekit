namespace PolygonOverlapEvaluation;

internal sealed record BuildingScore(string ImageId, int BuildingId, double IoU);
internal sealed record PredictionScore(string ImageId, int BuildingId, int? BestTruthBuildingId,
    double BestIoU, bool Matched, double AssignedScore);
internal sealed record ImageScore(string ImageId, int TruePos, int FalsePos, int FalseNeg,
    double Precision, double Recall, double F1Score);
internal sealed record ExcludedInput(string Role, string ImageId, int BuildingId, double OriginalArea, string Reason);
internal readonly record struct PairKey(int PredictionIndex, int TruthIndex);
internal readonly record struct PairScore(int PredictionIndex, int TruthIndex, string ImageId,
    int PredictionBuildingId, int TruthBuildingId, double Intersection, double Union, double IoU,
    double MissingArea, double ExcessArea, bool Intersects, bool UsedFallback, string? FallbackReason);
internal sealed record EvaluationResult(string Backend, BuildingScore[] Buildings, PredictionScore[] Predictions,
    ImageScore[] Images, PairScore[] Pairs, int EligibleTruth, int EligiblePredictions,
    int BoundsRejected, int FallbackPairs, ExcludedInput[] ExcludedInputs, bool Complete = true);

internal sealed class PreparedEvaluation
{
    internal Corpus Corpus { get; }
    internal PreparedPolygon[] Truth { get; }
    internal PreparedPolygon[] Predictions { get; }
    internal PreparedOperand[] TruthOperands { get; }
    internal PreparedOperand[] PredictionOperands { get; }
    internal IGeometryBackend Backend { get; }
    internal int[][] ImageTruth { get; }
    internal int[][] ImagePredictions { get; }
    internal string[] Images { get; }
    internal const double MinArea = 20, Threshold = .5;

    internal PreparedEvaluation(Corpus corpus, IGeometryBackend backend)
    {
        Corpus = corpus; Backend = backend;
        Truth = corpus.Truth.Select(x => GeometryPreparation.Prepare(Corpus.Geometry(x))).ToArray();
        Predictions = corpus.Predictions.Select(x => GeometryPreparation.Prepare(Corpus.Geometry(x))).ToArray();
        // A GEOS buffer(0) repair is a different semantic contract from winding fill. Fail explicitly
        // with offending keys rather than silently repairing/excluding it or claiming complete scores.
        var invalid = corpus.Truth.Zip(Truth).Select(x => (Role: "truth", Row: x.First, Geometry: x.Second))
            .Concat(corpus.Predictions.Zip(Predictions).Select(x => (Role: "prediction", Row: x.First, Geometry: x.Second)))
            .Where(x => !x.Geometry.OriginalWasValid).Select(x => $"{x.Role}:{x.Row.ImageId}/{x.Row.BuildingId}").ToArray();
        if (invalid.Length > 0) throw new NotSupportedException("Invalid geometry requires an explicitly verified GEOS repair policy: " + string.Join(", ", invalid));
        TruthOperands = Truth.Select(x => backend.Prepare(x, true)).ToArray();
        PredictionOperands = Predictions.Select(x => backend.Prepare(x, false)).ToArray();
        Images = corpus.Truth.Select(x => x.ImageId).Concat(corpus.Predictions.Select(x => x.ImageId))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        ImageTruth = Images.Select(image => Enumerable.Range(0, Truth.Length)
            .Where(i => corpus.Truth[i].ImageId == image && Truth[i].Area >= MinArea).ToArray()).ToArray();
        ImagePredictions = Images.Select(image => Enumerable.Range(0, Predictions.Length)
            .Where(i => corpus.Predictions[i].ImageId == image && Predictions[i].Area > MinArea)
            .OrderByDescending(i => corpus.Predictions[i].Confidence).ThenBy(i => corpus.Predictions[i].SourceIndex).ToArray()).ToArray();
    }

    internal PairScore Pair(PairKey key)
    {
        var p = Predictions[key.PredictionIndex]; var g = Truth[key.TruthIndex];
        var intersection = Backend.IntersectionArea(PredictionOperands[key.PredictionIndex], TruthOperands[key.TruthIndex]);
        double area = intersection.Area;
        double union = p.Area + g.Area - area;
        // Solaris selects geometrically intersecting rows, including touches (IoU=0).
        // A shared exact predicate is needed only for zero-area bbox candidates; it is timed for all backends.
        bool intersects = area > 0 || p.Geometry.Intersects(g.Geometry);
        if (!double.IsFinite(area) || !double.IsFinite(union) || union <= 0)
            throw new InvalidOperationException("Undefined IoU for a candidate; no clamping or fabricated zero.");
        double iou = area / union;
        var pr = Corpus.Predictions[key.PredictionIndex]; var gr = Corpus.Truth[key.TruthIndex];
        return new(key.PredictionIndex, key.TruthIndex, pr.ImageId, pr.BuildingId, gr.BuildingId,
            area, union, iou, g.Area - area, p.Area - area, intersects, intersection.UsedFallback, intersection.FallbackReason);
    }

    internal EvaluationResult Run()
    {
        // These arrays and the result rows are new for every run: no cached IoUs or matching state.
        var used = new bool[Truth.Length]; var scores = new double[Truth.Length];
        var predictions = new List<PredictionScore>(); var images = new List<ImageScore>(); var pairs = new List<PairScore>();
        int rejected = 0, fallback = 0;
        for (int image = 0; image < Images.Length; image++)
        {
            int tp = 0, fp = 0;
            foreach (int pi in ImagePredictions[image])
            {
                int best = -1; double bestIoU = double.NegativeInfinity;
                foreach (int gi in ImageTruth[image])
                {
                    if (used[gi]) continue;
                    if (!Predictions[pi].Bounds.Intersects(Truth[gi].Bounds)) { rejected++; continue; }
                    var pair = Pair(new(pi, gi)); pairs.Add(pair);
                    if (pair.UsedFallback) fallback++;
                    // Source row order breaks equal-IoU truth ties, as pandas idxmax does.
                    if (pair.Intersects && pair.IoU > bestIoU) { best = gi; bestIoU = pair.IoU; }
                }
                if (best >= 0 && bestIoU > scores[best]) scores[best] = bestIoU;
                bool matched = best >= 0 && bestIoU > Threshold;
                if (matched) { used[best] = true; tp++; } else fp++;
                var row = Corpus.Predictions[pi];
                predictions.Add(new(row.ImageId, row.BuildingId, best < 0 ? null : Corpus.Truth[best].BuildingId,
                    best < 0 ? 0 : bestIoU, matched, matched ? bestIoU : 0));
            }
            int fn = ImageTruth[image].Count(gi => !used[gi] && Truth[gi].Area > 0);
            double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0;
            double recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0;
            images.Add(new(Images[image], tp, fp, fn, precision, recall, precision * recall > 0 ? 2 * precision * recall / (precision + recall) : 0));
        }
        return new(Backend.Name, Corpus.Truth.Select((x, i) => new BuildingScore(x.ImageId, x.BuildingId, scores[i]))
            .OrderBy(x => x.ImageId, StringComparer.Ordinal).ThenBy(x => x.BuildingId).ToArray(), predictions.ToArray(), images.ToArray(),
            pairs.ToArray(), ImageTruth.Sum(x => x.Length), ImagePredictions.Sum(x => x.Length), rejected, fallback,
            Truth.Select((p, i) => (p, i)).Where(x => x.p.Area < MinArea).Select(x => new ExcludedInput("truth",
                Corpus.Truth[x.i].ImageId, Corpus.Truth[x.i].BuildingId, x.p.Area, x.p.IsEmpty ? "empty" : "area-below-20"))
                .Concat(Predictions.Select((p, i) => (p, i)).Where(x => x.p.Area <= MinArea).Select(x => new ExcludedInput("prediction",
                    Corpus.Predictions[x.i].ImageId, Corpus.Predictions[x.i].BuildingId, x.p.Area, x.p.IsEmpty ? "empty" : "area-at-most-20"))).ToArray());
    }
}
