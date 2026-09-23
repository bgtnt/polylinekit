using PolylineKit;

namespace PolylineKit.Recognition;

/// <summary>Owned, immutable open-stroke preparation shared by RMS, area and the DTW baseline.</summary>
public sealed class PreparedStroke
{
    internal PreparedStroke(Point2[] original, Point2[] normalized, Point2[] samples, AffineTransform2D transform)
    {
        Original = Array.AsReadOnly(original); NormalizedFull = Array.AsReadOnly(normalized);
        Samples64 = Array.AsReadOnly(samples); SampleArray = samples; FullArray = normalized;
        NormalizationTransform = transform;
    }
    /// <summary>Original coordinates including adjacent duplicates, owned by this result.</summary>
    public IReadOnlyList<Point2> Original { get; }
    /// <summary>All normalized vertices after adjacent duplicate removal; no resampling.</summary>
    public IReadOnlyList<Point2> NormalizedFull { get; }
    /// <summary>64 equally spaced arc-length samples, including both endpoints.</summary>
    public IReadOnlyList<Point2> Samples64 { get; }
    /// <summary>Original coordinates to centered, uniformly scaled bounds coordinates.</summary>
    public AffineTransform2D NormalizationTransform { get; }
    internal Point2[] SampleArray { get; }
    internal Point2[] FullArray { get; }
}

/// <summary>Independent native 16-point Protractor representation; does not prepare RMS/area samples.</summary>
public sealed class PreparedProtractor
{
    internal PreparedProtractor(double[] vector, bool sensitive)
    { Vector = Array.AsReadOnly(vector); VectorArray = vector; OrientationSensitive = sensitive; }
    /// <summary>Interleaved XY unit vector with 32 components.</summary>
    public IReadOnlyList<double> Vector { get; }
    /// <summary>Whether native indicative-angle quantization retained absolute orientation.</summary>
    public bool OrientationSensitive { get; }
    internal double[] VectorArray { get; }
}

/// <summary>RMS-selected query rotation about the normalized origin, without fitted translation or scale.</summary>
public readonly record struct SharedAlignment(double Rms, int RotationDegrees, AffineTransform2D Transform);

/// <summary>Two scores evaluated after exactly the same discrete RMS-selected rotation.</summary>
public readonly record struct PairScores(double Rms, double Area, int RotationDegrees, AffineTransform2D Transform)
{
    public SharedAlignment Alignment => new(Rms, RotationDegrees, Transform);
}

/// <summary>Small experiment/consumer adapter. These recognition policies are not PolylineKit's public geometry API.</summary>
public static class RecognitionEngine
{
    public const int SharedSampleCount = 64;
    public const int ProtractorSampleCount = 16;
    public const string ImplementationId = "recognition-engine-v1";
    public const string ProtractorSource = "https://depts.washington.edu/acelab/proj/dollar/protractor.pdf";

    /// <summary>Preserves input ownership, removes adjacent duplicates, fits uniform unit bounds, then samples 64 points.</summary>
    public static PreparedStroke Prepare(Point2[] raw)
    {
        Point2[] original = CopyValidated(raw);
        Point2[] clean = RemoveAdjacentDuplicates(original);
        NormalizationResult normalized = PolylineNormalization.ToUnitBounds(clean, BoundsScaling.Uniform);
        Point2[] full = normalized.Points.ToArray();
        Point2[] sampled = PolylineSampling.ResampleByArcLength(full, SharedSampleCount);
        return new PreparedStroke(original, full, sampled, normalized.Transform);
    }

    /// <summary>Searches zero rotation or the fixed [-15,15] integer-degree grid. Exact ties prefer smaller absolute rotation.</summary>
    public static SharedAlignment Align(PreparedStroke query, PreparedStroke template, int tiltDegrees = 0)
    {
        ValidatePair(query, template); ValidateTilt(tiltDegrees);
        double best = SquaredResidual(query.SampleArray, template.SampleArray, AffineTransform2D.Identity);
        int bestAngle = 0;
        AffineTransform2D bestTransform = AffineTransform2D.Identity;
        for (int magnitude = 1; magnitude <= tiltDegrees; magnitude++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int angle = magnitude * sign;
            AffineTransform2D transform = AffineTransform2D.Rotation(angle * (Math.PI / 180));
            double residual = SquaredResidual(query.SampleArray, template.SampleArray, transform);
            if (residual < best) { best = residual; bestAngle = angle; bestTransform = transform; }
        }
        return new SharedAlignment(Math.Sqrt(best / SharedSampleCount), bestAngle, bestTransform);
    }

