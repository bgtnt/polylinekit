using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Experimental area-only sweep of bounded integer closed walks.</summary>
/// <remarks>
/// No input rounding: coordinates must be integers in [-524288,524288], with at most 8192 supplied vertices
/// across the operation and at least three per loop. Loops close independently; no connector edges are added.
/// Topology and event ordering use exact integer/rational arithmetic; the final integrals use binary64.
/// This mutable reusable instance is neither thread-safe nor reentrant. Its endpoint-band rebuild and
/// insertion-sort inversion enumeration deliberately target few-level degenerate grids, not arbitrary paths.
/// </remarks>
internal sealed class IntegerScanbeam
{
    private const int CoordinateLimit = 524288, Int64CoordinateLimit = 2048, VertexLimit = 8192;

    private readonly struct Edge(long y0, long y1, long dx, long dy, long intercept, int delta, int loop)
    {
        internal readonly long Y0 = y0, Y1 = y1, Dx = dx, Dy = dy, B = intercept;
        internal readonly int Delta = delta, Loop = loop;
    }

    private readonly record struct Level(long N, long D)
    {
        // Explicit unchecked only for the proven bounded geometric expressions; project-wide checked
        // arithmetic remains enabled for input conversion, array sizes, indices and counters.
        internal static int Compare(Level a, Level b) =>
            unchecked((Int128)a.N * b.D).CompareTo(unchecked((Int128)b.N * a.D));
        internal static int Compare64(Level a, Level b) =>
            unchecked(a.N * b.D).CompareTo(unchecked(b.N * a.D));
    }

    private readonly record struct Crossing(Level Y, int A, int B);

    private Point2[] vertices = [];
    private Edge[] edges = [];
    private long[] levels = [];
    private int[] active = [], topOrder = [], positions = [], prefix = [], prefixB = [];
    private Level[] lastLevel = [];
    private Crossing[] crossings = [];
    private int edgeCount, crossingCount;
    private long bottom, top;
    private bool nonZero, useInt64, intersectTwoLoops;
    private Sum area;
    private readonly Comparison<int> bottomComparison;
    private readonly Comparison<int> slopeComparison;
    private readonly Comparison<Crossing> crossingComparison;

    internal IntegerScanbeam()
    {
        bottomComparison = (x, y) => CompareAtInteger(x, y, bottom, false);
        slopeComparison = CompareSlope;
        crossingComparison = ComparePoint;
    }

    internal long EventCount { get; private set; }
    internal long EventGroupCount { get; private set; }
    internal int BandCount { get; private set; }
    internal int PeakActiveCount { get; private set; }

    internal double Measure(IReadOnlyList<Point2> path, PathFillRule fillRule = PathFillRule.NonZero)
    {
        if (fillRule != PathFillRule.NonZero && fillRule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(fillRule));
        ArgumentNullException.ThrowIfNull(path);
        int count = path.Count;
        if (count < 3 || count > VertexLimit)
            throw new ArgumentException("The experimental sweep requires 3..8192 supplied vertices.", nameof(path));
        EnsureVertices(count);
        useInt64 = true;
        CopyLoop(path, count, 0, nameof(path));
        edgeCount = 0;
        AppendEdges(0, count, 0);
        return Sweep(count, fillRule, false);
    }

    /// <summary>Intersection area of two independently filled, implicitly closed integer loops.</summary>
    /// <remarks>
    /// Each loop requires at least three supplied vertices; at most 8192 are accepted in total. Repeated
    /// vertices, optional repeated closure, self intersections and retracing are accepted. Coordinates are
    /// validated exactly against [-524288,524288], without rounding. The fill rule applies to each loop
    /// separately. The same mutable-instance and approximate area-value contract as Measure applies.
    /// </remarks>
    internal double MeasureIntersection(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        PathFillRule fillRule = PathFillRule.NonZero)
    {
        if (fillRule != PathFillRule.NonZero && fillRule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(fillRule));
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        int firstCount = first.Count, secondCount = second.Count;
        if (firstCount < 3) throw new ArgumentException("Each loop requires at least three supplied vertices.", nameof(first));
        if (secondCount < 3) throw new ArgumentException("Each loop requires at least three supplied vertices.", nameof(second));
        if (firstCount > VertexLimit || secondCount > VertexLimit - firstCount)
            throw new ArgumentException("The experimental sweep accepts at most 8192 supplied vertices in total.");
        int count = firstCount + secondCount;
        EnsureVertices(count);
        useInt64 = true;
        CopyLoop(first, firstCount, 0, nameof(first));
        CopyLoop(second, secondCount, firstCount, nameof(second));
        edgeCount = 0;
        AppendEdges(0, firstCount, 0);
        AppendEdges(firstCount, count, 1);
        return Sweep(count, fillRule, true);
    }

