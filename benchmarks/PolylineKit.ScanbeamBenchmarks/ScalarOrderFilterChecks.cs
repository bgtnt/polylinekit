using System.Numerics;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Checks accepted scalar order signs against exact binary64 dyadic arithmetic.</summary>
internal static class ScalarOrderFilterChecks
{
    internal static void Run()
    {
        int checks = 0, accepted = 0, rejected = 0;
        Point2 a0 = new(-3, -2), a1 = new(4, 6), b0 = new(5, -4), b1 = new(9, 8);
        foreach (bool swap in new[] { false, true })
        foreach (bool reverseA in new[] { false, true })
        foreach (bool reverseB in new[] { false, true })
        {
            Point2 x0 = reverseA ? a1 : a0, x1 = reverseA ? a0 : a1;
            Point2 z0 = reverseB ? b1 : b0, z1 = reverseB ? b0 : b1;
            if (swap) (x0, x1, z0, z1) = (z0, z1, x0, x1);
            Check("ordinary non-axis-aligned order", x0, x1, z0, z1, .5, mustAccept: true);
        }
        Check("vertical separated edges", new(-4, -2), new(-4, 3), new(5, -3), new(5, 4), 0, mustAccept: true);
        Check("common translation", new(1e12 - 3, -2), new(1e12 + 4, 6),
            new(1e12 + 5, -4), new(1e12 + 9, 8), .5, mustAccept: true);

        // Exact equality is not an order decision. The interval/tie path remains responsible
        // for slope ordering at endpoints and unsupported constructed contacts.
        Check("interior crossing tie", new(0, -1), new(2, 1), new(2, -2), new(0, 2), 0, mustReject: true);
        Check("shared lower endpoint", new(1, 0), new(3, 2), new(1, 0), new(-5, 8), 0, mustReject: true);
        Check("collinear partial supports", new(-2, -2), new(2, 2), new(-4, -4), new(4, 4), 1, mustReject: true);
        Check("signed zero tie", new(-0.0, -1), new(0, 1), new(0, -2), new(-0.0, 2), -0.0, mustReject: true);
        Check("exact upper/lower contact", new(-2, -2), new(1, 0), new(1, 0), new(4, 4), 0, mustReject: true);

        foreach (double center in new[] { 0, 1, 1e8, 1e16, 1e90 })
        {
            double ulp = Math.BitIncrement(center) - center;
            for (int offset = -2; offset <= 2; offset++)
            {
                Point2 left0 = new(center - 4 * ulp, -1), left1 = new(center + 4 * ulp, 1);
                Point2 right0 = new(center - 4 * ulp + offset * ulp, -1);
                Point2 right1 = new(center + 4 * ulp + offset * ulp, 1);
                Check("near-equal X values", left0, left1, right0, right1, 0, mustReject: offset == 0);
            }
        }

        // Widely separated x/y exponents exercise intermediate products, divisions,
        // subnormal endpoints and cancellation. No acceptance fraction is presumed.
        int[] exponents = [-1074, -1000, -700, -500, -100, 0, 50, 100, 250, 300];
        var random = new Random(932_871);
        for (int iteration = 0; iteration < 2000; iteration++)
        {
            int xExponent = exponents[random.Next(exponents.Length)];
            int yExponent = exponents[random.Next(exponents.Length)];
            double ha = Math.ScaleB(random.Next(1, 129), yExponent);
            double hb = Math.ScaleB(random.Next(1, 129), yExponent);
            double y = Math.ScaleB(random.Next(-8, 9) * Math.Min(ha, hb), -4);
            Point2 p = new(Math.ScaleB(random.Next(-1024, 1025), xExponent), -ha);
            Point2 q = new(Math.ScaleB(random.Next(-1024, 1025), xExponent), ha);
            Point2 r = new(Math.ScaleB(random.Next(-1024, 1025), xExponent), -hb);
            Point2 s = new(Math.ScaleB(random.Next(-1024, 1025), xExponent), hb);
            Check("random exact dyadic order " + iteration, p, q, r, s, y);
            if (iteration % 100 == 0) Check("swapped dyadic order " + iteration, r, s, p, q, y);
        }
        Check("tiny slope underflow", new(-1e-300, -1e100), new(1e-300, 1e100),
            new(2e-300, -1e100), new(4e-300, 1e100), 0);
        Check("slope overflow rejection", new(-1e100, -double.Epsilon), new(1e100, double.Epsilon),
            new(1, -double.Epsilon), new(2, double.Epsilon), 0, mustReject: true);
        Check("least-subnormal X separation", new(0, -1), new(double.Epsilon, 1),
            new(2 * double.Epsilon, -1), new(3 * double.Epsilon, 1), 0);
        Check("half-epsilon product underflow", new(0, 0), new(double.Epsilon, 2 * double.Epsilon),
            new(2 * double.Epsilon, 0), new(3 * double.Epsilon, 2 * double.Epsilon), double.Epsilon);
        Check("reversed half-epsilon product underflow", new(double.Epsilon, 2 * double.Epsilon), new(0, 0),
            new(3 * double.Epsilon, 2 * double.Epsilon), new(2 * double.Epsilon, 0), double.Epsilon);
        Check("half-epsilon exact crossing tie", new(-0.0, 0), new(double.Epsilon, 2 * double.Epsilon),
            new(double.Epsilon, 0), new(0, 2 * double.Epsilon), double.Epsilon, mustReject: true);

        Check("horizontal input", new(0, 1), new(2, 1), b0, b1, 1, mustReject: true);
        Check("level outside first edge", a0, a1, b0, b1, 7, mustReject: true);
        Check("level outside second edge", a0, a1, new(5, -1), new(9, 1), 2, mustReject: true);
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            Math.BitIncrement(1e100), -Math.BitIncrement(1e100) })
        {
            Check("invalid X coordinate", new(bad, -2), a1, b0, b1, 0, mustReject: true);
            Check("invalid Y coordinate", new(-3, bad), a1, b0, b1, 0, mustReject: true);
            Check("invalid query level", a0, a1, b0, b1, bad, mustReject: true);
        }
        Require(accepted >= 10 && rejected >= 10, "Direct filter checks must exercise both accepted and rejected decisions.");
        Console.WriteLine($"Scalar endpoint order filter: {checks} exact-sign controls; {accepted} accepted and {rejected} rejected.");

        void Check(string name, Point2 p, Point2 q, Point2 r, Point2 s, double y, bool mustAccept = false, bool mustReject = false)
        {
            bool certified = GuardedDoubleSweep.TryScalarOrderAtY(p, q, r, s, y, out int order);
            if (certified)
            {
                accepted++;
                Require(!mustReject, name + ": accepted an excluded or exact-tie comparison.");
                Require(Eligible(p, q, r, s, y), name + ": accepted outside the filter domain.");
                int exact = ExactOrder(p, q, r, s, y);
                Require(order is -1 or 1 && order == exact, name + ": accepted sign disagrees with exact dyadic arithmetic.");
            }
            else
            {
                rejected++;
                Require(!mustAccept, name + ": ordinary separated values must exercise the scalar fast path.");
                Require(order == 0, name + ": rejected comparison left a usable-looking nonzero sign.");
            }
            checks++;
        }
    }

    private static bool Eligible(Point2 a, Point2 b, Point2 c, Point2 d, double y)
    {
        foreach (double value in new[] { a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y, y })
            if (!double.IsFinite(value) || Math.Abs(value) > 1e100) return false;
        return a.Y != b.Y && c.Y != d.Y && y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y) &&
            y >= Math.Min(c.Y, d.Y) && y <= Math.Max(c.Y, d.Y);
    }

    // Convert every original coordinate to one exact dyadic integer grid. Compare the
    // two line evaluations by cross multiplication with positive Y denominators.
    // No binary64 arithmetic, divisions, slope estimates or filter bounds enter the oracle.
    private static int ExactOrder(Point2 a, Point2 b, Point2 c, Point2 d, double y)
    {
        if (a.Y > b.Y) (a, b) = (b, a);
        if (c.Y > d.Y) (c, d) = (d, c);
        double[] values = [a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y, y];
        var parts = values.Select(Dyadic).ToArray();
        int exponent = parts.Where(p => !p.Mantissa.IsZero).Select(p => p.Exponent).DefaultIfEmpty(0).Min();
        BigInteger[] v = parts.Select(p => p.Mantissa.IsZero ? BigInteger.Zero : p.Mantissa << (p.Exponent - exponent)).ToArray();
        BigInteger denominatorA = v[3] - v[1], denominatorB = v[7] - v[5];
        BigInteger numeratorA = v[0] * denominatorA + (v[2] - v[0]) * (v[8] - v[1]);
        BigInteger numeratorB = v[4] * denominatorB + (v[6] - v[4]) * (v[8] - v[5]);
        return (numeratorA * denominatorB - numeratorB * denominatorA).Sign;
    }

    private static (BigInteger Mantissa, int Exponent) Dyadic(double value)
    {
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        int encoded = (int)((bits >> 52) & 2047);
        BigInteger mantissa = bits & ((1UL << 52) - 1);
        if (encoded != 0) mantissa += BigInteger.One << 52;
        if ((bits >> 63) != 0) mantissa = -mantissa;
        return (mantissa, encoded == 0 ? -1074 : encoded - 1075);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
