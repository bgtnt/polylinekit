namespace PolylineKit;

// Keep this deliberately small. The broader library owns copying, normalization
// and sampling helpers; the boundary-validation contract is checked across both.
internal static class WindingInput
{
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static void Validate(Point2 point, string name)
    {
        if (!Finite(point.X) || !Finite(point.Y) || Math.Abs(point.X) > 1e100 || Math.Abs(point.Y) > 1e100)
            throw new ArgumentException("Coordinates must be finite with magnitude at most 1e100.", name);
    }
    internal static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
}
