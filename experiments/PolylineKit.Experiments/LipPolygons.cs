using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Slow diagnostic reconstruction of the paper's ordered intersection polygons.</summary>
internal static class LipPolygons
{
    internal sealed record Region(double Area, double PLength, double QLength, double Weight);

    public static List<Region> Regions(Point2[] p, Point2[] q, double? wholeLength = null)
    {
        double denominator = wholeLength ?? (Geometry.Length(p) + Geometry.Length(q));
        List<(double P, double Q)> cuts = [(0, 0), (p.Length - 1, q.Length - 1)];
        for (int i = 1; i < p.Length; i++)
        for (int j = 1; j < q.Length; j++)
        {
            if (Geometry.Intersection(p[i - 1], p[i], q[j - 1], q[j], out double t, out double u))
                cuts.Add((i - 1 + t, j - 1 + u));
            else if (Geometry.Cross(Geometry.Sub(p[i], p[i - 1]), Geometry.Sub(q[j - 1], p[i - 1])) == 0 &&
                     Geometry.Cross(Geometry.Sub(p[i], p[i - 1]), Geometry.Sub(q[j], p[i - 1])) == 0)
            {
                // Collinear overlap boundaries, not an arbitrary representative point.
                AddCollinear(p[i - 1], i - 1, q[j - 1], q[j], j - 1, false);
                AddCollinear(p[i], i, q[j - 1], q[j], j - 1, false);
                AddCollinear(q[j - 1], j - 1, p[i - 1], p[i], i - 1, true);
                AddCollinear(q[j], j, p[i - 1], p[i], i - 1, true);
            }
        }
        cuts = cuts.Distinct().OrderBy(c => c.P).ThenBy(c => c.Q).ToList();
        List<Region> result = [];
        for (int i = 1; i < cuts.Count; i++)
        {
            var a = cuts[i - 1]; var b = cuts[i];
            if (b.Q < a.Q) throw new NotSupportedException("Crossings are not in a common traversal order.");
            var pSide = Slice(p, a.P, b.P); var qSide = Slice(q, a.Q, b.Q);
            var polygon = pSide.Concat(qSide.AsEnumerable().Reverse()).ToArray();
            double lp = Geometry.Length(pSide), lq = Geometry.Length(qSide);
            result.Add(new(Math.Abs(Geometry.SignedArea(polygon)), lp, lq, (lp + lq) / denominator));
        }
        return result;

        void AddCollinear(Point2 point, double own, Point2 a, Point2 b, int segment, bool swapped)
        {
            double t = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y) ? (point.X - a.X) / (b.X - a.X) : (point.Y - a.Y) / (b.Y - a.Y);
            if (t >= 0 && t <= 1) cuts.Add(swapped ? (segment + t, own) : (own, segment + t));
        }
    }

    private static Point2[] Slice(Point2[] p, double start, double end)
    {
        List<Point2> points = [At(p, start)];
        for (int i = (int)Math.Floor(start) + 1; i < end; i++) points.Add(p[i]);
        if (end != start) points.Add(At(p, end));
        return points.ToArray();
    }
    private static Point2 At(Point2[] p, double position)
    {
        int i = (int)position;
        return i == p.Length - 1 ? p[i] : Geometry.Lerp(p[i], p[i + 1], position - i);
    }
}
