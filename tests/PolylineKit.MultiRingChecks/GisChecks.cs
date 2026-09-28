using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using PolylineKit;

namespace PolylineKit.MultiRingChecks;

/// <summary>Full frozen Census population: preserve components and holes; never repair or select by results.</summary>
internal static class GisChecks
{
    private const double Scale = 1e6;
    private const double AreaBudget = 1;
    private const double FractionBudget = 1e-8;
    private static readonly GeometryFactory Factory = new();
    private sealed record RawFeature(string Id, string Name, string Type, Point2[][][] Parts);
    private sealed record Feature(string Id, string Name, Point2[][] Rings, Geometry Geometry, Paths64 Integer,
        int Parts, int Holes, bool PreviouslyAdmitted);
    private sealed record SourceHash(string File, int Bytes, string Sha256);
    private sealed record Values(double First, double Second, double Intersection, double Union, double Difference)
    {
        internal Values Reverse() => new(Second, First, Intersection, Union, Difference);
    }
    private sealed class ErrorSummary(string method, string rule, string metric, double budget)
    {
        public string Method { get; } = method;
        public string FillRule { get; } = rule;
        public string Metric { get; } = metric;
        public double Budget { get; } = budget;
        public int Checks { get; private set; }
        public double MaximumAbsoluteError { get; private set; }
        public string? WorstPair { get; private set; }
        public double ActualAtWorst { get; private set; }
        public double ReferenceAtWorst { get; private set; }
        internal void Add(string pair, double actual, double reference)
        {
            Checks++;
            double error = Math.Abs(actual - reference);
            if (WorstPair is null || error > MaximumAbsoluteError)
            {
                MaximumAbsoluteError = error; WorstPair = pair;
                ActualAtWorst = actual; ReferenceAtWorst = reference;
            }
            Require(double.IsFinite(actual) && double.IsFinite(reference) && error <= Budget,
                $"{Method}/{FillRule}/{Metric} failed at {pair}: {actual:R} vs {reference:R}, error {error:R}, budget {Budget:R}.");
        }
    }

