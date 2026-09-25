using PolylineKit;

/// <summary>
/// Example-only arithmetic for externally established geometric preconditions. None of these
/// methods certifies simplicity, containment, or disjointness; a failed Try call needs the
/// general engine. Coordinates are the exact real values of the supplied binary64 numbers.
/// </summary>
internal static class SpecializedArea
{
    internal readonly record struct AreaValues(double First, double Second, double Xor, double Union)
    {
        internal double Intersection { get; init; }
        internal double? Jaccard => Union > 0 ? Xor / Union : null;
    }

    internal readonly record struct Interval(double Lower, double Upper);
    internal readonly record struct AreaBounds(Interval First, Interval Second, Interval Xor,
        Interval Union, Interval Jaccard);
    internal enum Decision { Unresolved, Accept, Reject }

    private const double CoordinateLimit = 1e100;
    // These estimates deliberately decline extreme products. Conservative bounds below do
    // not use this restriction. Keeping every nonzero product above 2^-900 also keeps its
    // exact FMA residual (at most 106 significant product bits) out of gradual underflow.
    private static readonly double MinimumProduct = Math.ScaleB(1, -900);
    private static Interval Invalid => new(double.NaN, double.NaN);

    /// <summary>
    /// Maps an implicit closed contour to one same-direction cyclic subsequence. Equality is
    /// exact coordinate equality; duplicate original positions are rejected as ambiguous.
    /// This is correspondence validation, not a topology or simplification certificate.
    /// </summary>
    internal static bool TryMapRetained(Point2[] original, Point2[] simplified, out int[] indices)
    {
        indices = [];
        if (!ValidContour(original) || !ValidContour(simplified) || simplified.Length > original.Length)
            return false;
        var lookup = new Dictionary<(double, double), int>(original.Length);
        for (int i = 0; i < original.Length; i++)
            if (!lookup.TryAdd((original[i].X, original[i].Y), i)) return false;
        var map = new int[simplified.Length];
        for (int i = 0; i < map.Length; i++)
            if (!lookup.TryGetValue((simplified[i].X, simplified[i].Y), out map[i])) return false;
        if (!ValidMap(original, simplified, map)) return false;
        indices = map;
        return true;
    }

    /// <summary>
    /// REQUIRES two simple contours with one filled region contained in the other. Computes
    /// translated compensated areas, preserving their low components during subtraction.
    /// This numerical estimate is not a conservative predicate or a general XOR algorithm.
    /// Degenerate areas and extreme intermediate products fail open.
    /// </summary>
    internal static bool TryNestedSimple(Point2[] original, Point2[] simplified, out AreaValues values)
    {
        values = default;
        if (!ValidContour(original) || !ValidContour(simplified) ||
            !TryArea2(original, out var a) || !TryArea2(simplified, out var b)) return false;
        double first = a.Value * 0.5, second = b.Value * 0.5;
        var delta = new Compensated();
        delta.Add(a); delta.Add(b, -1);
        double xor = Math.Abs(delta.Value) * 0.5;
        values = new(first, second, xor, Math.Max(first, second))
        { Intersection = Math.Min(first, second) };
        return ValidValues(values);
    }

    /// <summary>
    /// REQUIRES simple original/result contours and simple replacement pockets with disjoint
    /// interiors, each toggling filled membership exactly once. Under those supplied set
    /// preconditions XOR is the sum of pocket areas. Simplicity alone is insufficient.
    /// A chain includes both retained endpoints and closes along the replacement chord.
    /// </summary>
    internal static bool TryDisjointSimplePockets(Point2[] original, Point2[] simplified, int[] indices,
        out AreaValues values)
    {
        values = default;
        if (!ValidContour(original) || !ValidContour(simplified) || !ValidMap(original, simplified, indices) ||
            !TryArea2(original, out var a) || !TryArea2(simplified, out var b)) return false;
        var difference = new Compensated();
        for (int j = 0; j < indices.Length; j++)
        {
            int start = indices[j], steps = Steps(start, indices[(j + 1) % indices.Length], original.Length);
            var pocket = new Compensated();
            for (int k = 1; k < steps; k++)
            {
                if (!TryTriangle2(original[start], original[Advance(start, k, original.Length)],
                    original[Advance(start, k + 1, original.Length)], ref pocket)) return false;
            }
            difference.Add(pocket, pocket.Value < 0 ? -1 : 1);
        }
        var union = new Compensated();
        union.Add(a); union.Add(b); union.Add(difference);
        var intersection = new Compensated();
        intersection.Add(a); intersection.Add(b); intersection.Add(difference, -1);
        values = new(a.Value * 0.5, b.Value * 0.5, difference.Value * 0.5, union.Value * 0.25)
        { Intersection = intersection.Value * 0.25 };
        return ValidValues(values);
    }

