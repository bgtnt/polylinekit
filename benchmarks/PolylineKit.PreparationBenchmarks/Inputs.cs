using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.PreparationBenchmarks;

internal sealed record PolygonInput(IReadOnlyList<Point2> Exterior, IReadOnlyList<IReadOnlyList<Point2>>? Holes);

internal sealed class RegionInput(string id, PolygonInput[] polygons)
{
    internal string Id { get; } = id;
    internal PolygonInput[] Polygons { get; } = polygons;
    internal IReadOnlyList<Point2>[] Rings { get; } = polygons
        .SelectMany(p => new[] { p.Exterior }.Concat(p.Holes ?? [])).ToArray();
    internal (IReadOnlyList<Point2> Exterior, IReadOnlyList<IReadOnlyList<Point2>>? Holes)[] Roles { get; } = polygons
        .Select(p => (p.Exterior, p.Holes)).ToArray();
    internal int VertexCount => Rings.Sum(r => r.Count);
    internal int HoleCount => Polygons.Sum(p => p.Holes?.Count ?? 0);

    internal PreparedRegion Prepare(string method) => method switch
    {
        "FromRings" => PreparedRegion.FromRings(Rings),
        "FromPolygon" when Polygons.Length == 1 => PreparedRegion.FromPolygon(Polygons[0].Exterior, Polygons[0].Holes),
        "FromPolygons" => PreparedRegion.FromPolygons(Roles),
        _ => throw new ArgumentException("Unsupported factory: " + method)
    };

    internal string[] Methods => Polygons.Length == 1
        ? ["FromRings", "FromPolygon", "FromPolygons"] : ["FromRings", "FromPolygons"];

    internal RegionInput Translate(string id, double dx, double dy) => new(id, Polygons.Select(p => new PolygonInput(
        Move(p.Exterior, dx, dy), p.Holes?.Select(h => (IReadOnlyList<Point2>)Move(h, dx, dy)).ToArray())).ToArray());
    private static Point2[] Move(IReadOnlyList<Point2> ring, double dx, double dy) => ring.Select(p => new Point2(p.X + dx, p.Y + dy)).ToArray();

    // Binary coordinates, ring roles, order and IDs are hashed without a JSON formatting dependency.
    internal static string Hash(IEnumerable<RegionInput> inputs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (RegionInput input in inputs)
        {
            writer.Write(input.Id); writer.Write(input.Polygons.Length);
            foreach (PolygonInput polygon in input.Polygons)
            {
                Write(polygon.Exterior); writer.Write(polygon.Holes?.Count ?? 0);
                foreach (IReadOnlyList<Point2> hole in polygon.Holes ?? []) Write(hole);
            }
        }
        writer.Flush();
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
        void Write(IReadOnlyList<Point2> ring)
        {
            writer.Write(ring.Count);
            foreach (Point2 p in ring) { writer.Write(p.X); writer.Write(p.Y); }
        }
    }
}

internal sealed record SourceFile(string File, int Bytes, string Sha256);
internal sealed record Corpus(RegionInput[] Synthetic, RegionInput[] Counties, RegionInput[] Districts, SourceFile[] Sources)
{
    private const string CountiesHash = "4e114c8d8168e782ac185cd1f1f7bc7c4a1772e218e9502d00b204837bc5441b";
    private const string DistrictsHash = "237b71fa2dd7cc73e4e46a0b624ab0256051bf4200e4920ecc805720f6a0886f";

