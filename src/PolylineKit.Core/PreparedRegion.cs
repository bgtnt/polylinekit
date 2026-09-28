namespace PolylineKit;

/// <summary>An immutable snapshot of closed rings contributing to one region's winding field.</summary>
/// <remarks>
/// Rings are implicitly closed and are never joined by artificial edges. Preparation copies and validates
/// coordinates, removes consecutive and closing duplicates, and records bounds. It does not infer holes,
/// reverse rings, repair topology or cache pairwise intersections. Instances can be shared across threads.
/// </remarks>
public sealed class PreparedRegion
{
    internal static readonly PreparedRegion Empty = new PreparedRegion(Array.Empty<Point2>(), new[] { 0 }, Array.Empty<WindingEngine.RingBounds>(), null);
    internal readonly Point2[] Vertices;
    internal readonly int[] Starts;
    internal readonly WindingEngine.RingBounds[] RingBounds;

    private PreparedRegion(Point2[] vertices, int[] starts, WindingEngine.RingBounds[] bounds, Bounds2D? overall)
    { Vertices = vertices; Starts = starts; RingBounds = bounds; Bounds = overall; }

    /// <summary>Number of independently closed rings, including any zero-area rings.</summary>
    public int RingCount => Starts.Length - 1;
    /// <summary>Number of vertices after consecutive and closing duplicate removal.</summary>
    public int VertexCount => Vertices.Length;
    /// <summary>Bounds of all stored coordinates; null for an empty collection.</summary>
    public Bounds2D? Bounds { get; }

    /// <summary>Copies rings without assigning shell/hole roles or changing their directions.</summary>
    /// <remarks>
    /// An empty collection represents an empty region. Every ring must have at least three vertices after
    /// duplicate removal; collinear rings are allowed. Coordinates must be finite with magnitude at most 1e100.
    /// Inputs must remain stable during this call, but may be changed afterwards without affecting the snapshot.
    /// Under NonZero, an ordinary hole has the opposite direction to its shell. Under EvenOdd direction is
    /// irrelevant. Overlapping rings combine their winding; they are not independently unioned.
    /// </remarks>
    public static PreparedRegion FromRings(IReadOnlyList<IReadOnlyList<Point2>> rings)
    {
        if (rings is null) throw new ArgumentNullException(nameof(rings));
        int count = rings.Count;
        if (count == 0) return Empty;
        var paths = new IReadOnlyList<Point2>[count];
        var lengths = new int[count];
        int capacity = 0;
        for (int r = 0; r < count; r++)
        {
            paths[r] = rings[r] ?? throw new ArgumentException("A ring is null.", nameof(rings));
            lengths[r] = paths[r].Count;
            capacity = checked(capacity + lengths[r]);
        }
        var vertices = new Point2[capacity];
        var starts = new int[checked(count + 1)];
        var bounds = new WindingEngine.RingBounds[count];
        Bounds2D? overall = null;
        int n = 0;
        for (int r = 0; r < count; r++)
        {
            int start = n;
            starts[r] = start;
            for (int p = 0; p < lengths[r]; p++) WindingEngine.Append(vertices, ref n, start, paths[r][p], nameof(rings));
            WindingEngine.CloseLoop(vertices, ref n, start);
            if (n - start < 3) throw new ArgumentException("Each ring requires at least 3 vertices after duplicate removal.", nameof(rings));
            bounds[r] = new WindingEngine.RingBounds(vertices, start, n);
            Bounds2D b = bounds[r].Bounds;
            overall = overall.HasValue ? overall.Value.Union(b) : b;
        }
        starts[count] = n;
        if (n != vertices.Length) Array.Resize(ref vertices, n);
        return new PreparedRegion(vertices, starts, bounds, overall);
    }
}
