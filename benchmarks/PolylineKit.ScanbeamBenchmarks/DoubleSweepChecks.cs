using System.Numerics;
using PolylineKit;
using PolylineKit.ActiveSweep;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Independent numerical and fallback controls for the experimental double sweep.</summary>
internal static class DoubleSweepChecks
{
    private static int passed, certified, fallback;

    internal static void Run()
    {
        passed = certified = fallback = 0;
        var engine = new GuardedDoubleSweep();
        Point2[] a = [new(-4, -3), new(5, 1), new(0, 6)];
        Point2[] b = [new(-3, 2), new(4, -4), new(7, 5)];
        Point2[] contained = [new(-.5, .25), new(.75, .75), new(.25, 1.5)];

        // Six distinct endpoint levels, no axis-aligned sides or coincident vertices.
        // These are mandatory certified successes, not correctness supplied by fallback.
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            CheckExact("generic crossing fast path", a, b, rule, ExactConvex(a, b), true);
            CheckExact("generic containment fast path", a, contained, rule, ExactConvex(a, contained), true);
        }
        PairVariants("generic crossings", a, b, ExactConvex(a, b));
        PairVariants("generic containment", a, contained, ExactConvex(a, contained));
        PairVariants("subdivided generic sides", Subdivide(a), Subdivide(b), ExactConvex(a, b));
        PairVariants("optional closure", [.. a, a[0]], [.. b, b[0]], ExactConvex(a, b));
        PairVariants("consecutive repeated vertices", [a[0], a[0], a[1], a[2]], b, ExactConvex(a, b));

