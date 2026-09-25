using System.Collections;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>The selected-fill scalar API preserves geometry, input ownership and workspace isolation.</summary>
internal static class WindingFilledAreaChecks
{
    private static int passed;
    private static readonly Point2[] Square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
    private static readonly PathFillRule[] Rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];

    internal static int Run()
    {
        passed = 0;
        CheckInputs();
        CheckAnalytic();
        CheckDispatchBoundaries();
        CheckIndependentSweep();
        CheckNestedCalls();
        CheckThreadsAndOwnership();
        Console.WriteLine($"PASS: {passed} selected filled-area public-contract checks.");
        return passed;
    }

    private static void CheckInputs()
    {
        foreach (PathFillRule rule in Rules)
        {
            Reject<ArgumentNullException>("null path", () => WindingArea.FilledArea(null!, rule), "path");
            Point2[][] invalid = [[], [new(0, 0)], [new(0, 0), new(1, 0)],
                [new(0, 0), new(1, 0), new(0, 0)], [new(0, 0), new(0, 0), new(0, 0)],
                [new(0, 0), new(0, 0), new(1, 0), new(1, 0), new(0, 0)],
                Enumerable.Repeat(new Point2(1, 1), 64).ToArray()];
            foreach (Point2[] path in invalid)
            foreach (IReadOnlyList<Point2> input in new IReadOnlyList<Point2>[] { path, path.ToList() })
                Reject<ArgumentException>("too few retained vertices", () => WindingArea.FilledArea(input, rule), "path");
        }
        foreach (int value in new[] { -1, 2, int.MaxValue })
        {
            PathFillRule rule = (PathFillRule)value;
            Reject<ArgumentOutOfRangeException>("invalid rule", () => WindingArea.FilledArea(Square, rule), "fillRule");
            Reject<ArgumentOutOfRangeException>("rule precedes null", () => WindingArea.FilledArea(null!, rule), "fillRule");
            Reject<ArgumentOutOfRangeException>("rule precedes empty", () => WindingArea.FilledArea([], rule), "fillRule");
        }

        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            Math.BitIncrement(1e100), -Math.BitIncrement(1e100) })
        foreach (bool y in new[] { false, true })
        {
            Point2 bad = y ? new(0, value) : new(value, 0);
            Point2[] repeated = Repeat(Square, 16);
            // Position 23 is outside the selector's sample for a 64-point repeated square.
            // A positive sample must never skip full coordinate validation.
            repeated[23] = bad;
            foreach (Point2[] path in new[] { new[] { bad }, new[] { Square[0], Square[1], bad }, repeated })
            foreach (PathFillRule rule in Rules)
                Reject<ArgumentException>("invalid coordinate", () => WindingArea.FilledArea(path, rule), "path");
        }
        Equal("inclusive coordinate boundary", 0,
            WindingArea.FilledArea([new(-1e100, 0), new(0, 0), new(1e100, 0)]));
        Equal("default rule is NonZero", 4, WindingArea.FilledArea(Repeat(Square, 16)));
    }

    private static void CheckAnalytic()
    {
        Point2[] bowtie = [new(0, 0), new(2, 2), new(2, 0), new(0, 2)];
        Point2[] hole = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0),
            new(1, 1), new(1, 3), new(3, 3), new(3, 1), new(1, 1)];
        Point2[] nested = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0),
            new(1, 1), new(3, 1), new(3, 3), new(1, 3), new(1, 1)];
        (string Name, Point2[] Path, double NonZero, double EvenOdd)[] cases =
        [
            ("square", Square, 4, 4),
            ("collinear", [new(-1, 1), new(1, 1), new(3, 1)], 0, 0),
            ("collinear repeated positions", [new(0, 0), new(2, 0), new(0, 0), new(2, 0)], 0, 0),
            ("fully retraced bend", [new(0, 0), new(2, 0), new(2, 2), new(2, 0)], 0, 0),
            ("underflowing triangle", [new(0, 0), new(double.Epsilon, 0), new(0, double.Epsilon)], 0, 0),
            ("subnormal-coordinate triangle", [new(0, 0), new(double.Epsilon, 0), new(0, Math.ScaleB(1, 300))], Math.ScaleB(1, -775), Math.ScaleB(1, -775)),
            ("large-offset triangle", [new(Math.ScaleB(1, 52), 0), new(Math.ScaleB(1, 52) + 1, 0), new(Math.ScaleB(1, 52), 1)], .5, .5),
            ("bowtie", bowtie, 2, 2), ("hole", hole, 12, 12), ("nested loops", nested, 16, 12),
            ("square sixteen times", Repeat(Square, 16), 4, 0),
            ("square seventeen times", Repeat(Square, 17), 4, 4),
            ("bowtie sixteen times", Repeat(bowtie, 16), 2, 0),
            ("bowtie seventeen times", Repeat(bowtie, 17), 2, 2),
            ("hole seven times", Repeat(hole, 7), 12, 12),
            ("nested loops eight times", Repeat(nested, 8), 16, 0),
            ("opposite repeated loops", Repeat([.. Square, Square[0], Square[3], Square[2], Square[1]], 8), 0, 0),
            ("collinear subdivisions", [new(0, 0), new(1, 0), new(2, 0), new(2, 1), new(2, 2), new(1, 2), new(0, 2), new(0, 1)], 4, 4),
            ("duplicates and explicit closure", [new(-0.0, 0), new(0, -0.0), new(2, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0), new(0, 0)], 4, 4)
        ];
        foreach (var item in cases)
        foreach (PathFillRule rule in Rules)
        foreach (Point2[] path in new[] { item.Path, item.Path.Reverse().ToArray(), Cycle(item.Path, item.Path.Length / 3) })
        {
            double expected = rule == PathFillRule.NonZero ? item.NonZero : item.EvenOdd;
            Equal(item.Name + " array " + rule, expected, WindingArea.FilledArea(path, rule));
            Equal(item.Name + " list " + rule, expected, WindingArea.FilledArea(path.ToList(), rule));
            Bits(item.Name + " list retains boundary integral " + rule, Selected(WindingArea.ClosedPath(path), rule),
                WindingArea.FilledArea(path.ToList(), rule));
        }
    }

    private static void CheckDispatchBoundaries()
    {
        foreach (int count in new[] { 63, 64, 1024, 1025 })
        {
            int loops = count / 4;
            Point2[] path = [.. Repeat(Square, loops), .. Enumerable.Repeat(Square[0], count % 4)];
            foreach (PathFillRule rule in Rules)
            {
                double expected = rule == PathFillRule.NonZero || (loops & 1) == 1 ? 4 : 0;
                Equal($"supplied count {count}, {rule}", expected, WindingArea.FilledArea(path, rule));
                Equal($"list supplied count {count}, {rule}", expected, WindingArea.FilledArea(path.ToList(), rule));
            }
        }
        // Both inclusive selector coordinate endpoints occur in a loop repeated 17 times.
        Point2[] wide = [new(-2048, -2048), new(2048, -2048), new(2048, 2048), new(-2048, 2048)];
        foreach (PathFillRule rule in Rules)
            Equal("inclusive specialization coordinates " + rule, 16777216, WindingArea.FilledArea(Repeat(wide, 17), rule));

        Point2[] eligible = Repeat(Square, 17);
        var fallbacks = new List<(string Name, Point2[] Points)>
        {
            ("binary fractions", eligible.Select(p => new Point2(p.X / 8 + .0625, p.Y / 8 - .03125)).ToArray()),
            ("nonbinary fractions", eligible.Select(p => new Point2(p.X / 3 + .1, p.Y / 7 - .2)).ToArray()),
            ("wide integers", eligible.Select(p => new Point2(p.X + 1e9, p.Y - 1e9)).ToArray()),
            ("beyond inclusive selector coordinate", Repeat(wide.Select(p => new Point2(p.X + 1, p.Y)).ToArray(), 17))
        };
        Point2[] unsampledFraction = Repeat(Square, 16);
        unsampledFraction[23] = new(unsampledFraction[23].X + .125, unsampledFraction[23].Y);
        fallbacks.Add(("unsampled fractional coordinate", unsampledFraction));
        foreach (var item in fallbacks)
        foreach (PathFillRule rule in Rules)
        {
            double expected = Selected(WindingArea.ClosedPath(item.Points), rule);
            Bits(item.Name + " exact fallback " + rule, expected, WindingArea.FilledArea(item.Points, rule));
            Bits(item.Name + " list fallback " + rule, expected, WindingArea.FilledArea(item.Points.ToList(), rule));
        }
    }

    private static void CheckIndependentSweep()
    {
        // This existing fixture oracle splits at x coordinates and integrates vertical slabs; it does
        // not call either production engine. Small integer grids exercise intersections and overlaps.
        uint state = 0x713cb08du;
        foreach (int count in new[] { 64, 96, 128 })
        for (int trial = 0; trial < 4; trial++)
        {
            Point2[] path = Enumerable.Range(0, count).Select(_ => new Point2(Next(ref state) % 8, Next(ref state) % 8)).ToArray();
            ContourAreas oracle = ContourSweep.Measure(path);
            // The same crossings near both integer-domain limits stress rational products and event
            // ordering. The transformation is exact; its area scales by 585 squared independently
            // of either production engine. Source coordinates 0..7 become -2048..2047.
            Point2[] nearLimits = path.Select(p => new Point2(p.X * 585 - 2048, p.Y * 585 - 2048)).ToArray();
            foreach (PathFillRule rule in Rules)
            foreach ((Point2[] input, double areaScale, string domain) in new[]
                { (path, 1.0, "small grid"), (nearLimits, 585.0 * 585, "near integer limits") })
            {
                double expected = (rule == PathFillRule.NonZero ? oracle.NonZero : oracle.EvenOdd) * areaScale;
                foreach (Point2[] transformed in new[] { input, input.Reverse().ToArray(), Cycle(input, 17) })
                {
                    double actual = WindingArea.FilledArea(transformed, rule);
                    Near($"slab oracle n={count}, trial={trial}, {domain}, {rule}", expected, actual);
                    Near("array and indexed list parity", actual, WindingArea.FilledArea(transformed.ToList(), rule));
                    Bits("readonly wrapper uses full-result projection", Selected(WindingArea.ClosedPath(transformed), rule),
                        WindingArea.FilledArea(Array.AsReadOnly(transformed), rule));
                }
            }
        }
    }

    private static void CheckNestedCalls()
    {
        Point2[] repeated = Repeat(Square, 17);
        foreach (PathFillRule rule in Rules)
        {
            Action[] nested =
            [
                () => Equal("nested eligible array", 4, WindingArea.FilledArea(repeated, rule)),
                () => Equal("nested readonly fallback", 4, WindingArea.FilledArea(repeated.ToList(), rule)),
                () => Equal("nested ClosedPath", 4, WindingArea.ClosedPath(Square).NonZero),
                () => Equal("two levels of nesting", 4, WindingArea.FilledArea(new CallbackList(Square,
                    () => Equal("innermost array", 4, WindingArea.FilledArea(repeated, rule))), rule))
            ];
            foreach (Action call in nested)
            {
                Equal("callback in FilledArea", 4, WindingArea.FilledArea(new CallbackList(repeated, call), rule));
                var full = WindingArea.ClosedPath(new CallbackList(repeated, call));
                Equal("FilledArea inside ClosedPath nonzero", 4, full.NonZero);
                Equal("FilledArea inside ClosedPath evenodd", 4, full.EvenOdd);
                Equal("FilledArea inside ClosedPath absolute", 68, full.AbsoluteWinding);
                Equal("FilledArea inside ClosedPath signed", 68, full.Signed);
            }

            foreach (bool outerFull in new[] { false, true })
            {
                var sentinel = new InvalidOperationException("intentional failure after a nested filled-area call");
                var input = new CallbackList(repeated, () =>
                {
                    Equal("nested call before failure", 4, WindingArea.FilledArea(repeated, rule));
                    throw sentinel;
                });
                try
                {
                    if (outerFull) _ = WindingArea.ClosedPath(input);
                    else _ = WindingArea.FilledArea(input, rule);
                    throw new InvalidOperationException("The indexer exception was swallowed.");
                }
                catch (InvalidOperationException error) when (ReferenceEquals(error, sentinel)) { passed++; }
                Equal("array reuse after callback failure", 4, WindingArea.FilledArea(repeated, rule));
                Equal("fallback reuse after callback failure", 4, WindingArea.FilledArea(repeated.ToList(), rule));
                Equal("full-result reuse after callback failure", 68, WindingArea.ClosedPath(repeated).AbsoluteWinding);
            }
        }
    }

    private static void CheckThreadsAndOwnership()
    {
        Point2[] repeated = Repeat(Square, 17), twice = Repeat(Square, 16);
        Point2[] fractional = repeated.Select(p => new Point2(p.X / 8, p.Y / 8)).ToArray();
        Point2[] duplicates = [new(-0.0, 0), new(0, -0.0), .. repeated, new(0, -0.0)];
        Point2[][] shared = [repeated, twice, fractional, duplicates];
        long[][] before = shared.Select(CoordinateBits).ToArray();
        using var start = new ManualResetEventSlim(false);
        Task[] workers = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            start.Wait();
            for (int iteration = 0; iteration < 8; iteration++)
            foreach (PathFillRule rule in Rules)
            {
                Equal("parallel odd traversal", 4, WindingArea.FilledArea(repeated, rule));
                Equal("parallel even traversal", rule == PathFillRule.NonZero ? 4 : 0, WindingArea.FilledArea(twice, rule));
                Equal("parallel fractional fallback", .0625, WindingArea.FilledArea(fractional, rule));
                Equal("parallel cleanup", 4, WindingArea.FilledArea(duplicates, rule));
                Reject<ArgumentException>("parallel failure", () => WindingArea.FilledArea([], rule), "path");
                Equal("parallel reuse after failure", 4, WindingArea.FilledArea(repeated, rule));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        start.Set();
        Task.WaitAll(workers);
        for (int i = 0; i < shared.Length; i++)
            True("caller coordinate bits remain unchanged", before[i].SequenceEqual(CoordinateBits(shared[i])));
    }

    private sealed class CallbackList(Point2[] points, Action callback) : IReadOnlyList<Point2>
    {
        private bool called;
        public int Count => points.Length;
        public Point2 this[int index]
        {
            get
            {
                if (!called && index == 1) { called = true; callback(); }
                return points[index];
            }
        }
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static Point2[] Repeat(Point2[] path, int count) => Enumerable.Range(0, count).SelectMany(_ => path).ToArray();
    private static Point2[] Cycle(Point2[] path, int offset) => Enumerable.Range(0, path.Length).Select(i => path[(i + offset) % path.Length]).ToArray();
    private static long[] CoordinateBits(Point2[] points) => points.SelectMany(p => new[] { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
    private static double Selected(WindingAreaResult full, PathFillRule rule) => rule == PathFillRule.NonZero ? full.NonZero : full.EvenOdd;
    private static uint Next(ref uint state) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }

    private static void Equal(string name, double expected, double actual)
    {
        if (!double.IsFinite(actual) || expected != actual)
            throw new InvalidOperationException($"{name}: expected {expected:R}, actual {actual:R}.");
        Interlocked.Increment(ref passed);
    }

    private static void Bits(string name, double expected, double actual)
    {
        if (!double.IsFinite(actual) || BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(actual))
            throw new InvalidOperationException($"{name}: expected bits of {expected:R}, actual {actual:R}.");
        Interlocked.Increment(ref passed);
    }

    private static void Near(string name, double expected, double actual)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-10 * Math.Max(1, Math.Abs(expected)))
            throw new InvalidOperationException($"{name}: expected {expected:R}, actual {actual:R}.");
        Interlocked.Increment(ref passed);
    }

    private static void True(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        Interlocked.Increment(ref passed);
    }

    private static void Reject<T>(string name, Action action, string parameter) where T : ArgumentException
    {
        try { action(); }
        catch (ArgumentException error) when (error.GetType() == typeof(T) && error.ParamName == parameter)
        { Interlocked.Increment(ref passed); return; }
        throw new InvalidOperationException($"{name}: expected {typeof(T).Name} for '{parameter}'.");
    }
}
