using PolylineKit;
using PolylineKit.ActiveSweep;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Topology controls against analytic fills and an independent exact-rational slab oracle.</summary>
internal static class Checks
{
    private static int passed;

    internal static void Run()
    {
        passed = 0;
        var engine = new IntegerScanbeam();
        Point2[] square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];
        Point2[] outer = [.. square, square[0]];
        Point2[] inner = [new(1, 1), new(3, 1), new(3, 3), new(1, 3), new(1, 1)];
        Point2[] shifted = square.Select(p => new Point2(p.X + 2, p.Y)).ToArray();
        (string Name, Point2[] Path, double NonZero, double EvenOdd)[] analytic =
        [
            ("square", square, 16, 16),
            ("square twice", [.. outer, .. outer], 16, 0),
            ("square three times", [.. outer, .. outer, .. outer], 16, 16),
            ("forward then backward", [.. outer, .. outer.Reverse()], 0, 0),
            ("bowtie", [new(0, 0), new(4, 4), new(4, 0), new(0, 4)], 8, 8),
            ("same-direction nested loops", [.. outer, .. inner], 16, 12),
            ("hole and retraced bridge", [.. outer, .. inner.Reverse()], 12, 12),
            ("overlapping loops", [.. outer, .. shifted, shifted[0]], 24, 16),
            ("shared vertical edge", [.. outer, .. outer.Select(p => new Point2(p.X + 4, p.Y))], 32, 32),
            ("point contact", [.. outer, .. outer.Select(p => new Point2(p.X + 4, p.Y + 4))], 32, 32),
            ("duplicates", [new(0, 0), new(0, 0), new(4, 0), new(4, 4), new(4, 4), new(0, 4), new(0, 0), new(0, 0)], 16, 16),
            ("all coincident", [new(7, 7), new(7, 7), new(7, 7)], 0, 0),
            ("vertical collinear", [new(1, -4), new(1, 6), new(1, 0)], 0, 0),
            ("horizontal collinear", [new(-4, 1), new(6, 1), new(0, 1)], 0, 0),
            ("oblique collinear", [new(-4, -4), new(6, 6), new(0, 0)], 0, 0),
            ("two positions", [new(0, 0), new(8, 2), new(0, 0)], 0, 0),
            ("legacy domain square", [new(-32768, -32768), new(32768, -32768), new(32768, 32768), new(-32768, 32768)], 4294967296, 4294967296),
            ("legacy unit boundary square", [new(32767, 32767), new(32768, 32767), new(32768, 32768), new(32767, 32768)], 1, 1),
            ("inclusive expanded domain square", [new(-524288, -524288), new(524288, -524288), new(524288, 524288), new(-524288, 524288)], 1099511627776, 1099511627776),
            ("expanded unit boundary square", [new(524287, 524287), new(524288, 524287), new(524288, 524288), new(524287, 524288)], 1, 1)
        ];
        foreach (var item in analytic)
        {
            var oracle = ExactAreaOracle.Measure(item.Path);
            Equal(item.Name + " independent oracle NonZero", item.NonZero, oracle.NonZero);
            Equal(item.Name + " independent oracle EvenOdd", item.EvenOdd, oracle.EvenOdd);
            foreach (Point2[] variant in Variants(item.Path))
            {
                Equal(item.Name + " NonZero", item.NonZero, engine.Measure(variant, PathFillRule.NonZero));
                Equal(item.Name + " EvenOdd", item.EvenOdd, engine.Measure(variant, PathFillRule.EvenOdd));
            }
        }

