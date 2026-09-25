namespace DynamicIndexBenchmarks;

public sealed class DenseLinearIndexFactory : IEdgeIndexFactory
{
    public string Name => "dense-linear";
    public IEdgeIndex Create(Box2[] initialBounds) => new DenseLinearIndex(initialBounds);
}

/// <summary>
/// Linear scanning over only active edge IDs. Removing a slot swaps the final live ID into
/// its position; bounds remain indexed by stable original ID. Query order may therefore change.
/// </summary>
public sealed class DenseLinearIndex : IEdgeIndex
{
    private readonly Box2[] bounds;
    private readonly int[] ids;
    private readonly int[] positions;
    private int activeCount;

    public DenseLinearIndex(Box2[] initialBounds)
    {
        ArgumentNullException.ThrowIfNull(initialBounds);
        foreach (var box in initialBounds) IndexValidation.Box(box, nameof(initialBounds));
        bounds = (Box2[])initialBounds.Clone();
        ids = new int[bounds.Length];
        positions = new int[bounds.Length];
        activeCount = bounds.Length;
        for (int i = 0; i < bounds.Length; i++) ids[i] = positions[i] = i;
    }

    /// <summary>
    /// Returns every active inclusive hit. An insufficient destination throws ArgumentException
    /// and may already contain a partial result. The warm path allocates no managed storage.
    /// </summary>
    public int Query(Box2 query, Span<int> destination)
    {
        IndexValidation.Box(query, nameof(query));
        int found = 0;
        for (int position = 0; position < activeCount; position++)
        {
            int id = ids[position];
            if (bounds[id].Disjoint(query)) continue;
            if (found == destination.Length)
                throw new ArgumentException("The destination cannot hold all query hits.", nameof(destination));
            destination[found++] = id;
        }
        return found;
    }

    public void ApplyShortcut(int keptId, Box2 oldKept, Box2 newKept, int removedId, Box2 oldRemoved)
    {
        if ((uint)keptId >= (uint)positions.Length) throw new ArgumentOutOfRangeException(nameof(keptId));
        if ((uint)removedId >= (uint)positions.Length) throw new ArgumentOutOfRangeException(nameof(removedId));
        if (keptId == removedId) throw new ArgumentException("A shortcut requires two distinct active edge IDs.");
        if (positions[keptId] < 0 || positions[removedId] < 0)
            throw new InvalidOperationException("Both shortcut edge IDs must be active.");
        IndexValidation.Box(newKept, nameof(newKept));
        IndexValidation.OldBounds(bounds[keptId], oldKept, bounds[removedId], oldRemoved);

        // All checks precede mutation. This also works when the removed ID is already last,
        // or when the kept ID is last and moves to the removed ID's former dense position.
        bounds[keptId] = newKept;
        int removedPosition = positions[removedId];
        int lastId = ids[--activeCount];
        ids[removedPosition] = lastId;
        positions[lastId] = removedPosition;
        positions[removedId] = -1;
    }
}
