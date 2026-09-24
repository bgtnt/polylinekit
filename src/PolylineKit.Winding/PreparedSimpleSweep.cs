using System.Runtime.CompilerServices;

namespace PolylineKit;

/// <summary>Certifies that an already validated, cleaned closed path is simple.</summary>
/// <remarks>
/// The owner must lease this instance with its WindingEngine workspace. Input vertices
/// are borrowed, never copied; a false result only asks the caller to use its general path.
/// Events follow lexicographic (x,y) order, equivalently x + epsilon*y, so vertical edges
/// need no floating-point shear. Every newly adjacent status pair is tested along its
/// complete segments before an undiscovered crossing could invalidate the status order.
/// Exact zero stays zero: symbolic perturbation must not hide touching or retracing.
///
/// Deterministic treap priorities are not a balancing guarantee. A traversal budget
/// bounded by O(n log n) aborts adversarial status work and requests the general path.
/// The vertex sort also takes O(n log n); seven retained arrays require 28 bytes per slot.
/// BigInteger work inside exact predicates depends on the input exponents.
/// </remarks>
internal sealed class PreparedSimpleSweep
{
    private Point2[] vertices = Array.Empty<Point2>();
    private int[] events = Array.Empty<int>(), start = Array.Empty<int>(), end = Array.Empty<int>();
    private int[] left = Array.Empty<int>(), right = Array.Empty<int>(), parent = Array.Empty<int>();
    private uint[] priority = Array.Empty<uint>();
    private int root, count, active, exactPredicates;
    private long remainingWork;
    private bool rejected;
#if NET10_0_OR_GREATER
    private readonly Comparison<int> eventComparison;

    internal PreparedSimpleSweep() => eventComparison = CompareEvent;
#endif

    /// <summary>
    /// True proves that only neighboring edges meet, at their common endpoint.
    /// False includes non-simple paths and exhaustion of the status traversal budget.
    /// </summary>
    internal bool TryCertify(Point2[] validatedCleanVertices, int vertexCount, ref WindingStatistics statistics)
    {
        if (vertexCount < 3) return false;
        vertices = validatedCleanVertices;
        count = vertexCount;
        exactPredicates = 0;
        rejected = false;
        root = -1;
        active = 0;
        try
        {
            Ensure(count);
            int levels = 1;
            for (int size = count; size > 1; size >>= 1) levels++;
            remainingWork = 32L * count * levels;
            for (int i = 0; i < count; i++)
            {
                events[i] = i;
                int j = i + 1 == count ? 0 : i + 1;
                if (ComparePoint(vertices[i], vertices[j]) < 0) { start[i] = i; end[i] = j; }
                else { start[i] = j; end[i] = i; }
                left[i] = right[i] = parent[i] = -1;
                priority[i] = Mix((uint)i);
            }
#if NET10_0_OR_GREATER
            // Unlike Array.Sort(IComparer), this uses the instance's cached delegate.
            events.AsSpan(0, count).Sort(eventComparison);
#else
            SortEvents();
#endif
            for (int i = 1; i < count; i++)
                if (Same(vertices[events[i - 1]], vertices[events[i]])) return false;

            for (int i = 0; i < count; i++)
            {
                int vertex = events[i];
                int before = vertex == 0 ? count - 1 : vertex - 1;
                // The two ending edges leave before either starting edge enters. Vertex
                // equality was excluded above, including nonadjacent repeated vertices.
                if (end[before] == vertex && !Remove(before)) return false;
                if (end[vertex] == vertex && !Remove(vertex)) return false;
                if (start[before] == vertex && !Insert(before)) return false;
                if (start[vertex] == vertex && !Insert(vertex)) return false;
            }
            return root == -1 && active == 0;
        }
        finally
        {
            statistics.ExactPredicates += exactPredicates;
            // Retaining an input reference could keep an older, subsequently grown
            // engine vertex buffer alive when this certificate is no longer selected.
            vertices = Array.Empty<Point2>();
        }
    }

    private int CompareEvent(int a, int b) => ComparePoint(vertices[a], vertices[b]);

