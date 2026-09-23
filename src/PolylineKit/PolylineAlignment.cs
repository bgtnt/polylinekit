namespace PolylineKit;

/// <summary>Options for an arc-length-correspondence least-squares fit.</summary>
public sealed class AlignmentOptions
{
    /// <summary>Number of equally spaced arc-length samples, from 3 to 1024. Default 64.</summary>
    public int SampleCount { get; set; } = 64;
    /// <summary>Whether both input paths have an implicit closing edge. Default false.</summary>
    public bool Closed { get; set; }
    /// <summary>Fit uniform positive scale in addition to proper rotation and translation. Default true.</summary>
    public bool AllowScaling { get; set; } = true;
    /// <summary>Also test opposite traversal correspondence. Does not enable reflection. Default false.</summary>
    public bool AllowReversal { get; set; }
    /// <summary>For closed paths, search the SampleCount discrete reference starting phases. Default true.</summary>
    public bool SearchClosedPhase { get; set; } = true;
}

/// <summary>An immutable fit result; aligned points retain the original moving input order and count.</summary>
public sealed class AlignmentResult
{
    internal AlignmentResult(AffineTransform2D transform, Point2[] points, double rmsError,
        double scale, double angle, bool reversed, double phaseShift, int samples, bool scaling)
    {
        Transform = transform; AlignedPoints = Array.AsReadOnly(points); RmsError = rmsError;
        Scale = scale; RotationRadians = angle; Reversed = reversed; PhaseShift = phaseShift;
        SampleCount = samples; Strategy = scaling ? "ArcLengthSimilarityLeastSquares" : "ArcLengthRigidLeastSquares";
    }
    /// <summary>Transformation from original moving coordinates into reference coordinates.</summary>
    public AffineTransform2D Transform { get; }
    /// <summary>Transformed snapshot of the original moving points; traversal is not reordered.</summary>
    public IReadOnlyList<Point2> AlignedPoints { get; }
    /// <summary>RMS Euclidean residual after applying Transform to the sampled correspondence, in reference units.</summary>
    public double RmsError { get; }
    /// <summary>The uniform positive scale; one for a rigid fit.</summary>
    public double Scale { get; }
    /// <summary>Counterclockwise rotation in radians, in the range [-pi, pi].</summary>
    public double RotationRadians { get; }
    /// <summary>Whether the chosen reference correspondence runs opposite to moving traversal.</summary>
    public bool Reversed { get; }
    /// <summary>Closed reference start offset as a fraction of its perimeter; zero for open paths.</summary>
    public double PhaseShift { get; }
    /// <summary>Number of arc-length samples used in each path.</summary>
    public int SampleCount { get; }
    /// <summary>The correspondence and transformation strategy used.</summary>
    public string Strategy { get; }
}

/// <summary>Alignment of ordered strokes, separate from any area comparison.</summary>
public static class PolylineAlignment
{
    /// <summary>Fits moving to reference using sampled arc-length correspondence and least squares.</summary>
    /// <remarks>
    /// Reflection is excluded. A closed phase search evaluates only discrete sample offsets, not a
    /// continuous global optimum. The objective is sampled Euclidean residual, not filled area.
    /// Zero-length inputs and fits with effectively zero rotational covariance are rejected.
    /// Fitting uses centered, scaled samples; the reported residual uses the returned affine map.
    /// Large coordinate offsets can lose precision when that map is applied; center inputs first
    /// when small local differences must be resolved accurately.
    /// </remarks>
    public static AlignmentResult FitSimilarity(IReadOnlyList<Point2> moving,
        IReadOnlyList<Point2> reference, AlignmentOptions? options = null)
    {
        options ??= new AlignmentOptions();
        int n = options.SampleCount;
        bool closed = options.Closed, scaling = options.AllowScaling;
        bool reversal = options.AllowReversal, phases = closed && options.SearchClosedPhase;
        if (n < 3 || n > 1024) throw new ArgumentOutOfRangeException(nameof(options), "SampleCount must be from 3 to 1024.");
        // Preserve duplicate and explicit closing vertices in the output snapshot.
        Point2[] original = PathInput.CopyValidated(moving, nameof(moving));
        Point2[] xs = PolylineSampling.ResampleValidated(original, n, closed);
        Point2[] ys = PolylineSampling.ResampleByArcLength(reference, n, closed);
        var x = Normalize(xs); var y = Normalize(ys);
        double bestError = double.PositiveInfinity, bestAngle = 0, bestScale = 0;
        int bestPhase = 0; bool bestReverse = false;
        for (int reverse = 0; reverse <= (reversal ? 1 : 0); reverse++)
        for (int phase = 0; phase < (phases ? n : 1); phase++)
        {
            double dot = 0, cross = 0;
            int target = closed ? phase : reverse != 0 ? n - 1 : 0;
            for (int i = 0; i < n; i++)
            {
                Point2 a = x.Points[i], b = y.Points[target];
                dot += a.X * b.X + a.Y * b.Y;
                cross += a.X * b.Y - a.Y * b.X;
                target = NextIndex(target, reverse != 0, n);
            }
            double magnitude = Math.Sqrt(dot * dot + cross * cross);
            // An orientation from cancellation-level covariance is arbitrary and unstable.
            if (magnitude <= 1e-14 * Math.Sqrt(x.Variance * y.Variance)) continue;
            double cos = dot / magnitude, sin = cross / magnitude;
            double fitFactor = scaling ? magnitude / x.Variance : 1;
            double fittedScale = scaling ? PositiveRatioProduct(y.Span, fitFactor, x.Span) : 1;
            if (!(fittedScale > 0) || double.IsInfinity(fittedScale)) continue;
            double residualUnit = scaling ? y.Span : Math.Max(x.Span, y.Span);
            double xFactor = scaling ? fitFactor : x.Span / residualUnit;
            double yFactor = scaling ? 1 : y.Span / residualUnit;
            double error = 0;
            target = closed ? phase : reverse != 0 ? n - 1 : 0;
            for (int i = 0; i < n; i++)
            {
                Point2 a = x.Points[i], b = y.Points[target];
                double dx = xFactor * (cos * a.X - sin * a.Y) - yFactor * b.X;
                double dy = xFactor * (sin * a.X + cos * a.Y) - yFactor * b.Y;
                error += dx * dx + dy * dy;
                target = NextIndex(target, reverse != 0, n);
            }
            if (error < bestError)
            {
                bestError = error; bestAngle = Math.Atan2(cross, dot); bestScale = fittedScale;
                bestPhase = phase; bestReverse = reverse != 0;
            }
        }
        if (double.IsInfinity(bestError))
            throw new ArgumentException("No numerically defined proper similarity fit exists for this correspondence.");

        double c = Math.Cos(bestAngle) * bestScale, s = Math.Sin(bestAngle) * bestScale;
        double tx = y.Center.X - (c * x.Center.X - s * x.Center.Y);
        double ty = y.Center.Y - (s * x.Center.X + c * x.Center.Y);
        if (!Finite(c) || !Finite(s) || !Finite(tx) || !Finite(ty))
            throw new ArgumentException("The fitted transformation exceeds the numeric range.");
        var transform = new AffineTransform2D(c, -s, s, c, tx, ty);
        double rms = AppliedResidual(transform, xs, ys, bestPhase, bestReverse, closed);
        return new AlignmentResult(transform, transform.Apply(original), rms, bestScale,
            bestAngle, bestReverse, (double)bestPhase / n, n, scaling);
    }

