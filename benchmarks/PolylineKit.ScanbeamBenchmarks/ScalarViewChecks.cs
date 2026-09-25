using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Exact observable parity and lifetime controls for borrowing only prepared scalar records.</summary>
internal static class ScalarViewChecks
{
    internal static void Run()
    {
        int checks = 0;
        var a = new Snapshot([new(-4, -3), new(5, 1), new(0, 6)]);
        var b = new Snapshot([new(-3, 2), new(4, -4), new(7, 5)]);
        var horizontalHeavy = new Snapshot([new(-2, -2), new(-1, -2), new(0, -2), new(2, -2),
            new(2, -2), new(2, 3), new(1, 3), new(0, 3), new(-2, 3), new(-2, -2)]);
        var large = new Snapshot(Subdivide(a.Original, 256));
        var medium = new Snapshot(Subdivide(b.Original, 32));
        var horizontalOnly = new Snapshot([new(-5, 1), new(0, 1), new(3, 1), new(5, 1)]);
        var invalid = new Snapshot([new(0, 0), new(double.NaN, 1), new(0, 2)]);
        var steep = new Snapshot([new(0, 0), new(1, double.Epsilon), new(0, 1)]);
        var containing = new Snapshot(Rectangle(-20, -10, 20, 10));
        var simultaneous = new Snapshot([new(-14, -4), new(-6, 4), new(-6, -4), new(-14, 4), new(-14, -4),
            new(6, -4), new(14, 4), new(14, -4), new(6, 4), new(6, -4)]);
        Point2[] oversized = new Point2[8193];
        Array.Fill(oversized, a.Original[0]);
        oversized[^2] = a.Original[1]; oversized[^1] = a.Original[2];
        (string Name, Snapshot? First, Snapshot? Second, bool Binds)[] pairs =
        [
            ("generic crossing", a, b, true),
            ("first vertex count differs from edge count", horizontalHeavy, a, true),
            ("second vertex count differs from edge count", b, horizontalHeavy, true),
            ("same snapshot in both roles", horizontalHeavy, horizontalHeavy, true),
            ("large first scalar range", large, horizontalHeavy, true),
            ("large second scalar range", horizontalHeavy, large, true),
            ("fallback after scalar binding", simultaneous, containing, true),
            ("zero first edge range", horizontalOnly, b, false),
            ("zero second edge range", b, horizontalOnly, false),
            ("disjoint input", new Snapshot(Rectangle(40, 40, 45, 45)), a, false),
            ("slope rejection before binding", steep, horizontalHeavy, false),
            ("malformed input before binding", invalid, a, false),
            ("invalid disjoint input still checked", invalid, new Snapshot(Rectangle(40, 40, 45, 45)), false),
            ("empty input", new Snapshot([]), a, false),
            ("point input", new Snapshot([new(0, 0)]), a, false),
            ("segment input", new Snapshot([new(0, 0), new(1, 1)]), a, false),
            ("collapsed closure", new Snapshot([new(0, 0), new(1, 0), new(0, 0)]), a, false),
            ("vertex budget before binding", new Snapshot(oversized), a, false),
            ("null first", null, b, false),
            ("null second", a, null, false)
        ];
        Require(horizontalHeavy.Prepared.EdgeCount == 2 && horizontalHeavy.Prepared.VertexCount == 10 &&
            large.Prepared.EdgeCount == 768, "Scalar range controls must use genuinely unequal edge and vertex counts.");

        foreach (bool roi in new[] { false, true })
        foreach (bool cache in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        {
            var group = new Engines(roi, cache, filter);
            foreach (var pair in pairs)
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Check(pair.Name, group, pair.First, pair.Second, rule, true, pair.Binds);
                Check(pair.Name + " swapped", group, pair.Second, pair.First, rule, true, pair.Binds);
            }
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                // The same hybrid and full-direct instances alternate prepared scalar views
                // with raw geometry, allocation growth and early/late failure paths.
                Check("bind before raw growth", group, horizontalHeavy, a, rule, true, true);
                Check("raw growth must ignore old scalar views", group, large, medium, rule, false, false);
                Check("reverse range split after raw growth", group, b, horizontalHeavy, rule, true, true);
                Check("short raw query after large storage", group, a, b, rule, false, false);
                Check("late fallback with bound scalars", group, simultaneous, containing, rule, true, true);
                Require(group.Hybrid.LastUsedFallback && group.Hybrid.EventCount > 0,
                    "Lifetime control must abandon a sweep after binding scalar records and finding crossings.");
                Check("raw recovery after late fallback", group, a, b, rule, false, false);
                Check("prepared after late fallback", group, horizontalHeavy, b, rule, true, true);
                Check("throwing prepared after binding", group, invalid, a, rule, true, false);
                Check("raw recovery after prepared exception", group, b, a, rule, false, false);
                Check("bind after prepared exception", group, a, horizontalHeavy, rule, true, true);
                Check("throwing raw after binding", group, invalid, a, rule, false, false);
                Check("prepared recovery after raw exception", group, b, a, rule, true, true);
                Check("zero edge range resets record counters", group, horizontalOnly, b, rule, true, false);
                Check("raw after zero edge range", group, a, b, rule, false, false);
            }
            foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
            {
                Check("invalid rule resets records", group, a, b, (PathFillRule)invalidRule, true, false);
                Check("invalid rule with nulls", group, null, null, (PathFillRule)invalidRule, true, false);
            }
        }

