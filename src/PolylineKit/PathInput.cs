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
    internal static Point2[] CopyClean(IReadOnlyList<Point2> points, string name, bool closed = false)
    {
        if (points is null) throw new ArgumentNullException(name);
        if (points.Count == 0) throw new ArgumentException("The path must not be empty.", name);
        var result = new List<Point2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            Validate(points[i], name);
            if (result.Count == 0 || !Same(result[result.Count - 1], points[i])) result.Add(points[i]);
        }
        if (closed && result.Count > 1 && Same(result[0], result[result.Count - 1])) result.RemoveAt(result.Count - 1);
        return result.ToArray();
    }
}
