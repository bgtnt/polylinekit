namespace DynamicIndexBenchmarks;

/// <summary>Benchmark contract only: finite closed axis-aligned bounds.</summary>
public readonly record struct Box2(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool Disjoint(Box2 b) => MaxX < b.MinX || b.MaxX < MinX || MaxY < b.MinY || b.MaxY < MinY;
    public bool Covers(Box2 b) => MinX <= b.MinX && MinY <= b.MinY && MaxX >= b.MaxX && MaxY >= b.MaxY;
}

/// <summary>Example-only dynamic edge index. IDs are fixed original outgoing-vertex slots.</summary>
public interface IEdgeIndex
{
    /// <summary>Write every active intersecting ID, including boundary contacts, once each.</summary>
    int Query(Box2 query, Span<int> ids);

    /// <summary>Replace kept and removed edges by the new kept edge; old bounds are snapshots.</summary>
    void ApplyShortcut(int keptId, Box2 oldKept, Box2 newKept, int removedId, Box2 oldRemoved);
}

/// <summary>Local benchmark plugin boundary, not a PolylineKit library API.</summary>
public interface IEdgeIndexFactory
{
    string Name { get; }
    IEdgeIndex Create(Box2[] initialBounds);
}
