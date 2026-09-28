namespace PolylineKit;

internal static partial class WindingEngine
{
    // Preorder layout: a failed bounds test jumps to End, without a query stack.
    // Ring is an original ring index at a leaf, -1 at an internal node.
    internal struct RingNode
    {
        internal RingBounds Bounds;
        internal int End, Ring;
    }

    private static int RingRoot(int[] parents, int r)
    {
        while (parents[r] != r) { parents[r] = parents[parents[r]]; r = parents[r]; }
        return r;
    }

    // Only partitions inclusive bounds; it must not reorder geometric input vertices.
    internal static int GroupRings(Workspace ws, int rings)
    {
        int[] parents = ws.RingParents;
        RingBounds[] bounds = ws.RegionBounds;
        for (int r = 0; r < rings; r++) parents[r] = r;
        if (ws.Order.Length < rings) ws.Order = new int[Grow(rings)];
        if (ws.Candidates.Length < rings) ws.Candidates = new int[Grow(rings)];
        int groups = rings;
        // Small real-world features need no hierarchy construction or additional storage.
        if (rings <= 16)
        {
            for (int a = 0; a < rings && groups > 1; a++)
                for (int b = 0; b < a; b++)
                    if (bounds[a].Intersects(bounds[b])) Join(a, b);
            return groups;
        }

        if (ws.Keys.Length < rings) ws.Keys = new double[Grow(rings)];
        int capacity = checked(rings * 2 - 1);
        if (ws.RingNodes.Length < capacity) ws.RingNodes = new RingNode[Grow(capacity)];
        RingNode[] nodes = ws.RingNodes;
        int[] order = ws.Order;
        double[] keys = ws.Keys;
        for (int r = 0; r < rings; r++) order[r] = r;
        int used = 0;
        Build(0, rings);

        for (int r = 0; r < rings && groups > 1; r++)
        {
            RingBounds query = bounds[r];
            int node = 0;
            while (node < used && groups > 1)
            {
                RingNode item = nodes[node];
                if (!query.Intersects(item.Bounds)) { node = item.End; continue; }
                if (item.Ring >= 0 && item.Ring != r) Join(r, item.Ring);
                node++;
            }
        }
        return groups;

        void Join(int a, int b)
        {
            int x = RingRoot(parents, a), y = RingRoot(parents, b);
            if (x == y) return;
            // Canonical roots make group accumulation order independent of tree traversal/tied keys.
            parents[Math.Max(x, y)] = Math.Min(x, y);
            groups--;
        }

        void Build(int from, int count)
        {
            int at = used++;
            RingBounds box = bounds[order[from]];
            if (count == 1)
            {
                nodes[at] = new RingNode { Bounds = box, End = used, Ring = order[from] };
                return;
            }
            double minX = box.CenterX, maxX = minX, minY = box.CenterY, maxY = minY;
            for (int i = from + 1; i < from + count; i++)
            {
                RingBounds b = bounds[order[i]];
                box = new RingBounds(box, b);
                minX = Math.Min(minX, b.CenterX); maxX = Math.Max(maxX, b.CenterX);
                minY = Math.Min(minY, b.CenterY); maxY = Math.Max(maxY, b.CenterY);
            }
            // Center spread handles long, parallel separated rectangles whose full extents overlap.
            bool y = maxY - minY > maxX - minX;
            for (int i = from; i < from + count; i++) keys[i] = y ? bounds[order[i]].CenterY : bounds[order[i]].CenterX;
            Array.Sort(keys, order, from, count);
            int leftCount = count / 2;
            Build(from, leftCount);
            Build(from + leftCount, count - leftCount);
            nodes[at] = new RingNode { Bounds = box, End = used, Ring = -1 };
        }
    }
}