    /// <summary>RMS alone; does not compute any area.</summary>
    public static double ScoreRms(PreparedStroke query, PreparedStroke template, int tiltDegrees = 0) =>
        Align(query, template, tiltDegrees).Rms;

    /// <summary>Area alone. With no tilt policy, no RMS calculation is needed or performed.</summary>
    public static double ScoreArea(PreparedStroke query, PreparedStroke template, int tiltDegrees = 0,
        bool fullVertexDiagnostic = false)
    {
        ValidatePair(query, template); ValidateTilt(tiltDegrees);
        AffineTransform2D transform = tiltDegrees == 0 ? AffineTransform2D.Identity : Align(query, template, tiltDegrees).Transform;
        return AreaAtTransform(query, template, transform, fullVertexDiagnostic);
    }

    /// <summary>Area under a previously selected transform; full-vertex area is a separate secondary diagnostic.</summary>
    public static double ScoreArea(PreparedStroke query, PreparedStroke template, SharedAlignment alignment,
        bool fullVertexDiagnostic = false)
    {
        ValidatePair(query, template);
        return AreaAtTransform(query, template, alignment.Transform, fullVertexDiagnostic);
    }

    /// <summary>Computes both diagnostic scores under a shared transform, without bounds-area ratio normalization.</summary>
    public static PairScores ScorePair(PreparedStroke query, PreparedStroke template, int tiltDegrees = 0)
    {
        SharedAlignment alignment = Align(query, template, tiltDegrees);
        return new PairScores(alignment.Rms, ScoreArea(query, template, alignment), alignment.RotationDegrees, alignment.Transform);
    }

    /// <summary>Frozen RMS/area mixture. Weight zero avoids area; weight one and zero tilt avoid RMS.</summary>
    public static double ScoreCombined(PreparedStroke query, PreparedStroke template, double weight,
        double rmsScale, double areaScale, int tiltDegrees = 0)
    {
        ValidateCombination(weight, rmsScale, areaScale); ValidateTilt(tiltDegrees);
        if (weight == 0) return ScoreRms(query, template, tiltDegrees) / rmsScale;
        if (weight == 1) return ScoreArea(query, template, tiltDegrees) / areaScale;
        PairScores pair = ScorePair(query, template, tiltDegrees);
        return Combine(pair.Rms, pair.Area, weight, rmsScale, areaScale);
    }

    /// <summary>Combines already computed scores. Positive scales come only from frozen development medians.</summary>
    public static double Combine(double rms, double area, double weight, double rmsScale, double areaScale)
    {
        ValidateCombination(weight, rmsScale, areaScale);
        if (!double.IsFinite(rms) || rms < 0) throw new ArgumentOutOfRangeException(nameof(rms));
        if (!double.IsFinite(area) || area < 0) throw new ArgumentOutOfRangeException(nameof(area));
        return (1 - weight) * rms / rmsScale + weight * area / areaScale;
    }

