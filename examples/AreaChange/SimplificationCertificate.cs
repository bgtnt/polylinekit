using System.Numerics;
using PolylineKit;

/// <summary>
/// Example-only exact simplicity check. It verifies a precondition of specialized
/// simplification bounds; it is not a public PolylineKit API or a fast general certifier.
/// </summary>
internal static class SimplificationCertificate
{
    private readonly record struct Dyadic(long Mantissa, int Exponent);
    private readonly record struct IntegerPoint(BigInteger X, BigInteger Y);

    /// <summary>
    /// Certifies one finite, simple, nondegenerate closed contour using exact integer
    /// predicates on the supplied binary64 coordinates. One repeated closing point is
    /// optional. Consecutive duplicates and every nonadjacent contact are rejected.
    /// </summary>
    /// <remarks>
    /// The method is intentionally conservative. It does not certify containment,
    /// pocket disjointness, retained-vertex correspondence, or numerical area accuracy.
    /// Coordinate conversion and allocated working arrays are part of this call.
    /// Pair enumeration is O(n squared), with cheap bounding-box rejection before exact
    /// predicates. BigInteger operation cost additionally depends on exponent spread.
    /// Prepared and validation-inclusive measurements must account for this separately.
    /// </remarks>
    internal static bool IsSimple(Point2[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        int count = points.Length;
        if (count > 1 && Same(points[0], points[count - 1])) count--;
        if (count < 3) return false;
        var dyadics = new Dyadic[count * 2];
        int minimumExponent = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Point2 point = points[i];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                Same(point, points[(i + 1) % count])) return false;
            dyadics[2 * i] = Decode(point.X);
            dyadics[2 * i + 1] = Decode(point.Y);
            for (int coordinate = 2 * i; coordinate <= 2 * i + 1; coordinate++)
                if (dyadics[coordinate].Mantissa != 0)
                    minimumExponent = Math.Min(minimumExponent, dyadics[coordinate].Exponent);
        }
        if (minimumExponent == int.MaxValue) return false;
        var exact = new IntegerPoint[count];
        for (int i = 0; i < count; i++)
            exact[i] = new(Scale(dyadics[2 * i], minimumExponent), Scale(dyadics[2 * i + 1], minimumExponent));

        BigInteger signedAreaTwice = BigInteger.Zero;
        for (int i = 0; i < count; i++)
        {
            var a = exact[i]; var b = exact[(i + 1) % count];
            signedAreaTwice += a.X * b.Y - a.Y * b.X;
        }
        if (signedAreaTwice.IsZero) return false;

