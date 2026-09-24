using System.Runtime.CompilerServices;

namespace PolylineKit.ActiveSweep;

internal struct SweepStatistics
{
    public int Vertices;
    public long Comparisons;
    public long NeighborChecks;
    public long ExactPredicates;
    public int PeakActive;
    public double SignedArea;
}

/// <summary>
/// Experimental Shamos-Hoey first-intersection sweep. It certifies a single simple
/// closed path and then sums its edges; a rejected path must use the general engine.
/// </summary>
/// <remarks>
/// Events use lexicographic (x,y) order, equivalent to the symbolic sweep coordinate
/// x + epsilon*y. No floating-point shear or intersection coordinates are constructed.
/// Vertical edges are therefore ordinary edges in the symbolic sweep. The active
/// order is only changed at vertices: every newly neighboring pair is checked for an
/// intersection along its complete segments, so the sweep stops before its active
/// order could become invalid. Exact orientation handles all ordering predicates.
///
/// The array treap uses a deterministic permutation of edge IDs for reproducibility.
/// Tested inputs show balanced-tree behavior, but deterministic priorities supply
/// no probabilistic guarantee: adversarial vertex order can make the sweep O(n^2).
/// A balanced status tree would bound the combinatorial work to O(n log n). Space is O(n).
/// Consecutive duplicate samples and one or more repeated closing samples are cleaned;
/// repeated nonadjacent vertices, nonadjacent touching, retracing, and overlaps reject.
/// Straight collinear subdivision in the same direction is supported.
/// </remarks>
internal static class SimpleSweep
{
    [ThreadStatic] private static Workspace? cached;

    internal static bool TryArea(IReadOnlyList<Point2> path, out double area, out SweepStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(path);
        Workspace workspace = cached ?? new Workspace();
        cached = null;
        try
        {
            bool accepted = workspace.Run(path, out area);
            statistics = workspace.Statistics;
            return accepted;
        }
        finally
        {
            // A nested call cannot borrow the outer call's live arrays.
            if (cached is null || cached.Vertices.Length < workspace.Vertices.Length) cached = workspace;
        }
    }

    private sealed class Workspace : IComparer<int>
    {
        internal Point2[] Vertices = [];
        internal SweepStatistics Statistics;
        private int[] events = [], start = [], end = [], left = [], right = [], parent = [];
        private uint[] priority = [];
        private int root, count, active;
        private bool rejected;

        internal bool Run(IReadOnlyList<Point2> path, out double area)
        {
            area = 0;
            Statistics = default;
            rejected = false;
            root = -1;
            active = 0;
            int inputCount = path.Count;
            Ensure(inputCount);
            count = 0;
            for (int i = 0; i < inputCount; i++)
            {
                Point2 point = path[i];
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                    Math.Abs(point.X) > 1e100 || Math.Abs(point.Y) > 1e100)
                    throw new ArgumentException("Coordinates must be finite with magnitude at most 1e100.", nameof(path));
                if (count == 0 || !Same(Vertices[count - 1], point)) Vertices[count++] = point;
            }
            while (count > 1 && Same(Vertices[0], Vertices[count - 1])) count--;
            Statistics.Vertices = count;
            if (count < 3) return false;

            for (int i = 0; i < count; i++)
            {
                events[i] = i;
                int j = i + 1 == count ? 0 : i + 1;
                if (ComparePoint(Vertices[i], Vertices[j]) < 0) { start[i] = i; end[i] = j; }
                else { start[i] = j; end[i] = i; }
                left[i] = right[i] = parent[i] = -1;
                priority[i] = Mix((uint)i);
            }
            Array.Sort(events, 0, count, this);
            for (int i = 1; i < count; i++)
                if (Same(Vertices[events[i - 1]], Vertices[events[i]])) return false;

