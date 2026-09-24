using System.Runtime.CompilerServices;

namespace PolylineKit;

internal struct WindingStatistics
{
    internal int Crossings;
    internal int ExactPredicates;
    internal int SymbolicTieBreaks;
}

/// <summary>
/// Area integrals of one or two closed vertex loops without building faces or output contours.
/// </summary>
/// <remarks>
/// For any weight F of the winding numbers, integral F dA equals one half of the sum over sub-edges
/// of [F(left) - F(right)] * cross(a - o, b - a) * (t1 - t0) for any origin o, provided every contribution
/// to one closed boundary chain uses the same origin. Sub-edges split input edges at crossings; left and
/// right differ by one in the winding of the edge's own loop. Winding numbers start at each loop's leftmost
/// perturbed vertex, where the local configuration determines them, and are propagated across crossings.
/// All combinatorial decisions use RobustOrientation.
/// </remarks>
internal static class WindingEngine
{
    internal struct Crossing
    {
        internal double T;
        internal int Edge;
        internal int Delta;
        internal bool SameLoop;
    }

    /// <summary>Working storage for one active call. A nested call on the same thread gets its own.</summary>
    internal sealed class Workspace
    {
        [ThreadStatic] private static Workspace? cached;

        internal Point2[] Vertices = new Point2[256];
        internal int[] Next = new int[256];
        internal double[] Keys = new double[256];
        internal int[] Order = new int[256];
        internal int[] Start = new int[257];
        internal Crossing[] Found = new Crossing[256];
        internal Crossing[] Sorted = new Crossing[256];
        internal double[] BucketKeys = new double[32];
        internal int[] BucketItems = new int[32];
        internal Crossing[] BucketCopy = new Crossing[32];

        /// <summary>Takes this thread's cached workspace, or a new one while that is in use by an outer call.</summary>
        internal static Workspace Rent()
        {
            Workspace? workspace = cached;
            if (workspace is null) return new Workspace();
            cached = null;
            return workspace;
        }

        /// <summary>Makes the workspace available to the next call on this thread.</summary>
        internal static void Return(Workspace workspace) => cached = workspace;

        /// <summary>Vertex storage with at least the requested capacity.</summary>
        internal Point2[] VertexBuffer(int capacity)
        {
            if (Vertices.Length < capacity) Vertices = new Point2[Grow(capacity)];
            return Vertices;
        }
    }

    /// <summary>Appends a validated point unless it repeats the previous point of the current loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Append(Point2[] v, ref int count, int loopStart, Point2 p, string name)
    {
        PathInput.Validate(p, name);
        if (count == loopStart || !PathInput.Same(v[count - 1], p)) v[count++] = p;
    }

    /// <summary>Removes closing points equal to the loop's first point.</summary>
    internal static void CloseLoop(Point2[] v, ref int count, int loopStart)
    {
        while (count - loopStart > 1 && PathInput.Same(v[loopStart], v[count - 1])) count--;
    }

