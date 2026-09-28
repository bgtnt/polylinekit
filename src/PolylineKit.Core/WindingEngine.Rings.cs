namespace PolylineKit;

internal static partial class WindingEngine
{
    // A seed belongs to a ring; its winding values aggregate every ring of its operand.
    private readonly struct RingSeed
    {
        internal RingSeed(int first, int count, int own, int other, bool isA)
        { First = first; Count = count; Own = own; Other = other; IsA = isA; }
        internal readonly int First, Count, Own, Other;
        internal readonly bool IsA;
    }

    internal static WindingOverlapResult MultipleRings(Workspace ws, int[] starts, int ringSplit,
        int split, PathFillRule rule, bool intersectionOnly)
    {
        int rings = starts.Length - 1;
        if (rings < 2) return ConnectedRings(ws, starts, ringSplit, split, rule, intersectionOnly);
        var bounds = new RingBounds[rings];
        var parents = new int[rings];
        for (int r = 0; r < rings; r++)
        {
            bounds[r] = new RingBounds(ws.Vertices, starts[r], starts[r + 1]);
            parents[r] = r;
        }
        int groups = rings;
        for (int a = 0; a < rings; a++)
            for (int b = 0; b < a; b++)
                if (bounds[a].Intersects(bounds[b]))
                {
                    int x = Root(a), y = Root(b);
                    if (x != y) { parents[x] = y; groups--; }
                }
        if (groups == 1) return ConnectedRings(ws, starts, ringSplit, split, rule, intersectionOnly);
        for (int r = 0; r < rings; r++) parents[r] = Root(r);

        // Disjoint closed bounds imply zero winding outside each group. All interacting rings
        // (including nesting, overlaps and contacts) remain together. Each group's weighted
        // boundary chains are closed, so local origins are safe; arbitrary per-ring origins are not.
        Sum first = default, second = default, intersection = default, union = default, difference = default;
        WindingStatistics statistics = default;
        for (int root = 0; root < rings; root++)
        {
            if (parents[root] != root) continue;
            int groupRings = 0, capacity = 0;
            for (int r = 0; r < rings; r++)
                if (parents[r] == root) { groupRings++; capacity += starts[r + 1] - starts[r]; }
            var group = Workspace.Rent();
            WindingOverlapResult result;
            try
            {
                Point2[] v = group.VertexBuffer(capacity);
                int[] groupStarts = new int[groupRings + 1];
                int n = 0, i = 0, groupRingSplit = 0, groupSplit = 0;
                // Preserve operand order and the relative symbolic ordering of every input vertex.
                for (int r = 0; r < rings; r++)
                {
                    if (parents[r] != root) continue;
                    int length = starts[r + 1] - starts[r];
                    groupStarts[i++] = n;
                    Array.Copy(ws.Vertices, starts[r], v, n, length);
                    n += length;
                    if (r < ringSplit) { groupRingSplit++; groupSplit = n; }
                }
                groupStarts[i] = n;
                result = ConnectedRings(group, groupStarts, groupRingSplit, groupSplit, rule, intersectionOnly);
            }
            finally { Workspace.Return(group); }
            first.Add(result.FirstArea); second.Add(result.SecondArea); intersection.Add(result.IntersectionArea);
            union.Add(result.UnionArea); difference.Add(result.SymmetricDifferenceArea);
            statistics.Crossings += result.CrossingCount;
            statistics.ExactPredicates += result.ExactPredicateCount;
            statistics.SymbolicTieBreaks += result.SymbolicTieBreakCount;
        }
        return new WindingOverlapResult(first.Value, second.Value, intersection.Value, union.Value, difference.Value, rule, statistics);

        int Root(int r)
        {
            while (parents[r] != r) { parents[r] = parents[parents[r]]; r = parents[r]; }
            return r;
        }
    }

