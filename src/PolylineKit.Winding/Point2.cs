namespace PolylineKit;

/// <summary>A point in an existing Cartesian coordinate system.</summary>
public readonly struct Point2
{
    /// <summary>Creates a point. Input validity is checked by the comparison method.</summary>
    public Point2(double x, double y) { X = x; Y = y; }
    /// <summary>The x coordinate.</summary>
    public double X { get; }
    /// <summary>The y coordinate.</summary>
    public double Y { get; }
}
