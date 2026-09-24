using System;
using System.Collections.Generic;
using System.Numerics;

namespace PolylineKit.ActiveSweep;

/// <summary>
/// Deliberately slow, independent exact-rational oracle for one implicitly closed path.
/// Every input binary64 coordinate is interpreted exactly. All segment pairs supply sweep
/// cuts; active edges are sorted afresh inside every slab. This is a small-input test oracle,
/// not the active-sweep implementation and not a performance comparison candidate.
/// </summary>
internal static class ExactAreaOracle
{
    internal static (double NonZero, double EvenOdd) Measure(IReadOnlyList<Point2> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var vertices = new Vertex[path.Count];
        var cuts = new SortedSet<Rational>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Point2 p = path[i];
            vertices[i] = new Vertex(Rational.FromDouble(p.X), Rational.FromDouble(p.Y));
            cuts.Add(vertices[i].X);
        }
        if (vertices.Length < 2) return (0, 0);

        // Vertical edges bound zero-width slabs. Their endpoint x coordinates are already
        // cuts, so they need neither an active-edge entry nor extra intersection cuts.
        var edges = new List<Edge>(vertices.Length);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vertex a = vertices[i], b = vertices[(i + 1) % vertices.Length];
            if (a.X != b.X) edges.Add(new Edge(a, b));
        }
        for (int i = 0; i < edges.Count; i++)
        {
            Edge a = edges[i];
            for (int j = i + 1; j < edges.Count; j++)
            {
                Edge b = edges[j];
                Rational denominator = Cross(a.Dx, a.Dy, b.Dx, b.Dy);
                // Collinear overlaps change only at input endpoints, already in cuts.
                if (denominator.Sign == 0) continue;
                Rational qx = b.A.X - a.A.X, qy = b.A.Y - a.A.Y;
                Rational t = Cross(qx, qy, b.Dx, b.Dy) / denominator;
                Rational u = Cross(qx, qy, a.Dx, a.Dy) / denominator;
                if (t >= Rational.Zero && t <= Rational.One &&
                    u >= Rational.Zero && u <= Rational.One)
                    cuts.Add(a.A.X + t * a.Dx);
            }
        }

        var orderedCuts = new List<Rational>(cuts);
        var active = new List<AtHeight>(edges.Count);
        var groups = new List<AtHeight>(edges.Count);
        Rational nonZero = Rational.Zero, evenOdd = Rational.Zero;
        for (int slab = 0; slab + 1 < orderedCuts.Count; slab++)
        {
            Rational left = orderedCuts[slab], right = orderedCuts[slab + 1];
            Rational middle = (left + right) / Rational.Two;
            active.Clear();
            foreach (Edge edge in edges)
                if (edge.MinX < middle && middle < edge.MaxX)
                    active.Add(new AtHeight(edge, edge.Y(middle), edge.Dx.Sign));
            active.Sort(static (a, b) => a.Height.CompareTo(b.Height));

            // Coincident active edges must be combined before classifying a gap. This
            // handles repeated/reversed traversals and collinear overlapping pieces.
            groups.Clear();
            foreach (AtHeight item in active)
            {
                int last = groups.Count - 1;
                if (last >= 0 && groups[last].Height == item.Height)
                {
                    AtHeight previous = groups[last];
                    groups[last] = new AtHeight(previous.Edge, previous.Height,
                        checked(previous.Delta + item.Delta));
                }
                else groups.Add(item);
            }
            int winding = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                winding = checked(winding + groups[i].Delta);
                if (i + 1 == groups.Count) break;
                if (winding == 0) continue;
                Edge lower = groups[i].Edge, upper = groups[i + 1].Edge;
                Rational heightLeft = upper.Y(left) - lower.Y(left);
                Rational heightRight = upper.Y(right) - lower.Y(right);
                if (heightLeft.Sign < 0 || heightRight.Sign < 0)
                    throw new InvalidOperationException("Exact oracle missed an intersection cut.");
                Rational area = (right - left) * (heightLeft + heightRight) / Rational.Two;
                nonZero += area;
                if ((winding & 1) != 0) evenOdd += area;
            }
            if (winding != 0)
                throw new InvalidOperationException("A closed path must have zero winding above all active edges.");
        }
        return (nonZero.ToDouble(), evenOdd.ToDouble());
    }

    private static Rational Cross(Rational ax, Rational ay, Rational bx, Rational by) => ax * by - ay * bx;

    private readonly record struct Vertex(Rational X, Rational Y);
    private readonly record struct AtHeight(Edge Edge, Rational Height, int Delta);

    private readonly struct Edge
    {
        internal readonly Vertex A;
        internal readonly Rational Dx, Dy, MinX, MaxX;

        internal Edge(Vertex a, Vertex b)
        {
            A = a;
            Dx = b.X - a.X;
            Dy = b.Y - a.Y;
            MinX = a.X < b.X ? a.X : b.X;
            MaxX = a.X > b.X ? a.X : b.X;
        }

        internal Rational Y(Rational x) => A.Y + (x - A.X) * Dy / Dx;
    }

    /// <summary>Reduced fraction with a strictly positive denominator; no default values are used.</summary>
    private readonly struct Rational : IComparable<Rational>, IEquatable<Rational>
    {
        private readonly BigInteger numerator, denominator;
        internal static readonly Rational Zero = new(BigInteger.Zero, BigInteger.One);
        internal static readonly Rational One = new(BigInteger.One, BigInteger.One);
        internal static readonly Rational Two = new(new BigInteger(2), BigInteger.One);
        internal int Sign => numerator.Sign;

        private Rational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero) throw new DivideByZeroException();
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            if (numerator.IsZero) { this.numerator = BigInteger.Zero; this.denominator = BigInteger.One; return; }
            BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            this.numerator = numerator / divisor;
            this.denominator = denominator / divisor;
        }

        internal static Rational FromDouble(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Coordinates must be finite.");
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            int encodedExponent = (int)((bits >> 52) & 0x7ff);
            ulong mantissa = bits & ((1UL << 52) - 1);
            int exponent = -1074;
            if (encodedExponent != 0)
            {
                mantissa |= 1UL << 52;
                exponent = encodedExponent - 1023 - 52;
            }
            BigInteger n = new(mantissa);
            if ((bits >> 63) != 0) n = -n;
            return exponent >= 0 ? new Rational(n << exponent, BigInteger.One)
                : new Rational(n, BigInteger.One << -exponent);
        }

        /// <summary>Correctly rounds the exact fraction to nearest-even binary64, including subnormals.</summary>
        internal double ToDouble()
        {
            if (numerator.IsZero) return 0;
            BigInteger n = BigInteger.Abs(numerator), d = denominator;
            int exponent = checked((int)(n.GetBitLength() - d.GetBitLength()));
            if (exponent >= 0 ? n < (d << exponent) : (n << -exponent) < d) exponent--;
            ulong sign = numerator.Sign < 0 ? 1UL << 63 : 0;
            if (exponent > 1023) return numerator.Sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
            ulong bits;
            if (exponent < -1022)
            {
                // One integer unit here is the least positive subnormal, 2^-1074.
                // Rounding to 2^52 directly produces the smallest normal encoding.
                bits = (ulong)RoundQuotient(n << 1074, d);
            }
            else
            {
                int shift = 52 - exponent;
                BigInteger significand = shift >= 0 ? RoundQuotient(n << shift, d)
                    : RoundQuotient(n, d << -shift);
                if (significand == (BigInteger.One << 53)) { significand >>= 1; exponent++; }
                if (exponent > 1023) return numerator.Sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
                bits = ((ulong)(exponent + 1023) << 52) | ((ulong)significand - (1UL << 52));
            }
            return BitConverter.Int64BitsToDouble(unchecked((long)(sign | bits)));
        }

        private static BigInteger RoundQuotient(BigInteger n, BigInteger d)
        {
            BigInteger quotient = BigInteger.DivRem(n, d, out BigInteger remainder);
            int direction = (remainder << 1).CompareTo(d);
            if (direction > 0 || (direction == 0 && !quotient.IsEven)) quotient++;
            return quotient;
        }

        public int CompareTo(Rational other) => (numerator * other.denominator).CompareTo(other.numerator * denominator);
        public bool Equals(Rational other) => numerator == other.numerator && denominator == other.denominator;
        public override bool Equals(object? obj) => obj is Rational other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(numerator, denominator);
        public static Rational operator +(Rational a, Rational b) => new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
        public static Rational operator -(Rational a, Rational b) => new(a.numerator * b.denominator - b.numerator * a.denominator, a.denominator * b.denominator);
        public static Rational operator *(Rational a, Rational b) => new(a.numerator * b.numerator, a.denominator * b.denominator);
        public static Rational operator /(Rational a, Rational b) => new(a.numerator * b.denominator, a.denominator * b.numerator);
        public static bool operator ==(Rational a, Rational b) => a.Equals(b);
        public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
        public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
        public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
        public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;
    }
}
