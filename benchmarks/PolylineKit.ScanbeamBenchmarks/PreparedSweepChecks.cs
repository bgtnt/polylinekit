using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Independent behavioral controls for reusable immutable sweep inputs.</summary>
internal static class PreparedSweepChecks
{
    private static int passed;

    internal static void Run()
    {
        passed = 0;
        Exception? preparationError = null;
        try { GuardedDoubleSweep.PreparePath(null!); }
        catch (Exception error) { preparationError = error; }
        Require(preparationError is ArgumentNullException, "Preparing null must fail immediately.");

        Point2[] square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];
        Point2[] closed = [.. square, square[0]];
        Point2[] inner = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        Point2[] firstTriangle = [new(-4, -3), new(5, 1), new(0, 6)];
        Point2[] secondTriangle = [new(-3, 2), new(4, -4), new(7, 5)];
        Point2[] overflowingSlope = [new(0, 0), new(1, double.Epsilon), new(0, 1)];
        Point2[] huge = [new(-1e100, -1), new(1e100, -1), new(1e100, 1), new(-1e100, 1)];
        Point2[] narrow = [new(0, -2), new(1e-250, -2), new(1e-250, 2), new(0, 2)];
        Point2[] repeatedTriangle = new Point2[8190];
        Array.Fill(repeatedTriangle, firstTriangle[0]);
        repeatedTriangle[^2] = firstTriangle[1]; repeatedTriangle[^1] = firstTriangle[2];
        Point2[] overBudget = new Point2[8193];
        Array.Fill(overBudget, firstTriangle[0]);
        overBudget[^2] = firstTriangle[1]; overBudget[^1] = firstTriangle[2];

        // Several rows force preparation to encounter arithmetic or validation uncertainty.
        // Construction must still succeed: query-time input order and the old AABB shortcut
        // decide whether these become fallback, an exception, or certified zero.
        var pairs = new (string Name, Snapshot First, Snapshot Second)[]
        {
            Pair("generic crossing", firstTriangle, secondTriangle),
            Pair("optional closing vertex", closed, inner),
            Pair("endpoint ties with skipped horizontal and duplicate edges",
                [new(0, 0), new(4, 0), new(4, 0), new(4, 4), new(2, 4), new(0, 4), new(0, 0)],
                [new(2, 0), new(6, 0), new(6, 4), new(2, 4), new(2, 0)]),
            Pair("start and end ties", [new(-4, 0), new(-2, 2), new(0, 0), new(2, 2), new(4, 0), new(0, -2)],
                [new(-3, 2), new(0, 0), new(3, 2), new(3, 4), new(-3, 4)]),
            Pair("double winding", [.. closed, .. closed], inner),
            Pair("opposite retracing", [.. closed, .. closed.Reverse()], square),
            Pair("self crossings", [new(0, 0), new(4, 4), new(4, 0), new(0, 4)], square),
            Pair("multiway crossings", [new(-6, -6), new(6, 6), new(-6, 6), new(6, -6),
                new(0, -8), new(0, 8), new(-8, 0), new(8, 0)], square),
            Pair("numerical fallback", huge, narrow),
            Pair("slope uncertainty with overlap", overflowingSlope, square),
            Pair("slope uncertainty but disjoint bounds", overflowingSlope,
                [new(10, 10), new(14, 10), new(14, 14), new(10, 14)]),
            Pair("slope uncertainty but zero-area other bounds", overflowingSlope,
                [new(-2, .5), new(0, .5), new(2, .5)]),
            Pair("slope uncertainty before invalid other input", overflowingSlope,
                [new(0, 0), new(4, 0), new(double.NaN, 4)]),
            Pair("empty", [], square),
            Pair("point", [new(0, 0)], square),
            Pair("segment", [new(0, 0), new(4, 4)], square),
            Pair("collapsed closure", [new(0, 0), new(1, 0), new(0, 0)], square),
            Pair("collinear zero-area", [new(0, 0), new(1, 1), new(2, 2)], square),
            Pair("invalid coordinate", [new(0, 0), new(double.PositiveInfinity, 0), new(0, 4)], square),
            Pair("different invalid inputs", [new(double.NaN, 0), new(1, 0), new(0, 1)],
                [new(0, 0), new(Math.BitIncrement(1e100), 0), new(0, 1)]),
            Pair("combined vertex budget", repeatedTriangle, secondTriangle),
            Pair("per-path vertex budget", overBudget, secondTriangle),
            Pair("vertex budget before invalid coordinates", overBudget,
                [new(double.NaN, 0), new(1, 0), new(0, 1)])
        };

