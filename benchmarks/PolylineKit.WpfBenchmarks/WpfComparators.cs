using System.Windows;
using System.Windows.Media;
using PolylineKit;

namespace RegionCoverage;

/// <summary>
/// Prepared public WPF input, not a prepared overlay/index or a cached pair result.
/// The original arrays are already in the common experiment coordinate system.
/// </summary>
internal sealed class WpfRegion(Point2[] points, Geometry geometry, PathFigure? figure)
    : PreparedRegion(points, RegionPreparation.Bounds(points), geometry.GetArea(WpfGeometry.Tolerance, ToleranceType.Absolute))
{
    internal Geometry Geometry { get; } = geometry;
    internal PathFigure? Figure { get; } = figure;
}

internal static class WpfGeometry
{
    internal const double Tolerance = 1e-6;

    internal static PathFigure Figure(Point2[] points, bool positiveOrientation)
    {
        bool reverse = positiveOrientation && RegionPreparation.SignedArea(points) < 0;
        int Start(int i) => reverse ? points.Length - 1 - i : i;
        Point Convert(int i) => new(points[Start(i)].X, points[Start(i)].Y);
        var rest = new PointCollection(points.Length - 1);
        for (int i = 1; i < points.Length; i++) rest.Add(Convert(i));
        var result = new PathFigure(Convert(0), [new PolyLineSegment(rest, true)], true) { IsFilled = true };
        result.Freeze();
        return result;
    }

    internal static StreamGeometry Stream(Point2[] points)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, points[0].Y), true, true);
            for (int i = 1; i < points.Length; i++)
                context.LineTo(new Point(points[i].X, points[i].Y), true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>
/// For two simple polygons: EvenOdd of both figures is XOR; consistently oriented Nonzero
/// is union. This reuses the two-figure container, replacing inputs on every call. It is
/// equivalent to GeometryGroup concatenation, without constructing its temporary path.
/// GetArea invokes WPF's native area scanner; it does not construct Boolean result contours.
/// Scanner quantization and inclusion/exclusion can lose tiny intersections or yield a
/// negative estimate. Values are returned unmodified, never clamped or silently substituted.
/// One comparator instance is a single-threaded experiment workspace.
/// </summary>
internal sealed class WpfScannerComparator : CoverageComparator
{
    private readonly FillRule rule;
    private readonly PathGeometry pair;
    internal WpfScannerComparator(FillRule rule)
    {
        this.rule = rule;
        pair = new PathGeometry([new PathFigure(), new PathFigure()], rule, null);
    }

    internal override string Name => rule == FillRule.EvenOdd ? "wpf-evenodd-area" : "wpf-nonzero-area";

    internal override PreparedRegion Prepare(Point2[] points, bool zone)
    {
        // Common dataset certification guarantees simple contours. No holes are inferred.
        var figure = WpfGeometry.Figure(points, rule == FillRule.Nonzero);
        var geometry = new PathGeometry([figure], FillRule.Nonzero, null);
        geometry.Freeze();
        return new WpfRegion(points, geometry, figure);
    }

    protected override double Intersection(PreparedRegion zone, PreparedRegion query)
    {
        var a = (WpfRegion)zone;
        var b = (WpfRegion)query;
        pair.Figures[0] = a.Figure!;
        pair.Figures[1] = b.Figure!;
        double combinedArea = pair.GetArea(WpfGeometry.Tolerance, ToleranceType.Absolute);
        double remainder = a.Area + b.Area - combinedArea;
        return rule == FillRule.EvenOdd ? remainder * 0.5 : remainder;
    }
}

/// <summary>
/// Public contour-producing baseline with an explicit tolerance in BOTH operations.
/// CombinedGeometry.GetArea would use the default tolerance for its hidden Combine step.
/// Independent StreamGeometry inputs are frozen and serialized during preparation.
/// </summary>
internal sealed class WpfCombineComparator : CoverageComparator
{
    internal override string Name => "wpf-combine-explicit";

    internal override PreparedRegion Prepare(Point2[] points, bool zone) => new WpfRegion(points, WpfGeometry.Stream(points), null);

    protected override double Intersection(PreparedRegion zone, PreparedRegion query)
    {
        var a = (WpfRegion)zone;
        var b = (WpfRegion)query;
        return Geometry.Combine(a.Geometry, b.Geometry, GeometryCombineMode.Intersect, null,
            WpfGeometry.Tolerance, ToleranceType.Absolute).GetArea(WpfGeometry.Tolerance, ToleranceType.Absolute);
    }
}
