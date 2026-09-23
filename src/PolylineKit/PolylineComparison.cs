using Clipper2Lib;

namespace PolylineKit;

/// <summary>Which portions of a closed walk count as filled. Neither rule counts traversal multiplicity.</summary>
public enum PathFillRule
{
    /// <summary>Count points with nonzero winding once.</summary>
    NonZero,
    /// <summary>Count points with odd winding once.</summary>
    EvenOdd
}

/// <summary>The explicitly selected meaning of an area comparison.</summary>
public enum AreaComparisonKind
{
    /// <summary>Fill the closed walk made by first, an end connector, reversed second and a start connector.</summary>
    EndpointBridged,
    /// <summary>Area belonging to exactly one of the two independently filled closed paths.</summary>
    FilledRegionDifference
}

/// <summary>Area, union-rectangle normalization and optional resolved boundary contours.</summary>
public sealed class AreaComparisonResult
{
    internal AreaComparisonResult(double area, Bounds2D bounds, AreaComparisonKind kind, PathFillRule rule,
        int precision, IReadOnlyList<IReadOnlyList<Point2>> contours)
    {
        RawArea = area; UnionBounds = bounds; Kind = kind; FillRule = rule; DecimalPrecision = precision; Contours = contours;
    }
    /// <summary>Nonnegative area, in squared coordinates, after quantized polygon resolution.</summary>
    public double RawArea { get; }
    /// <summary>Bounding rectangle of the original pair, in the supplied coordinates.</summary>
    public Bounds2D UnionBounds { get; }
    /// <summary>Area of UnionBounds; not the area of the union of two filled shapes.</summary>
    public double UnionBoundsArea => UnionBounds.Area;
    /// <summary>RawArea / UnionBoundsArea; null when rectangle area is zero. No clamping hides rounding.</summary>
    public double? NormalizedArea => UnionBoundsArea > 0 ? RawArea / UnionBoundsArea : null;
    /// <summary>The operation that produced this result.</summary>
    public AreaComparisonKind Kind { get; }
    /// <summary>The chosen interpretation of self intersections and repeats.</summary>
    public PathFillRule FillRule { get; }
    /// <summary>The decimal precision used by Clipper2, after a common origin shift.</summary>
    public int DecimalPrecision { get; }
    /// <summary>Optional resolved contours in input coordinates. Outer contours have positive signed area; holes negative. Empty unless requested.</summary>
    public IReadOnlyList<IReadOnlyList<Point2>> Contours { get; }
}

/// <summary>Area comparisons of arbitrary planar paths with explicitly named fill semantics.</summary>
public static class PolylineComparison
{
    /// <summary>Independently normalizes both paths into a centered unit square and applies the explicitly selected area comparison.</summary>
    /// <remarks>This removes position and uniform size (or aspect ratio too with Stretch), but does not search for rotation. RawArea is then measured in normalized coordinates.</remarks>
    public static NormalizedComparisonResult CompareNormalized(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        AreaComparisonKind kind, BoundsScaling scaling = BoundsScaling.Uniform, PathFillRule fillRule = PathFillRule.NonZero,
        int decimalPrecision = 6, bool includeContours = false)
    {
        if (kind != AreaComparisonKind.EndpointBridged && kind != AreaComparisonKind.FilledRegionDifference)
            throw new ArgumentOutOfRangeException(nameof(kind));
        NormalizationResult p = PolylineNormalization.ToUnitBounds(first, scaling), q = PolylineNormalization.ToUnitBounds(second, scaling);
        return new(p, q, Compare(p.Points, q.Points, kind, fillRule, decimalPrecision, includeContours));
    }

    /// <summary>Fills the closed walk first + reverse(second), joining their respective ends with straight segments.</summary>
    /// <remarks>
    /// Accepts vertical segments, backtracking and loops. Input traversal order matters. At least two
    /// distinct consecutive points are required in each path. A zero result does not imply equal strokes.
    /// This is endpoint-bridged fill area, not general unsigned split-area or homotopy area.
    /// NonZero and EvenOdd agree with the graph integral when both paths satisfy its contract, within quantization error.
    /// </remarks>
    public static AreaComparisonResult EndpointBridgedArea(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        PathFillRule fillRule = PathFillRule.NonZero, int decimalPrecision = 6, bool includeContours = false) =>
        Compare(first, second, AreaComparisonKind.EndpointBridged, fillRule, decimalPrecision, includeContours);

