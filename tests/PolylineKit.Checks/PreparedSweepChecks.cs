using System.Collections;
using PolylineKit;

namespace PolylineKit.Experiments;

internal static class PreparedSweepChecks
{
    private static int passed;

    internal static int Run()
    {
        passed = 0;
        AppContext.TryGetSwitch("PolylineKit.DisableSimpleSweep", out bool previous);
        try
        {
            var certificate = new PreparedSimpleSweep();
            var random = new Random(902_417);
            for (int trial = 0; trial < 10_000; trial++)
            {
                Point2[] input = Clean(Enumerable.Range(0, random.Next(3, 16))
                    .Select(_ => new Point2(random.Next(-5, 6), random.Next(-5, 6))));
                if (input.Length < 3) continue;
                var statistics = new WindingStatistics();
                bool actual = certificate.TryCertify(input, input.Length, ref statistics);
                True(actual == IsSimple(input, input.Length), "prepared certificate disagrees with exhaustive pairs");
            }
            CheckSmallGrid(certificate);
            CheckBudgetExhaustion(certificate);
            foreach (int n in new[] { 64, 256, 1024 })
            {
                foreach (bool diagonal in new[] { false, true })
                {
                    Point2[] path = Comb(n, diagonal);
                    for (int variant = 0; variant < 4; variant++)
                    {
                        Point2[] changed = path.Select(p => (variant & 1) == 0 ? p : new Point2(p.Y, p.X)).ToArray();
                        if ((variant & 2) != 0) Array.Reverse(changed);
                        Compare(changed, "comb");
                    }
                }
            }
            Point2[] simple = Comb(1024, true);
            var simpleRun = RunEngine(simple, false);
            True(simpleRun.Outcome == 2, "dense simple contour must use certificate");
            foreach (int exponent in new[] { -500, -54, 150, 280 })
            {
                double scale = Math.ScaleB(1, exponent);
                Point2[] scaled = simple.Select(p => new Point2(p.X * scale, p.Y * scale)).ToArray();
                Compare(scaled, "scaled certified contour " + exponent);
                True(RunEngine(scaled, false).Outcome == 2, "scaled contour exercises certification");
            }
            Point2[] translated = simple.Select(p => new Point2(p.X + Math.ScaleB(1, 52), p.Y + Math.ScaleB(1, 52))).ToArray();
            Compare(translated, "large-offset certified contour");
            True(RunEngine(translated, false).Outcome == 2, "large-offset contour exercises certification");
            True(RunEngine([new(0, 0), new(1, 1)], false).Outcome == 0, "collapsed bridged loop resets diagnostics");
            Point2[] crossing = (Point2[])simple.Clone();
            (crossing[^7], crossing[^5]) = (crossing[^5], crossing[^7]);
            Compare(crossing, "late crossing");
            True(RunEngine(crossing, false).Outcome == 1, "late crossing must resume after rejected certificate");
            Compare(simple.Concat(simple.Reverse()).ToArray(), "retracing");
            Compare(simple.Concat(simple).ToArray(), "double traversal");
            Compare(simple.SelectMany(p => new[] { p, p }).Append(simple[0]).ToArray(), "duplicates");

            // Each input index is read once, even after certificate rejection. A list
            // indexer may nest into the same API while the outer workspace is leased.
            double expected = RunEngine(crossing, true).Result.NonZero;
            AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", false);
            Near(expected, WindingArea.ClosedPath(new ReadOnceList(crossing, simple, false)).NonZero, "nested read-once fallback");
            bool threw = false;
            try { WindingArea.ClosedPath(new ReadOnceList(simple, crossing, true)); }
            catch (InvalidOperationException e) when (e.Message == "nested sentinel") { threw = true; }
            True(threw, "nested exception escapes");
            Compare(simple, "workspace reuse after throw");

            // Bridge construction also uses SingleLoop; separate filled regions do not.
            Point2[] first = simple.Take(simple.Length / 2).ToArray();
            Point2[] second = simple.Skip(simple.Length / 2).Reverse().ToArray();
            AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", true);
            var bridge = WindingArea.EndpointBridged(first, second);
            AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", false);
            Areas(bridge, WindingArea.EndpointBridged(first, second), "bridged contour");
            WindingArea.ClosedPath(simple);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) WindingArea.ClosedPath(simple);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            True(bytes == 0, "warm integrated certificate allocates");
        }
        finally { AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", previous); }
        Console.WriteLine($"Prepared sweep checks: {passed}");
        return passed;
    }

