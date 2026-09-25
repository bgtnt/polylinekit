using System.Numerics;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Exact dyadic enclosure controls for the optional trapezoid arithmetic specialization.</summary>
internal static class AreaArithmeticChecks
{
    internal static void Run()
    {
        int checks = 0, certified = 0, rejected = 0;
        double epsilon = double.Epsilon, minNormal = Math.ScaleB(1, -1022);
        (double Lo, double Hi)[] widths =
        [
            (0, 0), (-0.0, 0), (0, epsilon), (epsilon, epsilon), (epsilon, 3 * epsilon),
            (0, minNormal), (Math.BitDecrement(minNormal), minNormal),
            (Math.ScaleB(1, -537), Math.ScaleB(1, -536)),
            (.5, .5), (1, 1), (0, 1), (Math.BitDecrement(1), Math.BitIncrement(1)),
            (1, 2), (Math.ScaleB(1, 52), Math.BitIncrement(Math.ScaleB(1, 52))),
            (Math.ScaleB(1, 300), Math.ScaleB(1, 301)),
            (Math.ScaleB(1, 1000), Math.ScaleB(1, 1001)),
            (Math.BitDecrement(double.MaxValue), double.MaxValue)
        ];
        (double Lo, double Hi)[] heights =
        [
            (0, 0), (-0.0, 0), (0, epsilon), (epsilon, epsilon), (epsilon, 2 * epsilon),
            (minNormal, minNormal), (Math.ScaleB(1, -537), Math.ScaleB(1, -536)),
            (.5, .5), (1, 1), (0, 1), (1, Math.BitIncrement(1)), (1, 2),
            (Math.ScaleB(1, 300), Math.ScaleB(1, 301)),
            (Math.ScaleB(1, 1000), Math.ScaleB(1, 1001))
        ];

        // This Cartesian product covers zero/one identities, rounded lower endpoints below
        // zero, gradual underflow and multiplication overflow. The oracle uses no double
        // additions/products or production outward-rounding helpers.
        foreach (var lower in widths)
        foreach (var upper in widths)
        foreach (var height in heights)
            Check("endpoint matrix", lower.Lo, lower.Hi, upper.Lo, upper.Hi, height.Lo, height.Hi);

        Check("ordinary point rectangle", 1, 1, 1, 1, 1, 1, mustSucceed: true);
        Check("ordinary interval trapezoid", 1, 2, 3, 4, 2, 3, mustSucceed: true);
        Check("zero widths identity", 0, 0, 0, 0, double.MaxValue, double.MaxValue, mustSucceed: true);
        Check("zero height identity after finite widths", 1, 2, 3, 4, 0, 0, mustSucceed: true);
        Check("all signed zero identities", -0.0, 0, -0.0, -0.0, -0.0, 0, mustSucceed: true);
        Check("zero height still observes prior width-sum overflow", double.MaxValue, double.MaxValue,
            double.MaxValue, double.MaxValue, 0, 0, mustReject: true);
        Check("finite width product overflow", 1e300, 1e300, 1e300, 1e300, 1e300, 1e300, mustReject: true);

        // The generic path deliberately rounds zero down. Consequently the halved lower
        // endpoint can be negative epsilon even though true widths are nonnegative. Its
        // minimum product is then with the HIGH height, not the low height.
        Check("negative rounded lower times tall uncertain height", 0, epsilon, 0, epsilon,
            1, Math.ScaleB(1, 1000), mustSucceed: true);
        Check("negative rounded lower times zero-to-tall height", 0, minNormal, 0, minNormal,
            0, Math.ScaleB(1, 1000), mustSucceed: true);
        Check("half least-subnormal product", epsilon, epsilon, 0, 0, .5, .5, mustSucceed: true);
        Check("product at normal/subnormal boundary", minNormal, minNormal, minNormal, minNormal,
            Math.BitDecrement(1), Math.BitIncrement(1), mustSucceed: true);

        var random = new Random(684_229);
        int[] exponents = [-1074, -1073, -1022, -1000, -600, -537, -300, -52, 0, 50, 300, 500, 900, 1000];
        for (int i = 0; i < 2000; i++)
        {
            var a = RandomInterval();
            var b = RandomInterval();
            var h = RandomInterval();
            Check("deterministic dyadic intervals " + i, a.Lo, a.Hi, b.Lo, b.Hi, h.Lo, h.Hi);
        }

        // Repeated operations exercise the real accumulation path and accumulated radii.
        // The independent expected range is the exact single-term range times the count.
        foreach (int repetitions in new[] { 2, 17, 1024, 4096 })
        {
            Check("long ordinary accumulation", 1, 2, .5, 1, 1, 2, repetitions, mustSucceed: true);
            Check("long small exact products", Math.ScaleB(1, -500), Math.ScaleB(1, -499),
                Math.ScaleB(1, -500), Math.ScaleB(1, -499), Math.ScaleB(1, -500), Math.ScaleB(1, -499), repetitions, mustSucceed: true);
            Check("long underflow accumulation", epsilon, 2 * epsilon, 0, epsilon, .5, 1, repetitions, mustSucceed: true);
            Check("long negative rounded lower accumulation", 0, epsilon, 0, epsilon,
                1, Math.ScaleB(1, 1000), repetitions, mustSucceed: true);
            Check("long zero accumulation", -0.0, 0, 0, 0, 1, 2, repetitions, mustSucceed: true);
        }

        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -epsilon, -1 })
        {
            Check("invalid lower bound", invalid, 2, 1, 2, 1, 2, mustReject: true);
            Check("invalid upper bound", 1, invalid, 1, 2, 1, 2, mustReject: true);
            Check("invalid other width", 1, 2, invalid, 2, 1, 2, mustReject: true);
            Check("invalid height", 1, 2, 1, 2, invalid, 2, mustReject: true);
        }
        Check("reversed width interval", 2, 1, 1, 2, 1, 2, mustReject: true);
        Check("reversed height interval", 1, 2, 1, 2, 2, 1, mustReject: true);
        Check("zero repetitions", 1, 2, 1, 2, 1, 2, repetitions: 0, mustReject: true);
        Check("negative repetitions", 1, 2, 1, 2, 1, 2, repetitions: -1, mustReject: true);
        Require(certified > 1000 && rejected > 100, "Arithmetic controls must exercise successful enclosures and rejection.");
        Console.WriteLine($"Area arithmetic: {checks} exact dyadic controls; {certified} finite enclosures and {rejected} rejected operations; generic/optimized endpoints identical.");

        (double Lo, double Hi) RandomInterval()
        {
            double lo = Math.ScaleB(random.Next(0, 33), exponents[random.Next(exponents.Length)]);
            return random.Next(3) switch
            {
                0 => (lo, lo),
                1 => (lo, Math.BitIncrement(lo)),
                _ => (0, lo)
            };
        }

        void Check(string name, double lowerLo, double lowerHi, double upperLo, double upperHi,
            double heightLo, double heightHi, int repetitions = 1, bool mustSucceed = false, bool mustReject = false)
        {
            bool generic = GuardedDoubleSweep.TryAreaArithmeticForCheck(lowerLo, lowerHi, upperLo, upperHi,
                heightLo, heightHi, false, out double genericLo, out double genericHi, out string? genericReason, repetitions);
            bool optimized = GuardedDoubleSweep.TryAreaArithmeticForCheck(lowerLo, lowerHi, upperLo, upperHi,
                heightLo, heightHi, true, out double actualLo, out double actualHi, out string? actualReason, repetitions);
            Require(generic == optimized && genericReason == actualReason, name + ": optimization changed failure behavior.");
            bool eligible = Valid(lowerLo, lowerHi) && Valid(upperLo, upperHi) && Valid(heightLo, heightHi) && repetitions > 0;
            if (optimized)
            {
                certified++;
                Require(!mustReject && eligible, name + ": invalid operation was accepted.");
                Require(actualReason is null && double.IsFinite(actualLo) && double.IsFinite(actualHi) && actualLo <= actualHi,
                    name + ": malformed successful enclosure.");
                Require(Bits(actualLo) == Bits(genericLo) && Bits(actualHi) == Bits(genericHi),
                    name + ": optimized interval endpoints differ from generic arithmetic.");
                Dyadic exactLo = (Dyadic.From(lowerLo) + Dyadic.From(upperLo)).Half() * Dyadic.From(heightLo) * Dyadic.From(repetitions);
                Dyadic exactHi = (Dyadic.From(lowerHi) + Dyadic.From(upperHi)).Half() * Dyadic.From(heightHi) * Dyadic.From(repetitions);
                Require(Dyadic.From(actualLo).CompareTo(exactLo) <= 0 && Dyadic.From(actualHi).CompareTo(exactHi) >= 0,
                    name + ": returned enclosure excludes an exact endpoint of the true trapezoid-sum range.");
            }
            else
            {
                rejected++;
                Require(!mustSucceed, name + ": expected successful enclosure was rejected: " + actualReason);
                Require(!string.IsNullOrWhiteSpace(actualReason), name + ": failure has no explicit reason.");
                if (!eligible) Require(actualReason == "input-contract", name + ": malformed helper input did not fail validation.");
            }
            checks++;
        }
    }

    private static bool Valid(double lo, double hi) => double.IsFinite(lo) && double.IsFinite(hi) && lo >= 0 && lo <= hi;
    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Exact binary64 dyadics represented as an integer times a power of two. No floating
    // arithmetic, rounded intermediate bounds, or production interval helpers enter this oracle.
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

        internal Dyadic Half() => new(Integer, Exponent - 1);
        public static Dyadic operator +(Dyadic a, Dyadic b)
        {
            int exponent = Math.Min(a.Exponent, b.Exponent);
            return new((a.Integer << (a.Exponent - exponent)) + (b.Integer << (b.Exponent - exponent)), exponent);
        }
        public static Dyadic operator *(Dyadic a, Dyadic b) => new(a.Integer * b.Integer, a.Exponent + b.Exponent);
        public int CompareTo(Dyadic other)
        {
            int exponent = Math.Min(Exponent, other.Exponent);
            return (Integer << (Exponent - exponent)).CompareTo(other.Integer << (other.Exponent - exponent));
        }
    }
}
