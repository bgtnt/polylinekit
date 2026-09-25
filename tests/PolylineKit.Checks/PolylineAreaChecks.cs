using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Consumer-facing names preserve the established numerical and validation contracts.</summary>
internal static class PolylineAreaChecks
{
    private static int passed;

    internal static int Run()
    {
        passed = 0;
        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        Point2[] shifted = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
        Point2[] collinear = [new(0, 0), new(1, 0), new(2, 0)];
        Point2[] bowtie = [new(0, 0), new(2, 2), new(2, 0), new(0, 2)];
        Point2[] repeated = Enumerable.Range(0, 16).SelectMany(_ => square).ToArray();
        Point2[] translated = square.Select(p => new Point2(p.X + 1e12, p.Y - 1e12)).ToArray();
        Point2[][] cases = [square, shifted, collinear, bowtie, repeated, translated];
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            foreach (Point2[] points in cases)
            foreach (IReadOnlyList<Point2> path in new IReadOnlyList<Point2>[] { points, points.ToList() })
                Bits(WindingArea.FilledArea(path, rule), PolylineArea.FilledArea(path, rule), "selected filled area");

            foreach (Point2[] first in cases)
            foreach (Point2[] second in cases)
            {
                Bits(WindingArea.IntersectionArea(first, second, rule), PolylineArea.IntersectionArea(first, second, rule),
                    "intersection-only area");
                WindingOverlapResult expected = WindingArea.FilledRegions(first, second, rule);
                RegionOverlapResult actual = PolylineArea.CompareRegions(first, second, rule);
                Bits(expected.FirstArea, actual.FirstArea, "first area");
                Bits(expected.SecondArea, actual.SecondArea, "second area");
                Bits(expected.IntersectionArea, actual.IntersectionArea, "intersection area");
                Bits(expected.UnionArea, actual.UnionArea, "union area");
                Bits(expected.SymmetricDifferenceArea, actual.SymmetricDifferenceArea, "difference area");
                NullableBits(expected.IntersectionOverUnion, actual.IntersectionOverUnion, "IoU");
                NullableBits(expected.JaccardDistance, actual.JaccardDistance, "Jaccard distance");
                Check(expected.FillRule == actual.FillRule, "selected fill rule");
            }

            RegionOverlapResult overlap = PolylineArea.CompareRegions(square, shifted, rule);
            Bits(4, overlap.FirstArea, "analytic first square");
            Bits(4, overlap.SecondArea, "analytic second square");
            Bits(2, overlap.IntersectionArea, "analytic intersection");
            Bits(6, overlap.UnionArea, "analytic union");
            Bits(4, overlap.SymmetricDifferenceArea, "analytic difference");
            NullableBits(1.0 / 3, overlap.IntersectionOverUnion, "analytic IoU");
            NullableBits(2.0 / 3, overlap.JaccardDistance, "analytic Jaccard");
            RegionOverlapResult empty = PolylineArea.CompareRegions(collinear, collinear, rule);
            NullableBits(null, empty.IntersectionOverUnion, "zero-area IoU");
            NullableBits(null, empty.JaccardDistance, "zero-area Jaccard");
        }
        Bits(4, PolylineArea.FilledArea(repeated), "default fill is NonZero");
        Bits(2, PolylineArea.IntersectionArea(square, shifted), "default intersection fill");
        Check(PolylineArea.CompareRegions(square, shifted).FillRule == PathFillRule.NonZero, "default region fill");
        Bits(4, PolylineArea.BetweenGraphs([new(0, 0), new(2, 0)], [new(0, 2), new(2, 2)]), "graph area");

        CheckErrors(square);
        Console.WriteLine($"PASS: {passed} consumer-facing area API checks.");
        return passed;
    }

    private static void CheckErrors(Point2[] valid)
    {
        Point2[] invalidCoordinate = [new(0, 0), new(1, 0), new(double.NaN, 1)];
        IReadOnlyList<Point2>?[] badInputs = [null, Array.Empty<Point2>(), [new(0, 0)],
            [new(0, 0), new(1, 0)], invalidCoordinate];
        foreach (IReadOnlyList<Point2>? bad in badInputs)
        {
            Errors(() => WindingArea.FilledArea(bad!), () => PolylineArea.FilledArea(bad!), "filled-area input");
            Errors(() => WindingArea.IntersectionArea(bad!, valid), () => PolylineArea.IntersectionArea(bad!, valid), "intersection first input");
            Errors(() => WindingArea.IntersectionArea(valid, bad!), () => PolylineArea.IntersectionArea(valid, bad!), "intersection second input");
            Errors(() => WindingArea.FilledRegions(bad!, valid), () => PolylineArea.CompareRegions(bad!, valid), "region first input");
            Errors(() => WindingArea.FilledRegions(valid, bad!), () => PolylineArea.CompareRegions(valid, bad!), "region second input");
        }
        PathFillRule invalid = (PathFillRule)2;
        Errors(() => WindingArea.FilledArea(null!, invalid), () => PolylineArea.FilledArea(null!, invalid), "rule before path");
        Errors(() => WindingArea.IntersectionArea(null!, null!, invalid),
            () => PolylineArea.IntersectionArea(null!, null!, invalid), "intersection rule before paths");
        Errors(() => WindingArea.FilledRegions(null!, null!, invalid),
            () => PolylineArea.CompareRegions(null!, null!, invalid), "region rule before paths");
        Check(Capture(() => PolylineArea.FilledArea(null!, invalid)) is ArgumentOutOfRangeException { ParamName: "fillRule" },
            "invalid rule identifies fillRule");
    }

    private static void Errors(Action reference, Action candidate, string name)
    {
        Exception? expected = Capture(reference), actual = Capture(candidate);
        Check(expected is not null && actual?.GetType() == expected.GetType(), name + " exception type");
        Check((expected as ArgumentException)?.ParamName == (actual as ArgumentException)?.ParamName, name + " parameter");
        Check(expected!.Message == actual!.Message, name + " message");
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static void NullableBits(double? expected, double? actual, string name)
    {
        Check(expected.HasValue == actual.HasValue, name + " definition");
        if (expected.HasValue && actual.HasValue) Bits(expected.Value, actual.Value, name);
    }

    private static void Bits(double expected, double actual, string name) =>
        Check(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual), name + " exact bits");

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Consumer area API: " + name);
        passed++;
    }
}