    private static void Compare(Point2[] path, string name) => Areas(RunEngine(path, true).Result, RunEngine(path, false).Result, name);

    private static (WindingAreaResult Result, int Outcome) RunEngine(Point2[] path, bool disabled)
    {
        AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", disabled);
        var ws = WindingEngine.Workspace.Rent();
        try
        {
            Point2[] v = ws.VertexBuffer(path.Length);
            int n = 0;
            foreach (Point2 p in path) WindingEngine.Append(v, ref n, 0, p, "path");
            WindingEngine.CloseLoop(v, ref n, 0);
            var result = WindingEngine.SingleLoop(ws, n);
            return (result, ws.SimpleSweepOutcome);
        }
        finally { WindingEngine.Workspace.Return(ws); }
    }

    private static void Areas(WindingAreaResult expected, WindingAreaResult actual, string name)
    {
        Near(expected.NonZero, actual.NonZero, name + " nonzero");
        Near(expected.EvenOdd, actual.EvenOdd, name + " evenodd");
        Near(expected.AbsoluteWinding, actual.AbsoluteWinding, name + " absolute");
        Near(expected.Signed, actual.Signed, name + " signed");
        True(expected.CrossingCount == actual.CrossingCount, name + " crossings");
    }

    private static Point2[] Comb(int n, bool diagonal) => Enumerable.Range(0, n)
        .Select(i => new Point2(i % 4 is 0 or 3 ? 0 : n * 2, i / 2))
        .Concat(new Point2[] { new(-1, n / 2 - 1), new(-1, 0) })
        .Select(p => diagonal ? new Point2(p.X, p.Y + p.X) : p).ToArray();

    private static Point2[] Clean(IEnumerable<Point2> input)
    {
        var points = new List<Point2>();
        foreach (Point2 p in input) if (points.Count == 0 || !Same(points[^1], p)) points.Add(p);
        if (points.Count > 1 && Same(points[0], points[^1])) points.RemoveAt(points.Count - 1);
        return points.ToArray();
    }

    private static void CheckSmallGrid(PreparedSimpleSweep certificate)
    {
        Point2[] grid = Enumerable.Range(0, 9).Select(i => new Point2(i / 3 - 1, i % 3 - 1)).ToArray();
        var path = new Point2[9];
        int checkedPaths = 0, simplePaths = 0;
        // Every cycle with 3..9 distinct grid vertices, modulo cyclic shifts.
        // Keep both directions: index-based priorities and incident-edge order differ.
        for (int first = 0; first < 7; first++)
        {
            path[0] = grid[first];
            Visit(first, 1, 1 << first);
        }
        True(checkedPaths == 125_628, "exhaustive grid enumeration is incomplete");
        True(simplePaths > 0 && simplePaths < checkedPaths, "grid oracle must cover both outcomes");
        Console.WriteLine($"Prepared sweep independent integer grid: {checkedPaths} cycles, {simplePaths} simple.");

        void Visit(int first, int length, int used)
        {
            if (length >= 3)
            {
                bool expected = IsSimple(path, length);
                var statistics = new WindingStatistics();
                bool actual = certificate.TryCertify(path, length, ref statistics);
                if (actual != expected)
                    throw new InvalidOperationException("prepared certificate disagrees with independent integer grid: " +
                        string.Join(";", path.Take(length).Select(p => $"({p.X},{p.Y})")));
                passed++;
                checkedPaths++;
                if (expected) simplePaths++;
            }
            for (int next = first + 1; next < grid.Length; next++)
            {
                if ((used & (1 << next)) != 0) continue;
                path[length] = grid[next];
                Visit(first, length + 1, used | (1 << next));
            }
        }
    }

