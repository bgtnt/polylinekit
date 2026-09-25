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
        return passed;

        void Equal(double expected, double actual)
        {
            if (expected != actual) throw new InvalidOperationException($"Legacy consumer: expected {expected:R}, got {actual:R}.");
            passed++;
        }
    }
}