        // Caller mutation includes inputs that later use shipping fallback. Reference arrays
        // are independent copies, so a retained caller array cannot masquerade as a snapshot.
        foreach (var pair in pairs)
        {
            pair.First?.PoisonSource();
            pair.Second?.PoisonSource();
        }
        var afterMutation = new Engines(true, true, true);
        foreach (var pair in pairs)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            Check(pair.Name + " after source mutation", afterMutation, pair.First, pair.Second, rule, true, pair.Binds);

        // Prepared arrays alone are shared; each thread owns all mutable engine state.
        var outcomes = new string?[32];
        Parallel.For(0, outcomes.Length, i =>
        {
            try
            {
                var group = new Engines((i & 1) != 0, (i & 2) != 0, (i & 4) != 0,
                    area: (i & 8) != 0, coalesce: (i & 16) != 0, active: (i & 8) == 0);
                Snapshot first = (i & 1) == 0 ? horizontalHeavy : b;
                Snapshot second = (i & 1) == 0 ? a : horizontalHeavy;
                Compare("concurrent shared scalar snapshot", group, first, second,
                    (i & 2) == 0 ? PathFillRule.NonZero : PathFillRule.EvenOdd, true, true);
            }
            catch (Exception error) { outcomes[i] = error.ToString(); }
        });
        for (int i = 0; i < outcomes.Length; i++)
        {
            Require(outcomes[i] is null, "Concurrent scalar-view control failed: " + outcomes[i]);
            checks++;
        }
        Console.WriteLine($"Prepared scalar views: {checks} result/certificate/diagnostic, record-count and lifetime controls.");