    private static void CheckBudgetExhaustion(PreparedSimpleSweep certificate)
    {
        const int bands = 1024;
        var rank = new int[bands];
        int at = 0;
        // Order horizontal edge pairs by their deterministic treap priorities.
        // Left-side events build a deep status tree before right-side crossings
        // are encountered. This exhausts the real budget without a test override.
        foreach (int band in Enumerable.Range(0, bands).OrderByDescending(i =>
            Math.Max(Priority((uint)(4 * i)), Priority((uint)(4 * i + 2))))) rank[band] = at++;
        var path = new Point2[4 * bands];
        for (int band = 0; band < bands; band++)
        {
            int y = 2 * rank[band], edge = 4 * band;
            path[edge] = new(10, y); path[edge + 1] = new(0, y);
            path[edge + 2] = new(0, y + 1); path[edge + 3] = new(10, y + 1);
        }
        var statistics = new WindingStatistics();
        True(!certificate.TryCertify(path, path.Length, ref statistics), "adversarial status must reject");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        // Distinguish budget rejection from a geometric rejection, without adding
        // any runtime diagnostic or public switch solely for the test.
        long remaining = (long)typeof(PreparedSimpleSweep).GetField("remainingWork", flags)!.GetValue(certificate)!;
        True(remaining < 0, "adversarial status must exhaust the traversal budget");
        Point2[] borrowed = (Point2[])typeof(PreparedSimpleSweep).GetField("vertices", flags)!.GetValue(certificate)!;
        True(borrowed.Length == 0, "budget rejection must release the borrowed input");
        True(certificate.TryCertify([new(0, 0), new(2, 0), new(0, 2)], 3, ref statistics),
            "certificate must recover after partial-tree budget rejection");

        static uint Priority(uint value)
        {
            unchecked
            {
                value += 0x9e3779b9;
                value = (value ^ (value >> 16)) * 0x21f0aaad;
                value = (value ^ (value >> 15)) * 0x735a2d97;
                return value ^ (value >> 15);
            }
        }
    }

    // This oracle only receives the small integer grid inputs above. Products fit
    // in Int64; no production filter, exact predicate or symbolic tie-break is used.
    private static int GridOrientation(Point2 a, Point2 b, Point2 c) => Math.Sign(
        ((long)b.X - (long)a.X) * ((long)c.Y - (long)a.Y) -
        ((long)b.Y - (long)a.Y) * ((long)c.X - (long)a.X));

    private static bool IsSimple(Point2[] path, int n)
    {
        for (int i = 0; i < n; i++)
        {
            Point2 a = path[i], b = path[(i + 1) % n], c = path[(i + 2) % n];
            if (GridOrientation(a, b, c) == 0 && (On(a, b, c) || On(b, c, a))) return false;
            for (int j = i + 1; j < n; j++)
            {
                if (Same(a, path[j])) return false;
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                Point2 d = path[j], e = path[(j + 1) % n];
                int p = GridOrientation(a, b, d), q = GridOrientation(a, b, e),
                    r = GridOrientation(d, e, a), s = GridOrientation(d, e, b);
                if ((p == 0 && On(a, b, d)) || (q == 0 && On(a, b, e)) ||
                    (r == 0 && On(d, e, a)) || (s == 0 && On(d, e, b)) || (p * q < 0 && r * s < 0)) return false;
            }
        }
        return true;
    }
    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    private static bool On(Point2 a, Point2 b, Point2 p) => p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    private static void Near(double expected, double actual, string name) => True(double.IsFinite(actual) && Math.Abs(expected - actual) <= Math.Abs(expected) * 2e-11, $"{name}: {expected:R} != {actual:R}");
    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed++;
    }

    private sealed class ReadOnceList(Point2[] input, Point2[] nested, bool throwing) : IReadOnlyList<Point2>
    {
        private readonly bool[] seen = new bool[input.Length];
        public int Count => input.Length;
        public Point2 this[int index]
        {
            get
            {
                if (seen[index]) throw new InvalidOperationException("Input index read twice");
                seen[index] = true;
                if (index == 0)
                {
                    WindingArea.ClosedPath(nested);
                    if (throwing) throw new InvalidOperationException("nested sentinel");
                }
                return input[index];
            }
        }
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)input).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
