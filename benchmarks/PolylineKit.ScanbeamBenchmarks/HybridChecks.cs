using PolylineKit;
using PolylineKit.ActiveSweep;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Numeric and input-contract controls for scalar closed-area dispatch and single-loop sweeps.</summary>
internal static class HybridChecks
{
    internal static void Run()
    {
        int checks = 0, selected = 0, rejected = 0, certified = 0, fallbacks = 0;
        var hybrid = new HybridClosedArea();
        var integer = new IntegerScanbeam();
        Point2[] grid = Inputs.Grid(128, 0x6b792e21u);
        Point2[] square = Rectangle(0, 0, 4, 4);
        Point2[] inner = Rectangle(1, 1, 3, 3);
        Point2[] closed = [.. square, square[0]];
        Point2[] slopeOverflow = [new(0, 0), new(1, double.Epsilon), new(0, 1)];
        Point2[] tiny = square.Select(p => new Point2(Math.ScaleB(p.X, -500), Math.ScaleB(p.Y, -500))).ToArray();
        Point2[] duplicateCollapse = Enumerable.Repeat(new Point2(0, 0), 64).ToArray();
        Point2[] manyLevels = Enumerable.Range(0, 128).Select(i => new Point2(i % 3, i)).ToArray();
        Point2[] fewLevelSparse = [.. Enumerable.Range(0, 126).Select(i => new Point2(i, i % 2)), new(125, -1), new(0, -1)];
        Point2[] repeated = Enumerable.Range(0, 32).SelectMany(_ => square).ToArray();

        Require(HybridClosedArea.Select(grid).Backend == "IntegerScanbeam", "Dense control must exercise integer selection.");
        Require(HybridClosedArea.Select(fewLevelSparse).Backend == "Winding", "Few Y levels must not alone select the integer sweep.");
        Require(HybridClosedArea.Select(repeated).Backend == "Winding", "Repeated support must not count as proper sampled crossings.");
        Require(HybridClosedArea.Select(manyLevels).Backend == "Winding", "Many Y levels must stay with Winding.");
        Require(HybridClosedArea.Select(Inputs.Grid(63, 17)).Backend == "Winding" &&
            HybridClosedArea.Select(Inputs.Grid(1025, 17)).Backend == "Winding", "Vertex dispatch bounds are exclusive outside 64..1024.");
        Point2[] positiveBoundary = grid.Select(p => new Point2(p.X + 2041, p.Y)).ToArray();
        Point2[] negativeBoundary = grid.Select(p => new Point2(p.X - 2048, p.Y)).ToArray();
        Require(HybridClosedArea.Select(positiveBoundary).Backend == "IntegerScanbeam" &&
            HybridClosedArea.Select(negativeBoundary).Backend == "IntegerScanbeam", "Exact +/-2048 limits must be eligible.");
        Require(HybridClosedArea.Select(grid.Select(p => new Point2(p.X + 2042, p.Y)).ToArray()).Backend == "Winding",
            "A coordinate beyond 2048 must not enter the Int64 dispatch domain.");

        // Selected paths compare against the identical forced backend, including every output bit.
        // Rejected paths compare against the public Winding contract, including its exception parameter.
        (string Name, Point2[]? Points)[] contracts =
        [
            ("dense selected", grid), ("positive domain boundary", positiveBoundary), ("negative domain boundary", negativeBoundary),
            ("fractional dense rejected", grid.Select(p => new Point2(p.X / 8, p.Y / 8)).ToArray()),
            ("wide rejected", grid.Select(p => new Point2(p.X + 1e9, p.Y - 1e9)).ToArray()),
            ("many levels", manyLevels), ("few levels sparse", fewLevelSparse), ("repeated support", repeated),
            ("null", null), ("empty", []), ("point", [new(0, 0)]), ("segment", [new(0, 0), new(1, 1)]),
            ("collapsed duplicates", duplicateCollapse), ("collapsed closure", [new(0, 0), new(1, 1), new(0, 0)]),
            ("collinear area zero", [new(0, 0), new(1, 1), new(2, 2)]),
            ("square", square), ("optional closure", closed), ("tiny", tiny), ("slope overflow", slopeOverflow),
            ("NaN", [new(0, 0), new(double.NaN, 1), new(1, 0)]),
            ("infinite", [new(0, 0), new(1, double.PositiveInfinity), new(1, 0)]),
            ("too large", [new(0, 0), new(Math.BitIncrement(1e100), 1), new(1, 0)]),
            ("maximum magnitude", Rectangle(-1e100, -1, 1e100, 1))
        ];
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            foreach (var item in contracts)
            {
                Point2[]? points = item.Points;
                long[] before = Bits(points);
                HybridSelection selection = HybridClosedArea.Select(points);
                bool useInteger = selection.Backend == "IntegerScanbeam";
                Outcome expected = Capture(() => useInteger ? integer.Measure(points!, rule) : Winding(points!, rule));
                Outcome actual = Capture(() => hybrid.Measure(points!, rule));
                Require(actual == expected && hybrid.LastSelection == selection, item.Name + ": hybrid changed selected backend behavior.");
                Require(before.SequenceEqual(Bits(points)), item.Name + ": hybrid mutated input.");
                if (useInteger) selected++; else rejected++;
                checks++;
            }
            // Reusing the same instance across successful, rejected and throwing inputs must not retain a route.
            foreach (Point2[]? path in new Point2[]?[] { grid, square, duplicateCollapse, grid, null, square, positiveBoundary })
            {
                Outcome actual = Capture(() => hybrid.Measure(path!, rule));
                Outcome fresh = Capture(() => new HybridClosedArea().Measure(path!, rule));
                Require(actual == fresh, "Alternating hybrid routes retained previous state.");
                checks++;
            }
        }
        foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
        foreach (Point2[]? path in new Point2[]?[] { grid, square, null })
        {
            Outcome actual = Capture(() => hybrid.Measure(path!, (PathFillRule)invalidRule));
            Require(actual.ErrorType == typeof(ArgumentOutOfRangeException) && actual.ErrorParameter == "rule",
                "Hybrid must reject an invalid rule before selecting a route.");
            Require(hybrid.LastSelection == default, "Invalid rule retained hybrid route diagnostics.");
            checks++;
        }

