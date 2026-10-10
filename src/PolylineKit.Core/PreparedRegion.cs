namespace PolylineKit;

/// <summary>An immutable snapshot of closed rings contributing to one region's winding field.</summary>
/// <remarks>
/// Rings are implicitly closed and are never joined by artificial edges. Preparation copies and validates
/// coordinates, removes consecutive and closing duplicates, and records bounds. The polygon factories
/// normalize directions from explicit shell/hole roles; FromRings preserves raw winding directions.
/// Preparation does not infer nesting, repair topology or cache pairwise intersections. Instances can be
/// shared across threads.
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

    /// <summary>Copies one polygon, orienting its exterior positively and its holes negatively.</summary>
    /// <remarks>
    /// Roles are supplied explicitly: no nesting inference, topology validation or repair is performed.
    /// Directions are normalized using <see cref="WindingArea.ClosedPath"/>'s Signed result; a ring with
    /// exactly zero Signed area retains its supplied order after duplicate removal. Other rings are changed
    /// only by reversal. For an ordinary simple exterior with disjoint simple holes inside it, NonZero and
    /// EvenOdd give the same filled region. This agreement is not guaranteed for self-intersecting rings,
    /// overlapping holes or holes outside the exterior. Use the default NonZero rule for polygon roles.
    /// Every ring requires at least three vertices after duplicate removal; collinear rings are accepted.
    /// Coordinates must be finite with magnitude at most 1e100. Inputs must remain stable during this call;
    /// subsequent changes do not affect the snapshot. A null or empty holes collection means no holes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The exterior is null.</exception>
    /// <exception cref="ArgumentException">A hole is null, or a ring has invalid coordinates or too few vertices.</exception>
    public static PreparedRegion FromPolygon(IReadOnlyList<Point2> exterior,
        IReadOnlyList<IReadOnlyList<Point2>>? holes = null)
    {
        if (exterior is null) throw new ArgumentNullException(nameof(exterior));
        int holeCount = holes?.Count ?? 0;
        var rings = new IReadOnlyList<Point2>[checked(holeCount + 1)];
        rings[0] = exterior;
        for (int h = 0; h < holeCount; h++)
            rings[h + 1] = holes![h] ?? throw new ArgumentException("A hole is null.", nameof(holes));
        PreparedRegion region = FromRings(rings);
        OrientRing(region, 0, positive: true);
        for (int r = 1; r < region.RingCount; r++) OrientRing(region, r, positive: false);
        return region;
    }

    /// <summary>Copies polygons with explicit exterior/hole roles into one prepared region.</summary>
    /// <remarks>
    /// Each exterior is oriented positively and each hole negatively by the same contract as
    /// <see cref="FromPolygon"/>. Exactly zero Signed area retains the supplied order. Polygon and ring
    /// order are retained; no nesting inference or repair is performed. Under the recommended default
    /// NonZero rule, overlapping ordinary valid polygons contribute their union. Under EvenOdd,
    /// overlapping parts cancel. Consequently the two rules need not agree for overlapping polygons.
    /// An empty collection represents an empty region. Inputs must remain stable during the call;
    /// subsequent changes do not affect the snapshot.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The polygon collection is null.</exception>
    /// <exception cref="ArgumentException">An exterior or hole is null, or a ring has invalid coordinates or too few vertices.</exception>
    public static PreparedRegion FromPolygons(
        IReadOnlyList<(IReadOnlyList<Point2> Exterior, IReadOnlyList<IReadOnlyList<Point2>>? Holes)> polygons)
    {
        if (polygons is null) throw new ArgumentNullException(nameof(polygons));
        var rings = new List<IReadOnlyList<Point2>>();
        var exteriorRoles = new List<bool>();
        for (int p = 0; p < polygons.Count; p++)
        {
            var polygon = polygons[p];
            rings.Add(polygon.Exterior ?? throw new ArgumentException("An exterior is null.", nameof(polygons)));
            exteriorRoles.Add(true);
            if (polygon.Holes is null) continue;
            for (int h = 0; h < polygon.Holes.Count; h++)
            {
                rings.Add(polygon.Holes[h] ?? throw new ArgumentException("A hole is null.", nameof(polygons)));
                exteriorRoles.Add(false);
            }
        }
        PreparedRegion region = FromRings(rings);
        for (int r = 0; r < region.RingCount; r++) OrientRing(region, r, exteriorRoles[r]);
        return region;
    }

    /// <summary>Copies rings without assigning shell/hole roles or changing their directions.</summary>
    /// <remarks>
    /// An empty collection represents an empty region. Every ring must have at least three vertices after
    /// duplicate removal; collinear rings are allowed. Coordinates must be finite with magnitude at most 1e100.
    /// Inputs must remain stable during this call, but may be changed afterwards without affecting the snapshot.
    /// Under NonZero, an ordinary hole has the opposite direction to its shell. Under EvenOdd direction is
    /// irrelevant. Overlapping rings combine their winding; they are not independently unioned.
    /// Warning: a 10-by-10 shell and same-direction 4-by-4 hole give area 100 under NonZero versus 84
    /// under EvenOdd; use FromPolygon to assign shell/hole roles without depending on input directions.
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

    private static void OrientRing(PreparedRegion region, int ring, bool positive)
    {
        int start = region.Starts[ring], length = region.Starts[ring + 1] - start;
        var points = new Point2[length];
        Array.Copy(region.Vertices, start, points, 0, length);
        double signed = WindingArea.ClosedPath(points).Signed;
        if (positive ? signed < 0 : signed > 0) Array.Reverse(region.Vertices, start, length);
    }
}
