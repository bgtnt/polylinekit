using System.Collections;

namespace PolylineKit.ActiveSweep;

internal static class Checks
{
    private static int checks, accepted, rejected, rational;

    internal static int Run()
    {
        var square = new Point2[] { new(0, 0), new(4, 0), new(4, 4), new(0, 4) };
        var fixtures = new List<Fixture>
        {
            new("square", square),
            new("vertical-subdivision", [new(0, 0), new(4, 0), new(4, 2), new(4, 4), new(0, 4), new(0, 2)]),
            new("closing-duplicates", [new(0, 0), new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0), new(0, 0)]),
            new("bowtie", [new(0, 0), new(4, 4), new(0, 4), new(4, 0)]),
            new("collinear-reversal", [new(0, 0), new(4, 0), new(2, 0), new(2, 3), new(0, 3)]),
            new("vertex-touch", [new(0, 0), new(4, 0), new(4, 4), new(2, 0), new(0, 4)]),
            new("repeated-loop", square.Concat(square).ToArray()),
            new("cancelled-loop", square.Concat(square.Reverse()).ToArray()),
            new("zero-area", [new(0, 0), new(1, 1), new(2, 2), new(3, 3)]),
            new("hole-with-bridge", [new(0, 0), new(8, 0), new(8, 8), new(0, 8), new(0, 0),
                new(2, 2), new(2, 6), new(6, 6), new(6, 2), new(2, 2)]),
            new("translated-triangle", [new(Math.ScaleB(1, 52), Math.ScaleB(1, 52)),
                new(Math.ScaleB(1, 52) + 1, Math.ScaleB(1, 52)), new(Math.ScaleB(1, 52), Math.ScaleB(1, 52) + 1)]),
            new("tiny-triangle", [new(0, 0), new(Math.ScaleB(1, -387), 0), new(0, Math.ScaleB(1, -387))]),
            new("subnormal-coordinate", [new(0, 0), new(Math.ScaleB(1, -1074), 0), new(0, Math.ScaleB(1, 300))]),
            new("translated-thin-strip", [new(0, 0), new(1e16, 1e16), new(1e16, 1e16 + 4), new(0, 4)]),
            new("narrow-notch", [new(0, 0), new(1, 0), new(1, 1), new(.5 + Math.ScaleB(1, -27), 1),
                new(.5 + Math.ScaleB(1, -27), .5), new(.5, .5), new(.5, 1), new(0, 1)])
        };
        foreach (var fixture in fixtures)
            for (int transform = 0; transform < 8; transform++)
            {
                var path = fixture.Path.Select(p => (transform & 1) != 0 ? new Point2(p.Y, p.X) : p).ToArray();
                if ((transform & 2) != 0) path = path.Reverse().ToArray();
                if ((transform & 4) != 0) path = path.Skip(1).Concat(path.Take(1)).ToArray();
                Check($"{fixture.Name}/{transform}", path, true);
            }
        foreach (int n in new[] { 4, 8, 16, 64, 256 })
        {
            Check($"diagonal/{n}", Fixtures.Bars(n, true), n <= 16);
            Check($"stacked/{n}", Fixtures.Bars(n, false), n <= 16);
            Check($"ring/{n}", Fixtures.Radial(n, false), n <= 8);
            Check($"star/{n}", Fixtures.Radial(n, true), n <= 8);
            Check($"simple-comb/{n}", Fixtures.Comb(n, false), n <= 16);
            Check($"simple-diagonal-comb/{n}", Fixtures.Comb(n, true), n <= 16);
        }

        // Exact integer coordinates stress the decision, not just approximate area agreement.
        // Arbitrary walks include contacts, coincident events, overlap and multiple loops.
        var random = new Random(0x5a17);
        for (int sample = 0; sample < 20_000; sample++)
        {
            int n = random.Next(3, 25);
            var path = Enumerable.Range(0, n).Select(_ => new Point2(random.Next(-8, 9), random.Next(-8, 9))).ToArray();
            if (Clean(path).Length < 3) continue;
            Check($"grid/{sample}", path, sample < 300);
        }
        for (int sample = 0; sample < 2_000; sample++)
        {
            int n = random.Next(3, 33);
            // Positive radii in angular order; some very skinny triangles near the origin.
            var path = Enumerable.Range(0, n).Select(i =>
            {
                double angle = i * (2 * Math.PI / n), r = 1 + random.NextDouble() * 9;
                double x = r * Math.Cos(angle), y = r * Math.Sin(angle);
                return new Point2(x * 1e6 + y, y / 1e6);
            }).ToArray();
            Check($"anisotropic/{sample}", path, sample < 30);
        }
        for (int sample = 0; sample < 200; sample++)
        {
            var path = Fixtures.Radial(12, false);
            double scale = Math.ScaleB(1, random.Next(-400, 200));
            path = path.Select(p => new Point2(p.X * scale, p.Y * scale)).ToArray();
            Check($"exponent/{sample}", path, sample < 10);
        }

