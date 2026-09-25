using PolylineKit;

namespace PortableOnly;

/// <summary>A .NET Standard 2.0 compile-time consumer with only the winding project reference.</summary>
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
        return closed.NonZero + self.IntersectionArea;
    }
}
