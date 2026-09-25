using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Observable parity and actual-operation controls for probing strict order before common support.</summary>
internal static class FilterFirstChecks
{
    internal static void Run()
    {
        int checks = 0, certified = 0, fallbacks = 0, skippedSupportControls = 0, extraProbeControls = 0;
        var triangle = new Snapshot([new(-4, -3), new(5, 1), new(0, 6)]);
        var other = new Snapshot([new(-3, 2), new(4, -4), new(7, 5)]);
        var square = new Snapshot(Rectangle(0, 0, 4, 6));
        var sameVertical = new Snapshot(Rectangle(0, 2, 2, 8));
        var inner = new Snapshot(Rectangle(1, 1, 3, 4));
        Point2[] closed = [.. square.Points, square.Points[0]];
        var bowtie = new Snapshot([new(-4, -4), new(4, 4), new(4, -4), new(-4, 4)]);
        var simultaneous = new Snapshot([new(-14, -4), new(-6, 4), new(-6, -4), new(-14, 4), new(-14, -4),
            new(6, -4), new(14, 4), new(14, -4), new(6, 4), new(6, -4)]);
        var wide = new Snapshot(Rectangle(-20, -10, 20, 10));
        var invalid = new Snapshot([new(0, 0), new(double.NaN, 0), new(0, 1)]);
        var slopeOverflow = new Snapshot([new(0, 0), new(1, double.Epsilon), new(0, 1)]);
        var grown = new Snapshot(Subdivide(triangle.Points, 128));
        (string Name, Snapshot A, Snapshot B)[] pairs =
        [
            ("generic crossing", triangle, other),
            ("same snapshot oblique identity", triangle, triangle),
            ("same snapshot vertical identity", square, square),
            ("same vertical support with different extents", square, sameVertical),
            ("oblique collinear different extents", new Snapshot(Oblique(square.Points)), new Snapshot(Oblique(sameVertical.Points))),
            ("containment", square, inner),
            ("opposite retracing", new Snapshot([.. closed, .. closed.Reverse()]), inner),
            ("doubled winding", new Snapshot([.. closed, .. closed]), inner),
            ("endpoint ties and skipped horizontal edges", new Snapshot(Subdivide(square.Points, 4)), sameVertical),
            ("self crossing identity", bowtie, bowtie),
            ("simultaneous crossings after ordering", simultaneous, wide),
            ("inexact slopes", new Snapshot([new(0, 0), new(1, 3), new(-2, 7)]),
                new Snapshot([new(-1, -1), new(2, 4), new(-3, 6)])),
            ("near tie at large coordinates", new Snapshot(Translate(triangle.Points, Math.ScaleB(1, 48))),
                new Snapshot(Translate(other.Points, Math.ScaleB(1, 48)))),
            ("tiny nonzero area", new Snapshot(Scale(square.Points, -500)), new Snapshot(Scale(sameVertical.Points, -500))),
            ("slope uncertainty before scalar preparation", slopeOverflow, square),
            ("slope uncertainty outside common bounds", slopeOverflow, new Snapshot(Rectangle(20, 20, 24, 24))),
            ("empty", new Snapshot([]), square),
            ("point", new Snapshot([new(0, 0)]), square),
            ("segment", new Snapshot([new(0, 0), new(1, 1)]), square),
            ("zero area", new Snapshot([new(0, 0), new(1, 0), new(2, 0)]), square),
            ("invalid coordinate", invalid, square)
        ];

        // Unavailable scalar records must be inconclusive in either operand position.
        var available = ScalarOrderFilter.Prepare(0, 0, 1, 0, 0);
        ScalarOrderFilter.PreparedEdge unavailable = default;
        Require(available.Ready && !ScalarOrderFilter.TryOrder(in unavailable, in available, .5, out _) &&
            !ScalarOrderFilter.TryOrder(in available, in unavailable, .5, out _),
            "Unavailable scalar metadata must leave support and interval handling in control.");

        foreach (bool roi in new[] { false, true })
        foreach (bool cache in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        foreach (int storage in new[] { 0, 1, 2, 3 })
        {
            var baseline = Engine(roi, cache, filter, storage, true, true, true, false);
            var optimized = Engine(roi, cache, filter, storage, true, true, true, true);
            foreach (var pair in pairs)
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Check(pair.Name, pair.A, pair.B, rule, baseline, optimized, storage, filter);
                Check(pair.Name + " swapped", pair.B, pair.A, rule, baseline, optimized, storage, filter);
                Check(pair.Name + " reversed first", new Snapshot(pair.A.Points.Reverse().ToArray()), pair.B,
                    rule, baseline, optimized, storage, filter);
                Check(pair.Name + " reversed second", pair.A, new Snapshot(pair.B.Points.Reverse().ToArray()),
                    rule, baseline, optimized, storage, filter);
            }
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Check("mandatory strict order", triangle, other, rule, baseline, optimized, storage, filter, mustCertify: true);
                Require(!filter || optimized.FilterAcceptedCount > 0, "Strict-order control did not skip a support test.");
                Check("mandatory same support", square, square, rule, baseline, optimized, storage, filter, mustCertify: true);
                Require(optimized.EndpointSupportMatchCount > 0, "Identity control did not reach the support shortcut.");
                Check("mandatory different vertical extents", square, sameVertical, rule, baseline, optimized, storage, filter, mustCertify: true);
                Require(optimized.EndpointSupportMatchCount > 0, "Different vertical extents did not reach the support shortcut.");

                // One engine pair alternates both overloads, changing array sizes, loop roles,
                // late fallback, exceptions and early zero results. Counters must describe
                // only the current query, including queries that never enter the comparer.
                Check("raw growth after prepared", grown, other, rule, baseline, optimized, 0, filter);
                Check("small prepared after growth", square, sameVertical, rule, baseline, optimized, 1, filter, mustCertify: true);
                Check("late failure after endpoint ordering", simultaneous, wide, rule, baseline, optimized, storage, filter);
                Require(optimized.LastUsedFallback && optimized.EventCount > 0,
                    "Late fallback control must fail after finding crossings.");
                Check("raw recovery after late failure", other, triangle, rule, baseline, optimized, 0, filter, mustCertify: true);
                Check("throwing prepared after success", invalid, square, rule, baseline, optimized, 1, filter);
                Check("recovery after exception", sameVertical, square, rule, baseline, optimized, storage, filter, mustCertify: true);
                Check("null first resets counters", null, square, rule, baseline, optimized, storage, filter);
                Require(optimized.EndpointSupportTestCount == 0 && optimized.EndpointSupportMatchCount == 0 &&
                    optimized.ScalarProbeCount == 0, "Early input failure retained comparer diagnostics.");
                Check("null second", square, null, rule, baseline, optimized, storage, filter);
            }
            foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
            {
                Check("invalid rule", triangle, other, (PathFillRule)invalidRule, baseline, optimized, storage, filter);
                Check("invalid rule with nulls", null, null, (PathFillRule)invalidRule, baseline, optimized, storage, filter);
            }
        }