    /// <summary>
    /// Native Protractor preparation based on Yang Li, CHI 2010, official pseudocode (accessed 2026-09-23):
    /// https://depts.washington.edu/acelab/proj/dollar/protractor.pdf . Fresh implementation, no copied source.
    /// Arc-length resampling uses PolylineKit's cumulative-length implementation of the same geometric sampling rule.
    /// Mean translation, optional nearest-45-degree indicative orientation, then unit-vector normalization follow the baseline.
    /// A positive temporary scale prevents underflow in the final norm without changing the mathematical vector.
    /// </summary>
    public static PreparedProtractor PrepareProtractor(Point2[] raw, bool orientationSensitive)
    {
        Point2[] original = CopyValidated(raw);
        Point2[] samples = PolylineSampling.ResampleByArcLength(original, ProtractorSampleCount);
        double cx = 0, cy = 0;
        foreach (Point2 point in samples) { cx += point.X / ProtractorSampleCount; cy += point.Y / ProtractorSampleCount; }
        double maximum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = new Point2(samples[i].X - cx, samples[i].Y - cy);
            maximum = Math.Max(maximum, Math.Max(Math.Abs(samples[i].X), Math.Abs(samples[i].Y)));
        }
        if (!(maximum > 0) || !double.IsFinite(maximum)) throw new ArgumentException("Protractor requires nonzero centered extent.", nameof(raw));
        // If the first sample equals the centroid, atan2(0,0)=0 is the declared native-angle convention.
        double indicative = Math.Atan2(samples[0].Y, samples[0].X);
        double baseOrientation = orientationSensitive ? Math.PI / 4 * Math.Floor((indicative + Math.PI / 8) / (Math.PI / 4)) : 0;
        double delta = baseOrientation - indicative, c = Math.Cos(delta), s = Math.Sin(delta);
        var vector = new double[2 * ProtractorSampleCount];
        double squared = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double x = samples[i].X / maximum, y = samples[i].Y / maximum;
            double vx = x * c - y * s, vy = y * c + x * s;
            vector[2 * i] = vx; vector[2 * i + 1] = vy;
            squared += vx * vx + vy * vy;
        }
        double norm = Math.Sqrt(squared);
        if (!(norm > 0) || !double.IsFinite(norm)) throw new ArgumentException("Protractor requires a nonzero finite vector.", nameof(raw));
        for (int i = 0; i < vector.Length; i++) vector[i] /= norm;
        return new PreparedProtractor(vector, orientationSensitive);
    }

    /// <summary>Native optimal-cosine angular distance. Keeps atan(b/a)'s admitted domain rather than replacing it with atan2.</summary>
    public static double ScoreProtractor(PreparedProtractor query, PreparedProtractor template)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(template);
        if (query.OrientationSensitive != template.OrientationSensitive)
            throw new ArgumentException("Query and template must use the same native Protractor orientation policy.");
        return ProtractorDistance(template.VectorArray, query.VectorArray);
    }

    internal static double ProtractorDistance(double[] template, double[] query)
    {
        double a = 0, b = 0;
        for (int i = 0; i < template.Length; i += 2)
        {
            a += template[i] * query[i] + template[i + 1] * query[i + 1];
            b += template[i] * query[i + 1] - template[i + 1] * query[i];
        }
        // At a=b=0 all admitted rotations have zero correlation. Division 0/0 is unnecessary.
        if (a == 0 && b == 0) return Math.PI / 2;
        double angle = Math.Atan(b / a);
        double cosine = a * Math.Cos(angle) + b * Math.Sin(angle);
        // A unit-vector dot product can overshoot an acos endpoint by rounding. Correct only this
        // explicitly bounded domain error, never round an in-range distance to zero or alter ties.
        const double roundoff = 64 * 2.2204460492503131e-16;
        if (cosine < -1 - roundoff || cosine > 1 + roundoff || !double.IsFinite(cosine))
            throw new ArithmeticException("Protractor cosine is outside the unit-vector rounding allowance.");
        return Math.Acos(Math.Max(-1, Math.Min(1, cosine)));
    }

    /// <summary>
    /// Standard dynamic-programming DTW recurrence with a Sakoe-Chiba window: squared Euclidean local cost,
    /// forced first/last correspondence, horizontal/vertical/diagonal moves; accumulated cost divided by 64.
    /// Preparation is the same uniform unit-bounds, orientation-preserving 64-point representation as RMS.
    /// The chosen windows are fixed by the experiment, not inferred from labels. No fitted rotation is used.
    /// Historical window attribution: H. Sakoe and S. Chiba, IEEE TASSP 26(1), 1978, doi:10.1109/TASSP.1978.1163055.
    /// This fresh baseline fixes the recurrence above; it does not claim to reproduce that paper's speech experiments.
    /// </summary>
    public static double ScoreDtw(PreparedStroke query, PreparedStroke template, int window)
    {
        ValidatePair(query, template);
        if (window != 8 && window != 16 && window != 63) throw new ArgumentOutOfRangeException(nameof(window));
        return DtwDistance(query.SampleArray, template.SampleArray, window);
    }

    internal static double DtwDistance(Point2[] query, Point2[] template, int window)
    {
        int n = query.Length;
        if (n != template.Length || n < 1 || n > SharedSampleCount || window < 0)
            throw new ArgumentException("DTW check inputs must have equal lengths from 1 to 64 and a nonnegative window.");
        Span<double> previous = stackalloc double[SharedSampleCount + 1];
        Span<double> current = stackalloc double[SharedSampleCount + 1];
        previous.Fill(double.PositiveInfinity); previous[0] = 0;
        for (int i = 1; i <= n; i++)
        {
            current.Fill(double.PositiveInfinity);
            for (int j = Math.Max(1, i - window); j <= Math.Min(n, i + window); j++)
            {
                double dx = query[i - 1].X - template[j - 1].X, dy = query[i - 1].Y - template[j - 1].Y;
                current[j] = dx * dx + dy * dy + Math.Min(previous[j - 1], Math.Min(previous[j], current[j - 1]));
            }
            Span<double> swap = previous; previous = current; current = swap;
        }
        return previous[n] / n;
    }

    /// <summary>Ascending finite distance, followed by ordinal canonical sample ID; no epsilon tie buckets.</summary>
    public static int CompareRank(double firstScore, string firstId, double secondScore, string secondId)
    {
        if (!double.IsFinite(firstScore) || !double.IsFinite(secondScore)) throw new ArgumentException("Rank only successful finite scores.");
        ArgumentNullException.ThrowIfNull(firstId); ArgumentNullException.ThrowIfNull(secondId);
        int order = firstScore.CompareTo(secondScore);
        return order != 0 ? order : StringComparer.Ordinal.Compare(firstId, secondId);
    }

    private static double AreaAtTransform(PreparedStroke query, PreparedStroke template, AffineTransform2D transform, bool full)
    {
        Point2[] q = full ? query.FullArray : query.SampleArray;
        Point2[] t = full ? template.FullArray : template.SampleArray;
        // Applying even the identity keeps the exact same public affine arithmetic in diagnostic and scoring calls.
        return PolylineComparison.EndpointBridgedArea(t, transform.Apply(q), PathFillRule.NonZero, 6).RawArea;
    }

    private static double SquaredResidual(Point2[] query, Point2[] template, AffineTransform2D transform)
    {
        double sum = 0;
        for (int i = 0; i < query.Length; i++)
        {
            // Same operation association as AffineTransform2D.Apply, without allocating a candidate path.
            double x = transform.M11 * query[i].X + transform.M12 * query[i].Y + transform.OffsetX;
            double y = transform.M21 * query[i].X + transform.M22 * query[i].Y + transform.OffsetY;
            double dx = x - template[i].X, dy = y - template[i].Y;
            sum += dx * dx + dy * dy;
        }
        return sum;
    }

    private static Point2[] CopyValidated(Point2[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < 2) throw new ArgumentException("An open stroke needs at least two distinct points.", nameof(raw));
        var result = new Point2[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            Point2 p = raw[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1e100 || Math.Abs(p.Y) > 1e100)
                throw new ArgumentException("Coordinates must be finite with magnitude at most 1e100.", nameof(raw));
            result[i] = p;
        }
        return result;
    }

    private static Point2[] RemoveAdjacentDuplicates(Point2[] raw)
    {
        var clean = new List<Point2>(raw.Length);
        foreach (Point2 p in raw)
            if (clean.Count == 0 || p.X != clean[^1].X || p.Y != clean[^1].Y) clean.Add(p);
        if (clean.Count < 2) throw new ArgumentException("An open stroke needs nonzero extent.", nameof(raw));
        return clean.ToArray();
    }

    private static void ValidatePair(PreparedStroke query, PreparedStroke template)
    { ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(template); }

    private static void ValidateTilt(int tiltDegrees)
    { if (tiltDegrees != 0 && tiltDegrees != 15) throw new ArgumentOutOfRangeException(nameof(tiltDegrees), "Only 0 or 15 degrees are declared policies."); }

    private static void ValidateCombination(double weight, double rmsScale, double areaScale)
    {
        if (!double.IsFinite(weight) || weight < 0 || weight > 1) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!double.IsFinite(rmsScale) || rmsScale <= 0) throw new ArgumentOutOfRangeException(nameof(rmsScale));
        if (!double.IsFinite(areaScale) || areaScale <= 0) throw new ArgumentOutOfRangeException(nameof(areaScale));
    }
}
