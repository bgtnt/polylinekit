using PolylineKit;

namespace ContourSimplificationDemo;

internal static class DemoChecks
{
    private static int checks;
    private static readonly Point2[] Square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];

    internal static void Run(Preset[] presets)
    {
        checks = 0;
        InputChecks();
        CancellationChecks();
        AreaControls();
        SelectorChecks();
        CandidateChecks();
        PresetChecks(presets);
        Console.WriteLine($"ContourSimplificationDemo: {checks} analytic, validation, finite-selection and frozen-preset checks passed.");
    }

    private static void CancellationChecks()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        bool canceled = false;
        try { CandidateGenerator.Generate(CandidateGenerator.Coordinates(Square), source.Token); }
        catch (OperationCanceledException exception) { canceled = exception.CancellationToken == source.Token; }
        True(canceled, "cancelled generation preserves the caller's cancellation token");
    }

    private static void InputChecks()
    {
        Reject(null, "null input");
        Reject([], "empty input");
        Reject([[0, 0]], "one point");
        Reject([[0, 0], [1, 0]], "one segment");
        Reject([[0, 0], [1, 0], [2, 0]], "collinear zero filled area");
        Reject([[0, 0], [0, 0], [0, 0]], "duplicate points");
        Reject([[0, 0], [1, 0], [double.NaN, 1]], "NaN coordinate");
        Reject([[0, 0], [1, 0], [double.PositiveInfinity, 1]], "infinite coordinate");
        Reject([[0, 0], [1, 0], [Math.BitIncrement(1e100), 1]], "coordinate cap");
        Reject([[0, 0], [1, 0], [1, 1, 0]], "wrong dimension");
        Reject([[0, 0], [1, 0], null!], "null coordinate row");
        Reject(Enumerable.Repeat(new[] { 0.0, 0.0 }, CandidateGenerator.MaximumPoints + 1).ToArray(), "point count checked before cleaning");
        Point2[] clean = CandidateGenerator.Validate([[0, 0], [0, 0], [4, 0], [4, 4], [0, 4], [0, 0]]);
        True(clean.Length == 4, "duplicate and closing vertex removal");
        CandidateResponse bowTie = CandidateGenerator.Generate([[0, 0], [4, 4], [0, 4], [4, 0]]);
        Near(bowTie.OriginalArea, 8, 0, "self-intersecting bow-tie has positive filled area");
        Near(WindingArea.ClosedPath([new(0, 0), new(4, 4), new(0, 4), new(4, 0)]).Signed, 0, 0,
            "zero Signed area does not reject the bow-tie");
    }

    private static void AreaControls()
    {
        Point2[] expanded = [new(0, 0), new(2, 0), new(4, 0), new(4, 2), new(4, 4), new(2, 4), new(0, 4), new(0, 2)];
        Candidate collinear = CandidateGenerator.Score(expanded, Square, 1, .1, out string? reason) ??
            throw new InvalidOperationException(reason);
        Near(collinear.ChangedArea, 0, 0, "collinear subdivision preserves filled region");
        Near(collinear.UnionArea, 16, 0, "analytic square union");
        Near(collinear.Error, 0, 0, "same fill has zero Jaccard error");
        True(collinear.VertexCount < expanded.Length, "fewer points need not mean changed area");
        Point2[] original = CandidateGenerator.Validate(Presets.MixedPoints());
        Point2[] rectangle = [new(0, 0), new(8, 0), new(8, 4), new(0, 4)];
        Candidate mixed = CandidateGenerator.Score(original, rectangle, 1, 1, out reason) ??
            throw new InvalidOperationException(reason);
        // Independent analytic areas: two base-2, height-.5 triangles, one added and one removed.
        Near(mixed.FirstArea, 32, 0, "mixed original area");
        Near(mixed.SecondArea, 32, 0, "mixed rectangle area");
        Near(mixed.ChangedArea, 1, 0, "mixed XOR counts both half-unit changes");
        Near(mixed.UnionArea, 32.5, 0, "mixed union includes the outward half-unit triangle");
        Near(mixed.Error, 1 / 32.5, 1e-15, "Jaccard denominator is union, not original area");
    }

    private static void SelectorChecks()
    {
        Candidate Fixture(int index, double epsilon, int vertices, double error) =>
            new(index, epsilon, CandidateGenerator.Coordinates(Square), vertices, error * 16, 16, error, 16, 16);
        Candidate original = Fixture(0, 0, 10, 0);
        Candidate[] nonMonotone = [original, Fixture(1, .1, 7, .009), Fixture(2, .2, 4, .04), Fixture(3, .3, 3, .007)];
        True(CandidateGenerator.Select(nonMonotone, .007).Index == 3, "inclusive threshold and whole nonmonotone series inspected");
        True(CandidateGenerator.Select(nonMonotone, .007).Error <= .007, "selected candidate satisfies budget");
        True(CandidateGenerator.Select(nonMonotone, 0).Index == 0, "strict budget original fallback");
        True(CandidateGenerator.Select(nonMonotone, .007).Index != CandidateGenerator.Select(nonMonotone, .007 / 100).Index,
            "0.7 percent is .007, not .00007");
        Near(.7 / 100, .007, 1e-18, "percent to fraction conversion");
        Candidate[] ties = [original, Fixture(4, .2, 4, .005), Fixture(3, .1, 4, .005),
            Fixture(2, .1, 4, .005), Fixture(1, .01, 4, .006)];
        True(CandidateGenerator.Select(ties, .007).Index == 2, "stable tie order: vertices, error, epsilon, index");
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity, Math.BitIncrement(1.0) })
        {
            bool rejected = false;
            try { CandidateGenerator.Select(ties, invalid); } catch (InputException) { rejected = true; }
            True(rejected, "invalid selection budget rejected");
        }
    }

    private static void CandidateChecks()
    {
        True(CandidateGenerator.RelativeTolerances.Length == 18, "fixed finite policy has eighteen tolerances");
        foreach (Point2[] degenerate in new Point2[][] { [], [new(0, 0)], [new(0, 0), new(1, 0)],
            [new(0, 0), new(1, 0), new(2, 0)] })
        {
            Candidate? value = CandidateGenerator.Score(Square, degenerate, 1, .1, out string? reason);
            True(value is null && !string.IsNullOrEmpty(reason), "degenerate candidate skipped with diagnosis");
        }
        Candidate? invalid = CandidateGenerator.Score(Square, [new(0, 0), new(1, 0), new(double.NaN, 1)],
            1, .1, out string? invalidReason);
        True(invalid is null && invalidReason is not null, "invalid candidate coordinates excluded without clamping");
        True(CandidateGenerator.Score(Square, Square, 2, .2, out _) is not null, "a skipped candidate does not block a later valid one");
    }

    private static void PresetChecks(Preset[] presets)
    {
        True(presets.Length == 5 && presets[0].Id == "BGR", "four frozen contours plus one explanation input");
        foreach (Preset preset in presets)
        {
            double[][] before = preset.Points.Select(p => p.ToArray()).ToArray();
            CandidateResponse response = CandidateGenerator.Generate(preset.Points);
            True(response.FillRule == "NonZero", "explicit fill rule: " + preset.Id);
            True(response.Candidates.Length + response.Skipped.Length == 19, "every candidate accounted for: " + preset.Id);
            True(response.Candidates[0].Index == 0 && response.Candidates[0].Error == 0, "original fallback: " + preset.Id);
            True(before.Zip(preset.Points).All(pair => pair.First.SequenceEqual(pair.Second)), "input preserved: " + preset.Id);
            foreach (Candidate candidate in response.Candidates)
            {
                True(candidate.Points.Length == candidate.VertexCount, "candidate coordinates/count agree");
                True(candidate.Error >= 0 && candidate.Error <= 1 && double.IsFinite(candidate.Error), "finite budget value");
                if (candidate.Index > 0)
                {
                    Point2[] original = CandidateGenerator.Validate(response.Original);
                    Point2[] points = CandidateGenerator.Validate(candidate.Points);
                    RegionOverlapResult comparison = PolylineArea.CompareRegions(original, points, PathFillRule.NonZero);
                    Near(candidate.ChangedArea, comparison.SymmetricDifferenceArea, 0, "scored against original, not prior candidate");
                }
            }
            Candidate selected = CandidateGenerator.Select(response.Candidates, .007);
            True(selected.Error <= .007, "real selected result meets .7% budget: " + preset.Id);
            True(!response.Candidates.Any(c => c.Error <= .007 && c.VertexCount < selected.VertexCount), "fewest measured vertices within budget");
            if (preset.Id == "MIXED")
            {
                Candidate broadBudget = CandidateGenerator.Select(response.Candidates, .05);
                True(broadBudget.VertexCount < response.Original.Length, "real mixed preset simplifies at the broader budget");
                True(broadBudget.ChangedArea > 0, "real mixed simplification changes filled area");
            }
            Console.WriteLine($"{preset.Id}: {response.Original.Length} -> {selected.VertexCount} vertices, Jaccard {selected.Error:P6}, epsilon {selected.Epsilon:R}; {response.Skipped.Length} skipped.");
        }
    }

    private static void Reject(double[][]? points, string label)
    {
        bool rejected = false;
        try { CandidateGenerator.Generate(points); } catch (InputException) { rejected = true; }
        True(rejected, label);
    }
    private static void Near(double actual, double expected, double tolerance, string label) =>
        True(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{label}: {actual:R} versus {expected:R}");
    private static void True(bool value, string label)
    {
        checks++;
        if (!value) throw new InvalidOperationException("Demo check failed: " + label);
    }
}
