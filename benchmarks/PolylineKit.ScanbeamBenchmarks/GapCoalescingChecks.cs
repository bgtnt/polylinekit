using System.Numerics;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Independent area and lifecycle controls for merging contiguous filled-gap contributions.</summary>
internal static class GapCoalescingChecks
{
    internal static void Run()
    {
        int checks = 0, certified = 0, fallback = 0, demonstratedReductions = 0;
        Point2[] box = Rectangle(0, 0, 12, 16);
        Point2[] inner = Rectangle(2, 0, 10, 16);
        Point2[] hole = Rectangle(4, 4, 8, 12);
        Point2[] wider = Rectangle(-1, 4, 13, 12);
        Point2[] bowtie = [new(-4, -4), new(4, 4), new(4, -4), new(-4, 4)];
        var affine = PersistentAffine(16);
        var uncertainCrossing = LongCrossingWedges();
        (string Name, Point2[] A, Point2[] B, double NonZero, double EvenOdd, bool MustCertify)[] cases =
        [
            ("persistent affine gap", affine.A, affine.B, 80, 80, true),
            // At Y=0 the same two A edges remain adjacent. B's horizontal step changes
            // their gap from filled to unfilled inside the common-Y range [-5,5].
            ("unchanged boundary pair changes fill", Rectangle(-1, -10, 1, 10),
                [new(-4, -5), new(4, -5), new(4, 0), new(-2, 0), new(-2, 5), new(-4, 5)], 10, 10, true),
            // The same A pair is filled on [-5,-2], empty on [-2,2], and filled
            // again on [2,5]. A stale pending finish would wrongly bridge the empty middle.
            ("unchanged boundary pair filled-empty-filled", Rectangle(-1, -10, 1, 10),
                [new(-4, -5), new(4, -5), new(4, -2), new(-2, -2), new(-2, 2), new(4, 2), new(4, 5), new(-4, 5)],
                12, 12, true),
            ("neighbor replaced and restored", SubdivideLeft(box, 32),
                [new(2, 0), new(10, 0), new(10, 4), new(6, 4), new(6, 12), new(10, 12), new(10, 16), new(2, 16)],
                96, 96, true),
            ("temporary hole splits and rejoins gap", Join(box, hole.Reverse().ToArray()), inner, 96, 96, false),
            ("same winding inner loop differs by rule", Join(box, hole), inner, 128, 96, false),
            ("same adjacent pair changes parity only", Join(box, wider), inner, 128, 64, false),
            ("same adjacent pair loses and regains fill", Join(box, wider.Reverse().ToArray()), inner, 64, 64, false),
            ("separated filled intervals with same boundaries", Join(Rectangle(0, 2, 12, 4),
                Rectangle(0, 6, 12, 10), Rectangle(0, 12, 12, 14)), inner, 64, 64, false),
            ("crossing changes adjacent pairs", bowtie, CrossingContainer(), 32, 32, true),
            ("uncertain crossing height must stay local", uncertainCrossing.A, uncertainCrossing.B,
                Math.ScaleB(1, 35), Math.ScaleB(1, 35), true),
            ("doubled traversal", Join(inner, inner), box, 128, 0, false),
            ("opposite traversal", Join(inner, inner.Reverse().ToArray()), box, 0, 0, false),
            ("ROI begins and ends within unrelated edge spans", SubdivideLeft(box, 32), Rectangle(2, 3, 10, 13), 80, 80, true),
            ("shared side produces zero", box, Rectangle(12, 0, 20, 16), 0, 0, true),
            ("horizontal-only input produces zero", [new(0, 3), new(4, 3), new(8, 3)], box, 0, 0, true)
        ];

        // Exercise the new integration schedule through arrays and both prepared storage
        // modes. Common-Y restriction can otherwise hide a missing flush at one end.
        foreach (bool roi in new[] { false, true })
        foreach (int storage in new[] { 0, 1, 2 })
        {
            var baseline = new GuardedDoubleSweep(roi, true, true, storage == 2, true);
            var merged = new GuardedDoubleSweep(roi, true, true, storage == 2, true, true);
            foreach (var item in cases)
            foreach (var pair in Variants(item.A, item.B))
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                Check(item.Name, pair.A, pair.B, rule, Dyadic.From(rule == PathFillRule.NonZero ? item.NonZero : item.EvenOdd),
                    baseline, merged, storage, item.MustCertify);

            foreach (int divisions in new[] { 4, 16, 64, 256 })
            {
                var pair = PersistentAffine(divisions);
                foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                {
                    Check("many unrelated endpoint levels " + divisions, pair.A, pair.B, rule, Dyadic.From(80),
                        baseline, merged, storage, true, true);
                    Check("swapped persistent gap " + divisions, pair.B, pair.A, rule, Dyadic.From(80),
                        baseline, merged, storage, true, true);
                }
            }

            // All source values are small dyadics, so these input scalings are exact even
            // when their area is subnormal or below the least representable positive area.
            // A success must enclose the unrounded exact area, including a positive value
            // that would round to zero; fallback keeps the original shipping contract.
            foreach (int exponent in new[] { -600, -537, -500, -100, 200, 300 })
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                Check("power-of-two persistent gap " + exponent, Scale(affine.A, exponent), Scale(affine.B, exponent), rule,
                    Dyadic.From(80).Scale(2 * exponent), baseline, merged, storage);
            foreach (double offset in new[] { Math.ScaleB(1, 32), Math.ScaleB(1, 40) })
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                Check("exactly representable large translation", Translate(affine.A, offset, -offset),
                    Translate(affine.B, offset, -offset), rule, Dyadic.From(80), baseline, merged, storage);

            // Changing call shape, rule and storage must not retain a previous call's
            // pending contribution. Simultaneous crossings force whole-operation fallback
            // after earlier bands have already emitted filled intervals.
            Point2[] simultaneous = [new(-14, -4), new(-6, 4), new(-6, -4), new(-14, 4), new(-14, -4),
                new(6, -4), new(14, 4), new(14, -4), new(6, 4), new(6, -4)];
            Point2[] outer = CrossingContainer(20);
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Check("before pending-gap fallback", affine.A, affine.B, PathFillRule.NonZero, Dyadic.From(80),
                    baseline, merged, storage, true, true);
                Check("fallback abandons pending gaps", simultaneous, outer, PathFillRule.NonZero, Dyadic.From(64),
                    baseline, merged, storage);
                Require(merged.LastUsedFallback && merged.EventCount > 0 && merged.GapContributionCount > 0,
                    "The reset control must reach whole-call fallback after an actual pending-gap stream.");
                checks++;
                Check("after pending-gap fallback", affine.B, affine.A, PathFillRule.EvenOdd, Dyadic.From(80),
                    baseline, merged, storage, true, true);
                int otherStorage = storage == 0 ? 1 : 0;
                Check("alternate overload after coalescing", inner, box, PathFillRule.NonZero, Dyadic.From(128),
                    baseline, merged, otherStorage, true);
            }
        }

