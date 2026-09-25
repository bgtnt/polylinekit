using System.Security.Cryptography;
using System.Text.Json;
using NetTopologySuite.Geometries;
using PolylineKit;

namespace DynamicIndexBenchmarks;

internal sealed record Workload(string Id, string Family, Point2[] Points);

internal static class Workloads
{
    private sealed record SourceRegion(string Id, string Name, double[][] Points);
    private sealed record SourceFile(int SchemaVersion, string Role, int SpatialReference,
        string CoordinateUnit, SourceRegion[] Regions);

    internal static Workload[] Load()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "data");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        SourceFile Read(string name, string role, int expectedCount)
        {
            byte[] raw = File.ReadAllBytes(Path.Combine(directory, name));
            var entry = manifest.RootElement.GetProperty("Files").GetProperty(name);
            Require(raw.Length == entry.GetProperty("Bytes").GetInt32() &&
                Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() == entry.GetProperty("Sha256").GetString(),
                "Frozen source hash differs: " + name);
            SourceFile file = JsonSerializer.Deserialize<SourceFile>(raw)
                ?? throw new InvalidDataException("Missing input: " + name);
            Require(file.SchemaVersion == 1 && file.Role == role && file.SpatialReference == 5070 &&
                file.CoordinateUnit == "metre", "Unexpected source coordinate contract: " + name);
            Require(file.Regions.Length == expectedCount &&
                file.Regions.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == expectedCount,
                "Unexpected or duplicated source population: " + name);
            Require(file.Regions.All(r => r.Points.Length >= 4 && r.Points.All(p =>
                p.Length == 2 && double.IsFinite(p[0]) && double.IsFinite(p[1]))), "Invalid source coordinates.");
            return file;
        }

        SourceFile counties = Read("zones.json", "zones", 98), districts = Read("queries.json", "queries", 11);
        double[][] all = counties.Regions.Concat(districts.Regions).SelectMany(r => r.Points).ToArray();
        double ox = all.Min(p => p[0]) * .5 + all.Max(p => p[0]) * .5;
        double oy = all.Min(p => p[1]) * .5 + all.Max(p => p[1]) * .5;
        var workloads = new List<Workload>(115);
        void AddSource(SourceFile source, string family)
        {
            foreach (SourceRegion region in source.Regions.OrderBy(r => r.Id, StringComparer.Ordinal))
                workloads.Add(new(family + "-" + region.Id, family,
                    region.Points.Select(p => new Point2(p[0] - ox, p[1] - oy)).ToArray()));
        }
        AddSource(counties, "census-county"); AddSource(districts, "census-district");
        foreach (int count in new[] { 128, 512, 2048 })
        {
            workloads.Add(new("radial-" + count, "radial", Radial(count)));
            workloads.Add(new("orthogonal-comb-" + count, "orthogonal-comb", Comb(count)));
        }
        foreach (Workload workload in workloads) Validate(workload.Id, workload.Points);
        return workloads.ToArray();
    }

    internal static void Validate(string id, IReadOnlyList<Point2> points)
    {
        Require(points.Count >= 4, "Too few vertices: " + id);
        var coordinates = new Coordinate[points.Count + 1];
        for (int i = 0; i < points.Count; i++)
        {
            Point2 p = points[i], q = points[(i + 1) % points.Count];
            Require(double.IsFinite(p.X) && double.IsFinite(p.Y) && (p.X != q.X || p.Y != q.Y),
                "Nonfinite or consecutive duplicate position: " + id);
            coordinates[i] = new(p.X, p.Y);
        }
        coordinates[^1] = new(points[0].X, points[0].Y);
        Polygon polygon = new GeometryFactory().CreatePolygon(coordinates);
        Require(polygon.IsValid && polygon.IsSimple && polygon.NumInteriorRings == 0 &&
            double.IsFinite(polygon.Area) && polygon.Area > 0, "Invalid single-ring workload: " + id);
    }

    private static Point2[] Radial(int count)
    {
        var result = new Point2[count];
        for (int i = 0; i < count; i++)
        {
            double theta = 2 * Math.PI * i / count;
            double r = 1000 + 150 * Math.Sin(7 * theta) + 50 * Math.Sin(31 * theta);
            result[i] = new(r * Math.Cos(theta), r * Math.Sin(theta));
        }
        return result;
    }

    private static Point2[] Comb(int count)
    {
        int teeth = (count - 4) / 4;
        Require(teeth > 0 && 4 * teeth + 4 == count, "Comb vertex count must be 4k+4.");
        double pitch = 1000d / (3 * teeth + 1);
        var result = new Point2[count];
        result[0] = new(0, 0); result[1] = new(1000, 0); result[2] = new(1000, 400);
        int next = 3;
        for (int i = teeth - 1; i >= 0; i--)
        {
            double right = (3 * i + 2) * pitch, left = (3 * i + 1.5) * pitch, y = 80 + 20 * (i % 2);
            result[next++] = new(right, 400); result[next++] = new(right, y);
            result[next++] = new(left, y); result[next++] = new(left, 400);
        }
        result[next++] = new(0, 400);
        Require(next == count, "Wrong comb vertex count.");
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