    /// <summary>Single loop ws.Vertices[0..n): NonZero, EvenOdd, absolute and signed winding integrals.</summary>
    internal static WindingAreaResult SingleLoop(Workspace ws, int n)
    {
        var statistics = new WindingStatistics();
        if (n < 3) return new WindingAreaResult(0, 0, 0, 0, statistics);
        Point2[] v = ws.Vertices;
        int[] nx = Links(ws, n, n);
        int total = FindCrossings(ws, n, n, ref statistics);
        int[] offsets = ws.Start;
        Crossing[] list = ws.Sorted;
        // The loop's own bounds center keeps the vectors to the origin as short as the loop allows.
        Point2 origin = Bounds(v, 0, n).Center;

        int first = Leftmost(v, 0, n);
        int w = RobustOrientation.Sign(v, Previous(first, 0, n), first, nx[first], ref statistics) > 0 ? 0 : -1;
        Sum nonZero = default, evenOdd = default, absolute = default, signed = default;
        for (int step = 0, e = first; step < n; step++, e = nx[e])
        {
            double fz = 0, fe = 0, fa = 0, previous = 0;
            for (int x = offsets[e]; x < offsets[e + 1]; x++)
            {
                double length = list[x].T - previous;
                fz += length * NonZeroStep(w); fe += length * EvenOddStep(w); fa += length * AbsoluteStep(w);
                previous = list[x].T; w += list[x].Delta;
            }
            double rest = 1 - previous;
            fz += rest * NonZeroStep(w); fe += rest * EvenOddStep(w); fa += rest * AbsoluteStep(w);
            double cross = Cross(v[e], v[nx[e]], origin);
            nonZero.Add(cross * fz); evenOdd.Add(cross * fe); absolute.Add(cross * fa); signed.Add(cross);
        }
        statistics.Crossings = total / 2;
        return new WindingAreaResult(nonZero.Value / 2, evenOdd.Value / 2, absolute.Value / 2, signed.Value / 2, statistics);
    }

    /// <summary>Two independently filled loops ws.Vertices[0..split) and [split..n).</summary>
    /// <remarks>
    /// Each path's own area is a closed chain of its own sub-edges, so it uses that path's own center. The
    /// intersection chain mixes sub-edges of both paths and needs one shared origin; its nonzero weights lie
    /// inside both paths, hence inside the intersection of their bounds, whose center is used. Union and
    /// symmetric difference follow by inclusion-exclusion. Distant small paths then keep their areas.
    /// </remarks>
    internal static WindingOverlapResult TwoLoops(Workspace ws, int split, int n, PathFillRule rule)
    {
        var statistics = new WindingStatistics();
        Point2[] v = ws.Vertices;
        Links(ws, split, n);
        int total = FindCrossings(ws, n, split, ref statistics);
        var boundsA = Bounds(v, 0, split);
        var boundsB = Bounds(v, split, n);
        double minX = Math.Max(boundsA.MinX, boundsB.MinX), maxX = Math.Min(boundsA.MaxX, boundsB.MaxX);
        double minY = Math.Max(boundsA.MinY, boundsB.MinY), maxY = Math.Min(boundsA.MaxY, boundsB.MaxY);
        // Disjoint bounds leave no weighted intersection sub-edge; any origin then gives exactly zero.
        Point2 shared = minX <= maxX && minY <= maxY ? new Point2(minX + (maxX - minX) / 2, minY + (maxY - minY) / 2) : boundsA.Center;
        bool nonZero = rule == PathFillRule.NonZero;
        Sum first = default, second = default, intersection = default;
        WalkLoop(ws, 0, split, split, n, nonZero, boundsA.Center, shared, ref first, ref intersection, ref statistics);
        WalkLoop(ws, split, n, 0, split, nonZero, boundsB.Center, shared, ref second, ref intersection, ref statistics);
        statistics.Crossings = total / 2;
        double a = first.Value / 2, b = second.Value / 2, both = intersection.Value / 2;
        return new WindingOverlapResult(a, b, both, a + b - both, a + b - 2 * both, rule, statistics);
    }

    private static void WalkLoop(Workspace ws, int from, int to, int otherFrom, int otherTo, bool nonZero,
        Point2 ownOrigin, Point2 sharedOrigin, ref Sum own, ref Sum intersection, ref WindingStatistics statistics)
    {
        Point2[] v = ws.Vertices;
        int[] nx = ws.Next, offsets = ws.Start;
        Crossing[] list = ws.Sorted;
        int first = Leftmost(v, from, to);
        int w = RobustOrientation.Sign(v, Previous(first, from, to), first, nx[first], ref statistics) > 0 ? 0 : -1;
        int other = WindingAt(v, nx, first, otherFrom, otherTo, ref statistics);
        for (int step = 0, e = first; step < to - from; step++, e = nx[e])
        {
            double fOwn = 0, fIntersection = 0, previous = 0;
            for (int x = offsets[e]; x <= offsets[e + 1]; x++)
            {
                double t = x < offsets[e + 1] ? list[x].T : 1, length = t - previous;
                int change = Fill(w + 1, nonZero) - Fill(w, nonZero);
                fOwn += length * change;
                fIntersection += length * change * Fill(other, nonZero);
                if (x == offsets[e + 1]) break;
                previous = t;
                if (list[x].SameLoop) w += list[x].Delta; else other += list[x].Delta;
            }
            own.Add(Cross(v[e], v[nx[e]], ownOrigin) * fOwn);
            if (fIntersection != 0) intersection.Add(Cross(v[e], v[nx[e]], sharedOrigin) * fIntersection);
        }
    }