    private void CopyLoop(IReadOnlyList<Point2> path, int count, int offset, string name)
    {
        for (int i = 0; i < count; i++)
        {
            Point2 p = path[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > CoordinateLimit ||
                Math.Abs(p.Y) > CoordinateLimit || p.X != Math.Truncate(p.X) || p.Y != Math.Truncate(p.Y))
                throw new ArgumentException("Coordinates must be exact integers in [-524288,524288].", name);
            if (p.X < -Int64CoordinateLimit || p.X > Int64CoordinateLimit ||
                p.Y < -Int64CoordinateLimit || p.Y > Int64CoordinateLimit) useInt64 = false;
            vertices[offset + i] = p;
            levels[offset + i] = (long)p.Y;
        }
    }

    private void AppendEdges(int from, int to, int loop)
    {
        for (int i = from; i < to; i++)
        {
            Point2 a = vertices[i], b = vertices[i + 1 == to ? from : i + 1];
            if (a.Y == b.Y) continue; // Horizontal edges have zero vertical-sweep measure.
            int delta = a.Y < b.Y ? 1 : -1;
            if (delta < 0) (a, b) = (b, a);
            long x = (long)a.X, y = (long)a.Y, dx = (long)b.X - x, dy = (long)b.Y - y;
            edges[edgeCount++] = new(y, (long)b.Y, dx, dy, x * dy - y * dx, delta, loop);
        }
    }

    private double Sweep(int count, PathFillRule fillRule, bool intersection)
    {
        Array.Sort(levels, 0, count);
        int levelCount = 0;
        for (int i = 0; i < count; i++)
            if (levelCount == 0 || levels[i] != levels[levelCount - 1]) levels[levelCount++] = levels[i];
        nonZero = fillRule == PathFillRule.NonZero;
        intersectTwoLoops = intersection;
        area = default;
        EventCount = EventGroupCount = 0;
        BandCount = PeakActiveCount = 0;
        for (int band = 0; band + 1 < levelCount; band++)
        {
            bottom = levels[band]; top = levels[band + 1];
            int n = 0;
            for (int e = 0; e < edgeCount; e++)
                if (edges[e].Y0 <= bottom && edges[e].Y1 >= top) active[n++] = e;
            if (n == 0) continue;
            BandCount++;
            PeakActiveCount = Math.Max(PeakActiveCount, n);
            active.AsSpan(0, n).Sort(bottomComparison);
            var start = new Level(bottom, 1);
            for (int i = 0; i < n; i++)
            {
                int id = active[i];
                topOrder[i] = id; positions[id] = i; lastLevel[i] = start;
            }
            SetWinding(0, n, 0, 0);
            FindCrossings(n);
            crossings.AsSpan(0, crossingCount).Sort(crossingComparison);
            for (int c = 0; c < crossingCount;)
            {
                Crossing first = crossings[c];
                int end = c + 1, lo = Math.Min(positions[first.A], positions[first.B]),
                    hi = Math.Max(positions[first.A], positions[first.B]);
                while (end < crossingCount && ComparePoint(first, crossings[end]) == 0)
                {
                    lo = Math.Min(lo, Math.Min(positions[crossings[end].A], positions[crossings[end].B]));
                    hi = Math.Max(hi, Math.Max(positions[crossings[end].A], positions[crossings[end].B]));
                    end++;
                }
                // All edges through an interior multiway crossing form one contiguous block. Coincident
                // support-line cohorts are included in full because every member crosses the other slopes.
                int w = prefix[lo], wB = intersectTwoLoops ? prefixB[lo] : 0;
                for (int i = lo; i <= hi; i++)
                {
                    int id = active[i];
                    if (CompareX(id, first.A, first.Y) != 0)
                        throw new InvalidOperationException("Noncontiguous exact crossing block.");
                }
                // Close each affected gap, including the two bordering the crossing block, before its
                // bounding edges change. Unaffected gaps keep their earlier start level.
                for (int i = Math.Max(0, lo - 1); i <= Math.Min(n - 2, hi); i++) AccumulateGap(i, first.Y);
                active.AsSpan(lo, hi - lo + 1).Sort(slopeComparison);
                for (int i = lo; i <= hi; i++) positions[active[i]] = i;
                SetWinding(lo, hi + 1, w, wB);
                EventGroupCount++;
                c = end;
            }
            var finish = new Level(top, 1);
            for (int i = 0; i + 1 < n; i++) AccumulateGap(i, finish);
        }
        return area.Value;
    }

