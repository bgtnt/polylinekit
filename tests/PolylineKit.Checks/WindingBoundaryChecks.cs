using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Public input boundaries and the distinction between invalid paths and valid empty fills.</summary>
internal static class WindingBoundaryChecks
{
    private static int assertions;
    private static readonly Point2[] Square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
    private static readonly Point2[] Segment = [new(0, 0), new(2, 0)];

    public static int Run()
    {
        assertions = 0;
        InvalidCounts();
        InvalidCoordinates();
        EmptyFills();
        ClosureAndMultiplicity();
        Console.WriteLine($"PASS: {assertions} winding public-boundary checks.");
        return assertions;
    }

    private static void InvalidCounts()
    {
        Reject<ArgumentNullException>("ClosedPath null", () => WindingArea.ClosedPath(null!), "path");
        Reject<ArgumentNullException>("EndpointBridged first null", () => WindingArea.EndpointBridged(null!, Segment), "first");
        Reject<ArgumentNullException>("EndpointBridged second null", () => WindingArea.EndpointBridged(Segment, null!), "second");
        Reject<ArgumentNullException>("FilledRegions first null", () => WindingArea.FilledRegions(null!, Square), "first");
        Reject<ArgumentNullException>("FilledRegions second null", () => WindingArea.FilledRegions(Square, null!), "second");

        (string Name, Point2[] Points)[] invalidClosed =
        [
            ("empty", []), ("point", [new(0, 0)]), ("segment", Segment),
            ("closed segment", [new(0, 0), new(2, 0), new(0, 0)]),
            ("coincident vertices", [new(0, 0), new(0, 0), new(0, 0)]),
            ("duplicate segment endpoints", [new(0, 0), new(0, 0), new(2, 0), new(2, 0), new(0, 0)])
        ];
        foreach (var (name, points) in invalidClosed)
        {
            Reject<ArgumentException>("ClosedPath " + name, () => WindingArea.ClosedPath(points));
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Reject<ArgumentException>($"FilledRegions first {name}, {rule}", () => WindingArea.FilledRegions(points, Square, rule));
                Reject<ArgumentException>($"FilledRegions second {name}, {rule}", () => WindingArea.FilledRegions(Square, points, rule));
            }
        }
        foreach (var (name, points) in invalidClosed.Where(item => item.Name is "empty" or "point" or "coincident vertices"))
        {
            Reject<ArgumentException>("EndpointBridged first " + name, () => WindingArea.EndpointBridged(points, Segment));
            Reject<ArgumentException>("EndpointBridged second " + name, () => WindingArea.EndpointBridged(Segment, points));
        }
        foreach (int invalid in new[] { -1, 2, int.MaxValue })
            Reject<ArgumentOutOfRangeException>("Undefined fill rule " + invalid,
                () => WindingArea.FilledRegions(Square, Square, (PathFillRule)invalid), "fillRule");
    }

    private static void InvalidCoordinates()
    {
        // Even a too-short nonempty input is not a way to silently accept invalid coordinates.
        // Test each argument independently, without prescribing precedence among multiple errors.
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            Math.BitIncrement(1e100), -Math.BitIncrement(1e100) })
        foreach (bool y in new[] { false, true })
        {
            Point2 bad = y ? new(0, value) : new(value, 0);
            string name = $"invalid {(y ? "Y" : "X")}={value:R}";
            foreach (Point2[] points in new[] { new[] { bad }, new[] { new Point2(0, 0), new Point2(1, 0), bad } })
            {
                Reject<ArgumentException>("ClosedPath " + name, () => WindingArea.ClosedPath(points), "path");
                Reject<ArgumentException>("EndpointBridged first " + name, () => WindingArea.EndpointBridged(points, Segment), "first");
                Reject<ArgumentException>("EndpointBridged second " + name, () => WindingArea.EndpointBridged(Segment, points), "second");
                Reject<ArgumentException>("FilledRegions first " + name, () => WindingArea.FilledRegions(points, Square), "first");
                Reject<ArgumentException>("FilledRegions second " + name, () => WindingArea.FilledRegions(Square, points), "second");
            }
        }
        Integrals("inclusive coordinate limit", WindingArea.ClosedPath([new(-1e100, 0), new(0, 0), new(1e100, 0)]), 0, 0, 0, 0);
    }

    private static void EmptyFills()
    {
        Point2[][] emptyFills =
        [
            [new(-1, 1), new(1, 1), new(3, 1)], // Three distinct collinear points.
            [new(0, 0), new(2, 0), new(0, 0), new(2, 0)], // Two positions, four surviving vertices.
            [new(0, 0), new(2, 0), new(2, 2), new(2, 0)], // Noncollinear, fully retraced.
            [new(0, 0), new(double.Epsilon, 0), new(0, double.Epsilon)] // Positive exact area below binary64 range.
        ];
        foreach (Point2[] path in emptyFills)
        {
            Integrals("valid empty closed fill", WindingArea.ClosedPath(path), 0, 0, 0, 0);
            foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            {
                Overlap("two empty fills", WindingArea.FilledRegions(path, path, rule), 0, 0, 0, 0, 0, rule);
                Overlap("empty first fill", WindingArea.FilledRegions(path, Square, rule), 0, 4, 0, 4, 4, rule);
                Overlap("empty second fill", WindingArea.FilledRegions(Square, path, rule), 4, 0, 0, 4, 4, rule);
            }
        }
        // Two individually valid strokes can produce only a segment. This is a zero result,
        // not the invalid one-point input rejected above.
        Integrals("coincident segments", WindingArea.EndpointBridged(Segment, Segment), 0, 0, 0, 0);
        Integrals("oppositely traversed segment", WindingArea.EndpointBridged(Segment, Segment.Reverse().ToArray()), 0, 0, 0, 0);
        Integrals("parallel segments enclose area", WindingArea.EndpointBridged(Segment, [new(0, 2), new(2, 2)]), 4, 4, 4, 4);
        Integrals("default area result", default, 0, 0, 0, 0);
        Overlap("default overlap result", default, 0, 0, 0, 0, 0, PathFillRule.NonZero);
    }

    private static void ClosureAndMultiplicity()
    {
        Point2[] duplicates = [new(-0.0, 0), new(0, -0.0), new(2, 0), new(2, 0),
            new(2, 2), new(0, 2), new(0, 2), new(-0.0, -0.0), new(0, 0)];
        Integrals("duplicate vertices and repeated closure", WindingArea.ClosedPath(duplicates), 4, 4, 4, 4);
        Point2[] twice = [.. Square, .. Square];
        Point2[] bowtie = [new(0, 0), new(2, 2), new(2, 0), new(0, 2)];
        Integrals("signed zero is not empty fill", WindingArea.ClosedPath(bowtie), 2, 2, 2, 0);
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        {
            Overlap("duplicate closure identity", WindingArea.FilledRegions(Square, duplicates, rule), 4, 4, 4, 4, 0, rule);
            Overlap("bowtie self has defined ratios", WindingArea.FilledRegions(bowtie, bowtie, rule), 2, 2, 2, 2, 0, rule);
            double first = rule == PathFillRule.NonZero ? 4 : 0;
            Overlap("fill rule can empty a repeated loop", WindingArea.FilledRegions(twice, Square, rule), first, 4, first, 4, 4 - first, rule);
            Overlap("repeated-loop identity", WindingArea.FilledRegions(twice, twice, rule), first, first, first, first, 0, rule);
        }
        // EndpointBridged closes the combined walk, not each input ring. Repeating the
        // first vertex changes an open stroke and therefore need not preserve its result.
        Point2[] triangle = [Square[0], Square[1], Square[2]];
        Integrals("implicit rings remain open strokes when bridged", WindingArea.EndpointBridged(Square, triangle), 0, 0, 0, 0);
        Integrals("explicitly closed rings form their difference walk", WindingArea.EndpointBridged(
            [.. Square, Square[0]], [.. triangle, triangle[0]]), 2, 2, 2, 2);
        Overlap("independent ring closures", WindingArea.FilledRegions(Square, triangle), 4, 2, 2, 4, 2, PathFillRule.NonZero);
    }

    private static void Integrals(string name, WindingAreaResult result, double nonZero, double evenOdd, double absolute, double signed)
    {
        Equal(name + " NonZero", nonZero, result.NonZero);
        Equal(name + " EvenOdd", evenOdd, result.EvenOdd);
        Equal(name + " AbsoluteWinding", absolute, result.AbsoluteWinding);
        Equal(name + " Signed", signed, result.Signed);
    }

    private static void Overlap(string name, WindingOverlapResult result, double first, double second,
        double intersection, double union, double difference, PathFillRule rule)
    {
        Equal(name + " first", first, result.FirstArea);
        Equal(name + " second", second, result.SecondArea);
        Equal(name + " intersection", intersection, result.IntersectionArea);
        Equal(name + " union", union, result.UnionArea);
        Equal(name + " difference", difference, result.SymmetricDifferenceArea);
        Equal(name + " Jaccard", union > 0 ? difference / union : null, result.JaccardDistance);
        Equal(name + " IoU", union > 0 ? intersection / union : null, result.IntersectionOverUnion);
        assertions++;
        if (result.FillRule != rule) throw new InvalidOperationException(name + ": unexpected fill rule.");
    }

    private static void Equal(string name, double? expected, double? actual)
    {
        assertions++;
        // These expectations are dyadic and exact, or ratios of identical operands.
        // Positive and negative floating-point zero both satisfy the public zero-area contract.
        if (expected != actual || (actual.HasValue && !double.IsFinite(actual.Value)))
            throw new InvalidOperationException($"{name}: expected {expected?.ToString("R") ?? "null"}, actual {actual?.ToString("R") ?? "null"}.");
    }

    private static void Reject<T>(string name, Action call, string? parameter = null) where T : ArgumentException
    {
        assertions++;
        try { call(); }
        catch (ArgumentException error)
        {
            if (error.GetType() != typeof(T) || (parameter != null && error.ParamName != parameter))
                throw new InvalidOperationException($"{name}: unexpected {error.GetType().Name} for '{error.ParamName}'.", error);
            return;
        }
        throw new InvalidOperationException(name + ": expected " + typeof(T).Name + ".");
    }
}