        // Flag combinations cannot change the geometry proof. This smaller grid keeps the
        // broader lifecycle cases above bounded while exercising both arithmetic paths and
        // both endpoint-order options with genuine long-lived contributions.
        foreach (bool roi in new[] { false, true })
        foreach (bool cache in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        foreach (bool optimizedArea in new[] { false, true })
        {
            var baseline = new GuardedDoubleSweep(roi, cache, filter, optimizeAreaArithmetic: optimizedArea);
            var merged = new GuardedDoubleSweep(roi, cache, filter, optimizeAreaArithmetic: optimizedArea, coalesceGaps: true);
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                Check("independent flags", affine.A, affine.B, rule, Dyadic.From(80), baseline, merged, 0, true, true);
        }

        Require(certified > 100 && fallback > 0 && demonstratedReductions > 20,
            "Gap controls must exercise independent certificates, actual fallback and demonstrated integration reductions.");
        Console.WriteLine($"Gap coalescing: {checks} controls; {certified} exact-area certificates, {fallback} shipping fallbacks, {demonstratedReductions} integration reductions.");

        void Check(string name, Point2[] first, Point2[] second, PathFillRule rule, Dyadic exact,
            GuardedDoubleSweep baseline, GuardedDoubleSweep merged, int storage, bool mustCertify = false, bool mustReduce = false)
        {
            long[] firstBits = PointBits(first), secondBits = PointBits(second);
            double before = Measure(baseline, first, second, rule, storage);
            double actual = Measure(merged, first, second, rule, storage);
            Require(firstBits.SequenceEqual(PointBits(first)) && secondBits.SequenceEqual(PointBits(second)), name + ": mutated input.");
            Validate(name + " baseline", baseline, before, exact, first, second, rule);
            Validate(name + " coalesced", merged, actual, exact, first, second, rule);
            if (merged.LastUsedFallback) fallback++; else certified++;
            Require(!mustCertify || (!baseline.LastUsedFallback && !merged.LastUsedFallback),
                name + ": ordinary lifecycle control must certify in both modes (baseline=" + baseline.LastFallbackReason + ", merged=" + merged.LastFallbackReason + ").");
            if (!baseline.LastUsedFallback && !merged.LastUsedFallback)
            {
                Dyadic difference = (Dyadic.From(actual) - Dyadic.From(before)).Abs();
                Require(difference.CompareTo(Dyadic.From(baseline.LastErrorBound) + Dyadic.From(merged.LastErrorBound)) <= 0,
                    name + ": results differ by more than their combined certified radii.");
                Require(baseline.BandCount == merged.BandCount && baseline.EventCount == merged.EventCount &&
                    baseline.PeakActiveCount == merged.PeakActiveCount && baseline.ActiveEdgeVisits == merged.ActiveEdgeVisits &&
                    baseline.FilterAttemptCount == merged.FilterAttemptCount && baseline.FilterAcceptedCount == merged.FilterAcceptedCount &&
                    baseline.FilterIntervalCount == merged.FilterIntervalCount && baseline.XEvaluationCount == merged.XEvaluationCount &&
                    baseline.XCacheHitCount == merged.XCacheHitCount, name + ": coalescing changed completed sweep topology or ordering decisions.");
                Require(baseline.GapMergedCount == 0 && baseline.GapContributionCount == baseline.GapIntegrationCount &&
                    merged.GapContributionCount == baseline.GapContributionCount &&
                    merged.GapContributionCount == merged.GapIntegrationCount + merged.GapMergedCount,
                    name + ": emitted/integrated/merged contribution accounting differs.");
                if (mustReduce)
                {
                    Require(merged.GapMergedCount > 0 && merged.GapIntegrationCount < baseline.GapIntegrationCount &&
                        merged.HorizontalDifferenceEvaluationCount < baseline.HorizontalDifferenceEvaluationCount,
                        name + ": ordinary persistent gaps did not reduce actual integration work.");
                    demonstratedReductions++;
                }
            }
            checks++;
        }
    }