    // Sort the top-of-band order by insertion, recording exactly its inversions relative to bottom order.
    // At bottom a tie uses slope above the endpoint; at top it uses slope below. Endpoint contacts therefore
    // produce no interior event. Coincident support lines use the same stable ID tie at both ends.
    private void FindCrossings(int n)
    {
        crossingCount = 0;
        for (int i = 1; i < n; i++)
        {
            int id = topOrder[i], j = i;
            while (j > 0 && CompareAtInteger(id, topOrder[j - 1], top, true) < 0)
            {
                int other = topOrder[j - 1];
                Edge a = edges[id], b = edges[other];
                long numerator = b.B * a.Dy - a.B * b.Dy;
                long denominator = a.Dx * b.Dy - b.Dx * a.Dy;
                if (denominator == 0) throw new InvalidOperationException("A parallel pair changed order.");
                if (denominator < 0) { numerator = -numerator; denominator = -denominator; }
                if (numerator <= bottom * denominator || numerator >= top * denominator)
                    throw new InvalidOperationException("An inversion lies outside the open endpoint band.");
                if (crossingCount == crossings.Length) Array.Resize(ref crossings, Math.Max(256, crossings.Length * 2));
                crossings[crossingCount++] = new(new(numerator, denominator), id, other);
                topOrder[j] = other;
                j--;
            }
            topOrder[j] = id;
        }
        EventCount += crossingCount;
    }

    private void SetWinding(int from, int to, int winding, int windingB)
    {
        if (!intersectTwoLoops)
        {
            for (int i = from; i < to; i++)
            {
                prefix[i] = winding;
                winding += edges[active[i]].Delta;
            }
        }
        else
        {
            for (int i = from; i < to; i++)
            {
                prefix[i] = winding; prefixB[i] = windingB;
                Edge e = edges[active[i]];
                if (e.Loop == 0) winding += e.Delta; else windingB += e.Delta;
            }
        }
    }

    private int Fill(int winding) => nonZero ? (winding == 0 ? 0 : 1) : winding & 1;

    private void AccumulateGap(int position, Level y)
    {
        Level previous = lastLevel[position];
        lastLevel[position] = y;
        int left = active[position], right = active[position + 1];
        Edge boundary = edges[left];
        if (!intersectTwoLoops)
        {
            if (Fill(prefix[position] + boundary.Delta) == 0) return;
        }
        else
        {
            int w = prefix[position], wB = prefixB[position];
            if (boundary.Loop == 0) w += boundary.Delta; else wB += boundary.Delta;
            if (Fill(w) == 0 || Fill(wB) == 0) return;
        }
        // Subtract exact rational levels before rounding, including levels closer than one binary64 ULP.
        double height;
        if (useInt64)
        {
            long numerator = unchecked(y.N * previous.D - previous.N * y.D);
            if (numerator == 0) return;
            if (numerator < 0) throw new InvalidOperationException("A gap advanced backwards in sweep order.");
            long denominator = unchecked(y.D * previous.D);
            height = (double)numerator / (double)denominator;
        }
        else
        {
            Int128 numerator = unchecked((Int128)y.N * previous.D - (Int128)previous.N * y.D);
            if (numerator == 0) return;
            if (numerator < 0) throw new InvalidOperationException("A gap advanced backwards in sweep order.");
            Int128 denominator = unchecked((Int128)y.D * previous.D);
            height = (double)numerator / (double)denominator;
        }
        double lowerWidth = GapWidth(left, right, previous), upperWidth = GapWidth(left, right, y);
        // Widths, including exactly zero coincident-line gaps, are formed before binary64 rounding.
        // Every trapezoid is nonnegative; no subtraction of large boundary integrals is needed.
        area.Add((lowerWidth + upperWidth) * 0.5 * height);
    }