        // Arithmetic, gap coalescing and active-pass options remain orthogonal to the new
        // comparer order. The small matrix exercises every combination with and without
        // scalar filtering in raw, copied, full-direct and scalar-only borrowed storage.
        foreach (bool area in new[] { false, true })
        foreach (bool coalesce in new[] { false, true })
        foreach (bool active in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        foreach (int storage in new[] { 0, 1, 2, 3 })
        {
            var baseline = Engine(true, true, filter, storage, area, coalesce, active, false);
            var optimized = Engine(true, true, filter, storage, area, coalesce, active, true);
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Check("option matrix crossing", triangle, other, rule, baseline, optimized, storage, filter, mustCertify: true);
                Check("option matrix same support", square, sameVertical, rule, baseline, optimized, storage, filter, mustCertify: true);
                Check("option matrix self query", triangle, triangle, rule, baseline, optimized, storage, filter);
            }
        }

        Require(certified > 0 && fallbacks > 0 && skippedSupportControls > 0 && extraProbeControls > 0,
            "Controls must include successful sweeps, fallback, skipped tests and extra inconclusive probes.");
        Console.WriteLine($"Filter-first endpoint order: {checks} controls; {certified} certified, {fallbacks} fallback, " +
            $"{skippedSupportControls} with skipped support tests, {extraProbeControls} with extra support probes.");

