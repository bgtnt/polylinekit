namespace PolylineKit;

internal static class PathInput
{
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static void Validate(Point2 point, string name)
    {
        if (!Finite(point.X) || !Finite(point.Y) || Math.Abs(point.X) > 1e100 || Math.Abs(point.Y) > 1e100)
            throw new ArgumentException("Coordinates must be finite with magnitude at most 1e100.", name);
    }
    internal static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    internal static Point2[] CopyValidated(IReadOnlyList<Point2> points, string name)
    {
        if (points is null) throw new ArgumentNullException(name);
        if (points.Count == 0) throw new ArgumentException("The path must not be empty.", name);
        var result = new Point2[points.Count];
        for (int i = 0; i < result.Length; i++)
        {
            Point2 p = points[i]; Validate(p, name); result[i] = p;
        }
        return result;
    }
    internal static Point2[] CopyClean(IReadOnlyList<Point2> points, string name, bool closed = false)
    {
        if (points is null) throw new ArgumentNullException(name);
        if (points.Count == 0) throw new ArgumentException("The path must not be empty.", name);
        var result = new Point2[points.Count];
        int count = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Point2 p = points[i]; Validate(p, name);
            if (count == 0 || !Same(result[count - 1], p)) result[count++] = p;
        }
        if (closed && count > 1 && Same(result[0], result[count - 1])) count--;
        if (count != result.Length) Array.Resize(ref result, count);
        return result;
    }
}
