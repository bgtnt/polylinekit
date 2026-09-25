namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>A strict-order filter; an inconclusive result leaves interval/tie handling to the caller.</summary>
internal static class ScalarOrderFilter
{
    private const double FourU = 4.44089209850062616169452667236328125e-16; // 4 * 2^-53
    private const double BoundLimit = 3.27339060789614187001318969682759915e150; // 2^500
    private const double CoordinateLimit = 1e100;

    internal readonly struct PreparedEdge(double x, double y, double slope, double radius, bool ready)
    {
        internal readonly double X = x, Y = y, Slope = slope, Radius = radius;
        internal readonly bool Ready = ready;
    }

    /// <summary>Requires a certified finite enclosure of the exact endpoint slope, with lowerY &lt; upperY.</summary>
    internal static PreparedEdge Prepare(double x, double lowerY, double upperY, double slopeLo, double slopeHi)
    {
        if (!double.IsFinite(x) || !double.IsFinite(lowerY) || !double.IsFinite(upperY) ||
            Math.Abs(x) > CoordinateLimit || Math.Abs(lowerY) > CoordinateLimit || Math.Abs(upperY) > CoordinateLimit ||
            !(lowerY < upperY) || !double.IsFinite(slopeLo) || !double.IsFinite(slopeHi) || slopeLo > slopeHi)
            return default;

        double slope = slopeLo == slopeHi ? slopeLo : slopeLo + (slopeHi - slopeLo) * .5;
        if (!double.IsFinite(slope)) return default;
        slope = Math.Max(slopeLo, Math.Min(slopeHi, slope));
        double slopeRadius = slopeLo == slopeHi ? 0 : Up(Math.Max(slope - slopeLo, slopeHi - slope));
        double height = Up(upperY - lowerY);
        double productBound = slope == 0 ? 0 : Up(Math.Abs(slope) * height);
        if (!double.IsFinite(slopeRadius) || !double.IsFinite(productBound) || productBound > BoundLimit)
            return default;

        double propagated = slopeRadius == 0 ? 0 : Up(slopeRadius * height);
        double rounding = Up(FourU * Up(Math.Abs(x) + productBound));
        double radius = Up(Up(propagated + rounding) + 2 * double.Epsilon);
        if (!double.IsFinite(radius) || radius > BoundLimit) return default;
        return new(x, lowerY, slope, radius, true);
    }

    /// <summary>
    /// The supplied original endpoint Y must lie in both edges' closed vertical ranges. Accepted order is
    /// the exact sign of xA(Y)-xB(Y); false means only that the scalar filter could not certify strict order.
    /// </summary>
    internal static bool TryOrder(in PreparedEdge a, in PreparedEdge b, double y, out int order)
    {
        order = 0;
        if (!a.Ready || !b.Ready) return false;
        double advanceA = a.Slope * (y - a.Y), advanceB = b.Slope * (y - b.Y);
        double centerA = a.X + advanceA, centerB = b.X + advanceB;
        double difference = centerB - centerA, radius = a.Radius + b.Radius;
        // RN is monotone: RN(exact center difference) > RN(exact radius sum) implies the same strict
        // inequality before rounding. Consequently no extra rounded threshold inflation is needed here.
        if (difference > radius) { order = -1; return true; }
        if (difference < -radius) { order = 1; return true; }
        return false;
    }

    // Preparation only. One adjacent value encloses each correctly rounded nonnegative expression;
    // infinity disables the filter instead of affecting the existing interval evaluation.
    private static double Up(double value) => Math.BitIncrement(value);
}
