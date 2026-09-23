namespace PolylineKit;

/// <summary>Sampling of ordered strokes independently of their enclosed regions.</summary>
public static class PolylineSampling
{
    /// <summary>Returns uniformly spaced samples in normalized arc length.</summary>
    /// <param name="path">An ordered, nonzero-length polyline; consecutive duplicates are ignored.</param>
    /// <param name="sampleCount">Between 2 and 1,000,000 (at least 3 for a closed path).</param>
    /// <param name="closed">Includes the last-to-first edge, without repeating the final sample.</param>
    /// <remarks>
    /// Open sampling includes both endpoints. No smoothing or shape normalization is performed.
    /// Segment lengths are scaled by the path extent; segments far below that extent's floating-point
    /// resolution may contribute no representable length. Sampling cannot recover details already lost
    /// when very small displacements are stored at large coordinate offsets.
    /// </remarks>
    public static Point2[] ResampleByArcLength(IReadOnlyList<Point2> path, int sampleCount = 64, bool closed = false)
    {
        if (sampleCount < (closed ? 3 : 2) || sampleCount > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        Point2[] points = PathInput.CopyClean(path, nameof(path), closed);
        double minX = points[0].X, maxX = minX, minY = points[0].Y, maxY = minY;
        for (int i = 1; i < points.Length; i++)
        {
            minX = Math.Min(minX, points[i].X); maxX = Math.Max(maxX, points[i].X);
            minY = Math.Min(minY, points[i].Y); maxY = Math.Max(maxY, points[i].Y);
        }
        double span = Math.Max(maxX - minX, maxY - minY);
        if (!(span > 0)) throw new ArgumentException("Sampling requires a nonzero-length path.", nameof(path));

        int edgeCount = closed ? points.Length : points.Length - 1;
        var cumulative = new double[edgeCount + 1];
        for (int i = 0; i < edgeCount; i++)
        {
            Point2 a = points[i], b = points[(i + 1) % points.Length];
            // Scaling before squaring protects very small coordinates from underflow.
            double dx = (b.X - a.X) / span, dy = (b.Y - a.Y) / span;
            cumulative[i + 1] = cumulative[i] + Math.Sqrt(dx * dx + dy * dy);
        }
        double total = cumulative[edgeCount];
        if (!(total > 0) || double.IsInfinity(total))
            throw new ArgumentException("Path length cannot be represented.", nameof(path));

        var result = new Point2[sampleCount];
        result[0] = points[0];
        int edge = 0, denominator = closed ? sampleCount : sampleCount - 1;
        for (int i = 1; i < sampleCount; i++)
        {
            if (!closed && i == sampleCount - 1) { result[i] = points[points.Length - 1]; continue; }
            double target = total * ((double)i / denominator);
            while (edge < edgeCount - 1 && cumulative[edge + 1] <= target) edge++;
            double length = cumulative[edge + 1] - cumulative[edge];
            double t = length > 0 ? (target - cumulative[edge]) / length : 0;
            t = Math.Max(0, Math.Min(1, t));
            Point2 a = points[edge], b = points[(edge + 1) % points.Length];
            result[i] = new Point2(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        }
        return result;
    }
}
