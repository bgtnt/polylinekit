using PolylineKit;

namespace PolylineKit.Recognition;

/// <summary>Fresh analytical recognition fixtures, independent of legacy code and all evaluation datasets.</summary>
public static class EngineChecks
{
    private static int passed;
    private const string DisableSimd = "PolylineKit.DisableSimd";

    public static int Run()
    {
        passed = 0;
        CheckSharedScores();
        CheckTilt();
        CheckProtractor();
        CheckDtw();
        CheckOwnershipAndValidation();
        CheckScalarEquivalenceAndTies();
        Console.WriteLine($"Recognition engine checks: {passed}");
        return passed;
    }

    private static void CheckSharedScores()
    {
        Point2[] stroke = [new(0, 0), new(2, 0), new(2, 1), new(1, 3)];
        PreparedStroke prepared = RecognitionEngine.Prepare(stroke);
        PairScores identity = RecognitionEngine.ScorePair(prepared, prepared);
        Exact("RMS identity", 0, identity.Rms); Exact("area identity", 0, identity.Area);
        True("identity chooses no transform", identity.RotationDegrees == 0);
        Exact("standalone RMS equals pair", identity.Rms, RecognitionEngine.ScoreRms(prepared, prepared));
        Exact("standalone area equals pair", identity.Area, RecognitionEngine.ScoreArea(prepared, prepared));
        PreparedStroke moved = RecognitionEngine.Prepare(stroke.Select(p => new Point2(4 * p.X + 5, 4 * p.Y - 7)).ToArray());
        Near("uniform scale/translation are removed", 0, RecognitionEngine.ScoreRms(moved, prepared), 1e-14);
        Exact("uniform scale/translation area", 0, RecognitionEngine.ScoreArea(moved, prepared));
        PreparedStroke rising = RecognitionEngine.Prepare([new(-.5, -.5), new(.5, .5)]);
        PreparedStroke falling = RecognitionEngine.Prepare([new(-.5, .5), new(.5, -.5)]);
        PairScores cross = RecognitionEngine.ScorePair(rising, falling);
        // 64 samples of vertical displacement 2x, x uniformly spanning [-1/2,1/2].
        Near("crossed-line RMS analytical sum", Math.Sqrt(65.0 / 189), cross.Rms, 1e-14);
        Near("crossed-line unsigned two-lobe area", .5, cross.Area, 1e-12);
        Near("full-vertex diagnostic same crossed-line area", .5,
            RecognitionEngine.ScoreArea(rising, falling, fullVertexDiagnostic: true), 1e-12);
        Exact("pair stores exact shared transform RMS", cross.Rms, cross.Alignment.Rms);
        Exact("combined scale calculation", 2.5, RecognitionEngine.Combine(2, 12, .25, 1, 3));
        Exact("combined RMS endpoint", cross.Rms / 2,
            RecognitionEngine.ScoreCombined(rising, falling, 0, 2, 3));
        Exact("combined area endpoint", cross.Area / 3,
            RecognitionEngine.ScoreCombined(rising, falling, 1, 2, 3));
        Exact("combined mixture reuses shared scores", RecognitionEngine.Combine(cross.Rms, cross.Area, .5, 2, 3),
            RecognitionEngine.ScoreCombined(rising, falling, .5, 2, 3));
        PreparedStroke reversed = RecognitionEngine.Prepare(stroke.Reverse().ToArray());
        True("ordered reversal is not removed", RecognitionEngine.ScoreRms(reversed, prepared) > .1);
        PreparedStroke upsideDown = RecognitionEngine.Prepare(stroke.Select(p => new Point2(-p.X, -p.Y)).ToArray());
        True("digits retain 180-degree orientation", RecognitionEngine.ScoreRms(upsideDown, prepared) > .1);
    }

