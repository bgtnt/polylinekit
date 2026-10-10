using NetTopologySuite;
using NetTopologySuite.Geometries;
using PolylineKit;

namespace NetTopologySuiteInterop;

internal static class InteropChecks
{
    internal static readonly GeometryFactory Factory = new NtsGeometryServices(GeometryOverlay.Legacy).CreateGeometryFactory();
    internal static readonly PathFillRule[] Rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];

    internal static object Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; Require(condition, message); }
        void Equal(double actual, double expected, string message)
        { Check(actual == expected, $"{message}: {actual:R} vs {expected:R}"); }
        void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { Check(true, message); return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + message);
        }
        var directions = new List<object>();
        foreach (bool reverseShell in new[] { false, true })
        foreach (bool reverseHole in new[] { false, true })
        {
            Polygon polygon = Polygon(Rectangle(0, 0, 10, 10, reverseShell), Rectangle(2, 2, 6, 6, reverseHole));
            PreparedRegion region = PolygonInterop.Convert(polygon);
            Equal(polygon.Area, 84, "NTS shell/hole roles");
            Check(region.RingCount == 2 && region.VertexCount == 8, "The closing coordinate is removed by preparation.");
            double nonZero = RegionArea.FilledArea(region), evenOdd = RegionArea.FilledArea(region, PathFillRule.EvenOdd);
            Equal(nonZero, 84, "NonZero hole direction"); Equal(evenOdd, 84, "EvenOdd hole direction");
            var clip = PolygonInterop.Convert(Polygon(Rectangle(5, 0, 15, 10)));
            foreach (PathFillRule rule in Rules)
            {
                RegionOverlapResult overlap = RegionArea.Compare(region, clip, rule);
                Equal(overlap.IntersectionArea, 46, "Hole intersection");
                Equal(overlap.UnionArea, 138, "Hole union");
                Equal(overlap.IntersectionOverUnion!.Value, 1d / 3, "Hole IoU");
            }
            directions.Add(new { ReverseShell = reverseShell, ReverseHole = reverseHole, NonZeroArea = nonZero, EvenOddArea = evenOdd });
        }
        Polygon shellWithHole = Polygon(Rectangle(0, 0, 10, 10), Rectangle(2, 2, 6, 6));
        MultiPolygon multi = Factory.CreateMultiPolygon([shellWithHole, Polygon(Rectangle(3, 3, 5, 5)), Polygon(Rectangle(20, 0, 22, 2))]);
        Check(multi.IsValid, "Island and disconnected part must form a valid MultiPolygon.");
        foreach (PathFillRule rule in Rules)
            Equal(RegionArea.FilledArea(PolygonInterop.Convert(multi), rule), 92, "Island/disconnected hierarchy");

        foreach (Geometry empty in new Geometry[] { Factory.CreatePolygon(), Factory.CreateMultiPolygon() })
        {
            PreparedRegion region = PolygonInterop.Convert(empty);
            Check(region.RingCount == 0 && region.Bounds is null, "Empty polygons stay empty.");
            Equal(RegionArea.FilledArea(region), 0, "Empty area");
            Check(RegionArea.Compare(region, region).IntersectionOverUnion is null, "Two empty regions have undefined IoU.");
            Equal(RegionArea.Compare(region, PolygonInterop.Convert(shellWithHole)).IntersectionOverUnion!.Value, 0,
                "Empty/nonempty IoU");
        }
        var withEmptyPart = Factory.CreateMultiPolygon([Factory.CreatePolygon(), Polygon(Rectangle(20, 0, 22, 2))]);
        Equal(RegionArea.FilledArea(PolygonInterop.Convert(withEmptyPart)), 4, "Empty components in a MultiPolygon");
        Throws<ArgumentNullException>(() => PolygonInterop.Convert(null!), "Null input");
        foreach (Geometry other in new Geometry[]
            { Factory.CreatePoint(new Coordinate(0, 0)), Factory.CreateLineString([new(0, 0), new(1, 1)]), Factory.CreateGeometryCollection() })
            Throws<ArgumentException>(() => PolygonInterop.Convert(other), "Reject nonpolygon types, including empty collections");
        Polygon bowTie = Polygon([new(0, 0), new(4, 4), new(0, 4), new(4, 0), new(0, 0)]);
        Check(!bowTie.IsValid, "The bow tie must be invalid in NTS.");
        Throws<ArgumentException>(() => PolygonInterop.Convert(bowTie), "No silent repair");
        MultiPolygon invalidMulti = Factory.CreateMultiPolygon([Polygon(Rectangle(0, 0, 10, 10)), Polygon(Rectangle(5, 0, 15, 10))]);
        Check(!invalidMulti.IsValid, "Overlapping NTS MultiPolygon parts are invalid.");
        Throws<ArgumentException>(() => PolygonInterop.Convert(invalidMulti), "Validate the complete hierarchy");

        // SRID is metadata here. XY is copied exactly, never projected or normalized.
        Polygon srid = Polygon(Rectangle(1000, 2000, 1004, 2003));
        srid.SRID = 5070;
        PreparedRegion copied = PolygonInterop.Convert(srid);
        Equal(RegionArea.FilledArea(copied), 12, "Planar area in the input units");
        Check(copied.Bounds!.Value.MinX == 1000 && copied.Bounds.Value.MinY == 2000 && srid.SRID == 5070,
            "Coordinates and the original SRID are unchanged.");
        return new { Checks = checks, FourDirectionResults = directions, MultiPolygonArea = multi.Area,
            Scope = "Valid-only converter; roles preserved; no geometry repair, CRS conversion, normalization or alignment." };
    }

    internal static Polygon Polygon(Coordinate[] shell, params Coordinate[][] holes) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(shell), holes.Select(Factory.CreateLinearRing).ToArray());
    internal static Coordinate[] Rectangle(double x0, double y0, double x1, double y1, bool reversed = false)
    {
        Coordinate[] coordinates = [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1), new(x0, y0)];
        if (reversed) Array.Reverse(coordinates);
        return coordinates;
    }
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