    /// <summary>
    /// REQUIRES two simple contours and an exact same-direction cyclic retained mapping.
    /// L=|A-B| is a lower bound for XOR. The difference of the two contour winding functions
    /// is the sum of oriented replacement fan triangles. At every point of the filled XOR,
    /// its absolute value is one; its integral is bounded by the sum of triangle areas.
    /// Thus U remains valid when pockets self-intersect or the two orientations differ.
    /// No simple/disjoint pocket or containment assumption is needed for these bounds.
    /// Every primitive rounds outward; overflow/undefined area gives false, never a decision.
    /// </summary>
    internal static bool TryBounds(Point2[] original, Point2[] simplified, int[] indices, out AreaBounds bounds)
    {
        bounds = default;
        if (!ValidContour(original) || !ValidContour(simplified) || !ValidMap(original, simplified, indices))
            return false;
        Interval first = AreaInterval(original), second = AreaInterval(simplified);
        Interval sum = Add(first, second), lower = Abs(Subtract(first, second));
        Interval fan = Exact(0);
        for (int j = 0; j < indices.Length; j++)
        {
            int start = indices[j], steps = Steps(start, indices[(j + 1) % indices.Length], original.Length);
            for (int k = 1; k < steps; k++)
                fan = Add(fan, Abs(TriangleInterval(original[start], original[Advance(start, k, original.Length)],
                    original[Advance(start, k + 1, original.Length)])));
        }
        Interval xor = new(Math.Max(0, lower.Lower), Math.Min(fan.Upper, sum.Upper));
        Interval union = ScaleHalf(Add(sum, xor));
        union = new(Math.Max(union.Lower, Math.Max(first.Lower, Math.Max(second.Lower, xor.Lower))),
            Math.Min(union.Upper, sum.Upper));
        if (!Valid(first) || !Valid(second) || !Valid(xor) || !Valid(union) || union.Lower <= 0)
            return false;

        // J=2D/(S+D), increasing in D and decreasing in S. Combining interval endpoints
        // independently is conservative even though the true areas are correlated.
        Interval low = DividePositive(Multiply(Exact(2), Exact(xor.Lower)), Add(Exact(sum.Upper), Exact(xor.Lower)));
        Interval high = DividePositive(Multiply(Exact(2), Exact(xor.Upper)), Add(Exact(sum.Lower), Exact(xor.Upper)));
        Interval jaccard = new(Math.Max(0, low.Lower), Math.Min(1, high.Upper));
        if (!Valid(jaccard)) return false;
        bounds = new(first, second, xor, union, jaccard);
        return true;
    }