    private static void CheckTilt()
    {
        Point2[] samples = Enumerable.Range(0, 64).Select(i =>
        {
            double t = (double)i / 63;
            return new Point2(t - .5, .2 * Math.Sin(5 * t) + .1 * t);
        }).ToArray();
        PreparedStroke template = FromNormalized(samples);
        foreach (int degrees in new[] { -20, -10, 0, 10, 20, 180 })
        {
            PreparedStroke query = FromNormalized(AffineTransform2D.Rotation(degrees * Math.PI / 180).Apply(samples));
            SharedAlignment aligned = RecognitionEngine.Align(query, template, 15);
            int expected = degrees switch { -20 => 15, -10 => 10, 0 => 0, 10 => -10, 20 => -15, _ => aligned.RotationDegrees };
            True("bounded discrete rotation", aligned.RotationDegrees == expected && Math.Abs(aligned.RotationDegrees) <= 15);
            True("tilt never fits translation or scale", aligned.Transform.OffsetX == 0 && aligned.Transform.OffsetY == 0 &&
                Math.Abs(aligned.Transform.M11 * aligned.Transform.M11 + aligned.Transform.M21 * aligned.Transform.M21 - 1) < 1e-14);
            Point2[] applied = aligned.Transform.Apply(query.Samples64);
            double rms = Math.Sqrt(applied.Zip(template.Samples64).Sum(pair =>
            {
                double dx = pair.First.X - pair.Second.X, dy = pair.First.Y - pair.Second.Y;
                return dx * dx + dy * dy;
            }) / 64);
            Exact("reported RMS uses returned transform", rms, aligned.Rms);
            double directArea = PolylineComparison.EndpointBridgedArea(template.Samples64, applied, PathFillRule.NonZero, 6).RawArea;
            Exact("area uses exact RMS-selected transform", directArea, RecognitionEngine.ScoreArea(query, template, aligned));
            Exact("area-only bounded policy same result", directArea, RecognitionEngine.ScoreArea(query, template, 15));
            if (Math.Abs(degrees) <= 10) Near("known small rotation recovered", 0, aligned.Rms, 1e-14);
            if (degrees == 180) True("bounded policy preserves reversed orientation", aligned.Rms > .1);
        }
    }

    private static void CheckProtractor()
    {
        // Angular endpoint conditioning requires 1e-7 radians; ordinary finite angles use 1e-14.
        Near("native opposite vectors stay pi apart", Math.PI,
            RecognitionEngine.ProtractorDistance([1, 0], [-1, 0]), 1e-14);
        Near("a=0 positive b admits pi/2 rotation", 0,
            RecognitionEngine.ProtractorDistance([1, 0], [0, 1]), 1e-14);
        Near("a=0 negative b admits minus pi/2 rotation", 0,
            RecognitionEngine.ProtractorDistance([1, 0], [0, -1]), 1e-14);
        Near("zero rotational covariance gives pi/2 distance", Math.PI / 2,
            RecognitionEngine.ProtractorDistance([1, 0, 0, 0], [0, 0, 1, 0]), 1e-14);
        double q = Math.Sqrt(.5);
        Near("atan domain does not admit 135 degrees", Math.PI,
            RecognitionEngine.ProtractorDistance([1, 0], [-q, q]), 1e-7);
        Point2[] shape = [new(0, 0), new(3, 0), new(3, 1), new(1, 2)];
        Point2[] opposite = shape.Select(p => new Point2(-p.X, -p.Y)).ToArray();
        foreach (bool sensitive in new[] { false, true })
        {
            PreparedProtractor native = RecognitionEngine.PrepareProtractor(shape, sensitive);
            PreparedProtractor rotated = RecognitionEngine.PrepareProtractor(opposite, sensitive);
            True("native vector uses 16 points", native.Vector.Count == 32);
            Near("native vector has unit norm", 1, native.Vector.Sum(x => x * x), 1e-14);
            Near("native identity", 0, RecognitionEngine.ScoreProtractor(native, native), 1e-7);
            Near("native declared 180-degree policy", sensitive ? Math.PI : 0,
                RecognitionEngine.ScoreProtractor(rotated, native), 1e-7);
            PreparedProtractor scaled = RecognitionEngine.PrepareProtractor(shape.Select(p => new Point2(8 * p.X + 19, 8 * p.Y - 5)).ToArray(), sensitive);
            Near("native translation and scale invariance", 0, RecognitionEngine.ScoreProtractor(scaled, native), 1e-7);
            PreparedProtractor tiny = RecognitionEngine.PrepareProtractor(shape.Select(p => new Point2(p.X * 1e-200, p.Y * 1e-200)).ToArray(), sensitive);
            Near("native tiny coordinates avoid norm underflow", 0, RecognitionEngine.ScoreProtractor(tiny, native), 1e-7);
        }
        Reject("cannot mix native orientation policies", () => RecognitionEngine.ScoreProtractor(
            RecognitionEngine.PrepareProtractor(shape, true), RecognitionEngine.PrepareProtractor(shape, false)));
        Reject("native zero vector rejected", () => RecognitionEngine.PrepareProtractor([new(1, 1), new(1, 1)], true));
    }

