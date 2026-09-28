namespace PolylineKit;

// Internal feasibility prototype, deliberately absent from the supported public API.
// Rings contribute to one winding field per operand; they are not independently unioned.
internal static class MultiRingArea
{
    internal static double FilledArea(IReadOnlyList<IReadOnlyList<Point2>> rings,
        PathFillRule rule = PathFillRule.NonZero) => Compare(rings, Array.Empty<IReadOnlyList<Point2>>(), rule).FirstArea;

    internal static WindingOverlapResult Compare(IReadOnlyList<IReadOnlyList<Point2>> first,
        IReadOnlyList<IReadOnlyList<Point2>> second, PathFillRule rule = PathFillRule.NonZero) =>
        Evaluate(first, second, rule, false);

    internal static double Intersection(IReadOnlyList<IReadOnlyList<Point2>> first,
        IReadOnlyList<IReadOnlyList<Point2>> second, PathFillRule rule = PathFillRule.NonZero) =>
        Evaluate(first, second, rule, true).IntersectionArea;

    private static WindingOverlapResult Evaluate(IReadOnlyList<IReadOnlyList<Point2>> first,
        IReadOnlyList<IReadOnlyList<Point2>> second, PathFillRule rule, bool intersectionOnly)
    {
        if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(rule));
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        var ws = WindingEngine.Workspace.Rent();
        try
        {
            int capacity = checked(Capacity(first, nameof(first)) + Capacity(second, nameof(second)));
            Point2[] vertices = ws.VertexBuffer(capacity);
            int ringSplit = first.Count;
            // These O(ring count) descriptors are intentionally not pooled in the feasibility prototype.
            int[] starts = new int[checked(ringSplit + second.Count + 1)];
            int n = 0, ring = 0;
            Copy(first, nameof(first));
            int split = n;
            Copy(second, nameof(second));
            starts[ring] = n;
            return WindingEngine.MultipleRings(ws, starts, ringSplit, split, rule, intersectionOnly);

            void Copy(IReadOnlyList<IReadOnlyList<Point2>> rings, string name)
            {
                for (int r = 0; r < rings.Count; r++)
                {
                    var path = rings[r];
                    int start = n;
                    starts[ring++] = start;
                    for (int p = 0; p < path.Count; p++) WindingEngine.Append(vertices, ref n, start, path[p], name);
                    WindingEngine.CloseLoop(vertices, ref n, start);
                    if (n - start < 3) throw new ArgumentException("Each ring requires at least 3 vertices after duplicate removal.", name);
                }
            }
        }
        finally { WindingEngine.Workspace.Return(ws); }
    }

    private static int Capacity(IReadOnlyList<IReadOnlyList<Point2>> rings, string name)
    {
        int count = 0;
        for (int r = 0; r < rings.Count; r++)
        {
            var path = rings[r];
            if (path is null) throw new ArgumentException("A ring is null.", name);
            count = checked(count + path.Count);
        }
        return count;
    }
}
