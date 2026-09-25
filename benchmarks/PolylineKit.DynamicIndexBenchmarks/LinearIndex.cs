namespace DynamicIndexBenchmarks;

public sealed class LinearIndexFactory : IEdgeIndexFactory
{
    public string Name => "linear";
    public IEdgeIndex Create(Box2[] initialBounds) => new LinearIndex(initialBounds);
}

/// <summary>Mutable reference baseline: scan the bounds of every original edge slot.</summary>
public sealed class LinearIndex : IEdgeIndex
{
    private readonly Box2[] bounds;
    private readonly bool[] active;

    public LinearIndex(Box2[] initialBounds)
    {
        ArgumentNullException.ThrowIfNull(initialBounds);
        foreach (var box in initialBounds) IndexValidation.Box(box, nameof(initialBounds));
        bounds = (Box2[])initialBounds.Clone();
        active = new bool[bounds.Length];
        active.AsSpan().Fill(true);
    }

    /// <summary>
    /// Returns all active inclusive bounding-box hits. If the destination is too small,
    /// throws ArgumentException; the destination may already contain a prefix of the hits.
    /// </summary>
    public int Query(Box2 query, Span<int> ids)
    {
        IndexValidation.Box(query, nameof(query));
        int count = 0;
        for (int i = 0; i < bounds.Length; i++)
        {
            if (!active[i] || bounds[i].Disjoint(query)) continue;
            if (count == ids.Length) throw new ArgumentException("The destination cannot hold all query hits.", nameof(ids));
            ids[count++] = i;
        }
        return count;
    }

    public void ApplyShortcut(int keptId, Box2 oldKept, Box2 newKept, int removedId, Box2 oldRemoved)
    {
        IndexValidation.Ids(active, keptId, removedId);
        IndexValidation.Box(newKept, nameof(newKept));
        IndexValidation.OldBounds(bounds[keptId], oldKept, bounds[removedId], oldRemoved);
        bounds[keptId] = newKept;
        active[removedId] = false;
    }
}

internal static class IndexValidation
{
    internal static void Box(Box2 box, string parameter)
    {
        if (!double.IsFinite(box.MinX) || !double.IsFinite(box.MinY) ||
            !double.IsFinite(box.MaxX) || !double.IsFinite(box.MaxY) ||
            box.MinX > box.MaxX || box.MinY > box.MaxY)
            throw new ArgumentException("Bounds must be finite and ordered; zero extent is allowed.", parameter);
    }

    internal static void Ids(bool[] active, int keptId, int removedId)
    {
        if ((uint)keptId >= (uint)active.Length) throw new ArgumentOutOfRangeException(nameof(keptId));
        if ((uint)removedId >= (uint)active.Length) throw new ArgumentOutOfRangeException(nameof(removedId));
        if (keptId == removedId) throw new ArgumentException("A shortcut requires two distinct active edge IDs.");
        if (!active[keptId] || !active[removedId]) throw new InvalidOperationException("Both shortcut edge IDs must be active.");
    }

    internal static void OldBounds(Box2 currentKept, Box2 oldKept, Box2 currentRemoved, Box2 oldRemoved)
    {
        if (currentKept != oldKept) throw new ArgumentException("Kept edge bounds are stale.", nameof(oldKept));
        if (currentRemoved != oldRemoved) throw new ArgumentException("Removed edge bounds are stale.", nameof(oldRemoved));
    }
}
