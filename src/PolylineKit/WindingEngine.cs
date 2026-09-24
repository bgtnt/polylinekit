using System.Runtime.CompilerServices;
#if NET10_0_OR_GREATER
using System.Numerics;
using System.Runtime.Intrinsics;
#endif

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
/// identical segments of a chain are netted by their integer coefficients and summed as segments. A sub-edge of
/// any other edge contributes its share of its edge's term, measured between its points along the edge's
/// dominant axis. Chains are summed around the loop's bounds center for one loop, and around the center of each
/// chain's own segments for two. A long shared boundary then cancels exactly instead of leaving rounding of its
/// large terms, and a small region keeps its area regardless of what else the input contains.
/// </remarks>
internal static class WindingEngine
{
    internal struct Crossing
    {
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
        internal double[] MergeKeys = Array.Empty<double>();
        internal int[] MergeOrder = Array.Empty<int>();
        internal readonly int[] Runs = new int[65];
        internal int[] Candidates = new int[256];
        // Structure-of-arrays bounds permit contiguous SIMD loads; payload stays 24 bytes per edge slot.
        internal double[] SweepMax = new double[256];
        internal double[] SweepMinOther = new double[256];
        internal double[] SweepMaxOther = new double[256];
        internal int[] Start = new int[257];
        internal bool[] Overlapping = new bool[256];
        internal Crossing[] Found = new Crossing[256];
        internal Crossing[] Sorted = new Crossing[256];
        internal double[] CrossingKeys = new double[256];
        internal int[] CrossingOrder = new int[256];
        internal Piece[] Shared = new Piece[64];
        internal int SharedCount;
        internal int[] Slots = new int[128];
        internal readonly double[] MinX = new double[MaxChains], MinY = new double[MaxChains], MaxX = new double[MaxChains], MaxY = new double[MaxChains];
        internal readonly bool[] Used = new bool[MaxChains];
        internal readonly Point2[] Origin = new Point2[MaxChains];
        internal readonly int[] Group = new int[MaxChains];
        internal readonly double[] EdgeTerms = new double[MaxChains];
        internal readonly Sum[] Sums = new Sum[MaxChains];
        internal PreparedSimpleSweep? SimpleSweep;
        // Internal experiment diagnostics: 0 bypassed, 1 rejected/budget exhausted, 2 certified.
        internal int SimpleSweepOutcome;

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
    /// and each edge needs one area term, shared by its sub-edges in proportion to their extent. Sub-edges of
    /// collinearly overlapping edges are netted first and summed as segments.
    /// </remarks>
    internal static WindingAreaResult SingleLoop(Workspace ws, int n)
    {
        var statistics = new WindingStatistics();
        if (n < 3)
        {
            ws.SimpleSweepOutcome = 0;
            return new WindingAreaResult(0, 0, 0, 0, statistics);
        }
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
            Point2 a = v[e], b = v[nx[e]], p0 = a;
            bool overlapping = ws.Overlapping[e], xDominant = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
            double term = overlapping ? 0 : Cross(a, b, origin);
            bool wholeEdge = offsets[e] == offsets[e + 1];
            for (int x = offsets[e]; x <= offsets[e + 1]; x++)
            {
                bool last = x == offsets[e + 1];
                Point2 p1 = last ? b : list[x].P;
                if (!PathInput.Same(p0, p1))
                {
                    if (overlapping) ws.Take(new Piece { P0 = p0, P1 = p1, W0 = NonZeroStep(w), W1 = EvenOddStep(w), W2 = AbsoluteStep(w), W3 = 1 }, true);
                    else
                    {
                        double cross = wholeEdge ? term : term * Fraction(a, b, p0, p1, xDominant);
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
        return new WindingAreaResult(nonZero.Value / 4, evenOdd.Value / 4, absolute.Value / 4, signed.Value / 4, statistics);
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
    // An ordinary sub-edge contributes its share of its edge's term, so a crossing point off the edge's line
    // (rounded) does not distort the area of the long edges around it.
    private static void WalkRegions(Workspace ws, int split, int n, int first, int w, int other, bool nonZero, bool isA, bool sum)
    {
        Point2[] v = ws.Vertices;
        int[] nx = ws.Next, offsets = ws.Start;
        Crossing[] list = ws.Sorted;
        int edges = isA ? split : n - split;
        int own = isA ? OwnA : OwnB, only = isA ? AOnly : BOnly, otherOnly = isA ? BOnly : AOnly;
        for (int step = 0, e = first; step < edges; step++, e = nx[e])
        {
            Point2 a = v[e], b = v[nx[e]], p0 = a;
            bool overlapping = ws.Overlapping[e], xDominant = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
            bool wholeEdge = offsets[e] == offsets[e + 1];
            int known = 0; // origin groups whose edge term is in ws.EdgeTerms
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
                        double fraction = wholeEdge ? 1 : Fraction(a, b, p0, p1, xDominant);
                        ws.Sums[own].Add(EdgeTerm(ws, own, a, b, ref known) * fraction * change);
                        if (inside)
                        {
                            ws.Sums[Both].Add(EdgeTerm(ws, Both, a, b, ref known) * fraction * change);
                            ws.Sums[otherOnly].Add(EdgeTerm(ws, otherOnly, a, b, ref known) * fraction * -change);
                        }
                        else ws.Sums[only].Add(EdgeTerm(ws, only, a, b, ref known) * fraction * change);
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

    // Nets identical collected segments by their coefficients in one pass over an open-addressing table, and
    // returns the number of remaining segments, stored at the start of ws.Shared in order of first occurrence.
    private static int Net(Workspace ws)
    {
        int count = ws.SharedCount;
        if (count == 0) return 0;
        int size = 128;
        while (size < 2 * count) size *= 2;
        if (ws.Slots.Length < size) ws.Slots = new int[size];
        int[] slots = ws.Slots; // index + 1 of a distinct segment, 0 when empty
        Array.Clear(slots, 0, size);
        Piece[] shared = ws.Shared;
        int distinct = 0, mask = size - 1;
        for (int x = 0; x < count; x++)
        {
            Piece piece = shared[x];
            for (int h = Hash(piece) & mask; ; h = (h + 1) & mask)
            {
                int slot = slots[h];
                if (slot == 0) { shared[distinct] = piece; slots[h] = ++distinct; break; } // distinct <= x
                ref Piece total = ref shared[slot - 1];
                if (!SameSegment(total, piece)) continue;
                total.W0 += piece.W0; total.W1 += piece.W1; total.W2 += piece.W2; total.W3 += piece.W3; total.W4 += piece.W4;
                break;
            }
        }
        int net = 0;
        for (int x = 0; x < distinct; x++)
            if (!shared[x].IsZero) shared[net++] = shared[x];
        return net;
    }

    private static int Hash(Piece piece)
    {
        ulong h = Bits(piece.P0.X);
        h = (h ^ Bits(piece.P0.Y)) * 0x9E3779B97F4A7C15UL;
        h = (h ^ Bits(piece.P1.X)) * 0x9E3779B97F4A7C15UL;
        h = (h ^ Bits(piece.P1.Y)) * 0x9E3779B97F4A7C15UL;
        return (int)(h >> 32);
    }

    // Equal coordinates must hash equally: 0 and -0 compare equal.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Bits(double value) => value == 0 ? 0UL : (ulong)BitConverter.DoubleToInt64Bits(value);

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

    private static double Area(Workspace ws, int chain) => ws.Sums[chain].Value / 4;

    // Edge term of a->b around the origin of the chain's group, computed once per edge and group.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double EdgeTerm(Workspace ws, int chain, Point2 a, Point2 b, ref int known)
    {
        int group = ws.Group[chain];
        if ((known & (1 << group)) == 0) { ws.EdgeTerms[group] = Cross(a, b, ws.Origin[group]); known |= 1 << group; }
        return ws.EdgeTerms[group];
    }

    // Share of edge a->b between its points p and q, which are ordered along its dominant axis: the sub-edge's
    // term is the edge's term times this share, exactly as for the segment of the edge's own line between the
    // positions of p and q. A whole edge has share 1.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Fraction(Point2 a, Point2 b, Point2 p, Point2 q, bool xDominant) =>
        xDominant ? (q.X - p.X) / (b.X - a.X) : (q.Y - p.Y) / (b.Y - a.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Less(Point2 p, Point2 q) => p.X < q.X || (p.X == q.X && p.Y < q.Y);

    private static bool SameSegment(Piece x, Piece y) => PathInput.Same(x.P0, y.P0) && PathInput.Same(x.P1, y.P1);

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

    // Twice cross(a - o, b - o), twice the signed area of triangle (o, a, b), as cross(d, b - a) with
    // d = (a - o) + (b - o) twice the offset of the midpoint from o. The products are |m - o| |b - a| instead of
    // |a - o| |b - o|, so a short segment far from the origin does not lose its contribution to cancellation.
    // d includes the rounding errors of a - o and b - o, so a long segment passing close to o keeps its small
    // offset, and nothing is halved, so tiny coordinates do not underflow. Swapping a and b negates the result
    // exactly. Sums of these terms are divided by 4.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Cross(Point2 a, Point2 b, Point2 o)
    {
        double ax = a.X - o.X, bx = b.X - o.X, ay = a.Y - o.Y, by = b.Y - o.Y;
        double dx = (ax + bx) + (Tail(a.X, o.X, ax) + Tail(b.X, o.X, bx));
        double dy = (ay + by) + (Tail(a.Y, o.Y, ay) + Tail(b.Y, o.Y, by));
        return dx * (b.Y - a.Y) - dy * (b.X - a.X);
    }

    // The rounding error of difference = x - y (Knuth's TwoDiff): x - y equals difference + Tail exactly.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Tail(double x, double y, double difference)
    {
        double yv = x - difference, xv = difference + yv;
        return (x - xv) + (yv - y);
    }

    // Monotone pieces are common in paths. Merge a bounded number of natural runs;
    // use the framework sort for highly fragmented input rather than paying for many merge passes.
    private static void SortEdges(Workspace ws, int n)
    {
        double[] keys = ws.Keys;
        int[] order = ws.Order, runs = ws.Runs;
        if (n < 256) { Array.Sort(keys, order, 0, n); return; }
        int count = 0, start = 0;
        while (start < n)
        {
            if (count == runs.Length - 1) { Array.Sort(keys, order, 0, n); return; }
            runs[count++] = start;
            int end = start + 1;
            if (end < n && keys[end] < keys[start])
            {
                while (end < n && keys[end] < keys[end - 1]) end++;
                Array.Reverse(keys, start, end - start);
                Array.Reverse(order, start, end - start);
            }
            else while (end < n && keys[end] >= keys[end - 1]) end++;
            start = end;
        }
        if (count < 2) return;
        runs[count] = n;
        if (ws.MergeKeys.Length < n) { ws.MergeKeys = new double[Grow(n)]; ws.MergeOrder = new int[ws.MergeKeys.Length]; }
        double[] outputKeys = ws.MergeKeys;
        int[] outputOrder = ws.MergeOrder;
        while (count > 1)
        {
            int outputCount = 0;
            for (int run = 0; run < count; run += 2)
            {
                int lo = runs[run], mid = runs[run + 1], hi = run + 1 < count ? runs[run + 2] : mid;
                runs[outputCount++] = lo;
                if (mid == hi || keys[mid - 1] <= keys[mid])
                {
                    Array.Copy(keys, lo, outputKeys, lo, hi - lo);
                    Array.Copy(order, lo, outputOrder, lo, hi - lo);
                    continue;
                }
                int left = lo, right = mid, dest = lo;
                while (left < mid && right < hi)
                {
                    int source = keys[left] <= keys[right] ? left++ : right++;
                    outputKeys[dest] = keys[source]; outputOrder[dest++] = order[source];
                }
                if (left < mid) { Array.Copy(keys, left, outputKeys, dest, mid - left); Array.Copy(order, left, outputOrder, dest, mid - left); }
                else if (right < hi) { Array.Copy(keys, right, outputKeys, dest, hi - right); Array.Copy(order, right, outputOrder, dest, hi - right); }
            }
            count = outputCount; runs[count] = n;
            double[] oldKeys = keys; keys = outputKeys; outputKeys = oldKeys;
            int[] oldOrder = order; order = outputOrder; outputOrder = oldOrder;
        }
        // Keep the sorted arrays as primary storage; the other buffers are scratch for the next call.
        ws.Keys = keys; ws.Order = order; ws.MergeKeys = outputKeys; ws.MergeOrder = outputOrder;
    }

    // Compact the broad-phase survivors before running predicates. Geometry sees the same pair order.
    private static int Candidates(Workspace ws, int from, int n, double max, double minOther, double maxOther
#if NET10_0_OR_GREATER
        , bool vectorize
#endif
    )
    {
        double[] keys = ws.Keys, lower = ws.SweepMinOther, upper = ws.SweepMaxOther;
        int[] output = ws.Candidates;
        int count = 0, b = from;
#if NET10_0_OR_GREATER
        if (vectorize && from <= n - 16 && keys[from + 15] <= max)
        {
            Vector256<double> minimum = Vector256.Create(minOther), maximum = Vector256.Create(maxOther);
            while (b <= n - 4 && keys[b + 3] <= max)
            {
                Vector256<double> lo = Vector256.LoadUnsafe(ref lower[0], (nuint)b);
                Vector256<double> hi = Vector256.LoadUnsafe(ref upper[0], (nuint)b);
                uint mask = Vector256.ExtractMostSignificantBits(Vector256.BitwiseAnd(
                    Vector256.LessThanOrEqual(lo, maximum), Vector256.GreaterThanOrEqual(hi, minimum)));
                while (mask != 0)
                {
                    output[count++] = b + BitOperations.TrailingZeroCount(mask);
                    mask &= mask - 1;
                }
                b += 4;
            }
        }
#endif
        for (; b < n && keys[b] <= max; b++)
            if (upper[b] >= minOther && lower[b] <= maxOther) output[count++] = b;
        return count;
    }

    // Records every symbolic proper crossing between nonadjacent edges, and every split where edges overlap
    // collinearly, and orders them along each edge. Returns the number of proper crossings.
    private static int FindCrossings(Workspace ws, int n, int split, ref WindingStatistics statistics)
    {
        ws.SimpleSweepOutcome = 0;
        // Attempt only after the current broad phase has demonstrated substantial work.
        // A crossing or overlap cancels eligibility; failed certification resumes this
        // very pass, without recopying input, rebuilding bounds or retesting prior pairs.
        bool canCertify = split == n && n >= 256 &&
            !(AppContext.TryGetSwitch("PolylineKit.DisableSimpleSweep", out bool disableSweep) && disableSweep);
        long examinedCandidates = 0;
        Point2[] v = ws.Vertices;
        int[] nx = ws.Next;
        if (ws.Keys.Length < n) { ws.Keys = new double[Grow(n)]; ws.Order = new int[ws.Keys.Length]; }
        if (ws.Start.Length < n + 1) ws.Start = new int[Grow(n + 1)];
        if (ws.Overlapping.Length < n) ws.Overlapping = new bool[Grow(n)];
        Array.Clear(ws.Overlapping, 0, n);
        double[] k = ws.Keys; int[] o = ws.Order;
        // A fixed X sweep visits quadratically many point intervals along tall, thin paths.
        // Use the wider input extent as a cheap heuristic; the worst case is still quadratic.
        double minX = v[0].X, maxX = minX, minY = v[0].Y, maxY = minY;
        for (int e = 1; e < n; e++)
        {
            Point2 p = v[e];
            if (p.X < minX) minX = p.X; else if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; else if (p.Y > maxY) maxY = p.Y;
        }
        bool sweepY = maxY - minY > maxX - minX;
        for (int e = 0; e < n; e++)
        {
            k[e] = sweepY ? Math.Min(v[e].Y, v[nx[e]].Y) : Math.Min(v[e].X, v[nx[e]].X);
            o[e] = e;
        }
        SortEdges(ws, n);
        k = ws.Keys; o = ws.Order;
        if (ws.SweepMax.Length < n)
        {
            int capacity = Grow(n);
            ws.SweepMax = new double[capacity];
            ws.SweepMinOther = new double[capacity];
            ws.SweepMaxOther = new double[capacity];
        }
        double[] sweepMax = ws.SweepMax, sweepMinOther = ws.SweepMinOther, sweepMaxOther = ws.SweepMaxOther;
        for (int e = 0; e < n; e++)
        {
            int edge = o[e];
            Point2 a = v[edge], b = v[nx[edge]];
            sweepMax[e] = sweepY ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
            sweepMinOther[e] = sweepY ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
            sweepMaxOther[e] = sweepY ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
        }

#if NET10_0_OR_GREATER
        bool vectorize = n >= 64 && Vector256.IsHardwareAccelerated &&
            !(AppContext.TryGetSwitch("PolylineKit.DisableSimd", out bool disabled) && disabled);
#endif
        if (ws.Candidates.Length < n) ws.Candidates = new int[Grow(n)];
        int[] candidates = ws.Candidates;
        int count = 0, crossings = 0;
        for (int a = 0; a < n; a++)
        {
            int i = o[a], i1 = nx[i];
            Point2 pa = v[i], pb = v[i1];
            double max = sweepMax[a], minOther = sweepMinOther[a], maxOther = sweepMaxOther[a];
            int candidateCount = Candidates(ws, a + 1, n, max, minOther, maxOther
#if NET10_0_OR_GREATER
                , vectorize
#endif
            );
            for (int candidate = 0; candidate < candidateCount; candidate++)
            {
                int b = candidates[candidate];
                int j = o[b], j1 = nx[j];
                Point2 pc = v[j], pd = v[j1];
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
                Point2 point = Point(v, i, i1, j, j1, oa, ob, ea + eb, oc, od, ec + ed);
                if (count + 2 > ws.Found.Length) Array.Resize(ref ws.Found, ws.Found.Length * 2);
                bool same = (i < split) == (j < split);
                // Moving along i across j changes winding by sign(cross(dir j, dir i)) = sc.
                ws.Found[count++] = new Crossing { Edge = i, P = point, Delta = sc, SameLoop = same };
                ws.Found[count++] = new Crossing { Edge = j, P = point, Delta = -sc, SameLoop = same };
                crossings++;
            }
            if (canCertify)
            {
                // Every overlap found in this row marks its first edge, even when no
                // endpoint lies strictly inside it and MarkOverlap emits no split.
                if (count != 0 || ws.Overlapping[i]) canCertify = false;
                else if ((examinedCandidates += candidateCount) >= 8L * n)
                {
                    canCertify = false; // At most one attempt, including budget exhaustion.
                    ws.SimpleSweepOutcome = 1;
                    ws.SimpleSweep ??= new PreparedSimpleSweep();
                    if (ws.SimpleSweep.TryCertify(v, n, ref statistics))
                    {
                        ws.SimpleSweepOutcome = 2;
                        Array.Clear(ws.Start, 0, n + 1);
                        return 0; // Keep the existing four-chain area accumulation unchanged.
                    }
                }
            }
        }

        int[] s = ws.Start;
        Crossing[] found = ws.Found;
        Array.Clear(s, 0, n + 1);
        for (int c = 0; c < count; c++) s[found[c].Edge + 1]++;
        for (int e = 0; e < n; e++) s[e + 1] += s[e];
        if (ws.Sorted.Length < count) ws.Sorted = new Crossing[Grow(count)];
        if (ws.CrossingKeys.Length < count) { ws.CrossingKeys = new double[Grow(count)]; ws.CrossingOrder = new int[ws.CrossingKeys.Length]; }
        Crossing[] sorted = ws.Sorted;
        double[] keys = ws.CrossingKeys;
        int[] items = ws.CrossingOrder;
        for (int e = 0; e < n; e++) o[e] = s[e];
        for (int c = 0; c < count; c++) items[o[found[c].Edge]++] = c;
        // Events along an edge are ordered by their shared points, the same points the boundary pieces connect,
        // so no piece runs backwards along its edge. Parameters are not used: on a long edge two nearby crossings
        // can have parameters in one order and accurately computed points in the other. Keyed primitive sorts
        // (a comparer would allocate a delegate) give O(r log r) per edge, and each record is copied once.
        for (int e = 0; e < n; e++)
        {
            int lo = s[e], hi = s[e + 1];
            if (hi - lo < 2) continue;
            Point2 a = v[e], b = v[nx[e]];
            bool xDominant = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
            double major = (xDominant ? b.X > a.X : b.Y > a.Y) ? 1 : -1, minor = (xDominant ? b.Y < a.Y : b.X < a.X) ? -1 : 1;
            for (int x = lo; x < hi; x++) { Point2 q = found[items[x]].P; keys[x] = major * (xDominant ? q.X : q.Y); }
            Array.Sort(keys, items, lo, hi - lo);
            // Equal dominant coordinates: rounded points of nearly coincident crossings, ordered by the other
            // coordinate, or one shared point, whose events may come in any order because the piece between
            // them is empty.
            for (int x = lo; x < hi - 1;)
            {
                int y = x + 1;
                while (y < hi && keys[y] == keys[x]) y++;
                if (y - x > 1)
                {
                    for (int z = x; z < y; z++) { Point2 q = found[items[z]].P; keys[z] = minor * (xDominant ? q.Y : q.X); }
                    Array.Sort(keys, items, x, y - x);
                }
                x = y;
            }
        }
        for (int x = 0; x < count; x++) sorted[x] = found[items[x]];
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
        if (count == ws.Found.Length) Array.Resize(ref ws.Found, ws.Found.Length * 2);
        ws.Found[count++] = new Crossing { Edge = edge, P = p, Delta = 0, SameLoop = true };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Sign(double value) => value > 0 ? 1 : -1;

    // Point at a position known from both ends, evaluated from the nearer end.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point2 At(Point2 a, Point2 b, double t, double u) =>
        t <= .5 ? new Point2(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)) : new Point2(b.X - u * (b.X - a.X), b.Y - u * (b.Y - a.Y));

    // The point where i (a->b) and j (c->d) cross, from positions along both edges. Which edges cross was decided
    // exactly; the point only needs to be accurate, because swapping two crossings closer than its error changes
    // winding on that short piece alone. Ill-conditioned floating-point ratios are therefore recomputed from accurate
    // determinants. For exactly collinear edges the symbolic perturbation is dominated by the lowest-index
    // endpoint P, whose normal offset vanishes only at the other endpoint of the edge containing P: in the
    // limit the perturbed edges cross exactly there, which keeps several overlapping edges consistent.
    private static Point2 Point(Point2[] v, int i, int i1, int j, int j1, double oa, double ob, double errorI,
        double oc, double od, double errorJ)
    {
        Point2 a = v[i], b = v[i1], c = v[j], d = v[j1];
        bool accurateI = oa != 0 && ob != 0 && (oa > 0) != (ob > 0) && errorI <= Accuracy * (Math.Abs(oa) + Math.Abs(ob));
        bool accurateJ = oc != 0 && od != 0 && (oc > 0) != (od > 0) && errorJ <= Accuracy * (Math.Abs(oc) + Math.Abs(od));
        bool collinear = false;
        int endI = 0, endJ = 0; // -1 or +1 when the crossing is exactly at the start or end vertex of that edge
        double ti, ui, tj, uj;
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
        return vertex >= 0 ? v[vertex]
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
