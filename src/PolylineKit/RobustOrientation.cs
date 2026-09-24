using System.Numerics;
using System.Runtime.CompilerServices;
#if NET10_0_OR_GREATER
using System.Runtime.Intrinsics.X86;
#endif

namespace PolylineKit;

/// <summary>
/// Orientation of three indexed input vertices, never zero.
/// </summary>
/// <remarks>
/// A floating-point filter decides the sign when it lies outside Shewchuk's orient2d error bound.
/// Otherwise the determinant sign is computed exactly (floating-point expansions, or integer
/// arithmetic for extreme exponents). An exact zero is resolved by Simulation of Simplicity
/// (Edelsbrunner and Muecke, 1990): vertex p is perturbed by y += e^(2^(2p)), x += e^(2^(2p+1)).
/// Lower indices therefore move further, and a vertex's y moves further than its x. Every tie is
/// decided as for one consistent, infinitesimally perturbed input.
/// </remarks>
internal static class RobustOrientation
{
    /// <summary>(3 + 16 eps) eps for eps = 2^-53.</summary>
    internal const double ErrorBound = 3.3306690738754716e-16;
    private const double Splitter = 134217729.0; // 2^27 + 1
    private const double TinyComponent = 3.8725919148493183e-121; // 2^-400

    [ThreadStatic] private static double[]? terms;
    [ThreadStatic] private static double[]? expansion;

    /// <summary>Floating-point determinant of (b - a) x (c - a), its absolute error bound, and whether its sign is certified.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryFilter(Point2 a, Point2 b, Point2 c, out double det, out double bound)
    {
        double left = (b.X - a.X) * (c.Y - a.Y), right = (b.Y - a.Y) * (c.X - a.X);
        det = left - right;
        double sum = Math.Abs(left) + Math.Abs(right);
        bound = ErrorBound * sum;
        // The relative bound does not cover underflow; tiny magnitudes use the exact path.
        return Math.Abs(det) > bound && sum > 1e-250;
    }

    /// <summary>+1 when c lies to the left of the directed line a->b under the symbolic perturbation, otherwise -1.</summary>
    internal static int Sign(Point2[] v, int a, int b, int c, ref WindingStatistics statistics)
    {
        if (TryFilter(v[a], v[b], v[c], out double det, out _)) return det > 0 ? 1 : -1;
        return Resolve(v, a, b, c, ref statistics);
    }

    /// <summary>
    /// Parameters along a->b of its crossing with the line c->d, measured from a (t) and from b (u = 1 - t),
    /// each from determinants accurate to about one ulp. Returns false when a and b both lie on that line.
    /// </summary>
    /// <remarks>
    /// Only called for segments whose endpoints were decided to lie on opposite sides of the line. Both ends
    /// are kept because 1 - t cannot represent a crossing close to b on a long edge. <paramref name="vertex"/>
    /// is -1 when a lies exactly on the line, +1 when b does and 0 otherwise; a rounded t of 0 or 1 does not
    /// mean the crossing is at a vertex.
    /// </remarks>
    internal static bool CrossingParameter(Point2 c, Point2 d, Point2 a, Point2 b, out double t, out double u, out int vertex)
    {
        if (TryExpansion(c, d, a, out double ea) && TryExpansion(c, d, b, out double eb))
        {
            t = 0; u = 1; vertex = 0;
            if (ea == 0 && eb == 0) return false;
            t = ea / (ea - eb); u = eb / (eb - ea);
            vertex = ea == 0 ? -1 : eb == 0 ? 1 : 0;
            return true;
        }
        return IntegerParameter(c, d, a, b, out t, out u, out vertex);
    }

    /// <summary>Exact, then symbolic, sign for a triple whose floating-point sign is not certified.</summary>
    internal static int Resolve(Point2[] v, int a, int b, int c, ref WindingStatistics statistics)
    {
        statistics.ExactPredicates++;
        int exact = ExactSign(v[a], v[b], v[c]);
        if (exact != 0) return exact;
        statistics.SymbolicTieBreaks++;
        return SymbolicSign(v, a, b, c);
    }

    /// <summary>Exact sign of (b - a) x (c - a) for binary64 inputs.</summary>
    internal static int ExactSign(Point2 a, Point2 b, Point2 c) =>
        TryExpansion(a, b, c, out double value) ? Math.Sign(value) : IntegerSign(a, b, c);

