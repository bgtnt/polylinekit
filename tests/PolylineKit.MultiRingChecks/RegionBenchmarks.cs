using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Clipper2Lib;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using PolylineKit;

namespace PolylineKit.MultiRingChecks;

/// <summary>Fixed complete-region workloads; adapters and timing are outside the shipped library.</summary>
internal static class RegionBenchmarks
{
    private const double Scale = 1e6;
    private static readonly GeometryFactory Factory = new();
    private static readonly string[] Methods = ["Core-prepared-region", "Clipper64-reused-data", "NTS-OverlayNGRobust"];
    private static readonly string[] Scopes = ["prepare-catalogues", "warm-table", "prepare-plus-one-table"];
    private static double sink;
    private sealed record Raw(string Id, Point2[][][] Parts)
    {
        internal Point2[][] Rings { get; } = Parts.SelectMany(p => p).ToArray();
    }
    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Overlaps(Box other) => MaxX >= other.MinX && other.MaxX >= MinX &&
            MaxY >= other.MinY && other.MaxY >= MinY;
    }
    private sealed record Scenario(string Name, Raw[] First, Raw[] Second, bool Gis, int ExpectedCandidates,
        double? ExpectedOwnArea = null, double? ExpectedIntersection = null);
    private sealed record Corpus(Scenario[] Cases, SortedDictionary<string, string> SourceHashes, double OriginX, double OriginY);
    private sealed record Prepared(Box Bounds, double Area, object Data);
    private readonly record struct Digest(double IntersectionSum, double CoverageSum, int Candidates);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Row(string Scenario, string Method, string Scope, Digest Value,
        double MedianNanoseconds, double MedianBytes, Sample[] Samples);
    private sealed record Accuracy(string Scenario, string Method, string FillRule, int Pairs, int Candidates,
        double MaximumOwnAreaError, double MaximumIntersectionError, double MaximumCoverageError);

    internal static object Run(string root, int run)
    {
        Require(run is >= 1 and <= 3, "Run must be 1, 2 or 3.");
        Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Set DOTNET_TieredCompilation=0 before launching each process.");
        Corpus corpus = Load(root);
        var source = SourceHashes(root);
        var accuracy = Validate(corpus.Cases);
        var rows = new List<Row>();
        foreach (Scenario scenario in corpus.Cases)
        foreach (string method in Methods.Skip(run - 1).Concat(Methods.Take(run - 1)))
        {
            Session prepared = new(scenario, method, PathFillRule.NonZero);
            foreach (string scope in Scopes)
            {
                Func<Digest> invoke = scope switch
                {
                    "prepare-catalogues" => () => new Session(scenario, method, PathFillRule.NonZero).PreparationValue(),
                    "warm-table" => prepared.Table,
                    "prepare-plus-one-table" => () => new Session(scenario, method, PathFillRule.NonZero).Table(),
                    _ => throw new InvalidOperationException()
                };
                rows.Add(Measure(scenario.Name, method, scope, invoke));
            }
            Console.WriteLine($"Region benchmark run {run}: {scenario.Name}/{method} complete.");
        }
        Require(source.SequenceEqual(SourceHashes(root)), "Source changed during measurement.");
        Require(rows.Count == 90, "Incomplete benchmark matrix.");
        GC.KeepAlive(sink);
        return new
        {
            SchemaVersion = 1, Run = run, Utc = DateTime.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            ForceScalar = Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1",
            Vector256 = System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated,
            CoreVersion = typeof(RegionArea).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            CoreTarget = typeof(RegionArea).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName,
            BinarySha256 = new[] { typeof(RegionBenchmarks).Assembly, typeof(RegionArea).Assembly, typeof(Clipper).Assembly, typeof(Polygon).Assembly }
                .ToDictionary(a => a.GetName().Name!, a => Hash(File.ReadAllBytes(a.Location))),
            SourceSha256 = source, InputSha256 = corpus.SourceHashes,
            ProtocolSha256 = Hash(File.ReadAllBytes(Path.Combine(root, "tests/PolylineKit.MultiRingChecks/PROTOCOL.md"))),
            CommonOrigin = new { X = corpus.OriginX, Y = corpus.OriginY },
            ClipperGrid = 1 / Scale, TimedFillRule = "NonZero", WarmMilliseconds = 10, CalibrationMilliseconds = 10, Batches = 5,
            Scenarios = corpus.Cases.Select(c => new { c.Name, c.Gis, FirstFeatures = c.First.Length, SecondFeatures = c.Second.Length,
                FirstRings = c.First.Sum(f => f.Rings.Length), SecondRings = c.Second.Sum(f => f.Rings.Length),
                FirstVertices = c.First.Sum(f => f.Rings.Sum(r => r.Length)), SecondVertices = c.Second.Sum(f => f.Rings.Sum(r => r.Length)),
                Pairs = c.First.Length * c.Second.Length, c.ExpectedCandidates, c.ExpectedOwnArea, c.ExpectedIntersection }).ToArray(),
            Accuracy = accuracy, Measurements = rows.ToArray()
        };
    }

    // This parses and validates the full matrix before reporting medians of independent process medians.
    internal static object Summarize(string[] paths)
    {
        Require(paths.Length == 3 && paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "Supply three distinct run files.");
        var documents = paths.Select(p => JsonDocument.Parse(File.ReadAllBytes(p))).ToArray();
        try
        {
            JsonElement[] runs = documents.Select(d => d.RootElement).OrderBy(r => r.GetProperty("Run").GetInt32()).ToArray();
            Require(runs.Select(r => r.GetProperty("Run").GetInt32()).SequenceEqual(new[] { 1, 2, 3 }), "Run identities must be 1, 2, 3.");
            foreach (string key in new[] { "SchemaVersion", "Runtime", "OS", "Architecture", "Processor", "TieredCompilation", "ForceScalar", "Vector256", "CoreVersion", "CoreTarget",
                "BinarySha256", "SourceSha256", "InputSha256", "ProtocolSha256", "CommonOrigin", "ClipperGrid", "TimedFillRule", "WarmMilliseconds", "CalibrationMilliseconds", "Batches", "Scenarios", "Accuracy" })
                Require(runs.All(r => r.GetProperty(key).GetRawText() == runs[0].GetProperty(key).GetRawText()), "Run metadata changed: " + key);
            string[] scenarios = runs[0].GetProperty("Scenarios").EnumerateArray().Select(s => s.GetProperty("Name").GetString()!).ToArray();
            string[] requiredScenarios = new[] { "census-county-to-district", "census-district-to-county" }
                .Concat(new[] { 16, 64, 256, 1024 }.SelectMany(r => new[] { "disjoint-squares-" + r, "shells-with-holes-" + r })).ToArray();
            Require(scenarios.Length == 10 && scenarios.ToHashSet().SetEquals(requiredScenarios), "Expected all ten fixed scenarios.");
            var scenarioMetadata = runs[0].GetProperty("Scenarios").EnumerateArray().ToDictionary(s => s.GetProperty("Name").GetString()!);
            var accuracyKeys = new HashSet<(string, string, string)>();
            foreach (JsonElement group in runs[0].GetProperty("Accuracy").EnumerateArray())
            {
                string scenario = group.GetProperty("Scenario").GetString()!, method = group.GetProperty("Method").GetString()!, rule = group.GetProperty("FillRule").GetString()!;
                Require(scenarioMetadata.TryGetValue(scenario, out JsonElement metadata) && Methods.Contains(method) &&
                    rule is "NonZero" or "EvenOdd" && accuracyKeys.Add((scenario, method, rule)), "Unexpected/duplicate accuracy group.");
                Require(group.GetProperty("Pairs").GetInt32() == metadata.GetProperty("Pairs").GetInt32() &&
                    group.GetProperty("Candidates").GetInt32() == metadata.GetProperty("ExpectedCandidates").GetInt32(), "Incomplete accuracy pair inventory.");
                bool gis = metadata.GetProperty("Gis").GetBoolean();
                double own = gis ? 0 : metadata.GetProperty("ExpectedOwnArea").GetDouble();
                double intersection = gis ? 0 : metadata.GetProperty("ExpectedIntersection").GetDouble();
                foreach (var (metric, budget) in new[]
                {
                    ("MaximumOwnAreaError", gis ? 1 : 2e-12 * Math.Max(1, own)),
                    ("MaximumIntersectionError", gis ? 1 : 2e-12 * Math.Max(1, intersection)),
                    ("MaximumCoverageError", gis ? 1e-8 : 2e-12 * Math.Max(1, intersection / own))
                })
                {
                    double error = group.GetProperty(metric).GetDouble();
                    Require(double.IsFinite(error) && error >= 0 && error <= budget, "Invalid recorded accuracy result.");
                }
            }
            Require(accuracyKeys.Count == 60, "Expected all sixty accuracy groups.");
            var expected = scenarios.SelectMany(s => Methods.SelectMany(m => Scopes.Select(scope => (s, m, scope)))).ToHashSet();
            var matrices = new List<Dictionary<(string, string, string), JsonElement>>();
            foreach (JsonElement run in runs)
            {
                var matrix = new Dictionary<(string, string, string), JsonElement>();
                foreach (JsonElement row in run.GetProperty("Measurements").EnumerateArray())
                {
                    var key = (row.GetProperty("Scenario").GetString()!, row.GetProperty("Method").GetString()!, row.GetProperty("Scope").GetString()!);
                    Require(matrix.TryAdd(key, row), "Duplicate timing row.");
                    JsonElement[] samples = row.GetProperty("Samples").EnumerateArray().ToArray();
                    Require(samples.Length == 5, "Expected five batches.");
                    foreach (JsonElement sample in samples)
                    {
                        int iterations = sample.GetProperty("Iterations").GetInt32();
                        double time = sample.GetProperty("Nanoseconds").GetDouble(), bytes = sample.GetProperty("Bytes").GetDouble();
                        Require(iterations > 0 && iterations <= 16384 && (iterations & (iterations - 1)) == 0 &&
                            double.IsFinite(time) && time > 0 && double.IsFinite(bytes) && bytes >= 0, "Invalid sample.");
                    }
                    Require(row.GetProperty("MedianNanoseconds").GetDouble() == samples.Select(s => s.GetProperty("Nanoseconds").GetDouble()).Order().ElementAt(2) &&
                        row.GetProperty("MedianBytes").GetDouble() == samples.Select(s => s.GetProperty("Bytes").GetDouble()).Order().ElementAt(2), "Wrong recorded median.");
                }
                Require(matrix.Keys.ToHashSet().SetEquals(expected), "Incomplete timing matrix.");
                foreach (string scenario in scenarios)
                foreach (string method in Methods)
                {
                    JsonElement warm = matrix[(scenario, method, "warm-table")].GetProperty("Value");
                    JsonElement fresh = matrix[(scenario, method, "prepare-plus-one-table")].GetProperty("Value");
                    Require(warm.GetRawText() == fresh.GetRawText(), "Prepared/fresh table digests differ.");
                    int candidates = runs[0].GetProperty("Scenarios").EnumerateArray().Single(s => s.GetProperty("Name").GetString() == scenario)
                        .GetProperty("ExpectedCandidates").GetInt32();
                    Require(warm.GetProperty("Candidates").GetInt32() == candidates, "Timed candidate count differs.");
                }
                matrices.Add(matrix);
            }
            var summary = new List<object>();
            foreach (var key in expected.OrderBy(k => k.s, StringComparer.Ordinal).ThenBy(k => k.m, StringComparer.Ordinal).ThenBy(k => k.scope, StringComparer.Ordinal))
            {
                JsonElement[] rows = matrices.Select(m => m[key]).ToArray();
                Require(rows.All(r => r.GetProperty("Value").GetRawText() == rows[0].GetProperty("Value").GetRawText()), "Digest changed across runs.");
                double[] times = rows.Select(r => r.GetProperty("MedianNanoseconds").GetDouble()).ToArray();
                double[] bytes = rows.Select(r => r.GetProperty("MedianBytes").GetDouble()).ToArray();
                summary.Add(new { Scenario = key.s, Method = key.m, Scope = key.scope, MedianNanoseconds = times.Order().ElementAt(1),
                    MinimumNanoseconds = times.Min(), MaximumNanoseconds = times.Max(), MedianBytes = bytes.Order().ElementAt(1),
                    MinimumBytes = bytes.Min(), MaximumBytes = bytes.Max(), Value = rows[0].GetProperty("Value").Clone() });
            }
            return new { Samples = 1350, Processes = 3, RowsPerProcess = 90, CoreVersion = runs[0].GetProperty("CoreVersion").GetString(),
                RunFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }).ToArray(),
                Measurements = summary.ToArray(), Interpretation = "No speed threshold: compare matching scenarios and scopes; disagreement and unfavorable results remain reportable." };
        }
        finally { foreach (JsonDocument document in documents) document.Dispose(); }
    }

    private sealed class Session
    {
        private readonly string method;
        private readonly PathFillRule rule;
        private readonly Clipper64? clipper;
        private readonly Paths64? closed, open;
        internal Prepared[] First { get; }
        internal Prepared[] Second { get; }
        internal Session(Scenario scenario, string method, PathFillRule rule)
        {
            this.method = method; this.rule = rule;
            if (method == Methods[1]) { clipper = new(); closed = new(); open = new(); }
            First = scenario.First.Select(f => Prepare(f, false)).ToArray();
            Second = scenario.Second.Select(f => Prepare(f, true)).ToArray();
        }
        private Prepared Prepare(Raw raw, bool clip)
        {
            if (method == Methods[0])
            {
                PreparedRegion region = PreparedRegion.FromRings(raw.Rings);
                Bounds2D coreBounds = region.Bounds!.Value;
                return new(new(coreBounds.MinX, coreBounds.MinY, coreBounds.MaxX, coreBounds.MaxY), RegionArea.FilledArea(region, rule), region);
            }
            if (method == Methods[1])
            {
                var paths = new Paths64();
                foreach (Point2[] ring in raw.Rings)
                {
                    var path = new Path64(ring.Length);
                    foreach (Point2 point in ring)
                    {
                        double x = point.X * Scale, y = point.Y * Scale;
                        Require(double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= 4_503_599_627_370_496 && Math.Abs(y) <= 4_503_599_627_370_496,
                            "Clipper coordinate exceeds exact integer conversion range.");
                        path.Add(new Point64(checked((long)Math.Round(x, MidpointRounding.AwayFromZero)), checked((long)Math.Round(y, MidpointRounding.AwayFromZero))));
                    }
                    paths.Add(path);
                }
                var data = new ReuseableDataContainer64();
                data.AddPaths(paths, clip ? PathType.Clip : PathType.Subject, false);
                // Every timed input has a validated disjoint Polygon/MultiPolygon hierarchy. Signed
                // shell/hole area is therefore the filled denominator under both selected fill rules.
                return new(Bounds(paths.SelectMany(r => r).Select(p => new Point2(p.X / Scale, p.Y / Scale))),
                    Math.Abs(Clipper.Area(paths)) / Scale / Scale, data);
            }
            Geometry geometry = Geometry(raw);
            Envelope bounds = geometry.EnvelopeInternal;
            return new(new(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY), geometry.Area, geometry);
        }
        internal double Intersection(Prepared first, Prepared second)
        {
            if (!first.Bounds.Overlaps(second.Bounds)) return 0;
            if (method == Methods[0]) return RegionArea.IntersectionArea((PreparedRegion)first.Data, (PreparedRegion)second.Data, rule);
            if (method == Methods[1])
            {
                clipper!.Clear();
                clipper.AddReuseableData((ReuseableDataContainer64)first.Data);
                clipper.AddReuseableData((ReuseableDataContainer64)second.Data);
                Require(clipper.Execute(ClipType.Intersection, rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd, closed!, open!), "Clipper intersection failed.");
                return Math.Abs(Clipper.Area(closed!)) / Scale / Scale;
            }
            return OverlayNGRobust.Overlay((Geometry)first.Data, (Geometry)second.Data, SpatialFunction.Intersection).Area;
        }
        internal Digest Table()
        {
            double area = 0, coverage = 0; int candidates = 0;
            foreach (Prepared first in First)
            foreach (Prepared second in Second)
            {
                if (!first.Bounds.Overlaps(second.Bounds)) continue;
                candidates++;
                double intersection = Intersection(first, second);
                area += intersection; coverage += intersection / first.Area;
            }
            return new(area, coverage, candidates);
        }
        internal Digest PreparationValue() => new(First.Sum(f => f.Area), Second.Sum(f => f.Area), First.Length + Second.Length);
    }

    private static Accuracy[] Validate(Scenario[] scenarios)
    {
        var accuracy = new List<Accuracy>();
        foreach (Scenario scenario in scenarios)
        {
            foreach (Raw raw in scenario.First.Concat(scenario.Second))
            {
                Geometry geometry = Geometry(raw);
                Require(geometry.IsValid && geometry.IsSimple && geometry.Area > 0, "Invalid source geometry: " + raw.Id);
            }
            var reference = new Session(scenario, Methods[2], PathFillRule.NonZero);
            // Accuracy-only results are never provided to any timed Session.
            var expected = new double[reference.First.Length, reference.Second.Length];
            for (int i = 0; i < reference.First.Length; i++)
            for (int j = 0; j < reference.Second.Length; j++)
                expected[i, j] = scenario.ExpectedIntersection ?? reference.Intersection(reference.First[i], reference.Second[j]);
            foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
            foreach (string method in Methods)
            {
                var actual = new Session(scenario, method, rule);
                double ownError = 0, intersectionError = 0, coverageError = 0;
                int candidates = 0;
                void CheckOwn(Prepared[] source, Prepared[] comparison)
                {
                    for (int i = 0; i < source.Length; i++)
                    {
                        double expectedArea = scenario.ExpectedOwnArea ?? comparison[i].Area;
                        ownError = Math.Max(ownError, Math.Abs(source[i].Area - expectedArea));
                        Check(source[i].Area, expectedArea, false);
                    }
                }
                CheckOwn(actual.First, reference.First); CheckOwn(actual.Second, reference.Second);
                for (int i = 0; i < actual.First.Length; i++)
                for (int j = 0; j < actual.Second.Length; j++)
                {
                    bool candidate = actual.First[i].Bounds.Overlaps(actual.Second[j].Bounds);
                    Require(candidate == reference.First[i].Bounds.Overlaps(reference.Second[j].Bounds), "Adapter bounds changed candidate membership.");
                    if (candidate) candidates++;
                    double value = actual.Intersection(actual.First[i], actual.Second[j]);
                    double fraction = value / actual.First[i].Area;
                    double expectedFraction = expected[i, j] / (scenario.ExpectedOwnArea ?? reference.First[i].Area);
                    intersectionError = Math.Max(intersectionError, Math.Abs(value - expected[i, j]));
                    coverageError = Math.Max(coverageError, Math.Abs(fraction - expectedFraction));
                    Check(value, expected[i, j], false); Check(fraction, expectedFraction, true);
                }
                Require(candidates == scenario.ExpectedCandidates, "Candidate inventory changed: " + scenario.Name);
                accuracy.Add(new(scenario.Name, method, rule.ToString(), actual.First.Length * actual.Second.Length,
                    candidates, ownError, intersectionError, coverageError));
                void Check(double value, double expectedValue, bool fraction)
                {
                    double budget = scenario.Gis ? fraction ? 1e-8 : 1 : 2e-12 * Math.Max(1, Math.Abs(expectedValue));
                    Require(double.IsFinite(value) && double.IsFinite(expectedValue) && Math.Abs(value - expectedValue) <= budget,
                        $"{scenario.Name}/{method}/{rule}: {value:R} vs {expectedValue:R}; budget {budget:R}.");
                }
            }
        }
        Console.WriteLine($"Region benchmark: {accuracy.Count} scenario/backend/fill accuracy groups passed before timing.");
        return accuracy.ToArray();
    }

    private static Row Measure(string scenario, string method, string scope, Func<Digest> invoke)
    {
        Digest value = invoke();
        var watch = Stopwatch.StartNew();
        bool warmStable = true;
        while (watch.ElapsedMilliseconds < 10)
        {
            Digest actual = invoke(); Keep(actual); warmStable &= actual == value;
        }
        Require(warmStable, "Unstable warm-up digest.");
        int iterations = 1;
        while (true)
        {
            bool calibrationStable = true;
            watch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                Digest actual = invoke(); Keep(actual); calibrationStable &= actual == value;
            }
            Require(calibrationStable, "Unstable calibration digest.");
            if (watch.ElapsedMilliseconds >= 10 || iterations >= 16384) break;
            iterations *= 2;
        }
        var samples = new Sample[5];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            bool stable = true;
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                Digest actual = invoke(); Keep(actual); stable &= actual == value;
            }
            long elapsed = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(stable, "Unstable measured digest: " + scenario + "/" + method + "/" + scope);
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)bytes / iterations);
        }
        Require(value == invoke(), "Unstable benchmark digest: " + scenario + "/" + method + "/" + scope);
        return new(scenario, method, scope, value, samples.Select(s => s.Nanoseconds).Order().ElementAt(2),
            samples.Select(s => s.Bytes).Order().ElementAt(2), samples);
    }

    private static Corpus Load(string root)
    {
        string directory = Path.Combine(root, "examples/RegionCoverage/data");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Require(Hash(File.ReadAllBytes(Path.Combine(directory, "PROTOCOL.md"))) == manifest.RootElement.GetProperty("ProtocolSha256").GetString(), "Frozen source protocol hash differs.");
        foreach (JsonElement item in manifest.RootElement.GetProperty("SourceFiles").EnumerateArray())
        {
            string name = item.GetProperty("File").GetString()!;
            Require(name.StartsWith("source/nc-", StringComparison.Ordinal) && !name.Contains(".."), "Unexpected source path.");
            byte[] data = File.ReadAllBytes(Path.Combine(directory, name));
            string hash = Hash(data);
            Require(data.Length == item.GetProperty("Bytes").GetInt32() && hash == item.GetProperty("Sha256").GetString(), "Source hash differs: " + name);
            hashes.Add(name, hash);
        }
        Require(hashes.Count == 6, "Incomplete frozen source manifest.");
        Raw[] first = Read("nc-counties.geojson"), second = Read("nc-districts.geojson");
        Require(first.Length == 100 && second.Length == 14, "Incomplete Census population.");
        Box box = Bounds(first.Concat(second).SelectMany(f => f.Rings).SelectMany(r => r));
        double ox = box.MinX * .5 + box.MaxX * .5, oy = box.MinY * .5 + box.MaxY * .5;
        first = Translate(first); second = Translate(second);
        Require(first.Sum(f => f.Rings.Length) == 104 && second.Sum(f => f.Rings.Length) == 19 &&
            first.Sum(f => f.Rings.Sum(r => r.Length)) == 7017 && second.Sum(f => f.Rings.Sum(r => r.Length)) == 5165,
            "Complete Census ring/vertex inventory changed.");
        var cases = new List<Scenario> { new("census-county-to-district", first, second, true, 285), new("census-district-to-county", second, first, true, 285) };
        foreach (int rings in new[] { 16, 64, 256, 1024 })
        foreach (bool holes in new[] { false, true })
        {
            Raw a = Generate(rings, holes, 0), b = Generate(rings, holes, .25);
            cases.Add(new($"{(holes ? "shells-with-holes" : "disjoint-squares")}-{rings}", [a], [b], false, 1,
                holes ? 1.5 * rings : rings, holes ? 13.0 / 16 * rings : 9.0 / 16 * rings));
        }
        return new(cases.ToArray(), hashes, ox, oy);

        Raw[] Read(string name)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "source", name)));
            JsonElement collection = document.RootElement;
            Require(collection.GetProperty("type").GetString() == "FeatureCollection" &&
                collection.GetProperty("crs").GetProperty("properties").GetProperty("name").GetString() == "EPSG:5070" &&
                (!collection.TryGetProperty("exceededTransferLimit", out JsonElement limited) || !limited.GetBoolean()), "Unexpected source contract.");
            var result = new List<Raw>();
            foreach (JsonElement item in collection.GetProperty("features").EnumerateArray())
            {
                JsonElement properties = item.GetProperty("properties"), geometry = item.GetProperty("geometry");
                Require(properties.GetProperty("STATE").GetString() == "37", "Unexpected Census state.");
                Point2[][] Part(JsonElement part) => part.EnumerateArray().Select(ring =>
                {
                    Point2[] points = ring.EnumerateArray().Select(p =>
                    {
                        Require(p.GetArrayLength() == 2 && double.IsFinite(p[0].GetDouble()) && double.IsFinite(p[1].GetDouble()), "Invalid source point.");
                        return new Point2(p[0].GetDouble(), p[1].GetDouble());
                    }).ToArray();
                    Require(points.Length >= 4 && points[0].X == points[^1].X && points[0].Y == points[^1].Y, "Invalid source closure.");
                    return points[..^1];
                }).ToArray();
                JsonElement coordinates = geometry.GetProperty("coordinates");
                Point2[][][] parts = geometry.GetProperty("type").GetString() switch
                {
                    "Polygon" => [Part(coordinates)], "MultiPolygon" => coordinates.EnumerateArray().Select(Part).ToArray(),
                    _ => throw new InvalidOperationException("Unexpected geometry type.")
                };
                Require(parts.Length > 0 && parts.All(p => p.Length > 0), "Empty source feature.");
                var raw = new Raw(properties.GetProperty("GEOID").GetString()!, parts);
                Require(Geometry(raw).IsValid, "Invalid original source hierarchy: " + raw.Id);
                result.Add(raw);
            }
            Require(result.Select(r => r.Id).SequenceEqual(result.Select(r => r.Id).Distinct().Order(StringComparer.Ordinal)), "Unordered/duplicate source IDs.");
            return result.ToArray();
        }
        Raw[] Translate(Raw[] source) => source.Select(raw => new Raw(raw.Id, raw.Parts.Select(part => part.Select((ring, i) =>
        {
            Point2[] points = ring.Select(p => new Point2(p.X - ox, p.Y - oy)).ToArray();
            if (Orientation.IsCCW(Close(points)) != (i == 0)) Array.Reverse(points);
            return points;
        }).ToArray()).ToArray())).ToArray();
    }

    private static Raw Generate(int rings, bool holes, double shift)
    {
        int count = holes ? rings / 2 : rings;
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        Point2[] Square(double x, double y, double size, bool reverse = false)
        {
            Point2[] result = [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];
            if (reverse) Array.Reverse(result);
            return result;
        }
        var parts = new Point2[count][][];
        for (int i = 0; i < count; i++)
        {
            double x = (i % columns) * 4 + shift, y = (i / columns) * 4 + shift;
            parts[i] = holes ? [Square(x, y, 2), Square(x + .5, y + .5, 1, true)] : [Square(x, y, 1)];
        }
        return new((holes ? "holes-" : "squares-") + rings + "-" + shift.ToString("R", System.Globalization.CultureInfo.InvariantCulture), parts);
    }
    private static Geometry Geometry(Raw raw)
    {
        Polygon[] polygons = raw.Parts.Select(p => Factory.CreatePolygon(Factory.CreateLinearRing(Close(p[0])),
            p.Skip(1).Select(h => Factory.CreateLinearRing(Close(h))).ToArray())).ToArray();
        return polygons.Length == 1 ? polygons[0] : Factory.CreateMultiPolygon(polygons);
    }
    private static Coordinate[] Close(Point2[] ring) => ring.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(ring[0].X, ring[0].Y)).ToArray();
    private static Box Bounds(IEnumerable<Point2> points)
    {
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        foreach (Point2 p in points) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        return new(minX, minY, maxX, maxY);
    }
    private static SortedDictionary<string, string> SourceHashes(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string directory in new[] { "src/PolylineKit.Core", "tests/PolylineKit.MultiRingChecks" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.TopDirectoryOnly)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".json", StringComparison.Ordinal) || p.EndsWith("PROTOCOL.md", StringComparison.Ordinal)))
            result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(File.ReadAllBytes(path)));
        return result;
    }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static void Keep(Digest value) => sink = value.IntersectionSum + value.CoverageSum + value.Candidates;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
