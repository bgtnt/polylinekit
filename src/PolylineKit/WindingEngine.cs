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
/// For any weight F of the winding numbers, integral F dA is one half of the shoelace sum of a boundary chain:
/// every sub-edge (a segment between shared vertices and crossing points) enters with coefficient
/// F(left) - F(right), and left and right differ by one in the winding of the sub-edge's own loop. Winding numbers
/// start at each loop's leftmost perturbed vertex, where the local configuration determines them, and are
/// propagated across crossings. All combinatorial decisions use RobustOrientation.
///
/// Each chain is formed exactly before any area term is evaluated. Edges that overlap collinearly are split at
/// each other's endpoints, so a shared boundary piece is the same segment in every loop that contains it, and
/// identical segments of a chain are netted by their integer coefficients. Only the remaining net segments are
/// summed, around the center of their own bounds. A long shared boundary then cancels exactly instead of leaving
/// rounding of its large terms, and a small region keeps its area regardless of what else the input contains.
/// </remarks>
internal static class WindingEngine
{
    internal struct Crossing
    {
        internal double T; // parameter measured from the edge start
        internal double U; // the same position measured from the edge end, 1 - T without cancellation
        internal Point2 P; // the crossing point, shared by both incidences; exactly the vertex when it lies on one
        internal int Edge;
        internal int Delta; // winding change of the other edge's loop; zero for a split at a collinear overlap
        internal bool SameLoop;
    }

    // One oriented segment with its integer coefficient in each chain.
    internal struct Piece
    {
        internal Point2 P0, P1;
        internal int W0, W1, W2, W3, W4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly int Weight(int chain) => chain switch { 0 => W0, 1 => W1, 2 => W2, 3 => W3, _ => W4 };
        internal readonly bool IsZero => (W0 | W1 | W2 | W3 | W4) == 0;
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
        internal bool[] Overlapping = new bool[256];
        internal Crossing[] Found = new Crossing[256];
        internal Crossing[] Sorted = new Crossing[256];
        internal double[] BucketKeys = new double[32];
        internal int[] BucketItems = new int[32];
        internal Crossing[] BucketCopy = new Crossing[32];
        internal Piece[] Shared = new Piece[64];
        internal int SharedCount;
        internal readonly double[] MinX = new double[MaxChains], MinY = new double[MaxChains], MaxX = new double[MaxChains], MaxY = new double[MaxChains];
        internal readonly bool[] Used = new bool[MaxChains];
        internal readonly Point2[] Origin = new Point2[MaxChains];
        internal readonly int[] Group = new int[MaxChains];
        internal readonly Sum[] Sums = new Sum[MaxChains];

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

        /// <summary>Collects a sub-edge of a collinearly overlapping edge, with its coefficients, for netting.</summary>
        internal void Take(Piece piece, bool overlapping)
        {
            if (piece.IsZero || !overlapping) return;
            // Canonical orientation: a segment and its reverse must meet as the same key.
            if (Less(piece.P1, piece.P0))
                piece = new Piece { P0 = piece.P1, P1 = piece.P0, W0 = -piece.W0, W1 = -piece.W1, W2 = -piece.W2, W3 = -piece.W3, W4 = -piece.W4 };
            if (SharedCount == Shared.Length) Array.Resize(ref Shared, Shared.Length * 2);
            Shared[SharedCount++] = piece;
        }
    }

    private const int MaxChains = 5;

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

    private const int NonZeroChain = 0, EvenOddChain = 1, AbsoluteChain = 2, SignedChain = 3;

