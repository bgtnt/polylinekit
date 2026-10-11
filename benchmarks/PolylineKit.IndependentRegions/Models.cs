using NetTopologySuite.Geometries;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

internal readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
{
    internal bool Overlaps(Box other) => MaxX >= other.MinX && other.MaxX >= MinX &&
        MaxY >= other.MinY && other.MaxY >= MinY;

    internal static Box Of(IEnumerable<Point2> points)
    {
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        foreach (Point2 point in points)
        {
            minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
        }
        return new(minX, minY, maxX, maxY);
    }
}

internal sealed record RawPolygon(Point2[] Shell, Point2[][] Holes);
internal sealed record RawFeature(string Id, string Name, RawPolygon[] Polygons)
{
    internal Point2[][] Rings { get; } = Polygons.SelectMany(p => new[] { p.Shell }.Concat(p.Holes)).ToArray();
    internal Box Bounds { get; } = Box.Of(Polygons.SelectMany(p => new[] { p.Shell }.Concat(p.Holes)).SelectMany(r => r));
    internal int VertexCount => Rings.Sum(r => r.Length);
}

internal sealed record InputIssue(string Role, string Id, string Stage, string Reason);
internal sealed record Inventory(int SourceFeatures, int EligibleFeatures, int Components, int Rings, int Holes, int SourceVertices,
    int EligibleComponents, int EligibleRings, int EligibleHoles, int EligibleSourceVertices);
internal sealed record Layer(string Role, RawFeature[] Source, RawFeature[] Eligible, Inventory Inventory);
internal sealed record InputHash(string File, int Bytes, string Sha256);
internal sealed record FrozenCorpus(string Root, Layer Zones, Layer Queries, InputHash[] Sources, InputHash[] Derived,
    string ManifestSha256, string ProtocolSha256, int SourcePairs, int SourceCandidates, int EligiblePairs, int EligibleCandidates,
    InputIssue[] InvalidInputs, double MaximumQuantizationDisplacement);
internal readonly record struct MetricValue(double First, double Second, double Intersection, double Union, double Xor, double? Iou)
{
    internal static MetricValue From(RegionOverlapResult value) => new(value.FirstArea, value.SecondArea, value.IntersectionArea,
        value.UnionArea, value.SymmetricDifferenceArea, value.IntersectionOverUnion);
    internal static MetricValue Of(double first, double second, double intersection, double union, double xor) =>
        new(first, second, intersection, union, xor, union > 0 ? intersection / union : null);
}

internal static class Contracts
{
    internal const double AreaBudget = 1;
    internal const double FractionBudget = 1e-8;
    internal const double ClipperScale = 1e6;
    internal static string PublishedCoreSha256 => PublishedRelease.Current.CoreNet10Sha256;
    internal static readonly GeometryFactory Factory = new(new PrecisionModel(), 3067);
    internal static readonly PathFillRule[] Rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static Geometry Geometry(RawFeature feature)
    {
        Polygon[] polygons = feature.Polygons.Select(p => Factory.CreatePolygon(Ring(p.Shell), p.Holes.Select(Ring).ToArray())).ToArray();
        return polygons.Length == 1 ? polygons[0] : Factory.CreateMultiPolygon(polygons);
    }

    private static LinearRing Ring(Point2[] points)
    {
        bool closed = points.Length > 0 && points[0].X == points[^1].X && points[0].Y == points[^1].Y;
        Coordinate[] coordinates = new Coordinate[points.Length + (closed ? 0 : 1)];
        for (int i = 0; i < points.Length; i++) coordinates[i] = new(points[i].X, points[i].Y);
        if (!closed) coordinates[^1] = new(points[0].X, points[0].Y);
        return Factory.CreateLinearRing(coordinates);
    }
}
