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
            ("inclusive domain square", [new(-32768, -32768), new(32768, -32768), new(32768, 32768), new(-32768, 32768)], 4294967296, 4294967296),
            ("unit boundary square", [new(32767, 32767), new(32768, 32767), new(32768, 32768), new(32767, 32768)], 1, 1)
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
            [new(-32768, -32768), new(-1, -2), new(0, -1)]
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
        Console.WriteLine($"Integer scanbeam controls: {passed} assertions, including 400 small-grid and 10 full-domain independent walk oracles.");

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
            32769, -32769, .5, double.Epsilon })
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