        void Check(string name, Snapshot? first, Snapshot? second, PathFillRule rule,
            GuardedDoubleSweep baseline, GuardedDoubleSweep optimized, int storage, bool filter, bool mustCertify = false)
        {
            long[] beforeFirst = Bits(first?.Points), beforeSecond = Bits(second?.Points);
            Outcome old = Capture(baseline, first, second, rule, storage);
            Outcome current = Capture(optimized, first, second, rule, storage);
            Require(old.Scalar == current.Scalar && old.Diagnostics.SequenceEqual(current.Diagnostics),
                name + ": comparer order changed result, radius, exception, fallback or previous diagnostics.\n" +
                old.Scalar + "\n" + current.Scalar);
            Require(beforeFirst.SequenceEqual(Bits(first?.Points)) && beforeSecond.SequenceEqual(Bits(second?.Points)),
                name + ": caller coordinates changed.");
            Require(baseline.EndpointSupportMatchCount == optimized.EndpointSupportMatchCount,
                name + ": support shortcuts changed.");
            if (filter)
            {
                Require(baseline.ScalarProbeCount == baseline.FilterAttemptCount &&
                    baseline.EndpointSupportTestCount == baseline.EndpointSupportMatchCount + baseline.FilterAttemptCount &&
                    baseline.EndpointSupportTestCount - optimized.EndpointSupportTestCount == baseline.FilterAcceptedCount &&
                    optimized.ScalarProbeCount - baseline.ScalarProbeCount == baseline.EndpointSupportMatchCount &&
                    optimized.EndpointSupportTestCount == optimized.EndpointSupportMatchCount + optimized.FilterIntervalCount,
                    name + ": actual support/probe counts do not match strict accepts and inconclusive support shortcuts.");
            }
            else
                Require(baseline.EndpointSupportTestCount == optimized.EndpointSupportTestCount &&
                    baseline.ScalarProbeCount == 0 && optimized.ScalarProbeCount == 0,
                    name + ": disabled scalar filter changed operations or accessed scalar records.");
            Require(!mustCertify || (!optimized.LastUsedFallback && current.Scalar.ErrorType is null),
                name + ": mandatory success fell back or threw.");
            if (optimized.LastUsedFallback) fallbacks++;
            else if (current.Scalar.ErrorType is null) certified++;
            if (optimized.EndpointSupportTestCount < baseline.EndpointSupportTestCount) skippedSupportControls++;
            if (optimized.ScalarProbeCount > baseline.ScalarProbeCount) extraProbeControls++;
            checks++;
        }
    }

    private static GuardedDoubleSweep Engine(bool roi, bool cache, bool filter, int storage, bool area,
        bool coalesce, bool active, bool filterFirst) => new(roi, cache, filter, storage == 2, area,
            coalesce, active, storage == 3, filterFirst);

    private readonly record struct ScalarResult(long ValueBits, long RadiusBits, bool Fallback, string? Reason,
        Type? ErrorType, string? ErrorParameter);
    private readonly record struct Outcome(ScalarResult Scalar, long[] Diagnostics);
    private static Outcome Capture(GuardedDoubleSweep engine, Snapshot? first, Snapshot? second, PathFillRule rule, int storage)
    {
        double value = 0;
        Exception? error = null;
        try
        {
            value = storage == 0 ? engine.MeasureIntersection(first?.Points!, second?.Points!, rule) :
                engine.MeasureIntersection(first?.Prepared!, second?.Prepared!, rule);
        }
        catch (Exception caught) { error = caught; }
        return new(new(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(engine.LastErrorBound),
            engine.LastUsedFallback, engine.LastFallbackReason, error?.GetType(), (error as ArgumentException)?.ParamName),
        [
            engine.BandCount, engine.EventCount, engine.PeakActiveCount, engine.ActiveEdgeVisits, engine.WorkCount,
            engine.XEvaluationCount, engine.XCacheHitCount, engine.FilterAttemptCount, engine.FilterAcceptedCount, engine.FilterIntervalCount,
            engine.GapContributionCount, engine.GapIntegrationCount, engine.GapMergedCount, engine.HorizontalDifferenceEvaluationCount,
            engine.NoCrossingBandCount, engine.CrossingBandCount, engine.BandInitializationVisits, engine.TopOrderWrites, engine.TopOrderVerificationVisits,
            engine.PreparedScalarRecordsCopied, engine.PreparedScalarRecordsBorrowed
        ]);
    }

    private sealed class Snapshot(Point2[] points)
    {
        internal readonly Point2[] Points = points;
        internal readonly GuardedDoubleSweep.PreparedPath Prepared = GuardedDoubleSweep.PreparePath(points);
    }

    private static long[] Bits(Point2[]? path) => path is null ? [] : path.SelectMany(p => new[]
        { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
    private static Point2[] Rectangle(double left, double bottom, double right, double top) =>
        [new(left, bottom), new(right, bottom), new(right, top), new(left, top)];
    private static Point2[] Oblique(Point2[] path) => path.Select(p => new Point2(p.X + p.Y, p.Y - p.X)).ToArray();
    private static Point2[] Translate(Point2[] path, double offset) => path.Select(p => new Point2(p.X + offset, p.Y - offset)).ToArray();
    private static Point2[] Scale(Point2[] path, int exponent) => path.Select(p => new Point2(Math.ScaleB(p.X, exponent), Math.ScaleB(p.Y, exponent))).ToArray();
    private static Point2[] Subdivide(Point2[] path, int parts) => Enumerable.Range(0, path.Length)
        .SelectMany(i => Enumerable.Range(0, parts).Select(j => new Point2(
            path[i].X + (path[(i + 1) % path.Length].X - path[i].X) * j / parts,
            path[i].Y + (path[(i + 1) % path.Length].Y - path[i].Y) * j / parts))).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
