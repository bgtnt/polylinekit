#if NET10_0_OR_GREATER
namespace PolylineKit;

/// <summary>Area-only sweep for an admitted, bounded integer closed walk.</summary>
/// <remarks>
/// Extracted from the single-loop Int64 path of the experimental IntegerScanbeam at be96dea.
/// Coordinates are never rounded. Exact integer/rational decisions determine topology and event order;
/// local nonnegative trapezoids are converted to binary64 and accumulated with compensation.
/// Endpoint-band rebuilds and insertion-sort inversion enumeration suit few-level crossing/retraced
/// paths. The caller chooses this specialization; its admission checks alone do not promise a speedup.
/// A rented instance belongs to one call. Return it in finally; never share it concurrently.
/// </remarks>
internal sealed class IntegerFilledAreaSweep
{
    private const int CoordinateLimit = 2048, VertexLimit = 1024;

    private readonly struct Edge(long y0, long y1, long dx, long dy, long intercept, int delta)
    {
        internal readonly long Y0 = y0, Y1 = y1, Dx = dx, Dy = dy, B = intercept;
        internal readonly int Delta = delta;
    }

    private readonly record struct Level(long N, long D)
    {
        internal static int Compare(Level a, Level b) =>
            unchecked(a.N * b.D).CompareTo(unchecked(b.N * a.D));
    }

    private readonly record struct Crossing(Level Y, int A, int B);

    [ThreadStatic] private static IntegerFilledAreaSweep? cached;

    private Point2[] vertices = [];
    private Edge[] edges = [];
    private long[] levels = [];
    private int[] active = [], topOrder = [], positions = [], prefix = [];
    private Level[] lastLevel = [];
    private Crossing[] crossings = [];
    private int edgeCount, crossingCount;
    private long bottom, top;
    private bool nonZero;
    private Sum area;
    private readonly Comparison<int> bottomComparison;
    private readonly Comparison<int> slopeComparison;
    private readonly Comparison<Crossing> crossingComparison;

    private IntegerFilledAreaSweep()
    {
        bottomComparison = (x, y) => CompareAtInteger(x, y, bottom, false);
        slopeComparison = CompareSlope;
        crossingComparison = ComparePoint;
    }

    internal static IntegerFilledAreaSweep Rent()
    {
        IntegerFilledAreaSweep? workspace = cached;
        cached = null; // A nested call must receive a different workspace.
        return workspace ?? new IntegerFilledAreaSweep();
    }

    internal static void Return(IntegerFilledAreaSweep workspace) => cached = workspace;

    internal double Measure(Point2[] path, PathFillRule fillRule)
    {
        if (fillRule != PathFillRule.NonZero && fillRule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(fillRule));
        ArgumentNullException.ThrowIfNull(path);
        int count = path.Length;
        if (count < 3 || count > VertexLimit)
            throw new ArgumentException("The integer specialization requires 3..1024 supplied vertices.", nameof(path));
        EnsureVertices(count);
        int effectiveCount = 0;
        Point2 previous = default;
        for (int i = 0; i < count; i++)
        {
            Point2 p = path[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > CoordinateLimit ||
                Math.Abs(p.Y) > CoordinateLimit || p.X != Math.Truncate(p.X) || p.Y != Math.Truncate(p.Y))
                throw new ArgumentException("Coordinates must be exact integers in [-2048,2048].", nameof(path));
            if (effectiveCount == 0 || !WindingInput.Same(previous, p)) effectiveCount++;
            previous = p;
            vertices[i] = p;
            levels[i] = (long)p.Y;
        }
        if (effectiveCount > 1 && WindingInput.Same(vertices[0], previous)) effectiveCount--;
        if (effectiveCount < 3)
            throw new ArgumentException("The path requires at least 3 vertices after duplicate removal.", nameof(path));
        edgeCount = 0;
        AppendEdges(count);
        return Sweep(count, fillRule);
    }

