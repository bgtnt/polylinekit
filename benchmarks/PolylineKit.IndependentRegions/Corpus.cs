using System.Security.Cryptography;
using System.Text.Json;
using NetTopologySuite.Operation.Valid;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

internal static class Corpus
{
    internal static FrozenCorpus Load(string directory)
    {
        string root = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(root, "manifest.json");
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
        JsonElement description = manifest.RootElement;
        Contracts.Require(description.GetProperty("SchemaVersion").GetInt32() == 1, "Unknown manifest schema.");
        string protocolHash = description.GetProperty("ProtocolSha256").GetString()!;
        Contracts.Require(Hash(File.ReadAllBytes(Path.Combine(root, "PROTOCOL.md"))) == protocolHash,
            "The frozen protocol hash differs.");
        var sources = new List<InputHash>();
        foreach (JsonElement source in description.GetProperty("SourceFiles").EnumerateArray())
            sources.Add(Verify(root, source.GetProperty("File").GetString()!, source.GetProperty("Sha256").GetString()!,
                source.GetProperty("Bytes").GetInt32()));
        Contracts.Require(sources.Count > 0 && sources.Select(s => s.File).Distinct(StringComparer.Ordinal).Count() == sources.Count,
            "Source manifest is empty or contains duplicate files.");
        var derived = new List<InputHash>();
        var layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
        var invalid = new List<InputIssue>();
        double maximumDisplacement = 0;
        foreach (JsonElement layer in description.GetProperty("Layers").EnumerateArray())
        {
            string role = layer.GetProperty("Role").GetString()!;
            Contracts.Require(role is "zones" or "queries" && !layers.ContainsKey(role), "Unexpected or repeated layer role.");
            string file = layer.GetProperty("DerivedFile").GetString()!;
            InputHash identity = Verify(root, file, layer.GetProperty("DerivedSha256").GetString()!, null);
            derived.Add(identity);
            RawFeature[] raw = Read(Path.Combine(root, file), role);
            Contracts.Require(raw.Length == layer.GetProperty("SourceFeatures").GetInt32(), "Source feature count differs: " + role);
            Contracts.Require(raw.Sum(r => r.Polygons.Length) == layer.GetProperty("Components").GetInt32() &&
                raw.Sum(r => r.Rings.Length) == layer.GetProperty("Rings").GetInt32() &&
                raw.Sum(r => r.Polygons.Sum(p => p.Holes.Length)) == layer.GetProperty("Holes").GetInt32() &&
                raw.Sum(r => r.VertexCount) == layer.GetProperty("Vertices").GetInt32(), "Source geometry inventory differs: " + role);
            var eligible = new List<RawFeature>();
            foreach (RawFeature feature in raw)
            {
                try
                {
                    var geometry = Contracts.Geometry(feature);
                    var validity = new IsValidOp(geometry);
                    if (!validity.IsValid)
                    {
                        invalid.Add(new(role, feature.Id, "original-NTS", validity.ValidationError?.ToString() ?? "Invalid geometry"));
                        continue;
                    }
                    if (geometry.IsEmpty || !double.IsFinite(geometry.Area) || geometry.Area <= 0)
                    {
                        invalid.Add(new(role, feature.Id, "original-NTS", "Empty, nonfinite or nonpositive polygon area"));
                        continue;
                    }
                    eligible.Add(feature);
                    RawFeature quantized = Quantized(feature, out double displacement);
                    maximumDisplacement = Math.Max(maximumDisplacement, displacement);
                    var quantizedGeometry = Contracts.Geometry(quantized);
                    var quantizedValidity = new IsValidOp(quantizedGeometry);
                    if (!quantizedValidity.IsValid || quantizedGeometry.IsEmpty || quantizedGeometry.Area <= 0)
                        invalid.Add(new(role, feature.Id, "quantized-NTS", quantizedValidity.ValidationError?.ToString() ??
                            "Quantization produced empty or nonpositive geometry"));
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
                {
                    // Construction failure remains an identified source defect, never a repaired shape.
                    invalid.Add(new(role, feature.Id, eligible.Contains(feature) ? "quantized-NTS" : "original-NTS", error.Message));
                }
            }
            RawFeature[] selected = eligible.ToArray();
            layers.Add(role, new(role, raw, selected, new(raw.Length, selected.Length, raw.Sum(r => r.Polygons.Length),
                raw.Sum(r => r.Rings.Length), raw.Sum(r => r.Polygons.Sum(p => p.Holes.Length)), raw.Sum(r => r.VertexCount),
                selected.Sum(r => r.Polygons.Length), selected.Sum(r => r.Rings.Length),
                selected.Sum(r => r.Polygons.Sum(p => p.Holes.Length)), selected.Sum(r => r.VertexCount))));
        }
        Contracts.Require(layers.Count == 2, "Expected both complete input layers.");
        Layer zones = layers["zones"], queries = layers["queries"];
        Contracts.Require(zones.Source.Length == 34 && queries.Source.Length == 1095, "The full Helsinki source population differs.");
        int sourcePairs = checked(zones.Source.Length * queries.Source.Length);
        int sourceCandidates = Candidates(zones.Source, queries.Source);
        JsonElement inventory = description.GetProperty("PairInventory");
        Contracts.Require(sourcePairs == inventory.GetProperty("TotalPairsPerDirection").GetInt32() &&
            sourceCandidates == inventory.GetProperty("BoundingBoxCandidatesPerDirection").GetInt32(), "Frozen source pair inventory differs.");
        return new(root, zones, queries, sources.ToArray(), derived.ToArray(), Hash(manifestBytes), protocolHash, sourcePairs,
            sourceCandidates, checked(zones.Eligible.Length * queries.Eligible.Length), Candidates(zones.Eligible, queries.Eligible),
            invalid.ToArray(), maximumDisplacement);
    }

    internal static void VerifyUnchanged(FrozenCorpus corpus)
    {
        Contracts.Require(Hash(File.ReadAllBytes(Path.Combine(corpus.Root, "manifest.json"))) == corpus.ManifestSha256,
            "Manifest changed during evaluation.");
        Contracts.Require(Hash(File.ReadAllBytes(Path.Combine(corpus.Root, "PROTOCOL.md"))) == corpus.ProtocolSha256,
            "Source protocol changed during evaluation.");
        foreach (InputHash input in corpus.Sources.Concat(corpus.Derived))
            _ = Verify(corpus.Root, input.File, input.Sha256, input.Bytes);
    }

    private static RawFeature[] Read(string path, string role)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        Contracts.Require(root.GetProperty("SchemaVersion").GetInt32() == 1 && root.GetProperty("Role").GetString() == role &&
            root.GetProperty("SpatialReference").GetInt32() == 3067 && root.GetProperty("CoordinateUnit").GetString() == "metre",
            "Unexpected derived geometry contract: " + role);
        var features = new List<RawFeature>();
        foreach (JsonElement entry in root.GetProperty("Regions").EnumerateArray())
        {
            string id = entry.GetProperty("Id").GetString()!;
            Point2[] Ring(JsonElement coordinates)
            {
                Point2[] points = coordinates.EnumerateArray().Select(point =>
                {
                    Contracts.Require(point.GetArrayLength() == 2, "Unexpected coordinate dimension: " + role + "/" + id);
                    double x = point[0].GetDouble(), y = point[1].GetDouble();
                    Contracts.Require(double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= 1e100 && Math.Abs(y) <= 1e100,
                        "Nonfinite or out-of-range source coordinate: " + role + "/" + id);
                    return new Point2(x, y);
                }).ToArray();
                Contracts.Require(points.Length >= 4 && points[0].X == points[^1].X && points[0].Y == points[^1].Y,
                    "Unclosed or too-short source ring: " + role + "/" + id);
                return points;
            }
            RawPolygon[] parts = entry.GetProperty("Polygons").EnumerateArray().Select(p => new RawPolygon(Ring(p.GetProperty("Shell")),
                p.GetProperty("Holes").EnumerateArray().Select(Ring).ToArray())).ToArray();
            Contracts.Require(parts.Length > 0, "Empty polygon hierarchy: " + role + "/" + id);
            features.Add(new(id, entry.GetProperty("Name").GetString() ?? id, parts));
        }
        Contracts.Require(features.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() == features.Count,
            "Duplicated feature ID: " + role);
        return features.ToArray();
    }

