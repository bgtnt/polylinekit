using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

internal static class ClipperOracle
{
    public const int Precision = 8;
    public static double Between(Point2[] p, Point2[] q, FillRule rule = FillRule.NonZero) =>
        Contour(p.Concat(q.Reverse()).ToArray(), rule);

    public static double Contour(Point2[] contour, FillRule rule)
    {
        if (contour.Any(p => Math.Abs(p.X) > 1e6 || Math.Abs(p.Y) > 1e6))
            throw new ArgumentOutOfRangeException(nameof(contour), "Oracle fixtures use |coordinate| <= 1e6 at precision 8.");
        PathD path = new(contour.Select(p => new PointD(p.X, p.Y)));
        PathsD resolved = Clipper.Union(new PathsD { path }, new PathsD(), rule, Precision);
        // Signed sum retains holes. Abs of each returned contour would fill holes incorrectly.
        return Math.Abs(Clipper.Area(resolved));
    }
}
