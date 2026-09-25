using PolylineKit;

namespace PortableOnly;

/// <summary>A .NET Standard 2.0 compile-time consumer with only the winding project reference.</summary>
public static class PortableApi
{
    public static double SquareArea()
    {
        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        WindingAreaResult closed = WindingArea.ClosedPath(square);
        WindingOverlapResult self = WindingArea.FilledRegions(square, square, PathFillRule.EvenOdd);
        if (WindingArea.IntersectionArea(square, square, PathFillRule.EvenOdd) != self.IntersectionArea)
            throw new InvalidOperationException("Portable intersection-only contract differs.");
        return closed.NonZero + self.IntersectionArea;
    }
}
