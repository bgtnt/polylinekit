using System.Numerics;
using PolylineKit;
using static SpecializedArea;

/// <summary>Independent exact-dyadic checks of the example's conservative bounds and decisions.</summary>
internal static class SpecializedAreaChecks
{
    private static int checks;
    private static readonly Point2[] Square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];

    internal static int Run()
    {
        checks = 0;
        CheckInvalidInputs();
        CheckRetainedOrder();
        CheckDyadicCases();
        CheckExtremeBounds();
        Console.WriteLine($"Specialized area: {checks} independent exact-dyadic and argument checks passed.");
        return checks;
    }

    private static void CheckInvalidInputs()
    {
        Point2[][] invalid = [[], [new(0, 0)], [new(0, 0), new(1, 0)],
            [new(0, 0), new(1, 0), new(double.NaN, 1)],
            [new(0, 0), new(1, double.PositiveInfinity), new(0, 1)],
            [new(0, 0), new(1, 0), new(Math.BitIncrement(1e100), 1)]];
        foreach (Point2[]? input in invalid.Prepend(null))
        foreach (bool swap in new[] { false, true })
        {
            Point2[] a = (swap ? Square : input)!;
            Point2[] b = (swap ? input : Square)!;
            False("invalid mapping inputs", TryMapRetained(a, b, out _));
            False("invalid nested inputs", TryNestedSimple(a, b, out _));
            False("invalid pocket inputs", TryDisjointSimplePockets(a, b, [0, 1, 2, 3], out _));
            False("invalid bound inputs", TryBounds(a, b, [0, 1, 2, 3], out _));
        }
        Point2[] triangle = [Square[0], Square[1], Square[2]];
        foreach (int[]? map in new int[][] { [], [0, 1], [-1, 1, 2], [0, 1, 4], [0, 1, 1], [0, 2, 1] }.Prepend(null))
        {
            False("invalid supplied pocket map", TryDisjointSimplePockets(Square, triangle, map!, out _));
            False("invalid supplied bound map", TryBounds(Square, triangle, map!, out _));
        }
        True("ordinary bounds for threshold checks", TryBounds(Square, triangle, [0, 1, 2], out AreaBounds ordinary));
        foreach (double threshold in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -double.Epsilon, Math.BitIncrement(1.0) })
            True("invalid threshold fails open", Decide(ordinary, threshold) == Decision.Unresolved);
        True("undefined default bounds fail open", Decide(default, .5) == Decision.Unresolved);
    }

    private static void CheckRetainedOrder()
    {
        foreach (int[] expected in new int[][] { [0, 1, 2], [2, 3, 0], [3, 0, 2], [0, 1, 2, 3] })
        {
            Point2[] q = expected.Select(i => Square[i]).ToArray();
            True("cyclic correspondence", TryMapRetained(Square, q, out int[] map));
            True("exact retained indices", expected.SequenceEqual(map));
            Rational first = Area(Square), second = Area(q);
            CheckBounds("cyclic nested square", Square, q, map, Rational.Abs(first - second), Rational.Max(first, second));
            Point2[] pReverse = Square.Reverse().ToArray(), qReverse = q.Reverse().ToArray();
            True("both input directions reversed", TryMapRetained(pReverse, qReverse, out int[] reversed));
            CheckBounds("reversed nested square", pReverse, qReverse, reversed, Rational.Abs(first - second), Rational.Max(first, second));
        }
        False("reversed output is not cyclic subsequence", TryMapRetained(Square, [Square[2], Square[1], Square[0]], out _));
        False("unmatched output vertex", TryMapRetained(Square, [Square[0], new(1, 0), Square[2]], out _));
        False("repeated retained vertex", TryMapRetained(Square, [Square[0], Square[1], Square[0]], out _));
        False("ambiguous repeated original vertex", TryMapRetained([.. Square, Square[0], Square[1]], Square, out _));
        False("mapper requires implicit closure without a duplicate position", TryMapRetained([.. Square, Square[0]], Square, out _));
    }

    private static void CheckDyadicCases()
    {
        Point2[] octagon = [new(0, 0), new(2, 0), new(3, 1), new(3, 3), new(2, 4), new(0, 4), new(-1, 3), new(-1, 1)];
        // Two disjoint triangular pockets, one added and one removed. Area subtraction is zero,
        // although exact XOR is 2. A fan anchored further away remains a valid but looser bound.
        Point2[] mixed = [new(0, 0), new(8, 0), new(8, 4), new(7, 4), new(6, 5), new(5, 4),
            new(3, 4), new(2, 3), new(1, 4), new(0, 4)];
        foreach (int exponent in new[] { -20, 0, 20 })
        {
            double scale = Math.ScaleB(1, exponent);
            Point2[] p = octagon.Select(v => new Point2(v.X * scale, v.Y * scale)).ToArray();
            int[] indices = [0, 2, 4, 6]; Point2[] q = indices.Select(i => p[i]).ToArray();
            Rational d = Area(p) - Area(q);
            CheckBounds("convex nested", p, q, indices, d, Area(p));
            True("nested estimate available", TryNestedSimple(p, q, out AreaValues nested));
            ExactValues("nested estimate", nested, Area(p), Area(q), d, Area(p));
            True("convex disjoint pockets available", TryDisjointSimplePockets(p, q, indices, out AreaValues pockets));
            ExactValues("convex pocket estimate", pockets, Area(p), Area(q), d, Area(p));

            Point2[] edited = mixed.Select(v => new Point2(v.X * scale, v.Y * scale)).ToArray();
            Rational changed = new Rational(2, 1) * Rational.FromDouble(scale) * Rational.FromDouble(scale);
            Rational union = new Rational(33, 1) * Rational.FromDouble(scale) * Rational.FromDouble(scale);
            foreach (int[] retained in new int[][] { [0, 1, 2, 9], [0, 1, 2, 3, 5, 6, 8, 9] })
                CheckBounds("mixed inward/outward", edited, retained.Select(i => edited[i]).ToArray(), retained, changed, union);
            int[] disjoint = [0, 1, 2, 3, 5, 6, 8, 9]; Point2[] simplified = disjoint.Select(i => edited[i]).ToArray();
            True("disjoint mixed pockets available", TryDisjointSimplePockets(edited, simplified, disjoint, out AreaValues local));
            ExactValues("disjoint mixed estimate", local, Area(edited), Area(simplified), changed, union);
        }
        True("square/triangle bounds available", TryBounds(Square, [Square[0], Square[1], Square[2]], [0, 1, 2], out AreaBounds bounds));
        // Jaccard is D/union = 2D/(A+B+D)=1/2 here, whereas D/(A+B)=1/3 is wrong.
        True("Jaccard threshold uses union", Decide(bounds, .4) == Decision.Reject);
        True("separated upper threshold accepts", Decide(bounds, .6) == Decision.Accept);
    }

    private static void CheckExtremeBounds()
    {
        foreach (double scale in new[] { double.Epsilon, Math.ScaleB(1, -600), Math.ScaleB(1, -537), Math.ScaleB(1, -500), 1e99 })
        {
            Point2[] p = Square.Select(v => new Point2(v.X * scale, v.Y * scale)).ToArray();
            Point2[] q = [p[0], p[1], p[2]];
            CheckBounds("extreme dyadic input", p, q, [0, 1, 2], Area(p) - Area(q), Area(p), allowUnresolved: true);
        }
        double offset = Math.ScaleB(1, 52);
        Point2[] translated = Square.Select(v => new Point2(v.X + offset, v.Y + offset)).ToArray();
        CheckBounds("large translated square", translated, [translated[0], translated[1], translated[2]], [0, 1, 2], new(2, 1), new(4, 1));
        Point2[] collinear = [new(0, 0), new(1, 0), new(2, 0)];
        False("zero union cannot certify a ratio", TryBounds(collinear, collinear, [0, 1, 2], out _));
        Point2[] repeated = [new(0, 0), new(0, 0), new(0, 0)];
        False("coincident points cannot certify a ratio", TryBounds(repeated, repeated, [0, 1, 2], out _));
        foreach (Point2[] degenerate in new[] { collinear, repeated })
        {
            False("degenerate area estimate fails open", TryNestedSimple(degenerate, degenerate, out _));
            False("degenerate pocket estimate fails open", TryDisjointSimplePockets(degenerate, degenerate, [0, 1, 2], out _));
        }
        foreach (double length in new[] { 1e4, 1e8, 1e12, 1e16 })
        {
            double increment = Math.BitIncrement(2 * length) - 2 * length;
            Point2[] thin = [new(0, 0), new(length, length), new(2 * length, 2 * length + increment), new(length, length + increment)];
            Point2[] triangle = [thin[0], thin[1], thin[2]];
            CheckBounds("near-cancelling thin polygon", thin, triangle, [0, 1, 2], Area(thin) - Area(triangle), Area(thin), allowUnresolved: true);
        }
        foreach (double huge in new[] { double.MaxValue, 1e101 })
        {
            Point2[] p = [new(-huge, -huge), new(huge, -huge), new(huge, huge), new(-huge, huge)];
            False("overflow-range inputs fail open", TryBounds(p, [p[0], p[1], p[2]], [0, 1, 2], out _));
        }
    }

    private static void CheckBounds(string name, Point2[] p, Point2[] q, int[] map, Rational xor, Rational union, bool allowUnresolved = false)
    {
        bool available = TryBounds(p, q, map, out AreaBounds bounds);
        True(name + " available or conservatively unresolved", available || allowUnresolved);
        if (!available) return;
        Encloses(name + " first", bounds.First, Area(p));
        Encloses(name + " second", bounds.Second, Area(q));
        Encloses(name + " XOR", bounds.Xor, xor);
        Encloses(name + " union", bounds.Union, union);
        Rational jaccard = xor / union;
        Encloses(name + " Jaccard", bounds.Jaccard, jaccard);
        foreach (double threshold in new[] { 0, .125, .25, .4, .5, .75, 1 })
        {
            Decision decision = Decide(bounds, threshold);
            Rational t = Rational.FromDouble(threshold);
            True(name + " conservative threshold decision", decision == Decision.Unresolved ||
                (decision == Decision.Accept && jaccard.CompareTo(t) <= 0) || (decision == Decision.Reject && jaccard.CompareTo(t) > 0));
        }
    }

    private static void ExactValues(string name, AreaValues result, Rational first, Rational second, Rational xor, Rational union)
    {
        True(name + " first", Rational.FromDouble(result.First).CompareTo(first) == 0);
        True(name + " second", Rational.FromDouble(result.Second).CompareTo(second) == 0);
        True(name + " XOR", Rational.FromDouble(result.Xor).CompareTo(xor) == 0);
        True(name + " union", Rational.FromDouble(result.Union).CompareTo(union) == 0);
        True(name + " intersection", Rational.FromDouble(result.Intersection).CompareTo((first + second - xor) / new Rational(2, 1)) == 0);
        True(name + " Jaccard definition", result.Jaccard == result.Xor / result.Union);
    }

    private static Rational Area(Point2[] p)
    {
        BigInteger sum = 0;
        for (int i = 0; i < p.Length; i++)
        {
            Point2 a = p[i], b = p[(i + 1) % p.Length];
            sum += Coordinate(a.X) * Coordinate(b.Y) - Coordinate(a.Y) * Coordinate(b.X);
        }
        return new(BigInteger.Abs(sum), BigInteger.One << 2149);
    }

    private static BigInteger Coordinate(double value)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        BigInteger magnitude = bits & 0xfffffffffffffUL;
        if (exponent != 0) magnitude = (magnitude + (BigInteger.One << 52)) << (exponent - 1);
        return (bits >> 63) == 0 ? magnitude : -magnitude;
    }

    private readonly record struct Rational(BigInteger Numerator, BigInteger Denominator)
    {
        internal static Rational FromDouble(double value) => new(Coordinate(value), BigInteger.One << 1074);
        internal static Rational Abs(Rational value) => new(BigInteger.Abs(value.Numerator), value.Denominator);
        internal static Rational Max(Rational a, Rational b) => a.CompareTo(b) >= 0 ? a : b;
        internal int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        public static Rational operator +(Rational a, Rational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator -(Rational a, Rational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    private static void Encloses(string name, Interval interval, Rational exact) => True(name,
        double.IsFinite(interval.Lower) && double.IsFinite(interval.Upper) && interval.Lower >= 0 && interval.Lower <= interval.Upper &&
        Rational.FromDouble(interval.Lower).CompareTo(exact) <= 0 && Rational.FromDouble(interval.Upper).CompareTo(exact) >= 0);

    private static void False(string name, bool actual) => True(name, !actual);
    private static void True(string name, bool condition)
    {
        checks++;
        if (!condition) throw new InvalidDataException("Specialized area check failed: " + name);
    }
}