    internal static RawFeature Quantized(RawFeature raw, out double maximumDisplacement)
    {
        double max = 0;
        Point2[] Ring(Point2[] ring) => ring.Select(point =>
        {
            double x = Quantize(point.X) / Contracts.ClipperScale, y = Quantize(point.Y) / Contracts.ClipperScale;
            max = Math.Max(max, Math.Max(Math.Abs(point.X - x), Math.Abs(point.Y - y)));
            return new Point2(x, y);
        }).ToArray();
        var result = new RawFeature(raw.Id, raw.Name, raw.Polygons.Select(p => new RawPolygon(Ring(p.Shell), p.Holes.Select(Ring).ToArray())).ToArray());
        maximumDisplacement = max;
        return result;
    }

    internal static long Quantize(double coordinate)
    {
        double scaled = coordinate * Contracts.ClipperScale;
        Contracts.Require(double.IsFinite(scaled) && Math.Abs(scaled) <= 4_503_599_627_370_496,
            "Clipper scaled coordinate exceeds the exact integer-conversion range.");
        return checked((long)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }

    private static int Candidates(RawFeature[] first, RawFeature[] second) =>
        first.Sum(a => second.Count(b => a.Bounds.Overlaps(b.Bounds)));

    private static InputHash Verify(string root, string file, string hash, int? expectedBytes)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, file));
        Contracts.Require(path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "Source path escapes its data directory.");
        byte[] bytes = File.ReadAllBytes(path);
        string actual = Hash(bytes);
        Contracts.Require(actual == hash && (!expectedBytes.HasValue || bytes.Length == expectedBytes.Value), "Frozen input hash differs: " + file);
        return new(file, bytes.Length, actual);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