            for (int i = 0; i < count; i++)
            {
                int vertex = events[i];
                int before = vertex == 0 ? count - 1 : vertex - 1;
                // Remove both ending edges before inserting either starting edge at the same
                // polygon vertex. Distinct vertices never share a symbolic event coordinate.
                if (end[before] == vertex && !Remove(before)) return false;
                if (end[vertex] == vertex && !Remove(vertex)) return false;
                if (start[before] == vertex && !Insert(before)) return false;
                if (start[vertex] == vertex && !Insert(vertex)) return false;
            }
            if (root != -1 || active != 0) throw new InvalidOperationException("Unbalanced sweep events.");

            double minX = Vertices[0].X, maxX = minX, minY = Vertices[0].Y, maxY = minY;
            for (int i = 1; i < count; i++)
            {
                Point2 p = Vertices[i];
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            var origin = new Point2((minX + maxX) * .5, (minY + maxY) * .5);
            double sum = 0, compensation = 0;
            for (int i = 0; i < count; i++)
            {
                double value = Cross(Vertices[i], Vertices[i + 1 == count ? 0 : i + 1], origin);
                double next = sum + value;
                compensation += Math.Abs(sum) >= Math.Abs(value) ? (sum - next) + value : (value - next) + sum;
                sum = next;
            }
            Statistics.SignedArea = (sum + compensation) / 4;
            area = Math.Abs(Statistics.SignedArea);
            return true;
        }

        public int Compare(int a, int b) => ComparePoint(Vertices[a], Vertices[b]);

        private void Ensure(int required)
        {
            if (Vertices.Length >= required) return;
            int capacity = Math.Max(required, Math.Max(16, Vertices.Length * 2));
            Vertices = new Point2[capacity];
            events = new int[capacity]; start = new int[capacity]; end = new int[capacity];
            left = new int[capacity]; right = new int[capacity]; parent = new int[capacity];
            priority = new uint[capacity];
        }

        private bool Insert(int edge)
        {
            int node = root, previous = -1, comparison = 0;
            while (node >= 0)
            {
                previous = node;
                comparison = CompareStarting(edge, node);
                if (rejected) return false;
                node = comparison < 0 ? left[node] : right[node];
            }
            parent[edge] = previous;
            if (previous < 0) root = edge;
            else if (comparison < 0) left[previous] = edge;
            else right[previous] = edge;
            while (parent[edge] >= 0 && Higher(edge, parent[edge])) Promote(edge);
            active++;
            Statistics.PeakActive = Math.Max(Statistics.PeakActive, active);
            return !Intersects(Predecessor(edge), edge) && !Intersects(edge, Successor(edge));
        }

        private bool Remove(int edge)
        {
            int below = Predecessor(edge), above = Successor(edge);
            if (Intersects(below, above)) return false;
            while (left[edge] >= 0 || right[edge] >= 0)
            {
                int child = left[edge] < 0 ? right[edge] : right[edge] < 0 ? left[edge] :
                    Higher(left[edge], right[edge]) ? left[edge] : right[edge];
                Promote(child);
            }
            int p = parent[edge];
            if (p < 0) root = -1;
            else if (left[p] == edge) left[p] = -1;
            else right[p] = -1;
            parent[edge] = -1;
            active--;
            return true;
        }

        // Called only when 'added' starts at the current event. Every active segment
        // reaches that event in lexicographic sweep coordinates. Orientation therefore
        // orders its intersection with the symbolic sweep line without division.
        private int CompareStarting(int added, int existing)
        {
            Statistics.Comparisons++;
            Point2 point = Vertices[start[added]];
            Point2 a = Vertices[start[existing]], b = Vertices[end[existing]];
            int side = Orient(a, b, point);
            if (side != 0) return side;
            if (!Same(a, point)) { rejected = true; return 0; }
            side = Orient(a, b, Vertices[end[added]]);
            if (side == 0) rejected = true; // Two rays from one vertex overlap.
            return side;
        }