    private void Ensure(int required)
    {
        if (events.Length >= required) return;
        int capacity = Math.Max(required, Math.Max(16, events.Length <= int.MaxValue / 2 ? events.Length * 2 : int.MaxValue));
        events = new int[capacity]; start = new int[capacity]; end = new int[capacity];
        left = new int[capacity]; right = new int[capacity]; parent = new int[capacity];
        priority = new uint[capacity];
    }

#if !NET10_0_OR_GREATER
    // netstandard2.0 has no Span.Sort. A local heap sort avoids the temporary
    // Comparison delegate allocated by Array.Sort with an instance IComparer.
    private void SortEvents()
    {
        for (int i = count / 2 - 1; i >= 0; i--) SiftDown(i, count);
        for (int length = count - 1; length > 0; length--)
        {
            int last = events[length]; events[length] = events[0]; events[0] = last;
            SiftDown(0, length);
        }
    }

    private void SiftDown(int node, int length)
    {
        int item = events[node];
        while (node < length / 2)
        {
            int child = node * 2 + 1;
            if (child + 1 < length && CompareEvent(events[child], events[child + 1]) < 0) child++;
            if (CompareEvent(item, events[child]) >= 0) break;
            events[node] = events[child];
            node = child;
        }
        events[node] = item;
    }
#endif

    private bool Insert(int edge)
    {
        int node = root, previous = -1, comparison = 0;
        while (node >= 0)
        {
            if (!TakeWork()) return false;
            previous = node;
            comparison = CompareStarting(edge, node);
            if (rejected) return false;
            node = comparison < 0 ? left[node] : right[node];
        }
        parent[edge] = previous;
        if (previous < 0) root = edge;
        else if (comparison < 0) left[previous] = edge;
        else right[previous] = edge;
        while (parent[edge] >= 0 && Higher(edge, parent[edge]))
        {
            if (!TakeWork()) return false;
            Promote(edge);
        }
        active++;
        int below = Predecessor(edge), above = Successor(edge);
        return !rejected && !Intersects(below, edge) && !Intersects(edge, above);
    }

    private bool Remove(int edge)
    {
        int below = Predecessor(edge), above = Successor(edge);
        if (rejected || Intersects(below, above)) return false;
        while (left[edge] >= 0 || right[edge] >= 0)
        {
            if (!TakeWork()) return false;
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

    // 'added' starts at the current vertex event; every active segment reaches
    // this event in symbolic sweep coordinates. Orientation orders them without division.
    private int CompareStarting(int added, int existing)
    {
        Point2 point = vertices[start[added]];
        Point2 a = vertices[start[existing]], b = vertices[end[existing]];
        int side = Orient(a, b, point);
        if (side != 0) return side;
        if (!Same(a, point)) { rejected = true; return 0; }
        side = Orient(a, b, vertices[end[added]]);
        if (side == 0) rejected = true; // Collinear outgoing rays retrace each other.
        return side;
    }

    private bool Intersects(int first, int second)
    {
        if (first < 0 || second < 0) return false;
        Point2 a = vertices[start[first]], b = vertices[end[first]];
        Point2 c = vertices[start[second]], d = vertices[end[second]];
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
            Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;

        bool adjacent = first + 1 == second || second + 1 == first ||
            (first == 0 && second == count - 1) || (second == 0 && first == count - 1);
        if (adjacent)
        {
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
        exactPredicates++;
        return RobustOrientation.ExactSign(a, b, c);
    }

    private int Predecessor(int node)
    {
        if (left[node] >= 0)
        {
            node = left[node];
            while (right[node] >= 0)
            {
                if (!TakeWork()) return -1;
                node = right[node];
            }
            return node;
        }
        int p = parent[node];
        while (p >= 0 && left[p] == node)
        {
            if (!TakeWork()) return -1;
            node = p; p = parent[node];
        }
        return p;
    }

    private int Successor(int node)
    {
        if (right[node] >= 0)
        {
            node = right[node];
            while (left[node] >= 0)
            {
                if (!TakeWork()) return -1;
                node = left[node];
            }
            return node;
        }
        int p = parent[node];
        while (p >= 0 && right[p] == node)
        {
            if (!TakeWork()) return -1;
            node = p; p = parent[node];
        }
        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TakeWork()
    {
        if (--remainingWork >= 0) return true;
        rejected = true;
        return false;
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
}
