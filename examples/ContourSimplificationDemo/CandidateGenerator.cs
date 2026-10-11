using Clipper2Lib;
using PolylineKit;

namespace ContourSimplificationDemo;

/// <summary>Finite example policy; this is not a new simplification algorithm or a Core API.</summary>
internal static class CandidateGenerator
{
    internal const int MaximumPoints = 1024;
    internal static ReadOnlySpan<double> RelativeTolerances =>
    [
        .0001, .0002, .0004, .0007,
        .001, .0015, .002, .003,
        .004, .006, .008, .012,
        .016, .024, .032, .05,
        .075, .1
    ];

    internal static CandidateResponse Generate(double[][]? supplied, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Point2[] original = Validate(supplied);
        double originalArea = PolylineArea.FilledArea(original, PathFillRule.NonZero);
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(originalArea) || originalArea <= 0)
            throw new InputException("The contour must have positive finite NonZero filled area.");
        var candidates = new List<Candidate>();
        var skipped = new List<SkippedCandidate>();
        double[][] originalCoordinates = Coordinates(original);
        // The original is always an available exact identity/fallback for this finite search.
        candidates.Add(new(0, 0, originalCoordinates, original.Length, 0, originalArea, 0, originalArea, originalArea));
        double length = Math.Max(original.Max(p => p.X) - original.Min(p => p.X),
            original.Max(p => p.Y) - original.Min(p => p.Y));
        int index = 0;
        foreach (double ratio in RelativeTolerances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            double epsilon = length * ratio;
            // Each candidate comes directly from the same original, never from another simplification.
            var path = new PathD(original.Length);
            foreach (Point2 point in original) path.Add(new(point.X, point.Y));
            Point2[] simplified = Clipper.SimplifyPath(path, epsilon, isClosedPath: true)
                .Select(p => new Point2(p.x, p.y)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            Candidate? scored = Score(original, simplified, index, epsilon, out string? reason);
            cancellationToken.ThrowIfCancellationRequested();
            if (scored is null) skipped.Add(new(index, epsilon, reason!));
            else candidates.Add(scored);
        }
        return new("NonZero", originalCoordinates, originalArea, candidates.ToArray(), skipped.ToArray());
    }

    internal static Point2[] Validate(double[][]? points)
    {
        if (points is null) throw new InputException("Provide points as an array of [x, y] coordinates.");
        if (points.Length > MaximumPoints) throw new InputException($"The demo accepts at most {MaximumPoints} supplied points.");
        var converted = new Point2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double[]? point = points[i];
            if (point is null || point.Length != 2)
                throw new InputException($"Point {i + 1} must contain exactly two coordinates.");
            if (!ValidCoordinate(point[0]) || !ValidCoordinate(point[1]))
                throw new InputException($"Point {i + 1} must have finite coordinates with magnitude at most 1e100.");
            converted[i] = new(point[0], point[1]);
        }
        Point2[] clean = Clean(converted);
        if (clean.Length < 3) throw new InputException("A closed contour needs at least three vertices after duplicate removal.");
        return clean;
    }

    internal static Candidate? Score(Point2[] original, IReadOnlyList<Point2> candidate, int index, double epsilon,
        out string? reason)
    {
        reason = null;
        Point2[] clean = Clean(candidate);
        if (clean.Length < 3) { reason = "Fewer than three vertices after duplicate removal."; return null; }
        if (clean.Any(p => !ValidCoordinate(p.X) || !ValidCoordinate(p.Y)))
        { reason = "The simplifier returned invalid coordinates."; return null; }
        RegionOverlapResult result = PolylineArea.CompareRegions(original, clean, PathFillRule.NonZero);
        double? error = result.JaccardDistance;
        if (!double.IsFinite(result.SecondArea) || result.SecondArea <= 0)
        { reason = "The simplified contour has no positive finite filled area."; return null; }
        if (!double.IsFinite(result.FirstArea) || result.FirstArea <= 0 ||
            !double.IsFinite(result.UnionArea) || result.UnionArea <= 0 ||
            !double.IsFinite(result.SymmetricDifferenceArea) || result.SymmetricDifferenceArea < 0 ||
            !error.HasValue || !double.IsFinite(error.Value) || error.Value < 0 || error.Value > 1)
        { reason = "The area comparison cannot provide a finite Jaccard budget value in [0, 1]."; return null; }
        return new(index, epsilon, Coordinates(clean), clean.Length, result.SymmetricDifferenceArea,
            result.UnionArea, error.Value, result.FirstArea, result.SecondArea);
    }

    internal static Candidate Select(IReadOnlyList<Candidate> candidates, double budget)
    {
        if (!double.IsFinite(budget) || budget < 0 || budget > 1) throw new InputException("The budget must be a fraction in [0, 1].");
        // Jaccard errors need not be monotone in epsilon: inspect the entire finite set.
        return candidates.Where(c => double.IsFinite(c.Error) && c.Error >= 0 && c.Error <= budget)
            .OrderBy(c => c.VertexCount).ThenBy(c => c.Error).ThenBy(c => c.Epsilon).ThenBy(c => c.Index)
            .FirstOrDefault() ?? throw new InputException("No scored candidate satisfies the budget.");
    }

    internal static Point2[] Clean(IReadOnlyList<Point2> points)
    {
        var clean = new List<Point2>(points.Count);
        foreach (Point2 point in points)
            if (clean.Count == 0 || !Same(clean[^1], point)) clean.Add(point);
        if (clean.Count > 1 && Same(clean[0], clean[^1])) clean.RemoveAt(clean.Count - 1);
        return clean.ToArray();
    }

    internal static double[][] Coordinates(IEnumerable<Point2> points) => points.Select(p => new[] { p.X, p.Y }).ToArray();
    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    private static bool ValidCoordinate(double value) => double.IsFinite(value) && Math.Abs(value) <= 1e100;
}