    private readonly struct RingBounds
    {
        private readonly double minX, minY, maxX, maxY;
        internal RingBounds(Point2[] v, int from, int to)
        {
            minX = maxX = v[from].X; minY = maxY = v[from].Y;
            for (int i = from + 1; i < to; i++)
            {
                minX = Math.Min(minX, v[i].X); minY = Math.Min(minY, v[i].Y);
                maxX = Math.Max(maxX, v[i].X); maxY = Math.Max(maxY, v[i].Y);
            }
        }
        internal bool Intersects(RingBounds b) => minX <= b.maxX && b.minX <= maxX && minY <= b.maxY && b.minY <= maxY;
    }

    private static WindingOverlapResult ConnectedRings(Workspace ws, int[] starts, int ringSplit,
        int split, PathFillRule rule, bool intersectionOnly)
    {
        var statistics = new WindingStatistics();
        int n = starts[starts.Length - 1], rings = starts.Length - 1;
        if (n == 0) return new WindingOverlapResult(0, 0, 0, 0, 0, rule, statistics);
        if (ws.Next.Length < n) ws.Next = new int[Grow(n)];
        int[] nx = ws.Next;
        for (int r = 0; r < rings; r++)
        {
            int from = starts[r], to = starts[r + 1];
            for (int e = from; e < to - 1; e++) nx[e] = e + 1;
            nx[to - 1] = from; // Never introduce an edge between rings.
        }

        // Certification and selected integer dispatch assume one closure and cannot admit these inputs.
        statistics.Crossings = FindCrossings(ws, n, split, ref statistics, singleRing: false);
        var seeds = new RingSeed[rings];
        Point2[] v = ws.Vertices;
        for (int r = 0; r < rings; r++)
        {
            int from = starts[r], to = starts[r + 1], first = Leftmost(v, from, to);
            bool isA = r < ringSplit;
            int own = RobustOrientation.Sign(v, Previous(first, from, to), first, nx[first], ref statistics) > 0 ? 0 : -1;
            int ownFrom = isA ? 0 : split, ownTo = isA ? split : n;
            // Use the same symbolic vertex indices as crossing detection, including at shared boundaries.
            own += WindingAt(v, nx, first, ownFrom, from, ref statistics);
            own += WindingAt(v, nx, first, to, ownTo, ref statistics);
            int other = WindingAt(v, nx, first, isA ? split : 0, isA ? n : split, ref statistics);
            seeds[r] = new RingSeed(first, to - from, own, other, isA);
        }

        bool nonZero = rule == PathFillRule.NonZero;
        ws.SharedCount = 0;
        Array.Clear(ws.Used, 0, MaxChains);
        Array.Clear(ws.Sums, 0, MaxChains);
        Walk(sum: false);
        int net = Net(ws);
        for (int x = 0; x < net; x++) Include(ws, ws.Shared[x], MaxChains);
        for (int c = 0; c < MaxChains; c++)
        {
            ws.Origin[c] = ws.Used[c] ? new Point2(ws.MinX[c] + (ws.MaxX[c] - ws.MinX[c]) / 2,
                ws.MinY[c] + (ws.MaxY[c] - ws.MinY[c]) / 2) : default;
            ws.Group[c] = c;
            for (int g = 0; g < c; g++)
                if (ws.Group[g] == g && WindingInput.Same(ws.Origin[g], ws.Origin[c])) { ws.Group[c] = g; break; }
        }
        Walk(sum: true);
        for (int x = 0; x < net; x++) Add(ws, ws.Shared[x], MaxChains);
        double aOnly = Area(ws, AOnly), bOnly = Area(ws, BOnly), both = Area(ws, Both);
        return new WindingOverlapResult(Area(ws, OwnA), Area(ws, OwnB), both,
            aOnly + bOnly + both, aOnly + bOnly, rule, statistics);

        void Walk(bool sum)
        {
            foreach (RingSeed seed in seeds)
                if (intersectionOnly)
                    WalkIntersection(ws, seed.Count, seed.First, seed.Own, seed.Other, nonZero, sum);
                else
                    WalkRegions(ws, seed.Count, seed.First, seed.Own, seed.Other, nonZero, seed.IsA, sum);
        }
    }
}
