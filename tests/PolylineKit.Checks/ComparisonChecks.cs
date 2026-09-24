using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Analytic and metamorphic checks for the explicit preprocessing and area APIs.</summary>
internal static class ComparisonChecks
{
    private static int passed;

    public static int Run()
    {
        passed = 0;
        CheckAreaSemantics();
        CheckFilledRegionOverlap();
        CheckGraphAgreement();
        CheckNormalization();
        CheckTransformsAndBounds();
        CheckInvalidInputs();
        Console.WriteLine($"PASS: {passed} comparison, normalization, transform and input checks.");
        return passed;
    }

    private static void CheckAreaSemantics()
    {
        Point2[] bottom = [new(0, 0), new(2, 0)];
        Point2[] top = [new(0, 2), new(2, 2)];
        var rectangle = PolylineComparison.EndpointBridgedArea(bottom, top, includeContours: true);
        Near("rectangle bridge area", 4, rectangle.RawArea);
        Near("union rectangle denominator", 4, rectangle.UnionBoundsArea);
        Near("rectangle bounds area ratio", 1, rectangle.BoundsAreaRatio!.Value);
        Near("compatibility alias forwards bounds ratio", rectangle.BoundsAreaRatio.Value, rectangle.NormalizedArea!.Value);
        // This denominator measures occupancy of the joint rectangle, not distance: changing
        // a resolvable positive gap between equal-length parallel segments leaves the ratio at one.
        foreach (double gap in new[] { 1e-4, .01, 1, 100 })
        {
            Point2[] parallel = [new(0, gap), new(2, gap)];
            var parallelResult = PolylineComparison.EndpointBridgedArea(bottom, parallel);
            Near("parallel gap raw area " + gap, 2 * gap, parallelResult.RawArea);
            Near("parallel gap bounds occupancy " + gap, 1, parallelResult.BoundsAreaRatio!.Value);
        }
        var collinearIdentity = PolylineComparison.EndpointBridgedArea(bottom, bottom);
        Near("collinear identity raw area", 0, collinearIdentity.RawArea);
        True("collinear identity bounds ratio is undefined", collinearIdentity.BoundsAreaRatio is null);
        True("compatibility alias preserves undefined ratio", collinearIdentity.NormalizedArea is null);
        True("bridge metadata", rectangle.Kind == AreaComparisonKind.EndpointBridged && rectangle.FillRule == PathFillRule.NonZero && rectangle.DecimalPrecision == 6);
        Near("returned contours preserve orientation and area", 4, rectangle.Contours.Sum(SignedArea));
        True("contours are optional", PolylineComparison.EndpointBridgedArea(bottom, top).Contours.Count == 0);
        Near("simultaneously reversed open inputs", 4, PolylineComparison.EndpointBridgedArea(bottom.Reverse().ToArray(), top.Reverse().ToArray()).RawArea);
        Near("one reversed open input changes bridge", 2, PolylineComparison.EndpointBridgedArea(bottom, top.Reverse().ToArray()).RawArea);
        Near("open exchange symmetry", rectangle.RawArea, PolylineComparison.EndpointBridgedArea(top, bottom).RawArea);

        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0)];
        Point2[] reversed = square.Reverse().ToArray();
        Near("closed stroke with same order cancels", 0, PolylineComparison.EndpointBridgedArea(square, square).RawArea);
        Near("closed reversed stroke has nonzero double winding", 4, PolylineComparison.EndpointBridgedArea(square, reversed).RawArea);
        Near("closed reversed stroke has even parity", 0, PolylineComparison.EndpointBridgedArea(square, reversed, PathFillRule.EvenOdd).RawArea);
        Near("filled region ignores closed reversal", 0, PolylineComparison.FilledRegionDifference(square, reversed).RawArea);
        Near("filled region ignores closing duplicate", 0, PolylineComparison.FilledRegionDifference(square, square.Take(4).ToArray()).RawArea);
        Point2[] shiftedStart = [new(2, 2), new(0, 2), new(0, 0), new(2, 0)];
        Near("filled region ignores starting vertex", 0, PolylineComparison.FilledRegionDifference(square, shiftedStart).RawArea);

        Point2[] moved = AffineTransform2D.Translation(1, 0).Apply(square);
        var difference = PolylineComparison.FilledRegionDifference(square, moved, includeContours: true);
        Near("overlapping square symmetric difference", 4, difference.RawArea);
        Near("difference rectangle denominator", 6, difference.UnionBoundsArea);
        Near("difference bounds area ratio", 2.0 / 3, difference.BoundsAreaRatio!.Value);
        True("filled-region metadata", difference.Kind == AreaComparisonKind.FilledRegionDifference);
        Near("filled-region exchange symmetry", difference.RawArea, PolylineComparison.FilledRegionDifference(moved, square).RawArea);
        Point2[] triangle = [new(0, 0), new(2, 0), new(0, 2)];
        Near("implicit triangle closure", 2, PolylineComparison.FilledRegionDifference(square, triangle).RawArea);

        Point2[] emptyRegion = [new(0, 0), new(1, 0), new(2, 0)];
        var expectedContours = new Dictionary<string, (double NonZero, double EvenOdd)>
        {
            ["square-once"] = (4, 4), ["square-twice"] = (4, 0),
            ["square-forward-backward"] = (0, 0), ["bow-tie"] = (2, 2),
            ["overlapping-squares"] = (6, 4), ["hole-with-retraced-bridge"] = (12, 12)
        };
        foreach (var (name, contour) in Fixtures.Contours())
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            double expected = rule == PathFillRule.NonZero ? expectedContours[name].NonZero : expectedContours[name].EvenOdd;
            var result = PolylineComparison.FilledRegionDifference(contour, emptyRegion, rule, includeContours: true);
            Near(name + " filled " + rule, expected, result.RawArea);
            Near(name + " diagnostic signed sum " + rule, expected, result.Contours.Sum(SignedArea));
            Near(name + " endpoint bridge " + rule, expected, PolylineComparison.EndpointBridgedArea(contour, bottom, rule).RawArea);
            if (name == "hole-with-retraced-bridge")
                True("hole retained as negative contour " + rule, result.Contours.Any(c => SignedArea(c) < 0));
        }

        var collapsed = PolylineComparison.EndpointBridgedArea(bottom, [new(1, 0), new(3, 0)]);
        Near("overlapping collinear paths area", 0, collapsed.RawArea);
        True("zero-area denominator is undefined", collapsed.BoundsAreaRatio is null);
        var collinearClosed = PolylineComparison.FilledRegionDifference(emptyRegion, [new(1, 0), new(2, 0), new(3, 0)]);
        Near("collinear filled paths have no area", 0, collinearClosed.RawArea);
        True("collinear filled normalization is undefined", collinearClosed.BoundsAreaRatio is null);

        var transform = AffineTransform2D.Scaling(3).Then(AffineTransform2D.Translation(10, -12));
        var transformed = PolylineComparison.EndpointBridgedArea(transform.Apply(bottom), transform.Apply(top));
        Near("common uniform transform scales area quadratically", 36, transformed.RawArea);
        Near("common uniform transform preserves area ratio", 1, transformed.BoundsAreaRatio!.Value);
        var transpose = new AffineTransform2D(0, 1, 1, 0, 0, 0);
        Near("common axis exchange preserves filled difference", difference.RawArea,
            PolylineComparison.FilledRegionDifference(transpose.Apply(square), transpose.Apply(moved), includeContours: true).Contours.Sum(SignedArea));

        Point2[] bottomCopy = bottom.ToArray(), topCopy = top.ToArray();
        var stored = PolylineComparison.EndpointBridgedArea(bottom, top, includeContours: true);
        Sequence("comparison leaves first input unchanged", bottomCopy, bottom);
        Sequence("comparison leaves second input unchanged", topCopy, top);
        bottom[0] = new(100, 100); top[0] = new(200, 200);
        Near("comparison result owns its data", 4, stored.Contours.Sum(SignedArea));
        Near("comparison original bounds remain stable", 4, stored.UnionBoundsArea);
    }

    private static void CheckFilledRegionOverlap()
    {
        Point2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Point2[] offset = AffineTransform2D.Translation(.5, .5).Apply(square);
        Point2[] far = AffineTransform2D.Translation(100, 100).Apply(square);
        Point2[] diagonal = [new(0, 0), new(1, 1), new(2, 2)];
        Point2[] large = AffineTransform2D.Scaling(2).Apply(square);
        Point2[] hole = Fixtures.Contours()["hole-with-retraced-bridge"];
        Point2[] inner = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            var identity = PolylineComparison.FilledRegionOverlap(square, square, rule);
            Overlap("identity " + rule, identity, 0, 1, 0, 1);
            Overlap("reverse identity independent fill " + rule,
                PolylineComparison.FilledRegionOverlap(square, square.Reverse().ToArray(), rule), 0, 1, 0, 1);
            Overlap("closing duplicate identity " + rule,
                PolylineComparison.FilledRegionOverlap(square.Concat([square[0]]).ToArray(), square, rule), 0, 1, 0, 1);
            Point2[] cyclic = [square[2], square[3], square[0], square[1]];
            Overlap("cyclic start identity " + rule, PolylineComparison.FilledRegionOverlap(square, cyclic, rule), 0, 1, 0, 1);

            var overlap = PolylineComparison.FilledRegionOverlap(square, offset, rule, decimalPrecision: 8);
            Overlap("diagonal offset " + rule, overlap, 1.5, 1.75, 6.0 / 7, 1.0 / 7);
            Near("offset bounds ratio differs from Jaccard " + rule, 2.0 / 3,
                PolylineComparison.FilledRegionDifference(square, offset, rule, decimalPrecision: 8).BoundsAreaRatio!.Value);
            True("overlap metadata " + rule, overlap.FillRule == rule && overlap.DecimalPrecision == 8);
            Near("overlap original bounds " + rule, 2.25, overlap.UnionBounds.Area);
            Overlap("overlapping opposite winding independent fill " + rule,
                PolylineComparison.FilledRegionOverlap(square, offset.Reverse().ToArray(), rule), 1.5, 1.75, 6.0 / 7, 1.0 / 7);
            Overlap("exchange symmetry " + rule, PolylineComparison.FilledRegionOverlap(offset, square, rule), 1.5, 1.75, 6.0 / 7, 1.0 / 7);
            Overlap("containment " + rule, PolylineComparison.FilledRegionOverlap(large, square, rule), 3, 4, .75, .25);
            Overlap("edge touching " + rule, PolylineComparison.FilledRegionOverlap(square,
                AffineTransform2D.Translation(1, 0).Apply(square), rule), 2, 2, 1, 0);
            Overlap("vertex touching " + rule, PolylineComparison.FilledRegionOverlap(square,
                AffineTransform2D.Translation(1, 1).Apply(square), rule), 2, 2, 1, 0);
            Overlap("far disjoint " + rule, PolylineComparison.FilledRegionOverlap(square, far, rule), 2, 2, 1, 0);
            Near("far bounding rectangle is not union area " + rule, 2.0 / 10201,
                PolylineComparison.FilledRegionDifference(square, far, rule).BoundsAreaRatio!.Value);

            Overlap("hole subtraction identity " + rule, PolylineComparison.FilledRegionOverlap(hole, hole.Reverse().ToArray(), rule), 0, 12, 0, 1);
            Overlap("region inside hole has no overlap " + rule, PolylineComparison.FilledRegionOverlap(hole, inner, rule), 16, 16, 1, 0);
            Overlap("one zero-area region " + rule, PolylineComparison.FilledRegionOverlap(diagonal, square, rule), 1, 1, 1, 0);
            var empty = PolylineComparison.FilledRegionOverlap(diagonal, diagonal.Reverse().ToArray(), rule);
            Near("both zero regions XOR " + rule, 0, empty.SymmetricDifferenceArea);
            Near("both zero regions union " + rule, 0, empty.UnionArea);
            True("both empty ratios undefined despite positive rectangle " + rule,
                empty.JaccardDistance is null && empty.IntersectionOverUnion is null && empty.UnionBounds.Area > 0);
            Point2[] tiny = AffineTransform2D.Scaling(1e-9).Apply(square);
            var collapsed = PolylineComparison.FilledRegionOverlap(tiny, tiny, rule, decimalPrecision: 6);
            Near("precision-collapsed union " + rule, 0, collapsed.UnionArea);
            True("precision-collapsed ratios undefined " + rule, collapsed.JaccardDistance is null && collapsed.IntersectionOverUnion is null);

            var map = AffineTransform2D.Scaling(3).Then(AffineTransform2D.Translation(17, -11));
            Overlap("common scale and translation " + rule,
                PolylineComparison.FilledRegionOverlap(map.Apply(square), map.Apply(offset), rule), 13.5, 15.75, 6.0 / 7, 1.0 / 7);
            var transpose = new AffineTransform2D(0, 1, 1, 0, 0, 0);
            Overlap("common axis exchange " + rule,
                PolylineComparison.FilledRegionOverlap(transpose.Apply(square), transpose.Apply(offset), rule), 1.5, 1.75, 6.0 / 7, 1.0 / 7);
            // Rectangular bounds force the sweep-axis exchange rather than only a square tie.
            var stretch = AffineTransform2D.Scaling(3, 1);
            Overlap("rectangular sweep axis " + rule,
                PolylineComparison.FilledRegionOverlap(stretch.Apply(square), stretch.Apply(offset), rule), 4.5, 5.25, 6.0 / 7, 1.0 / 7);
            var subdivided = Fixtures.Subdivide(square.Concat([square[0]]).ToArray(), 3);
            Overlap("collinear subdivision " + rule, PolylineComparison.FilledRegionOverlap(subdivided, offset, rule), 1.5, 1.75, 6.0 / 7, 1.0 / 7);
            Near("XOR-only and overlap share exact preparation " + rule,
                PolylineComparison.FilledRegionDifference(square, offset, rule, decimalPrecision: 8).RawArea,
                overlap.SymmetricDifferenceArea, 0);
        }
        Point2[] twice = Fixtures.Contours()["square-twice"];
        Overlap("NonZero repeated traversal counts once", PolylineComparison.FilledRegionOverlap(twice, diagonal), 4, 4, 1, 0);
        var evenRepeated = PolylineComparison.FilledRegionOverlap(twice, diagonal, PathFillRule.EvenOdd);
        True("EvenOdd repeat has no filled union", evenRepeated.UnionArea == 0 && evenRepeated.JaccardDistance is null && evenRepeated.IntersectionOverUnion is null);
        Overlap("bow tie lobes do not cancel", PolylineComparison.FilledRegionOverlap(Fixtures.Contours()["bow-tie"], diagonal), 2, 2, 1, 0);

        Point2[] originalSquare = square.ToArray(), originalOffset = offset.ToArray();
        var stored = PolylineComparison.FilledRegionOverlap(square, offset);
        Sequence("overlap preserves first input", originalSquare, square);
        Sequence("overlap preserves second input", originalOffset, offset);
        square[0] = new(999, 999); offset[0] = new(-999, -999);
        Overlap("overlap result detached from inputs", stored, 1.5, 1.75, 6.0 / 7, 1.0 / 7);
        Near("stored overlap bounds detached", 2.25, stored.UnionBounds.Area);
        Reject<ArgumentNullException>("overlap null", () => PolylineComparison.FilledRegionOverlap(null!, originalSquare));
        Reject<ArgumentException>("overlap empty", () => PolylineComparison.FilledRegionOverlap([], originalSquare));
        Reject<ArgumentException>("overlap insufficient vertices", () => PolylineComparison.FilledRegionOverlap([new(0, 0), new(1, 0)], originalSquare));
        Reject<ArgumentOutOfRangeException>("overlap invalid fill rule", () => PolylineComparison.FilledRegionOverlap(originalSquare, originalSquare, (PathFillRule)99));
        Reject<ArgumentOutOfRangeException>("overlap invalid precision", () => PolylineComparison.FilledRegionOverlap(originalSquare, originalSquare, decimalPrecision: 9));
        Reject<ArgumentOutOfRangeException>("overlap clipping range", () => PolylineComparison.FilledRegionOverlap(
            AffineTransform2D.Scaling(1e8).Apply(originalSquare), originalSquare, decimalPrecision: 8));
    }

    private static void Overlap(string name, FilledRegionOverlapResult actual, double difference, double union, double distance, double iou)
    {
        Near(name + " XOR", difference, actual.SymmetricDifferenceArea);
        Near(name + " union", union, actual.UnionArea);
        True(name + " defined ratios", actual.JaccardDistance.HasValue && actual.IntersectionOverUnion.HasValue);
        Near(name + " Jaccard", distance, actual.JaccardDistance!.Value);
        Near(name + " IoU", iou, actual.IntersectionOverUnion!.Value);
    }

    private static void CheckGraphAgreement()
    {
        foreach (var fixture in Fixtures.HandCases().Concat(Enumerable.Range(0, 12).Select(seed => Fixtures.RandomGraphs(seed))))
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            double expected = fixture.ExpectedArea ?? PolylineArea.BetweenGraphs(fixture.P, fixture.Q);
            var result = PolylineComparison.EndpointBridgedArea(fixture.P, fixture.Q, rule, decimalPrecision: 8);
            Near(fixture.Name + " graph/fill agreement " + rule, expected, result.RawArea, 2e-7);
            Near(fixture.Name + " subdivision agreement " + rule, result.RawArea,
                PolylineComparison.EndpointBridgedArea(Fixtures.Subdivide(fixture.P), Fixtures.Subdivide(fixture.Q, 3), rule, decimalPrecision: 8).RawArea, 2e-7);
        }
    }

    private static void CheckNormalization()
    {
        Point2[] source = [new(10, 20), new(14, 20), new(14, 22), new(10, 22), new(10, 20)];
        Point2[] snapshot = source.ToArray();
        var uniform = PolylineNormalization.ToUnitBounds(source);
        Near("uniform normalized width", 1, uniform.Bounds.Width);
        Near("uniform normalized height", .5, uniform.Bounds.Height);
        Point("uniform normalization center", new(0, 0), uniform.Bounds.Center);
        Point("uniform first coordinate", new(-.5, -.25), uniform.Points[0]);
        True("uniform normalization metadata", uniform.Strategy == BoundsScaling.Uniform && uniform.OriginalBounds.MinX == 10 && uniform.OriginalBounds.MinY == 20);
        Sequence("normalization transform reproduces points", uniform.Points, uniform.Transform.Apply(source));
        Sequence("normalization preserves input", snapshot, source);
        True("normalization preserves closing duplicate", uniform.Points.Count == source.Length && uniform.Points[0].Equals(uniform.Points[^1]));

        var stretch = PolylineNormalization.ToUnitBounds(source, BoundsScaling.Stretch);
        Near("stretch normalized width", 1, stretch.Bounds.Width);
        Near("stretch normalized height", 1, stretch.Bounds.Height);
        Point("stretch first coordinate", new(-.5, -.5), stretch.Points[0]);
        var sizeTwo = PolylineNormalization.ToUnitBounds(source, size: 2);
        Near("requested normalization size", 2, sizeTwo.Bounds.Width);
        Near("requested normalization size preserves aspect", 1, sizeTwo.Bounds.Height);
        Near("uniform and stretch normalized regions differ", .5,
            PolylineComparison.FilledRegionDifference(uniform.Points, stretch.Points).RawArea);

        Point2[] scaledTranslated = AffineTransform2D.Scaling(7).Then(AffineTransform2D.Translation(-30, 80)).Apply(source);
        var otherUniform = PolylineNormalization.ToUnitBounds(scaledTranslated);
        Near("independently normalized equivalent shapes", 0, PolylineComparison.FilledRegionDifference(uniform.Points, otherUniform.Points).RawArea);
        Point2[] stretchedSource = AffineTransform2D.Scaling(2, 3).Apply(source);
        Near("independent stretch normalization removes aspect changes", 0,
            PolylineComparison.FilledRegionDifference(stretch.Points, PolylineNormalization.ToUnitBounds(stretchedSource, BoundsScaling.Stretch).Points).RawArea);
        True("uniform normalization retains aspect changes",
            PolylineComparison.FilledRegionDifference(uniform.Points, PolylineNormalization.ToUnitBounds(stretchedSource).Points).RawArea > .2);

        Point2[] target = [new(8, 20), new(16, 24)];
        var matched = PolylineNormalization.MatchBounds(source, target);
        Point("matched lower corner", new(8, 20), matched.Points[0]);
        Point("matched upper corner", new(16, 24), matched.Points[2]);
        Sequence("match-bounds transform provenance", matched.Points, matched.Transform.Apply(source));
        var fitted = PolylineNormalization.FitBounds(source, new Bounds2D(0, 0, 8, 8));
        Point("uniform fit is centered in target", new(4, 4), fitted.Bounds.Center);
        Near("uniform fit preserves aspect", 4, fitted.Bounds.Height);
        var fitStretch = PolylineNormalization.FitBounds(source, new Bounds2D(0, 0, 8, 8), BoundsScaling.Stretch);
        Near("stretched fit reaches target height", 8, fitStretch.Bounds.Height);

        foreach (BoundsScaling scaling in new[] { BoundsScaling.Uniform, BoundsScaling.Stretch })
        {
            var horizontal = PolylineNormalization.ToUnitBounds([new(5, 7), new(9, 7)], scaling);
            Point("horizontal collapsed axis " + scaling, new(-.5, 0), horizontal.Points[0]);
            Point("horizontal other endpoint " + scaling, new(.5, 0), horizontal.Points[1]);
            var vertical = PolylineNormalization.ToUnitBounds([new(5, 7), new(5, 11)], scaling);
            Point("vertical collapsed axis " + scaling, new(0, -.5), vertical.Points[0]);
            Point("vertical other endpoint " + scaling, new(0, .5), vertical.Points[1]);
            var lineMatch = PolylineNormalization.MatchBounds([new(0, 1), new(2, 1)], [new(4, 8), new(10, 8)], scaling);
            Point("horizontal line-to-line match " + scaling, new(4, 8), lineMatch.Points[0]);
        }
        source[0] = new(-100, -100);
        Point("normalization snapshot detached from input", new(-.5, -.25), uniform.Points[0]);
    }

    private static void CheckTransformsAndBounds()
    {
        Point2 input = new(2, 3);
        Point("identity", input, AffineTransform2D.Identity.Apply(input));
        Point("default affine is zero map", new(0, 0), default(AffineTransform2D).Apply(input));
        Point("direct affine coefficients", new(15, 31), new AffineTransform2D(2, 3, 4, 5, 2, 8).Apply(input));
        var translation = AffineTransform2D.Translation(5, -2);
        var scale = AffineTransform2D.Scaling(2, 3);
        Point("translation then scale order", new(14, 3), translation.Then(scale).Apply(input));
        Point("scale then translation order", new(9, 7), scale.Then(translation).Apply(input));
        Point("composition agrees with sequential application", scale.Apply(translation.Apply(input)), translation.Then(scale).Apply(input));
        Point("quarter turn counterclockwise", new(-3, 2), AffineTransform2D.Rotation(Math.PI / 2).Apply(input));
        Point("reflection supported explicitly", new(-2, 3), AffineTransform2D.Scaling(-1, 1).Apply(input));
        Point2[] points = [input, input, new(-3, 5)];
        var transformed = translation.Apply(points);
        True("affine application preserves duplicates", transformed.Length == 3 && transformed[0].Equals(transformed[1]));
        Point("affine application leaves source unchanged", input, points[0]);

        var bounds = Bounds2D.FromPoints([new(3, -2), new(-1, 5), new(2, 1)]);
        Near("bounds width", 4, bounds.Width);
        Near("bounds height", 7, bounds.Height);
        Near("bounds rectangle area", 28, bounds.Area);
        Point("bounds center", new(1, 1.5), bounds.Center);
        var union = bounds.Union(new Bounds2D(-5, -1, 1, 8));
        Point("union lower corner", new(-5, -2), new(union.MinX, union.MinY));
        Point("union upper corner", new(3, 8), new(union.MaxX, union.MaxY));
        var huge = new Bounds2D(-1e100, -1e100, 1e100, 1e100);
        Near("permitted large bounds area remains finite", 4e200, huge.Area);
        Point("symmetric large bounds center", new(0, 0), huge.Center);
    }

    private static void CheckInvalidInputs()
    {
        Point2[] axis = [new(0, 0), new(2, 0)];
        Point2[] triangle = [new(0, 0), new(2, 0), new(0, 1)];
        Reject<ArgumentNullException>("comparison null", () => PolylineComparison.EndpointBridgedArea(null!, axis));
        Reject<ArgumentException>("comparison empty", () => PolylineComparison.EndpointBridgedArea([], axis));
        Reject<ArgumentException>("comparison point-only", () => PolylineComparison.EndpointBridgedArea([new(0, 0), new(0, 0)], axis));
        Reject<ArgumentException>("filled region insufficient cleaned vertices", () => PolylineComparison.FilledRegionDifference([new(0, 0), new(1, 0), new(0, 0)], triangle));
        Reject<ArgumentOutOfRangeException>("invalid fill rule", () => PolylineComparison.EndpointBridgedArea(axis, axis, (PathFillRule)99));
        foreach (int precision in new[] { -9, 9 })
            Reject<ArgumentOutOfRangeException>("invalid precision " + precision, () => PolylineComparison.EndpointBridgedArea(axis, axis, decimalPrecision: precision));
        Reject<ArgumentOutOfRangeException>("clipping extent limit", () => PolylineComparison.EndpointBridgedArea([new(0, 0), new(1e8, 0)], axis, decimalPrecision: 8));
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e101 })
        {
            Reject<ArgumentException>("invalid comparison coordinate " + invalid, () => PolylineComparison.EndpointBridgedArea([new(0, invalid), new(2, 0)], axis));
            Reject<ArgumentException>("invalid bounds coordinate " + invalid, () => Bounds2D.FromPoints([new(invalid, 0)]));
            Reject<ArgumentException>("invalid transform input " + invalid, () => AffineTransform2D.Identity.Apply(new Point2(invalid, 0)));
        }
        Reject<ArgumentNullException>("normalization null", () => PolylineNormalization.ToUnitBounds(null!));
        Reject<ArgumentException>("normalization empty", () => PolylineNormalization.ToUnitBounds([]));
        Reject<ArgumentException>("normalization point-only", () => PolylineNormalization.ToUnitBounds([new(3, 4), new(3, 4)]));
        Reject<ArgumentException>("normalization collapsed target", () => PolylineNormalization.FitBounds(axis, default));
        Reject<ArgumentException>("normalization cannot collapse nonzero axis", () => PolylineNormalization.FitBounds(triangle, new Bounds2D(0, 0, 1, 0)));
        Reject<ArgumentOutOfRangeException>("invalid normalization strategy", () => PolylineNormalization.ToUnitBounds(axis, (BoundsScaling)99));
        foreach (double size in new[] { 0, -1, double.NaN, double.PositiveInfinity, 1e101 })
            Reject<ArgumentOutOfRangeException>("invalid normalization size " + size, () => PolylineNormalization.ToUnitBounds(axis, size: size));
        Reject<ArgumentException>("unrepresentable normalization scale", () => PolylineNormalization.ToUnitBounds([new(0, 0), new(double.Epsilon, 0)]));
        Reject<ArgumentNullException>("bounds null", () => Bounds2D.FromPoints(null!));
        Reject<ArgumentException>("bounds empty", () => Bounds2D.FromPoints([]));
        Reject<ArgumentException>("unordered bounds", () => new Bounds2D(1, 0, 0, 1));
        Reject<ArgumentException>("nonfinite affine coefficient", () => new AffineTransform2D(double.NaN, 0, 0, 1, 0, 0));
        Reject<ArgumentOutOfRangeException>("nonfinite rotation", () => AffineTransform2D.Rotation(double.PositiveInfinity));
        Reject<ArgumentException>("transform output range", () => AffineTransform2D.Scaling(1e100).Apply(new Point2(2, 0)));
        Reject<ArgumentException>("transform output overflow", () => AffineTransform2D.Scaling(double.MaxValue).Apply(new Point2(2, 0)));
        Reject<ArgumentException>("transform composition overflow", () => AffineTransform2D.Scaling(double.MaxValue).Then(AffineTransform2D.Scaling(2)));
        Reject<ArgumentNullException>("transform list null", () => AffineTransform2D.Identity.Apply((IReadOnlyList<Point2>)null!));
    }

    private static double SignedArea(IReadOnlyList<Point2> points)
    {
        double sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Point2 a = points[i], b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - a.Y * b.X;
        }
        return sum / 2;
    }

    private static void Sequence(string name, IReadOnlyList<Point2> expected, IReadOnlyList<Point2> actual)
    {
        True(name + " count", expected.Count == actual.Count);
        for (int i = 0; i < expected.Count; i++) Point(name + " point " + i, expected[i], actual[i]);
    }

    private static void Point(string name, Point2 expected, Point2 actual)
    {
        Near(name + " x", expected.X, actual.X);
        Near(name + " y", expected.Y, actual.Y);
    }

    private static void Near(string name, double expected, double actual, double tolerance = 2e-12)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance * Math.Max(1, Math.Abs(expected)))
            throw new Exception($"{name}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}");
        passed++;
    }

    private static void True(string name, bool condition)
    {
        if (!condition) throw new Exception(name);
        passed++;
    }

    private static void Reject<T>(string name, Action action) where T : Exception
    {
        try { action(); }
        catch (T) { passed++; return; }
        throw new Exception(name + " should have been rejected.");
    }
}