    /// <summary>Accurate value of twice (b-a) x (c-a), including subnormal results.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double TwiceDeterminant(Point2 a, Point2 b, Point2 c)
    {
        // Canonical operand order makes negation bitwise consistent even if two equivalent
        // expansion orders would choose different faithful roundings of a midpoint tie.
        bool reverse = b.X > c.X || (b.X == c.X && b.Y > c.Y);
        if (reverse) (b, c) = (c, b);
        double value;
        if (TryFilteredValue(a, b, c, out double determinant) || TryExpansion(a, b, c, out determinant)) value = 2 * determinant;
        else
        {
            BigInteger[] coordinates = Scaled(a, b, c, c, out int exponent);
            // Round the DOUBLED exact determinant. Rounding det first can discard half an
            // epsilon that becomes representable after doubling (e.g. a side-2^-537 square).
            value = RoundDyadic(Determinant(coordinates, 0, 2, 4), 2 * exponent + 1);
        }
        return reverse ? -value : value;
    }

    // A value filter, not an orientation-sign filter. The quick result has error at most
    // approximately 4u times the product magnitudes. A failed quick filter compensates
    // products and coordinate differences; only uncertainty in the small correction remains.
    // Both accepted paths have error <8u|value|. See docs/winding-numerics.md for the bounds.
    private static bool TryFilteredValue(Point2 a, Point2 b, Point2 c, out double value)
    {
        double bx = b.X - a.X, by = b.Y - a.Y, cx = c.X - a.X, cy = c.Y - a.Y;
        double left = bx * cy, right = by * cx;
        value = left - right;
        double products = Math.Abs(left) + Math.Abs(right);
        if (!(products > 1e-250)) return false;
        if (products <= 1.75 * Math.Abs(value)) return true;

        double bxt = DifferenceTail(b.X, a.X, bx), byt = DifferenceTail(b.Y, a.Y, by);
        double cxt = DifferenceTail(c.X, a.X, cx), cyt = DifferenceTail(c.Y, a.Y, cy);
        double lt = ProductTail(bx, cy, left), rt = ProductTail(by, cx, right);
        double dt = DifferenceTail(left, right, value);
        double correction = (lt - rt) + dt;
        double correctionSize = Math.Abs(lt) + Math.Abs(rt) + Math.Abs(dt);
        if (bxt != 0 || byt != 0 || cxt != 0 || cyt != 0)
        {
            double l0 = bx * cyt, l1 = bxt * cy, l2 = bxt * cyt;
            double r0 = by * cxt, r1 = byt * cx, r2 = byt * cxt;
            correction += ((l0 + l1) + l2) - ((r0 + r1) + r2);
            correctionSize += Math.Abs(l0) + Math.Abs(l1) + Math.Abs(l2) + Math.Abs(r0) + Math.Abs(r1) + Math.Abs(r2);
        }
        else if (lt == 0 && rt == 0 && dt == 0) return true; // Exact arithmetic, including exact cancellation.

        value += correction;
        // Correction arithmetic costs at most 7u*correctionSize; final addition at most
        // (u/(1-u))*|value|. This power-of-two test leaves more than 3u of safety margin.
        return Math.Abs(value) > 1e-250 && correctionSize <= .5 * Math.Abs(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DifferenceTail(double a, double b, double difference)
    {
        double bv = a - difference, av = difference + bv;
        return (a - av) + (bv - b);
    }

    // Used only by the area-value filter. Predicate expansion arithmetic remains unchanged.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ProductTail(double a, double b, double product)
    {
#if NET10_0_OR_GREATER
        if (Fma.IsSupported) return Math.FusedMultiplyAdd(a, b, -product);
#endif
        TwoProduct(a, b, out _, out double tail);
        return tail;
    }

    // Evaluates (b - a) x (c - a) as an exact nonoverlapping expansion and returns its faithfully rounded
    // value, whose sign is exact. Returns false when a component could underflow in a product.
    private static bool TryExpansion(Point2 a, Point2 b, Point2 c, out double value)
    {
        value = 0;
        // Repeated vertices and axis-aligned triples have an exactly zero determinant, even
        // at subnormal magnitudes. Resolve still applies the same symbolic tie-break.
        if ((a.X == b.X && (a.Y == b.Y || a.X == c.X)) ||
            (a.Y == b.Y && a.Y == c.Y) ||
            (a.X == c.X && a.Y == c.Y) ||
            (b.X == c.X && b.Y == c.Y)) return true;
        TwoDiff(b.X, a.X, out double bxh, out double bxl);
        TwoDiff(c.Y, a.Y, out double cyh, out double cyl);
        TwoDiff(b.Y, a.Y, out double byh, out double byl);
        TwoDiff(c.X, a.X, out double cxh, out double cxl);
        if (Tiny(bxh) || Tiny(bxl) || Tiny(cyh) || Tiny(cyl) || Tiny(byh) || Tiny(byl) || Tiny(cxh) || Tiny(cxl))
            return false;
        if (bxl == 0 && cyl == 0 && byl == 0 && cxl == 0)
        {
            // Exact differences: when both products and their difference are exact too (as for integer or
            // coarse dyadic coordinates), the floating-point determinant is already the exact value.
            TwoProduct(bxh, cyh, out double left, out double leftError);
            TwoProduct(byh, cxh, out double right, out double rightError);
            if (leftError == 0 && rightError == 0)
            {
                TwoDiff(left, right, out double difference, out double differenceError);
                if (differenceError == 0) { value = difference; return true; }
            }
            // These four components already represent the exact determinant. Reusing them
            // avoids recomputing both products and six products involving zero tails.
            double[] exactTerms = terms ??= new double[16];
            exactTerms[0] = left; exactTerms[1] = leftError;
            exactTerms[2] = -right; exactTerms[3] = -rightError;
            value = Sum(exactTerms, 4);
            return true;
        }
        double[] t = terms ??= new double[16];
        int n = 0;
        Product(bxh, bxl, cyh, cyl, 1, t, ref n);
        Product(byh, byl, cxh, cxl, -1, t, ref n);
        value = Sum(t, n);
        return true;
    }

    /// <summary>Tie-break for an exactly collinear triple of distinct indices.</summary>
    internal static int SymbolicSign(Point2[] v, int a, int b, int c)
    {
        int i = a, j = b, k = c, parity = 1;
        if (i > j) { (i, j) = (j, i); parity = -parity; }
        if (j > k) { (j, k) = (k, j); parity = -parity; }
        if (i > j) { (i, j) = (j, i); parity = -parity; }
        Point2 pi = v[i], pj = v[j], pk = v[k];
        // Coefficients of the largest perturbation monomials of det(i, j, k), in decreasing order:
        // dy_i: x_k - x_j; dx_i: y_j - y_k; dy_j: x_i - x_k; dx_i * dy_j: +1.
        int s = pk.X.CompareTo(pj.X);
        if (s == 0) s = pj.Y.CompareTo(pk.Y);
        if (s == 0) s = pi.X.CompareTo(pk.X);
        if (s == 0) s = 1;
        return parity * s;
    }

    /// <summary>True when perturbed vertex a lies above perturbed vertex b (lower index wins an exact y tie).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Above(Point2[] v, int a, int b) => v[a].Y > v[b].Y || (v[a].Y == v[b].Y && a < b);

    /// <summary>True when perturbed vertex a lies left of perturbed vertex b (higher index wins an exact x tie).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool LeftOf(Point2[] v, int a, int b) => v[a].X < v[b].X || (v[a].X == v[b].X && a > b);

    private static bool Tiny(double x) => x != 0 && Math.Abs(x) < TinyComponent;

    private static void Product(double ah, double al, double bh, double bl, double sign, double[] t, ref int n)
    {
        int start = n;
        TwoProduct(ah, bh, out t[n], out t[n + 1]);
        n += 2;
        if (bl != 0) { TwoProduct(ah, bl, out t[n], out t[n + 1]); n += 2; }
        if (al != 0)
        {
            TwoProduct(al, bh, out t[n], out t[n + 1]); n += 2;
            if (bl != 0) { TwoProduct(al, bl, out t[n], out t[n + 1]); n += 2; }
        }
        if (sign < 0) for (int i = start; i < n; i++) t[i] = -t[i];
    }

    // Grow-Expansion with zero elimination. The largest nonzero component of the resulting nonoverlapping
    // expansion carries the sign of the exact sum; adding components upward rounds the value faithfully.
    private static double Sum(double[] t, int count)
    {
        double[] e = expansion ??= new double[17];
        int m = 0;
        for (int i = 0; i < count; i++)
        {
            double q = t[i];
            int k = 0;
            for (int j = 0; j < m; j++)
            {
                TwoSum(q, e[j], out double sum, out double error);
                if (error != 0) e[k++] = error;
                q = sum;
            }
            if (q != 0) e[k++] = q;
            m = k;
        }
        double value = 0;
        for (int i = 0; i < m; i++) value += e[i];
        return value;
    }

    private static void TwoSum(double a, double b, out double x, out double y)
    {
        x = a + b;
        double bv = x - a, av = x - bv;
        y = (a - av) + (b - bv);
    }

    private static void TwoDiff(double a, double b, out double x, out double y)
    {
        x = a - b;
        double bv = a - x, av = x + bv;
        y = (a - av) + (bv - b);
    }

    private static void Split(double a, out double hi, out double lo)
    {
        double c = Splitter * a, big = c - a;
        hi = c - big; lo = a - hi;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TwoProduct(double a, double b, out double x, out double y)
    {
        x = a * b;
        Split(a, out double ah, out double al);
        Split(b, out double bh, out double bl);
        double err1 = x - ah * bh, err2 = err1 - al * bh, err3 = err2 - ah * bl;
        y = al * bl - err3;
    }

    // Exact dyadic-rational evaluation for magnitudes where expansion products could underflow.
    private static int IntegerSign(Point2 a, Point2 b, Point2 c)
    {
        BigInteger[] s = Scaled(a, b, c, c);
        return Determinant(s, 0, 2, 4).Sign;
    }

    private static bool IntegerParameter(Point2 c, Point2 d, Point2 a, Point2 b, out double t, out double u, out int vertex)
    {
        BigInteger[] s = Scaled(c, d, a, b);
        BigInteger da = Determinant(s, 0, 2, 4), db = Determinant(s, 0, 2, 6);
        t = 0; u = 1; vertex = 0;
        if (da.IsZero && db.IsZero) return false;
        vertex = da.IsZero ? -1 : db.IsZero ? 1 : 0;
        BigInteger span = BigInteger.Abs(da - db);
        t = Ratio(BigInteger.Abs(da), span); u = Ratio(BigInteger.Abs(db), span);
        return true;
    }

    // numerator / denominator for 0 <= numerator <= denominator, with relative (not absolute) precision:
    // the integer quotient keeps at least 64 significant bits before scaling back by a power of two.
    internal static double Ratio(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero) return 0;
        int shift = 64 + BitLength(denominator) - BitLength(numerator);
        double quotient = (double)((numerator << shift) / denominator);
        // Two half steps avoid a premature underflow of 2^-shift for very small ratios.
        return quotient * Math.Pow(2, -(shift / 2)) * Math.Pow(2, -(shift - shift / 2));
    }

    private static int BitLength(BigInteger positive)
    {
        byte[] bytes = positive.ToByteArray();
        int top = bytes.Length - 1;
        while (top > 0 && bytes[top] == 0) top--;
        int bits = top * 8;
        for (int value = bytes[top]; value != 0; value >>= 1) bits++;
        return bits;
    }

    // (p_b - p_a) x (p_c - p_a) for points stored as consecutive (x, y) pairs starting at the given offsets.
    private static BigInteger Determinant(BigInteger[] s, int a, int b, int c) =>
        (s[b] - s[a]) * (s[c + 1] - s[a + 1]) - (s[b + 1] - s[a + 1]) * (s[c] - s[a]);

    // Coordinates of four points as integers sharing one power-of-two scale.
    private static BigInteger[] Scaled(Point2 p0, Point2 p1, Point2 p2, Point2 p3) =>
        Scaled(p0, p1, p2, p3, out _);

    private static BigInteger[] Scaled(Point2 p0, Point2 p1, Point2 p2, Point2 p3, out int minimum)
    {
        double[] values = [p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y];
        var mantissa = new long[8];
        var exponent = new int[8];
        minimum = int.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            long bits = BitConverter.DoubleToInt64Bits(values[i]);
            int biased = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            mantissa[i] = biased == 0 ? fraction : fraction | (1L << 52);
            exponent[i] = biased == 0 ? -1074 : biased - 1075;
            if (bits < 0) mantissa[i] = -mantissa[i];
            if (mantissa[i] != 0) minimum = Math.Min(minimum, exponent[i]);
        }
        var scaled = new BigInteger[8];
        for (int i = 0; i < 8; i++)
            scaled[i] = mantissa[i] == 0 ? BigInteger.Zero : new BigInteger(mantissa[i]) << (exponent[i] - minimum);
        return scaled;
    }

    // Correctly rounded value of integer * 2^exponent, with one nearest/even rounding at
    // the final binary64 precision (or the fixed 2^-1074 subnormal quantum). The coordinate
    // cap guarantees a finite answer. No intermediate conversion may underflow first.
    private static double RoundDyadic(BigInteger integer, int exponent)
    {
        if (integer.IsZero) return 0;
        int sign = integer.Sign;
        BigInteger magnitude = BigInteger.Abs(integer);
        int shift = Math.Max(0, Math.Max(BitLength(magnitude) - 53, -1074 - exponent));
        if (shift != 0)
        {
            BigInteger rounded = magnitude >> shift;
            BigInteger remainder = magnitude - (rounded << shift);
            BigInteger halfway = BigInteger.One << (shift - 1);
            if (remainder > halfway || (remainder == halfway && !rounded.IsEven)) rounded++;
            magnitude = rounded;
        }
        int scale = exponent + shift;
        double unit = scale < -1022
            ? BitConverter.Int64BitsToDouble(1L << (scale + 1074))
            : BitConverter.Int64BitsToDouble((long)(scale + 1023) << 52);
        double value = (double)magnitude * unit;
        return sign < 0 ? -value : value;
    }
}