        private bool Intersects(int first, int second)
        {
            if (first < 0 || second < 0) return false;
            Statistics.NeighborChecks++;
            Point2 a = Vertices[start[first]], b = Vertices[end[first]];
            Point2 c = Vertices[start[second]], d = Vertices[end[second]];
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;

            bool adjacent = first + 1 == second || second + 1 == first ||
                (first == 0 && second == count - 1) || (second == 0 && first == count - 1);
            if (adjacent)
            {
                // Adjacent segments always share exactly one vertex after cleanup. They
                // may meet or continue collinearly, but must not turn back over one another.
                Point2 shared, otherFirst, otherSecond;
                if (Same(a, c)) { shared = a; otherFirst = b; otherSecond = d; }
                else if (Same(a, d)) { shared = a; otherFirst = b; otherSecond = c; }
                else if (Same(b, c)) { shared = b; otherFirst = a; otherSecond = d; }
                else { shared = b; otherFirst = a; otherSecond = c; }
                if (Orient(shared, otherFirst, otherSecond) != 0) return false;
                return Math.Sign(ComparePoint(otherFirst, shared)) == Math.Sign(ComparePoint(otherSecond, shared));
            }
            int cSide = Orient(a, b, c), dSide = Orient(a, b, d);
            if (cSide != 0 && cSide == dSide) return false;
            int aSide = Orient(c, d, a), bSide = Orient(c, d, b);
            return aSide == 0 || bSide == 0 || aSide != bSide;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Orient(Point2 a, Point2 b, Point2 c)
        {
            if (RobustOrientation.TryFilter(a, b, c, out double determinant, out _)) return determinant > 0 ? 1 : -1;
            Statistics.ExactPredicates++;
            return RobustOrientation.ExactSign(a, b, c);
        }

        private int Predecessor(int node)
        {
            if (left[node] >= 0)
            {
                node = left[node];
                while (right[node] >= 0) node = right[node];
                return node;
            }
            int p = parent[node];
            while (p >= 0 && left[p] == node) { node = p; p = parent[node]; }
            return p;
        }

        private int Successor(int node)
        {
            if (right[node] >= 0)
            {
                node = right[node];
                while (left[node] >= 0) node = left[node];
                return node;
            }
            int p = parent[node];
            while (p >= 0 && right[p] == node) { node = p; p = parent[node]; }
            return p;
        }

        private bool Higher(int a, int b) => priority[a] > priority[b] || (priority[a] == priority[b] && a > b);

        private void Promote(int child)
        {
            int p = parent[child], grand = parent[p];
            if (left[p] == child)
            {
                left[p] = right[child];
                if (left[p] >= 0) parent[left[p]] = p;
                right[child] = p;
            }
            else
            {
                right[p] = left[child];
                if (right[p] >= 0) parent[right[p]] = p;
                left[child] = p;
            }
            parent[p] = child;
            parent[child] = grand;
            if (grand < 0) root = child;
            else if (left[grand] == p) left[grand] = child;
            else right[grand] = child;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComparePoint(Point2 a, Point2 b)
    {
        int x = a.X.CompareTo(b.X);
        return x == 0 ? a.Y.CompareTo(b.Y) : x;
    }

    private static uint Mix(uint value)
    {
        value += 0x9e3779b9;
        value = (value ^ (value >> 16)) * 0x21f0aaad;
        value = (value ^ (value >> 15)) * 0x735a2d97;
        return value ^ (value >> 15);
    }

    // Same unhalved midpoint form and TwoDiff tails as WindingEngine.Cross.
    // Retains the reviewed large-offset triangle and subnormal-area behavior.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Cross(Point2 a, Point2 b, Point2 origin)
    {
        double ax = a.X - origin.X, bx = b.X - origin.X, ay = a.Y - origin.Y, by = b.Y - origin.Y;
        double dx = (ax + bx) + (Tail(a.X, origin.X, ax) + Tail(b.X, origin.X, bx));
        double dy = (ay + by) + (Tail(a.Y, origin.Y, ay) + Tail(b.Y, origin.Y, by));
        return dx * (b.Y - a.Y) - dy * (b.X - a.X);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Tail(double x, double y, double difference)
    {
        double yv = x - difference, xv = difference + yv;
        return (x - xv) + (yv - y);
    }
}
