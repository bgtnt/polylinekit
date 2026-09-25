using System.Numerics;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Small sub-edge shares must not disappear before multiplication by a large edge term.</summary>
internal static class WindingFractionChecks
{
    private static int checkedValues;
    private static readonly List<string> Failures = [];
    private readonly record struct Case(string Name, double HalfLength, double Width, double Height);

    public static int Run()
    {
        checkedValues = 0;
        Failures.Clear();
        double boundaryWidth = Math.ScaleB(1, -721), dyadicLength = Math.ScaleB(1, 300);
        Case[] cases =
        [
            new("normal share control", 1e100, 1e-200, 1),
            new("subnormal nonzero share", 1e100, 1e-210, 1),
            new("coarse subnormal share", 1e100, 1e-220, 1),
            new("zero share width 1e-250", 1e100, 1e-250, 1),
            new("zero share width 1e-300", 1e100, 1e-300, 1),
            new("subnormal intersection", dyadicLength, Math.ScaleB(1, -1050), 1),
            new("epsilon intersection", dyadicLength, double.Epsilon, 1),
            new("share below smallest normal", dyadicLength, Math.BitDecrement(Math.BitDecrement(boundaryWidth)), 1),
            new("share at smallest normal", dyadicLength, boundaryWidth, 1),
            new("share above smallest normal", dyadicLength, Math.BitIncrement(boundaryWidth), 1),
            new("multiply-first underflow control", Math.ScaleB(1, -40), 3 * double.Epsilon, Math.ScaleB(1, 10)),
            new("epsilon product control", Math.ScaleB(1, -40), double.Epsilon, 1),
            new("large finite product control", 1e100, 1e90, 1e100),
            new("tiny ordinary products control", Math.ScaleB(1, -400), Math.ScaleB(1, -500), Math.ScaleB(1, -400))
        ];
        foreach (Case item in cases)
        {
            CheckRegions(item);
            CheckRetracedSpurs(item);
        }
        if (Failures.Count != 0)
            throw new InvalidOperationException($"{Failures.Count} of {checkedValues} sub-edge share checks failed.\n"
                + string.Join("\n", Failures.Take(16)));
        Console.WriteLine($"PASS: {checkedValues} sub-edge share and extreme-ratio checks.");
        return checkedValues;
    }

