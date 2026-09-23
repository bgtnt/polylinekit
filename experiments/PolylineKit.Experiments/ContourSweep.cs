using PolylineKit;

namespace PolylineKit.Experiments;

internal sealed record ContourAreas(double NonZero, double EvenOdd, double AbsoluteWinding, double Signed);

/// <summary>Small, deliberately slow independent fixture oracle; not a production polygon engine.</summary>
internal static class ContourSweep
{
    public static ContourAreas Measure(Point2[] closedWalk)
    {
        var edges = Enumerable.Range(0, closedWalk.Length)
            .Select(i => (A: closedWalk[i], B: closedWalk[(i + 1) % closedWalk.Length]))
            .Where(e => !Geometry.Same(e.A, e.B)).ToArray();
        SortedSet<double> cuts = new(closedWalk.Select(p => p.X));
        for (int i = 0; i < edges.Length; i++)
        for (int j = i + 1; j < edges.Length; j++)
            if (Geometry.Intersection(edges[i].A, edges[i].B, edges[j].A, edges[j].B, out double t, out _))
                cuts.Add(Geometry.Lerp(edges[i].A, edges[i].B, t).X);
        double nz = 0, eo = 0, aw = 0, signed = 0;
        double[] xs = cuts.ToArray();
        for (int k = 1; k < xs.Length; k++)
        {
            double x = (xs[k - 1] + xs[k]) / 2, width = xs[k] - xs[k - 1];
            var active = edges.Where(e => x > Math.Min(e.A.X, e.B.X) && x < Math.Max(e.A.X, e.B.X))
                .Select(e => (Y: Geometry.At(e.A, e.B, x), Delta: e.B.X > e.A.X ? 1 : -1)).OrderBy(e => e.Y).ToArray();
            int winding = 0;
            for (int i = 0; i + 1 < active.Length; i++)
            {
                winding += active[i].Delta;
                // Ordering is constant inside this slab. Linear height => midpoint integration is exact.
                double area = (active[i + 1].Y - active[i].Y) * width;
                if (winding != 0) nz += area;
                if ((Math.Abs(winding) & 1) == 1) eo += area;
                aw += Math.Abs(winding) * area;
                signed += winding * area;
            }
        }
        return new(nz, eo, aw, signed);
    }
}