    /// <summary>Single loop ws.Vertices[0..n): NonZero, EvenOdd, absolute and signed winding integrals.</summary>
    /// <remarks>
    /// All four chains consist of the loop's own sub-edges, so one origin (the loop's bounds center) serves them
    /// and each sub-edge needs one area term. Sub-edges of collinearly overlapping edges are netted first.
    /// </remarks>
    internal static WindingAreaResult SingleLoop(Workspace ws, int n)
    {
        var statistics = new WindingStatistics();
        if (n < 3) return new WindingAreaResult(0, 0, 0, 0, statistics);
        Point2[] v = ws.Vertices;
        int[] nx = Links(ws, n, n);
        statistics.Crossings = FindCrossings(ws, n, n, ref statistics);
        int[] offsets = ws.Start;
        Crossing[] list = ws.Sorted;
        Point2 origin = Center(v, 0, n);
        int first = Leftmost(v, 0, n);
        int w = RobustOrientation.Sign(v, Previous(first, 0, n), first, nx[first], ref statistics) > 0 ? 0 : -1;
        ws.SharedCount = 0;
        Sum nonZero = default, evenOdd = default, absolute = default, signed = default;
        for (int step = 0, e = first; step < n; step++, e = nx[e])
        {
            Point2 b = v[nx[e]], p0 = v[e];
            bool overlapping = ws.Overlapping[e];
            for (int x = offsets[e]; x <= offsets[e + 1]; x++)
            {
                bool last = x == offsets[e + 1];
                Point2 p1 = last ? b : list[x].P;
                if (!PathInput.Same(p0, p1))
                {
                    if (overlapping) ws.Take(new Piece { P0 = p0, P1 = p1, W0 = NonZeroStep(w), W1 = EvenOddStep(w), W2 = AbsoluteStep(w), W3 = 1 }, true);
                    else
                    {
                        double cross = Cross(p0, p1, origin);
                        int step0 = NonZeroStep(w);
                        if (step0 != 0) nonZero.Add(cross * step0);
                        evenOdd.Add(cross * EvenOddStep(w)); absolute.Add(cross * AbsoluteStep(w)); signed.Add(cross);
                    }
                }
                if (last) break;
                p0 = p1; w += list[x].Delta;
            }
        }
        int net = Net(ws);
        for (int x = 0; x < net; x++)
        {
            Piece piece = ws.Shared[x];
            double cross = Cross(piece.P0, piece.P1, origin);
            nonZero.Add(cross * piece.W0); evenOdd.Add(cross * piece.W1); absolute.Add(cross * piece.W2); signed.Add(cross * piece.W3);
        }
        return new WindingAreaResult(nonZero.Value / 2, evenOdd.Value / 2, absolute.Value / 2, signed.Value / 2, statistics);
    }

    private const int OwnA = 0, OwnB = 1, AOnly = 2, BOnly = 3, Both = 4;

    /// <summary>Two independently filled loops ws.Vertices[0..split) and [split..n).</summary>
    /// <remarks>
    /// Five chains are formed: each path's own area, A without B, B without A, and A and B. Union and symmetric
    /// difference are sums of these nonnegative areas, never differences of rounded totals, so a small symmetric
    /// difference between large regions survives. Each chain is summed around the center of its own net bounds:
    /// a first walk finds those bounds (and collects the sub-edges of overlapping edges for netting), a second
    /// walk adds the terms.
    /// </remarks>
    internal static WindingOverlapResult TwoLoops(Workspace ws, int split, int n, PathFillRule rule)
    {
        var statistics = new WindingStatistics();
        Point2[] v = ws.Vertices;
        int[] nx = Links(ws, split, n);
        statistics.Crossings = FindCrossings(ws, n, split, ref statistics);
        bool nonZero = rule == PathFillRule.NonZero;
        int firstA = Leftmost(v, 0, split), firstB = Leftmost(v, split, n);
        int ownA = RobustOrientation.Sign(v, Previous(firstA, 0, split), firstA, nx[firstA], ref statistics) > 0 ? 0 : -1;
        int ownB = RobustOrientation.Sign(v, Previous(firstB, split, n), firstB, nx[firstB], ref statistics) > 0 ? 0 : -1;
        int bAtA = WindingAt(v, nx, firstA, split, n, ref statistics), aAtB = WindingAt(v, nx, firstB, 0, split, ref statistics);
        ws.SharedCount = 0;
        Array.Clear(ws.Used, 0, MaxChains);
        Array.Clear(ws.Sums, 0, MaxChains);
        WalkRegions(ws, split, n, firstA, ownA, bAtA, nonZero, true, false);
        WalkRegions(ws, split, n, firstB, ownB, aAtB, nonZero, false, false);
        int net = Net(ws);
        for (int x = 0; x < net; x++) Include(ws, ws.Shared[x], MaxChains);
        for (int c = 0; c < MaxChains; c++)
        {
            ws.Origin[c] = ws.Used[c] ? new Point2(ws.MinX[c] + (ws.MaxX[c] - ws.MinX[c]) / 2, ws.MinY[c] + (ws.MaxY[c] - ws.MinY[c]) / 2) : default;
            ws.Group[c] = c;
            for (int g = 0; g < c; g++)
                if (ws.Group[g] == g && PathInput.Same(ws.Origin[g], ws.Origin[c])) { ws.Group[c] = g; break; }
        }
        WalkRegions(ws, split, n, firstA, ownA, bAtA, nonZero, true, true);
        WalkRegions(ws, split, n, firstB, ownB, aAtB, nonZero, false, true);
        for (int x = 0; x < net; x++) Add(ws, ws.Shared[x], MaxChains);
        double aOnly = Area(ws, AOnly), bOnly = Area(ws, BOnly), both = Area(ws, Both);
        return new WindingOverlapResult(Area(ws, OwnA), Area(ws, OwnB), both, aOnly + bOnly + both, aOnly + bOnly, rule, statistics);
    }