    private static double AppliedResidual(AffineTransform2D transform, Point2[] moving,
        Point2[] reference, int phase, bool reversed, bool closed)
    {
        Point2[] applied = transform.Apply(moving);
        double maximum = 0;
        for (int i = 0; i < applied.Length; i++)
        {
            Point2 target = reference[Index(i, phase, reversed, closed, applied.Length)];
            maximum = Math.Max(maximum, Math.Max(Math.Abs(applied[i].X - target.X), Math.Abs(applied[i].Y - target.Y)));
        }
        if (maximum == 0) return 0;
        double sum = 0;
        for (int i = 0; i < applied.Length; i++)
        {
            Point2 target = reference[Index(i, phase, reversed, closed, applied.Length)];
            double dx = (applied[i].X - target.X) / maximum;
            double dy = (applied[i].Y - target.Y) / maximum;
            sum += dx * dx + dy * dy;
        }
        return maximum * Math.Sqrt(sum / applied.Length);
    }

    private static int Index(int i, int phase, bool reverse, bool closed, int n) =>
        closed ? (phase + (reverse ? n - i : i)) % n : reverse ? n - 1 - i : i;

    // Same correspondence and iteration order as Index, without division per sample.
    private static int NextIndex(int current, bool reverse, int n) =>
        reverse ? (current == 0 ? n - 1 : current - 1) : (current + 1 == n ? 0 : current + 1);

    private static double PositiveRatioProduct(double numerator, double factor, double denominator)
    {
        double value = numerator / denominator * factor;
        return value > 0 && !double.IsInfinity(value) ? value :
            Math.Exp(Math.Log(numerator) + Math.Log(factor) - Math.Log(denominator));
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static NormalizedSamples Normalize(Point2[] points)
    {
        double minX = points[0].X, maxX = minX, minY = points[0].Y, maxY = minY;
        foreach (Point2 p in points)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        double span = Math.Max(maxX - minX, maxY - minY);
        if (!(span > 0)) throw new ArgumentException("The samples have zero spatial extent; use more samples or another path.");
        double originX = minX + (maxX - minX) / 2, originY = minY + (maxY - minY) / 2;
        double meanX = 0, meanY = 0;
        var normalized = new Point2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            normalized[i] = new Point2((points[i].X - originX) / span, (points[i].Y - originY) / span);
            meanX += normalized[i].X / points.Length; meanY += normalized[i].Y / points.Length;
        }
        double variance = 0;
        for (int i = 0; i < points.Length; i++)
        {
            normalized[i] = new Point2(normalized[i].X - meanX, normalized[i].Y - meanY);
            variance += normalized[i].X * normalized[i].X + normalized[i].Y * normalized[i].Y;
        }
        if (!(variance > 0)) throw new ArgumentException("The samples have zero variance.");
        return new NormalizedSamples(normalized, new Point2(originX + meanX * span, originY + meanY * span), span, variance);
    }

    private sealed class NormalizedSamples
    {
        public NormalizedSamples(Point2[] points, Point2 center, double span, double variance)
        { Points = points; Center = center; Span = span; Variance = variance; }
        public Point2[] Points { get; }
        public Point2 Center { get; }
        public double Span { get; }
        public double Variance { get; }
    }
}
