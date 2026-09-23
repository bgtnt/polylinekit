using System.Collections;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Public-contract checks shared by scalar and optional SIMD paths.</summary>
internal static class OptimizationChecks
{
    private const string DisableSimd = "PolylineKit.DisableSimd";
    private static int passed;

    public static int Run()
    {
        passed = 0;
        bool hadSwitch = AppContext.TryGetSwitch(DisableSimd, out bool originalSwitch);
        try
        {
            CheckTransforms();
            CheckInvalidTransforms();
            CheckNormalization();
            CheckAlignment();
            CheckSampling();
            Console.WriteLine($"Optimization scalar/SIMD checks: {passed}");
            return passed;
        }
        finally
        {
            // AppContext has no remove-switch API: restore the prior effective value.
            AppContext.SetSwitch(DisableSimd, hadSwitch && originalSwitch);
        }
    }

    private static void CheckTransforms()
    {
        foreach (int count in new[] { 0, 1, 2, 31, 32, 33, 63, 64, 65, 257, 1024 })
        foreach (double size in new[] { 1e-200, 1.0, 1e90 })
        {
            Point2[] input = Stroke(count, size);
            Point2[] snapshot = [.. input];
            foreach (AffineTransform2D transform in new[]
            {
                AffineTransform2D.Identity,
                AffineTransform2D.Scaling(.25, -.7).Then(AffineTransform2D.Rotation(.713)),
                new AffineTransform2D(1.25, -.375, .625, .875, .3 * size, -.9 * size)
            })
            {
                Point2[] scalar = InMode(true, () => transform.Apply(input));
                Point2[] simd = InMode(false, () => transform.Apply(input));
                PointsEqual($"apply/{count}/{size:R}", scalar, simd);
                Point2[] expected = input.Select(p => new Point2(
                    transform.M11 * p.X + transform.M12 * p.Y + transform.OffsetX,
                    transform.M21 * p.X + transform.M22 * p.Y + transform.OffsetY)).ToArray();
                PointsEqual("apply scalar arithmetic oracle", expected, simd);
                PointsEqual("apply wrapper dispatch", scalar,
                    InMode(false, () => transform.Apply(new ReadOnlyPoints(input))));
                True("apply returns a fresh array", !ReferenceEquals(input, simd));
                True("apply output remains finite and in range", simd.All(p =>
                    double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= 1e100 && Math.Abs(p.Y) <= 1e100));
            }
            PointsEqual("apply preserves input", snapshot, input);
        }
        Point2[] special = [new(1e100, -1e100), new(-1e100, 1e100), new(0.0, -0.0),
            new(-0.0, 0.0), new(-0.0, -0.0), new(double.Epsilon, -double.Epsilon),
            new(2 * double.Epsilon, -2 * double.Epsilon),
            new(BitConverter.Int64BitsToDouble(0x0010000000000000), -BitConverter.Int64BitsToDouble(0x0010000000000000))];
        // Reach the SIMD threshold and cover both packed lanes, successive vector
        // boundaries, and odd scalar tails. Rotations put every special value in each role.
        foreach (int count in new[] { 32, 33, 64, 65 })
        foreach (int shift in Enumerable.Range(0, special.Length))
        {
            Point2[] limits = [.. Enumerable.Range(0, count).Select(i => special[(i + shift) % special.Length])];
            foreach (AffineTransform2D transform in new[]
            {
                AffineTransform2D.Identity,
                AffineTransform2D.Scaling(.5, -.5),
                new AffineTransform2D(1, 0, 0, 1, -0.0, -0.0)
            })
            {
                Point2[] expected = [.. limits.Select(p => transform.Apply(p))];
                PointsEqual("boundary scalar point/array agreement", expected, InMode(true, () => transform.Apply(limits)));
                PointsEqual("SIMD bound, signed-zero and subnormal bit patterns", expected, InMode(false, () => transform.Apply(limits)));
            }
        }
        var originals = Stroke(33, 1);
        var result = InMode(false, () => AffineTransform2D.Identity.Apply(originals));
        Point2 old = result[0]; originals[0] = new Point2(999, 999);
        True("apply snapshot does not alias source", Same(old.X, result[0].X) && Same(old.Y, result[0].Y));
    }

