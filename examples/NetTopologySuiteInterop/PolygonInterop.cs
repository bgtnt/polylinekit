using NetTopologySuite.Geometries;
using PolylineKit;

namespace NetTopologySuiteInterop;

/// <summary>Copies a valid NTS polygon hierarchy into a prepared region without repairing it.</summary>
public static class PolygonInterop
{
    /// <summary>Preserves shell/hole roles and XY coordinates; does not transform the SRID.</summary>
    public static PreparedRegion Convert(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry is not Polygon && geometry is not MultiPolygon)
            throw new ArgumentException("Expected Polygon or MultiPolygon.", nameof(geometry));
        if (!geometry.IsValid)
            throw new ArgumentException("The polygon must be valid; this converter does not repair geometry.", nameof(geometry));
        if (geometry.IsEmpty) return PreparedRegion.FromPolygons([]);
        if (geometry is Polygon polygon)
            return PreparedRegion.FromPolygon(Ring(polygon.ExteriorRing), Holes(polygon));
        var polygons = Enumerable.Range(0, geometry.NumGeometries)
            .Select(i => (Polygon)geometry.GetGeometryN(i)).Where(p => !p.IsEmpty)
            .Select(p => (Exterior: Ring(p.ExteriorRing), Holes: Holes(p))).ToArray();
        return PreparedRegion.FromPolygons(polygons);
    }

    private static IReadOnlyList<Point2> Ring(LineString ring) =>
        ring.Coordinates.Select(c => new Point2(c.X, c.Y)).ToArray();
    private static IReadOnlyList<IReadOnlyList<Point2>>? Holes(Polygon polygon) =>
        Enumerable.Range(0, polygon.NumInteriorRings).Select(i => Ring(polygon.GetInteriorRingN(i))).ToArray();
}
