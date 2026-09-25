using PolylineKit;

namespace PortableOnly;

/// <summary>A .NET Standard 2.0 compile-time consumer with only the dependency-free core reference.</summary>
public static class PortableApi
{
    public static double SquareArea()
    {
        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        WindingAreaResult closed = WindingArea.ClosedPath(square);
        RegionOverlapResult self = PolylineArea.CompareRegions(square, square, PathFillRule.EvenOdd);
        if (PolylineArea.FilledArea(square) != closed.NonZero
            || PolylineArea.FilledArea(square, PathFillRule.EvenOdd) != closed.EvenOdd)
            throw new InvalidOperationException("Portable selected-fill contract differs.");
        if (PolylineArea.IntersectionArea(square, square, PathFillRule.EvenOdd) != self.IntersectionArea)
            throw new InvalidOperationException("Portable intersection-only contract differs.");
        if (PolylineArea.BetweenGraphs([new(0, 0), new(2, 0)], [new(0, 2), new(2, 2)]) != 4)
            throw new InvalidOperationException("Portable graph area is incorrect.");
        AffineTransform2D transform = AffineTransform2D.Scaling(2).Then(AffineTransform2D.Translation(10, -4));
        Point2[] transformed = transform.Apply(square);
        Bounds2D bounds = Bounds2D.FromPoints(transformed);
        NormalizationResult normalized = PolylineNormalization.ToUnitBounds(transformed, BoundsScaling.Uniform);
        Point2[] samples = PolylineSampling.ResampleByArcLength(square, 8, closed: true);
        var options = new AlignmentOptions { Closed = true, SampleCount = 8, SearchClosedPhase = false };
        AlignmentResult aligned = PolylineAlignment.FitSimilarity(transformed, square, options);
        if (bounds.Width != 4 || PolylineArea.FilledArea(normalized.Points) != 1 || samples.Length != 8 || aligned.RmsError > 1e-12)
            throw new InvalidOperationException("Portable transform, normalization, sampling or alignment contract differs.");
        return closed.NonZero + self.IntersectionArea;
    }
}