    // Winding number of loop [from, to) at perturbed vertex p, by a rightward horizontal ray.
    private static int WindingAt(Point2[] v, int[] nx, int p, int from, int to, ref WindingStatistics statistics)
    {
        int w = 0;
        for (int e = from; e < to; e++)
        {
            int f = nx[e];
            bool startAbove = RobustOrientation.Above(v, e, p), endAbove = RobustOrientation.Above(v, f, p);
            if (startAbove == endAbove) continue;
            int side = RobustOrientation.Sign(v, e, f, p, ref statistics);
            if (endAbove && side > 0) w++;        // upward edge passes right of p
            else if (!endAbove && side < 0) w--;  // downward edge passes right of p
        }
        return w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Fill(int w, bool nonZero) => nonZero ? (w != 0 ? 1 : 0) : w & 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NonZeroStep(int w) => w == 0 ? 1 : w == -1 ? -1 : 0;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvenOddStep(int w) => (w & 1) == 0 ? 1 : -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AbsoluteStep(int w) => w >= 0 ? 1 : -1;

    private static int[] Links(Workspace ws, int split, int n)
    {
        if (ws.Next.Length < n) ws.Next = new int[Grow(n)];
        int[] nx = ws.Next;
        for (int e = 0; e < n; e++) nx[e] = e + 1;
        nx[split - 1] = 0;
        if (split < n) nx[n - 1] = split;
        return nx;
    }

    private static int Previous(int p, int from, int to) => p == from ? to - 1 : p - 1;

    private static int Leftmost(Point2[] v, int from, int to)
    {
        int best = from;
        for (int p = from + 1; p < to; p++) if (RobustOrientation.LeftOf(v, p, best)) best = p;
        return best;
    }

    private readonly struct Box
    {
        internal Box(double minX, double minY, double maxX, double maxY) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        internal double MinX { get; }
        internal double MinY { get; }
        internal double MaxX { get; }
        internal double MaxY { get; }
        internal Point2 Center => new(MinX + (MaxX - MinX) / 2, MinY + (MaxY - MinY) / 2);
    }

    private static Box Bounds(Point2[] v, int from, int to)
    {
        double minX = v[from].X, maxX = minX, minY = v[from].Y, maxY = minY;
        for (int i = from + 1; i < to; i++)
        {
            Point2 p = v[i];
            if (p.X < minX) minX = p.X; else if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; else if (p.Y > maxY) maxY = p.Y;
        }
        return new Box(minX, minY, maxX, maxY);
    }

    // cross(a - o, b - o) rewritten as cross(a - o, b - a): the products are |a - o| |b - a| instead of
    // |a - o| |b - o|, so a short edge far from the origin does not lose its contribution to cancellation.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Cross(Point2 a, Point2 b, Point2 o) => (a.X - o.X) * (b.Y - a.Y) - (a.Y - o.Y) * (b.X - a.X);

    // Records every symbolic proper crossing between nonadjacent edges and orders them along each edge.
    // Returns the number of recorded crossing incidences (two per crossing).
    private static int FindCrossings(Workspace ws, int n, int split, ref WindingStatistics statistics)
    {
        Point2[] v = ws.Vertices;
        int[] nx = ws.Next;
        if (ws.Keys.Length < n) { ws.Keys = new double[Grow(n)]; ws.Order = new int[ws.Keys.Length]; }
        if (ws.Start.Length < n + 1) ws.Start = new int[Grow(n + 1)];
        double[] k = ws.Keys; int[] o = ws.Order;
        for (int e = 0; e < n; e++) { k[e] = Math.Min(v[e].X, v[nx[e]].X); o[e] = e; }
        Array.Sort(k, o, 0, n);

        int count = 0;
        for (int a = 0; a < n; a++)
        {
            int i = o[a], i1 = nx[i];
            Point2 pa = v[i], pb = v[i1];
            double maxX = Math.Max(pa.X, pb.X), minY = Math.Min(pa.Y, pb.Y), maxY = Math.Max(pa.Y, pb.Y);
            for (int b = a + 1; b < n && k[b] <= maxX; b++)
            {
                int j = o[b], j1 = nx[j];
                if (j1 == i || i1 == j) continue; // adjacent edges share a vertex and cannot properly cross
                Point2 pc = v[j], pd = v[j1];
                if (Math.Max(pc.Y, pd.Y) < minY || Math.Min(pc.Y, pd.Y) > maxY) continue;
                int sc = RobustOrientation.TryFilter(pa, pb, pc, out double oc, out double ec) ? Sign(oc) : RobustOrientation.Resolve(v, i, i1, j, ref statistics);
                int sd = RobustOrientation.TryFilter(pa, pb, pd, out double od, out double ed) ? Sign(od) : RobustOrientation.Resolve(v, i, i1, j1, ref statistics);
                if (sc == sd) continue;
                int sa = RobustOrientation.TryFilter(pc, pd, pa, out double oa, out double ea) ? Sign(oa) : RobustOrientation.Resolve(v, j, j1, i, ref statistics);
                int sb = RobustOrientation.TryFilter(pc, pd, pb, out double ob, out double eb) ? Sign(ob) : RobustOrientation.Resolve(v, j, j1, i1, ref statistics);
                if (sa == sb) continue;
                Parameters(v, i, i1, j, j1, oa, ob, ea + eb, oc, od, ec + ed, out double ti, out double tj);
                if (count + 2 > ws.Found.Length) Array.Resize(ref ws.Found, ws.Found.Length * 2);
                bool same = (i < split) == (j < split);
                // Moving along i across j changes winding by sign(cross(dir j, dir i)) = sc.
                ws.Found[count++] = new Crossing { Edge = i, T = ti, Delta = sc, SameLoop = same };
                ws.Found[count++] = new Crossing { Edge = j, T = tj, Delta = -sc, SameLoop = same };
            }
        }

        int[] s = ws.Start;
        Crossing[] found = ws.Found;
        Array.Clear(s, 0, n + 1);
        for (int c = 0; c < count; c++) s[found[c].Edge + 1]++;
        for (int e = 0; e < n; e++) s[e + 1] += s[e];
        if (ws.Sorted.Length < count) ws.Sorted = new Crossing[Grow(count)];
        Crossing[] sorted = ws.Sorted;
        for (int e = 0; e < n; e++) o[e] = s[e];
        for (int c = 0; c < count; c++) sorted[o[found[c].Edge]++] = found[c];
        for (int e = 0; e < n; e++)
        {
            int lo = s[e], length = s[e + 1] - lo;
            if (length > 16) SortLarge(ws, lo, length);
            else
                for (int x = lo + 1; x < lo + length; x++)
                {
                    Crossing key = sorted[x]; int y = x - 1;
                    while (y >= lo && sorted[y].T > key.T) { sorted[y + 1] = sorted[y]; y--; }
                    sorted[y + 1] = key;
                }
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Sign(double value) => value > 0 ? 1 : -1;

    // Keyed primitive sort: Array.Sort with an IComparer allocates a delegate on every call.
    private static void SortLarge(Workspace ws, int lo, int length)
    {
        if (ws.BucketKeys.Length < length)
        {
            int size = Grow(length);
            ws.BucketKeys = new double[size]; ws.BucketItems = new int[size]; ws.BucketCopy = new Crossing[size];
        }
        Crossing[] list = ws.Sorted, copy = ws.BucketCopy;
        double[] keys = ws.BucketKeys; int[] items = ws.BucketItems;
        for (int x = 0; x < length; x++) { keys[x] = list[lo + x].T; items[x] = x; copy[x] = list[lo + x]; }
        Array.Sort(keys, items, 0, length);
        for (int x = 0; x < length; x++) list[lo + x] = copy[items[x]];
    }

    // Crossing position along i (a->b) and j (c->d). Which edges cross was decided exactly; positions only
    // need to be accurate, because swapping two crossings closer than the position error changes winding on
    // that short piece alone. Ill-conditioned floating-point ratios are therefore recomputed from accurate
    // determinants. For exactly collinear edges the symbolic perturbation is dominated by the lowest-index
    // endpoint P, whose normal offset vanishes only at the other endpoint of the edge containing P: in the
    // limit the perturbed edges cross exactly there, which keeps several overlapping edges consistent.
    private static void Parameters(Point2[] v, int i, int i1, int j, int j1, double oa, double ob, double errorI,
        double oc, double od, double errorJ, out double ti, out double tj)
    {
        Point2 a = v[i], b = v[i1], c = v[j], d = v[j1];
        bool accurateI = oa != 0 && ob != 0 && (oa > 0) != (ob > 0) && errorI <= Accuracy * (Math.Abs(oa) + Math.Abs(ob));
        bool accurateJ = oc != 0 && od != 0 && (oc > 0) != (od > 0) && errorJ <= Accuracy * (Math.Abs(oc) + Math.Abs(od));
        bool collinear = false;
        if (accurateI) ti = oa / (oa - ob);
        else if (!RobustOrientation.CrossingParameter(c, d, a, b, out ti)) collinear = true;
        if (accurateJ) tj = oc / (oc - od);
        else if (!RobustOrientation.CrossingParameter(a, b, c, d, out tj)) collinear = true;
        if (collinear)
        {
            int p = Math.Min(Math.Min(i, i1), Math.Min(j, j1));
            int vertex = p == i ? i1 : p == i1 ? i : p == j ? j1 : j;
            ti = vertex == i ? 0 : vertex == i1 ? 1 : Project(v[vertex], a, b);
            tj = vertex == j ? 0 : vertex == j1 ? 1 : Project(v[vertex], c, d);
        }
        // Every path above yields a finite ratio: denominators are differences of distinct doubles or of
        // determinants with opposite signs. A NaN would be an internal error, never a position to guess.
        if (double.IsNaN(ti) || double.IsNaN(tj))
            throw new InvalidOperationException("Internal error: a crossing position could not be computed.");
        ti = Clamp(ti); tj = Clamp(tj);
    }

    private const double Accuracy = 3.552713678800501e-15; // 2^-48

    // Parameter of p, which lies exactly on the line a->b, along the dominant coordinate. Unlike a dot-product
    // projection it squares nothing, so tiny or anisotropic coordinates cannot underflow to 0/0.
    private static double Project(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y; // nonzero along at least one axis: a and b are distinct doubles
        return Math.Abs(dx) >= Math.Abs(dy) ? (p.X - a.X) / dx : (p.Y - a.Y) / dy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    private static int Grow(int capacity) => Math.Max(256, capacity + capacity / 2);

    // Neumaier summation for edge contributions of mixed signs.
    private struct Sum
    {
        private double sum, compensation;
        internal void Add(double value)
        {
            double t = sum + value;
            compensation += Math.Abs(sum) >= Math.Abs(value) ? (sum - t) + value : (value - t) + sum;
            sum = t;
        }
        internal readonly double Value => sum + compensation;
    }
}
