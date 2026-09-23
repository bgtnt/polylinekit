namespace PolylineKit;

/// <summary>Unsigned area in existing coordinates; experimental API.</summary>
public static class PolylineArea
{
    /// <summary>Integrates |first(x) - second(x)| over their common x interval.</summary>
    /// <remarks>
    /// Both inputs must traverse x in increasing order with exactly equal domain endpoints.
    /// Consecutive identical points are allowed. Vertical segments, backtracking, closed paths,
    /// nonfinite coordinates and coordinate magnitudes above 1e100 are rejected.
    /// Endpoints are joined vertically. No normalization, resampling or alignment is applied.
    /// Returns squared coordinate units. O(n+m) time and O(1) auxiliary storage.
    /// In exact arithmetic, zero characterizes the same graph and collinear subdivision preserves area.
    /// Double arithmetic can both erase small separations and introduce nonzero area for identical
    /// geometry represented with different vertices. Intermediate interpolation error, even with
    /// exactly representable inputs, can accumulate over a wide domain. No absolute error bound is promised.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An input is null.</exception>
    /// <exception cref="ArgumentException">An input violates the graph contract.</exception>
    public static double BetweenGraphs(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second)
    {
        Validate(first, nameof(first));
        Validate(second, nameof(second));
        if (first[0].X != second[0].X || first[first.Count - 1].X != second[second.Count - 1].X)
            throw new ArgumentException("Graphs must have exactly the same x domain.");

        int i = 1, j = 1;
        double x = first[0].X, end = first[first.Count - 1].X, sum = 0, compensation = 0;
        while (x < end)
        {
            while (first[i].X <= x) i++;
            while (second[j].X <= x) j++;
            double next = Math.Min(first[i].X, second[j].X);
            double d0 = At(first[i - 1], first[i], x) - At(second[j - 1], second[j], x);
            double d1 = At(first[i - 1], first[i], next) - At(second[j - 1], second[j], next);
            double a = Math.Abs(d0), b = Math.Abs(d1);
            double contribution;
            if ((d0 < 0 && d1 > 0) || (d0 > 0 && d1 < 0))
            {
                double t = a / (a + b);
                contribution = (next - x) * (a * t + b * (1 - t)) * 0.5;
            }
            else contribution = (next - x) * (a + b) * 0.5;
            // Compensated summation keeps many tiny strips from being lost to earlier large strips.
            double adjusted = contribution - compensation;
            double total = sum + adjusted;
            compensation = (total - sum) - adjusted;
            sum = total;
            x = next;
        }
        return sum;
    }

    private static double At(Point2 a, Point2 b, double x)
    {
        if (x == a.X) return a.Y;
        if (x == b.X) return b.Y;
        return a.Y + (b.Y - a.Y) * ((x - a.X) / (b.X - a.X));
    }

    private static void Validate(IReadOnlyList<Point2> points, string name)
    {
        if (points is null) throw new ArgumentNullException(name);
        if (points.Count < 2) throw new ArgumentException("At least two distinct x positions are required.", name);
        for (int i = 0; i < points.Count; i++)
        {
            Point2 p = points[i];
            if (double.IsNaN(p.X) || double.IsNaN(p.Y) || Math.Abs(p.X) > 1e100 || Math.Abs(p.Y) > 1e100)
                throw new ArgumentException("Coordinates must be finite and have magnitude at most 1e100.", name);
            if (i > 0 && (p.X < points[i - 1].X || (p.X == points[i - 1].X && p.Y != points[i - 1].Y)))
                throw new ArgumentException("Only increasing-x graphs and consecutive identical points are supported.", name);
        }
        if (points[0].X == points[points.Count - 1].X)
            throw new ArgumentException("The x interval must have positive length.", name);
    }
}
