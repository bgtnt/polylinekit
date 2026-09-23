namespace PolylineKit;

/// <summary>An affine map: x'=M11*x+M12*y+OffsetX, y'=M21*x+M22*y+OffsetY.</summary>
public readonly struct AffineTransform2D
{
    /// <summary>Creates an affine map with finite coefficients. The default struct is the zero map.</summary>
    public AffineTransform2D(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        if (!PathInput.Finite(m11) || !PathInput.Finite(m12) || !PathInput.Finite(m21) || !PathInput.Finite(m22) || !PathInput.Finite(offsetX) || !PathInput.Finite(offsetY))
            throw new ArgumentException("Transform coefficients must be finite.");
        M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = offsetX; OffsetY = offsetY;
    }
    /// <summary>Coefficient of x in transformed x.</summary>
    public double M11 { get; }
    /// <summary>Coefficient of y in transformed x.</summary>
    public double M12 { get; }
    /// <summary>Coefficient of x in transformed y.</summary>
    public double M21 { get; }
    /// <summary>Coefficient of y in transformed y.</summary>
    public double M22 { get; }
    /// <summary>X translation.</summary>
    public double OffsetX { get; }
    /// <summary>Y translation.</summary>
    public double OffsetY { get; }
    /// <summary>The identity transform.</summary>
    public static AffineTransform2D Identity => new(1, 0, 0, 1, 0, 0);
    /// <summary>Creates a translation.</summary>
    public static AffineTransform2D Translation(double x, double y) => new(1, 0, 0, 1, x, y);
    /// <summary>Creates independent scaling about the origin. Negative values reflect an axis.</summary>
    public static AffineTransform2D Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);
    /// <summary>Creates uniform scaling about the origin.</summary>
    public static AffineTransform2D Scaling(double scale) => Scaling(scale, scale);
    /// <summary>Creates counterclockwise rotation about the origin, in radians.</summary>
    public static AffineTransform2D Rotation(double radians)
    {
        if (!PathInput.Finite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new(c, -s, s, c, 0, 0);
    }
    /// <summary>Applies this transform first, followed by next.</summary>
    public AffineTransform2D Then(AffineTransform2D next) => new(
        next.M11 * M11 + next.M12 * M21, next.M11 * M12 + next.M12 * M22,
        next.M21 * M11 + next.M22 * M21, next.M21 * M12 + next.M22 * M22,
        next.M11 * OffsetX + next.M12 * OffsetY + next.OffsetX,
        next.M21 * OffsetX + next.M22 * OffsetY + next.OffsetY);
    /// <summary>Transforms a point; unrepresentable or out-of-range results are rejected.</summary>
    public Point2 Apply(Point2 point)
    {
        PathInput.Validate(point, nameof(point));
        var result = new Point2(M11 * point.X + M12 * point.Y + OffsetX, M21 * point.X + M22 * point.Y + OffsetY);
        PathInput.Validate(result, nameof(point));
        return result;
    }
    /// <summary>Transforms all points into a new array; input order and duplicate vertices are preserved.</summary>
    public Point2[] Apply(IReadOnlyList<Point2> points)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        var result = new Point2[points.Count];
        if (points is Point2[] array)
        {
#if NET10_0_OR_GREATER
            if (array.Length >= SimdTransforms.MinimumPointCount && SimdTransforms.TryApply(this, array, result)) return result;
#endif
            for (int i = 0; i < array.Length; i++) result[i] = Apply(array[i]);
            return result;
        }
        for (int i = 0; i < points.Count; i++) result[i] = Apply(points[i]);
        return result;
    }
}