    // One walk around loop A ([0, split)) or B ([split, n)) from its initial winding state. Without sum it records
    // chain bounds of ordinary sub-edges and collects those of overlapping edges; with sum it adds their terms.
    private static void WalkRegions(Workspace ws, int split, int n, int first, int w, int other, bool nonZero, bool isA, bool sum)
    {
        Point2[] v = ws.Vertices;
        int[] nx = ws.Next, offsets = ws.Start;
        Crossing[] list = ws.Sorted;
        int edges = isA ? split : n - split;
        int own = isA ? OwnA : OwnB, only = isA ? AOnly : BOnly, otherOnly = isA ? BOnly : AOnly;
        for (int step = 0, e = first; step < edges; step++, e = nx[e])
        {
            Point2 p0 = v[e], b = v[nx[e]];
            bool overlapping = ws.Overlapping[e];
            for (int x = offsets[e]; x <= offsets[e + 1]; x++)
            {
                bool last = x == offsets[e + 1];
                Point2 p1 = last ? b : list[x].P;
                int change = Fill(w + 1, nonZero) - Fill(w, nonZero);
                if (change != 0 && !PathInput.Same(p0, p1))
                {
                    // Inside the other path a piece bounds the intersection and removes area from the other
                    // path's exclusive part; outside it, it bounds this path's exclusive part.
                    bool inside = Fill(other, nonZero) != 0;
                    if (overlapping)
                    {
                        if (!sum)
                        {
                            var piece = new Piece { P0 = p0, P1 = p1 };
                            Set(ref piece, own, change);
                            if (inside) { Set(ref piece, Both, change); Set(ref piece, otherOnly, -change); }
                            else Set(ref piece, only, change);
                            ws.Take(piece, true);
                        }
                    }
                    else if (!sum)
                    {
                        Include(ws, own, p0, p1);
                        if (inside) { Include(ws, Both, p0, p1); Include(ws, otherOnly, p0, p1); }
                        else Include(ws, only, p0, p1);
                    }
                    else
                    {
                        double crossOwn = Cross(p0, p1, ws.Origin[own]);
                        ws.Sums[own].Add(crossOwn * change);
                        if (inside)
                        {
                            double crossBoth = ws.Group[Both] == ws.Group[own] ? crossOwn : Cross(p0, p1, ws.Origin[Both]);
                            ws.Sums[Both].Add(crossBoth * change);
                            double crossOther = ws.Group[otherOnly] == ws.Group[own] ? crossOwn
                                : ws.Group[otherOnly] == ws.Group[Both] ? crossBoth : Cross(p0, p1, ws.Origin[otherOnly]);
                            ws.Sums[otherOnly].Add(crossOther * -change);
                        }
                        else
                        {
                            double crossOnly = ws.Group[only] == ws.Group[own] ? crossOwn : Cross(p0, p1, ws.Origin[only]);
                            ws.Sums[only].Add(crossOnly * change);
                        }
                    }
                }
                if (last) break;
                p0 = p1;
                if (list[x].SameLoop) w += list[x].Delta; else other += list[x].Delta;
            }
        }
    }

    private static Point2 Center(Point2[] v, int from, int to)
    {
        double minX = v[from].X, maxX = minX, minY = v[from].Y, maxY = minY;
        for (int i = from + 1; i < to; i++)
        {
            Point2 p = v[i];
            if (p.X < minX) minX = p.X; else if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; else if (p.Y > maxY) maxY = p.Y;
        }
        return new Point2(minX + (maxX - minX) / 2, minY + (maxY - minY) / 2);
    }

