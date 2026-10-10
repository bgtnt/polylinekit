using System.Security.Cryptography;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using PolylineKit;

namespace NetTopologySuiteInterop;

/// <summary>Uses the complete existing Census snapshots, without selecting single-ring features.</summary>
internal static class GisChecks
{
    // Same fixed acceptance budgets as tests/PolylineKit.MultiRingChecks/GisChecks.cs.
    private const double AreaBudgetM2 = 1;
    private const double FractionBudget = 1e-8;
    private sealed record Feature(string Id, Geometry Geometry, PreparedRegion Prepared, int Components, int Rings, int Holes);
    private sealed record SourceHash(string File, int Bytes, string Sha256);
    private sealed class ErrorSummary(string metric, double budget)
    {
        public string Metric { get; } = metric;
        public double Budget { get; } = budget;
        public int Checks { get; private set; }
        public double MaximumAbsoluteError { get; private set; }
        public string? WorstInput { get; private set; }
        public double ActualAtWorst { get; private set; }
        public double ReferenceAtWorst { get; private set; }
        internal void Add(string input, double actual, double reference)
        {
            Checks++;
            double error = Math.Abs(actual - reference);
            if (WorstInput is null || error > MaximumAbsoluteError)
            {
                MaximumAbsoluteError = error; WorstInput = input;
                ActualAtWorst = actual; ReferenceAtWorst = reference;
            }
            InteropChecks.Require(double.IsFinite(actual) && double.IsFinite(reference) && error <= Budget,
                $"{Metric}/{input}: {actual:R} vs {reference:R}; difference {error:R}, budget {Budget:R}.");
        }
    }

    internal static object Run(string root)
    {
        string directory = Path.Combine(root, "examples", "RegionCoverage", "data");
        SourceHash[] hashes = VerifySources(directory);
        Feature[] counties = ReadFeatures(directory, "nc-counties.geojson"), districts = ReadFeatures(directory, "nc-districts.geojson");
        InteropChecks.Require(counties.Length == 100 && districts.Length == 14, "Unexpected complete Census inventory.");
        InteropChecks.Require(counties.Sum(f => f.Rings) == 104 && districts.Sum(f => f.Rings) == 19 &&
            counties.Sum(f => f.Holes) == 0 && districts.Sum(f => f.Holes) == 1, "Unexpected complete ring/hole inventory.");
        var errors = new Dictionary<string, ErrorSummary>(StringComparer.Ordinal);
        void Check(string metric, string input, double actual, double reference, bool fraction = false)
        {
            if (!errors.TryGetValue(metric, out ErrorSummary? summary))
                errors.Add(metric, summary = new(metric, fraction ? FractionBudget : AreaBudgetM2));
            summary.Add(input, actual, reference);
        }
        foreach (Feature feature in counties.Concat(districts))
        foreach (PathFillRule rule in InteropChecks.Rules)
            Check("OwnAreaM2", feature.Id + "/" + rule, RegionArea.FilledArea(feature.Prepared, rule), feature.Geometry.Area);
        int candidates = 0, pairs = 0, fullCalls = 0, selectedCalls = 0;
        foreach (Feature county in counties)
        foreach (Feature district in districts)
        {
            bool candidate = county.Geometry.EnvelopeInternal.Intersects(district.Geometry.EnvelopeInternal);
            if (candidate) candidates++;
            // The bounds rejection preserves exact zero for disjoint envelopes; every pair is checked in Core.
            double intersection = candidate ? OverlayNGRobust.Overlay(county.Geometry, district.Geometry, SpatialFunction.Intersection).Area : 0;
            double union = candidate ? OverlayNGRobust.Overlay(county.Geometry, district.Geometry, SpatialFunction.Union).Area
                : county.Geometry.Area + district.Geometry.Area;
            double difference = candidate ? OverlayNGRobust.Overlay(county.Geometry, district.Geometry, SpatialFunction.SymDifference).Area : union;
            foreach (bool reverse in new[] { false, true })
            foreach (PathFillRule rule in InteropChecks.Rules)
            {
                Feature first = reverse ? district : county, second = reverse ? county : district;
                string input = first.Id + "/" + second.Id + "/" + rule;
                RegionOverlapResult result = RegionArea.Compare(first.Prepared, second.Prepared, rule);
                fullCalls++;
                Check("FirstAreaM2", input, result.FirstArea, first.Geometry.Area);
                Check("SecondAreaM2", input, result.SecondArea, second.Geometry.Area);
                Check("IntersectionM2", input, result.IntersectionArea, intersection);
                Check("UnionM2", input, result.UnionArea, union);
                Check("SymmetricDifferenceM2", input, result.SymmetricDifferenceArea, difference);
                InteropChecks.Require(result.IntersectionOverUnion.HasValue && union > 0, "Valid nonempty GIS union required.");
                Check("IoU", input, result.IntersectionOverUnion!.Value, intersection / union, true);
                double selected = RegionArea.IntersectionArea(first.Prepared, second.Prepared, rule);
                selectedCalls++;
                Check("SelectedIntersectionM2", input, selected, intersection);
                Check("SelectedVsFullIntersectionM2", input, selected, result.IntersectionArea);
            }
            pairs++;
            if (pairs % 100 == 0) Console.WriteLine($"NTS interop GIS: {pairs}/1400 pairs, both directions and fill rules.");
        }
        InteropChecks.Require(pairs == 1400 && candidates == 285, "Frozen complete pair inventory changed.");
        // Recheck the inputs after evaluation so the evidence cannot mix source revisions.
        InteropChecks.Require(hashes.SequenceEqual(VerifySources(directory)), "Source files changed during evaluation.");
        return new
        {
            Description = "Complete existing Census snapshot, including disconnected parts and the real hole; no new download or repair.",
            SourceFiles = hashes,
            SpatialReference = "EPSG:5070", CoordinateUnit = "metre",
            CoordinatePreparation = "Copy original XY doubles unchanged; remove closing duplicates inside Core preparation; orient shells/holes via the role-aware factories.",
            Counties = Inventory(counties), Districts = Inventory(districts),
            GeometricPairs = pairs, Directions = 2, FillRules = 2,
            BoundingBoxCandidates = candidates, BoundingBoxRejects = pairs - candidates,
            FullMetricCalls = fullCalls, SelectedIntersectionCalls = selectedCalls,
            Checks = errors.Values.Sum(e => e.Checks), AreaBudgetM2, FractionBudget,
            Reference = "NetTopologySuite 2.6.0 OverlayNGRobust; independent binary64 Polygon/MultiPolygon overlay, not exact arithmetic.",
            Errors = errors.Values.OrderBy(e => e.Metric, StringComparer.Ordinal).ToArray()
        };
    }