        (string Name, Point2[] Points)[] analytic =
        [
            ("square", square), ("optional closure", closed),
            ("collinear", [new(-2, -2), new(0, 0), new(2, 2)]),
            ("double traversal", [.. closed, .. closed]),
            ("opposite traversal", [.. closed, .. closed.Reverse()]),
            ("shared collinear support", [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0), new(2, 0), new(2, 4), new(0, 4)]),
            ("bowtie", [new(0, 0), new(4, 4), new(4, 0), new(0, 4)]),
            ("multiway crossing", [new(-4, -4), new(4, 4), new(-4, 4), new(4, -4), new(0, -5), new(0, 5)]),
            ("fractional", [new(.125, .375), new(1.375, 2.125), new(3.625, .875), new(.625, 2.875)]),
            ("small translated triangle", [new(Math.ScaleB(1, 52), 0), new(Math.ScaleB(1, 52) + 1, 0), new(Math.ScaleB(1, 52), 1)]),
            ("slope uncertainty", slopeOverflow), ("tiny square", tiny)
        ];
        var oracleCases = analytic.Concat(Enumerable.Range(0, 24)
            .Select(i => ($"oracle-grid-{i}", Inputs.Grid(8 + i % 5, (uint)(0x411351 + i))))).ToArray();
        foreach (int configuration in Enumerable.Range(0, 4))
        {
            GuardedDoubleSweep engine = Engine(configuration);
            foreach (var item in oracleCases)
            {
                var expected = ExactAreaOracle.Measure(item.Item2);
                foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                {
                    Point2[] points = item.Item2;
                    long[] before = Bits(points);
                    double value = engine.MeasureClosedPath(points, rule);
                    double truth = rule == PathFillRule.NonZero ? expected.NonZero : expected.EvenOdd;
                    Require(double.IsFinite(value) && Math.Abs(value - truth) <= 1e-10 * Math.Max(1, Math.Abs(truth)),
                        item.Item1 + ": closed double sweep disagrees with exact-rational oracle.");
                    if (engine.LastUsedFallback)
                    {
                        Require(BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(Winding(points, rule)),
                            item.Item1 + ": fallback did not preserve the Winding result.");
                        fallbacks++;
                    }
                    else
                    {
                        double oracleRounding = Math.Abs(Math.BitIncrement(truth) - truth);
                        Require(double.IsFinite(engine.LastErrorBound) && engine.LastErrorBound >= 0 &&
                            Math.Abs(value - truth) <= engine.LastErrorBound + oracleRounding,
                            item.Item1 + ": declared radius does not contain the exact-oracle rounded area.");
                        certified++;
                    }
                    Require(before.SequenceEqual(Bits(points)), item.Item1 + ": closed sweep mutated input.");
                    checks++;
                }
            }
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                foreach (var item in contracts.Where(item => item.Points is null || item.Points.Length < 4 ||
                    item.Name is "collapsed duplicates" or "maximum magnitude"))
                {
                    Outcome expected = Capture(() => Winding(item.Points!, rule));
                    Outcome actual = Capture(() => engine.MeasureClosedPath(item.Points!, rule));
                    if (expected.ErrorType is not null)
                        Require(actual == expected, item.Name + ": closed sweep changed Winding input validation.");
                    else
                        Require(actual.ErrorType is null, item.Name + ": closed sweep rejected valid input.");
                    checks++;
                }

                // Exercise a prepared two-loop call on an engine which just performed a single-loop call,
                // then switch back. A leaked single-loop mode would ignore the second loop's winding.
                var preparedSquare = GuardedDoubleSweep.PreparePath(square);
                var preparedInner = GuardedDoubleSweep.PreparePath(inner);
                foreach (int iteration in Enumerable.Range(0, 3))
                {
                    CheckSweep(() => engine.MeasureClosedPath(closed, rule),
                        fresh => fresh.MeasureClosedPath(closed, rule), "closed before intersection");
                    CheckSweep(() => engine.MeasureIntersection(square, inner, rule),
                        fresh => fresh.MeasureIntersection(square, inner, rule), "raw intersection after closed");
                    CheckSweep(() => engine.MeasureClosedPath(slopeOverflow, rule),
                        fresh => fresh.MeasureClosedPath(slopeOverflow, rule), "single-loop fallback after raw intersection");
                    Require(engine.LastUsedFallback, "Slope overflow control must exercise single-loop fallback.");
                    CheckSweep(() => engine.MeasureIntersection(preparedSquare, preparedInner, rule),
                        fresh => fresh.MeasureIntersection(preparedSquare, preparedInner, rule), "prepared intersection after closed fallback");
                    CheckSweep(() => engine.MeasureClosedPath(null!, rule),
                        fresh => fresh.MeasureClosedPath(null!, rule), "closed exception after prepared intersection");
                    CheckSweep(() => engine.MeasureIntersection(inner, square, rule),
                        fresh => fresh.MeasureIntersection(inner, square, rule), "swapped raw intersection after exception");
                }
            }
            foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
            {
                Outcome actual = Capture(() => engine.MeasureClosedPath(null!, (PathFillRule)invalidRule));
                Require(actual.ErrorType == typeof(ArgumentOutOfRangeException) && actual.ErrorParameter == "rule",
                    "Single-loop sweep must reject an invalid rule before the path.");
                checks++;
            }

            void CheckSweep(Func<double> reused, Func<GuardedDoubleSweep, double> firstCall, string name)
            {
                var fresh = Engine(configuration);
                SweepOutcome expected = CaptureSweep(fresh, () => firstCall(fresh));
                SweepOutcome actual = CaptureSweep(engine, reused);
                Require(actual == expected, name + ": call-mode reuse changed value, radius, exception or fallback.");
                checks++;
            }
        }
        Require(selected > 0 && rejected > 0 && certified > 0 && fallbacks > 0,
            "Hybrid checks must cover both selected backends and both double-sweep outcomes.");
        Console.WriteLine($"Hybrid closed area: {checks} controls; selected={selected}, rejected={rejected}, " +
            $"closed sweep certified={certified}, fallback={fallbacks}.");
    }

    private static GuardedDoubleSweep Engine(int configuration) => configuration == 0 ? new() :
        new(restrictToCommonY: true, cacheEndpointX: true, scalarOrderFilter: true,
            directPreparedEdges: configuration == 2, optimizeAreaArithmetic: true, coalesceGaps: true,
            optimizeActivePasses: true, borrowPreparedScalars: configuration == 3, filterBeforeSupport: true);
    private static double Winding(Point2[] path, PathFillRule rule)
    {
        WindingAreaResult value = WindingArea.ClosedPath(path);
        return rule == PathFillRule.NonZero ? value.NonZero : value.EvenOdd;
    }
    private readonly record struct Outcome(long ValueBits, Type? ErrorType, string? ErrorParameter);
    private static Outcome Capture(Func<double> call)
    {
        try { return new(BitConverter.DoubleToInt64Bits(call()), null, null); }
        catch (Exception error) { return new(0, error.GetType(), (error as ArgumentException)?.ParamName); }
    }
    private readonly record struct SweepOutcome(Outcome Result, bool Fallback, string? Reason, long RadiusBits);
    private static SweepOutcome CaptureSweep(GuardedDoubleSweep engine, Func<double> call)
    {
        Outcome result = Capture(call);
        return new(result, engine.LastUsedFallback, engine.LastFallbackReason, BitConverter.DoubleToInt64Bits(engine.LastErrorBound));
    }
    private static Point2[] Rectangle(double left, double bottom, double right, double top) =>
        [new(left, bottom), new(right, bottom), new(right, top), new(left, top)];
    private static long[] Bits(Point2[]? points) => points is null ? [] : points.SelectMany(p => new[]
        { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
