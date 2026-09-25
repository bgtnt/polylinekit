using System.Collections;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>The intersection-only entry point retains the filled-region input and workspace contract.</summary>
internal static class WindingIntersectionChecks
{
    private static int passed;
    private static readonly Point2[] Square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];

    internal static int Run()
    {
        passed = 0;
        CheckInputs(); CheckAnalytic(); CheckNestedCalls(); CheckInputOwnership();
        Console.WriteLine($"PASS: {passed} intersection-only public-contract checks.");
        return passed;
    }

    private static void CheckInputs()
    {
        Reject<ArgumentNullException>(() => WindingArea.IntersectionArea(null!, Square), "first");
        Reject<ArgumentNullException>(() => WindingArea.IntersectionArea(Square, null!), "second");
        Point2[][] invalid = [[], [new(0, 0)], [new(0, 0), new(2, 0)],
            [new(0, 0), new(2, 0), new(0, 0)], [new(0, 0), new(0, 0), new(0, 0)],
            [new(0, 0), new(0, 0), new(2, 0), new(2, 0), new(0, 0)]];
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        foreach (Point2[] path in invalid)
        {
            Reject<ArgumentException>(() => WindingArea.IntersectionArea(path, Square, rule));
            Reject<ArgumentException>(() => WindingArea.IntersectionArea(Square, path, rule));
        }
        foreach (int rule in new[] { -1, 2, int.MaxValue })
            Reject<ArgumentOutOfRangeException>(() => WindingArea.IntersectionArea(Square, Square, (PathFillRule)rule), "fillRule");
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            Math.BitIncrement(1e100), -Math.BitIncrement(1e100) })
        foreach (bool y in new[] { false, true })
        {
            Point2 bad = y ? new(0, value) : new(value, 0);
            foreach (Point2[] path in new[] { new[] { bad }, new[] { new Point2(0, 0), new Point2(1, 0), bad } })
            {
                Reject<ArgumentException>(() => WindingArea.IntersectionArea(path, Square), "first");
                Reject<ArgumentException>(() => WindingArea.IntersectionArea(Square, path), "second");
            }
        }
        Equal("inclusive coordinate limit", 0,
            WindingArea.IntersectionArea([new(-1e100, 0), new(0, 0), new(1e100, 0)], Square));
        Equal("default fill rule is NonZero", 4, WindingArea.IntersectionArea([.. Square, .. Square], Square));
    }

    private static void CheckAnalytic()
    {
        Point2[] inner = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        Point2[] hole = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0),
            new(1, 1), new(1, 3), new(3, 3), new(3, 1), new(1, 1)];
        Point2[] nested = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0), .. inner, inner[0]];
        (string Name, Point2[] A, Point2[] B, double NonZero, double EvenOdd)[] cases =
        [
            ("identity", Square, Square, 4, 4),
            ("partial overlap", Square, [new(1, 0), new(3, 0), new(3, 2), new(1, 2)], 2, 2),
            ("nested", [new(-1, -1), new(3, -1), new(3, 3), new(-1, 3)], Square, 4, 4),
            ("distant", Square, Square.Select(p => new Point2(p.X + 1e12, p.Y + 1e12)).ToArray(), 0, 0),
            ("edge contact", Square, [new(2, 0), new(4, 0), new(4, 2), new(2, 2)], 0, 0),
            ("point contact", Square, [new(2, 2), new(4, 2), new(4, 4), new(2, 4)], 0, 0),
            ("bowtie signed zero", [new(0, 0), new(2, 2), new(2, 0), new(0, 2)], Square, 2, 2),
            ("twice traversed", [.. Square, .. Square], Square, 4, 0),
            ("hole", hole, inner, 0, 0), ("winding-two interior", nested, inner, 4, 0),
            ("repeated closure", [.. Square, Square[0], Square[0]], Square, 4, 4),
            ("collinear", [new(-1, 0), new(0, 0), new(1, 0)], Square, 0, 0),
            ("retraced", [new(0, 0), new(2, 0), new(2, 2), new(2, 0)], Square, 0, 0),
            ("two surviving positions", [new(0, 0), new(1, 1), new(0, 0), new(1, 1)], Square, 0, 0),
            ("underflowing fill", [new(0, 0), new(double.Epsilon, 0), new(0, double.Epsilon)], Square, 0, 0)
        ];
        foreach (var item in cases)
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        for (int reverse = 0; reverse < 4; reverse++)
        for (int swap = 0; swap < 2; swap++)
        {
            Point2[] a = (Point2[])item.A.Clone(), b = (Point2[])item.B.Clone();
            if ((reverse & 1) != 0) Array.Reverse(a);
            if ((reverse & 2) != 0) Array.Reverse(b);
            if (swap != 0) (a, b) = (b, a);
            double expected = rule == PathFillRule.NonZero ? item.NonZero : item.EvenOdd;
            Equal($"{item.Name}, {rule}, reverse={reverse}, swap={swap}", expected, WindingArea.IntersectionArea(a, b, rule));
        }
        // Cyclic starts must not change a self-overlapping ring's independently filled area.
        Point2[] bowtie = cases[6].A;
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        for (int offset = 0; offset < bowtie.Length; offset++)
            Equal("cyclic bowtie", 2, WindingArea.IntersectionArea(
                Enumerable.Range(0, bowtie.Length).Select(i => bowtie[(i + offset) % bowtie.Length]).ToArray(), Square, rule));
    }

    private static void CheckNestedCalls()
    {
        Point2[] other = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
        Action[] nested =
        [
            () => WindingArea.ClosedPath(Square),
            () => WindingArea.EndpointBridged([new(0, 0), new(2, 0)], [new(0, 2), new(2, 2)]),
            () => WindingArea.FilledRegions(Square, other),
            () => WindingArea.IntersectionArea(Square, other),
            () => WindingArea.IntersectionArea(new NestedList(Square, () => WindingArea.IntersectionArea(Square, Square)), other)
        ];
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
        foreach (Action call in nested)
        {
            Equal("nested first input", 2, WindingArea.IntersectionArea(new NestedList(Square, call), other, rule));
            Equal("nested second input", 2, WindingArea.IntersectionArea(Square, new NestedList(other, call), rule));
        }
        for (int side = 0; side < 2; side++)
        {
            var sentinel = new InvalidOperationException("intentional indexer failure");
            var bad = new NestedList(Square, () => { WindingArea.IntersectionArea(Square, other); throw sentinel; });
            try
            {
                _ = side == 0 ? WindingArea.IntersectionArea(bad, other) : WindingArea.IntersectionArea(other, bad);
                throw new InvalidOperationException("Indexer exception was swallowed.");
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, sentinel)) { passed++; }
            Equal("workspace after throwing indexer", 2, WindingArea.IntersectionArea(Square, other));
            Equal("full result after throwing narrow call", 2, WindingArea.FilledRegions(Square, other).IntersectionArea);
        }
        // Conversely the new method may rent a workspace inside the older entry points.
        Equal("narrow inside full", 2, WindingArea.FilledRegions(new NestedList(Square,
            () => WindingArea.IntersectionArea(Square, other)), other).IntersectionArea);
    }

    private static void CheckInputOwnership()
    {
        Point2[] a = [new(-0.0, 0), new(0, -0.0), new(2, 0), new(2, 2), new(0, 2), new(-0.0, 0)];
        Point2[] b = Square.Reverse().ToArray();
        long[] Bits(Point2[] points) => points.SelectMany(p => new[] { BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y) }).ToArray();
        long[] beforeA = Bits(a), beforeB = Bits(b);
        foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
            Equal("readonly indexed inputs", 4, WindingArea.IntersectionArea(new NestedList(a, () => { }), new NestedList(b, () => { }), rule));
        if (!beforeA.SequenceEqual(Bits(a)) || !beforeB.SequenceEqual(Bits(b)))
            throw new InvalidOperationException("Intersection-only mutated caller storage.");
        passed++;
    }

    private sealed class NestedList(Point2[] points, Action nested) : IReadOnlyList<Point2>
    {
        public int Count => points.Length;
        public Point2 this[int index] { get { if (index == 1) nested(); return points[index]; } }
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void Equal(string name, double expected, double actual)
    {
        if (!double.IsFinite(actual) || expected != actual)
            throw new InvalidOperationException($"{name}: expected {expected:R}, actual {actual:R}.");
        passed++;
    }

    private static void Reject<T>(Action call, string? parameter = null) where T : ArgumentException
    {
        try { call(); }
        catch (ArgumentException error) when (error.GetType() == typeof(T) && (parameter is null || error.ParamName == parameter))
        { passed++; return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} for '{parameter}'.");
    }
}