        Point2[][] events =
        [
            // Four directions meet at a multiway crossing; tie order must not manufacture width.
            [new(-6, -6), new(6, 6), new(-6, 6), new(6, -6), new(0, -8), new(0, 8), new(-8, 0), new(8, 0)],
            // Endpoint on a nonadjacent edge, and horizontal overlap split at an interior endpoint.
            [new(0, 0), new(8, 0), new(8, 8), new(4, 0), new(0, 8)],
            [new(0, 0), new(4, 0), new(4, 2), new(2, 2), new(2, 0), new(6, 0), new(6, 4), new(0, 4)],
            // Two separate bowties cross at (-10,0) and (10,0): equal event Y must not
            // combine spatially unrelated crossing groups. The inter-loop bridge retraces.
            [new(-14, -4), new(-6, 4), new(-6, -4), new(-14, 4), new(-14, -4),
                new(6, -4), new(14, 4), new(14, -4), new(6, 4), new(6, -4)],
            // An oblique support line is traversed twice forward and once backward. Its
            // entire coincident cohort crosses the opposite slope at the interior point (0,0).
            [new(-6, -6), new(6, 6), new(-6, 6), new(-6, -6), new(6, 6), new(-6, -6),
                new(6, -6), new(-6, 6), new(-6, -6)],
            // Nearly parallel diagonals at the maximum permitted extent exercise exact event order.
            [new(-32768, -32768), new(32768, 32767), new(-32768, -32767), new(32767, 32768)],
            [new(-32768, 0), new(32768, 1), new(-32767, 1), new(32767, 0)]
        ];
        for (int i = 0; i < events.Length; i++) OracleVariants("event control " + i, events[i]);
        for (int seed = 0; seed < 10; seed++)
        {
            var random = new Random(950_201 + seed * 17);
            Point2[] boundary = Enumerable.Range(0, 8).Select(i => new Point2(
                i % 3 == 0 ? (seed % 2 == 0 ? -32768 : 32768) : random.Next(-32768, 32769),
                i % 4 == 0 ? (seed % 2 == 0 ? 32768 : -32768) : random.Next(-32768, 32769))).ToArray();
            OracleVariants("full-domain n=8 seed=" + seed, boundary);
        }
        for (int seed = 0; seed < 5; seed++)
        {
            var random = new Random(461_871 + seed * 31);
            Point2[] boundary = Enumerable.Range(0, 8).Select(i => new Point2(
                i % 3 == 0 ? -524288 : random.Next(-524288, 524289),
                i % 4 == 0 ? 524288 : random.Next(-524288, 524289))).ToArray();
            OracleVariants("expanded-domain n=8 seed=" + seed, boundary);
        }
        // The optional Int64 arithmetic dispatch changes at max |coordinate| = 2048.
        // Crossings use non-dyadic event positions, with extents on both sides of that boundary.
        foreach (int extent in new[] { 2047, 2048, 2049 })
            OracleVariants("arithmetic dispatch crossings extent=" + extent,
            [new(-extent, -extent), new(extent, extent - 1), new(1 - extent, extent), new(extent, -extent),
                new(0, extent), new(0, -extent), new(-extent, 1), new(extent, -1)]);
        Point2[] narrowDispatch = [new(0, 0), new(2047, 2046), new(2048, 2047)];
        Point2[] wideDispatch = [new(0, 0), new(2048, 2047), new(2049, 2048)];
        foreach (var (name, triangle) in new[]
        {
            ("at dispatch boundary", narrowDispatch), ("above dispatch boundary", wideDispatch),
            ("translated across dispatch boundary", narrowDispatch.Select(p => new Point2(p.X + 1, p.Y + 1)).ToArray())
        })
        {
            var exact = ExactAreaOracle.Measure(triangle);
            Equal(name + " determinant-one NonZero", .5, exact.NonZero);
            Equal(name + " determinant-one EvenOdd", .5, exact.EvenOdd);
            OracleVariants(name, triangle);
        }
        Point2[] maximumDispatch = [new(-32768, -32768), new(32768, 32767), new(-32768, 32767), new(32767, -32768)];
        var maximumExact = ExactAreaOracle.Measure(maximumDispatch);
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            double small = engine.Measure(narrowDispatch, rule), wide = engine.Measure(wideDispatch, rule);
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Equal("small dispatch after wide call", small, engine.Measure(narrowDispatch, rule));
                Near("maximum dispatch after small call", rule == PathFillRule.NonZero ? maximumExact.NonZero : maximumExact.EvenOdd,
                    engine.Measure(maximumDispatch, rule));
                Equal("small dispatch after maximum call", small, engine.Measure(narrowDispatch, rule));
                Equal("wide dispatch after small call", wide, engine.Measure(wideDispatch, rule));
            }
        }
        // Consecutive large integer vectors have determinant one, so each area is exactly 1/2.
        // The slab implementation still performs final integration in binary64, under the protocol budget.
        foreach (Point2[] thin in new Point2[][]
        {
            [new(0, 0), new(32767, 32766), new(32768, 32767)],
            [new(-32768, -32768), new(-1, -2), new(0, -1)],
            [new(0, 0), new(524287, 524286), new(524288, 524287)],
            [new(-524288, -524288), new(-1, -2), new(0, -1)]
        })
        {
            var exact = ExactAreaOracle.Measure(thin);
            Equal("unit determinant oracle NonZero", .5, exact.NonZero);
            Equal("unit determinant oracle EvenOdd", .5, exact.EvenOdd);
            foreach (Point2[] variant in Variants(thin))
            {
                Near("unit determinant thin triangle NonZero", .5, engine.Measure(variant, PathFillRule.NonZero));
                Near("unit determinant thin triangle EvenOdd", .5, engine.Measure(variant, PathFillRule.EvenOdd));
            }
        }

        // Small integer grids deliberately produce shared points, retracing and coincident edges.
        // The oracle enumerates every pair and uses BigInteger rationals; it shares no event scheduler.
        foreach (int n in new[] { 8, 16 })
        for (int seed = 0; seed < 200; seed++)
        {
            var random = new Random(151_027 + seed * 31 + n);
            Point2[] path = Enumerable.Range(0, n).Select(_ => new Point2(random.Next(-5, 6), random.Next(-5, 6))).ToArray();
            if (seed % 11 == 0) path[2] = path[1];
            OracleVariants($"grid n={n} seed={seed}", path);
            // A far translation stays in the integer domain and leaves the exact area unchanged.
            // This catches loss from subtracting almost equal absolute-coordinate heights.
            if (seed % 20 == 0)
            {
                var exact = ExactAreaOracle.Measure(path);
                Point2[] translated = path.Select(p => new Point2(p.X + 32760, p.Y - 32760)).ToArray();
                Near("translated grid NonZero", exact.NonZero, engine.Measure(translated, PathFillRule.NonZero));
                Near("translated grid EvenOdd", exact.EvenOdd, engine.Measure(translated, PathFillRule.EvenOdd));
            }
        }

        CheckContract(engine, square);
        CheckIntersection(engine, square);
        Console.WriteLine($"Integer scanbeam controls: {passed} assertions, including 400 small-grid, 15 boundary-domain walk oracles and 30 independent triangle-pair oracles.");

        void OracleVariants(string name, Point2[] path)
        {
            var exact = ExactAreaOracle.Measure(path);
            foreach (Point2[] variant in Variants(path))
            {
                Near(name + " NonZero", exact.NonZero, engine.Measure(variant, PathFillRule.NonZero));
                Near(name + " EvenOdd", exact.EvenOdd, engine.Measure(variant, PathFillRule.EvenOdd));
            }
        }
    }

    private static IEnumerable<Point2[]> Variants(Point2[] path)
    {
        yield return path;
        yield return path.Reverse().ToArray();
        yield return path.Select(p => new Point2(p.Y, p.X)).ToArray();
        yield return Enumerable.Range(0, path.Length).Select(i => path[(i + 1) % path.Length]).ToArray();
    }

    private static void CheckContract(IntegerScanbeam engine, Point2[] square)
    {
        Reject<ArgumentNullException>(() => engine.Measure(null!));
        foreach (Point2[] bad in new Point2[][] { [], [new(0, 0)], [new(0, 0), new(1, 1)], new Point2[8193] })
            Reject<ArgumentException>(() => engine.Measure(bad));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            524289, -524289, .5, double.Epsilon })
        foreach (bool y in new[] { false, true })
        {
            Point2 point = y ? new(0, bad) : new(bad, 0);
            Reject<ArgumentException>(() => engine.Measure([new(0, 0), new(1, 0), point]));
        }
        foreach (int bad in new[] { -1, 2, int.MaxValue })
            Reject<ArgumentOutOfRangeException>(() => engine.Measure(square, (PathFillRule)bad));
        Equal("maximum supplied count degenerate", 0, engine.Measure(new Point2[8192]));
        Equal("valid call following rejected input", 16, engine.Measure(square));
        Point2[] source = [new(-0.0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, -0.0)];
        long[] Bits() => source.SelectMany(p => new[] { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
        long[] before = Bits();
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            double first = engine.Measure(source, rule);
            engine.Measure([new(-4, -4), new(-2, 2), new(2, -2)]);
            Equal("repeated call after different geometry", first, engine.Measure(source, rule));
        }
        if (!before.SequenceEqual(Bits())) throw new InvalidOperationException("Scanbeam mutated caller input.");
        passed++;
    }

    private static void CheckIntersection(IntegerScanbeam engine, Point2[] square)
    {
        Point2[] closed = [.. square, square[0]];
        Point2[] small = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        Point2[] smallClosed = [.. small, small[0]];
        Point2[] bowtie = [new(0, 0), new(4, 4), new(4, 0), new(0, 4)];
        Point2[] full = [new(-524288, -524288), new(524288, -524288), new(524288, 524288), new(-524288, 524288)];
        (string Name, Point2[] A, Point2[] B, double NonZero, double EvenOdd)[] analytic =
        [
            ("identity", square, square, 16, 16),
            ("nested", square, small, 4, 4),
            ("proper overlap", square, Shift(square, 2, 1), 6, 6),
            ("separate independently closed loops", square, Shift(square, 9, 11), 0, 0),
            ("shared edge", square, Shift(square, 4, 0), 0, 0),
            ("shared endpoint", square, Shift(square, 4, 4), 0, 0),
            ("repeated first loop", [.. closed, .. closed], square, 16, 0),
            ("repeated both loops", [.. closed, .. closed], [.. closed, .. closed], 16, 0),
            ("opposite retrace", [.. closed, .. closed.Reverse()], square, 0, 0),
            ("nested duplicate winding", [.. closed, .. smallClosed], small, 4, 0),
            ("hole empty intersection", [.. closed, .. smallClosed.Reverse()], small, 0, 0),
            ("bowtie contained", bowtie, square, 8, 8),
            ("bowtie self", bowtie, bowtie, 8, 8),
            ("bowtie partial", bowtie, [new(0, 0), new(2, 0), new(2, 4), new(0, 4)], 4, 4),
            ("coincident points", [new(1, 1), new(1, 1), new(1, 1)], square, 0, 0),
            ("collinear retrace", [new(-4, 2), new(8, 2), new(2, 2)], square, 0, 0),
            ("duplicates and explicit closure", [new(0, 0), new(4, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)], closed, 16, 16),
            ("fractional intersection vertices", [new(0, 0), new(1, 0), new(0, 1)], [new(0, 0), new(1, 1), new(1, 0)], .25, .25),
            ("expanded-domain identity", full, full, 1099511627776, 1099511627776),
            ("expanded-domain thin strips", [new(-524288, -1), new(524288, -1), new(524288, 1), new(-524288, 1)],
                [new(0, -524288), new(1, -524288), new(1, 524288), new(0, 524288)], 2, 2),
            ("distant unit intersection", Shift(square, 524283, 524283),
                [new(524286, 524286), new(524288, 524286), new(524288, 524288), new(524286, 524288)], 1, 1),
            ("expanded-domain determinant one", [new(0, 0), new(524287, 524286), new(524288, 524287)], full, .5, .5)
        ];
        foreach (var item in analytic) PairVariants(item.Name, item.A, item.B, item.NonZero, item.EvenOdd);

        // All triangles are nondegenerate and canonically counterclockwise. Each winds only
        // zero or one times; hence the joined-path NonZero oracle is their geometric union.
        // The connector is traversed in both directions. This identity is deliberately NOT
        // used for arbitrary signed/self-intersecting paths, where winding cancellation fails.
        for (int seed = 0; seed < 30; seed++)
        {
            var random = new Random(761_071 + 31 * seed);
            Point2[] a = Triangle(random), b = Triangle(random);
            Point2[] joined = [.. a, a[0], .. b, b[0]];
            double union = ExactAreaOracle.Measure(joined).NonZero;
            double expected = TriangleArea(a) + TriangleArea(b) - union;
            if (expected < 0 || expected > Math.Min(TriangleArea(a), TriangleArea(b)))
                throw new InvalidOperationException("Invalid independent triangle intersection oracle.");
            PairVariants("union-derived rational triangle pair " + seed, a, b, expected, expected);
        }

        Reject<ArgumentNullException>(() => engine.MeasureIntersection(null!, square));
        Reject<ArgumentNullException>(() => engine.MeasureIntersection(square, null!));
        foreach (Point2[] bad in new Point2[][] { [], [new(0, 0)], [new(0, 0), new(1, 1)], new Point2[8190] })
        {
            Reject<ArgumentException>(() => engine.MeasureIntersection(bad, square));
            Reject<ArgumentException>(() => engine.MeasureIntersection(square, bad));
        }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 524289, -524289, .5, double.Epsilon })
        foreach (bool y in new[] { false, true })
        {
            Point2 invalid = y ? new(0, bad) : new(bad, 0);
            Point2[] path = [new(0, 0), new(1, 0), invalid];
            Reject<ArgumentException>(() => engine.MeasureIntersection(path, square));
            Reject<ArgumentException>(() => engine.MeasureIntersection(square, path));
            // An empty geometric fill must not bypass validation of the other input.
            Reject<ArgumentException>(() => engine.MeasureIntersection(new Point2[3], path));
        }
        foreach (int bad in new[] { -1, 2, int.MaxValue })
            Reject<ArgumentOutOfRangeException>(() => engine.MeasureIntersection(square, square, (PathFillRule)bad));
        Equal("maximum combined count degenerate", 0, engine.MeasureIntersection(new Point2[4096], new Point2[4096]));
        Reject<ArgumentException>(() => engine.MeasureIntersection(new Point2[4096], new Point2[4097]));
        Equal("intersection following rejected input", 16, engine.MeasureIntersection(square, square));

        Point2[] source = [new(-0.0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, -0.0)];
        Point2[] other = Shift(source, 2, 1);
        long[] Bits() => source.Concat(other).SelectMany(p => new[]
            { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
        long[] before = Bits();
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Equal("small intersection reused", 6, engine.MeasureIntersection(source, other, rule));
            Equal("single after intersection", 16, engine.Measure(square, rule));
            Equal("expanded intersection after single", 1099511627776, engine.MeasureIntersection(full, full, rule));
            Equal("small single after expanded intersection", 16, engine.Measure(square, rule));
        }
        if (!before.SequenceEqual(Bits())) throw new InvalidOperationException("Intersection mutated caller input.");
        passed++;

        void PairVariants(string name, Point2[] a, Point2[] b, double nonZero, double evenOdd)
        {
            (Point2[] A, Point2[] B)[] variants =
            [
                (a, b), (b, a), (a.Reverse().ToArray(), b), (a, b.Reverse().ToArray()),
                (a.Reverse().ToArray(), b.Reverse().ToArray()),
                (a.Skip(1).Concat(a.Take(1)).ToArray(), b.Skip(1).Concat(b.Take(1)).ToArray()),
                (a.Select(p => new Point2(p.Y, p.X)).ToArray(), b.Select(p => new Point2(p.Y, p.X)).ToArray())
            ];
            foreach (var pair in variants)
            {
                Near(name + " NonZero", nonZero, engine.MeasureIntersection(pair.A, pair.B, PathFillRule.NonZero));
                Near(name + " EvenOdd", evenOdd, engine.MeasureIntersection(pair.A, pair.B, PathFillRule.EvenOdd));
            }
        }

        static Point2[] Shift(Point2[] path, int x, int y) => path.Select(p => new Point2(p.X + x, p.Y + y)).ToArray();
        static long TwiceArea(Point2[] path) => checked((long)path[0].X * ((long)path[1].Y - (long)path[2].Y)
            + (long)path[1].X * ((long)path[2].Y - (long)path[0].Y)
            + (long)path[2].X * ((long)path[0].Y - (long)path[1].Y));
        static double TriangleArea(Point2[] path) => TwiceArea(path) * .5;
        static Point2[] Triangle(Random random)
        {
            while (true)
            {
                Point2[] path = Enumerable.Range(0, 3).Select(_ => new Point2(random.Next(-8, 9), random.Next(-8, 9))).ToArray();
                long area = TwiceArea(path);
                if (area == 0) continue;
                if (area < 0) Array.Reverse(path);
                return path;
            }
        }
    }

    private static void Equal(string name, double expected, double actual)
    {
        if (!double.IsFinite(actual) || expected != actual)
            throw new InvalidOperationException($"{name}: expected {expected:R}, actual {actual:R}.");
        passed++;
    }

    private static void Near(string name, double expected, double actual)
    {
        // Topology is exact within the experimental integer domain; integration is compensated
        // binary64. Apply the independently declared protocol's absolute/relative area budget.
        if (!double.IsFinite(actual) || actual < 0 || Math.Abs(expected - actual) > 1e-10 * Math.Max(1, Math.Abs(expected)))
            throw new InvalidOperationException($"{name}: exact oracle {expected:R}, actual {actual:R}.");
        passed++;
    }

    private static void Reject<T>(Action call) where T : ArgumentException
    {
        try { call(); }
        catch (T) { passed++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
}
