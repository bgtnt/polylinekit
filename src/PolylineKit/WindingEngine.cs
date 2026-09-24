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
/// of [F(left) - F(right)] * cross(a, b) * (t1 - t0). Sub-edges split input edges at crossings;
/// left and right differ by one in the winding of the edge's own loop. Winding numbers start at
/// each loop's leftmost perturbed vertex, where the local configuration determines them, and are
/// propagated across crossings. All combinatorial decisions use RobustOrientation.
/// </remarks>
internal static class WindingEngine
{
    private struct Crossing
    {
        internal double T;
        internal int Edge;
        internal int Delta;
        internal bool SameLoop;
    }


    [ThreadStatic] private static Point2[]? vertices;
    [ThreadStatic] private static int[]? next;
    [ThreadStatic] private static double[]? keys;
    [ThreadStatic] private static int[]? order;
    [ThreadStatic] private static int[]? start;
    [ThreadStatic] private static Crossing[]? found;
    [ThreadStatic] private static Crossing[]? sorted;
    [ThreadStatic] private static double[]? bucketKeys;
    [ThreadStatic] private static int[]? bucketItems;
    [ThreadStatic] private static Crossing[]? bucketCopy;

    /// <summary>Reusable per-thread vertex buffer with at least the requested capacity.</summary>
    internal static Point2[] Vertices(int capacity)
    {
        if (vertices is null || vertices.Length < capacity) vertices = new Point2[Grow(capacity)];
        return vertices;
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

    /// <summary>Single loop v[0..n): NonZero, EvenOdd, absolute and signed winding integrals.</summary>
    internal static WindingAreaResult SingleLoop(Point2[] v, int n)
    {
        var statistics = new WindingStatistics();
        if (n < 3) return new WindingAreaResult(0, 0, 0, 0, statistics);
        int[] nx = Links(n, n);
        int total = FindCrossings(v, n, nx, n, ref statistics);
        int[] offsets = start!;
        Crossing[] list = sorted!;
        Point2 origin = Center(v, n);

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

    /// <summary>Two independently filled loops v[0..split) and v[split..n).</summary>
    internal static WindingOverlapResult TwoLoops(Point2[] v, int split, int n, PathFillRule rule)
    {
        var statistics = new WindingStatistics();
        int[] nx = Links(split, n);
        int total = FindCrossings(v, n, nx, split, ref statistics);
        Point2 origin = Center(v, n);
        bool nonZero = rule == PathFillRule.NonZero;
        Sum first = default, second = default, intersection = default, union = default, difference = default;
        WalkLoop(v, nx, 0, split, split, n, nonZero, origin, ref first, ref intersection, ref union, ref difference, ref statistics);
        WalkLoop(v, nx, split, n, 0, split, nonZero, origin, ref second, ref intersection, ref union, ref difference, ref statistics);
        statistics.Crossings = total / 2;
        return new WindingOverlapResult(first.Value / 2, second.Value / 2, intersection.Value / 2,
            union.Value / 2, difference.Value / 2, rule, statistics);
    }

    private static void WalkLoop(Point2[] v, int[] nx, int from, int to, int otherFrom, int otherTo, bool nonZero,
        Point2 origin, ref Sum own, ref Sum intersection, ref Sum union, ref Sum difference, ref WindingStatistics statistics)
    {
        int[] offsets = start!;
        Crossing[] list = sorted!;
        int first = Leftmost(v, from, to);
        int w = RobustOrientation.Sign(v, Previous(first, from, to), first, nx[first], ref statistics) > 0 ? 0 : -1;
        int other = WindingAt(v, nx, first, otherFrom, otherTo, ref statistics);
        for (int step = 0, e = first; step < to - from; step++, e = nx[e])
        {
            double fOwn = 0, fIntersection = 0, fUnion = 0, fDifference = 0, previous = 0;
            for (int x = offsets[e]; x <= offsets[e + 1]; x++)
            {
                double t = x < offsets[e + 1] ? list[x].T : 1, length = t - previous;
                int step1 = Fill(w + 1, nonZero) - Fill(w, nonZero), inside = Fill(other, nonZero);
                fOwn += length * step1;
                fIntersection += length * step1 * inside;
                fUnion += length * step1 * (1 - inside);
                fDifference += length * step1 * (1 - 2 * inside);
                if (x == offsets[e + 1]) break;
                previous = t;
                if (list[x].SameLoop) w += list[x].Delta; else other += list[x].Delta;
            }
            double cross = Cross(v[e], v[nx[e]], origin);
            own.Add(cross * fOwn); intersection.Add(cross * fIntersection);
            union.Add(cross * fUnion); difference.Add(cross * fDifference);
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

    private static int[] Links(int split, int n)
    {
        if (next is null || next.Length < n) next = new int[Grow(n)];
        int[] nx = next;
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

    private static Point2 Center(Point2[] v, int n)
    {
        double minX = v[0].X, maxX = minX, minY = v[0].Y, maxY = minY;
        for (int i = 1; i < n; i++)
        {
            Point2 p = v[i];
            if (p.X < minX) minX = p.X; else if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; else if (p.Y > maxY) maxY = p.Y;
        }
        return new Point2(minX + (maxX - minX) / 2, minY + (maxY - minY) / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Cross(Point2 a, Point2 b, Point2 o) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    // Records every symbolic proper crossing between nonadjacent edges and orders them along each edge.
    // Returns the number of recorded crossing incidences (two per crossing).
    private static int FindCrossings(Point2[] v, int n, int[] nx, int split, ref WindingStatistics statistics)
    {
        if (keys is null || keys.Length < n) { keys = new double[Grow(n)]; order = new int[keys.Length]; }
        if (start is null || start.Length < n + 1) start = new int[Grow(n + 1)];
        found ??= new Crossing[256];
        double[] k = keys; int[] o = order!;
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
                if (count + 2 > found.Length) Array.Resize(ref found, found.Length * 2);
                bool same = (i < split) == (j < split);
                // Moving along i across j changes winding by sign(cross(dir j, dir i)) = sc.
                found[count++] = new Crossing { Edge = i, T = ti, Delta = sc, SameLoop = same };
                found[count++] = new Crossing { Edge = j, T = tj, Delta = -sc, SameLoop = same };
            }
        }

        int[] s = start;
        Array.Clear(s, 0, n + 1);
        for (int c = 0; c < count; c++) s[found[c].Edge + 1]++;
        for (int e = 0; e < n; e++) s[e + 1] += s[e];
        if (sorted is null || sorted.Length < count) sorted = new Crossing[Grow(count)];
        for (int e = 0; e < n; e++) o[e] = s[e];
        for (int c = 0; c < count; c++) sorted[o[found[c].Edge]++] = found[c];
        for (int e = 0; e < n; e++)
        {
            int lo = s[e], length = s[e + 1] - lo;
            if (length > 16) SortLarge(sorted, lo, length);
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
    private static void SortLarge(Crossing[] list, int lo, int length)
    {
        if (bucketKeys is null || bucketKeys.Length < length)
        {
            int size = Grow(length);
            bucketKeys = new double[size]; bucketItems = new int[size]; bucketCopy = new Crossing[size];
        }
        double[] keys = bucketKeys; int[] items = bucketItems!; Crossing[] copy = bucketCopy!;
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
        ti = Clamp(ti); tj = Clamp(tj);
    }

    private const double Accuracy = 3.552713678800501e-15; // 2^-48

    private static double Project(Point2 p, Point2 a, Point2 b)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y;
        return ((p.X - a.X) * ux + (p.Y - a.Y) * uy) / (ux * ux + uy * uy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double t) => double.IsNaN(t) ? .5 : t < 0 ? 0 : t > 1 ? 1 : t;

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
