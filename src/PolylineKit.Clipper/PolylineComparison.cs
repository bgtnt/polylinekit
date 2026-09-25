using Clipper2Lib;

namespace PolylineKit;

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
    public double? BoundsAreaRatio => UnionBoundsArea > 0 ? RawArea / UnionBoundsArea : null;
    /// <summary>Compatibility alias for BoundsAreaRatio; this is a rectangle occupancy ratio, not a similarity score.</summary>
    public double? NormalizedArea => BoundsAreaRatio;
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
        return new(p, q, Compare(p.Points, q.Points, kind, fillRule, decimalPrecision, includeContours, p.Bounds.Union(q.Bounds)));
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

    /// <summary>Measures the union and symmetric difference of two independently filled closed paths.</summary>
    /// <remarks>
    /// Closure is implicit and at least three vertices after consecutive duplicate removal are required.
    /// Both Boolean operations use the same cleaned pair, common origin, axis exchange, fill rule and
    /// decimal precision. Jaccard distance and IoU are undefined when the quantized union has zero area.
    /// This compares filled regions; it does not interpret open strokes as gesture similarity.
    /// </remarks>
    public static FilledRegionOverlapResult FilledRegionOverlap(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        PathFillRule fillRule = PathFillRule.NonZero, int decimalPrecision = 6)
    {
        PreparedPair pair = Prepare(first, second, true, fillRule, decimalPrecision);
        var subject = new PathsD { ConvertPath(pair.First, pair.Origin, pair.Transpose) };
        var clip = new PathsD { ConvertPath(pair.Second, pair.Origin, pair.Transpose) };
        // Keep independently interpreted regions in separate subject/clip sets. A single subject
        // set could cancel overlapping opposite windings (NonZero), or any overlap (EvenOdd).
        PathsD difference = Clipper.Xor(subject, clip, pair.ClipperRule, decimalPrecision);
        PathsD union = Clipper.Union(subject, clip, pair.ClipperRule, decimalPrecision);
        return new FilledRegionOverlapResult(Math.Abs(Clipper.Area(difference)), Math.Abs(Clipper.Area(union)),
            pair.Bounds, fillRule, decimalPrecision);
    }

    private static AreaComparisonResult Compare(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second,
        AreaComparisonKind kind, PathFillRule rule, int precision, bool includeContours, Bounds2D? knownBounds = null)
    {
        bool closed = kind == AreaComparisonKind.FilledRegionDifference;
        PreparedPair pair = Prepare(first, second, closed, rule, precision, knownBounds);
        Point2[] p = pair.First, q = pair.Second;
        Bounds2D bounds = pair.Bounds;
        Point2 origin = pair.Origin;
        bool transpose = pair.Transpose;
        FillRule clipperRule = pair.ClipperRule;
        PathsD resolved;
        if (closed)
        {
            // Clipper applies the fill rule separately to the subject and clip sets before XOR.
            resolved = Clipper.Xor(new PathsD { ConvertPath(p, origin, transpose) }, new PathsD { ConvertPath(q, origin, transpose) }, clipperRule, precision);
        }
        else
        {
            PathD combined = new(p.Length + q.Length);
            for (int i = 0; i < p.Length; i++) combined.Add(ConvertPoint(p[i]));
            for (int i = q.Length - 1; i >= 0; i--) combined.Add(ConvertPoint(q[i]));
            resolved = Clipper.Union(new PathsD { combined }, new PathsD(), clipperRule, precision);
        }
        // Signed contour summation subtracts holes; Abs on each contour would fill them incorrectly.
        double area = Math.Abs(Clipper.Area(resolved));
        IReadOnlyList<IReadOnlyList<Point2>> contours = Array.Empty<IReadOnlyList<Point2>>();
        if (includeContours)
        {
            var collected = new IReadOnlyList<Point2>[resolved.Count];
            int index = 0;
            foreach (PathD path in resolved)
            {
                Point2[] points = path.Select(v => transpose ? new Point2(v.y + origin.X, v.x + origin.Y) : new Point2(v.x + origin.X, v.y + origin.Y)).ToArray();
                if (transpose) Array.Reverse(points);
                collected[index++] = Array.AsReadOnly(points);
            }
            contours = Array.AsReadOnly(collected);
        }
        return new(area, bounds, kind, rule, precision, contours);

        PointD ConvertPoint(Point2 v) => transpose ? new PointD(v.Y - origin.Y, v.X - origin.X) : new PointD(v.X - origin.X, v.Y - origin.Y);
    }

    private static PreparedPair Prepare(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second, bool closed,
        PathFillRule rule, int precision, Bounds2D? knownBounds = null)
    {
        if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd) throw new ArgumentOutOfRangeException(nameof(rule));
        if (precision < -8 || precision > 8) throw new ArgumentOutOfRangeException(nameof(precision), "Decimal precision must be between -8 and 8.");
        Point2[] p = PathInput.CopyClean(first, nameof(first), closed), q = PathInput.CopyClean(second, nameof(second), closed);
        int minimum = closed ? 3 : 2;
        if (p.Length < minimum || q.Length < minimum) throw new ArgumentException($"Each path requires at least {minimum} vertices after duplicate removal.");
        Bounds2D bounds = knownBounds ?? Bounds2D.FromPoints(p).Union(Bounds2D.FromPoints(q));
        double scale = Math.Pow(10, precision);
        double extent = Math.Max(bounds.Width, bounds.Height);
        if (extent * scale > 1e14)
            throw new ArgumentOutOfRangeException(nameof(precision), "Coordinate extent exceeds the conservative clipping range; normalize or reduce decimal precision.");
        return new PreparedPair(p, q, bounds, rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
    }

    private static PathD ConvertPath(Point2[] path, Point2 origin, bool transpose)
    {
        var result = new PathD(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            Point2 point = path[i];
            result.Add(transpose ? new PointD(point.Y - origin.Y, point.X - origin.X) : new PointD(point.X - origin.X, point.Y - origin.Y));
        }
        return result;
    }

    private readonly struct PreparedPair
    {
        internal PreparedPair(Point2[] first, Point2[] second, Bounds2D bounds, FillRule rule)
        { First = first; Second = second; Bounds = bounds; ClipperRule = rule; }
        internal Point2[] First { get; }
        internal Point2[] Second { get; }
        internal Bounds2D Bounds { get; }
        internal Point2 Origin => Bounds.Center;
        // Keep the largest extent along Clipper's sweep direction. Both supported fill rules are
        // invariant under this common axis exchange. Diagnostic contours undo it.
        internal bool Transpose => Bounds.Width > Bounds.Height;
        internal FillRule ClipperRule { get; }
    }
}
