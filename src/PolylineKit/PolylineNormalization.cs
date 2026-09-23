namespace PolylineKit;

/// <summary>How bounds scaling treats the shape's aspect ratio.</summary>
public enum BoundsScaling
{
    /// <summary>Preserve aspect ratio and fit inside the target rectangle.</summary>
    Uniform,
    /// <summary>Scale nonzero axes independently. A collapsed axis remains collapsed and centered.</summary>
    Stretch
}

/// <summary>A deterministic bounds transformation with the transformed path and its provenance.</summary>
public sealed class NormalizationResult
{
    internal NormalizationResult(Point2[] points, AffineTransform2D transform, Bounds2D original, Bounds2D bounds, BoundsScaling strategy)
    {
        Points = Array.AsReadOnly(points); Transform = transform; OriginalBounds = original;
        Bounds = bounds; Strategy = strategy;
    }
    /// <summary>Transformed points in original order.</summary>
    public IReadOnlyList<Point2> Points { get; }
    /// <summary>The exact affine map used to produce Points, subject to double arithmetic.</summary>
    public AffineTransform2D Transform { get; }
    /// <summary>Bounds before transformation.</summary>
    public Bounds2D OriginalBounds { get; }
    /// <summary>Bounds after transformation.</summary>
    public Bounds2D Bounds { get; }
    /// <summary>The chosen bounds scaling strategy.</summary>
    public BoundsScaling Strategy { get; }
}

/// <summary>Explicit bounds normalization; no rotation search or correspondence inference.</summary>
public static class PolylineNormalization
{
    /// <summary>Centers a noncollapsed path at the origin and fits it to a square of side size.</summary>
    /// <remarks>
    /// Uniform scaling maps the longest bounds side to size. Stretch independently scales nonzero axes.
    /// A point-only path is rejected. Ill-conditioned affine maps that materially lose centering or shape
    /// precision are rejected; translate such inputs closer to the origin before normalization.
    /// </remarks>
    public static NormalizationResult ToUnitBounds(IReadOnlyList<Point2> points, BoundsScaling scaling = BoundsScaling.Uniform, double size = 1)
    {
        if (!PathInput.Finite(size) || size <= 0 || size > 1e100) throw new ArgumentOutOfRangeException(nameof(size));
        return FitBounds(points, new Bounds2D(-size / 2, -size / 2, size / 2, size / 2), scaling);
    }

    /// <summary>Fits and centers a path in the bounds of a reference path.</summary>
    public static NormalizationResult MatchBounds(IReadOnlyList<Point2> moving, IReadOnlyList<Point2> reference, BoundsScaling scaling = BoundsScaling.Uniform) =>
        FitBounds(moving, Bounds2D.FromPoints(reference), scaling);

    /// <summary>Fits and centers a path in the target bounds; returns the actual applied transform.</summary>
    /// <remarks>
    /// Does not rotate. A nonzero source axis cannot fit into a zero target axis. Fully collapsed
    /// source/target bounds are rejected. Affine application is checked against a calculation relative
    /// to the source minimum: differences above 1e-10 of the fitted extent, plus an allowance for
    /// floating-point rounding at the target coordinates, cause an ArgumentException.
    /// </remarks>
    public static NormalizationResult FitBounds(IReadOnlyList<Point2> points, Bounds2D target, BoundsScaling scaling = BoundsScaling.Uniform)
    {
        if (scaling != BoundsScaling.Uniform && scaling != BoundsScaling.Stretch) throw new ArgumentOutOfRangeException(nameof(scaling));
        Bounds2D source = Bounds2D.FromPoints(points);
        if (Math.Max(source.Width, source.Height) == 0 || Math.Max(target.Width, target.Height) == 0)
            throw new ArgumentException("Point-only bounds do not define a scaling.");
        if ((source.Width > 0 && target.Width == 0) || (source.Height > 0 && target.Height == 0))
            throw new ArgumentException("A nonzero source axis cannot be collapsed to fit the target bounds.");
        double sx = source.Width > 0 ? target.Width / source.Width : double.PositiveInfinity;
        double sy = source.Height > 0 ? target.Height / source.Height : double.PositiveInfinity;
        if (scaling == BoundsScaling.Uniform) sx = sy = Math.Min(sx, sy);
        else { if (source.Width == 0) sx = 1; if (source.Height == 0) sy = 1; }
        if (!PathInput.Finite(sx) || !PathInput.Finite(sy) || sx <= 0 || sy <= 0)
            throw new ArgumentException("The requested bounds scale is not representable.");
        Point2 from = source.Center, to = target.Center;
        AffineTransform2D transform = AffineTransform2D.Translation(-from.X, -from.Y)
            .Then(AffineTransform2D.Scaling(sx, sy)).Then(AffineTransform2D.Translation(to.X, to.Y));
        Point2[] transformed = transform.Apply(points);
        Bounds2D actualBounds = ValidatePrecisionAndBounds(points, transformed, source, target, sx, sy);
        return new(transformed, transform, source, actualBounds, scaling);
    }

    private static Bounds2D ValidatePrecisionAndBounds(IReadOnlyList<Point2> original, Point2[] transformed,
        Bounds2D source, Bounds2D target, double sx, double sy)
    {
        double width = source.Width * sx, height = source.Height * sy;
        double extent = Math.Max(width, height);
        double targetMagnitude = Math.Max(Math.Max(Math.Abs(target.MinX), Math.Abs(target.MaxX)),
            Math.Max(Math.Abs(target.MinY), Math.Abs(target.MaxY)));
        const double machineEpsilon = 2.2204460492503131e-16;
        double tolerance = 1e-10 * extent + 8 * machineEpsilon * targetMagnitude + 8 * double.Epsilon;
        Point2 center = target.Center;
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        for (int i = 0; i < transformed.Length; i++)
        {
            // Source-minimum subtraction preserves representable local displacements, even when
            // the source center itself cannot represent the half-way position between two doubles.
            double expectedX = ((original[i].X - source.MinX) * sx - width / 2) + center.X;
            double expectedY = ((original[i].Y - source.MinY) * sy - height / 2) + center.Y;
            if (Math.Abs(transformed[i].X - expectedX) > tolerance || Math.Abs(transformed[i].Y - expectedY) > tolerance)
                throw new ArgumentException("The affine normalization loses significant precision. Translate input coordinates closer to the origin before normalizing.", "points");
            // Apply already validates every output coordinate. Measure the actual rounded
            // points, rather than inferring bounds from the ideal transformation.
            minX = Math.Min(minX, transformed[i].X); maxX = Math.Max(maxX, transformed[i].X);
            minY = Math.Min(minY, transformed[i].Y); maxY = Math.Max(maxY, transformed[i].Y);
        }
        return new Bounds2D(minX, minY, maxX, maxY);
    }
}
