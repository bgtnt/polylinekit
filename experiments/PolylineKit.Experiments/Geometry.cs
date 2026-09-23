using PolylineKit;

namespace PolylineKit.Experiments;

internal static class Geometry
{
    public static Point2 Sub(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
    public static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
    public static double Length(Point2 a, Point2 b) => Math.Sqrt(Dot(Sub(a, b), Sub(a, b)));
    public static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    public static Point2 Lerp(Point2 a, Point2 b, double t) => new(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
    public static double At(Point2 a, Point2 b, double x) => x == b.X ? b.Y : a.Y + (b.Y - a.Y) * ((x - a.X) / (b.X - a.X));
    public static double Length(IReadOnlyList<Point2> p)
    {
        double length = 0;
        for (int i = 1; i < p.Count; i++) length += Length(p[i - 1], p[i]);
        return length;
    }
    public static double SignedArea(IReadOnlyList<Point2> p)
    {
        // Translation to the first vertex avoids unnecessary large-origin cancellation.
        double twice = 0;
        for (int i = 1; i + 1 < p.Count; i++) twice += Cross(Sub(p[i], p[0]), Sub(p[i + 1], p[0]));
        return twice / 2;
    }
    public static Point2[] Clean(IReadOnlyList<Point2> p, bool mergeCollinear = false)
    {
        List<Point2> result = [];
        foreach (Point2 item in p)
        {
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y)) throw new ArgumentException("Nonfinite point.");
            if (result.Count > 0 && Same(result[^1], item)) continue;
            if (mergeCollinear)
                while (result.Count >= 2 && Cross(Sub(result[^1], result[^2]), Sub(item, result[^1])) == 0 &&
                       Dot(Sub(result[^1], result[^2]), Sub(item, result[^1])) >= 0) result.RemoveAt(result.Count - 1);
            result.Add(item);
        }
        if (result.Count < 2) throw new ArgumentException("At least one nonzero segment required.");
        return result.ToArray();
    }
    public static bool IncreasingX(IReadOnlyList<Point2> p)
    {
        for (int i = 1; i < p.Count; i++) if (p[i].X <= p[i - 1].X) return false;
        return p.Count >= 2;
    }
    // Endpoint-inclusive proper (nonparallel) segment intersection.
    public static bool Intersection(Point2 a, Point2 b, Point2 c, Point2 d, out double t, out double u)
    {
        Point2 r = Sub(b, a), s = Sub(d, c), ca = Sub(c, a);
        double determinant = Cross(r, s);
        t = u = 0;
        if (determinant == 0) return false;
        t = Cross(ca, s) / determinant;
        u = Cross(ca, r) / determinant;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