    internal static object Run(string root)
    {
        string directory = Path.Combine(root, "examples", "RegionCoverage", "data");
        SourceHash[] hashes = VerifySource(directory);
        RawFeature[] rawCounties = ReadSource(directory, "nc-counties.geojson");
        RawFeature[] rawDistricts = ReadSource(directory, "nc-districts.geojson");
        Require(rawCounties.Length == 100 && rawDistricts.Length == 14, "Unexpected complete Census population.");
        Point2[] all = rawCounties.Concat(rawDistricts).SelectMany(f => f.Parts).SelectMany(p => p).SelectMany(r => r).ToArray();
        double originX = all.Min(p => p.X) * .5 + all.Max(p => p.X) * .5;
        double originY = all.Min(p => p.Y) * .5 + all.Max(p => p.Y) * .5;
        int reversals = 0;
        Feature[] ConvertAll(RawFeature[] source) => source.Select(f => Convert(f, originX, originY, ref reversals)).ToArray();
        Feature[] counties = ConvertAll(rawCounties), districts = ConvertAll(rawDistricts);
        Require(counties.Sum(f => f.Rings.Length) == 104 && districts.Sum(f => f.Rings.Length) == 19,
            "Unexpected complete ring inventory.");
        Require(counties.Sum(f => f.Holes) == 0 && districts.Sum(f => f.Holes) == 1, "Unexpected hole inventory.");
        var errors = new Dictionary<string, ErrorSummary>(StringComparer.Ordinal);
        void Check(string method, PathFillRule rule, string metric, string pair, double value, double reference, bool fraction = false)
        {
            string key = method + "/" + rule + "/" + metric;
            if (!errors.TryGetValue(key, out ErrorSummary? summary))
                errors.Add(key, summary = new(method, rule.ToString(), metric, fraction ? FractionBudget : AreaBudget));
            summary.Add(pair, value, reference);
        }
        void CompareValues(string method, PathFillRule rule, string pair, Values actual, Values reference)
        {
            Check(method, rule, "FirstAreaM2", pair, actual.First, reference.First);
            Check(method, rule, "SecondAreaM2", pair, actual.Second, reference.Second);
            Check(method, rule, "IntersectionM2", pair, actual.Intersection, reference.Intersection);
            Check(method, rule, "UnionM2", pair, actual.Union, reference.Union);
            Check(method, rule, "SymmetricDifferenceM2", pair, actual.Difference, reference.Difference);
            Require(actual.First > 0 && actual.Second > 0 && actual.Union > 0, "Nonpositive area in valid Census geometry: " + pair);
            Check(method, rule, "IoU", pair, actual.Intersection / actual.Union, reference.Intersection / reference.Union, true);
            Check(method, rule, "FirstCoverage", pair, actual.Intersection / actual.First, reference.Intersection / reference.First, true);
            Check(method, rule, "SecondCoverage", pair, actual.Intersection / actual.Second, reference.Intersection / reference.Second, true);
        }
        int candidates = 0, newCandidates = 0, pairCalls = 0, intersectionCalls = 0;
        var pairEvidence = new List<object>();
        foreach (Feature a in counties)
        foreach (Feature b in districts)
        {
            bool candidate = a.Geometry.EnvelopeInternal.Intersects(b.Geometry.EnvelopeInternal);
            if (candidate)
            {
                candidates++;
                if (!a.PreviouslyAdmitted || !b.PreviouslyAdmitted) newCandidates++;
            }
            // NTS receives real Polygon/MultiPolygon hierarchy, independently of the flattened winding input.
            double intersection = candidate ? OverlayNGRobust.Overlay(a.Geometry, b.Geometry, SpatialFunction.Intersection).Area : 0;
            double union = candidate ? OverlayNGRobust.Overlay(a.Geometry, b.Geometry, SpatialFunction.Union).Area : a.Geometry.Area + b.Geometry.Area;
            double difference = candidate ? OverlayNGRobust.Overlay(a.Geometry, b.Geometry, SpatialFunction.SymDifference).Area : union;
            var reference = new Values(a.Geometry.Area, b.Geometry.Area, intersection, union, difference);
            pairEvidence.Add(new { County = a.Id, District = b.Id, BoundsCandidate = candidate,
                IncludesPreviouslyExcludedFeature = !a.PreviouslyAdmitted || !b.PreviouslyAdmitted,
                NtsIntersectionM2 = intersection, NtsCountyCoverage = intersection / a.Geometry.Area,
                NtsDistrictCoverage = intersection / b.Geometry.Area, NtsIoU = intersection / union });
            foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
            {
                FillRule clipperRule = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
                var clipper = new Values(ClipperArea(a.Integer, null, ClipType.Union, clipperRule),
                    ClipperArea(b.Integer, null, ClipType.Union, clipperRule),
                    ClipperArea(a.Integer, b.Integer, ClipType.Intersection, clipperRule),
                    ClipperArea(a.Integer, b.Integer, ClipType.Union, clipperRule),
                    ClipperArea(a.Integer, b.Integer, ClipType.Xor, clipperRule));
                CompareValues("Clipper64-vs-NTS", rule, a.Id + "/" + b.Id, clipper, reference);
                foreach (bool reversed in new[] { false, true })
                {
                    Feature first = reversed ? b : a, second = reversed ? a : b;
                    Values expected = reversed ? reference.Reverse() : reference;
                    Values quantized = reversed ? clipper.Reverse() : clipper;
                    string pair = first.Id + "/" + second.Id;
                    WindingOverlapResult result = MultiRingArea.Compare(first.Rings, second.Rings, rule);
                    pairCalls++;
                    var actual = new Values(result.FirstArea, result.SecondArea, result.IntersectionArea,
                        result.UnionArea, result.SymmetricDifferenceArea);
                    CompareValues("Core-vs-NTS", rule, pair, actual, expected);
                    CompareValues("Core-vs-Clipper64", rule, pair, actual, quantized);
                    double selected = MultiRingArea.Intersection(first.Rings, second.Rings, rule);
                    intersectionCalls++;
                    Check("Core-selected-vs-NTS", rule, "IntersectionM2", pair, selected, expected.Intersection);
                    Check("Core-selected-vs-full", rule, "IntersectionM2", pair, selected, actual.Intersection);
                }
            }
            if (pairEvidence.Count % 100 == 0) Console.WriteLine($"Full GIS: {pairEvidence.Count}/1400 geometric pairs checked in both directions and fill rules.");
        }
        foreach (Feature feature in counties.Concat(districts))
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
            Check("Core-selected-vs-NTS", rule, "OwnAreaM2", feature.Id,
                MultiRingArea.FilledArea(feature.Rings, rule), feature.Geometry.Area);
        Require(candidates == 285 && newCandidates == 74, "Complete candidate inventory changed.");