    private static void Include(Workspace ws, int c, Point2 p0, Point2 p1)
    {
        if (!ws.Used[c]) { ws.Used[c] = true; ws.MinX[c] = ws.MaxX[c] = p0.X; ws.MinY[c] = ws.MaxY[c] = p0.Y; }
        Extend(ws, c, p0); Extend(ws, c, p1);
    }

    private static void Set(ref Piece piece, int chain, int weight)
    {
        switch (chain)
        {
            case 0: piece.W0 = weight; break;
            case 1: piece.W1 = weight; break;
            case 2: piece.W2 = weight; break;
            case 3: piece.W3 = weight; break;
            default: piece.W4 = weight; break;
        }
    }

    // Sorts the collected sub-edges of overlapping edges and nets identical segments by their coefficients.
    // Returns the number of remaining segments, stored at the start of ws.Shared.
    private static int Net(Workspace ws)
    {
        Piece[] shared = ws.Shared;
        SortPieces(shared, ws.SharedCount);
        int net = 0;
        for (int x = 0; x < ws.SharedCount;)
        {
            Piece total = shared[x];
            int y = x + 1;
            for (; y < ws.SharedCount && SameSegment(shared[x], shared[y]); y++)
            {
                total.W0 += shared[y].W0; total.W1 += shared[y].W1; total.W2 += shared[y].W2;
                total.W3 += shared[y].W3; total.W4 += shared[y].W4;
            }
            if (!total.IsZero) shared[net++] = total;
            x = y;
        }
        return net;
    }

