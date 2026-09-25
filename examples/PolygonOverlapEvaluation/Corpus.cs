using System.Security.Cryptography;
using System.Text.Json;
using NetTopologySuite.Geometries;

namespace PolygonOverlapEvaluation;

internal sealed record InputRow(int SourceIndex, string ImageId, int BuildingId,
    double? Confidence, bool IsEmpty, double[][][][] Polygons);
internal sealed record ExpectedRow(int SourceIndex, string ImageId, int BuildingId, double IoU);
internal sealed record Corpus(int SchemaVersion, string SourceCommit, InputRow[] Predictions,
    InputRow[] Truth, ExpectedRow[] Expected = null!)
{
    internal const string SolarisCommit = "5315390942e05e919555088361bd3df42d4f5a18";
    internal const string SolarisFixtureHash = "883b7d327f1e082a8a34bbe065a40d8fb5a89bddd5d5c1014be5fbcf62fd4b20";
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static Corpus Load(string path)
    {
        using var stream = File.OpenRead(path); // Included in the full evaluator measurement (warm OS cache).
        Corpus data = JsonSerializer.Deserialize<Corpus>(stream) ?? throw new InvalidDataException("Empty input document.");
        if (data.SchemaVersion != 1 || data.Predictions is null || data.Truth is null)
            throw new InvalidDataException("Unexpected fixture schema or missing row arrays.");
        data = data with { Expected = data.Expected ?? [] };
        ValidateRows(data.Predictions, true); ValidateRows(data.Truth, false);
        if (data.Expected.Length > 0 && (data.Expected.Any(x => x is null) || data.Expected.Length != data.Truth.Length ||
            data.Expected.Select(x => (x.ImageId, x.BuildingId)).Distinct().Count() != data.Expected.Length ||
            !data.Expected.Select(x => (x.ImageId, x.BuildingId)).ToHashSet().SetEquals(data.Truth.Select(x => (x.ImageId, x.BuildingId))) ||
            data.Expected.Any(x => !double.IsFinite(x.IoU))))
            throw new InvalidDataException("Reference keys/count/values differ from the truth table.");
        return data;
    }

    private static void ValidateRows(InputRow[] rows, bool predictions)
    {
        if (rows.Any(x => x is null) || rows.Select(x => (x.ImageId, x.BuildingId)).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Duplicate image/building key.");
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.SourceIndex != i || string.IsNullOrWhiteSpace(row.ImageId) ||
                (predictions && (row.Confidence is null || !double.IsFinite(row.Confidence.Value))))
                throw new InvalidDataException("Invalid row order, key or confidence.");
            if (row.Polygons is null || row.IsEmpty != (row.Polygons.Length == 0)) throw new InvalidDataException("Empty sentinel disagrees with geometry.");
            foreach (var polygon in row.Polygons)
            {
                if (polygon is null || polygon.Length == 0) throw new InvalidDataException("Polygon needs an exterior ring.");
                foreach (var ring in polygon)
                {
                    if (ring is null || ring.Length < 4 || ring.Any(p => p is null || p.Length != 2 || !double.IsFinite(p[0]) || !double.IsFinite(p[1])) ||
                        !ring[0].SequenceEqual(ring[^1])) throw new InvalidDataException("Expected finite, explicitly closed XY ring.");
                }
            }
        }
    }

    internal static Geometry Geometry(InputRow row)
    {
        var factory = GeometryFactory.Default;
        if (row.IsEmpty) return factory.CreatePolygon();
        var polygons = row.Polygons.Select(p => factory.CreatePolygon(Ring(p[0]), p.Skip(1).Select(Ring).ToArray())).ToArray();
        return polygons.Length == 1 ? polygons[0] : factory.CreateMultiPolygon(polygons);
        LinearRing Ring(double[][] points) => factory.CreateLinearRing(points.Select(p => new Coordinate(p[0], p[1])).ToArray());
    }

    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");
    }
}
