using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Experimental area-only sweep of bounded integer closed walks.</summary>
/// <remarks>
/// No input rounding: coordinates must be integers in [-32768,32768], with 3..8192 supplied vertices.
/// Topology and event ordering use exact integer/rational arithmetic; the final integrals use binary64.
/// This mutable reusable instance is neither thread-safe nor reentrant. Its endpoint-band rebuild and
/// insertion-sort inversion enumeration deliberately target few-level degenerate grids, not arbitrary paths.
/// </remarks>
internal sealed class IntegerScanbeam
{
    private const int CoordinateLimit = 32768, VertexLimit = 8192;

    private readonly struct Edge(long y0, long y1, long dx, long dy, long intercept, int delta)
    {
        internal readonly long Y0 = y0, Y1 = y1, Dx = dx, Dy = dy, B = intercept;
        internal readonly int Delta = delta;
    }

    private readonly record struct Level(long N, long D)
    {
        // Explicit unchecked only for the proven bounded geometric expressions; project-wide checked
        // arithmetic remains enabled for input conversion, array sizes, indices and counters.
        internal static int Compare(Level a, Level b) =>
            unchecked((Int128)a.N * b.D).CompareTo(unchecked((Int128)b.N * a.D));
    }

    private readonly record struct Crossing(Level Y, int A, int B);

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
        for (int i = 0; i < count; i++)
        {
            Point2 p = path[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > CoordinateLimit ||
                Math.Abs(p.Y) > CoordinateLimit || p.X != Math.Truncate(p.X) || p.Y != Math.Truncate(p.Y))
                throw new ArgumentException("Coordinates must be exact integers in [-32768,32768].", nameof(path));
            vertices[i] = p;
            levels[i] = (long)p.Y;
        }
        edgeCount = 0;
        for (int i = 0; i < count; i++)
        {
            Point2 a = vertices[i], b = vertices[(i + 1) % count];
            if (a.Y == b.Y) continue; // Horizontal edges have zero vertical-sweep measure.
            int delta = a.Y < b.Y ? 1 : -1;
            if (delta < 0) (a, b) = (b, a);
            long x = (long)a.X, y = (long)a.Y, dx = (long)b.X - x, dy = (long)b.Y - y;
            edges[edgeCount++] = new(y, (long)b.Y, dx, dy, x * dy - y * dx, delta);
        }
        Array.Sort(levels, 0, count);
        int levelCount = 0;
        for (int i = 0; i < count; i++)
            if (levelCount == 0 || levels[i] != levels[levelCount - 1]) levels[levelCount++] = levels[i];
        nonZero = fillRule == PathFillRule.NonZero;
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
                // All edges through an interior multiway crossing form one contiguous block. Coincident
                // support-line cohorts are included in full because every member crosses the other slopes.
                int w = prefix[lo];
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
                SetWinding(lo, hi + 1, w);
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

    private void SetWinding(int from, int to, int winding)
    {
        for (int i = from; i < to; i++)
        {
            prefix[i] = winding;
            winding += edges[active[i]].Delta;
        }
    }

    private int Fill(int winding) => nonZero ? (winding == 0 ? 0 : 1) : winding & 1;

    private void AccumulateGap(int position, Level y)
    {
        Level previous = lastLevel[position];
        lastLevel[position] = y;
        int left = active[position], right = active[position + 1];
        if (Fill(prefix[position] + edges[left].Delta) == 0) return;
        // Subtract exact rational levels before rounding, including levels closer than one binary64 ULP.
        Int128 heightNumerator = unchecked((Int128)y.N * previous.D - (Int128)previous.N * y.D);
        if (heightNumerator == 0) return;
        if (heightNumerator < 0) throw new InvalidOperationException("A gap advanced backwards in sweep order.");
        Int128 heightDenominator = unchecked((Int128)y.D * previous.D);
        double height = (double)heightNumerator / (double)heightDenominator;
        double lowerWidth = GapWidth(left, right, previous), upperWidth = GapWidth(left, right, y);
        // Widths, including exactly zero coincident-line gaps, are formed before binary64 rounding.
        // Every trapezoid is nonnegative; no subtraction of large boundary integrals is needed.
        area.Add((lowerWidth + upperWidth) * 0.5 * height);
    }

    private double GapWidth(int left, int right, Level y)
    {
        Edge a = edges[left], b = edges[right];
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

    // With M=2^15, |dx|,dy<=2^16 and |B|<=2^32. Event |N|<=2^49,D<=2^33.
    // Evaluating x gives |numerator|<=2^66, denominator<=2^49; comparison products fit below 2^116.
    // Gap-width numerators are below 2^83 and denominators below 2^65; height differences below 2^83 and
    // denominators below 2^66. We convert these local fractions separately rather than multiplying their
    // exact numerators, which would require a wider intermediate.
    private int CompareX(int left, int right, Level y)
    {
        Edge a = edges[left], b = edges[right];
        Int128 lhs = unchecked(((Int128)a.Dx * y.N + (Int128)a.B * y.D) * b.Dy);
        Int128 rhs = unchecked(((Int128)b.Dx * y.N + (Int128)b.B * y.D) * a.Dy);
        return lhs.CompareTo(rhs); // Common event denominator cancels.
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
        vertices = new Point2[capacity]; edges = new Edge[capacity]; levels = new long[capacity];
        active = new int[capacity]; topOrder = new int[capacity]; positions = new int[capacity];
        prefix = new int[capacity]; lastLevel = new Level[capacity];
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
