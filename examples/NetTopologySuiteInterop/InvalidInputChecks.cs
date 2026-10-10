using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using PolylineKit;

namespace NetTopologySuiteInterop;

internal static class InvalidInputChecks
{
    internal static object[] Run()
    {
        (string Name, Coordinate[] Ring, double NonZeroArea, double EvenOddArea, double NonZeroIntersection, double EvenOddIntersection)[] cases =
        [
            ("bow-tie", [new(0, 0), new(4, 4), new(0, 4), new(4, 0), new(0, 0)], 8, 8, 4.5, 4.5),
            ("annotation-loop", [new(0, 0), new(6, 0), new(6, 6), new(0, 6), new(0, 0),
                new(2, 2), new(4, 2), new(4, 4), new(2, 4), new(2, 2), new(0, 0)], 36, 32, 16, 12),
            ("self-touch", [new(0, 0), new(4, 0), new(4, 4), new(2, 2), new(0, 4), new(2, 2), new(0, 0)], 8, 8, 4.5, 4.5)
        ];
        Polygon clip = InteropChecks.Polygon(InteropChecks.Rectangle(1, 1, 5, 5));
        PreparedRegion clipRegion = PolygonInterop.Convert(clip);
        return cases.Select(input =>
        {
            Polygon polygon = InteropChecks.Polygon(input.Ring);
            // Deliberately bypass the valid-only converter. The winding field belongs to the raw walk.
            PreparedRegion raw = PreparedRegion.FromRings([input.Ring.Select(c => new Point2(c.X, c.Y)).ToArray()]);
            object[] core = InteropChecks.Rules.Select(rule =>
            {
                RegionOverlapResult result = RegionArea.Compare(raw, clipRegion, rule);
                InteropChecks.Require(double.IsFinite(result.FirstArea) && result.FirstArea >= 0 &&
                    double.IsFinite(result.IntersectionArea) && result.IntersectionArea >= 0, "Invalid-input observation produced an invalid metric.");
                double expectedArea = rule == PathFillRule.NonZero ? input.NonZeroArea : input.EvenOddArea;
                double expectedIntersection = rule == PathFillRule.NonZero ? input.NonZeroIntersection : input.EvenOddIntersection;
                InteropChecks.Require(result.FirstArea == expectedArea && result.IntersectionArea == expectedIntersection,
                    $"{input.Name}/{rule}: the analytically defined raw-winding area/intersection differs.");
                return (object)new { FillRule = rule.ToString(), Area = result.FirstArea,
                    result.IntersectionArea, result.UnionArea, result.IntersectionOverUnion };
            }).ToArray();
            return (object)new
            {
                input.Name,
                Input = input.Ring.Select(c => new[] { c.X, c.Y }).ToArray(),
                NtsIsValid = polygon.IsValid,
                NtsArea = Observe(() => polygon.Area),
                NtsIntersectionLegacy = Observe(() => polygon.Intersection(clip).Area),
                NtsIntersectionOverlayNGRobust = Observe(() => OverlayNGRobust.Overlay(polygon, clip, SpatialFunction.Intersection).Area),
                Core = core, CoreAnalyticChecks = 4,
                Interpretation = "Observation of this locked NTS version, not a claim that either operation defines the intended repair."
            };
        }).ToArray();
    }

    private static object Observe(Func<double> operation)
    {
        try { return new { Value = (double?)operation(), ExceptionType = (string?)null, Message = (string?)null }; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return new { Value = (double?)null, ExceptionType = error.GetType().FullName, error.Message }; }
    }
}