        for (int i = 0; i < count; i++)
        {
            int nextI = (i + 1) % count;
            Point2 a = points[i], b = points[nextI];
            double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
            for (int j = i + 1; j < count; j++)
            {
                int nextJ = (j + 1) % count;
                Point2 c = points[j], d = points[nextJ];
                // Comparisons of input coordinates introduce no new rounded geometry.
                if (maxX < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < minX ||
                    maxY < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < minY) continue;
                int intersection = Intersects(exact[i], exact[nextI], exact[j], exact[nextJ]);
                bool adjacent = j == i + 1 || (i == 0 && j == count - 1);
                // Adjacent edges may share exactly their common endpoint, but not overlap.
                if (intersection != 0 && (!adjacent || intersection != 1)) return false;
            }
        }
        return true;
    }

    internal static int CheckControls()
    {
        int checks = 0;
        void Check(bool expected, params Point2[] points)
        {
            if (IsSimple(points) != expected) throw new InvalidDataException("Simplification simplicity control failed.");
            checks++;
            if (IsSimple(points.Reverse().ToArray()) != expected)
                throw new InvalidDataException("Reversed simplification simplicity control failed.");
            checks++;
        }
        Check(true, new(0, 0), new(2, 0), new(2, 2), new(0, 2));
        Check(true, new(0, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0));
        Check(true, new(0, 0), new(1, 0), new(2, 0), new(2, 2), new(0, 2));
        Check(true, new(0, 0), new(3, 0), new(3, 3), new(2, 3), new(2, 1), new(1, 1), new(1, 3), new(0, 3));
        Check(false, new(0, 0), new(3, 0), new(1, 0), new(3, 2), new(0, 2)); // Adjacent retracing.
        Check(false, new(0, 0), new(2, 2), new(0, 2), new(2, 0)); // Zero algebraic-area bow tie.
        Check(false, new(0, 0), new(3, 3), new(0, 4), new(4, 0)); // Nonzero algebraic-area crossing.
        Check(false, new(0, 0), new(4, 0), new(4, 4), new(2, 0), new(0, 4)); // Vertex on another edge.
        Check(false, new(0, 0), new(2, 0), new(1, 1), new(2, 2), new(0, 2), new(1, 1));
        Check(false, new(0, 0), new(2, 0), new(2, 0), new(2, 2), new(0, 2));
        Check(false, new(0, 0), new(1, 0), new(2, 0));
        Check(false, new(0, 0), new(1, 1));
        Check(false, new(0, 0), new(double.NaN, 0), new(0, 1));
        Check(false, new(0, 0), new(1, double.PositiveInfinity), new(0, 1));
        double tiny = Math.ScaleB(1, -600), offset = Math.ScaleB(1, 52);
        Check(true, new(0, 0), new(tiny, 0), new(0, tiny)); // Ordinary double area underflows.
        Check(true, new(0, 0), new(double.Epsilon, 0), new(0, double.Epsilon)); // Subnormal coordinate decoding.
        Check(true, new(offset, offset), new(offset + 1, offset), new(offset, offset + 1));
        Check(true, new(-1e100, -.5), new(1e100, -.5), new(1e100, .5), new(-1e100, .5));
        Check(true, new(-1e100, -1e100), new(0, 0), new(1e100, Math.BitIncrement(1e100)));
        return checks;
    }

    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;

    private static Dyadic Decode(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int biasedExponent = (int)((bits >> 52) & 0x7ff);
        long mantissa = bits & 0xfffffffffffffL;
        if (biasedExponent != 0) mantissa |= 1L << 52;
        int exponent = biasedExponent == 0 ? -1074 : biasedExponent - 1075;
        if (mantissa != 0)
        {
            int zeros = BitOperations.TrailingZeroCount((ulong)mantissa);
            mantissa >>= zeros;
            exponent += zeros;
        }
        return new(bits < 0 ? -mantissa : mantissa, exponent);
    }

    private static BigInteger Scale(Dyadic value, int minimumExponent) => value.Mantissa == 0
        ? BigInteger.Zero : new BigInteger(value.Mantissa) << (value.Exponent - minimumExponent);

    private static int Orientation(IntegerPoint a, IntegerPoint b, IntegerPoint c) =>
        ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)).Sign;

    private static bool OnSegment(IntegerPoint a, IntegerPoint b, IntegerPoint point) =>
        point.X >= BigInteger.Min(a.X, b.X) && point.X <= BigInteger.Max(a.X, b.X) &&
        point.Y >= BigInteger.Min(a.Y, b.Y) && point.Y <= BigInteger.Max(a.Y, b.Y);

    // 0: none; 1: a single boundary contact; 2: proper crossing; 3: collinear overlap.
    private static int Intersects(IntegerPoint a, IntegerPoint b, IntegerPoint c, IntegerPoint d)
    {
        int abc = Orientation(a, b, c), abd = Orientation(a, b, d);
        int cda = Orientation(c, d, a), cdb = Orientation(c, d, b);
        if (abc * abd < 0 && cda * cdb < 0) return 2;
        if (abc == 0 && abd == 0 && cda == 0 && cdb == 0)
        {
            bool useX = a.X != b.X || c.X != d.X;
            BigInteger av = useX ? a.X : a.Y, bv = useX ? b.X : b.Y;
            BigInteger cv = useX ? c.X : c.Y, dv = useX ? d.X : d.Y;
            BigInteger low = BigInteger.Max(BigInteger.Min(av, bv), BigInteger.Min(cv, dv));
            BigInteger high = BigInteger.Min(BigInteger.Max(av, bv), BigInteger.Max(cv, dv));
            return high > low ? 3 : high == low ? 1 : 0;
        }
        return (abc == 0 && OnSegment(a, b, c)) || (abd == 0 && OnSegment(a, b, d)) ||
            (cda == 0 && OnSegment(c, d, a)) || (cdb == 0 && OnSegment(c, d, b)) ? 1 : 0;
    }
}