        Point2[] square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];
        Point2[] closed = [.. square, square[0]];
        Point2[] inner = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        Point2[] innerClosed = [.. inner, inner[0]];
        Point2[] bowtie = [new(0, 0), new(4, 4), new(4, 0), new(0, 4)];
        (string Name, Point2[] A, Point2[] B, double NonZero, double EvenOdd)[] analytic =
        [
            ("identity", square, square, 16, 16),
            ("nested", square, inner, 4, 4),
            ("disjoint", square, Shift(square, 10, 9), 0, 0),
            ("shared side", square, Shift(square, 4, 0), 0, 0),
            ("shared corner", square, Shift(square, 4, 4), 0, 0),
            ("partial collinear overlap", square, Shift(square, 2, 0), 8, 8),
            ("oblique supports with different endpoints", Oblique(square), Oblique(Shift(square, 2, 0)), 16, 16),
            ("nonvertex oblique tangency", Oblique(square), Oblique([new(2, 0), new(1, -2), new(3, -1)]), 0, 0),
            ("nonvertex oblique endpoint inside", Oblique(square), Oblique([new(2, 0), new(3, 2), new(1, 2)]), 4, 4),
            ("crossing with shared vertices", [new(0, 0), new(1, 0), new(0, 1)],
                [new(0, 0), new(1, 1), new(1, 0)], .25, .25),
            ("doubled winding", [.. closed, .. closed], square, 16, 0),
            ("opposite retracing", [.. closed, .. closed.Reverse()], square, 0, 0),
            ("nested same winding", [.. closed, .. innerClosed], inner, 4, 0),
            ("hole", [.. closed, .. innerClosed.Reverse()], inner, 0, 0),
            ("bowtie identity", bowtie, bowtie, 8, 8),
            ("bowtie contained", bowtie, square, 8, 8),
            ("collinear zero area", [new(-2, 1), new(7, 1), new(3, 1)], square, 0, 0),
            ("repeated two positions", [new(0, 0), new(2, 1), new(0, 0), new(2, 1)], square, 0, 0),
            ("tiny unit translated to 2^52", Shift([new(0, 0), new(1, 0), new(0, 1)], Math.ScaleB(1, 52), Math.ScaleB(1, 52)),
                Shift([new(0, 0), new(1, 0), new(0, 1)], Math.ScaleB(1, 52), Math.ScaleB(1, 52)), .5, .5)
        ];
        foreach (var item in analytic)
        foreach (var pair in Variants(item.A, item.B))
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            CheckExact(item.Name, pair.A, pair.B, rule, R.From(rule == PathFillRule.NonZero ? item.NonZero : item.EvenOdd));

        // The exact convex oracle interprets the actual rounded binary64 coordinates. It
        // therefore does not assume an ideal translation/scaling that the input cannot express.
        foreach (int exponent in new[] { -600, -537, -500, -100, 40, 200, 300 })
        {
            double scale = Math.ScaleB(1, exponent);
            Point2[] first = Scale(square, scale), second = Scale(Shift(square, 2, 1), scale);
            PairVariants("power-of-two scale " + exponent, first, second, ExactConvex(first, second));
        }
        double tinySide = Math.ScaleB(1, -537);
        Point2[] tinySquare = [new(0, 0), new(tinySide, 0), new(tinySide, tinySide), new(0, tinySide)];
        PairVariants("least-subnormal area", tinySquare, tinySquare, R.From(double.Epsilon));
        foreach (double offset in new[] { 1e8, 1e12, Math.ScaleB(1, 48) })
        {
            Point2[] first = Shift(a, offset, -offset), second = Shift(b, offset, -offset);
            // Shipping crossing coordinates have a documented offset-dependent error. The
            // fallback obligation here is bitwise compatibility, not a new tighter contract.
            // Every fast success still must certify the true unrounded rational area.
            PairVariants("large offset " + offset, first, second, ExactConvex(first, second), checkFallbackAccuracy: false);
        }
        double largeInteger = Math.ScaleB(1, 40);
        Point2[] determinantOne = [new(0, 0), new(largeInteger, largeInteger - 1), new(largeInteger + 1, largeInteger)];
        PairVariants("large determinant-one triangle", determinantOne, determinantOne, R.From(.5));
        PairVariants("distant full-range regions", Shift(square, -1e90, 0), Shift(square, 1e90, 0), R.Zero,
            expectShippingError: true); // Finite coordinates can still collapse to fewer than three vertices.
        Point2[] huge = [new(-1e100, -1), new(1e100, -1), new(1e100, 1), new(-1e100, 1)];
        Point2[] narrow = [new(0, -2), new(1e-250, -2), new(1e-250, 2), new(0, 2)];
        PairVariants("sub-edge ratio underflow", huge, narrow, R.From(1e-250) * R.From(2));

        // Existing independent rational slab oracle checks self-overlapping identities.
        // These are not used as proof that any fast path was exercised.
        Point2[] multiway = [new(-6, -6), new(6, 6), new(-6, 6), new(6, -6),
            new(0, -8), new(0, 8), new(-8, 0), new(8, 0)];
        Point2[] simultaneousSeparate = [new(-14, -4), new(-6, 4), new(-6, -4), new(-14, 4), new(-14, -4),
            new(6, -4), new(14, 4), new(14, -4), new(6, 4), new(6, -4)];
        foreach (Point2[] path in new[] { multiway, simultaneousSeparate, bowtie, closed.Concat(closed).ToArray() })
        {
            var exact = ExactAreaOracle.Measure(path);
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                CheckRoundedOracle("exact slab identity", path, path, rule,
                    rule == PathFillRule.NonZero ? exact.NonZero : exact.EvenOdd);
        }

        // Thirty independent convex intersections. The exact clipping oracle below uses
        // BigInteger rationals and half-planes, with no sweep/event ordering or runtime predicates.
        for (int seed = 0; seed < 30; seed++)
        {
            var random = new Random(413_083 + seed * 23);
            Point2[] first = Triangle(random), second = Triangle(random);
            R expected = ExactConvex(first, second);
            Point2[] joined = [.. first, first[0], .. second, second[0]];
            double union = ExactAreaOracle.Measure(joined).NonZero;
            double unionDerived = Area(first).ToDouble() + Area(second).ToDouble() - union;
            // A second oracle is rounded before inclusion-exclusion, so permit only that
            // rounding here. The fast-result enclosure itself is checked against exact R.
            Require(Math.Abs(unionDerived - expected.ToDouble()) <= 1e-12,
                "Independent clipping and slab triangle oracles disagree.");
            PairVariants("exact triangle pair " + seed, first, second, expected);
        }

        // The operation budget is based on supplied count, while shipping accepts the
        // equivalent triangle after duplicate removal. Exercise both per-array and combined
        // limits without introducing a costly large self-overlapping geometry.
        foreach (int suppliedCount in new[] { 8190, 8193 })
        {
            Point2[] repeatedTriangle = new Point2[suppliedCount];
            Array.Fill(repeatedTriangle, a[0]);
            repeatedTriangle[^2] = a[1]; repeatedTriangle[^1] = a[2];
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                CheckExact("vertex-budget triangle", repeatedTriangle, b, rule, ExactConvex(a, b));
                Require(engine.LastUsedFallback && engine.LastFallbackReason == "vertex-budget",
                    "The supplied-count budget must use whole-operation fallback.");
            }
        }

        CheckContract(engine, square);
        long[] beforeA = Bits(a), beforeB = Bits(b);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            CheckExact("fast after fallback", a, b, PathFillRule.NonZero, ExactConvex(a, b), true);
            CheckExact("fallback between fast calls", huge, narrow, PathFillRule.NonZero, R.From(1e-250) * R.From(2));
            CheckExact("fast fill reset", a, b, PathFillRule.EvenOdd, ExactConvex(a, b), true);
        }
        Require(beforeA.SequenceEqual(Bits(a)) && beforeB.SequenceEqual(Bits(b)), "Sweep changed caller arrays.");
        Require(certified >= 4, "The double sweep controls cannot pass using fallback for every ordinary case.");
        Require(fallback > 0, "The controls must also exercise whole-call fallback.");
        Console.WriteLine($"Guarded double sweep: {passed} assertions; {certified} certified and {fallback} fallback calls.");

        void PairVariants(string name, Point2[] first, Point2[] second, R exact, bool expectShippingError = false, bool checkFallbackAccuracy = true)
        {
            foreach (var pair in Variants(first, second))
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                if (expectShippingError) SameOutcome(engine, pair.A, pair.B, rule);
                else CheckExact(name, pair.A, pair.B, rule, exact, checkFallbackAccuracy: checkFallbackAccuracy);
        }

        void CheckExact(string name, Point2[] first, Point2[] second, PathFillRule rule, R exact, bool mustCertify = false, bool checkFallbackAccuracy = true)
        {
            long[] beforeFirst = Bits(first), beforeSecond = Bits(second);
            double actual = engine.MeasureIntersection(first, second, rule);
            CheckDiagnostics(name, first, second, rule, actual, mustCertify);
            Require(double.IsFinite(actual) && actual >= 0, name + ": invalid area.");
            Require(beforeFirst.SequenceEqual(Bits(first)) && beforeSecond.SequenceEqual(Bits(second)), name + ": mutated input.");
            if (!engine.LastUsedFallback)
            {
                R error = R.Abs(R.From(actual) - exact);
                Require(error <= R.From(engine.LastErrorBound), name + ": the certified error bound does not enclose the exact area.");
            }
            else if (checkFallbackAccuracy) RoundedNear(name, exact.ToDouble(), actual);
        }

        void CheckRoundedOracle(string name, Point2[] first, Point2[] second, PathFillRule rule, double expected)
        {
            double actual = engine.MeasureIntersection(first, second, rule);
            CheckDiagnostics(name, first, second, rule, actual, false);
            RoundedNear(name, expected, actual);
            // ExactAreaOracle returned the correctly rounded area. Do not pretend it
            // supplies the unrounded rational needed for a strict LastErrorBound test.
        }

        void CheckDiagnostics(string name, Point2[] first, Point2[] second, PathFillRule rule, double actual, bool mustCertify)
        {
            if (engine.LastUsedFallback)
            {
                fallback++;
                Require(!mustCertify, name + ": ordinary case unexpectedly used fallback (" + engine.LastFallbackReason + ").");
                Require(!string.IsNullOrWhiteSpace(engine.LastFallbackReason), name + ": fallback has no reason.");
                Require(double.IsNaN(engine.LastErrorBound), name + ": fallback must not claim a certified Winding error bound.");
                double shipping = WindingArea.IntersectionArea(first, second, rule);
                Require(BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(shipping), name + ": fallback differs from shipping operation.");
            }
            else
            {
                certified++;
                Require(engine.LastFallbackReason is null, name + ": stale fallback reason on certified success.");
                double error = engine.LastErrorBound;
                Require(double.IsFinite(error) && error >= 0 && error <= Math.Min(.25, 1e-10 * Math.Abs(actual)), name + ": certificate exceeds the frozen area budget.");
                if (actual == 0) Require(error == 0, name + ": zero result requires an exact zero enclosure.");
            }
        }
    }

    private static void CheckContract(GuardedDoubleSweep engine, Point2[] square)
    {
        Point2[]?[] invalid = [null, [], [new(0, 0)], [new(0, 0), new(1, 0)],
            [new(0, 0), new(0, 0), new(0, 0)], [new(0, 0), new(1, 0), new(0, 0)]];
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        foreach (Point2[]? path in invalid)
        {
            SameOutcome(engine, path, square, rule);
            SameOutcome(engine, square, path, rule);
        }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            Math.BitIncrement(1e100), -Math.BitIncrement(1e100) })
        foreach (bool y in new[] { false, true })
        {
            Point2 p = y ? new(0, bad) : new(bad, 0);
            foreach (Point2[] path in new[] { new[] { p }, new[] { new Point2(0, 0), new Point2(1, 0), p } })
            {
                SameOutcome(engine, path, square, PathFillRule.NonZero);
                SameOutcome(engine, square, path, PathFillRule.NonZero);
                SameOutcome(engine, [new(-1e100, 0), new(0, 0), new(1e100, 0)], path, PathFillRule.NonZero);
            }
        }
        foreach (int invalidRule in new[] { -1, 2, int.MaxValue })
        {
            SameOutcome(engine, square, square, (PathFillRule)invalidRule);
            SameOutcome(engine, null, null, (PathFillRule)invalidRule);
        }
        SameOutcome(engine, [new(-1e100, 0), new(0, 0), new(1e100, 0)], square, PathFillRule.NonZero);
        SameOutcome(engine, [.. square, .. square], square, PathFillRule.NonZero);
        SameOutcome(engine, [.. square, .. square], square, PathFillRule.EvenOdd);
    }

    private static void SameOutcome(GuardedDoubleSweep engine, Point2[]? first, Point2[]? second, PathFillRule rule)
    {
        (double Value, Exception? Error) Capture(Func<double> operation)
        {
            try { return (operation(), null); } catch (Exception error) { return (0, error); }
        }
        var expected = Capture(() => WindingArea.IntersectionArea(first!, second!, rule));
        var actual = Capture(() => engine.MeasureIntersection(first!, second!, rule));
        Require(actual.Error?.GetType() == expected.Error?.GetType(), "Guarded sweep changed the shipping exception type.");
        if (expected.Error is ArgumentException a && actual.Error is ArgumentException b)
            Require(a.ParamName == b.ParamName, "Guarded sweep changed the shipping exception parameter.");
        if (expected.Error is null) RoundedNear("shipping result contract", expected.Value, actual.Value);
    }

    private static IEnumerable<(Point2[] A, Point2[] B)> Variants(Point2[] first, Point2[] second)
    {
        yield return (first, second);
        yield return (second, first);
        yield return (first.Reverse().ToArray(), second);
        yield return (first, second.Reverse().ToArray());
        yield return (first.Reverse().ToArray(), second.Reverse().ToArray());
        yield return (first.Skip(1).Concat(first.Take(1)).ToArray(), second.Skip(1).Concat(second.Take(1)).ToArray());
        yield return (first.Select(p => new Point2(p.Y, p.X)).ToArray(), second.Select(p => new Point2(p.Y, p.X)).ToArray());
    }

    private static long[] Bits(Point2[] path) => path.SelectMany(p => new[]
        { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
    private static Point2[] Shift(Point2[] path, double x, double y) => path.Select(p => new Point2(p.X + x, p.Y + y)).ToArray();
    private static Point2[] Scale(Point2[] path, double scale) => path.Select(p => new Point2(p.X * scale, p.Y * scale)).ToArray();
    // An exact linear transform with determinant -2, making axis-aligned analytic controls oblique.
    private static Point2[] Oblique(Point2[] path) => path.Select(p => new Point2(p.X + p.Y, p.X - p.Y)).ToArray();
    private static Point2[] Subdivide(Point2[] path) => Enumerable.Range(0, path.Length).SelectMany(i =>
        new[] { path[i], new Point2((path[i].X + path[(i + 1) % path.Length].X) * .5,
            (path[i].Y + path[(i + 1) % path.Length].Y) * .5) }).ToArray();
    private static Point2[] Triangle(Random random)
    {
        while (true)
        {
            Point2[] path = Enumerable.Range(0, 3).Select(_ => new Point2(random.Next(-10, 11) * .25, random.Next(-10, 11) * .25)).ToArray();
            R signed = TwiceSignedArea(path.Select(V.From).ToList());
            if (signed.Sign == 0) continue;
            if (signed.Sign < 0) Array.Reverse(path);
            return path;
        }
    }

    private static void RoundedNear(string name, double expected, double actual)
    {
        double ulp = Math.Abs(Math.BitIncrement(expected) - expected);
        Require(double.IsFinite(actual) && actual >= 0 && Math.Abs(actual - expected) <= 64 * ulp,
            $"{name}: expected {expected:R}, got {actual:R} (64 ULP comparison of rounded independent area).");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed++;
    }

    // Independent convex half-plane clipping in exact rationals. It is deliberately small,
    // allocating and slow; it shares no numerical predicates or event machinery with either engine.
    private static R ExactConvex(Point2[] first, Point2[] second)
    {
        List<V> polygon = first.Select(V.From).ToList(), clip = second.Select(V.From).ToList();
        if (TwiceSignedArea(clip).Sign < 0) clip.Reverse();
        for (int edge = 0; edge < clip.Count && polygon.Count != 0; edge++)
        {
            V a = clip[edge], b = clip[(edge + 1) % clip.Count];
            var output = new List<V>();
            V previous = polygon[^1]; R previousSide = Cross(a, b, previous);
            foreach (V current in polygon)
            {
                R side = Cross(a, b, current);
                if ((side.Sign >= 0) != (previousSide.Sign >= 0))
                {
                    R t = previousSide / (previousSide - side);
                    output.Add(new(previous.X + t * (current.X - previous.X), previous.Y + t * (current.Y - previous.Y)));
                }
                if (side.Sign >= 0) output.Add(current);
                previous = current; previousSide = side;
            }
            polygon = output;
        }
        return R.Abs(TwiceSignedArea(polygon)) / R.From(2);
    }
    private static R Area(Point2[] path) => R.Abs(TwiceSignedArea(path.Select(V.From).ToList())) / R.From(2);
    private static R TwiceSignedArea(List<V> points)
    {
        R sum = R.Zero;
        for (int i = 0; i < points.Count; i++)
        {
            V a = points[i], b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - a.Y * b.X;
        }
        return sum;
    }
    private static R Cross(V a, V b, V p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
    private readonly record struct V(R X, R Y)
    {
        internal static V From(Point2 p) => new(R.From(p.X), R.From(p.Y));
    }
    private readonly struct R
    {
        private readonly BigInteger numerator, denominator;
        internal static R Zero => new(BigInteger.Zero, BigInteger.One);
        internal int Sign => numerator.Sign;
        private R(BigInteger n, BigInteger d)
        {
            if (d.IsZero) throw new DivideByZeroException();
            if (d.Sign < 0) { n = -n; d = -d; }
            BigInteger common = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
            numerator = n / common; denominator = d / common;
        }
        internal static R From(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            int exponent = (int)((bits >> 52) & 2047);
            BigInteger significand = bits & ((1UL << 52) - 1);
            if (exponent != 0) significand += BigInteger.One << 52;
            int shift = exponent == 0 ? -1074 : exponent - 1075;
            if ((bits >> 63) != 0) significand = -significand;
            return shift >= 0 ? new(significand << shift, BigInteger.One) : new(significand, BigInteger.One << -shift);
        }
        internal static R Abs(R r) => new(BigInteger.Abs(r.numerator), r.denominator);
        internal double ToDouble()
        {
            if (numerator.IsZero) return 0;
            BigInteger n = BigInteger.Abs(numerator), d = denominator;
            int exponent = checked((int)(n.GetBitLength() - d.GetBitLength()));
            if (exponent >= 0 ? n < (d << exponent) : (n << -exponent) < d) exponent--;
            if (exponent > 1023) return numerator.Sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
            ulong bits;
            if (exponent < -1022) bits = (ulong)Round(n << 1074, d);
            else
            {
                int shift = 52 - exponent;
                BigInteger significant = shift >= 0 ? Round(n << shift, d) : Round(n, d << -shift);
                if (significant == (BigInteger.One << 53)) { significant >>= 1; exponent++; }
                bits = ((ulong)(exponent + 1023) << 52) | ((ulong)significant - (1UL << 52));
            }
            if (numerator.Sign < 0) bits |= 1UL << 63;
            return BitConverter.Int64BitsToDouble(unchecked((long)bits));
        }
        private static BigInteger Round(BigInteger n, BigInteger d)
        {
            BigInteger quotient = BigInteger.DivRem(n, d, out BigInteger remainder);
            int direction = (remainder << 1).CompareTo(d);
            return direction > 0 || (direction == 0 && !quotient.IsEven) ? quotient + 1 : quotient;
        }
        public static R operator +(R a, R b) => new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
        public static R operator -(R a, R b) => new(a.numerator * b.denominator - b.numerator * a.denominator, a.denominator * b.denominator);
        public static R operator *(R a, R b) => new(a.numerator * b.numerator, a.denominator * b.denominator);
        public static R operator /(R a, R b) => new(a.numerator * b.denominator, a.denominator * b.numerator);
        public static bool operator <=(R a, R b) => a.numerator * b.denominator <= b.numerator * a.denominator;
        public static bool operator >=(R a, R b) => a.numerator * b.denominator >= b.numerator * a.denominator;
    }
}