    private static void CheckInvalidTransforms()
    {
        RejectBoth("null array", () => AffineTransform2D.Identity.Apply((IReadOnlyList<Point2>)null!),
            typeof(ArgumentNullException), "points");
        foreach (int count in new[] { 31, 32, 33, 65, 257 })
        foreach (int index in new[] { 0, count / 2, count - 1 })
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.01e100, -1.01e100 })
        foreach (bool badX in new[] { false, true })
        {
            Point2[] path = Stroke(count, 1);
            path[index] = badX ? new Point2(bad, 0) : new Point2(0, bad);
            RejectBoth($"invalid coordinate/{count}/{index}/{bad:R}/{badX}",
                () => AffineTransform2D.Identity.Apply(path), typeof(ArgumentException), "point");
        }
        foreach (int index in new[] { 0, 32, 64 })
        {
            Point2[] path = Stroke(65, 1);
            path[index] = new Point2(1e99, -1e99);
            foreach (double scale in new[] { 100.0, 1e210 })
                RejectBoth($"invalid transformed output/{index}/{scale:R}",
                    () => AffineTransform2D.Scaling(scale).Apply(path), typeof(ArgumentException), "point");
        }
    }

    private static void CheckNormalization()
    {
        foreach (int count in new[] { 31, 32, 33, 63, 64, 65, 257 })
        foreach (double size in new[] { 1e-200, 1.0, 1e90 })
        foreach (BoundsScaling scaling in new[] { BoundsScaling.Uniform, BoundsScaling.Stretch })
        {
            Point2[] path = Stroke(count, size);
            NormalizationResult scalar = InMode(true, () => PolylineNormalization.ToUnitBounds(path, scaling));
            NormalizationResult simd = InMode(false, () => PolylineNormalization.ToUnitBounds(path, scaling));
            PointsEqual("normalization exact coordinates", scalar.Points, simd.Points);
            TransformEqual("normalization exact transform", scalar.Transform, simd.Transform);
            True("normalization exact bounds", BoundsEqual(scalar.Bounds, simd.Bounds) &&
                BoundsEqual(scalar.OriginalBounds, simd.OriginalBounds) && scalar.Strategy == simd.Strategy);
            PointsEqual("normalization wrapper dispatch", scalar.Points,
                InMode(false, () => PolylineNormalization.ToUnitBounds(new ReadOnlyPoints(path), scaling)).Points);
            True("normalization fits declared unit bounds", simd.Points.All(p =>
                double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= .5 + 1e-14 && Math.Abs(p.Y) <= .5 + 1e-14));
        }
        Point2[] illConditioned = [new(10000, 0), new(10000.000000000005, 0)];
        RejectBoth("precision guard retained", () => PolylineNormalization.ToUnitBounds(illConditioned),
            typeof(ArgumentException), "points");
    }

    private static void CheckAlignment()
    {
        foreach (double size in new[] { 1e-200, 1.0, 1e90 })
        foreach (int sampleCount in new[] { 31, 32, 33, 64, 65, 257 })
        {
            Point2[] moving = Stroke(65, size);
            var transform = AffineTransform2D.Scaling(1.7).Then(AffineTransform2D.Rotation(-.43))
                .Then(AffineTransform2D.Translation(3 * size, -2 * size));
            Point2[] reference = InMode(true, () => transform.Apply(moving));
            foreach (bool reversed in new[] { false, true })
            {
                var target = reversed ? reference.Reverse().ToArray() : reference;
                var options = new AlignmentOptions { SampleCount = sampleCount, AllowReversal = true };
                AlignmentResult scalar = InMode(true, () => PolylineAlignment.FitSimilarity(moving, target, options));
                AlignmentResult simd = InMode(false, () => PolylineAlignment.FitSimilarity(moving, target, options));
                FitEqual("open fitted result", scalar, simd);
                True("open traversal choice retained", simd.Reversed == reversed && simd.PhaseShift == 0);
            }
        }
        Point2[] closed = [new(0, 0), new(4, 0), new(4, 1), new(2, 3), new(0, 1)];
        Point2[] shifted = [closed[2], closed[3], closed[4], closed[0], closed[1]];
        foreach (int count in new[] { 64, 256 })
        foreach (bool reverse in new[] { false, true })
        foreach (bool search in new[] { false, true })
        {
            Point2[] target = reverse ? shifted.Reverse().ToArray() : shifted;
            var options = new AlignmentOptions { Closed = true, SampleCount = count, SearchClosedPhase = search, AllowReversal = true };
            FitEqual("closed phase and reversal selection",
                InMode(true, () => PolylineAlignment.FitSimilarity(closed, target, options)),
                InMode(false, () => PolylineAlignment.FitSimilarity(closed, target, options)));
        }
        Point2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        foreach (double perturbation in new[] { 0, 1e-15, 1e-14, 1e-13, 1e-9 })
        {
            Point2[] rectangle = [new(0, 0), new(1 + perturbation, 0), new(1 + perturbation, 1), new(0, 1)];
            var options = new AlignmentOptions { Closed = true, SampleCount = 4 };
            AlignmentResult? ScalarFit() => PolylineAlignment.FitSimilarity(rectangle, square.Reverse().ToArray(), options);
            var scalar = Capture(() => InMode(true, ScalarFit));
            var simd = Capture(() => InMode(false, ScalarFit));
            True("covariance threshold outcome unchanged", scalar.Exception?.GetType() == simd.Exception?.GetType());
            if (scalar.Result is not null && simd.Result is not null) FitEqual("near covariance threshold", scalar.Result, simd.Result);
            if (perturbation == 0) True("zero covariance rejected", scalar.Exception is ArgumentException);
            if (perturbation == 1e-9) True("resolved covariance accepted", scalar.Result is not null);
        }
        RejectBoth("collapsed alignment", () => PolylineAlignment.FitSimilarity([new(1, 1), new(1, 1)], square),
            typeof(ArgumentException), "path");
    }

    private static void CheckSampling()
    {
        Point2[] clean = [new(0, 0), new(4, 0), new(4, 2), new(1, 3), new(-1, 1)];
        Point2[] duplicates = [clean[0], clean[0], clean[1], clean[1], clean[1], clean[2], clean[3], clean[3], clean[4], clean[4]];
        foreach (int count in new[] { 3, 31, 64, 65, 257 })
        foreach (bool closed in new[] { false, true })
        {
            Point2[] path = closed ? [.. duplicates, clean[0], clean[0]] : duplicates;
            Point2[] expected = InMode(true, () => PolylineSampling.ResampleByArcLength(clean, count, closed));
            PointsEqual("sampling cleans duplicate and closing vertices", expected,
                InMode(false, () => PolylineSampling.ResampleByArcLength(path, count, closed)));
            PointsEqual("sampling wrapper cleaning", expected,
                InMode(false, () => PolylineSampling.ResampleByArcLength(new ReadOnlyPoints(path), count, closed)));
            var options = new AlignmentOptions { SampleCount = count, Closed = closed };
            var cleanFit = InMode(true, () => PolylineAlignment.FitSimilarity(clean, clean, options));
            var dirtyFit = InMode(false, () => PolylineAlignment.FitSimilarity(path, clean, options));
            TransformEqual("validated sampler does not change fitted transform", cleanFit.Transform, dirtyFit.Transform);
            True("validated sampler retains residual", Same(cleanFit.RmsError, dirtyFit.RmsError));
            True("aligned snapshot retains duplicate and explicit close count", dirtyFit.AlignedPoints.Count == path.Length);
            PointsEqual("aligned snapshot retains original traversal", dirtyFit.Transform.Apply(path), dirtyFit.AlignedPoints);
        }
    }

    private static Point2[] Stroke(int count, double size) => [.. Enumerable.Range(0, count).Select(i =>
    {
        double t = (double)i / Math.Max(1, count - 1);
        return new Point2(size * (3 * t + .5 * Math.Sin(9 * t)), size * (Math.Sin(5 * t) + t));
    })];

    private static T InMode<T>(bool scalar, Func<T> action)
    {
        bool hadSwitch = AppContext.TryGetSwitch(DisableSimd, out bool previous);
        AppContext.SetSwitch(DisableSimd, scalar);
        try { return action(); }
        finally { AppContext.SetSwitch(DisableSimd, hadSwitch && previous); }
    }

    private static void RejectBoth(string name, Action action, Type expected, string? parameter)
    {
        foreach (bool scalar in new[] { true, false })
        {
            Exception? caught = null;
            try { InMode(scalar, () => { action(); return 0; }); }
            catch (Exception e) { caught = e; }
            True(name + " rejects with original type and parameter", caught?.GetType() == expected &&
                (caught as ArgumentException)?.ParamName == parameter);
        }
    }

    private static (AlignmentResult? Result, Exception? Exception) Capture(Func<AlignmentResult?> action)
    {
        try { return (action(), null); } catch (ArgumentException e) { return (null, e); }
    }

    private static bool Same(double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y);
    private static bool BoundsEqual(Bounds2D x, Bounds2D y) =>
        Same(x.MinX, y.MinX) && Same(x.MaxX, y.MaxX) && Same(x.MinY, y.MinY) && Same(x.MaxY, y.MaxY);

    private static void PointsEqual(string name, IReadOnlyList<Point2> expected, IReadOnlyList<Point2> actual) =>
        True(name, expected.Count == actual.Count && Enumerable.Range(0, expected.Count)
            .All(i => Same(expected[i].X, actual[i].X) && Same(expected[i].Y, actual[i].Y)));

    private static void TransformEqual(string name, AffineTransform2D expected, AffineTransform2D actual) =>
        True(name, Same(expected.M11, actual.M11) && Same(expected.M12, actual.M12) &&
            Same(expected.M21, actual.M21) && Same(expected.M22, actual.M22) &&
            Same(expected.OffsetX, actual.OffsetX) && Same(expected.OffsetY, actual.OffsetY));

    private static void FitEqual(string name, AlignmentResult scalar, AlignmentResult simd)
    {
        TransformEqual(name + " transform", scalar.Transform, simd.Transform);
        PointsEqual(name + " output", scalar.AlignedPoints, simd.AlignedPoints);
        True(name + " metadata", Same(scalar.RmsError, simd.RmsError) && Same(scalar.Scale, simd.Scale) &&
            Same(scalar.RotationRadians, simd.RotationRadians) && Same(scalar.PhaseShift, simd.PhaseShift) &&
            scalar.Reversed == simd.Reversed && scalar.SampleCount == simd.SampleCount && scalar.Strategy == simd.Strategy);
    }

    private static void True(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException("Optimization: " + name);
        passed++;
    }

    // Exercises the portable IReadOnlyList contract without an array/list fast-path type.
    private sealed class ReadOnlyPoints(Point2[] points) : IReadOnlyList<Point2>
    {
        public Point2 this[int index] => points[index];
        public int Count => points.Length;
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