    private double GapWidth(int left, int right, Level y)
    {
        Edge a = edges[left], b = edges[right];
        if (useInt64)
        {
            long n = unchecked((b.Dx * y.N + b.B * y.D) * a.Dy -
                (a.Dx * y.N + a.B * y.D) * b.Dy);
            if (n < 0) throw new InvalidOperationException("A filled gap has negative exact width.");
            long d = unchecked(y.D * a.Dy * b.Dy);
            return (double)n / (double)d;
        }
        Int128 numerator = unchecked(((Int128)b.Dx * y.N + (Int128)b.B * y.D) * a.Dy -
            ((Int128)a.Dx * y.N + (Int128)a.B * y.D) * b.Dy);
        if (numerator < 0) throw new InvalidOperationException("A filled gap has negative exact width.");
        Int128 denominator = unchecked((Int128)y.D * a.Dy * b.Dy);
        return (double)numerator / (double)denominator;
    }

    private int CompareAtInteger(int left, int right, long y, bool below)
    {
        Edge a = edges[left], b = edges[right];
        long lhs = (a.Dx * y + a.B) * b.Dy, rhs = (b.Dx * y + b.B) * a.Dy;
        int comparison = lhs.CompareTo(rhs);
        if (comparison == 0)
        {
            comparison = (a.Dx * b.Dy).CompareTo(b.Dx * a.Dy);
            if (below) comparison = -comparison;
        }
        return comparison != 0 ? comparison : left.CompareTo(right);
    }

    private int CompareSlope(int left, int right)
    {
        Edge a = edges[left], b = edges[right];
        int comparison = (a.Dx * b.Dy).CompareTo(b.Dx * a.Dy);
        return comparison != 0 ? comparison : left.CompareTo(right);
    }

    // For coordinates bounded by M, |dx|,dy<=2M and B=x0*y1-y0*x1 gives |B|<=2M^2. Event |N|<=8M^3,
    // D<=8M^2; each integer-level comparator operand is <=8M^3. With M=2^19 these fit in Int64 (<=2^60).
    // Every rational comparison, width and height numerator is <=128M^5, and every denominator <=64M^4.
    // At M=524288 these are 2^102 and 2^82, within Int128; at M=2048 they are 2^62 and 2^50, within Int64.
    // These bounds include all intermediate sums/products. Local fractions are converted separately before
    // multiplying width by height; multiplying their exact numerators could exceed Int128.
    // Selection depends on every original coordinate on every call; larger input retains the Int128 path.
    private int CompareX(int left, int right, Level y)
    {
        Edge a = edges[left], b = edges[right];
        if (useInt64)
        {
            long l = unchecked((a.Dx * y.N + a.B * y.D) * b.Dy);
            long r = unchecked((b.Dx * y.N + b.B * y.D) * a.Dy);
            return l.CompareTo(r);
        }
        Int128 lhs = unchecked(((Int128)a.Dx * y.N + (Int128)a.B * y.D) * b.Dy);
        Int128 rhs = unchecked(((Int128)b.Dx * y.N + (Int128)b.B * y.D) * a.Dy);
        return lhs.CompareTo(rhs); // Common event denominator cancels.
    }

    private int ComparePoint(Crossing a, Crossing b)
    {
        int comparison = useInt64 ? Level.Compare64(a.Y, b.Y) : Level.Compare(a.Y, b.Y);
        return comparison != 0 ? comparison : CompareX(a.A, b.A, a.Y);
    }

    private void EnsureVertices(int count)
    {
        if (vertices.Length >= count) return;
        int capacity = Math.Max(256, count + count / 2);
        vertices = new Point2[capacity]; edges = new Edge[capacity]; levels = new long[capacity];
        active = new int[capacity]; topOrder = new int[capacity]; positions = new int[capacity];
        prefix = new int[capacity]; prefixB = new int[capacity]; lastLevel = new Level[capacity];
    }

    private struct Sum
    {
        private double value, correction;
        internal void Add(double term)
        {
            double next = value + term;
            correction += Math.Abs(value) >= Math.Abs(term) ? (value - next) + term : (term - next) + value;
            value = next;
        }
        internal readonly double Value => value + correction;
    }
}