    /// <summary>Computes the symmetric-difference area of two filled closed paths.</summary>
    /// <remarks>
    /// Closure from last to first is implicit; a repeated closing point is optional. At least three
    /// vertices after consecutive duplicate removal are required. The fill rule applies to each input
    /// independently. Starting vertex and traversal reversal do not change the filled region.
    /// This measures filled regions, not stroke order, traveled distance or correspondence.
    /// </remarks>
    public static AreaComparisonResult FilledRegionDifference(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        PathFillRule fillRule = PathFillRule.NonZero, int decimalPrecision = 6, bool includeContours = false) =>
        Compare(first, second, AreaComparisonKind.FilledRegionDifference, fillRule, decimalPrecision, includeContours);

    private static AreaComparisonResult Compare(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        AreaComparisonKind kind, PathFillRule rule, int precision, bool includeContours)
    {
        if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd) throw new ArgumentOutOfRangeException(nameof(rule));
        if (precision < -8 || precision > 8) throw new ArgumentOutOfRangeException(nameof(precision), "Decimal precision must be between -8 and 8.");
        bool closed = kind == AreaComparisonKind.FilledRegionDifference;
        Point2[] p = PathInput.CopyClean(first, nameof(first), closed), q = PathInput.CopyClean(second, nameof(second), closed);
        int minimum = closed ? 3 : 2;
        if (p.Length < minimum || q.Length < minimum) throw new ArgumentException($"Each path requires at least {minimum} vertices after duplicate removal.");
        Bounds2D bounds = Bounds2D.FromPoints(p).Union(Bounds2D.FromPoints(q));
        Point2 origin = bounds.Center;
        // Keep the largest extent along Clipper's sweep direction. Both supported fill rules are
        // invariant under this common axis exchange. Undo it for diagnostic contours.
        bool transpose = bounds.Width > bounds.Height;
        double scale = Math.Pow(10, precision);
        double extent = Math.Max(bounds.Width, bounds.Height);
        if (extent * scale > 1e14)
            throw new ArgumentOutOfRangeException(nameof(precision), "Coordinate extent exceeds the conservative clipping range; normalize or reduce decimal precision.");
        FillRule clipperRule = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
        PathsD resolved;
        if (closed)
        {
            // Clipper applies the fill rule separately to the subject and clip sets before XOR.
            resolved = Clipper.Xor(new PathsD { Convert(p) }, new PathsD { Convert(q) }, clipperRule, precision);
        }
        else
        {
            PathD combined = Convert(p), reverse = Convert(q);
            for (int i = reverse.Count - 1; i >= 0; i--) combined.Add(reverse[i]);
            resolved = Clipper.Union(new PathsD { combined }, new PathsD(), clipperRule, precision);
        }
        // Signed contour summation subtracts holes; Abs on each contour would fill them incorrectly.
        double area = Math.Abs(Clipper.Area(resolved));
        var contours = new List<IReadOnlyList<Point2>>(includeContours ? resolved.Count : 0);
        if (includeContours)
            foreach (PathD path in resolved)
            {
                Point2[] points = path.Select(v => transpose ? new Point2(v.y + origin.X, v.x + origin.Y) : new Point2(v.x + origin.X, v.y + origin.Y)).ToArray();
                if (transpose) Array.Reverse(points);
                contours.Add(Array.AsReadOnly(points));
            }
        return new(area, bounds, kind, rule, precision, contours.AsReadOnly());

        PathD Convert(Point2[] path) => new(path.Select(v => transpose ? new PointD(v.Y - origin.Y, v.X - origin.X) : new PointD(v.X - origin.X, v.Y - origin.Y)));
    }
}
