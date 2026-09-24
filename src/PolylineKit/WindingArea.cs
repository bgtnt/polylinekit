namespace PolylineKit;

/// <summary>Winding-number area integrals of one closed walk.</summary>
/// <remarks>
/// With w the winding number of the walk: NonZero integrates 1[w != 0], EvenOdd integrates |w| mod 2,
/// AbsoluteWinding integrates |w| and Signed integrates w. All four come from one boundary pass.
/// Which edges cross, and every tie, is decided exactly for the binary64 input (see <see cref="WindingArea"/>);
/// the values carry ordinary floating-point rounding from crossing positions and summation, and are not clamped.
/// </remarks>
public readonly struct WindingAreaResult
{
    internal WindingAreaResult(double nonZero, double evenOdd, double absolute, double signed, WindingStatistics statistics)
    {
        NonZero = nonZero; EvenOdd = evenOdd; AbsoluteWinding = absolute; Signed = signed;
        CrossingCount = statistics.Crossings; ExactPredicateCount = statistics.ExactPredicates;
        SymbolicTieBreakCount = statistics.SymbolicTieBreaks;
    }

    /// <summary>Area where the winding number is nonzero, counted once, in squared coordinate units.</summary>
    public double NonZero { get; }
    /// <summary>Area where the winding number is odd, counted once.</summary>
    public double EvenOdd { get; }
    /// <summary>Integral of |w|: area covered repeatedly in the same direction counts repeatedly.</summary>
    /// <remarks>Opposite windings still cancel at the same point, so this is not total traversal without cancellation.</remarks>
    public double AbsoluteWinding { get; }
    /// <summary>Integral of w, the shoelace area: counterclockwise coverage is positive.</summary>
    public double Signed { get; }
    /// <summary>Number of proper crossings between nonadjacent edges, after symbolic tie-breaking.</summary>
    public int CrossingCount { get; }
    /// <summary>Orientation tests whose floating-point sign was not certified and was evaluated exactly.</summary>
    public int ExactPredicateCount { get; }
    /// <summary>Exactly degenerate orientation tests (shared, touching or collinear geometry) resolved symbolically.</summary>
    public int SymbolicTieBreakCount { get; }
}

/// <summary>Areas of two independently filled closed paths, from one boundary pass.</summary>
/// <remarks>Values carry floating-point rounding and are not clamped; no decimal grid is applied.</remarks>
public readonly struct WindingOverlapResult
{
    internal WindingOverlapResult(double first, double second, double intersection, double union, double difference,
        PathFillRule rule, WindingStatistics statistics)
    {
        FirstArea = first; SecondArea = second; IntersectionArea = intersection; UnionArea = union;
        SymmetricDifferenceArea = difference; FillRule = rule; CrossingCount = statistics.Crossings;
        ExactPredicateCount = statistics.ExactPredicates; SymbolicTieBreakCount = statistics.SymbolicTieBreaks;
    }

    /// <summary>Filled area of the first path under FillRule.</summary>
    public double FirstArea { get; }
    /// <summary>Filled area of the second path under FillRule.</summary>
    public double SecondArea { get; }
    /// <summary>Area filled by both paths.</summary>
    public double IntersectionArea { get; }
    /// <summary>Area filled by at least one path, with overlap counted once.</summary>
    public double UnionArea { get; }
    /// <summary>Area filled by exactly one path.</summary>
    public double SymmetricDifferenceArea { get; }
    /// <summary>SymmetricDifferenceArea / UnionArea; null when UnionArea is not positive. Smaller means more overlap.</summary>
    public double? JaccardDistance => UnionArea > 0 ? SymmetricDifferenceArea / UnionArea : null;
    /// <summary>IntersectionArea / UnionArea; null when UnionArea is not positive. Larger means more overlap.</summary>
    public double? IntersectionOverUnion => UnionArea > 0 ? IntersectionArea / UnionArea : null;
    /// <summary>The fill rule applied independently to each path.</summary>
    public PathFillRule FillRule { get; }
    /// <summary>Number of proper crossings between nonadjacent edges of either path, after symbolic tie-breaking.</summary>
    public int CrossingCount { get; }
    /// <summary>Orientation tests whose floating-point sign was not certified and was evaluated exactly.</summary>
    public int ExactPredicateCount { get; }
    /// <summary>Exactly degenerate orientation tests resolved symbolically.</summary>
    public int SymbolicTieBreakCount { get; }
}