    private static void Validate(string name, GuardedDoubleSweep engine, double actual, Dyadic exact,
        Point2[] first, Point2[] second, PathFillRule rule)
    {
        Require(double.IsFinite(actual) && actual >= 0, name + ": invalid area.");
        if (engine.LastUsedFallback)
        {
            Require(!string.IsNullOrWhiteSpace(engine.LastFallbackReason) && double.IsNaN(engine.LastErrorBound),
                name + ": fallback claimed a certificate or omitted its reason.");
            Require(Bits(actual) == Bits(WindingArea.IntersectionArea(first, second, rule)),
                name + ": whole-call fallback differs from the original inputs' shipping result.");
        }
        else
        {
            Require(engine.LastFallbackReason is null && double.IsFinite(engine.LastErrorBound) && engine.LastErrorBound >= 0 &&
                engine.LastErrorBound <= Math.Min(.25, 1e-10 * Math.Abs(actual)), name + ": invalid certificate budget.");
            Require((Dyadic.From(actual) - exact).Abs().CompareTo(Dyadic.From(engine.LastErrorBound)) <= 0,
                name + ": certified radius does not enclose the independent exact area.");
            if (actual == 0) Require(engine.LastErrorBound == 0, name + ": zero success is not exact.");
        }
    }

    private static double Measure(GuardedDoubleSweep engine, Point2[] first, Point2[] second, PathFillRule rule, int storage) =>
        storage == 0 ? engine.MeasureIntersection(first, second, rule) :
            engine.MeasureIntersection(GuardedDoubleSweep.PreparePath(first), GuardedDoubleSweep.PreparePath(second), rule);

    private static (Point2[] A, Point2[] B) PersistentAffine(int divisions)
    {
        var a = new List<Point2> { new(0, 0), new(8, 0), new(10, 16), new(0, 16) };
        for (int i = divisions - 1; i > 0; i--) a.Add(new(0, 16.0 * i / divisions));
        var b = new List<Point2> { new(2, 0), new(20, 0) };
        for (int i = 1; i <= divisions; i++) b.Add(new(20, 16.0 * i / divisions));
        b.Add(new(6, 16));
        return (a.ToArray(), b.ToArray());
    }

    private static Point2[] CrossingContainer(double width = 6)
    {
        var path = new List<Point2> { new(-width, -5), new(width, -5), new(width, 5), new(-width, 5) };
        // No endpoint at Y=0: the bowtie crossing lies strictly inside a band.
        for (int i = 19; i >= 0; i--) path.Add(new(-width, -4.75 + .5 * i));
        return path.ToArray();
    }

