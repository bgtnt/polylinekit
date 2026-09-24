using PolylineKit;

namespace PolylineKit.Experiments;

internal static class AlignmentChecks
{
    public static int Run()
    {
        int passed = 0;
        void True(string name, bool condition)
        { if (!condition) throw new InvalidOperationException("Alignment: " + name); passed++; }
        void Near(string name, double expected, double actual, double tolerance = 1e-10) =>
            True(name, double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance * Math.Max(1, Math.Abs(expected)));
        void Reject(string name, Action action)
        {
            try { action(); } catch (ArgumentException) { passed++; return; }
            throw new InvalidOperationException("Alignment accepted: " + name);
        }
        Point2[] stroke = [new(0, 0), new(2, 0), new(2, 1), new(3, 1)];
        const double angle = .37, scale = 2.5;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point2 Known(Point2 p) => new(scale * (c * p.X - s * p.Y) + 7, scale * (s * p.X + c * p.Y) - 4);
        Point2[] reference = stroke.Select(Known).ToArray();
        var fit = PolylineAlignment.FitSimilarity(stroke, reference);
        Near("known scale", scale, fit.Scale); Near("known angle", angle, fit.RotationRadians);
        Near("known zero residual", 0, fit.RmsError); Near("known translation X", 7, fit.Transform.OffsetX);
        Near("known translation Y", -4, fit.Transform.OffsetY);
        True("strategy", fit.Strategy == "ArcLengthSimilarityLeastSquares");
        for (int i = 0; i < stroke.Length; i++)
        { Near("aligned X", reference[i].X, fit.AlignedPoints[i].X); Near("aligned Y", reference[i].Y, fit.AlignedPoints[i].Y); }
        Point2[] subdivided = [new(0, 0), new(.5, 0), new(1, 0), new(2, 0), new(2, .7), new(2, 1), new(3, 1)];
        var unequal = PolylineAlignment.FitSimilarity(subdivided, reference);
        Near("unequal sampling", 0, unequal.RmsError); Near("unequal sampling scale", scale, unequal.Scale);
        Point2[] rigidTarget = stroke.Select(p => new Point2(c * p.X - s * p.Y + 7, s * p.X + c * p.Y - 4)).ToArray();
        var rigid = PolylineAlignment.FitSimilarity(stroke, rigidTarget, new AlignmentOptions { AllowScaling = false });
        Near("rigid scale", 1, rigid.Scale); Near("rigid angle", angle, rigid.RotationRadians); Near("rigid residual", 0, rigid.RmsError);
        var reversed = PolylineAlignment.FitSimilarity(stroke, reference.Reverse().ToArray(), new AlignmentOptions { AllowReversal = true });
        True("open reversal", reversed.Reversed); Near("open reversal residual", 0, reversed.RmsError);
        Near("reversal keeps original order", reference[0].X, reversed.AlignedPoints[0].X);

        Point2[] rectangle = [new(0, 0), new(3, 0), new(3, 1), new(0, 1)];
        Point2[] shiftedRectangle = [new(3, 0), new(3, 1), new(0, 1), new(0, 0)];
        var closedFit = PolylineAlignment.FitSimilarity(rectangle, shiftedRectangle, new AlignmentOptions { Closed = true });
        Near("closed cyclic start residual", 0, closedFit.RmsError);
        True("closed phase recorded", closedFit.PhaseShift > 0 && closedFit.PhaseShift < 1);
        var closedReverse = PolylineAlignment.FitSimilarity(rectangle, shiftedRectangle.Reverse().ToArray(),
            new AlignmentOptions { Closed = true, AllowReversal = true });
        Near("closed reverse residual", 0, closedReverse.RmsError); True("closed reversal recorded", closedReverse.Reversed);
        var fixedPhase = PolylineAlignment.FitSimilarity(rectangle, shiftedRectangle,
            new AlignmentOptions { Closed = true, SearchClosedPhase = false });
        Near("fixed phase recorded", 0, fixedPhase.PhaseShift); True("phase search improves residual", fixedPhase.RmsError > 1e-3);

        var openSamples = PolylineSampling.ResampleByArcLength(stroke, 5);
        Near("open first sample", 0, openSamples[0].X); Near("open final sample", 3, openSamples[4].X);
        Near("sample at first corner", 2, openSamples[2].X); Near("sample at second corner", 1, openSamples[3].Y);
        var closedSamples = PolylineSampling.ResampleByArcLength(rectangle, 8, true);
        Near("closing edge sampled", 0, closedSamples[7].X); Near("no repeated closed endpoint", 1, closedSamples[7].Y);
        var closedExplicit = PolylineSampling.ResampleByArcLength(rectangle.Concat([rectangle[0]]).ToArray(), 8, true);
        for (int i = 0; i < 8; i++) { Near("explicit close X", closedSamples[i].X, closedExplicit[i].X); Near("explicit close Y", closedSamples[i].Y, closedExplicit[i].Y); }
        Point2[] withDuplicates = [new(0, 0), new(0, 0), new(2, 0), new(2, 1), new(3, 1)];
        var duplicateFit = PolylineAlignment.FitSimilarity(withDuplicates, reference);
        True("output retains duplicate count", duplicateFit.AlignedPoints.Count == withDuplicates.Length);
        Near("duplicate invariant residual", 0, duplicateFit.RmsError);
        stroke[0] = new Point2(99, 99); reference[0] = new Point2(-99, -99);
        Near("result does not alias input", 7, fit.AlignedPoints[0].X);
        True("result list is read-only", ((ICollection<Point2>)fit.AlignedPoints).IsReadOnly);

        // A stored affine matrix can suffer cancellation even though the centered fit is accurate.
        Point2[] offsetMoving = [new(1e15, -1e15), new(1e15 + 2, -1e15),
            new(1e15 + 2, -1e15 + 1), new(1e15 + 3, -1e15 + 1)];
        Point2[] offsetReference = offsetMoving.Select(Known).ToArray();
        var offsetFit = PolylineAlignment.FitSimilarity(offsetMoving, offsetReference);
        Point2[] appliedSamples = offsetFit.Transform.Apply(PolylineSampling.ResampleByArcLength(offsetMoving, offsetFit.SampleCount));
        Point2[] referenceSamples = PolylineSampling.ResampleByArcLength(offsetReference, offsetFit.SampleCount);
        double actualSquaredResidual = 0;
        for (int i = 0; i < appliedSamples.Length; i++)
        {
            double dx = appliedSamples[i].X - referenceSamples[i].X;
            double dy = appliedSamples[i].Y - referenceSamples[i].Y;
            actualSquaredResidual += dx * dx + dy * dy;
        }
        double actualRms = Math.Sqrt(actualSquaredResidual / appliedSamples.Length);
        True("large offsets exercise application rounding", actualRms > 0);
        Near("reported residual uses the actual transform", actualRms, offsetFit.RmsError, 1e-12);

        Point2[] tiny = [new(0, 0), new(1e-200, 0), new(1e-200, 1e-200)];
        var tinyFit = PolylineAlignment.FitSimilarity(tiny, tiny.Select(p => new Point2(p.X * 2, p.Y * 2)).ToArray());
        Near("small-coordinate scale", 2, tinyFit.Scale);
        Point2[] large = [new(0, 0), new(1e90, 0), new(1e90, 1e90)];
        var largeFit = PolylineAlignment.FitSimilarity(large, large.Select(p => new Point2(p.X * 2, p.Y * 2)).ToArray());
        Near("large-coordinate scale", 2, largeFit.Scale);
        Reject("empty", () => PolylineAlignment.FitSimilarity([], rectangle));
        Reject("single point", () => PolylineSampling.ResampleByArcLength([new(0, 0)]));
        Reject("zero length", () => PolylineAlignment.FitSimilarity([new(1, 1), new(1, 1)], rectangle));
        Reject("nonfinite", () => PolylineSampling.ResampleByArcLength([new(0, 0), new(double.NaN, 1)]));
        Reject("sample limit", () => PolylineAlignment.FitSimilarity(rectangle, rectangle, new AlignmentOptions { SampleCount = 1025 }));
        Reject("too few closed samples", () => PolylineSampling.ResampleByArcLength(rectangle, 2, true));
        // Equal-spaced square vs opposite traversal has zero proper-rotation covariance.
        Point2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Reject("zero covariance does not collapse scale", () => PolylineAlignment.FitSimilarity(square, square.Reverse().ToArray(),
            new AlignmentOptions { Closed = true, SampleCount = 4 }));
        var subnormalBounds = new Bounds2D(double.Epsilon, -double.Epsilon, double.Epsilon, -double.Epsilon);
        True("bounds preserve subnormal point center", subnormalBounds.Center.X == double.Epsilon && subnormalBounds.Center.Y == -double.Epsilon);
        Point2[] illConditioned = [new(10000, 0), new(10000.000000000005, 0)];
        Reject("normalization rejects cancellation-induced center drift", () => PolylineNormalization.ToUnitBounds(illConditioned));
        var recentered = AffineTransform2D.Translation(-10000, 0).Apply(illConditioned);
        var stableNormalization = PolylineNormalization.ToUnitBounds(recentered);
        Near("recentering restores normalization lower bound", -.5, stableNormalization.Bounds.MinX);
        Near("recentering restores normalization upper bound", .5, stableNormalization.Bounds.MaxX);
        Near("recentering restores normalization center", 0, stableNormalization.Bounds.Center.X);
        Near("stable normalization transform provenance", stableNormalization.Points[0].X,
            stableNormalization.Transform.Apply(recentered[0]).X);
        Console.WriteLine($"Alignment/sampling checks: {passed}");
        return passed;
    }
}
