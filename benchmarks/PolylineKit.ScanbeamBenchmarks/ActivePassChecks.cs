using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Observable equivalence and bounded-work controls for streaming active-edge passes.</summary>
internal static class ActivePassChecks
{
    internal static void Run()
    {
        int checks = 0, noCrossingSuccesses = 0, crossingSuccesses = 0, fallbackControls = 0, budgetImprovements = 0;
        var triangle = new Snapshot([new(-4, -3), new(5, 1), new(0, 6)]);
        var other = new Snapshot([new(-3, 2), new(4, -4), new(7, 5)]);
        var square = new Snapshot(Rectangle(-8, -8, 8, 8));
        var inner = new Snapshot(Rectangle(-2, -2, 2, 2));
        Point2[] closed = [.. inner.Points, inner.Points[0]];
        var bowtie = new Snapshot([new(-4, -4), new(4, 4), new(-4, 4), new(4, -4)]);
        var surrounding = new Snapshot(SubdividedContainer());
        var simultaneous = new Snapshot([new(-14, -4), new(-6, 4), new(-6, -4), new(-14, 4), new(-14, -4),
            new(6, -4), new(14, 4), new(14, -4), new(6, 4), new(6, -4)]);
        var wide = new Snapshot(Rectangle(-20, -10, 20, 10));
        var invalid = new Snapshot([new(0, 0), new(double.NaN, 0), new(0, 1)]);
        var slopeOverflow = new Snapshot([new(0, 0), new(1, double.Epsilon), new(0, 1)]);
        var persistentPair = new Snapshot(Rectangle(-1, -10, 1, 10));
        var fillWindows = new Snapshot([new(-4, -5), new(4, -5), new(4, -2), new(-2, -2),
            new(-2, 2), new(4, 2), new(4, 5), new(-4, 5)]);
        (string Name, Snapshot A, Snapshot B)[] pairs =
        [
            ("generic crossings", triangle, other),
            ("many crossing-free bands", new Snapshot(Subdivide(square.Points, 16)), inner),
            ("real crossing inside a band", bowtie, surrounding),
            ("shared side", inner, new Snapshot(Rectangle(2, -2, 6, 2))),
            ("partial overlap and endpoint ties", inner, new Snapshot(Rectangle(0, -2, 4, 2))),
            ("double winding", new Snapshot([.. closed, .. closed]), square),
            ("opposite retracing", new Snapshot([.. closed, .. closed.Reverse()]), square),
            ("hole", new Snapshot([.. square.Points, square.Points[0], .. closed.Reverse()]), inner),
            ("self intersection identity", bowtie, bowtie),
            ("multiple crossings share one level", simultaneous, wide),
            ("slope overflow", slopeOverflow, inner),
            ("slope overflow outside common bounds", slopeOverflow, new Snapshot(Rectangle(20, 20, 24, 24))),
            ("empty", new Snapshot([]), square),
            ("point", new Snapshot([new(0, 0)]), square),
            ("segment", new Snapshot([new(0, 0), new(1, 1)]), square),
            ("zero area", new Snapshot([new(0, 0), new(1, 0), new(2, 0)]), square),
            ("malformed coordinate", invalid, square),
            ("large exact translation", new Snapshot(Translate(triangle.Points, Math.ScaleB(1, 40))),
                new Snapshot(Translate(other.Points, Math.ScaleB(1, 40)))),
            ("small dyadic scale", new Snapshot(Scale(inner.Points, -500)), new Snapshot(Scale(square.Points, -500)))
        ];

        foreach (bool roi in new[] { false, true })
        foreach (bool coalesce in new[] { false, true })
        foreach (int storage in new[] { 0, 1, 2 })
        {
            var baseline = new GuardedDoubleSweep(roi, true, true, storage == 2, true, coalesce);
            var optimized = new GuardedDoubleSweep(roi, true, true, storage == 2, true, coalesce, true);
            foreach (var pair in pairs)
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Check(pair.Name, pair.A, pair.B, rule, baseline, optimized, storage);
                Check(pair.Name + " swapped", pair.B, pair.A, rule, baseline, optimized, storage);
                Check(pair.Name + " reverse first", new Snapshot(pair.A.Points.Reverse().ToArray()), pair.B, rule, baseline, optimized, storage);
                Check(pair.Name + " reverse second", pair.A, new Snapshot(pair.B.Points.Reverse().ToArray()), rule, baseline, optimized, storage);
            }

            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                // The same two strip edges remain adjacent while the other polygon's
                // winding makes their gap filled, empty, then filled at internal Y levels.
                Check("same pair filled-empty-filled", persistentPair, fillWindows, rule, baseline, optimized, storage,
                    mustCertify: true, exactArea: 12);
                Check("mandatory zero-crossing stream", square, inner, rule, baseline, optimized, storage, mustCertify: true);
                Require(optimized.NoCrossingBandCount > 0 && optimized.CrossingBandCount == 0 &&
                    optimized.BandInitializationVisits == 0 && optimized.TopOrderVerificationVisits == 0,
                    "A crossing-free stream must actually omit initialization and verification passes.");
                Check("mandatory crossing replay", bowtie, surrounding, rule, baseline, optimized, storage, mustCertify: true);
                Require(optimized.CrossingBandCount > 0 && optimized.EventCount > 0 && optimized.TopOrderVerificationVisits > 0,
                    "Crossing bands must retain verified replay.");

                // Crossed -> uncrossed -> failed -> uncrossed exercises reset of the band
                // branch and winding state. The same engine alternates overloads and sizes.
                Check("raw after crossed prepared query", square, inner, rule, baseline, optimized, 0, mustCertify: true);
                Check("failed crossing enumeration", simultaneous, wide, rule, baseline, optimized, storage);
                Require(optimized.LastUsedFallback && optimized.EventCount > 0, "The enumeration failure control must actually fall back after finding crossings.");
                Check("prepared after failed enumeration", inner, square, rule, baseline, optimized, 1, mustCertify: true);
                Check("throwing input after stream", invalid, inner, rule, baseline, optimized, storage);
                Check("raw after throwing input", square, inner, rule, baseline, optimized, 0, mustCertify: true);
                Check("grow scratch after prepared stream", new Snapshot(Subdivide(triangle.Points, 128)), other,
                    rule, baseline, optimized, 0);
                Check("small prepared after growth", inner, square, rule, baseline, optimized, 1, mustCertify: true);
                Check("null first", null, square, rule, baseline, optimized, storage);
                Check("null second", square, null, rule, baseline, optimized, storage);
            }
            foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
            {
                Check("invalid rule", triangle, other, (PathFillRule)invalidRule, baseline, optimized, storage);
                Check("invalid rule and nulls", null, null, (PathFillRule)invalidRule, baseline, optimized, storage);
            }
        }

        // Relevant optional algorithms remain independent. This grid adds both scalar-order
        // and arithmetic settings without duplicating the full lifecycle matrix above.
        foreach (bool cache in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        foreach (bool area in new[] { false, true })
        {
            var baseline = new GuardedDoubleSweep(true, cache, filter, optimizeAreaArithmetic: area, coalesceGaps: true);
            var optimized = new GuardedDoubleSweep(true, cache, filter, optimizeAreaArithmetic: area, coalesceGaps: true, optimizeActivePasses: true);
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Check("flag grid no crossing", square, inner, rule, baseline, optimized, 1, mustCertify: true);
                Check("flag grid crossing", bowtie, surrounding, rule, baseline, optimized, 1, mustCertify: true);
            }
        }

        // Nested same-direction rectangles have an independently known NonZero union.
        // Their many coincident vertical edges make active passes expensive without
        // introducing ambiguous crossings. Smaller actual work may legitimately avoid
        // an old work-budget fallback, but the hard bound itself must still be enforced.
        bool optimizedBudgetReached = false;
        foreach (int count in new[] { 320, 448, 640 })
        {
            double height = 2 * count + 2;
            var nested = new Snapshot(NestedRectangles(count));
            var strip = new Snapshot(Rectangle(1, 0, 3, height));
            var baseline = new GuardedDoubleSweep(true, true, true, optimizeAreaArithmetic: true, coalesceGaps: true);
            var optimized = new GuardedDoubleSweep(true, true, true, optimizeAreaArithmetic: true, coalesceGaps: true, optimizeActivePasses: true);
            Outcome old = Capture(baseline, nested, strip, PathFillRule.NonZero, 1);
            Outcome current = Capture(optimized, nested, strip, PathFillRule.NonZero, 1);
            Require(old.Error is null && current.Error is null, "The valid budget controls must return an area.");
            double exact = 2 * height;
            Require(!optimized.LastUsedFallback || optimized.LastFallbackReason == "work-budget", "Budget family had an unrelated optimized fallback.");
            Require(!baseline.LastUsedFallback || baseline.LastFallbackReason == "work-budget", "Budget family had an unrelated baseline fallback.");
            Require(Math.Abs(current.Value - exact) <= (optimized.LastUsedFallback ? 0 : optimized.LastErrorBound),
                "Optimized budget control does not enclose the exact rectangular intersection.");
            Require(Math.Abs(old.Value - exact) <= (baseline.LastUsedFallback ? 0 : baseline.LastErrorBound),
                "Baseline budget control does not enclose the exact rectangular intersection.");
            if (baseline.LastUsedFallback && !optimized.LastUsedFallback) budgetImprovements++;
            if (optimized.LastUsedFallback)
            {
                optimizedBudgetReached = true;
                Require(baseline.LastUsedFallback && optimized.WorkCount == 2_000_001,
                    "Optimization must preserve the hard charged-work limit.");
            }
            else
            {
                Require(optimized.WorkCount <= 2_000_000, "Successful operation exceeded the work budget.");
                Require(double.IsFinite(optimized.LastErrorBound) && optimized.LastErrorBound >= 0 &&
                    optimized.LastErrorBound <= Math.Min(.25, 1e-10 * Math.Abs(current.Value)),
                    "The newly certified budget control must retain the original accuracy requirement.");
                if (!baseline.LastUsedFallback) Compare("budget family below limit", old, current, baseline, optimized);
            }
            checks++;
        }
        Require(budgetImprovements > 0 && optimizedBudgetReached, "Budget controls must demonstrate both saved work and the unchanged hard limit.");
        Require(noCrossingSuccesses > 0 && crossingSuccesses > 0 && fallbackControls > 0,
            "Active-pass controls must cover both successful band paths and whole-call fallback.");
        Console.WriteLine($"Active passes: {checks} controls; {noCrossingSuccesses} no-crossing successes, {crossingSuccesses} crossing successes, {fallbackControls} fallback controls, {budgetImprovements} work-budget improvements.");

        void Check(string name, Snapshot? first, Snapshot? second, PathFillRule rule, GuardedDoubleSweep baseline,
            GuardedDoubleSweep optimized, int storage, bool mustCertify = false, double? exactArea = null)
        {
            Outcome old = Capture(baseline, first, second, rule, storage);
            Outcome current = Capture(optimized, first, second, rule, storage);
            Require(baseline.LastFallbackReason != "work-budget" && optimized.LastFallbackReason != "work-budget",
                name + ": nonbudget control accidentally reached the work limit.");
            Compare(name, old, current, baseline, optimized);
            Require(!mustCertify || (current.Error is null && !optimized.LastUsedFallback), name + ": mandatory success fell back or threw.");
            if (exactArea.HasValue)
                Require(current.Error is null && !optimized.LastUsedFallback && Math.Abs(current.Value - exactArea.Value) <= optimized.LastErrorBound,
                    name + ": certificate excludes the independently known area.");
            if (optimized.LastUsedFallback) fallbackControls++;
            else if (current.Error is null)
            {
                if (optimized.CrossingBandCount > 0) crossingSuccesses++;
                else if (optimized.NoCrossingBandCount > 0) noCrossingSuccesses++;
            }
            checks++;
        }
    }

    private static void Compare(string name, Outcome old, Outcome current, GuardedDoubleSweep baseline, GuardedDoubleSweep optimized)
    {
        Require(old.Error?.GetType() == current.Error?.GetType() &&
            (old.Error as ArgumentException)?.ParamName == (current.Error as ArgumentException)?.ParamName,
            name + ": changed exception behavior.");
        Require(BitConverter.DoubleToInt64Bits(old.Value) == BitConverter.DoubleToInt64Bits(current.Value) &&
            BitConverter.DoubleToInt64Bits(baseline.LastErrorBound) == BitConverter.DoubleToInt64Bits(optimized.LastErrorBound) &&
            baseline.LastUsedFallback == optimized.LastUsedFallback && baseline.LastFallbackReason == optimized.LastFallbackReason,
            name + ": changed result, certificate or fallback bits.");
        Require(Topology(baseline).SequenceEqual(Topology(optimized)), name + ": changed topology, geometry arithmetic or order-filter decisions.");
        Require(optimized.ActiveEdgeVisits <= baseline.ActiveEdgeVisits && optimized.WorkCount <= baseline.WorkCount &&
            baseline.ActiveEdgeVisits - optimized.ActiveEdgeVisits == baseline.WorkCount - optimized.WorkCount,
            name + ": saved visits do not match saved charged work.");
        Require(optimized.BandInitializationVisits <= baseline.BandInitializationVisits &&
            optimized.TopOrderWrites <= baseline.TopOrderWrites &&
            optimized.TopOrderVerificationVisits <= baseline.TopOrderVerificationVisits,
            name + ": specialized active passes increased their structural operations.");
        if (current.Error is null && !optimized.LastUsedFallback)
            Require(baseline.ActiveEdgeVisits - optimized.ActiveEdgeVisits ==
                baseline.BandInitializationVisits - optimized.BandInitializationVisits +
                baseline.TopOrderVerificationVisits - optimized.TopOrderVerificationVisits,
                name + ": completed sweep savings do not equal omitted initialization and verification visits.");
    }

    private static long[] Topology(GuardedDoubleSweep engine) =>
    [
        engine.BandCount, engine.EventCount, engine.PeakActiveCount, engine.NoCrossingBandCount, engine.CrossingBandCount,
        engine.XEvaluationCount, engine.XCacheHitCount, engine.FilterAttemptCount, engine.FilterAcceptedCount, engine.FilterIntervalCount,
        engine.GapContributionCount, engine.GapIntegrationCount, engine.GapMergedCount, engine.HorizontalDifferenceEvaluationCount
    ];

    private readonly record struct Outcome(double Value, Exception? Error);
    private static Outcome Capture(GuardedDoubleSweep engine, Snapshot? first, Snapshot? second, PathFillRule rule, int storage)
    {
        try
        {
            return new(storage == 0 ? engine.MeasureIntersection(first?.Points!, second?.Points!, rule) :
                engine.MeasureIntersection(first?.Prepared!, second?.Prepared!, rule), null);
        }
        catch (Exception error) { return new(0, error); }
    }

    private sealed class Snapshot(Point2[] points)
    {
        internal readonly Point2[] Points = points;
        internal readonly GuardedDoubleSweep.PreparedPath Prepared = GuardedDoubleSweep.PreparePath(points);
    }

    private static Point2[] NestedRectangles(int count) => Enumerable.Range(0, count)
        .SelectMany(i => Rectangle(0, i, 4, 2 * count + 2 - i).Append(new Point2(0, i))).ToArray();
    private static Point2[] Rectangle(double left, double bottom, double right, double top) =>
        [new(left, bottom), new(right, bottom), new(right, top), new(left, top)];
    private static Point2[] SubdividedContainer() =>
        [new(-6, -5), new(6, -5), new(6, 5), new(-6, 5),
            .. Enumerable.Range(0, 20).Reverse().Select(i => new Point2(-6, -4.75 + .5 * i))];
    private static Point2[] Subdivide(Point2[] path, int count) => Enumerable.Range(0, path.Length)
        .SelectMany(i => Enumerable.Range(0, count).Select(j => new Point2(
            path[i].X + (path[(i + 1) % path.Length].X - path[i].X) * j / count,
            path[i].Y + (path[(i + 1) % path.Length].Y - path[i].Y) * j / count))).ToArray();
    private static Point2[] Translate(Point2[] path, double offset) => path.Select(p => new Point2(p.X + offset, p.Y - offset)).ToArray();
    private static Point2[] Scale(Point2[] path, int exponent) => path.Select(p => new Point2(Math.ScaleB(p.X, exponent), Math.ScaleB(p.Y, exponent))).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