        // Cache leasing remains correct when arbitrary user list indexers call us recursively.
        var nested = new NestedList(square, false);
        Equal(16, Program.Hybrid(nested), "nested call");
        bool threw = false;
        try { Program.Hybrid(new NestedList(square, true)); }
        catch (InvalidOperationException e) when (e.Message == "indexer sentinel") { threw = true; }
        Require(threw, "indexer exception must escape");
        Equal(16, Program.Hybrid(square), "call after nested exception");
        Require(SimpleSweep.TryArea(square, out _, out _), "ordinary square is accepted");
        Require(!SimpleSweep.TryArea(fixtures[3].Path, out _, out _), "bowtie must fall back");
        Equal(.5, Program.Hybrid(fixtures[10].Path), "translated triangle analytic area");
        Equal(Math.ScaleB(1, -775), Program.Hybrid(fixtures[11].Path), "tiny triangle analytic area");
        Equal(Math.ScaleB(1, -775), Program.Hybrid(fixtures[12].Path), "subnormal coordinate analytic area");
        KnownNumericalLimit(1e12);
        KnownNumericalLimit(1e16);
        Console.WriteLine($"Active sweep: {checks} checks; {accepted} certified paths; {rejected} fallback paths; {rational} exact rational area cases.");
        return 0;
    }

    private static void Check(string name, Point2[] path, bool exactArea)
    {
        bool certified = SimpleSweep.TryArea(path, out double area, out var statistics);
        bool simple = IsSimple(Clean(path));
        Require(certified == simple, $"{name}: sweep {certified}, independent all-pairs simplicity {simple}");
        if (certified) accepted++; else rejected++;
        var baseline = WindingArea.ClosedPath(path);
        double hybrid = certified ? area : baseline.NonZero;
        Equal(baseline.NonZero, hybrid, name + " engine nonzero");
        if (certified)
        {
            Equal(baseline.EvenOdd, area, name + " engine parity");
            Equal(baseline.Signed, statistics.SignedArea, name + " signed");
        }
        if (exactArea)
        {
            var exact = ExactAreaOracle.Measure(path);
            Equal(exact.NonZero, hybrid, name + " rational nonzero");
            Equal(exact.EvenOdd, certified ? area : baseline.EvenOdd, name + " rational parity");
            rational++;
        }
    }

    private static void KnownNumericalLimit(double length)
    {
        Point2[] path = [new(0, 0), new(length, length), new(2 * length, double.BitIncrement(2 * length))];
        Require(SimpleSweep.TryArea(path, out double area, out _), "skinny triangle must be simple");
        double baseline = WindingArea.ClosedPath(path).NonZero;
        double exact = ExactAreaOracle.Measure(path).NonZero;
        Equal(baseline, area, "skinny triangle retains baseline arithmetic");
        // This deliberately documents a limitation instead of treating agreement with
        // the baseline as independent evidence of numerical accuracy.
        Require(Math.Abs(area - exact) / exact < .11, "skinny triangle known relative-error bound");
        Console.WriteLine($"Known arithmetic limitation L={length:R}: simple={area:R}, engine={baseline:R}, exact={exact:R}");
    }

    // Independent exhaustive certificate check. No sweep status or event ordering is reused.
    private static bool IsSimple(Point2[] path)
    {
        int n = path.Length;
        if (n < 3) return false;
        for (int i = 0; i < n; i++)
        {
            Point2 a = path[i], b = path[(i + 1) % n], c = path[(i + 2) % n];
            if (RobustOrientation.ExactSign(a, b, c) == 0 && (On(a, b, c) || On(b, c, a))) return false;
            for (int j = i + 1; j < n; j++)
            {
                if (Same(path[i], path[j])) return false;
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                Point2 d = path[j], e = path[(j + 1) % n];
                int p = RobustOrientation.ExactSign(a, b, d), q = RobustOrientation.ExactSign(a, b, e),
                    r = RobustOrientation.ExactSign(d, e, a), s = RobustOrientation.ExactSign(d, e, b);
                if ((p == 0 && On(a, b, d)) || (q == 0 && On(a, b, e)) ||
                    (r == 0 && On(d, e, a)) || (s == 0 && On(d, e, b))) return false;
                if (p * q < 0 && r * s < 0) return false;
            }
        }
        return true;
    }

    private static Point2[] Clean(IEnumerable<Point2> path)
    {
        var result = new List<Point2>();
        foreach (Point2 p in path) if (result.Count == 0 || !Same(result[^1], p)) result.Add(p);
        if (result.Count > 1 && Same(result[0], result[^1])) result.RemoveAt(result.Count - 1);
        return result.ToArray();
    }
    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    private static bool On(Point2 a, Point2 b, Point2 p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    private static void Equal(double expected, double actual, string name) =>
        Require(double.IsFinite(actual) && Math.Abs(expected - actual) <= Math.Abs(expected) * 2e-11,
            $"{name}: {actual:R} != {expected:R}");
    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class NestedList(Point2[] points, bool throwing) : IReadOnlyList<Point2>
    {
        private bool called;
        public int Count => points.Length;
        public Point2 this[int index]
        {
            get
            {
                if (!called)
                {
                    called = true;
                    Equal(16, Program.Hybrid(points), "inner call");
                    if (throwing) throw new InvalidOperationException("indexer sentinel");
                }
                return points[index];
            }
        }
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => points.GetEnumerator();
    }
}
