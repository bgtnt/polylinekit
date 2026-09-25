namespace RegionCoverage;

/// <summary>Example-only immutable 32-way index of inclusive axis-aligned bounds.</summary>
/// <remarks>
/// This independently written experiment uses X-sliced/Y-ordered leaf packing: sort centers by X,
/// divide into approximately square slices, sort each slice by Y, then form leaves of at most 32
/// items. Upper levels group consecutive nodes, without re-sorting. This is not a dynamic R-tree
/// or a claim to implement STR packing at every level. Every subtree owns one contiguous item
/// range, so a fully covered subtree can emit IDs without testing individual bounds.
/// Construction copies bounds; callers may subsequently change their input array. Queries are
/// read-only and allocate nothing. Output order is deterministic packing order, not input order.
/// </remarks>
internal sealed class PackedBoundsIndex
{
    private const int Fanout = 32;
    private readonly RegionBounds[] items, nodes;
    private readonly int[] ids, childStarts, childCounts, itemStarts, itemCounts;
    private readonly int root;

    internal PackedBoundsIndex(RegionBounds[] boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        foreach (RegionBounds b in boxes) Validate(b);
        int count = boxes.Length;
        ids = new int[count]; items = new RegionBounds[count];
        if (count == 0)
        {
            nodes = []; childStarts = []; childCounts = []; itemStarts = []; itemCounts = [];
            root = -1; return;
        }
        for (int i = 0; i < count; i++) ids[i] = i;
        double CenterX(int i) => boxes[i].MinX * .5 + boxes[i].MaxX * .5;
        double CenterY(int i) => boxes[i].MinY * .5 + boxes[i].MaxY * .5;
        var byX = Comparer<int>.Create((a, b) =>
        {
            int c = CenterX(a).CompareTo(CenterX(b));
            if (c == 0) c = CenterY(a).CompareTo(CenterY(b));
            return c != 0 ? c : a.CompareTo(b);
        });
        var byY = Comparer<int>.Create((a, b) =>
        {
            int c = CenterY(a).CompareTo(CenterY(b));
            if (c == 0) c = CenterX(a).CompareTo(CenterX(b));
            return c != 0 ? c : a.CompareTo(b);
        });
        Array.Sort(ids, byX);
        int leaves = Groups(count), slices = (int)Math.Ceiling(Math.Sqrt(leaves));
        int sliceSize = (int)Math.Min((long)((leaves + slices - 1) / slices) * Fanout, count);
        for (int start = 0; start < count;)
        {
            int length = Math.Min(sliceSize, count - start);
            Array.Sort(ids, start, length, byY); start += length;
        }
        for (int i = 0; i < count; i++) items[i] = boxes[ids[i]];

        int nodeCount = leaves;
        for (int level = leaves; level > 1;) { level = Groups(level); nodeCount += level; }
        nodes = new RegionBounds[nodeCount]; childStarts = new int[nodeCount]; childCounts = new int[nodeCount];
        itemStarts = new int[nodeCount]; itemCounts = new int[nodeCount];
        for (int leaf = 0; leaf < leaves; leaf++)
        {
            int start = leaf * Fanout, length = Math.Min(Fanout, count - start);
            itemStarts[leaf] = start; itemCounts[leaf] = length;
            RegionBounds bounds = items[start];
            for (int i = 1; i < length; i++) bounds = Union(bounds, items[start + i]);
            nodes[leaf] = bounds;
        }
        int levelStart = 0, levelCount = leaves, next = leaves;
        while (levelCount > 1)
        {
            int parentStart = next;
            for (int offset = 0; offset < levelCount;)
            {
                int start = levelStart + offset, length = Math.Min(Fanout, levelCount - offset);
                RegionBounds bounds = nodes[start];
                for (int i = 1; i < length; i++) bounds = Union(bounds, nodes[start + i]);
                nodes[next] = bounds; childStarts[next] = start; childCounts[next] = length;
                itemStarts[next] = itemStarts[start];
                int last = start + length - 1;
                itemCounts[next] = itemStarts[last] + itemCounts[last] - itemStarts[start];
                next++; offset += length;
            }
            levelStart = parentStart; levelCount = next - parentStart;
        }
        root = next - 1;
    }

    /// <summary>Write all intersecting input IDs. Throw if the destination cannot hold all matches.</summary>
    /// <remarks>On insufficient capacity, an initial part of the destination may already be written.</remarks>
    internal int Query(RegionBounds query, Span<int> destination)
    {
        Validate(query);
        int written = 0;
        if (root >= 0) Visit(root, query, destination, ref written);
        return written;
    }

    private void Visit(int node, RegionBounds query, Span<int> destination, ref int written)
    {
        if (query.Disjoint(nodes[node])) return;
        int start = itemStarts[node], count = itemCounts[node];
        if (query.Covers(nodes[node]))
        {
            if (count > destination.Length - written) throw new ArgumentException("Destination cannot hold all matches.", nameof(destination));
            ids.AsSpan(start, count).CopyTo(destination[written..]); written += count;
        }
        else if (childCounts[node] == 0)
        {
            for (int i = start, end = start + count; i < end; i++)
                if (!query.Disjoint(items[i]))
                {
                    if (written == destination.Length) throw new ArgumentException("Destination cannot hold all matches.", nameof(destination));
                    destination[written++] = ids[i];
                }
        }
        else
            for (int i = childStarts[node], end = i + childCounts[node]; i < end; i++) Visit(i, query, destination, ref written);
    }

    private static int Groups(int count) => (count - 1) / Fanout + 1;
    private static RegionBounds Union(RegionBounds a, RegionBounds b) =>
        new(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
    private static void Validate(RegionBounds b)
    {
        if (!double.IsFinite(b.MinX) || !double.IsFinite(b.MinY) || !double.IsFinite(b.MaxX) || !double.IsFinite(b.MaxY) ||
            b.MinX > b.MaxX || b.MinY > b.MaxY) throw new ArgumentException("Bounds must be finite and ordered.");
    }
}
