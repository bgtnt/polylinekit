using PolylineKit;

namespace PolylineKit.Experiments;

internal sealed record RegionAreas(double First, double Second, double Intersection, double Union, double SymmetricDifference);

/// <summary>
/// Slow independent oracle for two independently filled closed paths: split x at every vertex and crossing,
/// order edges inside each slab and integrate the two winding numbers separately. Not a production engine.
/// </summary>
internal static class RegionSweep
{
    public static RegionAreas Measure(Point2[] first, Point2[] second, bool nonZero)
    {
        var edges = Edges(first, 0).Concat(Edges(second, 1)).ToArray();
        SortedSet<double> cuts = new(first.Concat(second).Select(p => p.X));
        for (int i = 0; i < edges.Length; i++)
        for (int j = i + 1; j < edges.Length; j++)
            if (Geometry.Intersection(edges[i].A, edges[i].B, edges[j].A, edges[j].B, out double t, out _))
                cuts.Add(Geometry.Lerp(edges[i].A, edges[i].B, t).X);
        bool Filled(int w) => nonZero ? w != 0 : (Math.Abs(w) & 1) == 1;
        double a = 0, b = 0, both = 0, either = 0, one = 0;
        double[] xs = cuts.ToArray();
        for (int k = 1; k < xs.Length; k++)
        {
            double x = (xs[k - 1] + xs[k]) / 2, width = xs[k] - xs[k - 1];
            var active = edges.Where(e => x > Math.Min(e.A.X, e.B.X) && x < Math.Max(e.A.X, e.B.X))
                .Select(e => (Y: Geometry.At(e.A, e.B, x), e.Loop, Delta: e.B.X > e.A.X ? 1 : -1)).OrderBy(e => e.Y).ToArray();
            int[] w = new int[2];
            for (int i = 0; i + 1 < active.Length; i++)
            {
                w[active[i].Loop] += active[i].Delta;
                double area = (active[i + 1].Y - active[i].Y) * width;
                bool fa = Filled(w[0]), fb = Filled(w[1]);
                if (fa) a += area;
                if (fb) b += area;
                if (fa && fb) both += area;
                if (fa || fb) either += area;
                if (fa != fb) one += area;
            }
        }
        return new(a, b, both, either, one);
    }

    private static IEnumerable<(Point2 A, Point2 B, int Loop)> Edges(Point2[] loop, int id) =>
        Enumerable.Range(0, loop.Length).Select(i => (A: loop[i], B: loop[(i + 1) % loop.Length], Loop: id))
            .Where(e => !Geometry.Same(e.A, e.B));
}
