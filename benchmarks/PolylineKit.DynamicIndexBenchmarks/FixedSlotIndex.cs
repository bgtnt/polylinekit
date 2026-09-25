namespace DynamicIndexBenchmarks;

public sealed class FixedSlotIndexFactory : IEdgeIndexFactory
{
    public string Name => "fixed-slots";
    public IEdgeIndex Create(Box2[] initialBounds) => new FixedSlotIndex(initialBounds);
}

/// <summary>
/// A fixed binary hierarchy over original outgoing-edge IDs. Leaves never move or rotate;
/// shortcuts change one leaf and deactivate another. Original input order determines grouping.
/// Heap-layout parent links are implicit, and construction takes linear time and storage.
/// </summary>
public sealed class FixedSlotIndex : IEdgeIndex
{
    private readonly Box2[] bounds;
    private readonly int[] activeCounts;
    private readonly bool[] active;
    private readonly int leafBase;

    public FixedSlotIndex(Box2[] initialBounds)
    {
        ArgumentNullException.ThrowIfNull(initialBounds);
        foreach (var box in initialBounds) IndexValidation.Box(box, nameof(initialBounds));
        int count = initialBounds.Length;
        int nodes = count == 0 ? 0 : checked(count + (count - 1));
        bounds = new Box2[nodes];
        activeCounts = new int[nodes];
        active = new bool[count];
        active.AsSpan().Fill(true);
        leafBase = count - 1;
        for (int i = 0; i < count; i++)
        {
            bounds[leafBase + i] = initialBounds[i];
            activeCounts[leafBase + i] = 1;
        }
        for (int i = leafBase - 1; i >= 0; i--) Refit(i);
    }

    /// <summary>
    /// Returns active inclusive hits in unspecified order. No managed allocation is needed.
    /// An insufficient destination throws ArgumentException and may contain a partial result.
    /// </summary>
    public int Query(Box2 query, Span<int> ids)
    {
        IndexValidation.Box(query, nameof(query));
        if (bounds.Length == 0) return 0;
        // An int-indexed complete binary hierarchy has fewer than 32 levels. DFS holds at
        // most one pending sibling per level. Negative entries mark already covered branches.
        Span<int> stack = stackalloc int[32];
        int pending = 1, count = 0;
        stack[0] = 0;
        while (pending > 0)
        {
            int entry = stack[--pending];
            bool covered = entry < 0;
            int node = covered ? ~entry : entry;
            if (activeCounts[node] == 0 || !covered && bounds[node].Disjoint(query)) continue;
            if (node >= leafBase)
            {
                int id = node - leafBase;
                if (!active[id]) continue;
                if (count == ids.Length) throw new ArgumentException("The destination cannot hold all query hits.", nameof(ids));
                ids[count++] = id;
                continue;
            }
            covered = covered || query.Covers(bounds[node]);
            int left = node * 2 + 1, right = left + 1;
            stack[pending++] = covered ? ~right : right;
            stack[pending++] = covered ? ~left : left;
        }
        return count;
    }

    public void ApplyShortcut(int keptId, Box2 oldKept, Box2 newKept, int removedId, Box2 oldRemoved)
    {
        IndexValidation.Ids(active, keptId, removedId);
        IndexValidation.Box(newKept, nameof(newKept));
        int keptLeaf = leafBase + keptId, removedLeaf = leafBase + removedId;
        IndexValidation.OldBounds(bounds[keptLeaf], oldKept, bounds[removedLeaf], oldRemoved);

        // Validation above precedes every mutation, including stale-input checks.
        bounds[keptLeaf] = newKept;
        active[removedId] = false;
        activeCounts[removedLeaf] = 0;

        // Higher heap indices are lower in the tree or later at the same depth. Advancing
        // the larger ancestor first refits both child paths before their shared ancestor.
        int a = Parent(keptLeaf), b = Parent(removedLeaf);
        while (a != b)
        {
            if (a > b) { Refit(a); a = Parent(a); }
            else { Refit(b); b = Parent(b); }
        }
        while (a >= 0) { Refit(a); a = Parent(a); }
    }

    private static int Parent(int node) => (node - 1) >> 1;

    private void Refit(int node)
    {
        int left = node * 2 + 1, right = left + 1;
        int leftCount = activeCounts[left], rightCount = activeCounts[right];
        activeCounts[node] = leftCount + rightCount;
        if (leftCount == 0) bounds[node] = rightCount == 0 ? default : bounds[right];
        else if (rightCount == 0) bounds[node] = bounds[left];
        else
        {
            Box2 a = bounds[left], b = bounds[right];
            bounds[node] = new(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY),
                Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
        }
    }
}
