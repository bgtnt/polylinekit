using System.Numerics;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>
/// Area-value regressions independent of the orientation predicates. These inputs have no proper crossings:
/// the oracle interprets each input double exactly, sums integer shoelace products and rounds only the answer.
/// A correct orientation sign alone does not establish the accuracy of an area term.
/// </summary>
internal static class WindingTermChecks
{
    private static int checkedValues;
    private static readonly List<string> Failures = [];

    public static int Run()
    {
        checkedValues = 0;
        Failures.Clear();
        CheckOracleRounding();
        foreach (double length in new[] { 1e4, 1e8, 1e12, 1e16 })
        {
            foreach (int exponent in new[] { 0, -500 })
            {
                double scale = Math.ScaleB(1, exponent);
                Point2[] triangle = [new(0, 0), new(length * scale, length * scale),
                    new(2 * length * scale, Math.BitIncrement(2 * length) * scale)];
                CheckVariants($"skinny L={length:R} scale=2^{exponent}", triangle);
            }

            // Translate the actual double inputs, then recompute the oracle. At 2^52 the thinness of the
            // first three triangles rounds away; comparing with the untranslated area would be a bad test.
            double origin = Math.ScaleB(1, 52);
            Point2[] translated = [new(origin, origin), new(origin + length, origin + length),
                new(origin + 2 * length, origin + Math.BitIncrement(2 * length))];
            CheckVariants($"skinny L={length:R} translated 2^52", translated);
        }

        foreach (int exponent in new[] { -538, -537, -500, 0, 300 })
        {
            double side = Math.ScaleB(1, exponent);
            Point2[] square = [new(0, 0), new(side, 0), new(side, side), new(0, side)];
            // In particular, area(2^-537 square) is double.Epsilon. Halving an exact determinant
            // before converting/scaling an area term can wrongly turn this representable result into zero.
            Equal($"square 2^{exponent} oracle", Math.ScaleB(1, 2 * exponent), Area(square));
            CheckVariants($"square side=2^{exponent}", square);
        }

        CheckVariants("anisotropic triangle", [new(0, 0), new(Math.ScaleB(1, 300), Math.ScaleB(1, -600)),
            new(Math.ScaleB(1, 301), Math.BitIncrement(Math.ScaleB(1, -599)))]);
        CheckVariants("large origin and tiny height", [new(Math.ScaleB(1, 300), 0),
            new(Math.BitIncrement(Math.ScaleB(1, 300)), 0), new(Math.ScaleB(1, 300), Math.ScaleB(1, -700))]);
        CheckVariants("subnormal-coordinate rectangle", [new(0, 0), new(Math.ScaleB(1, 300), 0),
            new(Math.ScaleB(1, 300), double.Epsilon), new(0, double.Epsilon)]);

        if (Failures.Count != 0)
            throw new InvalidOperationException($"{Failures.Count} of {checkedValues} exact area-value checks failed.\n"
                + string.Join("\n", Failures.Take(20)));
        Console.WriteLine($"PASS: {checkedValues} independent exact area-value checks.");
        return checkedValues;
    }

    private static void CheckVariants(string name, Point2[] source)
    {
        for (int reverse = 0; reverse < 2; reverse++)
        for (int shift = 0; shift < source.Length; shift++)
        {
            Point2[] path = Enumerable.Range(0, source.Length).Select(i => source[(i + shift) % source.Length]).ToArray();
            if (reverse != 0) Array.Reverse(path);
            string label = $"{name}, reverse={reverse}, shift={shift}";
            BigInteger numerator = SignedAreaNumerator(path);
            double signed = RoundDyadic(numerator, -2149), expected = Math.Abs(signed);
            Integrals(label + " closed", WindingArea.ClosedPath(path), expected, signed);

            // Following all vertices against the direct start-to-end chord closes exactly this polygon.
            Integrals(label + " bridged", WindingArea.EndpointBridged(path, [path[0], path[^1]]), expected, signed);
            foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
            {
                var self = WindingArea.FilledRegions(path, path, rule);
                string selfLabel = $"{label} self {rule}";
                Equal(selfLabel + " first", expected, self.FirstArea);
                Equal(selfLabel + " second", expected, self.SecondArea);
                Equal(selfLabel + " intersection", expected, self.IntersectionArea);
                Equal(selfLabel + " narrow intersection", expected, WindingArea.IntersectionArea(path, path, rule));
                Equal(selfLabel + " union", expected, self.UnionArea);
                Equal(selfLabel + " xor", 0, self.SymmetricDifferenceArea);
                NullableEqual(selfLabel + " IoU", expected > 0 ? 1 : null, self.IntersectionOverUnion);
                NullableEqual(selfLabel + " Jaccard", expected > 0 ? 0 : null, self.JaccardDistance);
            }

            // Independently chosen coordinate margins keep the original vertices strictly inside,
            // including rectangles whose X/Y scales differ by more than a thousand powers of two.
            double minX = path.Min(p => p.X), maxX = path.Max(p => p.X);
            double minY = path.Min(p => p.Y), maxY = path.Max(p => p.Y);
            double padX = maxX - minX, padY = maxY - minY;
            Point2[] container = [new(minX - padX, minY - padY), new(maxX + padX, minY - padY),
                new(maxX + padX, maxY + padY), new(minX - padX, maxY + padY)];
            BigInteger outerNumerator = BigInteger.Abs(SignedAreaNumerator(container));
            double outerArea = RoundDyadic(outerNumerator, -2149);
            double difference = RoundDyadic(outerNumerator - BigInteger.Abs(numerator), -2149);
            foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
            foreach (bool swap in new[] { false, true })
            {
                var result = swap ? WindingArea.FilledRegions(container, path, rule)
                    : WindingArea.FilledRegions(path, container, rule);
                string label2 = $"{label} contained {rule}, swap={swap}";
                Equal(label2 + " first", swap ? outerArea : expected, result.FirstArea, swap ? 2 : 0);
                Equal(label2 + " second", swap ? expected : outerArea, result.SecondArea, swap ? 0 : 2);
                Equal(label2 + " intersection", expected, result.IntersectionArea);
                Equal(label2 + " narrow intersection", expected, swap ? WindingArea.IntersectionArea(container, path, rule)
                    : WindingArea.IntersectionArea(path, container, rule));
                Equal(label2 + " union", outerArea, result.UnionArea, 2);
                // The container's coordinates/products and the difference chain have ordinary
                // floating-point arithmetic. Allow two representable neighbors of their exact areas.
                Equal(label2 + " xor", difference, result.SymmetricDifferenceArea, 2);
                NullableEqual(label2 + " IoU", outerArea > 0 ? expected / outerArea : null, result.IntersectionOverUnion, 2);
            }
        }
    }

