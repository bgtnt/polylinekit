using PolylineKit;

namespace LegacyApiConsumer;

/// <summary>This component is compiled once against the monolithic assembly and then kept unchanged.</summary>
public static class LegacyEntry
{
    public static int Run()
    {
        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        Point2[] shifted = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
        int passed = 0;
        Equal(2, square[2].X); Equal(2, square[2].Y);
        WindingAreaResult closed = WindingArea.ClosedPath(square);
        Equal(4, closed.NonZero); Equal(4, closed.EvenOdd);
        Equal(4, closed.AbsoluteWinding); Equal(4, closed.Signed);
        Equal(0, closed.CrossingCount);
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            WindingOverlapResult overlap = WindingArea.FilledRegions(square, shifted, rule);
            Equal(4, overlap.FirstArea); Equal(4, overlap.SecondArea);
            Equal(2, overlap.IntersectionArea); Equal(6, overlap.UnionArea);
            Equal(4, overlap.SymmetricDifferenceArea);
            Equal(1.0 / 3, overlap.IntersectionOverUnion!.Value);
            Equal(2.0 / 3, overlap.JaccardDistance!.Value);
            Equal((int)rule, (int)overlap.FillRule);
        }
        Point2[] bottom = [new(0, 0), new(2, 0)], top = [new(0, 2), new(2, 2)];
        Equal(4, WindingArea.EndpointBridged(bottom, top).NonZero);
        // Both old Point2 and PolylineArea identities must resolve through the parent assembly's forwarders.
        Equal(4, PolylineArea.BetweenGraphs(bottom, top));
        // Compile all nine transform/sampling/alignment types against their original assembly identity.
        AffineTransform2D transform = AffineTransform2D.Translation(3, 5);
        Point2[] moved = transform.Apply(square);
        Bounds2D bounds = Bounds2D.FromPoints(moved);
        Equal(2, bounds.Width); Equal(2, bounds.Height); Equal(4, bounds.Center.X); Equal(6, bounds.Center.Y);
        BoundsScaling scaling = BoundsScaling.Uniform;
        NormalizationResult normalized = PolylineNormalization.ToUnitBounds(moved, scaling);
        Equal(1, normalized.Bounds.Width); Equal(1, normalized.Bounds.Height);
        Equal(0, normalized.Bounds.Center.X); Equal(0, normalized.Bounds.Center.Y);
        Point2[] samples = PolylineSampling.ResampleByArcLength(square, 8, closed: true);
        Equal(8, samples.Length); Equal(1, samples[1].X); Equal(0, samples[1].Y);
        AlignmentOptions options = new() { Closed = true, SampleCount = 4, AllowScaling = false, SearchClosedPhase = false };
        AlignmentResult alignment = PolylineAlignment.FitSimilarity(moved, square, options);
        Equal(0, alignment.RmsError); Equal(1, alignment.Scale); Equal(-3, alignment.Transform.OffsetX);
        Equal(-5, alignment.Transform.OffsetY); Equal(4, alignment.AlignedPoints.Count);
        return passed;

        void Equal(double expected, double actual)
        {
            if (expected != actual) throw new InvalidOperationException($"Legacy consumer: expected {expected:R}, got {actual:R}.");
            passed++;
        }
    }
}