        // One prepared catalogue is used by every flag combination. Preparation therefore
        // cannot capture a loop number, mutable status index, cache generation, or engine flag.
        foreach (bool roi in new[] { false, true })
        foreach (bool cache in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        {
            var raw = new GuardedDoubleSweep(roi, cache, filter);
            var prepared = new GuardedDoubleSweep(roi, cache, filter);
            foreach (var pair in pairs)
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Compare(pair.Name, raw, prepared, pair.First, pair.Second, rule);
                Compare(pair.Name + " swapped", raw, prepared, pair.Second, pair.First, rule);
                Compare(pair.Name + " same snapshot twice", raw, prepared, pair.First, pair.First, rule);
            }

            var ordinary = pairs[0];
            foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
            {
                Compare("invalid rule", raw, prepared, ordinary.First, ordinary.Second, (PathFillRule)invalidRule);
                Compare("invalid rule and malformed input", raw, prepared, pairs[18].First, ordinary.First, (PathFillRule)invalidRule);
                Compare("invalid rule and nulls", raw, prepared, null, null, (PathFillRule)invalidRule);
            }
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Compare("null first snapshot", raw, prepared, null, ordinary.Second, rule);
                Compare("null second snapshot", raw, prepared, ordinary.First, null, rule);
            }

            // Reuse the same prepared engine across success, slope-overflow fallback,
            // throwing input, different loop roles, and a self query, then recover success.
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Compare("recovery before fallback", raw, prepared, ordinary.First, ordinary.Second, PathFillRule.NonZero);
                Compare("slope-overflow fallback", raw, prepared, pairs[9].First, pairs[9].Second, PathFillRule.NonZero);
                Require(prepared.LastUsedFallback && prepared.LastFallbackReason == "nonfinite-arithmetic",
                    "The reuse control must actually execute fallback for the overflowing 1 / epsilon slope.");
                Compare("throwing fallback", raw, prepared, pairs[18].First, ordinary.Second, PathFillRule.EvenOdd);
                Compare("recovery after exception", raw, prepared, ordinary.Second, ordinary.First, PathFillRule.EvenOdd);
                Require(!prepared.LastUsedFallback, "The recovery control must return to certified sweep execution.");
                Compare("reused self query", raw, prepared, ordinary.First, ordinary.First, PathFillRule.NonZero);
            }

            Compare("disjoint uncertain slopes", raw, prepared, pairs[10].First, pairs[10].Second, PathFillRule.NonZero);
            Require(!prepared.LastUsedFallback && prepared.LastErrorBound == 0,
                "Slope preparation uncertainty must not override the exact disjoint-bounds result.");
        }

        // Mutate the actual source arrays after preparing, including those needed for whole-call
        // fallback. The baseline retains an independent copy of their original bits. This checks
        // both precomputed geometry and the coordinates supplied to the fallback implementation.
        foreach (var pair in pairs)
        {
            Poison(pair.First.Source);
            Poison(pair.Second.Source);
        }
        var rawAfterMutation = new GuardedDoubleSweep(true, true, true);
        var preparedAfterMutation = new GuardedDoubleSweep(true, true, true);
        foreach (var pair in pairs)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Compare(pair.Name + " after caller mutation", rawAfterMutation, preparedAfterMutation, pair.First, pair.Second, rule);
            Compare(pair.Name + " reversed after caller mutation", rawAfterMutation, preparedAfterMutation, pair.Second, pair.First, rule);
        }

        // Engines own their scratch buffers; snapshots alone may be shared concurrently.
        // Compare after the parallel loop so the assertion counter itself stays single-threaded.
        var concurrent = new (Outcome Expected, Outcome Actual)[32];
        Parallel.For(0, concurrent.Length, i =>
        {
            bool reverse = (i & 1) != 0;
            var pair = pairs[(i / 2) % 8];
            Snapshot a = reverse ? pair.Second : pair.First, b = reverse ? pair.First : pair.Second;
            PathFillRule rule = (i & 2) == 0 ? PathFillRule.NonZero : PathFillRule.EvenOdd;
            var raw = new GuardedDoubleSweep((i & 4) != 0, (i & 8) != 0, (i & 16) != 0);
            var prepared = new GuardedDoubleSweep((i & 4) != 0, (i & 8) != 0, (i & 16) != 0);
            concurrent[i] = (Capture(raw, () => raw.MeasureIntersection(a.Original, b.Original, rule)),
                Capture(prepared, () => prepared.MeasureIntersection(a.Prepared, b.Prepared, rule)));
        });
        for (int i = 0; i < concurrent.Length; i++)
            Require(concurrent[i].Expected == concurrent[i].Actual, "Shared snapshot changed an independent engine's outcome at " + i + ".");

        Console.WriteLine($"Prepared double sweep: {passed} snapshot, reuse, input-contract and all-diagnostics parity controls.");
    }

    private static (string Name, Snapshot First, Snapshot Second) Pair(string name, Point2[] first, Point2[] second) =>
        (name, new Snapshot(first), new Snapshot(second));

    private sealed class Snapshot
    {
        internal readonly Point2[] Source, Original;
        internal readonly GuardedDoubleSweep.PreparedPath Prepared;
        internal Snapshot(Point2[] path)
        {
            Source = (Point2[])path.Clone();
            Original = (Point2[])path.Clone();
            Prepared = GuardedDoubleSweep.PreparePath(Source);
        }
    }

    private static void Poison(Point2[] source)
    {
        for (int i = 0; i < source.Length; i++) source[i] = new(double.NaN, i);
    }

    private static void Compare(string name, GuardedDoubleSweep raw, GuardedDoubleSweep prepared,
        Snapshot? first, Snapshot? second, PathFillRule rule)
    {
        Outcome expected = Capture(raw, () => raw.MeasureIntersection(first?.Original!, second?.Original!, rule));
        Outcome actual = Capture(prepared, () => prepared.MeasureIntersection(first?.Prepared!, second?.Prepared!, rule));
        Require(actual == expected, name + ": preparation changed outcome or diagnostics.\nExpected: " + expected + "\nActual: " + actual);
    }

    private readonly record struct Outcome(long ValueBits, Type? ExceptionType, string? ExceptionParameter,
        bool Fallback, string? FallbackReason, long RadiusBits, int Bands, long Events, int PeakActive,
        long ActiveVisits, long Work, long XEvaluations, long XCacheHits, long FilterAttempts, long FilterAccepted, long FilterInterval);

    private static Outcome Capture(GuardedDoubleSweep engine, Func<double> operation)
    {
        double value = 0;
        Exception? exception = null;
        try { value = operation(); }
        catch (Exception error) { exception = error; }
        return new(BitConverter.DoubleToInt64Bits(value), exception?.GetType(), (exception as ArgumentException)?.ParamName,
            engine.LastUsedFallback, engine.LastFallbackReason, BitConverter.DoubleToInt64Bits(engine.LastErrorBound),
            engine.BandCount, engine.EventCount, engine.PeakActiveCount, engine.ActiveEdgeVisits, engine.WorkCount,
            engine.XEvaluationCount, engine.XCacheHitCount, engine.FilterAttemptCount, engine.FilterAcceptedCount, engine.FilterIntervalCount);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed++;
    }
}