    private static object Inventory(Feature[] features) => new
    {
        Features = features.Length, Components = features.Sum(f => f.Components), Rings = features.Sum(f => f.Rings),
        Holes = features.Sum(f => f.Holes), PreparedVertices = features.Sum(f => f.Prepared.VertexCount)
    };

    private static SourceHash[] VerifySources(string directory)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        InteropChecks.Require(Hash(File.ReadAllBytes(Path.Combine(directory, "PROTOCOL.md"))) == root.GetProperty("ProtocolSha256").GetString(),
            "The frozen protocol hash differs.");
        var hashes = new List<SourceHash>();
        foreach (JsonElement entry in root.GetProperty("SourceFiles").EnumerateArray())
        {
            string file = entry.GetProperty("File").GetString()!;
            InteropChecks.Require(file.StartsWith("source/nc-", StringComparison.Ordinal) && !file.Contains("..", StringComparison.Ordinal),
                "Unexpected source filename.");
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, file));
            string hash = Hash(bytes);
            InteropChecks.Require(bytes.Length == entry.GetProperty("Bytes").GetInt32() && hash == entry.GetProperty("Sha256").GetString(),
                "The frozen source hash differs: " + file);
            hashes.Add(new(file, bytes.Length, hash));
        }
        InteropChecks.Require(hashes.Count == 6 && hashes.Select(h => h.File).Distinct(StringComparer.Ordinal).Count() == 6,
            "Incomplete source manifest.");
        return hashes.ToArray();
    }

    private static Feature[] ReadFeatures(string directory, string file)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "source", file)));
        JsonElement root = document.RootElement;
        InteropChecks.Require(root.GetProperty("type").GetString() == "FeatureCollection" &&
            root.GetProperty("crs").GetProperty("properties").GetProperty("name").GetString() == "EPSG:5070", "Unexpected source coordinate reference.");
        InteropChecks.Require(!root.TryGetProperty("exceededTransferLimit", out JsonElement limit) || !limit.GetBoolean(), "Truncated source.");
        var result = new List<Feature>();
        foreach (JsonElement feature in root.GetProperty("features").EnumerateArray())
        {
            JsonElement properties = feature.GetProperty("properties"), geometry = feature.GetProperty("geometry");
            InteropChecks.Require(properties.GetProperty("STATE").GetString() == "37", "Unexpected source state.");
            string id = properties.GetProperty("GEOID").GetString()!;
            Polygon ReadPolygon(JsonElement part)
            {
                Coordinate[][] rings = part.EnumerateArray().Select(r => r.EnumerateArray().Select(c =>
                {
                    InteropChecks.Require(c.GetArrayLength() == 2, "Unexpected dimension: " + id);
                    double x = c[0].GetDouble(), y = c[1].GetDouble();
                    InteropChecks.Require(double.IsFinite(x) && double.IsFinite(y), "Nonfinite coordinate: " + id);
                    return new Coordinate(x, y);
                }).ToArray()).ToArray();
                InteropChecks.Require(rings.Length > 0 && rings.All(r => r.Length >= 4 && r[0].Equals2D(r[^1])), "Unclosed source ring: " + id);
                return InteropChecks.Polygon(rings[0], rings.Skip(1).ToArray());
            }
            JsonElement coordinates = geometry.GetProperty("coordinates");
            Polygon[] polygons = geometry.GetProperty("type").GetString() switch
            {
                "Polygon" => [ReadPolygon(coordinates)],
                "MultiPolygon" => coordinates.EnumerateArray().Select(ReadPolygon).ToArray(),
                _ => throw new InvalidOperationException("Unexpected source geometry type: " + id)
            };
            Geometry nts = polygons.Length == 1 ? polygons[0] : InteropChecks.Factory.CreateMultiPolygon(polygons);
            nts.SRID = 5070;
            InteropChecks.Require(nts.IsValid && nts.IsSimple && nts.Area > 0, "Invalid source hierarchy: " + id);
            int holes = polygons.Sum(p => p.NumInteriorRings);
            result.Add(new(id, nts, PolygonInterop.Convert(nts), polygons.Length, polygons.Length + holes, holes));
        }
        InteropChecks.Require(result.Select(f => f.Id).SequenceEqual(result.Select(f => f.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            "Duplicated or out-of-order source IDs.");
        return result.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