    internal static Corpus Load(string repositoryRoot)
    {
        var sources = new List<SourceFile>();
        RegionInput[] Read(string name, string expected)
        {
            string relative = "examples/RegionCoverage/data/source/" + name;
            byte[] bytes = File.ReadAllBytes(Path.Combine(repositoryRoot, relative));
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Program.Require(hash == expected, "Frozen source hash changed: " + relative);
            sources.Add(new(relative, bytes.Length, hash));
            using JsonDocument document = JsonDocument.Parse(bytes);
            return document.RootElement.GetProperty("features").EnumerateArray().Select(feature =>
            {
                string id = feature.GetProperty("properties").GetProperty("GEOID").GetString()!;
                JsonElement geometry = feature.GetProperty("geometry");
                JsonElement coordinates = geometry.GetProperty("coordinates");
                PolygonInput[] polygons = geometry.GetProperty("type").GetString() switch
                {
                    "Polygon" => [ReadPolygon(coordinates)],
                    "MultiPolygon" => coordinates.EnumerateArray().Select(ReadPolygon).ToArray(),
                    _ => throw new InvalidDataException("Only Polygon and MultiPolygon admitted: " + id)
                };
                return new RegionInput(id, polygons);
            }).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        }
        RegionInput[] counties = Read("nc-counties.geojson", CountiesHash);
        RegionInput[] districts = Read("nc-districts.geojson", DistrictsHash);
        Program.Require(counties.Length == 100 && districts.Length == 14, "Complete Census population changed.");
        Program.Require(counties.Sum(r => r.Rings.Length) == 104 && districts.Sum(r => r.Rings.Length) == 19 &&
            counties.Sum(r => r.HoleCount) == 0 && districts.Sum(r => r.HoleCount) == 1, "Complete Census ring inventory changed.");
        return new(SyntheticCases(), counties, districts, sources.ToArray());
    }

    private static PolygonInput ReadPolygon(JsonElement coordinates)
    {
        var rings = coordinates.EnumerateArray().Select((ring, index) =>
        {
            Point2[] points = ring.EnumerateArray().Select(p => new Point2(p[0].GetDouble(), p[1].GetDouble())).ToArray();
            if (points.Length > 1 && points[0].X == points[^1].X && points[0].Y == points[^1].Y) Array.Resize(ref points, points.Length - 1);
            Program.Require(points.Length >= 3 && points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)), "Invalid source coordinates.");
            // This independent untimed orientation step gives every factory the identical role-oriented input.
            // With ordinary valid Census rings this small local shoelace sum has a clear nonzero sign.
            double ox = points[0].X, oy = points[0].Y, signed = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Point2 a = points[i], b = points[(i + 1) % points.Length];
                signed += (a.X - ox) * (b.Y - oy) - (b.X - ox) * (a.Y - oy);
            }
            Program.Require(double.IsFinite(signed) && signed != 0, "Ambiguous Census ring orientation.");
            if (index == 0 ? signed < 0 : signed > 0) Array.Reverse(points);
            return (IReadOnlyList<Point2>)points;
        }).ToArray();
        return new(rings[0], rings.Length == 1 ? null : rings.Skip(1).ToArray());
    }

    private static RegionInput[] SyntheticCases()
    {
        var cases = new List<RegionInput>();
        foreach (int n in new[] { 16, 64, 256, 1024 })
        {
            Point2[] simple = Radial(n, 100, alternating: true);
            cases.Add(new($"simple-dense-{n}", [new(simple, null)]));
            Point2[] circle = Radial(n, 100);
            cases.Add(new($"crossing-walk-{n}", [new(Enumerable.Range(0, n).Select(i => circle[i * 5 % n]).ToArray(), null)]));
            cases.Add(new($"four-components-{n}", Enumerable.Range(0, 4).Select(component =>
                new PolygonInput(Radial(n / 4, 20, offsetX: 60 * component), null)).ToArray()));
            Point2[] hole = Radial(n / 4, 25); Array.Reverse(hole);
            cases.Add(new($"shell-hole-{n}", [new(Radial(n * 3 / 4, 100), new IReadOnlyList<Point2>[] { hole })]));
        }
        return cases.ToArray();
    }

    private static Point2[] Radial(int n, double radius, bool alternating = false, double offsetX = 0) =>
        Enumerable.Range(0, n).Select(i =>
        {
            double angle = 2 * Math.PI * i / n, r = alternating && i % 2 != 0 ? radius * .95 : radius;
            return new Point2(offsetX + r * Math.Cos(angle), r * Math.Sin(angle));
        }).ToArray();
}