    private static void Integrals(string name, WindingAreaResult result, double expected, double signed)
    {
        Equal(name + " NonZero", expected, result.NonZero);
        Equal(name + " EvenOdd", expected, result.EvenOdd);
        Equal(name + " AbsoluteWinding", expected, result.AbsoluteWinding);
        Equal(name + " Signed", signed, result.Signed);
    }

    private static double Area(Point2[] path) => Math.Abs(RoundDyadic(SignedAreaNumerator(path), -2149));

    // Coordinate(d) is exactly d / 2^-1074, including signed zero and subnormals. No arithmetic
    // on the input floating values, runtime predicates, clipping, intersections or epsilon is involved.
    private static BigInteger Coordinate(double value)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        BigInteger significand = bits & 0x000f_ffff_ffff_ffffUL;
        if (exponent != 0) significand = (significand + (BigInteger.One << 52)) << (exponent - 1);
        return (bits >> 63) != 0 ? -significand : significand;
    }

    private static BigInteger SignedAreaNumerator(Point2[] path)
    {
        BigInteger sum = 0;
        for (int i = 0; i < path.Length; i++)
        {
            Point2 a = path[i], b = path[(i + 1) % path.Length];
            sum += Coordinate(a.X) * Coordinate(b.Y) - Coordinate(a.Y) * Coordinate(b.X);
        }
        return sum;
    }

    // Round once, including ties-to-even at the normal/subnormal boundary and at zero. Converting
    // an unbounded integer to double first could overflow or introduce double rounding here.
    private static double RoundDyadic(BigInteger integer, int exponent)
    {
        int sign = integer.Sign;
        if (sign == 0) return 0;
        integer = BigInteger.Abs(integer);
        int shift = Math.Max(checked((int)integer.GetBitLength()) - 53, -1074 - exponent);
        if (shift > 0)
        {
            BigInteger high = integer >> shift;
            BigInteger remainder = integer - (high << shift), half = BigInteger.One << (shift - 1);
            if (remainder > half || (remainder == half && !high.IsEven)) high++;
            integer = high;
            exponent += shift;
        }
        return sign * Math.ScaleB((double)integer, exponent);
    }

    private static void CheckOracleRounding()
    {
        Equal("oracle subnormal half ties to even zero", 0, RoundDyadic(1, -1075));
        Equal("oracle subnormal upper half ties to even", 2 * double.Epsilon, RoundDyadic(3, -1075));
        Equal("oracle subnormal lower half ties to even", 2 * double.Epsilon, RoundDyadic(5, -1075));
        Equal("oracle negative subnormal tie", -2 * double.Epsilon, RoundDyadic(-3, -1075));
        Equal("oracle normal half ties to even", 1, RoundDyadic((BigInteger.One << 53) + 1, -53));
        Equal("oracle normal round up", Math.BitIncrement(1), RoundDyadic((BigInteger.One << 54) + 3, -54));
        Equal("oracle wide integer stays finite", Math.ScaleB(1, 600), RoundDyadic(BigInteger.One << 2749, -2149));
        Equal("oracle known L=1e8 triangle", 1.4901161193847656,
            Area([new(0, 0), new(1e8, 1e8), new(2e8, Math.BitIncrement(2e8))]));
        Equal("oracle known L=1e12 triangle", 122070312.5,
            Area([new(0, 0), new(1e12, 1e12), new(2e12, Math.BitIncrement(2e12))]));
        Equal("oracle known L=1e16 triangle", 2e16,
            Area([new(0, 0), new(1e16, 1e16), new(2e16, Math.BitIncrement(2e16))]));
    }

    private static void NullableEqual(string name, double? expected, double? actual, int ulps = 0)
    {
        if (expected.HasValue && actual.HasValue) Equal(name, expected.Value, actual.Value, ulps);
        else
        {
            checkedValues++;
            if (expected.HasValue != actual.HasValue) Failures.Add($"{name}: expected {expected}, actual {actual}");
        }
    }

    private static void Equal(string name, double expected, double actual, int ulps = 0)
    {
        checkedValues++;
        double low = expected, high = expected;
        for (int i = 0; i < ulps; i++) { low = Math.BitDecrement(low); high = Math.BitIncrement(high); }
        if (!double.IsFinite(actual) || (expected >= 0 && actual < 0) || actual < low || actual > high)
            Failures.Add($"{name}: expected {expected:R}, actual {actual:R}, tolerance {ulps} ULP");
    }
}