    private void AppendEdges(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Point2 a = vertices[i], b = vertices[i + 1 == count ? 0 : i + 1];
            if (a.Y == b.Y) continue; // Horizontal edges have zero vertical-sweep measure.
            int delta = a.Y < b.Y ? 1 : -1;
            if (delta < 0) (a, b) = (b, a);
            long x = (long)a.X, y = (long)a.Y, dx = (long)b.X - x, dy = (long)b.Y - y;
            edges[edgeCount++] = new(y, (long)b.Y, dx, dy, x * dy - y * dx, delta);
        }
    }

    private double Sweep(int count, PathFillRule fillRule)
    {
        Array.Sort(levels, 0, count);
        int levelCount = 0;
        for (int i = 0; i < count; i++)
            if (levelCount == 0 || levels[i] != levels[levelCount - 1]) levels[levelCount++] = levels[i];
        nonZero = fillRule == PathFillRule.NonZero;
        area = default;
        for (int band = 0; band + 1 < levelCount; band++)
        {
            bottom = levels[band]; top = levels[band + 1];
            int n = 0;
            for (int e = 0; e < edgeCount; e++)
                if (edges[e].Y0 <= bottom && edges[e].Y1 >= top) active[n++] = e;
            if (n == 0) continue;
            active.AsSpan(0, n).Sort(bottomComparison);
            var start = new Level(bottom, 1);
            for (int i = 0; i < n; i++)
            {
                int id = active[i];
                topOrder[i] = id; positions[id] = i; lastLevel[i] = start;
            }
            SetWinding(0, n, 0);
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
                // Every edge through an interior multiway crossing belongs to one contiguous block.
                // Include entire coincident-support cohorts: each member crosses the other slopes.
                int winding = prefix[lo];
                for (int i = lo; i <= hi; i++)
                    if (CompareX(active[i], first.A, first.Y) != 0)
                        throw new InvalidOperationException("Noncontiguous exact crossing block.");
                // Close affected gaps, including the two bordering the block, before replacing their
                // boundary edges. Unaffected gaps retain their earlier start level.
                for (int i = Math.Max(0, lo - 1); i <= Math.Min(n - 2, hi); i++) AccumulateGap(i, first.Y);
                active.AsSpan(lo, hi - lo + 1).Sort(slopeComparison);
                for (int i = lo; i <= hi; i++) positions[active[i]] = i;
                SetWinding(lo, hi + 1, winding);
                c = end;
            }
            var finish = new Level(top, 1);
            for (int i = 0; i + 1 < n; i++) AccumulateGap(i, finish);
        }
        return area.Value;
    }

    // Insert-sort the top-of-band order, recording its inversions relative to bottom order. A tie uses
    // slope above a bottom endpoint and below a top endpoint, excluding endpoint contacts. Coincident
    // support lines retain the same stable edge-ID tie at both endpoints.
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
                // At most n*(n-1)/2 < 2^19 pair events per band for n <= 1024. Doubling therefore
                // needs at most 2^19 slots, and cannot overflow an Int32 size or index.
                if (crossingCount == crossings.Length) Array.Resize(ref crossings, Math.Max(256, crossings.Length * 2));
                crossings[crossingCount++] = new(new(numerator, denominator), id, other);
                topOrder[j] = other;
                j--;
            }
            topOrder[j] = id;
        }
    }

    private void SetWinding(int from, int to, int winding)
    {
        for (int i = from; i < to; i++)
        {
            prefix[i] = winding;
            winding += edges[active[i]].Delta;
        }
    }

    private void AccumulateGap(int position, Level y)
    {
        Level previous = lastLevel[position];
        lastLevel[position] = y;
        int left = active[position], right = active[position + 1];
        int winding = prefix[position] + edges[left].Delta;
        if (nonZero ? winding == 0 : (winding & 1) == 0) return;
        // Subtract rational levels exactly before rounding, even when they are less than one binary64
        // ULP apart. Multiply only after converting each local width and height separately.
        long numerator = unchecked(y.N * previous.D - previous.N * y.D);
        if (numerator == 0) return;
        if (numerator < 0) throw new InvalidOperationException("A gap advanced backwards in sweep order.");
        long denominator = unchecked(y.D * previous.D);
        double height = (double)numerator / (double)denominator;
        double lowerWidth = GapWidth(left, right, previous), upperWidth = GapWidth(left, right, y);
        area.Add((lowerWidth + upperWidth) * 0.5 * height);
    }

    private double GapWidth(int left, int right, Level y)
    {
        Edge a = edges[left], b = edges[right];
        // Form the width before rounding, preserving exactly zero coincident-support gaps.
        long n = unchecked((b.Dx * y.N + b.B * y.D) * a.Dy -
            (a.Dx * y.N + a.B * y.D) * b.Dy);
        if (n < 0) throw new InvalidOperationException("A filled gap has negative exact width.");
        long d = unchecked(y.D * a.Dy * b.Dy);
        return (double)n / (double)d;
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

    // Defensive admission fixes M=2048=2^11: |dx|,dy<=2M, |B|=|x0*y1-y0*x1|<=2M^2.
    // Event |N|<=8M^3, 0<D<=8M^2. Integer-level comparator operands are <=8M^3.
    // Rational comparisons, width/height numerators and all their intermediate sums/products have
    // magnitude <=128M^5=2^62; denominators are <=64M^4=2^50. Thus every geometric Int64
    // expression is exact even in unchecked arithmetic. Winding counts have magnitude <=1024.
    // Multiplying width and height as exact fractions could exceed these bounds; convert each first.
    private int CompareX(int left, int right, Level y)
    {
        Edge a = edges[left], b = edges[right];
        long lhs = unchecked((a.Dx * y.N + a.B * y.D) * b.Dy);
        long rhs = unchecked((b.Dx * y.N + b.B * y.D) * a.Dy);
        return lhs.CompareTo(rhs); // The positive common event denominator cancels.
    }

    private int ComparePoint(Crossing a, Crossing b)
    {
        int comparison = Level.Compare(a.Y, b.Y);
        return comparison != 0 ? comparison : CompareX(a.A, b.A, a.Y);
    }

    private void EnsureVertices(int count)
    {
        if (vertices.Length >= count) return;
        int capacity = Math.Max(256, count + count / 2);
        // Publish replacement buffers together. A failed allocation must not leave the cached instance
        // with a larger vertices capacity than the other arrays used on its next rented call.
        var nextVertices = new Point2[capacity]; var nextEdges = new Edge[capacity];
        var nextLevels = new long[capacity]; var nextActive = new int[capacity];
        var nextTopOrder = new int[capacity]; var nextPositions = new int[capacity];
        var nextPrefix = new int[capacity]; var nextLastLevel = new Level[capacity];
        vertices = nextVertices; edges = nextEdges; levels = nextLevels;
        active = nextActive; topOrder = nextTopOrder; positions = nextPositions;
        prefix = nextPrefix; lastLevel = nextLastLevel;
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
#endif