    private static void CheckRegions(Case item)
    {
        Point2[] wide = Rectangle(-item.HalfLength, item.HalfLength, -item.Height / 2, item.Height / 2);
        Point2[] thin = Rectangle(0, item.Width, -item.Height, item.Height);
        // All boundaries are axis aligned. The exact intersection is [0,width] x [-height/2,height/2].
        // Compute every expected area before one binary64 rounding, including the union/XOR sums.
        BigInteger wideExact = RectangleNumerator(wide), thinExact = RectangleNumerator(thin);
        BigInteger intersectionExact = (Coordinate(item.Width) - Coordinate(0))
            * (Coordinate(item.Height / 2) - Coordinate(-item.Height / 2));
        double first = Area(wideExact), second = Area(thinExact), intersection = Area(intersectionExact);
        double union = Area(wideExact + thinExact - intersectionExact);
        double xor = Area(wideExact + thinExact - 2 * intersectionExact);
        foreach (bool transpose in new[] { false, true })
        foreach (bool reverseFirst in new[] { false, true })
        foreach (bool reverseSecond in new[] { false, true })
        foreach (bool swap in new[] { false, true })
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            Point2[] a = Variant(wide, transpose, reverseFirst), b = Variant(thin, transpose, reverseSecond);
            WindingOverlapResult result = swap ? WindingArea.FilledRegions(b, a, rule) : WindingArea.FilledRegions(a, b, rule);
            string name = $"{item.Name}, transpose={transpose}, reverse={reverseFirst}/{reverseSecond}, swap={swap}, {rule}";
            Equal(name + " first", swap ? second : first, result.FirstArea);
            Equal(name + " second", swap ? first : second, result.SecondArea);
            Equal(name + " intersection", intersection, result.IntersectionArea);
            Equal(name + " narrow intersection", intersection, swap ? WindingArea.IntersectionArea(b, a, rule)
                : WindingArea.IntersectionArea(a, b, rule));
            Equal(name + " union", union, result.UnionArea);
            Equal(name + " XOR", xor, result.SymmetricDifferenceArea);
            Equal(name + " IoU", intersection / union, result.IntersectionOverUnion!.Value, 8);
            Equal(name + " Jaccard", xor / union, result.JaccardDistance!.Value, 8);
        }
    }

    private static void CheckRetracedSpurs(Case item)
    {
        // The two opposite, retraced spurs add no area but preserve the same extreme overall extent.
        // This covers both single-walk entry points without inferring a tiny intersection by subtracting
        // huge rounded totals. Collinear spurs are netted; the region regressions exercise tiny shares.
        Point2[] path = [new(0, 0), new(-item.HalfLength, 0), new(0, 0), new(item.HalfLength, 0), new(0, 0),
            new(0, -item.Height / 2), new(item.Width, -item.Height / 2), new(item.Width, item.Height / 2), new(0, item.Height / 2)];
        double expected = Area(Coordinate(item.Width) * (Coordinate(item.Height / 2) - Coordinate(-item.Height / 2)));
        foreach (bool transpose in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        {
            Point2[] variant = Variant(path, transpose, reverse);
            string name = $"{item.Name}, retraced spurs, transpose={transpose}, reverse={reverse}";
            double signed = transpose != reverse ? -expected : expected;
            Check(name + " closed", WindingArea.ClosedPath(variant));
            Check(name + " bridged", WindingArea.EndpointBridged(variant, [variant[0], variant[^1]]));
            void Check(string label, WindingAreaResult result)
            {
                Equal(label + " NonZero", expected, result.NonZero);
                Equal(label + " EvenOdd", expected, result.EvenOdd);
                Equal(label + " AbsoluteWinding", expected, result.AbsoluteWinding);
                Equal(label + " Signed", signed, result.Signed);
            }
        }
    }

    private static Point2[] Rectangle(double left, double right, double bottom, double top) =>
        [new(left, bottom), new(right, bottom), new(right, top), new(left, top)];

    private static Point2[] Variant(Point2[] path, bool transpose, bool reverse)
    {
        Point2[] copy = path.Select(p => transpose ? new Point2(p.Y, p.X) : p).ToArray();
        if (reverse) Array.Reverse(copy);
        return copy;
    }

    private static BigInteger RectangleNumerator(Point2[] rectangle) =>
        (Coordinate(rectangle[2].X) - Coordinate(rectangle[0].X))
        * (Coordinate(rectangle[2].Y) - Coordinate(rectangle[0].Y));

    // Each binary64 coordinate is an integer multiple of 2^-1074. No runtime orientation,
    // sub-edge fraction, clipping or floating-point product is used in this expected value.
    private static BigInteger Coordinate(double value)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        BigInteger significand = bits & 0x000f_ffff_ffff_ffffUL;
        if (exponent != 0) significand = (significand + (BigInteger.One << 52)) << (exponent - 1);
        return (bits >> 63) != 0 ? -significand : significand;
    }

    private static double Area(BigInteger integer)
    {
        const int exponent = -2148;
        int shift = Math.Max(checked((int)integer.GetBitLength()) - 53, -1074 - exponent);
        BigInteger high = integer >> shift, remainder = integer - (high << shift), half = BigInteger.One << (shift - 1);
        if (remainder > half || (remainder == half && !high.IsEven)) high++;
        return Math.ScaleB((double)high, exponent + shift);
    }

    private static void Equal(string name, double expected, double actual, int ulps = 4)
    {
        checkedValues++;
        double low = expected, high = expected;
        for (int i = 0; i < ulps; i++) { low = Math.BitDecrement(low); high = Math.BitIncrement(high); }
        // Both criteria apply. A generous absolute epsilon would hide the original 50% loss;
        // ULP-only tolerances could even accept zero or twice the answer for a subnormal area.
        bool correct = double.IsFinite(actual) && (expected == 0 ? actual == 0
            : Math.Sign(actual) == Math.Sign(expected) && actual >= low && actual <= high
                && Math.Abs((actual - expected) / expected) <= 2e-15);
        if (!correct) Failures.Add($"{name}: expected {expected:R}, actual {actual:R}; max {ulps} ULP and 2e-15 relative error");
    }
}
