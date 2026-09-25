using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Experimental intersection sweep over the original binary64 coordinates, with certified fallback.</summary>
/// <remarks>
/// Endpoint events are sorted once. A persistent active order is changed only by endpoint insertions/removals
/// and certified interior crossings; a copy enumerates top-of-band inversions. Uncertain topology or area
/// accuracy abandons the whole operation and calls WindingArea.IntersectionArea on the original inputs.
/// Instances retain scratch arrays and are neither thread-safe nor reentrant. No input array is modified.
/// </remarks>
internal sealed class GuardedDoubleSweep
{
    private const int VertexBudget = 8192, BandEventBudget = 65536;
    private const long WorkBudget = 2_000_000;
    private const double CoordinateLimit = 1e100, AbsoluteAreaBudget = .25;

    private readonly struct Edge(Point2 lower, Point2 upper, int delta, int loop, Interval slope)
    {
        internal readonly Point2 Lower = lower, Upper = upper;
        internal readonly int Delta = delta, Loop = loop;
        internal readonly Interval Slope = slope;
    }

    private readonly record struct Endpoint(double Y, int Edge, bool Starts);
    private readonly record struct Level(Interval Y, int A, int B)
    {
        internal static Level At(double y) => new(Interval.Point(y), -1, -1);
        internal bool IsCrossingOf(int a, int b) => (A == a && B == b) || (A == b && B == a);
    }
    private readonly record struct Crossing(Level Level, int Id);
    private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool HasNoArea => MinX == MaxX || MinY == MaxY;
        internal bool NoAreaOverlap(Bounds other) => MaxX <= other.MinX || other.MaxX <= MinX ||
            MaxY <= other.MinY || other.MaxY <= MinY;
    }

    private Point2[] vertices = [];
    private Edge[] edges = [];
    private Endpoint[] endpoints = [];
    private Crossing[] crossings = [];
    private int[] active = [], topOrder = [], positions = [], prefixA = [], prefixB = [];
    private Level[] gapStarts = [];
    private Interval[] cachedX = [];
    private ScalarOrderFilter.PreparedEdge[] scalarEdges = [];
    private long[] cachedXLevelBits = [];
    private int[] cachedXGeneration = [];
    private int cacheGeneration;
    private int edgeCount, endpointCount, activeCount, crossingCount;
    private bool nonZero;
    private long work;
    private Interval area;
    private readonly Comparison<Endpoint> endpointComparison;
    private readonly Comparison<Crossing> crossingComparison;
    private readonly bool restrictToCommonY, cacheEndpointX, scalarOrderFilter;

    internal GuardedDoubleSweep(bool restrictToCommonY = false, bool cacheEndpointX = false, bool scalarOrderFilter = false)
    {
        this.restrictToCommonY = restrictToCommonY;
        this.cacheEndpointX = cacheEndpointX;
        this.scalarOrderFilter = scalarOrderFilter;
        endpointComparison = CompareEndpoints;
        crossingComparison = CompareCrossings;
    }

    internal bool LastUsedFallback { get; private set; }
    internal string? LastFallbackReason { get; private set; }
    internal double LastErrorBound { get; private set; } = double.NaN;
    internal int BandCount { get; private set; }
    internal long EventCount { get; private set; }
    internal int PeakActiveCount { get; private set; }
    /// <summary>Active entries visited by status maintenance, copying, prefix walks and gap integration.</summary>
    internal long ActiveEdgeVisits { get; private set; }
    internal long WorkCount => work;
    /// <summary>Actual XAt evaluations, including known endpoints and excluding cache hits.</summary>
    internal long XEvaluationCount { get; private set; }
    internal long XCacheHitCount { get; private set; }
    internal long FilterAttemptCount { get; private set; }
    internal long FilterAcceptedCount { get; private set; }
    internal long FilterIntervalCount { get; private set; }

    internal double MeasureIntersection(Point2[] first, Point2[] second, PathFillRule rule = PathFillRule.NonZero)
    {
        LastUsedFallback = false; LastFallbackReason = null; LastErrorBound = double.NaN;
        BandCount = PeakActiveCount = 0; EventCount = ActiveEdgeVisits = work = 0;
        XEvaluationCount = XCacheHitCount = 0;
        FilterAttemptCount = FilterAcceptedCount = FilterIntervalCount = 0;
        if (cacheEndpointX)
        {
            if (cacheGeneration == int.MaxValue) { Array.Clear(cachedXGeneration); cacheGeneration = 1; }
            else cacheGeneration++;
        }
        activeCount = edgeCount = endpointCount = crossingCount = 0;
        area = Interval.Zero;
        if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd)
            return Fallback(first, second, rule, "input-contract");
        if (first is null || second is null)
            return Fallback(first!, second!, rule, "input-contract");
        if (first.Length < 3 || second.Length < 3)
            return Fallback(first, second, rule, "input-contract");
        if (first.Length > VertexBudget || second.Length > VertexBudget - first.Length)
            return Fallback(first, second, rule, "vertex-budget");
        try
        {
            int count = first.Length + second.Length;
            EnsureVertices(count);
            // Both entire inputs, including their effective vertex counts, are checked before an AABB result.
            Bounds a = CopyValidated(first, 0), b = CopyValidated(second, first.Length);
            if (a.HasNoArea || b.HasNoArea || a.NoAreaOverlap(b))
            {
                LastErrorBound = 0;
                return 0;
            }
            nonZero = rule == PathFillRule.NonZero;
            AppendEdges(0, first.Length, 0);
            AppendEdges(first.Length, count, 1);
            Array.Fill(positions, -1, 0, edgeCount);
            endpoints.AsSpan(0, endpointCount).Sort(endpointComparison);
            if (restrictToCommonY)
                SweepCommonY(Math.Max(a.MinY, b.MinY), Math.Min(a.MaxY, b.MaxY));
            else
            {
                int at = 0;
                while (at < endpointCount)
                {
                    double y = endpoints[at].Y;
                    int end = at + 1;
                    while (end < endpointCount && endpoints[end].Y == y) end++;
                    // The preceding band has already closed every gap at y. Ended edges leave before new edges
                    // are inserted, so every edge in the next band spans its whole open height interval.
                    for (int i = at; i < end; i++) if (!endpoints[i].Starts) Remove(endpoints[i].Edge);
                    for (int i = at; i < end; i++) if (endpoints[i].Starts) Insert(endpoints[i].Edge, y);
                    if (end < endpointCount && activeCount > 0) ProcessBand(y, endpoints[end].Y);
                    at = end;
                }
                if (activeCount != 0) throw new Uncertified("unclosed-status");
            }
            return CertifiedValue();
        }
        catch (Exception ex) when (FindReason(ex) is not null)
        {
            return Fallback(first, second, rule, FindReason(ex)!);
        }
    }

    private void SweepCommonY(double lo, double hi)
    {
        // A filled closed loop has zero winding outside its Y bounds. Only their shared range can
        // contribute to intersection, but every edge spanning its lower boundary must be initialized.
        // Certified insertion orders those edges just above lo; histories/crossings below lo are irrelevant.
        for (int e = 0; e < edgeCount; e++)
        {
            Charge();
            if (edges[e].Lower.Y <= lo && edges[e].Upper.Y > lo) Insert(e, lo);
        }
        int at = 0;
        while (at < endpointCount && endpoints[at].Y <= lo) at++;
        double y = lo;
        while (y < hi)
        {
            double next = at < endpointCount ? Math.Min(hi, endpoints[at].Y) : hi;
            // ProcessBand initializes both winding prefixes from the unbounded left side, where each is zero.
            if (activeCount > 0) ProcessBand(y, next);
            if (next == hi) break;
            int end = at + 1;
            while (end < endpointCount && endpoints[end].Y == next) end++;
            for (int i = at; i < end; i++) if (!endpoints[i].Starts) Remove(endpoints[i].Edge);
            for (int i = at; i < end; i++) if (endpoints[i].Starts) Insert(endpoints[i].Edge, next);
            at = end;
            y = next;
        }
        // Edges may still span hi. Their winding above hi cannot contribute, so they need not be removed.
        // The next MeasureIntersection resets activeCount and every live position before using this storage.
    }

    private Bounds CopyValidated(Point2[] path, int offset)
    {
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        int distinct = 0;
        Point2 previous = default;
        for (int i = 0; i < path.Length; i++)
        {
            Point2 p = path[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > CoordinateLimit || Math.Abs(p.Y) > CoordinateLimit)
                throw new Uncertified("input-contract");
            vertices[offset + i] = p;
            if (distinct == 0 || !Same(previous, p)) { distinct++; previous = p; }
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        if (distinct > 1 && Same(vertices[offset], previous)) distinct--;
        if (distinct < 3) throw new Uncertified("input-contract");
        return new(minX, minY, maxX, maxY);
    }

    private void AppendEdges(int from, int to, int loop)
    {
        for (int i = from; i < to; i++)
        {
            Point2 a = vertices[i], b = vertices[i + 1 == to ? from : i + 1];
            if (a.Y == b.Y) continue;
            int delta = a.Y < b.Y ? 1 : -1;
            if (delta < 0) (a, b) = (b, a);
            Interval slope = Interval.Divide(Interval.Difference(b.X, a.X), Interval.Difference(b.Y, a.Y));
            edges[edgeCount] = new(a, b, delta, loop, slope);
            if (scalarOrderFilter)
                scalarEdges[edgeCount] = ScalarOrderFilter.Prepare(a.X, a.Y, b.Y, slope.Lo, slope.Hi);
            endpoints[endpointCount++] = new(a.Y, edgeCount, true);
            endpoints[endpointCount++] = new(b.Y, edgeCount, false);
            edgeCount++;
        }
    }

    private void Remove(int edge)
    {
        int at = positions[edge];
        if (at < 0 || at >= activeCount || active[at] != edge) throw new Uncertified("endpoint-status");
        for (int i = at + 1; i < activeCount; i++)
        {
            Visit(); active[i - 1] = active[i]; positions[active[i - 1]] = i - 1;
        }
        activeCount--; positions[edge] = -1;
    }

    private void Insert(int edge, double y)
    {
        int lo = 0, hi = activeCount;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            Visit();
            if (CompareAtEndpoint(edge, active[mid], y, false) < 0) hi = mid; else lo = mid + 1;
        }
        for (int i = activeCount; i > lo; i--)
        {
            Visit(); active[i] = active[i - 1]; positions[active[i]] = i;
        }
        active[lo] = edge; positions[edge] = lo; activeCount++;
        PeakActiveCount = Math.Max(PeakActiveCount, activeCount);
    }

    private void ProcessBand(double bottom, double top)
    {
        BandCount++;
        Level start = Level.At(bottom);
        for (int i = 0; i < activeCount; i++)
        {
            Visit(); topOrder[i] = active[i]; gapStarts[i] = start;
        }
        SetWinding(0, activeCount, 0, 0);
        crossingCount = 0;
        // This sorts a copy only. Each inversion of the certified bottom/top orders is one interior crossing.
        for (int i = 1; i < activeCount; i++)
        {
            int id = topOrder[i], j = i;
            while (j > 0 && CompareAtEndpoint(id, topOrder[j - 1], top, true) < 0)
            {
                Visit();
                int other = topOrder[j - 1];
                if (crossingCount == BandEventBudget) throw new Uncertified("event-budget");
                if (crossingCount == crossings.Length) Array.Resize(ref crossings, Math.Max(256, crossings.Length * 2));
                Level level = ConstructCrossing(other, id, bottom, top);
                crossings[crossingCount] = new(level, crossingCount);
                crossingCount++; EventCount++;
                topOrder[j] = other; j--;
            }
            topOrder[j] = id;
        }
        crossings.AsSpan(0, crossingCount).Sort(crossingComparison);
        for (int c = 1; c < crossingCount; c++)
            if (!(crossings[c - 1].Level.Y.Hi < crossings[c].Level.Y.Lo)) throw new Uncertified("event-order");
        for (int c = 0; c < crossingCount; c++)
        {
            Charge();
            Level level = crossings[c].Level;
            int lo = Math.Min(positions[level.A], positions[level.B]), hi = Math.Max(positions[level.A], positions[level.B]);
            if (lo < 0 || hi != lo + 1) throw new Uncertified("nonadjacent-event");
            int wA = prefixA[lo], wB = prefixB[lo];
            for (int i = Math.Max(0, lo - 1); i <= Math.Min(activeCount - 2, hi); i++) AccumulateGap(i, level);
            (active[lo], active[hi]) = (active[hi], active[lo]);
            positions[active[lo]] = lo; positions[active[hi]] = hi;
            SetWinding(lo, hi + 1, wA, wB);
        }
        for (int i = 0; i < activeCount; i++)
        {
            Visit();
            if (active[i] != topOrder[i]) throw new Uncertified("top-order-status");
        }
        Level finish = Level.At(top);
        for (int i = 0; i + 1 < activeCount; i++) AccumulateGap(i, finish);
    }

    private Level ConstructCrossing(int left, int right, double bottom, double top)
    {
        Interval separation = HorizontalDifference(left, right, Level.At(bottom));
        Interval relativeSlope = Interval.Subtract(edges[left].Slope, edges[right].Slope);
        if (!(relativeSlope.Lo > 0)) throw new Uncertified("crossing-slope");
        Interval y = Interval.Add(Interval.Point(bottom), Interval.Divide(separation, relativeSlope));
        if (!(y.Lo > bottom && y.Hi < top)) throw new Uncertified("crossing-endpoint");
        return new(y, left, right);
    }

    private int CompareAtEndpoint(int left, int right, double y, bool below)
    {
        Charge();
        if (left == right) return 0;
        if (SameSupport(left, right)) return left.CompareTo(right);
        if (scalarOrderFilter)
        {
            FilterAttemptCount++;
            if (ScalarOrderFilter.TryOrder(in scalarEdges[left], in scalarEdges[right], y, out int certifiedOrder))
            {
                FilterAcceptedCount++;
                return certifiedOrder;
            }
            FilterIntervalCount++;
        }
        Interval a = XAt(left, Interval.Point(y)), b = XAt(right, Interval.Point(y));
        if (a.Hi < b.Lo) return -1;
        if (b.Hi < a.Lo) return 1;
        // A true exact source/evaluation equality is different from overlapping rounded enclosures.
        if (!a.IsPoint || !b.IsPoint || a.Lo != b.Lo) throw new Uncertified("endpoint-order");
        Interval sa = edges[left].Slope, sb = edges[right].Slope;
        int order;
        if (sa.Hi < sb.Lo) order = -1;
        else if (sb.Hi < sa.Lo) order = 1;
        else if (sa.IsPoint && sb.IsPoint && sa.Lo == sb.Lo) return left.CompareTo(right);
        else throw new Uncertified("endpoint-slope");
        Edge ea = edges[left], eb = edges[right];
        if (!((y == ea.Lower.Y || y == ea.Upper.Y) && (y == eb.Lower.Y || y == eb.Upper.Y)))
            throw new Uncertified("endpoint-on-edge");
        return below ? -order : order;
    }

    /// <summary>Direct probe of the actual scalar filter using the sweep's certified slope construction.</summary>
    internal static bool TryScalarOrderAtY(Point2 a0, Point2 a1, Point2 b0, Point2 b1, double y, out int order)
    {
        order = 0;
        if (!FilterPointValid(a0) || !FilterPointValid(a1) || !FilterPointValid(b0) || !FilterPointValid(b1) ||
            !double.IsFinite(y) || a0.Y == a1.Y || b0.Y == b1.Y) return false;
        if (a0.Y > a1.Y) (a0, a1) = (a1, a0);
        if (b0.Y > b1.Y) (b0, b1) = (b1, b0);
        if (y < a0.Y || y > a1.Y || y < b0.Y || y > b1.Y) return false;
        try
        {
            Interval aSlope = Interval.Divide(Interval.Difference(a1.X, a0.X), Interval.Difference(a1.Y, a0.Y));
            Interval bSlope = Interval.Divide(Interval.Difference(b1.X, b0.X), Interval.Difference(b1.Y, b0.Y));
            ScalarOrderFilter.PreparedEdge a = ScalarOrderFilter.Prepare(a0.X, a0.Y, a1.Y, aSlope.Lo, aSlope.Hi);
            ScalarOrderFilter.PreparedEdge b = ScalarOrderFilter.Prepare(b0.X, b0.Y, b1.Y, bSlope.Lo, bSlope.Hi);
            return ScalarOrderFilter.TryOrder(in a, in b, y, out order);
        }
        catch (Uncertified) { return false; }
    }

    private static bool FilterPointValid(Point2 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        Math.Abs(p.X) <= CoordinateLimit && Math.Abs(p.Y) <= CoordinateLimit;

    private int CompareCrossings(Crossing a, Crossing b)
    {
        Charge();
        if (a.Id == b.Id) return 0;
        if (a.Level.Y.Hi < b.Level.Y.Lo) return -1;
        if (b.Level.Y.Hi < a.Level.Y.Lo) return 1;
        // No approximate equality or arbitrary pair order: uncertain/multiway events use the general engine.
        throw new Uncertified("event-order");
    }

    private static int CompareEndpoints(Endpoint a, Endpoint b)
    {
        int order = a.Y.CompareTo(b.Y);
        if (order == 0) order = a.Starts.CompareTo(b.Starts);
        return order != 0 ? order : a.Edge.CompareTo(b.Edge);
    }

    private void SetWinding(int from, int to, int a, int b)
    {
        for (int i = from; i < to; i++)
        {
            Visit(); prefixA[i] = a; prefixB[i] = b;
            Edge edge = edges[active[i]];
            if (edge.Loop == 0) a += edge.Delta; else b += edge.Delta;
        }
    }

    private void AccumulateGap(int position, Level finish)
    {
        Visit();
        Level start = gapStarts[position];
        gapStarts[position] = finish;
        int left = active[position], right = active[position + 1];
        Edge edge = edges[left];
        int a = prefixA[position], b = prefixB[position];
        if (edge.Loop == 0) a += edge.Delta; else b += edge.Delta;
        if (!Filled(a) || !Filled(b)) return;
        Interval height = Interval.Subtract(finish.Y, start.Y).Nonnegative();
        if (height.IsZero) return;
        Interval lower = HorizontalDifference(left, right, start).Nonnegative();
        Interval upper = HorizontalDifference(left, right, finish).Nonnegative();
        Interval trapezoid = Interval.Multiply(Interval.Multiply(Interval.Add(lower, upper), Interval.Point(.5)), height);
        area = Interval.Add(area, trapezoid);
    }

    private bool Filled(int winding) => nonZero ? winding != 0 : (winding & 1) != 0;

    private Interval HorizontalDifference(int left, int right, Level level)
    {
        if (level.IsCrossingOf(left, right) || SameSupport(left, right)) return Interval.Zero;
        Edge a = edges[left], b = edges[right];
        if (TryKnownX(a, level.Y, out double ax) && TryKnownX(b, level.Y, out double bx))
            return Interval.Difference(bx, ax);
        // Subtract a common reference before evaluation. Original coordinate differences keep their exact
        // TwoDiff remainder in the enclosure; this is not a rounded translation of the supplied geometry.
        Interval originDifference = Interval.Difference(b.Lower.X, a.Lower.X);
        Interval advanceA = Interval.Multiply(a.Slope, Interval.Subtract(level.Y, Interval.Point(a.Lower.Y)));
        Interval advanceB = Interval.Multiply(b.Slope, Interval.Subtract(level.Y, Interval.Point(b.Lower.Y)));
        return Interval.Add(originDifference, Interval.Subtract(advanceB, advanceA));
    }

    private Interval XAt(int edge, Interval y)
    {
        long bits = 0;
        if (cacheEndpointX && y.IsPoint)
        {
            bits = BitConverter.DoubleToInt64Bits(y.Lo);
            if (cachedXGeneration[edge] == cacheGeneration && cachedXLevelBits[edge] == bits)
            {
                XCacheHitCount++;
                return cachedX[edge];
            }
        }
        XEvaluationCount++;
        Edge e = edges[edge];
        Interval result = TryKnownX(e, y, out double x) ? Interval.Point(x) :
            Interval.Add(Interval.Point(e.Lower.X), Interval.Multiply(e.Slope, Interval.Subtract(y, Interval.Point(e.Lower.Y))));
        // Cache only successful, requested evaluations. No eager computation can introduce a new fallback;
        // the exact level bits and call generation prevent reuse across changed input or endpoint levels.
        if (cacheEndpointX && y.IsPoint)
        {
            cachedX[edge] = result; cachedXLevelBits[edge] = bits; cachedXGeneration[edge] = cacheGeneration;
        }
        return result;
    }

    private static bool TryKnownX(Edge edge, Interval y, out double x)
    {
        if (edge.Lower.X == edge.Upper.X) { x = edge.Lower.X; return true; }
        if (y.IsPoint && y.Lo == edge.Lower.Y) { x = edge.Lower.X; return true; }
        if (y.IsPoint && y.Lo == edge.Upper.Y) { x = edge.Upper.X; return true; }
        x = 0; return false;
    }

    private bool SameSupport(int first, int second)
    {
        Edge a = edges[first], b = edges[second];
        return (Same(a.Lower, b.Lower) && Same(a.Upper, b.Upper)) ||
            (a.Lower.X == a.Upper.X && b.Lower.X == b.Upper.X && a.Lower.X == b.Lower.X);
    }

    private double CertifiedValue()
    {
        if (area.IsZero) { LastErrorBound = 0; return 0; }
        if (!(area.Lo > 0)) throw new Uncertified("area-accuracy");
        double value = area.Lo + (area.Hi - area.Lo) * .5;
        value = Math.Max(area.Lo, Math.Min(area.Hi, value));
        double lowerError = value == area.Lo ? 0 : Interval.Up(value - area.Lo);
        double upperError = value == area.Hi ? 0 : Interval.Up(area.Hi - value);
        double error = Math.Max(lowerError, upperError);
        // Rounding the requested relative tolerance downward makes the acceptance test conservative too.
        double relativeBudget = Interval.Down(Math.BitDecrement(1e-10) * Math.Abs(value));
        if (!(error <= AbsoluteAreaBudget && error <= relativeBudget)) throw new Uncertified("area-accuracy");
        LastErrorBound = error;
        return value;
    }

    private double Fallback(Point2[] first, Point2[] second, PathFillRule rule, string reason)
    {
        LastUsedFallback = true; LastFallbackReason = reason; LastErrorBound = double.NaN;
        return WindingArea.IntersectionArea(first, second, rule);
    }

    private void Visit() { ActiveEdgeVisits++; Charge(); }
    private void Charge() { if (++work > WorkBudget) throw new Uncertified("work-budget"); }
    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;

    private void EnsureVertices(int count)
    {
        if (vertices.Length >= count) return;
        int capacity = Math.Max(256, count + count / 2);
        vertices = new Point2[capacity]; edges = new Edge[capacity]; endpoints = new Endpoint[2 * capacity];
        active = new int[capacity]; topOrder = new int[capacity]; positions = new int[capacity];
        prefixA = new int[capacity]; prefixB = new int[capacity]; gapStarts = new Level[capacity];
        if (scalarOrderFilter) scalarEdges = new ScalarOrderFilter.PreparedEdge[capacity];
        if (cacheEndpointX)
        {
            cachedX = new Interval[capacity]; cachedXLevelBits = new long[capacity]; cachedXGeneration = new int[capacity];
        }
    }

    private sealed class Uncertified(string reason) : Exception(reason)
    {
        internal string Reason { get; } = reason;
    }

    private static string? FindReason(Exception exception)
    {
        // Span.Sort can wrap a comparer exception. Only our explicit uncertainty signals trigger fallback.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is Uncertified failure) return failure.Reason;
        return null;
    }

    /// <summary>Finite closed enclosures; every arithmetic operation rounds outwards.</summary>
    private readonly struct Interval
    {
        internal readonly double Lo, Hi;
        private Interval(double lo, double hi)
        {
            if (!double.IsFinite(lo) || !double.IsFinite(hi) || lo > hi) throw new Uncertified("nonfinite-arithmetic");
            Lo = lo; Hi = hi;
        }
        internal static Interval Zero => new(0, 0);
        internal static Interval Point(double value) => new(value, value);
        internal bool IsPoint => Lo == Hi;
        internal bool IsZero => Lo == 0 && Hi == 0;
        internal static double Down(double value)
        {
            double result = Math.BitDecrement(value);
            if (!double.IsFinite(result)) throw new Uncertified("nonfinite-arithmetic");
            return result;
        }
        internal static double Up(double value)
        {
            double result = Math.BitIncrement(value);
            if (!double.IsFinite(result)) throw new Uncertified("nonfinite-arithmetic");
            return result;
        }
        internal Interval Nonnegative()
        {
            if (Hi < 0) throw new Uncertified("negative-ordered-gap");
            // Certified active order/time order proves the true value nonnegative, even if its enclosure
            // crosses zero. Intersecting with that proven constraint does not discard any possible value.
            return Lo < 0 ? new(0, Hi) : this;
        }
        internal static Interval Difference(double a, double b)
        {
            double value = a - b;
            if (!double.IsFinite(value)) throw new Uncertified("nonfinite-arithmetic");
            double bv = a - value, av = value + bv;
            double tail = (a - av) + (bv - b);
            if (!double.IsFinite(tail)) throw new Uncertified("nonfinite-arithmetic");
            // TwoDiff is error-free here. A subnormal sum/difference of binary64 operands is exact on the
            // 2^-1074 lattice; no product residual is assumed exact under underflow.
            return tail == 0 ? Point(value) : tail > 0 ? new(value, Up(value)) : new(Down(value), value);
        }
        internal static Interval Add(Interval a, Interval b)
        {
            if (a.IsZero) return b;
            if (b.IsZero) return a;
            return new(Down(a.Lo + b.Lo), Up(a.Hi + b.Hi));
        }
        internal static Interval Subtract(Interval a, Interval b)
        {
            if (a.IsPoint && b.IsPoint) return Difference(a.Lo, b.Lo);
            if (b.IsZero) return a;
            return new(Down(a.Lo - b.Hi), Up(a.Hi - b.Lo));
        }
        internal static Interval Multiply(Interval a, Interval b)
        {
            if (a.IsZero || b.IsZero) return Zero;
            if (a.IsPoint && a.Lo == 1) return b;
            if (b.IsPoint && b.Lo == 1) return a;
            double p0 = a.Lo * b.Lo, p1 = a.Lo * b.Hi, p2 = a.Hi * b.Lo, p3 = a.Hi * b.Hi;
            return new(Down(Math.Min(Math.Min(p0, p1), Math.Min(p2, p3))), Up(Math.Max(Math.Max(p0, p1), Math.Max(p2, p3))));
        }
        internal static Interval Divide(Interval a, Interval b)
        {
            if (b.Lo <= 0 && b.Hi >= 0) throw new Uncertified("uncertain-denominator");
            if (a.IsZero) return Zero;
            if (b.IsPoint && b.Lo == 1) return a;
            double p0 = a.Lo / b.Lo, p1 = a.Lo / b.Hi, p2 = a.Hi / b.Lo, p3 = a.Hi / b.Hi;
            return new(Down(Math.Min(Math.Min(p0, p1), Math.Min(p2, p3))), Up(Math.Max(Math.Max(p0, p1), Math.Max(p2, p3))));
        }
    }
}
