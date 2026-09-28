namespace PolylineKit;

/// <summary>Filled area and overlap metrics for regions containing multiple closed rings.</summary>
/// <remarks>
/// Each operand is filled independently by the selected rule. Coordinates retain their original position
/// and scale. Results use squared coordinate units, with floating-point rounding and no output contours.
/// Prepared regions are reusable; crossing detection and winding propagation run for each call.
/// </remarks>
public static class RegionArea
{
    /// <summary>Measures one region's filled area under the selected rule.</summary>
    public static double FilledArea(PreparedRegion region, PathFillRule fillRule = PathFillRule.NonZero)
    {
        ValidateRule(fillRule);
        if (region is null) throw new ArgumentNullException(nameof(region));
        return Evaluate(region, PreparedRegion.Empty, fillRule, false).FirstArea;
    }

    /// <summary>Measures only the area filled by both regions.</summary>
    public static double IntersectionArea(PreparedRegion first, PreparedRegion second, PathFillRule fillRule = PathFillRule.NonZero)
    {
        Validate(first, second, fillRule);
        if (!first.Bounds.HasValue || !second.Bounds.HasValue) return 0;
        Bounds2D a = first.Bounds.Value, b = second.Bounds.Value;
        if (a.MaxX < b.MinX || b.MaxX < a.MinX || a.MaxY < b.MinY || b.MaxY < a.MinY) return 0;
        return Evaluate(first, second, fillRule, true).IntersectionArea;
    }

    /// <summary>Measures both regions, intersection, union, symmetric difference and overlap ratios.</summary>
    public static RegionOverlapResult Compare(PreparedRegion first, PreparedRegion second, PathFillRule fillRule = PathFillRule.NonZero)
    {
        Validate(first, second, fillRule);
        return new RegionOverlapResult(Evaluate(first, second, fillRule, false));
    }

    private static void ValidateRule(PathFillRule fillRule)
    {
        if (fillRule != PathFillRule.NonZero && fillRule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(fillRule));
    }

    private static void Validate(PreparedRegion first, PreparedRegion second, PathFillRule fillRule)
    {
        ValidateRule(fillRule);
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
    }

    private static WindingOverlapResult Evaluate(PreparedRegion first, PreparedRegion second, PathFillRule rule, bool intersectionOnly)
    {
        var ws = WindingEngine.Workspace.Rent();
        try
        {
            int rings = checked(first.RingCount + second.RingCount);
            int split = first.VertexCount, n = checked(split + second.VertexCount);
            Point2[] vertices = ws.VertexBuffer(n);
            ws.RingBuffers(rings);
            Array.Copy(first.Vertices, 0, vertices, 0, split);
            Array.Copy(second.Vertices, 0, vertices, split, second.VertexCount);
            Array.Copy(first.Starts, ws.RingStarts, first.RingCount);
            for (int r = 0; r < second.RingCount; r++) ws.RingStarts[first.RingCount + r] = split + second.Starts[r];
            ws.RingStarts[rings] = n;
            Array.Copy(first.RingBounds, ws.RegionBounds, first.RingCount);
            Array.Copy(second.RingBounds, 0, ws.RegionBounds, first.RingCount, second.RingCount);
            return WindingEngine.MultipleRings(ws, ws.RingStarts, first.RingCount, split, rule, intersectionOnly, rings, boundsReady: true);
        }
        finally { WindingEngine.Workspace.Return(ws); }
    }
}