    private static (Point2[] A, Point2[] B) LongCrossingWedges()
    {
        double width = Math.ScaleB(1, 14), height = Math.ScaleB(1, 20), origin = Math.ScaleB(1, 40);
        // Top/bottom wedges have width zero at their crossing and exact total area
        // 2*width*height = 2^35. Construction of crossing Y near 2^40 has a nonpoint
        // enclosure. Extending either adjacent one-unit band to a height near 2^20
        // amplifies that uncertainty by a width near 2^14, despite unchanged geometry.
        // Only unrelated point-boundary pieces farther from the crossing may be merged.
        Point2[] bowtie = [new(-width, origin - height), new(width, origin + height),
            new(-width, origin + height), new(width, origin - height)];
        var outer = new List<Point2>
        {
            new(-2 * width, origin - height - 16), new(2 * width, origin - height - 16),
            new(2 * width, origin + height + 16), new(-2 * width, origin + height + 16)
        };
        for (int i = 15; i > 0; i--) outer.Add(new(-2 * width, origin + height * i / 16));
        outer.Add(new(-2 * width, origin + 1));
        outer.Add(new(-2 * width, origin - 1));
        for (int i = 1; i < 16; i++) outer.Add(new(-2 * width, origin - height * i / 16));
        return (bowtie, outer.ToArray());
    }

    private static Point2[] SubdivideLeft(Point2[] rectangle, int divisions)
    {
        var path = rectangle.ToList();
        double low = rectangle[0].Y, high = rectangle[3].Y;
        for (int i = divisions - 1; i > 0; i--) path.Add(new(rectangle[0].X, low + (high - low) * i / divisions));
        return path.ToArray();
    }

    private static Point2[] Rectangle(double left, double bottom, double right, double top) =>
        [new(left, bottom), new(right, bottom), new(right, top), new(left, top)];
    private static Point2[] Join(params Point2[][] loops) => loops.SelectMany(loop => loop.Append(loop[0])).ToArray();
    private static Point2[] Scale(Point2[] path, int exponent) => path.Select(p => new Point2(Math.ScaleB(p.X, exponent), Math.ScaleB(p.Y, exponent))).ToArray();
    private static Point2[] Translate(Point2[] path, double x, double y) => path.Select(p => new Point2(p.X + x, p.Y + y)).ToArray();
    private static long[] PointBits(Point2[] path) => path.SelectMany(p => new[] { Bits(p.X), Bits(p.Y) }).ToArray();
    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static IEnumerable<(Point2[] A, Point2[] B)> Variants(Point2[] a, Point2[] b)
    {
        yield return (a, b);
        yield return (b, a);
        yield return (a.Reverse().ToArray(), b);
        yield return (a, b.Reverse().ToArray());
        yield return (a.Skip(1).Concat(a.Take(1)).ToArray(), b.Skip(1).Concat(b.Take(1)).ToArray());
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Area expectations are analytic integers scaled by exact powers of two. Compare the
    // unrounded binary64 geometry/result as dyadics rather than rounding a tiny oracle to zero.
    private readonly record struct Dyadic(BigInteger Integer, int Exponent) : IComparable<Dyadic>
    {
        internal static Dyadic From(double value)
        {
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            int encoded = (int)((bits >> 52) & 2047);
            BigInteger integer = bits & ((1UL << 52) - 1);
            if (encoded != 0) integer += BigInteger.One << 52;
            if ((bits >> 63) != 0) integer = -integer;
            return new(integer, encoded == 0 ? -1074 : encoded - 1075);
        }
        internal Dyadic Scale(int exponent) => new(Integer, Exponent + exponent);
        internal Dyadic Abs() => new(BigInteger.Abs(Integer), Exponent);
        public static Dyadic operator +(Dyadic a, Dyadic b)
        {
            int exponent = Math.Min(a.Exponent, b.Exponent);
            return new((a.Integer << (a.Exponent - exponent)) + (b.Integer << (b.Exponent - exponent)), exponent);
        }
        public static Dyadic operator -(Dyadic a, Dyadic b) => a + new Dyadic(-b.Integer, b.Exponent);
        public int CompareTo(Dyadic other)
        {
            int exponent = Math.Min(Exponent, other.Exponent);
            return (Integer << (Exponent - exponent)).CompareTo(other.Integer << (other.Exponent - exponent));
        }
    }
}
