namespace PolylineKit;

/// <summary>An axis-aligned Cartesian rectangle, including zero-width or zero-height bounds.</summary>
public readonly struct Bounds2D
{
    /// <summary>Creates ordered finite bounds; coordinate magnitudes must not exceed 1e100.</summary>
    public Bounds2D(double minX, double minY, double maxX, double maxY)
    {
        PathInput.Validate(new(minX, minY), nameof(minX));
        PathInput.Validate(new(maxX, maxY), nameof(maxX));
        if (minX > maxX || minY > maxY) throw new ArgumentException("Bounds must be ordered.");
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
    }
    /// <summary>Minimum x coordinate.</summary>
    public double MinX { get; }
    /// <summary>Minimum y coordinate.</summary>
    public double MinY { get; }
    /// <summary>Maximum x coordinate.</summary>
    public double MaxX { get; }
    /// <summary>Maximum y coordinate.</summary>
    public double MaxY { get; }
    /// <summary>Width in coordinate units.</summary>
    public double Width => MaxX - MinX;
    /// <summary>Height in coordinate units.</summary>
    public double Height => MaxY - MinY;
    /// <summary>Rectangle area in squared coordinate units.</summary>
    public double Area => Width * Height;
    /// <summary>Rectangle center, not the path centroid.</summary>
    public Point2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);
    /// <summary>Calculates bounds without changing or resampling a nonempty input.</summary>
    public static Bounds2D FromPoints(IReadOnlyList<Point2> points)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        if (points.Count == 0) throw new ArgumentException("The path must not be empty.", nameof(points));
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i]; PathInput.Validate(p, nameof(points));
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return new(minX, minY, maxX, maxY);
    }
    /// <summary>The smallest rectangle containing both rectangles.</summary>
    public Bounds2D Union(Bounds2D other) => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
}