/// <summary>
/// Area integrals computed directly from boundaries, without polygon clipping, output contours or a decimal grid.
/// </summary>
/// <remarks>
/// Edges are split at their crossings and each sub-edge contributes its shoelace term weighted by the change
/// of the chosen integrand across it. Winding numbers start at a leftmost vertex and are propagated across
/// crossings, so one wrong crossing decision would shift every later value. Crossing decisions therefore use
/// orientation signs that are certified by a floating-point filter or evaluated exactly, and exact ties
/// (shared vertices, vertices on edges, collinear overlap) are resolved by Simulation of Simplicity as one
/// consistent infinitesimal perturbation of the input. Area is continuous in the vertices, so the result is the
/// exact area of the given binary64 input up to rounding in crossing positions, products and summation.
/// Every value is the shoelace sum of a boundary chain formed exactly before any area term is evaluated:
/// crossing points are shared by both edges (and are the vertex itself where a crossing lies on one) and order
/// the events along each edge, collinearly overlapping edges are split at each other's endpoints, and identical
/// segments are netted by their integer coefficients. A sub-edge of any other edge contributes its share of its
/// edge's term, so a rounded crossing point does not distort the long edges around it. One path is summed
/// around the center of its bounds, and each chain of two filled paths around the center of its own remaining
/// segments, so shared boundaries cancel exactly and a small region or a small difference keeps its area next
/// to large or distant geometry. Rounding still grows with the distance of the remaining segments from their
/// center, which matters for one closed path whose parts lie far apart relative to their size. A crossing point
/// is rounded relative to the coordinates of the edges that form it, which matters for a small region cut out
/// by very long edges.
/// Time is O(n log n + m + k log k + s) for n edges, m candidate pairs from an axis-sorted sweep, k crossing and
/// overlap-split events, and s segments on collinearly overlapping edges, which are netted by hashing (expected
/// linear time, not a worst-case bound); m, k and s are O(n^2) in the worst case. Each call uses its own working storage: a per-thread workspace is
/// reused by consecutive calls, and a call made while another is active on the same thread (for example from
/// a list indexer) gets a separate one. Warm calls whose predicates are decided by the filter or by expansion
/// arithmetic allocate no managed memory; first use, buffer growth, nested calls and the integer path do, and
/// the per-thread workspace keeps its largest size. Results differ from the Clipper2-based methods by their
/// quantization.
/// </remarks>
public static class WindingArea
{
    /// <summary>Winding integrals of a closed path; closure from last to first is implicit.</summary>
    /// <remarks>
    /// A repeated closing point is optional. At least three vertices after consecutive duplicate removal are
    /// required. Self intersections, loops, retracing and collinear overlap are accepted.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The path is empty, has nonfinite or too large coordinates, or too few vertices.</exception>
    public static WindingAreaResult ClosedPath(IReadOnlyList<Point2> path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (path.Count == 0) throw new ArgumentException("The path must not be empty.", nameof(path));
        var workspace = WindingEngine.Workspace.Rent();
        try
        {
            Point2[] v = workspace.VertexBuffer(path.Count);
            int n = 0;
            for (int i = 0; i < path.Count; i++) WindingEngine.Append(v, ref n, 0, path[i], nameof(path));
            WindingEngine.CloseLoop(v, ref n, 0);
            if (n < 3) throw new ArgumentException("The path requires at least 3 vertices after duplicate removal.", nameof(path));
            return WindingEngine.SingleLoop(workspace, n);
        }
        finally { WindingEngine.Workspace.Return(workspace); }
    }