        // This is a real hole/island complement, not a generated analytic fixture.
        Feature district2 = districts.Single(f => f.Id == "3702"), district13 = districts.Single(f => f.Id == "3713");
        Point2[] island = district2.Rings.Single(r => r.Length == 10), hole = district13.Rings.Single(r => r.Length == 10);
        Require(SameBoundaryReversed(island, hole), "The recorded District 2 island / District 13 hole boundary differs.");
        double islandArea = Factory.CreatePolygon(Close(island)).Area;
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            Check("Core-real-hole", rule, "IslandAreaM2", "3702/island", MultiRingArea.FilledArea([island], rule), islandArea);
            Check("Core-real-hole", rule, "HoleIntersectionM2", "3702-island/3713",
                MultiRingArea.Intersection([island], district13.Rings, rule), 0);
        }
        return new
        {
            Description = "Full frozen Census population; complete hierarchy retained; no repair, filtering or downloads.",
            SourceFiles = hashes, SourceIdentitySha256 = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hashes))),
            SpatialReference = "EPSG:5070", CoordinateUnit = "metre", CommonOrigin = new { X = originX, Y = originY },
            CoordinatePreparation = "One common translation; remove exactly one closing duplicate; orient shells CCW and holes CW with NTS robust orientation.",
            ReversedRingsForNonZero = reversals,
            Counties = Inventory(counties), Districts = Inventory(districts),
            PairsPerDirection = counties.Length * districts.Length, Directions = 2, FillRules = 2,
            BoundingBoxCandidatesPerDirection = candidates, BoundingBoxRejectsPerDirection = 1400 - candidates,
            NewCandidatesVersusSingleRing = newCandidates, FullMetricCalls = pairCalls, SelectedIntersectionCalls = intersectionCalls,
            ClipperCoordinateGridMetres = 1 / Scale, AreaBudgetM2 = AreaBudget, FractionBudget,
            NtsReference = "OverlayNGRobust, binary64 Polygon/MultiPolygon hierarchy; independent comparison, not exact arithmetic.",
            TinyIslandHole = new { IslandDistrict = "3702", HoleDistrict = "3713", Vertices = island.Length, AreaM2 = islandArea,
                BoundaryExactlyReversed = true },
            Errors = errors.Values.OrderBy(e => e.Method).ThenBy(e => e.FillRule).ThenBy(e => e.Metric).ToArray(),
            PairEvidence = pairEvidence
        };
    }

    private static object Inventory(Feature[] features) => new
    {
        Features = features.Length, Components = features.Sum(f => f.Parts), Rings = features.Sum(f => f.Rings.Length),
        Holes = features.Sum(f => f.Holes), Vertices = features.Sum(f => f.Rings.Sum(r => r.Length)),
        PreviouslyExcluded = features.Where(f => !f.PreviouslyAdmitted).Select(f => new
        { f.Id, f.Name, Components = f.Parts, Rings = f.Rings.Length, f.Holes, Vertices = f.Rings.Sum(r => r.Length) }).ToArray()
    };

    private static SourceHash[] VerifySource(string directory)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Require(Hash(File.ReadAllBytes(Path.Combine(directory, "PROTOCOL.md"))) == root.GetProperty("ProtocolSha256").GetString(),
            "Frozen source protocol hash differs.");
        var hashes = new List<SourceHash>();
        foreach (JsonElement entry in root.GetProperty("SourceFiles").EnumerateArray())
        {
            string file = entry.GetProperty("File").GetString()!;
            // The manifest is repository data, but source names remain deliberately bounded.
            Require(file.StartsWith("source/nc-", StringComparison.Ordinal) && !file.Contains(".."), "Unexpected source filename.");
            byte[] raw = File.ReadAllBytes(Path.Combine(directory, file));
            string hash = Hash(raw);
            Require(raw.Length == entry.GetProperty("Bytes").GetInt32() && hash == entry.GetProperty("Sha256").GetString(),
                "Frozen source hash differs: " + file);
            hashes.Add(new(file, raw.Length, hash));
        }
        Require(hashes.Count == 6 && hashes.Select(h => h.File).Distinct().Count() == 6, "Incomplete source manifest.");
        return hashes.ToArray();
    }

    private static RawFeature[] ReadSource(string directory, string filename)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "source", filename)));
        JsonElement collection = document.RootElement;
        Require(collection.GetProperty("type").GetString() == "FeatureCollection" &&
            collection.GetProperty("crs").GetProperty("properties").GetProperty("name").GetString() == "EPSG:5070", "Unexpected source contract.");
        Require(!collection.TryGetProperty("exceededTransferLimit", out JsonElement limited) || !limited.GetBoolean(), "Truncated source.");
        var features = new List<RawFeature>();
        foreach (JsonElement item in collection.GetProperty("features").EnumerateArray())
        {
            JsonElement properties = item.GetProperty("properties"), geometry = item.GetProperty("geometry");
            Require(properties.GetProperty("STATE").GetString() == "37", "Unexpected Census state.");
            string id = properties.GetProperty("GEOID").GetString()!, name = properties.GetProperty("NAME").GetString()!;
            string type = geometry.GetProperty("type").GetString()!;
            JsonElement coordinates = geometry.GetProperty("coordinates");
            Point2[][] Part(JsonElement part) => part.EnumerateArray().Select(ring =>
            {
                Point2[] closed = ring.EnumerateArray().Select(point =>
                {
                    Require(point.GetArrayLength() == 2, "Unexpected coordinate dimension: " + id);
                    double x = point[0].GetDouble(), y = point[1].GetDouble();
                    Require(double.IsFinite(x) && double.IsFinite(y), "Nonfinite source coordinate: " + id);
                    return new Point2(x, y);
                }).ToArray();
                Require(closed.Length >= 4 && Same(closed[0], closed[^1]), "Invalid source closure: " + id);
                return closed[..^1];
            }).ToArray();
            Point2[][][] parts = type switch
            {
                "Polygon" => [Part(coordinates)],
                "MultiPolygon" => coordinates.EnumerateArray().Select(Part).ToArray(),
                _ => throw new InvalidOperationException("Unsupported geometry type: " + type)
            };
            Require(parts.Length > 0 && parts.All(p => p.Length > 0), "Empty source geometry: " + id);
            features.Add(new(id, name, type, parts));
        }
        Require(features.Select(f => f.Id).SequenceEqual(features.Select(f => f.Id).Distinct().Order(StringComparer.Ordinal)),
            "Source IDs are duplicated or out of order.");
        return features.ToArray();
    }

    private static Feature Convert(RawFeature source, double ox, double oy, ref int reversals)
    {
        // Validate original and translated geometry independently; source ring order defines shell/hole roles.
        Geometry sourceGeometry = BuildGeometry(source.Parts);
        Require(sourceGeometry.IsValid && sourceGeometry.IsSimple && sourceGeometry.Area > 0, "Invalid original GIS geometry: " + source.Id);
        var parts = new List<Point2[][]>();
        foreach (Point2[][] part in source.Parts)
        {
            var rings = new List<Point2[]>();
            for (int i = 0; i < part.Length; i++)
            {
                Point2[] ring = part[i].Select(p => new Point2(p.X - ox, p.Y - oy)).ToArray();
                bool isCcw = Orientation.IsCCW(Close(ring));
                if (isCcw != (i == 0)) { Array.Reverse(ring); reversals++; }
                rings.Add(ring);
            }
            parts.Add(rings.ToArray());
        }
        Geometry geometry = BuildGeometry(parts.ToArray());
        Require(geometry.IsValid && geometry.IsSimple && geometry.Area > 0, "Invalid translated GIS geometry: " + source.Id);
        Point2[][] flattened = parts.SelectMany(p => p).ToArray();
        var integer = new Paths64();
        foreach (Point2[] ring in flattened)
        {
            var path = new Path64(ring.Length);
            foreach (Point2 p in ring)
            {
                double x = p.X * Scale, y = p.Y * Scale;
                Require(Math.Abs(x) <= 4_503_599_627_370_496 && Math.Abs(y) <= 4_503_599_627_370_496,
                    "Clipper input exceeds exact integer conversion range.");
                path.Add(new Point64(checked((long)Math.Round(x, MidpointRounding.AwayFromZero)),
                    checked((long)Math.Round(y, MidpointRounding.AwayFromZero))));
            }
            integer.Add(path);
        }
        return new(source.Id, source.Name, flattened, geometry, integer, parts.Count,
            parts.Sum(p => p.Length - 1), source.Type == "Polygon" && parts[0].Length == 1);
    }

    private static Geometry BuildGeometry(Point2[][][] parts)
    {
        Polygon[] polygons = parts.Select(part => Factory.CreatePolygon(Factory.CreateLinearRing(Close(part[0])),
            part.Skip(1).Select(r => Factory.CreateLinearRing(Close(r))).ToArray())).ToArray();
        return polygons.Length == 1 ? polygons[0] : Factory.CreateMultiPolygon(polygons);
    }
    private static Coordinate[] Close(Point2[] ring) => ring.Select(p => new Coordinate(p.X, p.Y))
        .Append(new Coordinate(ring[0].X, ring[0].Y)).ToArray();
    private static double ClipperArea(Paths64 first, Paths64? second, ClipType operation, FillRule rule)
    {
        var engine = new Clipper64();
        engine.AddSubject(first);
        if (second is not null) engine.AddClip(second);
        var solution = new Paths64();
        Require(engine.Execute(operation, rule, solution), "Clipper failed during full GIS validation.");
        return Math.Abs(Clipper.Area(solution)) / Scale / Scale;
    }
    private static bool SameBoundaryReversed(Point2[] a, Point2[] b)
    {
        if (a.Length != b.Length) return false;
        for (int offset = 0; offset < b.Length; offset++)
            if (Enumerable.Range(0, a.Length).All(i => Same(a[i], b[(offset - i + b.Length) % b.Length]))) return true;
        return false;
    }
    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
    private static string Hash(byte[] bytes) => System.Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