    private static void Include(Workspace ws, Piece piece, int chains)
    {
        for (int c = 0; c < chains; c++)
            if (piece.Weight(c) != 0) Include(ws, c, piece.P0, piece.P1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Extend(Workspace ws, int c, Point2 p)
    {
        if (p.X < ws.MinX[c]) ws.MinX[c] = p.X; else if (p.X > ws.MaxX[c]) ws.MaxX[c] = p.X;
        if (p.Y < ws.MinY[c]) ws.MinY[c] = p.Y; else if (p.Y > ws.MaxY[c]) ws.MaxY[c] = p.Y;
    }

    private static void Add(Workspace ws, Piece piece, int chains)
    {
        int group = -1;
        double cross = 0;
        for (int c = 0; c < chains; c++)
        {
            int weight = piece.Weight(c);
            if (weight == 0) continue;
            if (ws.Group[c] != group) { group = ws.Group[c]; cross = Cross(piece.P0, piece.P1, ws.Origin[group]); }
            ws.Sums[c].Add(cross * weight);
        }
    }

    private static double Area(Workspace ws, int chain) => ws.Sums[chain].Value / 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Less(Point2 p, Point2 q) => p.X < q.X || (p.X == q.X && p.Y < q.Y);

    private static bool SameSegment(Piece x, Piece y) => PathInput.Same(x.P0, y.P0) && PathInput.Same(x.P1, y.P1);

    private static int Compare(Piece x, Piece y)
    {
        if (!PathInput.Same(x.P0, y.P0)) return Less(x.P0, y.P0) ? -1 : 1;
        if (!PathInput.Same(x.P1, y.P1)) return Less(x.P1, y.P1) ? -1 : 1;
        return 0;
    }

    // In-place heap sort: only pieces of collinearly overlapping edges are sorted, and no delegate is allocated.
    private static void SortPieces(Piece[] items, int count)
    {
        if (count <= 16)
        {
            for (int x = 1; x < count; x++)
            {
                Piece key = items[x]; int y = x - 1;
                while (y >= 0 && Compare(items[y], key) > 0) { items[y + 1] = items[y]; y--; }
                items[y + 1] = key;
            }
            return;
        }
        for (int root = count / 2 - 1; root >= 0; root--) SiftDown(items, root, count);
        for (int end = count - 1; end > 0; end--)
        {
            (items[0], items[end]) = (items[end], items[0]);
            SiftDown(items, 0, end);
        }
    }

    private static void SiftDown(Piece[] items, int root, int count)
    {
        while (true)
        {
            int child = 2 * root + 1;
            if (child >= count) return;
            if (child + 1 < count && Compare(items[child + 1], items[child]) > 0) child++;
            if (Compare(items[root], items[child]) >= 0) return;
            (items[root], items[child]) = (items[child], items[root]);
            root = child;
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

    // cross(a - o, b - o) rewritten as cross(m - o, b - a) with m the segment midpoint: the products are
    // |m - o| |b - a| instead of |a - o| |b - o|, so a short segment far from the origin does not lose its
    // contribution to cancellation, and the same segment traversed backwards gives exactly the negated term.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Cross(Point2 a, Point2 b, Point2 o) =>
        ((a.X + b.X) * .5 - o.X) * (b.Y - a.Y) - ((a.Y + b.Y) * .5 - o.Y) * (b.X - a.X);

    // Records every symbolic proper crossing between nonadjacent edges, and every split where edges overlap
    // collinearly, and orders them along each edge. Returns the number of proper crossings.
    private static int FindCrossings(Workspace ws, int n, int split, ref WindingStatistics statistics)
    {
        Point2[] v = ws.Vertices;
        int[] nx = ws.Next;
        if (ws.Keys.Length < n) { ws.Keys = new double[Grow(n)]; ws.Order = new int[ws.Keys.Length]; }
        if (ws.Start.Length < n + 1) ws.Start = new int[Grow(n + 1)];
        if (ws.Overlapping.Length < n) ws.Overlapping = new bool[Grow(n)];
        Array.Clear(ws.Overlapping, 0, n);
        double[] k = ws.Keys; int[] o = ws.Order;
        for (int e = 0; e < n; e++) { k[e] = Math.Min(v[e].X, v[nx[e]].X); o[e] = e; }
        Array.Sort(k, o, 0, n);

        int count = 0, crossings = 0;
        for (int a = 0; a < n; a++)
        {
            int i = o[a], i1 = nx[i];
            Point2 pa = v[i], pb = v[i1];
            double maxX = Math.Max(pa.X, pb.X), minY = Math.Min(pa.Y, pb.Y), maxY = Math.Max(pa.Y, pb.Y);
            for (int b = a + 1; b < n && k[b] <= maxX; b++)
            {
                int j = o[b], j1 = nx[j];
                Point2 pc = v[j], pd = v[j1];
                if (Math.Max(pc.Y, pd.Y) < minY || Math.Min(pc.Y, pd.Y) > maxY) continue;
                if (j1 == i || i1 == j)
                {
                    // Adjacent edges share a vertex and cannot properly cross, but may retrace each other.
                    int shared = j1 == i ? i : j, farI = j1 == i ? i1 : i, farJ = j1 == i ? j : j1;
                    if (Collinear(v[shared], v[farI], v[farJ]) && Dot(v[shared], v[farI], v[farJ]) > 0)
                        MarkOverlap(ws, ref count, i, i1, j, j1);
                    continue;
                }
                bool certainC = RobustOrientation.TryFilter(pa, pb, pc, out double oc, out double ec);
                bool certainD = RobustOrientation.TryFilter(pa, pb, pd, out double od, out double ed);
                int sc = certainC ? Sign(oc) : Exact(v, i, i1, j, ref statistics);
                int sd = certainD ? Sign(od) : Exact(v, i, i1, j1, ref statistics);
                if (sc == 0 && sd == 0) MarkOverlap(ws, ref count, i, i1, j, j1);
                if (sc == 0) sc = Symbolic(v, i, i1, j, ref statistics);
                if (sd == 0) sd = Symbolic(v, i, i1, j1, ref statistics);
                if (sc == sd) continue;
                int sa = RobustOrientation.TryFilter(pc, pd, pa, out double oa, out double ea) ? Sign(oa) : RobustOrientation.Resolve(v, j, j1, i, ref statistics);
                int sb = RobustOrientation.TryFilter(pc, pd, pb, out double ob, out double eb) ? Sign(ob) : RobustOrientation.Resolve(v, j, j1, i1, ref statistics);
                if (sa == sb) continue;
                Parameters(v, i, i1, j, j1, oa, ob, ea + eb, oc, od, ec + ed, out double ti, out double ui, out double tj, out double uj, out Point2 point);
                if (count + 2 > ws.Found.Length) Array.Resize(ref ws.Found, ws.Found.Length * 2);
                bool same = (i < split) == (j < split);
                // Moving along i across j changes winding by sign(cross(dir j, dir i)) = sc.
                ws.Found[count++] = new Crossing { Edge = i, T = ti, U = ui, P = point, Delta = sc, SameLoop = same };
                ws.Found[count++] = new Crossing { Edge = j, T = tj, U = uj, P = point, Delta = -sc, SameLoop = same };
                crossings++;
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
            Point2 direction = new(v[nx[e]].X - v[e].X, v[nx[e]].Y - v[e].Y);
            if (length > 16) SortLarge(ws, lo, length, direction);
            else
                for (int x = lo + 1; x < lo + length; x++)
                {
                    Crossing key = sorted[x]; int y = x - 1;
                    while (y >= lo && After(sorted[y], key, direction)) { sorted[y + 1] = sorted[y]; y--; }
                    sorted[y + 1] = key;
                }
        }
        return crossings;
    }

    // Exact sign of an uncertain orientation, zero included; the symbolic tie-break is applied separately.
    private static int Exact(Point2[] v, int a, int b, int c, ref WindingStatistics statistics)
    {
        statistics.ExactPredicates++;
        return RobustOrientation.ExactSign(v[a], v[b], v[c]);
    }

    private static int Symbolic(Point2[] v, int a, int b, int c, ref WindingStatistics statistics)
    {
        statistics.SymbolicTieBreaks++;
        return RobustOrientation.SymbolicSign(v, a, b, c);
    }

    private static bool Collinear(Point2 a, Point2 b, Point2 c) =>
        !RobustOrientation.TryFilter(a, b, c, out _, out _) && RobustOrientation.ExactSign(a, b, c) == 0;

    // Sign of the dot product of b - a and c - a is only needed where both rays lie on one line: then the
    // coordinates along the dominant axis decide it exactly.
    private static double Dot(Point2 a, Point2 b, Point2 c) =>
        Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y) ? Math.Sign(b.X.CompareTo(a.X)) * Math.Sign(c.X.CompareTo(a.X))
            : Math.Sign(b.Y.CompareTo(a.Y)) * Math.Sign(c.Y.CompareTo(a.Y));

    // Collinear edges i (a->b) and j (c->d) overlap: split each at the other's endpoints that lie strictly inside
    // it, so that every shared boundary piece becomes the same segment on both edges. The split carries no
    // winding change.
    private static void MarkOverlap(Workspace ws, ref int count, int i, int i1, int j, int j1)
    {
        ws.Overlapping[i] = ws.Overlapping[j] = true;
        Point2[] v = ws.Vertices;
        Split(ws, ref count, i, v[i], v[i1], v[j]);
        Split(ws, ref count, i, v[i], v[i1], v[j1]);
        Split(ws, ref count, j, v[j], v[j1], v[i]);
        Split(ws, ref count, j, v[j], v[j1], v[i1]);
    }

    private static void Split(Workspace ws, ref int count, int edge, Point2 a, Point2 b, Point2 p)
    {
        // p lies on the line of a->b; it is strictly inside the segment when strictly between along the dominant axis.
        bool xDominant = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
        double lo = xDominant ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y), hi = xDominant ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
        double q = xDominant ? p.X : p.Y;
        if (!(q > lo && q < hi)) return;
        Position(p, false, false, a, b, out double t, out double u);
        if (count == ws.Found.Length) Array.Resize(ref ws.Found, ws.Found.Length * 2);
        ws.Found[count++] = new Crossing { Edge = edge, T = Clamp(t), U = Clamp(u), P = p, Delta = 0, SameLoop = true };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Sign(double value) => value > 0 ? 1 : -1;

    // Keyed primitive sort: Array.Sort with an IComparer allocates a delegate on every call.
    private static void SortLarge(Workspace ws, int lo, int length, Point2 direction)
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
        // Equal T near the edge end may still differ in U; order those runs with the full comparison.
        for (int x = lo + 1; x < lo + length; x++)
        {
            Crossing key = list[x]; int y = x - 1;
            while (y >= lo && After(list[y], key, direction)) { list[y + 1] = list[y]; y--; }
            list[y + 1] = key;
        }
    }

    // Order along an edge: by distance from the start, then by distance from the end (larger U comes first).
    // Crossings whose parameters both round to the same values on a very long edge are ordered by their points
    // along the edge's dominant axis; points on vertices and axis-parallel crossings are exact.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool After(Crossing x, Crossing y, Point2 direction)
    {
        if (x.T != y.T) return x.T > y.T;
        if (x.U != y.U) return x.U < y.U;
        return Math.Abs(direction.X) >= Math.Abs(direction.Y)
            ? (direction.X > 0 ? x.P.X > y.P.X : x.P.X < y.P.X)
            : (direction.Y > 0 ? x.P.Y > y.P.Y : x.P.Y < y.P.Y);
    }

    // Point at a position known from both ends, evaluated from the nearer end.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point2 At(Point2 a, Point2 b, double t, double u) =>
        t <= .5 ? new Point2(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)) : new Point2(b.X - u * (b.X - a.X), b.Y - u * (b.Y - a.Y));

    // Crossing position along i (a->b) and j (c->d). Which edges cross was decided exactly; positions only
    // need to be accurate, because swapping two crossings closer than the position error changes winding on
    // that short piece alone. Ill-conditioned floating-point ratios are therefore recomputed from accurate
    // determinants. For exactly collinear edges the symbolic perturbation is dominated by the lowest-index
    // endpoint P, whose normal offset vanishes only at the other endpoint of the edge containing P: in the
    // limit the perturbed edges cross exactly there, which keeps several overlapping edges consistent.
    private static void Parameters(Point2[] v, int i, int i1, int j, int j1, double oa, double ob, double errorI,
        double oc, double od, double errorJ, out double ti, out double ui, out double tj, out double uj, out Point2 point)
    {
        Point2 a = v[i], b = v[i1], c = v[j], d = v[j1];
        bool accurateI = oa != 0 && ob != 0 && (oa > 0) != (ob > 0) && errorI <= Accuracy * (Math.Abs(oa) + Math.Abs(ob));
        bool accurateJ = oc != 0 && od != 0 && (oc > 0) != (od > 0) && errorJ <= Accuracy * (Math.Abs(oc) + Math.Abs(od));
        bool collinear = false;
        int endI = 0, endJ = 0; // -1 or +1 when the crossing is exactly at the start or end vertex of that edge
        if (accurateI) { ti = oa / (oa - ob); ui = ob / (ob - oa); }
        else if (!RobustOrientation.CrossingParameter(c, d, a, b, out ti, out ui, out endI)) collinear = true;
        if (accurateJ) { tj = oc / (oc - od); uj = od / (od - oc); }
        else if (!RobustOrientation.CrossingParameter(a, b, c, d, out tj, out uj, out endJ)) collinear = true;
        int vertex = -1;
        if (collinear)
        {
            int p = Math.Min(Math.Min(i, i1), Math.Min(j, j1));
            vertex = p == i ? i1 : p == i1 ? i : p == j ? j1 : j;
            Position(v[vertex], vertex == i, vertex == i1, a, b, out ti, out ui);
            Position(v[vertex], vertex == j, vertex == j1, c, d, out tj, out uj);
        }
        // Every path above yields finite ratios: denominators are differences of distinct doubles or of
        // determinants with opposite signs. A NaN would be an internal error, never a position to guess.
        if (double.IsNaN(ti) || double.IsNaN(ui) || double.IsNaN(tj) || double.IsNaN(uj))
            throw new InvalidOperationException("Internal error: a crossing position could not be computed.");
        ti = Clamp(ti); ui = Clamp(ui); tj = Clamp(tj); uj = Clamp(uj);
        // One point for both incidences. A crossing at a vertex (exact zero determinant, or the collinear limit)
        // is that vertex exactly, so boundary pieces shared by both paths have identical endpoints. A parameter
        // that merely rounds to 0 or 1 on a long edge is not such a crossing.
        point = vertex >= 0 ? v[vertex]
            : endI < 0 ? a : endI > 0 ? b : endJ < 0 ? c : endJ > 0 ? d
            : Meet(a, b, ti, ui, c, d, tj, uj);
    }

    // A proper crossing point: each coordinate from the edge that spans less along that axis, whose rounding
    // error in the parameter moves that coordinate least. Axis-parallel edges then meet exactly.
    private static Point2 Meet(Point2 a, Point2 b, double ti, double ui, Point2 c, Point2 d, double tj, double uj)
    {
        Point2 onI = At(a, b, ti, ui), onJ = At(c, d, tj, uj);
        return new Point2(Math.Abs(b.X - a.X) <= Math.Abs(d.X - c.X) ? onI.X : onJ.X,
                          Math.Abs(b.Y - a.Y) <= Math.Abs(d.Y - c.Y) ? onI.Y : onJ.Y);
    }

    private static void Position(Point2 p, bool isStart, bool isEnd, Point2 a, Point2 b, out double t, out double u)
    {
        if (isStart) { t = 0; u = 1; }
        else if (isEnd) { t = 1; u = 0; }
        else { t = Project(p, a, b); u = Project(p, b, a); }
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

    // Neumaier summation for contributions of mixed signs.
    internal struct Sum
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