    /// <summary>Winding integrals of the closed walk first + reverse(second), joined by straight endpoint connectors.</summary>
    /// <remarks>
    /// This is the walk filled by <see cref="PolylineComparison.EndpointBridgedArea"/>; its NonZero and EvenOdd values
    /// agree with that method up to its clipping quantization. Each path needs at least two points after consecutive
    /// duplicate removal. Input order matters. A zero result does not imply equal strokes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A path is null.</exception>
    /// <exception cref="ArgumentException">A path is empty, has nonfinite or too large coordinates, or too few vertices.</exception>
    public static WindingAreaResult EndpointBridged(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        int firstLength = first.Count, secondLength = second.Count;
        if (firstLength == 0) throw new ArgumentException("The path must not be empty.", nameof(first));
        if (secondLength == 0) throw new ArgumentException("The path must not be empty.", nameof(second));
        var workspace = WindingEngine.Workspace.Rent();
        try
        {
            Point2[] v = workspace.VertexBuffer(firstLength + secondLength);
            int n = 0;
            for (int i = 0; i < firstLength; i++) WindingEngine.Append(v, ref n, 0, first[i], nameof(first));
            int firstCount = n, secondCount = 0;
            Point2 previous = default;
            for (int i = secondLength - 1; i >= 0; i--)
            {
                Point2 point = second[i];
                // Count this path independently: Append also removes a shared bridge endpoint.
                if (i == secondLength - 1 || !PathInput.Same(previous, point)) secondCount++;
                WindingEngine.Append(v, ref n, 0, point, nameof(second));
                previous = point;
            }
            if (firstCount < 2 || secondCount < 2) throw new ArgumentException("Each path requires at least 2 vertices after duplicate removal.");
            WindingEngine.CloseLoop(v, ref n, 0);
            return WindingEngine.SingleLoop(workspace, n);
        }
        finally { WindingEngine.Workspace.Return(workspace); }
    }

    /// <summary>Intersection, union and symmetric-difference areas of two independently filled closed paths.</summary>
    /// <remarks>
    /// Closure is implicit and a repeated closing point is optional. Each path needs at least three vertices after
    /// consecutive duplicate removal. The fill rule is applied to each path separately, as in
    /// <see cref="PolylineComparison.FilledRegionOverlap"/>, which computes the same quantities after quantization.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A path is null.</exception>
    /// <exception cref="ArgumentException">A path is empty, has nonfinite or too large coordinates, or too few vertices.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The fill rule is not defined.</exception>
    public static WindingOverlapResult FilledRegions(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        PathFillRule fillRule = PathFillRule.NonZero)
    {
        if (fillRule != PathFillRule.NonZero && fillRule != PathFillRule.EvenOdd) throw new ArgumentOutOfRangeException(nameof(fillRule));
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        if (first.Count == 0) throw new ArgumentException("The path must not be empty.", nameof(first));
        if (second.Count == 0) throw new ArgumentException("The path must not be empty.", nameof(second));
        var workspace = WindingEngine.Workspace.Rent();
        try
        {
            Point2[] v = workspace.VertexBuffer(first.Count + second.Count);
            int n = 0;
            for (int i = 0; i < first.Count; i++) WindingEngine.Append(v, ref n, 0, first[i], nameof(first));
            WindingEngine.CloseLoop(v, ref n, 0);
            int split = n;
            for (int i = 0; i < second.Count; i++) WindingEngine.Append(v, ref n, split, second[i], nameof(second));
            WindingEngine.CloseLoop(v, ref n, split);
            if (split < 3 || n - split < 3) throw new ArgumentException("Each path requires at least 3 vertices after duplicate removal.");
            return WindingEngine.TwoLoops(workspace, split, n, fillRule);
        }
        finally { WindingEngine.Workspace.Return(workspace); }
    }

}