    private static void CheckDtw()
    {
        Point2[] first = [new(0, 0), new(0, 0), new(1, 0)];
        Point2[] second = [new(0, 0), new(1, 0), new(1, 0)];
        Near("DTW diagonal cost divided by sample count", 1.0 / 3, RecognitionEngine.DtwDistance(first, second, 0), 1e-14);
        Exact("DTW permits monotone repetition", 0, RecognitionEngine.DtwDistance(first, second, 1));
        Exact("DTW forces mismatching endpoints", 1, RecognitionEngine.DtwDistance([new(0, 0), new(0, 0)], [new(1, 0), new(1, 0)], 1));
        Point2[][] cases =
        [
            [new(0, 0), new(1, 1), new(2, 0)],
            [new(0, 1), new(1, 0), new(2, 2)],
            [new(1, 1), new(0, 0), new(1, 1)]
        ];
        foreach (Point2[] query in cases)
        foreach (Point2[] template in cases)
        foreach (int window in new[] { 0, 1, 2 })
        {
            // Independent exhaustive path enumeration is feasible for these three-point fixtures.
            double brute = BruteDtw(query, template, window, 0, 0) / query.Length;
            Exact("DTW equals exhaustive monotone path minimum", brute, RecognitionEngine.DtwDistance(query, template, window));
        }
        PreparedStroke a = RecognitionEngine.Prepare([new(0, 0), new(1, 2), new(2, 1)]);
        PreparedStroke b = RecognitionEngine.Prepare([new(0, 0), new(1, 1), new(2, 2)]);
        foreach (int window in new[] { 8, 16, 63 }) Exact("public DTW identity", 0, RecognitionEngine.ScoreDtw(a, a, window));
        double narrow = RecognitionEngine.ScoreDtw(a, b, 8), medium = RecognitionEngine.ScoreDtw(a, b, 16), wide = RecognitionEngine.ScoreDtw(a, b, 63);
        True("larger DTW window cannot raise optimum", wide <= medium && medium <= narrow);
        Reject("undeclared DTW window rejected", () => RecognitionEngine.ScoreDtw(a, b, 7));
    }

    private static void CheckOwnershipAndValidation()
    {
        Point2[] raw = [new(0, 0), new(0, 0), new(2, 0), new(2, 1)];
        PreparedStroke result = RecognitionEngine.Prepare(raw);
        Point2[] normalizedBefore = result.NormalizedFull.ToArray();
        raw[0] = new Point2(99, 99); raw[2] = new Point2(-99, -99);
        True("preparation preserves original ownership", result.Original[0].X == 0 && result.Original[2].X == 2);
        True("duplicates kept only in original diagnostic path", result.Original.Count == 4 && result.NormalizedFull.Count == 3);
        PointsEqual("normalization immutable snapshot", normalizedBefore, result.NormalizedFull);
        True("prepared arrays exposed read-only", ((ICollection<Point2>)result.Original).IsReadOnly &&
            ((ICollection<Point2>)result.NormalizedFull).IsReadOnly && ((ICollection<Point2>)result.Samples64).IsReadOnly);
        PreparedProtractor native = RecognitionEngine.PrepareProtractor([new(0, 0), new(1, 1)], false);
        True("native vector exposed read-only", ((ICollection<double>)native.Vector).IsReadOnly);
        foreach (Point2[] bad in new Point2[][]
        {
            [], [new(0, 0)], [new(0, 0), new(0, 0)], [new(0, 0), new(double.NaN, 0)],
            [new(0, 0), new(0, double.PositiveInfinity)], [new(0, 0), new(1.01e100, 0)]
        })
        {
            Reject("shared invalid path rejected", () => RecognitionEngine.Prepare(bad));
            Reject("native invalid path rejected", () => RecognitionEngine.PrepareProtractor(bad, true));
        }
        Reject("undeclared tilt policy rejected", () => RecognitionEngine.Align(result, result, 14));
        Reject("zero RMS scale rejected", () => RecognitionEngine.Combine(1, 1, .5, 0, 1));
        Reject("nonfinite area scale rejected", () => RecognitionEngine.Combine(1, 1, .5, 1, double.NaN));
        Reject("out-of-range weight rejected", () => RecognitionEngine.Combine(1, 1, 1.01, 1, 1));
        Reject("failed score cannot enter ranking", () => RecognitionEngine.CompareRank(double.NaN, "a", 1, "b"));
    }