    /// <summary>Accept means XOR/union &lt;= threshold; Reject means strictly greater.</summary>
    internal static Decision Decide(AreaBounds bounds, double threshold)
    {
        if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1 || !Valid(bounds.First) ||
            !Valid(bounds.Second) || !Valid(bounds.Xor) || !Valid(bounds.Union) || bounds.Union.Lower <= 0 ||
            !Valid(bounds.Jaccard) || bounds.Jaccard.Lower < 0 || bounds.Jaccard.Upper > 1)
            return Decision.Unresolved;
        if (bounds.Jaccard.Upper <= threshold) return Decision.Accept;
        return bounds.Jaccard.Lower > threshold ? Decision.Reject : Decision.Unresolved;
    }

    private static bool ValidContour(Point2[]? points)
    {
        if (points is null || points.Length < 3) return false;
        foreach (var p in points)
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > CoordinateLimit ||
                Math.Abs(p.Y) > CoordinateLimit) return false;
        return true;
    }

    private static bool ValidMap(Point2[] original, Point2[] simplified, int[]? indices)
    {
        if (indices is null || indices.Length != simplified.Length || indices.Length > original.Length) return false;
        long total = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i], next = indices[(i + 1) % indices.Length];
            if ((uint)index >= (uint)original.Length || (uint)next >= (uint)original.Length ||
                original[index].X != simplified[i].X || original[index].Y != simplified[i].Y) return false;
            int steps = Steps(index, next, original.Length);
            if (steps == 0) return false;
            total += steps;
        }
        return total == original.Length;
    }

    private static int Steps(int first, int second, int count) => second >= first ? second - first : count - first + second;
    private static int Advance(int start, int steps, int count) => steps < count - start ? start + steps : steps - (count - start);
    private static bool ValidValues(AreaValues v) => double.IsFinite(v.First) && double.IsFinite(v.Second) &&
        double.IsFinite(v.Xor) && double.IsFinite(v.Union) && double.IsFinite(v.Intersection) &&
        v.First > 0 && v.Second > 0 && v.Xor >= 0 && v.Union > 0 && v.Intersection >= 0 &&
        v.Xor <= v.Union && v.Intersection <= Math.Min(v.First, v.Second);

    private static bool TryArea2(Point2[] points, out Compensated sum)
    {
        sum = new();
        for (int i = 1; i < points.Length - 1; i++)
            if (!TryTriangle2(points[0], points[i], points[i + 1], ref sum)) return false;
        if (sum.Value < 0) sum.Negate();
        return double.IsFinite(sum.Value) && sum.Value > 0;
    }

    private static bool TryTriangle2(Point2 o, Point2 a, Point2 b, ref Compensated sum)
    {
        double ax = a.X - o.X, ay = a.Y - o.Y, bx = b.X - o.X, by = b.Y - o.Y;
        double axt = DifferenceTail(a.X, o.X, ax), ayt = DifferenceTail(a.Y, o.Y, ay);
        double bxt = DifferenceTail(b.X, o.X, bx), byt = DifferenceTail(b.Y, o.Y, by);
        // FMA captures the product residual; coordinate subtraction tails are retained too.
        // Sum products separately, so cancellation does not discard their low components.
        return AddProduct(ax, by, 1, ref sum) && AddProduct(ay, bx, -1, ref sum) &&
            AddProduct(ax, byt, 1, ref sum) && AddProduct(axt, by, 1, ref sum) &&
            AddProduct(axt, byt, 1, ref sum) && AddProduct(ay, bxt, -1, ref sum) &&
            AddProduct(ayt, bx, -1, ref sum) && AddProduct(ayt, bxt, -1, ref sum);
    }

    private static bool AddProduct(double a, double b, int sign, ref Compensated sum)
    {
        if (a == 0 || b == 0) return true;
        double p = a * b;
        if (!double.IsFinite(p) || Math.Abs(p) < MinimumProduct) return false;
        sum.Add(sign * p);
        sum.Add(sign * Math.FusedMultiplyAdd(a, b, -p));
        return true;
    }

    private static double DifferenceTail(double a, double b, double head)
    {
        double bv = a - head;
        return (a - (head + bv)) + (bv - b);
    }

    private struct Compensated
    {
        private double head, tail;
        internal readonly double Value => head + tail;
        internal void Add(double value)
        {
            double next = head + value;
            tail += Math.Abs(head) >= Math.Abs(value) ? (head - next) + value : (value - next) + head;
            head = next;
        }
        internal void Add(Compensated value, int sign = 1) { Add(sign * value.head); Add(sign * value.tail); }
        internal void Negate() { head = -head; tail = -tail; }
    }

    private static Interval AreaInterval(Point2[] points)
    {
        Interval sum = Exact(0);
        for (int i = 1; i < points.Length - 1; i++)
            sum = Add(sum, TriangleInterval(points[0], points[i], points[i + 1]));
        return Abs(sum);
    }

    private static Interval TriangleInterval(Point2 o, Point2 a, Point2 b)
    {
        Interval ax = Subtract(Exact(a.X), Exact(o.X)), ay = Subtract(Exact(a.Y), Exact(o.Y));
        Interval bx = Subtract(Exact(b.X), Exact(o.X)), by = Subtract(Exact(b.Y), Exact(o.Y));
        return ScaleHalf(Subtract(Multiply(ax, by), Multiply(ay, bx)));
    }

    private static Interval Exact(double value) => new(value, value);
    private static bool Valid(Interval a) => double.IsFinite(a.Lower) && double.IsFinite(a.Upper) && a.Lower <= a.Upper;
    private static bool Zero(Interval a) => a.Lower == 0 && a.Upper == 0;
    private static Interval Outward(double lower, double upper)
    {
        if (!double.IsFinite(lower) || !double.IsFinite(upper)) return Invalid;
        return new(Math.BitDecrement(lower), Math.BitIncrement(upper));
    }
    private static Interval Add(Interval a, Interval b)
    {
        if (!Valid(a) || !Valid(b)) return Invalid;
        if (Zero(a)) return b;
        if (Zero(b)) return a;
        return Outward(a.Lower + b.Lower, a.Upper + b.Upper);
    }
    private static Interval Subtract(Interval a, Interval b)
    {
        if (!Valid(a) || !Valid(b)) return Invalid;
        if (Zero(b)) return a;
        if (a.Lower == a.Upper && b.Lower == b.Upper && a.Lower == b.Lower) return Exact(0);
        return Outward(a.Lower - b.Upper, a.Upper - b.Lower);
    }
    private static Interval Multiply(Interval a, Interval b)
    {
        if (!Valid(a) || !Valid(b)) return Invalid;
        if (Zero(a) || Zero(b)) return Exact(0);
        double ll = a.Lower * b.Lower, lu = a.Lower * b.Upper;
        double ul = a.Upper * b.Lower, uu = a.Upper * b.Upper;
        return Outward(Math.Min(Math.Min(ll, lu), Math.Min(ul, uu)), Math.Max(Math.Max(ll, lu), Math.Max(ul, uu)));
    }
    private static Interval ScaleHalf(Interval a) => Multiply(a, Exact(0.5));
    private static Interval Abs(Interval a)
    {
        if (!Valid(a)) return Invalid;
        if (a.Lower >= 0) return a;
        if (a.Upper <= 0) return new(-a.Upper, -a.Lower);
        return new(0, Math.Max(-a.Lower, a.Upper));
    }
    private static Interval DividePositive(Interval numerator, Interval denominator)
    {
        if (!Valid(numerator) || !Valid(denominator) || numerator.Lower < 0 || denominator.Lower <= 0) return Invalid;
        if (Zero(numerator)) return Exact(0);
        var result = Outward(numerator.Lower / denominator.Upper, numerator.Upper / denominator.Lower);
        return new(Math.Max(0, result.Lower), result.Upper);
    }
}
