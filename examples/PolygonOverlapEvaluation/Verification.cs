namespace PolygonOverlapEvaluation;

internal static class Verification
{
    internal static object Run(string input)
    {
        if (Corpus.Hash(input) != Corpus.SolarisFixtureHash) throw new InvalidDataException("Pinned Solaris fixture checksum differs.");
        Corpus corpus = Corpus.Load(input);
        if (corpus.SourceCommit != Corpus.SolarisCommit || corpus.Expected.Length != 172)
            throw new InvalidDataException("Wrong reference population.");
        int protocolChecks = EvaluatorChecks.Run();
        int geometryChecks = GeometryChecks.Run();
        int reportChecks = ReviewReportChecks.Run(corpus);
        var reference = new PreparedEvaluation(corpus, GeometryBackends.Create("nts"));
        EvaluationResult expected = reference.Run();
        var population = new
        {
            Images = reference.Images.Length, PredictionRows = corpus.Predictions.Length, TruthRows = corpus.Truth.Length,
            expected.EligiblePredictions, expected.EligibleTruth,
            EmptyPredictions = reference.Predictions.Count(x => x.IsEmpty), EmptyTruth = reference.Truth.Count(x => x.IsEmpty),
            ConvexPredictions = reference.Predictions.Count(x => x.IsConvexSimple), ConvexTruth = reference.Truth.Count(x => x.IsConvexSimple),
            ConcavePredictions = reference.Predictions.Count(x => !x.IsEmpty && !x.IsConvexSimple),
            ConcaveTruth = reference.Truth.Count(x => !x.IsEmpty && !x.IsConvexSimple),
            Invalid = reference.Predictions.Concat(reference.Truth).Count(x => !x.OriginalWasValid),
            Holes = reference.Predictions.Concat(reference.Truth).Count(x => x.HasHoles),
            Multipart = reference.Predictions.Concat(reference.Truth).Count(x => x.IsMultipart),
            SuppliedVertices = corpus.Predictions.Concat(corpus.Truth).Sum(x => x.Polygons.Sum(p => p.Sum(r => r.Length))),
            BoundsCandidates = expected.Pairs.Length, PreciseIntersections = expected.Pairs.Count(x => x.Intersects),
            PositiveIntersectionPairs = expected.Pairs.Count(x => x.Intersection > 0), expected.BoundsRejected,
            BothConvexPairs = expected.Pairs.Count(x => reference.Predictions[x.PredictionIndex].IsConvexSimple && reference.Truth[x.TruthIndex].IsConvexSimple)
        };
        if (population.Images != 6 || population.PredictionRows != 145 || population.TruthRows != 172 ||
            population.EligiblePredictions != 144 || population.EligibleTruth != 169 ||
            population.ConvexPredictions != 144 || population.ConvexTruth != 71 || population.SuppliedVertices != 5771)
            throw new InvalidDataException("Runtime population differs from independently prepared inventory.");
        // Independently reproduced pinned source logic; the original CSV is the per-building oracle.
        if (expected.Images.Sum(x => x.TruePos) != 87 || expected.Images.Sum(x => x.FalsePos) != 57 || expected.Images.Sum(x => x.FalseNeg) != 82)
            throw new InvalidDataException("Unexpected source-protocol aggregate decisions.");
        var original = corpus.Expected.ToDictionary(x => (x.ImageId, x.BuildingId));
        var baselinePairs = expected.Pairs.ToDictionary(x => new PairKey(x.PredictionIndex, x.TruthIndex));
        var reports = new List<object>();
        foreach (string name in GeometryBackends.Names)
        {
            var prepared = new PreparedEvaluation(corpus, GeometryBackends.Create(name));
            var actual = prepared.Run();
            if (actual.Buildings.Length != original.Count || !actual.Buildings.Select(x => (x.ImageId, x.BuildingId)).ToHashSet().SetEquals(original.Keys))
                throw new InvalidDataException(name + ": reference keys/count differ.");
            double maxExpected = actual.Buildings.Max(x => Math.Abs(x.IoU - original[(x.ImageId, x.BuildingId)].IoU));
            if (!(maxExpected < 1e-9)) throw new InvalidOperationException($"{name}: expected-score error {maxExpected:R} is not < 1e-9.");
            if (!actual.Images.SequenceEqual(expected.Images) ||
                !actual.Predictions.Select(x => (x.ImageId, x.BuildingId, x.BestTruthBuildingId, x.Matched))
                    .SequenceEqual(expected.Predictions.Select(x => (x.ImageId, x.BuildingId, x.BestTruthBuildingId, x.Matched))) ||
                !actual.Pairs.Select(x => new PairKey(x.PredictionIndex, x.TruthIndex))
                    .SequenceEqual(expected.Pairs.Select(x => new PairKey(x.PredictionIndex, x.TruthIndex))))
                throw new InvalidOperationException(name + ": candidate schedule or matching decisions differ.");
            double areaError = 0, iouError = 0, symmetryError = 0;
            var reverseBackend = GeometryBackends.Create(name);
            var reversedPredictions = prepared.Predictions.Select(x => reverseBackend.Prepare(x, true)).ToArray();
            var reversedTruth = prepared.Truth.Select(x => reverseBackend.Prepare(x, false)).ToArray();
            foreach (var pair in actual.Pairs)
            {
                var baseline = baselinePairs[new(pair.PredictionIndex, pair.TruthIndex)];
                double maxArea = Math.Max(prepared.Predictions[pair.PredictionIndex].Area, prepared.Truth[pair.TruthIndex].Area);
                double minArea = Math.Min(prepared.Predictions[pair.PredictionIndex].Area, prepared.Truth[pair.TruthIndex].Area);
                double tolerance = 1e-8 + 1e-11 * maxArea;
                double reverse = reverseBackend.IntersectionArea(reversedTruth[pair.TruthIndex], reversedPredictions[pair.PredictionIndex]).Area;
                areaError = Math.Max(areaError, Math.Abs(pair.Intersection - baseline.Intersection));
                iouError = Math.Max(iouError, Math.Abs(pair.IoU - baseline.IoU));
                symmetryError = Math.Max(symmetryError, Math.Abs(pair.Intersection - reverse));
                if (!double.IsFinite(pair.Intersection) || pair.Intersection < -tolerance || pair.Intersection > minArea + tolerance ||
                    Math.Abs(pair.Intersection - baseline.Intersection) > tolerance || Math.Abs(pair.Intersection - reverse) > tolerance ||
                    Math.Abs(pair.IoU - baseline.IoU) >= 1e-9 || pair.Intersects != baseline.Intersects)
                    throw new InvalidOperationException($"{name}: candidate failed at {pair.ImageId}: prediction {pair.PredictionBuildingId}, truth {pair.TruthBuildingId}.");
            }
            reports.Add(new
            {
                Backend = name, ExpectedRows = actual.Buildings.Length, MaxExpectedIoUError = maxExpected,
                MaxIntersectionErrorVsNts = areaError, MaxPairIoUErrorVsNts = iouError, MaxSymmetryError = symmetryError,
                Candidates = actual.Pairs.Length, actual.FallbackPairs,
                FallbackReasons = actual.Pairs.Where(x => x.UsedFallback).GroupBy(x => x.FallbackReason!).ToDictionary(x => x.Key, x => x.Count())
            });
        }
        return new
        {
            Pass = true, SourceCommit = corpus.SourceCommit, DataSHA256 = Corpus.Hash(input), ProtocolChecks = protocolChecks,
            GeometryChecks = geometryChecks, ReviewReportChecks = reportChecks, FullFixtureCoverage = true, Population = population,
            MinSelectedIoUDistanceToThreshold = expected.Predictions.Where(x => x.BestTruthBuildingId is not null).Min(x => Math.Abs(x.BestIoU - .5)),
            RetainedPositiveSubthresholdTruthScores = expected.Buildings.Count(x => x.IoU > 0 && x.IoU <= .5),
            Backends = reports, Images = expected.Images,
            ExtraDiagnostics = "MissingArea and ExcessArea are added diagnostics, not original Solaris scores.",
            UnsupportedPolicy = "Invalid geometry is explicitly rejected; no GEOS buffer(0) equivalence asserted. Valid holes/multipart use explicit NTS fallback."
        };
    }
}