        void Check(string name, Engines group, Snapshot? first, Snapshot? second, PathFillRule rule, bool prepared, bool binds)
        {
            Compare(name, group, first, second, rule, prepared, binds);
            checks++;
        }
    }

    private sealed class Engines
    {
        internal readonly GuardedDoubleSweep Raw, Copied, Hybrid, Direct, DirectWithFlag;
        internal readonly bool Filter;
        internal Engines(bool roi, bool cache, bool filter, bool area = true, bool coalesce = true, bool active = true)
        {
            Filter = filter;
            Raw = new(roi, cache, filter, false, area, coalesce, active);
            Copied = new(roi, cache, filter, false, area, coalesce, active);
            Hybrid = new(roi, cache, filter, false, area, coalesce, active, true);
            Direct = new(roi, cache, filter, true, area, coalesce, active);
            DirectWithFlag = new(roi, cache, filter, true, area, coalesce, active, true);
        }
    }

    private static void Compare(string name, Engines group, Snapshot? first, Snapshot? second, PathFillRule rule,
        bool prepared, bool binds)
    {
        long[] beforeFirst = Bits(first?.Original), beforeSecond = Bits(second?.Original);
        Result raw = Capture(group.Raw, first, second, rule, false);
        Require(group.Raw.PreparedScalarRecordsCopied == 0 && group.Raw.PreparedScalarRecordsBorrowed == 0,
            name + ": raw reference reported prepared scalar records.");
        int expectedRecords = prepared && binds && group.Filter ? first!.Prepared.EdgeCount + second!.Prepared.EdgeCount : 0;
        foreach (var (engine, borrows) in new[] { (group.Copied, false), (group.Hybrid, true), (group.Direct, true), (group.DirectWithFlag, true) })
        {
            Result actual = Capture(engine, first, second, rule, prepared);
            Require(raw.Scalar == actual.Scalar && raw.Diagnostics.SequenceEqual(actual.Diagnostics),
                name + ": scalar views changed result, radius, exception, fallback or existing diagnostics.\nRaw: " + raw.Scalar + "\nActual: " + actual.Scalar);
            Require(engine.PreparedScalarRecordsCopied == (borrows ? 0 : expectedRecords) &&
                engine.PreparedScalarRecordsBorrowed == (borrows ? expectedRecords : 0),
                name + ": copied/borrowed scalar records do not match the actual prepared query and filter mode.");
        }
        Require(beforeFirst.SequenceEqual(Bits(first?.Original)) && beforeSecond.SequenceEqual(Bits(second?.Original)),
            name + ": changed caller coordinate arrays.");
    }

    private readonly record struct ScalarResult(long ValueBits, long RadiusBits, bool Fallback, string? Reason,
        Type? ErrorType, string? ErrorParameter);
    private readonly record struct Result(ScalarResult Scalar, long[] Diagnostics);
    private static Result Capture(GuardedDoubleSweep engine, Snapshot? first, Snapshot? second, PathFillRule rule, bool prepared)
    {
        double value = 0;
        Exception? error = null;
        try
        {
            value = prepared ? engine.MeasureIntersection(first?.Prepared!, second?.Prepared!, rule) :
                engine.MeasureIntersection(first?.Original!, second?.Original!, rule);
        }
        catch (Exception caught) { error = caught; }
        return new(new(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(engine.LastErrorBound),
            engine.LastUsedFallback, engine.LastFallbackReason, error?.GetType(), (error as ArgumentException)?.ParamName),
        [
            engine.BandCount, engine.EventCount, engine.PeakActiveCount, engine.ActiveEdgeVisits, engine.WorkCount,
            engine.XEvaluationCount, engine.XCacheHitCount, engine.FilterAttemptCount, engine.FilterAcceptedCount, engine.FilterIntervalCount,
            engine.GapContributionCount, engine.GapIntegrationCount, engine.GapMergedCount, engine.HorizontalDifferenceEvaluationCount,
            engine.NoCrossingBandCount, engine.CrossingBandCount, engine.BandInitializationVisits, engine.TopOrderWrites, engine.TopOrderVerificationVisits
        ]);
    }

    private sealed class Snapshot
    {
        internal readonly Point2[] Source, Original;
        internal readonly GuardedDoubleSweep.PreparedPath Prepared;
        internal Snapshot(Point2[] points)
        {
            Source = (Point2[])points.Clone();
            Original = (Point2[])points.Clone();
            Prepared = GuardedDoubleSweep.PreparePath(Source);
        }
        internal void PoisonSource()
        {
            for (int i = 0; i < Source.Length; i++) Source[i] = new(double.NaN, i);
        }
    }

    private static long[] Bits(Point2[]? path) => path is null ? [] : path.SelectMany(p => new[]
        { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
    private static Point2[] Rectangle(double left, double bottom, double right, double top) =>
        [new(left, bottom), new(right, bottom), new(right, top), new(left, top)];
    private static Point2[] Subdivide(Point2[] path, int parts) => Enumerable.Range(0, path.Length)
        .SelectMany(i => Enumerable.Range(0, parts).Select(j => new Point2(
            path[i].X + (path[(i + 1) % path.Length].X - path[i].X) * j / parts,
            path[i].Y + (path[(i + 1) % path.Length].Y - path[i].Y) * j / parts))).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
