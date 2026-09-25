using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.AreaBenchmarks;

internal sealed record Contour(string Id, string County, Point2[] Points)
{
    internal string CoordinateHash => Corpus.Hash(JsonSerializer.SerializeToUtf8Bytes(Points));
}

internal static class Corpus
{
    internal const string FileHash = "a2147b794f7395a103727bc67373b63d33a21dbcb1d601aa32f9a3572b98749c";
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static Contour[] Load()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data/counties-5070.json"));
        if (Hash(bytes) != FileHash) throw new InvalidDataException("Frozen Census response hash changed.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.GetProperty("spatialReference").GetProperty("wkid").GetInt32() != 5070 ||
            root.TryGetProperty("exceededTransferLimit", out JsonElement truncated) && truncated.GetBoolean())
            throw new InvalidDataException("Unexpected coordinate system or incomplete source response.");
        var contours = new List<Contour>();
        var ids = new List<string>();
        foreach (JsonElement feature in root.GetProperty("features").EnumerateArray())
        {
            JsonElement attributes = feature.GetProperty("attributes");
            string id = attributes.GetProperty("GEOID").GetString()!, name = attributes.GetProperty("NAME").GetString()!;
            ids.Add(id);
            int ring = 0;
            foreach (JsonElement coordinates in feature.GetProperty("geometry").GetProperty("rings").EnumerateArray())
                contours.Add(new($"{id}/ring-{ring++}", name,
                    coordinates.EnumerateArray().Select(p => new Point2(p[0].GetDouble(), p[1].GetDouble())).ToArray()));
        }
        string[] expected = ["10001", "10003", "10005", "44001", "44003", "44005", "44007", "44009"];
        if (!ids.SequenceEqual(expected) || contours.Count != 9 || contours.Sum(p => p.Points.Length) != 16668)
            throw new InvalidDataException("The complete, frozen eight-county/nine-ring population changed.");
        return contours.ToArray();
    }

    internal sealed record GeometryCheck(bool Simple, string? InvalidReason, int EffectiveVertices,
        double TranslatedShoelace, double ExactDyadicShoelace, double Perimeter);
    private readonly record struct ExactPoint(BigInteger X, BigInteger Y);

    // Independent of the library: exact dyadic coordinates, exhaustive nonadjacent-edge tests and
    // integer determinants. Axis-aligned boxes only discard provably disjoint pairs. This deliberately
    // slow untimed oracle has no library predicates, sweep, symbolic ties or geometry dependency.
    internal static GeometryCheck Check(Point2[] supplied)
    {
        var clean = new List<Point2>();
        foreach (Point2 p in supplied)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
                return new(false, "nonfinite-coordinate", clean.Count, 0, 0, 0);
            if (clean.Count == 0 || !Same(clean[^1], p)) clean.Add(p);
        }
        if (clean.Count > 1 && Same(clean[0], clean[^1])) clean.RemoveAt(clean.Count - 1);
        if (clean.Count < 3) return new(false, "fewer-than-three-effective-vertices", clean.Count, 0, 0, 0);
        Point2[] points = clean.ToArray();
        int exponent = points.SelectMany(p => new[] { Parts(p.X).Exponent, Parts(p.Y).Exponent }).Min();
        BigInteger Integer(double value)
        {
            var (mantissa, e) = Parts(value);
            return mantissa << (e - exponent);
        }
        ExactPoint[] exact = points.Select(p => new ExactPoint(Integer(p.X), Integer(p.Y))).ToArray();
        int n = points.Length;
        BigInteger twice = 0;
        double sum = 0, correction = 0, perimeter = 0;
        Point2 origin = points[0];
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            twice += (exact[i].X - exact[0].X) * (exact[next].Y - exact[0].Y) -
                (exact[i].Y - exact[0].Y) * (exact[next].X - exact[0].X);
            double ax = points[i].X - origin.X, ay = points[i].Y - origin.Y,
                bx = points[next].X - origin.X, by = points[next].Y - origin.Y;
            double term = ax * by - ay * bx, total = sum + term;
            correction += Math.Abs(sum) >= Math.Abs(term) ? (sum - total) + term : (term - total) + sum;
            sum = total;
            double dx = points[next].X - points[i].X, dy = points[next].Y - points[i].Y;
            perimeter += Math.Sqrt(dx * dx + dy * dy);
        }
        double translated = Math.Abs((sum + correction) * .5);
        double exactArea = RoundDyadic(BigInteger.Abs(twice), 2 * exponent - 1);
        GeometryCheck Result(string? error) => new(error is null, error, n, translated, exactArea, perimeter);
        if (twice.IsZero) return Result("zero-shoelace-area");
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n, after = (i + 2) % n;
            if (Orient(exact[i], exact[next], exact[after]) == 0 &&
                (exact[i].X - exact[next].X) * (exact[after].X - exact[next].X) +
                (exact[i].Y - exact[next].Y) * (exact[after].Y - exact[next].Y) > 0)
                return Result($"adjacent-retracing:{i},{next}");
            for (int j = i + 1; j < n; j++)
            {
                int jnext = (j + 1) % n;
                if (next == j || jnext == i || !BoxesOverlap(points[i], points[next], points[j], points[jnext])) continue;
                int a = Orient(exact[i], exact[next], exact[j]), b = Orient(exact[i], exact[next], exact[jnext]),
                    c = Orient(exact[j], exact[jnext], exact[i]), d = Orient(exact[j], exact[jnext], exact[next]);
                if (a * b <= 0 && c * d <= 0) return Result($"nonadjacent-contact:{i},{j}");
            }
        }
        return Result(null);
    }

    // BigInteger's double conversion can discard low bits. Round the exact nonnegative dyadic
    // directly to the binary64 quantum, including subnormals, before converting its <=53-bit
    // significand. This also avoids double rounding at the normal/subnormal boundary.
    private static double RoundDyadic(BigInteger magnitude, int exponent)
    {
        if (magnitude.IsZero) return 0;
        if (magnitude.Sign < 0) throw new ArgumentOutOfRangeException(nameof(magnitude));
        int highBit = checked((int)magnitude.GetBitLength() - 1);
        int valueExponent = checked(highBit + exponent);
        if (valueExponent > 1023) return double.PositiveInfinity;
        if (valueExponent < -1075) return 0;
        int quantum = Math.Max(valueExponent - 52, -1074), shift = quantum - exponent;
        BigInteger rounded;
        if (shift > 0)
        {
            rounded = magnitude >> shift;
            BigInteger remainder = magnitude - (rounded << shift), half = BigInteger.One << (shift - 1);
            if (remainder > half || remainder == half && !rounded.IsEven) rounded++;
        }
        else rounded = magnitude << -shift;
        // A normal carry is exactly 2^53, still representable; scaling may round it to infinity.
        return Math.ScaleB((double)(ulong)rounded, quantum);
    }

    internal static void CheckRounding()
    {
        BigInteger two53 = BigInteger.One << 53;
        void Check(BigInteger numerator, int exponent, double expected)
        {
            if (BitConverter.DoubleToInt64Bits(RoundDyadic(numerator, exponent)) != BitConverter.DoubleToInt64Bits(expected))
                throw new InvalidDataException($"Exact dyadic rounding regression at exponent {exponent}.");
        }
        Check(BigInteger.Zero, 0, 0);
        Check(BigInteger.One, 0, 1);
        Check(two53 + 1, -53, 1); // Halfway, lower significand even.
        Check(two53 + 3, -53, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1) + 2));
        Check((BigInteger.One << 54) - 1, -53, 2); // Carry through an exponent boundary.
        Check((two53 + 1) * 4 - 1, -55, 1); // Immediately below the halfway point.
        Check((two53 + 1) * 4 + 1, -55, Math.BitIncrement(1));
        Check(two53 + 3, -23, Math.ScaleB(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1) + 2), 30));
        Check(BigInteger.One, -1076, 0);
        Check(BigInteger.One, -1075, 0); // Half of the least subnormal ties to zero.
        Check(BigInteger.One, -1074, double.Epsilon);
        Check(new BigInteger(3), -1075, 2 * double.Epsilon);
        Check(new BigInteger(5), -1075, 2 * double.Epsilon);
        Check(two53 - 1, -1075, Math.ScaleB(1, -1022)); // Subnormal-to-normal carry.
        Check(two53 - 1, 971, double.MaxValue);
        Check((BigInteger.One << 55) - 3, 969, double.MaxValue); // Below overflow halfway.
        Check((BigInteger.One << 54) - 1, 970, double.PositiveInfinity); // Overflow halfway.
        Check(BigInteger.One, 1024, double.PositiveInfinity);
    }

    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    private static bool BoxesOverlap(Point2 a, Point2 b, Point2 c, Point2 d) =>
        Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) &&
        Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
    private static int Orient(ExactPoint a, ExactPoint b, ExactPoint c) =>
        ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)).Sign;
    private static (BigInteger Mantissa, int Exponent) Parts(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 2047);
        long mantissa = bits & 0x000f_ffff_ffff_ffffL;
        if (exponent != 0) mantissa |= 1L << 52;
        if (bits < 0) mantissa = -mantissa;
        return (new BigInteger(mantissa), exponent == 0 ? -1074 : exponent - 1075);
    }
}