    private static void CheckScalarEquivalenceAndTies()
    {
        Point2[] raw = Enumerable.Range(0, 257).Select(i => new Point2(i / 256.0, Math.Sin(i / 31.0) * .2 + i / 512.0)).ToArray();
        Point2[] altered = raw.Select((p, i) => new Point2(p.X + .02 * Math.Sin(i / 17.0), p.Y + .01 * Math.Cos(i / 9.0))).ToArray();
        foreach (int tilt in new[] { 0, 15 })
        {
            var scalar = InMode(true, () => (Query: RecognitionEngine.Prepare(raw), Template: RecognitionEngine.Prepare(altered)));
            var simd = InMode(false, () => (Query: RecognitionEngine.Prepare(raw), Template: RecognitionEngine.Prepare(altered)));
            PointsEqual("scalar/SIMD prepared coordinates", scalar.Query.Samples64, simd.Query.Samples64);
            PairScores a = InMode(true, () => RecognitionEngine.ScorePair(scalar.Query, scalar.Template, tilt));
            PairScores b = InMode(false, () => RecognitionEngine.ScorePair(simd.Query, simd.Template, tilt));
            Exact("scalar/SIMD RMS", a.Rms, b.Rms); Exact("scalar/SIMD area", a.Area, b.Area);
            True("scalar/SIMD transform selection", a.RotationDegrees == b.RotationDegrees);
            PointsEqual("scalar/SIMD returned transformed path", a.Transform.Apply(scalar.Query.Samples64), b.Transform.Apply(simd.Query.Samples64));
            foreach (int window in new[] { 8, 16, 63 })
                Exact("scalar/SIMD DTW", InMode(true, () => RecognitionEngine.ScoreDtw(scalar.Query, scalar.Template, window)),
                    InMode(false, () => RecognitionEngine.ScoreDtw(simd.Query, simd.Template, window)));
        }
        True("exact tie uses ordinal ID", RecognitionEngine.CompareRank(.25, "sample-A", .25, "sample-a") < 0);
        True("one-ULP score difference is not a tie", RecognitionEngine.CompareRank(Math.BitIncrement(.25), "a", .25, "z") > 0);
        PreparedStroke query = RecognitionEngine.Prepare([new(0, 0), new(2, 0), new(2, 1)]);
        var bank = new[]
        {
            (Id: "z-identical", Label: "corner", Points: query),
            (Id: "b-other", Label: "line", Points: RecognitionEngine.Prepare([new(0, 0), new(2, 1)])),
            (Id: "a-identical", Label: "corner", Points: query)
        };
        foreach (bool scalar in new[] { true, false })
        {
            var ranked = InMode(scalar, () => bank.Select(t => (t.Id, t.Label, Score: RecognitionEngine.ScoreRms(query, t.Points))).ToList());
            ranked.Sort((x, y) => RecognitionEngine.CompareRank(x.Score, x.Id, y.Score, y.Id));
            True("frozen bank prediction and exact tie deterministic", ranked[0].Id == "a-identical" && ranked[0].Label == "corner");
        }
    }

    private static PreparedStroke FromNormalized(Point2[] samples) =>
        new(samples.ToArray(), samples.ToArray(), samples.ToArray(), AffineTransform2D.Identity);

    private static double BruteDtw(Point2[] query, Point2[] template, int window, int i, int j)
    {
        if (i >= query.Length || j >= template.Length || Math.Abs(i - j) > window) return double.PositiveInfinity;
        double dx = query[i].X - template[j].X, dy = query[i].Y - template[j].Y;
        double cost = dx * dx + dy * dy;
        if (i == query.Length - 1 && j == template.Length - 1) return cost;
        return cost + Math.Min(BruteDtw(query, template, window, i + 1, j + 1),
            Math.Min(BruteDtw(query, template, window, i + 1, j), BruteDtw(query, template, window, i, j + 1)));
    }

    private static T InMode<T>(bool scalar, Func<T> action)
    {
        bool had = AppContext.TryGetSwitch(DisableSimd, out bool previous);
        AppContext.SetSwitch(DisableSimd, scalar);
        try { return action(); } finally { AppContext.SetSwitch(DisableSimd, had && previous); }
    }
    private static void PointsEqual(string name, IReadOnlyList<Point2> x, IReadOnlyList<Point2> y) =>
        True(name, x.Count == y.Count && Enumerable.Range(0, x.Count).All(i => Same(x[i].X, y[i].X) && Same(x[i].Y, y[i].Y)));
    private static bool Same(double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y);
    private static void Exact(string name, double expected, double actual) => True(name, Same(expected, actual));
    private static void Near(string name, double expected, double actual, double tolerance) =>
        True(name, double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance);
    private static void Reject(string name, Action action)
    {
        try { action(); } catch (ArgumentException) { passed++; return; }
        throw new InvalidOperationException("Recognition engine accepted: " + name);
    }
    private static void True(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("Recognition engine: " + name); passed++; }
}
