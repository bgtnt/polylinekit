using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.HPRtree;
using NetTopologySuite.Index.Strtree;
using PolylineKit;
using RegionCoverage;

return CoverageExperiment.Run(args);

internal static partial class CoverageExperiment
{
    private const double Scale = 1e6;
    private enum OuterIndex { Str, Packed, Hilbert }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static double sink;
    private sealed record RegionEntry(string Id, string Name, double[][] Points);
    private sealed record RegionFile(int SchemaVersion, string Role, int SpatialReference, string CoordinateUnit, RegionEntry[] Regions);
    private sealed record Shape(string Id, string Name, Point2[] Points, Polygon Polygon, bool Convex);
    private sealed record Inputs(Shape[] Counties, Shape[] Districts, double OriginX, double OriginY, string Hash);
    private sealed record Direction(string Name, Shape[] Zones, Shape[] Queries);
    private sealed record PairEvidence(string Zone, string Query, bool BoundsCandidate, bool Nested, bool Convex,
        double ReferenceIntersection, double ReferenceCoverage);
    private sealed record NumericRow(string Method, string Zone, string Query, double Intersection, double? Coverage,
        double AreaDelta, double? FractionDelta, bool WithinBudget);
    private sealed record Accuracy(string Direction, PairEvidence[] Pairs, NumericRow[] Values);
    private readonly record struct Digest(double AreaSum, double CoverageSum, int Candidates);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Measurement(string Direction, string Method, string Scope, int TablesPerInvocation, Digest Value,
        double MedianNanoseconds, double MedianBytes, Sample[] Samples);
    private sealed record FirstUse(string Direction, string Method, double Nanoseconds, long Bytes, Digest Value);
    private sealed record BenchmarkRun(int Run, string Revision, DateTimeOffset Utc, string Runtime, string OS, string Architecture,
        string? CPU, string? TieredCompilation, string InputHash, Dictionary<string, string> Assemblies,
        Accuracy[] Accuracy, FirstUse[] FirstUses, Measurement[] Measurements);

    static partial void AddPlatformComparators(List<CoverageComparator> methods);
    static partial void CreatePlatformComparator(string name, ref CoverageComparator? method);
    static partial void AddPlatformHashes(Dictionary<string, string> hashes);
    static partial void CheckPlatformComparators();

    private static CoverageComparator[] Methods()
    {
        var methods = Comparators.Create(Scale).ToList(); AddPlatformComparators(methods); return methods.ToArray();
    }

    // The targeted ablation keeps the established strongest direct Clipper baseline and
    // the unchanged full-metrics Winding operation under the same six STRtree scopes.
    private static string[] MeasuredMethods(bool intersectionOnly) => intersectionOnly
        ? ["Winding", "Winding-intersection-only", "Clipper64-reused-data"]
        : Methods().Select(m => m.Name).ToArray();

    internal static int Run(string[] args)
    {
        if (args.Length == 0) args = ["check"];
        Inputs data = Load();
        switch (args[0])
        {
            case "check" when args.Length == 1:
                Check(data); return 0;
            case "run" when args.Length == 2:
                Directory.CreateDirectory(args[1]);
                File.WriteAllText(Path.Combine(args[1], "accuracy.json"), JsonSerializer.Serialize(Check(data), Json) + "\n");
                return 0;
            case "benchmark" when args.Length == 4:
                Benchmark(data, args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3]); return 0;
            case "summarize" when args.Length == 2:
                Summarize(data, args[1]); return 0;
            case "benchmark-intersection" when args.Length == 4:
                Benchmark(data, args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3], true); return 0;
            case "summarize-intersection" when args.Length == 2:
                Summarize(data, args[1], true); return 0;
            default:
                Console.Error.WriteLine("Commands: check | run <output> | benchmark[-intersection] <output> <run:1..3> <revision> | summarize[-intersection] <output>");
                return 2;
        }
    }

    private static Inputs Load()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "data");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        RegionFile Read(string filename)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, filename));
            Require(Hash(bytes) == manifest.RootElement.GetProperty("Files").GetProperty(filename).GetProperty("Sha256").GetString(),
                "Frozen input checksum differs: " + filename);
            var file = JsonSerializer.Deserialize<RegionFile>(bytes)!;
            Require(file.SchemaVersion == 1 && file.SpatialReference == 5070 && file.CoordinateUnit == "metre", "Unexpected coordinate contract.");
            Require(file.Regions.Length > 0 && file.Regions.Select(r => r.Id).Distinct().Count() == file.Regions.Length,
                "Empty or duplicated source population.");
            return file;
        }
        var counties = Read("zones.json"); var districts = Read("queries.json");
        var all = counties.Regions.Concat(districts.Regions).SelectMany(r => r.Points).ToArray();
        Require(all.All(p => p.Length == 2 && double.IsFinite(p[0]) && double.IsFinite(p[1])), "Invalid coordinates.");
        double ox = all.Min(p => p[0]) * .5 + all.Max(p => p[0]) * .5;
        double oy = all.Min(p => p[1]) * .5 + all.Max(p => p[1]) * .5;
        Shape[] ConvertRegions(RegionFile file) => file.Regions.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r =>
        {
            var points = r.Points.Select(p => new Point2(p[0] - ox, p[1] - oy)).ToArray();
            Require(points.Length >= 3, "Too few vertices: " + r.Id);
            var coordinates = points.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(points[0].X, points[0].Y)).ToArray();
            var polygon = new GeometryFactory().CreatePolygon(coordinates);
            // Validate before any timing. Failure aborts; no repair or post-hoc filtering.
            Require(polygon.IsValid && polygon.IsSimple && polygon.Area > 0 && polygon.NumInteriorRings == 0,
                "Ineligible geometry after translation: " + r.Id);
            bool convex = polygon.EqualsTopologically(polygon.ConvexHull());
            return new Shape(r.Id, r.Name, points, polygon, convex);
        }).ToArray();
        var a = ConvertRegions(counties); var b = ConvertRegions(districts);
        string hash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            OriginX = ox, OriginY = oy,
            Counties = a.Select(s => new { s.Id, s.Points }), Districts = b.Select(s => new { s.Id, s.Points })
        })));
        return new(a, b, ox, oy, hash);
    }

    private static Direction[] Directions(Inputs data) =>
        [new("county-zones", data.Counties, data.Districts), new("district-zones", data.Districts, data.Counties)];

    private static RegionBounds Bounds(Shape s)
    {
        var e = s.Polygon.EnvelopeInternal; return new(e.MinX, e.MinY, e.MaxX, e.MaxY);
    }
    private static Envelope Envelope(RegionBounds b) => new(b.MinX, b.MaxX, b.MinY, b.MaxY);

    private static Accuracy[] Check(Inputs data)
    {
        ComparatorChecks.Run();
        PackedBoundsIndexChecks.Run();
        CheckPlatformComparators();
        var output = new List<Accuracy>();
        foreach (Direction direction in Directions(data))
        {
            var pairs = new List<PairEvidence>();
            foreach (Shape zone in direction.Zones)
            foreach (Shape query in direction.Queries)
            {
                bool candidate = !Bounds(zone).Disjoint(Bounds(query));
                double intersection = candidate ? zone.Polygon.Intersection(query.Polygon).Area : 0;
                pairs.Add(new(zone.Id, query.Id, candidate,
                    candidate && (zone.Polygon.Covers(query.Polygon) || query.Polygon.Covers(zone.Polygon)),
                    zone.Convex || query.Convex, intersection, intersection / zone.Polygon.Area));
            }
            var values = new List<NumericRow>();
            foreach (var method in Methods())
            {
                var prepared = Prepare(direction, method);
                int index = 0, failed = 0;
                foreach (var zone in prepared.Zones)
                foreach (var query in prepared.Queries)
                {
                    var reference = pairs[index++]; var value = method.Compare(zone, query);
                    Require(!zone.Bounds.Disjoint(query.Bounds) == reference.BoundsCandidate,
                        "Prepared bounds changed a candidate pair: " + reference.Zone + "/" + reference.Query);
                    double areaDelta = Math.Abs(value.Intersection - reference.ReferenceIntersection);
                    double? fractionDelta = value.Coverage.HasValue ? Math.Abs(value.Coverage.Value - reference.ReferenceCoverage) : null;
                    bool pass = double.IsFinite(areaDelta) && areaDelta <= 1 && fractionDelta.HasValue &&
                        double.IsFinite(fractionDelta.Value) && fractionDelta <= 1e-8;
                    if (!pass) failed++;
                    values.Add(new(method.Name, reference.Zone, reference.Query, value.Intersection, value.Coverage, areaDelta, fractionDelta, pass));
                }
                // WPF's documented graphics precision is a separate comparison contract.
                // Record every disagreement; never clamp, repair, or omit a difficult pair.
                Require(failed == 0 || method.Name.StartsWith("wpf-", StringComparison.Ordinal),
                    $"{method.Name}: {failed} pairs fail the predeclared area/coverage agreement budget.");
                prepared.PrepareCandidates();
                Digest linear = prepared.Linear(), indexed = prepared.Indexed(), candidates = prepared.Candidates();
                var packed = Prepare(direction, Fresh(method.Name), OuterIndex.Packed);
                var hilbert = Prepare(direction, Fresh(method.Name), OuterIndex.Hilbert);
                prepared.VerifyIndex(); packed.VerifyIndex(); hilbert.VerifyIndex();
                Require(linear.Candidates == pairs.Count(p => p.BoundsCandidate), "Prepared bounds changed candidate population.");
                Require(SameDigest(linear, indexed) && SameDigest(linear, candidates) &&
                    SameDigest(linear, packed.Indexed()) && SameDigest(linear, hilbert.Indexed()),
                    "Query index changed results.");
                Console.WriteLine($"{direction.Name}/{method.Name}: {pairs.Count} pairs, {failed} outside numerical budget.");
            }
            var fullValues = values.Where(v => v.Method == "Winding").ToDictionary(v => (v.Zone, v.Query));
            foreach (var narrow in values.Where(v => v.Method == "Winding-intersection-only"))
            {
                var full = fullValues[(narrow.Zone, narrow.Query)];
                Require(narrow.Intersection == full.Intersection && narrow.Coverage == full.Coverage,
                    "Intersection-only changed the full operation's area/coverage value: " + narrow.Zone + "/" + narrow.Query);
            }
            output.Add(new(direction.Name, pairs.ToArray(), values.ToArray()));
            Console.WriteLine($"{direction.Name}: {pairs.Count(p => p.BoundsCandidate)} bbox candidates, " +
                $"{pairs.Count(p => p.BoundsCandidate && p.Nested)} nested, {pairs.Count(p => p.BoundsCandidate && p.Convex)} convex-eligible.");
        }
        return output.ToArray();
    }

    private sealed class QueryCollector(int capacity) : IItemVisitor<int>
    {
        internal readonly int[] Items = new int[capacity];
        internal int Count;
        public void VisitItem(int item) => Items[Count++] = item;
    }

    private sealed class PreparedBatch
    {
        internal readonly CoverageComparator Method;
        internal readonly PreparedRegion[] Zones, Queries;
        private readonly ISpatialIndex<int>? index;
        private readonly PackedBoundsIndex? packed;
        private readonly Envelope[]? zoneBounds;
        private readonly QueryCollector collector;
        private (int Zone, int Query)[]? pairs;

        internal PreparedBatch(Direction direction, CoverageComparator method, OuterIndex kind)
        {
            Method = method;
            Zones = direction.Zones.Select(s => method.Prepare(s.Points, true)).ToArray();
            Queries = direction.Queries.Select(s => method.Prepare(s.Points, false)).ToArray();
            collector = new(Queries.Length);
            if (kind == OuterIndex.Packed) packed = new(Queries.Select(q => q.Bounds).ToArray());
            else
            {
                index = kind == OuterIndex.Hilbert ? new HPRtree<int>() : new STRtree<int>();
                zoneBounds = Zones.Select(s => Envelope(s.Bounds)).ToArray();
                for (int i = 0; i < Queries.Length; i++) index.Insert(Envelope(Queries[i].Bounds), i);
                if (index is HPRtree<int> hpr) hpr.Build(); else ((STRtree<int>)index).Build();
            }
        }

        // Benchmark-only inventory; real indexed sessions never build an all-pairs list.
        internal void PrepareCandidates()
        {
            var candidates = new List<(int, int)>();
            for (int a = 0; a < Zones.Length; a++)
            for (int b = 0; b < Queries.Length; b++)
                if (!Zones[a].Bounds.Disjoint(Queries[b].Bounds)) candidates.Add((a, b));
            pairs = candidates.ToArray();
        }

        internal Digest Candidates()
        {
            double area = 0, coverage = 0;
            var candidates = pairs ?? throw new InvalidOperationException("Prepare candidate inventory outside timing.");
            foreach (var (a, b) in candidates)
            {
                var value = Method.Compare(Zones[a], Queries[b]); area += value.Intersection; coverage += value.Coverage.GetValueOrDefault();
            }
            return new(area, coverage, candidates.Length);
        }

        internal Digest Linear()
        {
            double area = 0, coverage = 0; int count = 0;
            foreach (var zone in Zones)
            foreach (var query in Queries)
            {
                if (zone.Bounds.Disjoint(query.Bounds)) continue;
                var value = Method.Compare(zone, query); area += value.Intersection; coverage += value.Coverage.GetValueOrDefault(); count++;
            }
            return new(area, coverage, count);
        }

        internal Digest Indexed()
        {
            double area = 0, coverage = 0; int count = 0;
            for (int i = 0; i < Zones.Length; i++)
            {
                FindCandidates(i);
                for (int k = 0; k < collector.Count; k++)
                {
                    var value = Method.Compare(Zones[i], Queries[collector.Items[k]]);
                    area += value.Intersection; coverage += value.Coverage.GetValueOrDefault(); count++;
                }
            }
            return new(area, coverage, count);
        }

        private void FindCandidates(int zone)
        {
            if (packed is not null) collector.Count = packed.Query(Zones[zone].Bounds, collector.Items);
            else { collector.Count = 0; index!.Query(zoneBounds![zone], collector); }
        }

        internal void VerifyIndex()
        {
            for (int i = 0; i < Zones.Length; i++)
            {
                FindCandidates(i);
                int[] expected = Enumerable.Range(0, Queries.Length)
                    .Where(j => !Zones[i].Bounds.Disjoint(Queries[j].Bounds)).ToArray();
                int[] actual = collector.Items.AsSpan(0, collector.Count).ToArray();
                Array.Sort(actual);
                Require(expected.SequenceEqual(actual), "Index changed candidate IDs.");
            }
        }

        internal Digest PreparationValue() => new(Zones.Sum(z => z.Area), Queries.Sum(q => q.Area), 0);
    }

    private static PreparedBatch Prepare(Direction direction, CoverageComparator method, OuterIndex kind = OuterIndex.Str) => new(direction, method, kind);
    private static CoverageComparator Fresh(string name)
    {
        CoverageComparator? method = Comparators.Create(name, Scale);
        if (method is null) CreatePlatformComparator(name, ref method);
        return method ?? throw new ArgumentException("Unknown method: " + name);
    }
    private static Digest Session(Direction direction, string method, int tables, OuterIndex kind = OuterIndex.Str)
    {
        var prepared = Prepare(direction, Fresh(method), kind); var value = default(Digest);
        for (int i = 0; i < tables; i++) { value = prepared.Indexed(); Keep(value); }
        return value;
    }

    private static Measurement Measure(string direction, string method, string scope, int tables, Func<Digest> invoke)
    {
        Digest value = invoke(); var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 50) Keep(invoke());
        int iterations = 1;
        while (true)
        {
            watch.Restart(); for (int i = 0; i < iterations; i++) Keep(invoke());
            if (watch.ElapsedMilliseconds >= 20 || iterations >= 16384) break;
            iterations *= 2;
        }
        var samples = new Sample[5];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) Keep(invoke());
            long elapsed = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations / tables, (double)bytes / iterations / tables);
        }
        Require(value == invoke(), "Unstable benchmark output.");
        return new(direction, method, scope, tables, value, samples.Select(s => s.Nanoseconds).Order().ElementAt(2),
            samples.Select(s => s.Bytes).Order().ElementAt(2), samples);
    }

    private static Dictionary<string, string> AssemblyHashes()
    {
        var hashes = new[] { typeof(CoverageExperiment).Assembly, typeof(WindingArea).Assembly,
            typeof(Clipper2Lib.Clipper).Assembly, typeof(Polygon).Assembly }
            .ToDictionary(a => a.GetName().Name!, a => Hash(File.ReadAllBytes(a.Location)));
        AddPlatformHashes(hashes); return hashes;
    }

    private static void Benchmark(Inputs data, string directory, int run, string revision, bool intersectionOnly = false)
    {
        if (run is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(run));
        Accuracy[] accuracy = Check(data);
        var rows = new List<Measurement>(); var firstUses = new List<FirstUse>();
        foreach (Direction direction in Directions(data))
        {
            string[] names = MeasuredMethods(intersectionOnly);
            foreach (string name in names.Skip(run - 1).Concat(names.Take(run - 1)))
            {
                var prepared = Prepare(direction, Fresh(name));
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                Digest initial = prepared.Indexed();
                long elapsed = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                firstUses.Add(new(direction.Name, name, elapsed * 1e9 / Stopwatch.Frequency, bytes, initial));
                prepared.PrepareCandidates();
                rows.Add(Measure(direction.Name, name, "prepare-catalogues-and-index", 1, () => Prepare(direction, Fresh(name)).PreparationValue()));
                rows.Add(Measure(direction.Name, name, "warm-candidate-geometry", 1, prepared.Candidates));
                rows.Add(Measure(direction.Name, name, "warm-linear-table", 1, prepared.Linear));
                rows.Add(Measure(direction.Name, name, "warm-indexed-table", 1, prepared.Indexed));
                rows.Add(Measure(direction.Name, name, "prepare-plus-one-table", 1, () => Session(direction, name, 1)));
                rows.Add(Measure(direction.Name, name, "prepare-plus-eight-tables", 8, () => Session(direction, name, 8)));
                if (intersectionOnly)
                {
                    Console.WriteLine($"Run {run}: {direction.Name}/{name} complete.");
                    continue;
                }
                foreach (var (label, kind) in new[] { ("packed", OuterIndex.Packed), ("hilbert", OuterIndex.Hilbert) })
                {
                    var alternative = Prepare(direction, Fresh(name), kind);
                    rows.Add(Measure(direction.Name, name, $"prepare-{label}-catalogues-and-index", 1, () => Prepare(direction, Fresh(name), kind).PreparationValue()));
                    rows.Add(Measure(direction.Name, name, $"warm-{label}-table", 1, alternative.Indexed));
                    rows.Add(Measure(direction.Name, name, $"prepare-{label}-plus-one-table", 1, () => Session(direction, name, 1, kind)));
                    rows.Add(Measure(direction.Name, name, $"prepare-{label}-plus-eight-tables", 8, () => Session(direction, name, 8, kind)));
                }
                Console.WriteLine($"Run {run}: {direction.Name}/{name} complete.");
            }
        }
        var record = new BenchmarkRun(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            data.Hash, AssemblyHashes(), accuracy, firstUses.ToArray(), rows.ToArray());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(record, Json) + "\n");
        GC.KeepAlive(sink);
    }

    private static void Summarize(Inputs data, string directory, bool intersectionOnly = false)
    {
        var runs = Enumerable.Range(1, 3).Select(i => JsonSerializer.Deserialize<BenchmarkRun>(
            File.ReadAllText(Path.Combine(directory, $"run-{i}.json")))!).ToArray();
        string Identity(BenchmarkRun r) => JsonSerializer.Serialize(new { r.Revision, r.Runtime, r.OS, r.Architecture,
            r.CPU, r.TieredCompilation, r.InputHash, r.Assemblies, r.Accuracy });
        Require(runs[0].InputHash == data.Hash && JsonSerializer.Serialize(runs[0].Assemblies) == JsonSerializer.Serialize(AssemblyHashes()),
            "Summarize with the measured inputs and assemblies.");
        var maps = runs.Select(r => r.Measurements.ToDictionary(m => (m.Direction, m.Method, m.Scope))).ToArray();
        var expected = new Dictionary<(string Direction, string Method, string Scope), (int Tables, Digest Value)>();
        var expectedFirstUses = new Dictionary<(string Direction, string Method), Digest>();
        foreach (Direction direction in Directions(data))
        foreach (string name in MeasuredMethods(intersectionOnly))
        {
            var prepared = Prepare(direction, Fresh(name));
            Digest preparation = prepared.PreparationValue(), indexed = prepared.Indexed(), linear = prepared.Linear();
            prepared.PrepareCandidates();
            expectedFirstUses.Add((direction.Name, name), indexed);
            expected.Add((direction.Name, name, "prepare-catalogues-and-index"), (1, preparation));
            expected.Add((direction.Name, name, "warm-candidate-geometry"), (1, prepared.Candidates()));
            expected.Add((direction.Name, name, "warm-linear-table"), (1, linear));
            expected.Add((direction.Name, name, "warm-indexed-table"), (1, indexed));
            expected.Add((direction.Name, name, "prepare-plus-one-table"), (1, indexed));
            expected.Add((direction.Name, name, "prepare-plus-eight-tables"), (8, indexed));
            if (intersectionOnly) continue;
            foreach (var (label, kind) in new[] { ("packed", OuterIndex.Packed), ("hilbert", OuterIndex.Hilbert) })
            {
                var alternative = Prepare(direction, Fresh(name), kind);
                Digest value = alternative.Indexed();
                expected.Add((direction.Name, name, $"prepare-{label}-catalogues-and-index"), (1, alternative.PreparationValue()));
                expected.Add((direction.Name, name, $"warm-{label}-table"), (1, value));
                expected.Add((direction.Name, name, $"prepare-{label}-plus-one-table"), (1, value));
                expected.Add((direction.Name, name, $"prepare-{label}-plus-eight-tables"), (8, value));
            }
        }
        for (int i = 0; i < runs.Length; i++)
        {
            Require(runs[i].Run == i + 1 && Identity(runs[i]) == Identity(runs[0]) &&
                maps[i].Keys.ToHashSet().SetEquals(expected.Keys), "Mismatched independent runs or matrix.");
            var firstUses = runs[i].FirstUses.ToDictionary(f => (f.Direction, f.Method));
            Require(firstUses.Keys.ToHashSet().SetEquals(expectedFirstUses.Keys) && firstUses.All(p =>
                p.Value.Value == expectedFirstUses[p.Key] && double.IsFinite(p.Value.Nanoseconds) &&
                p.Value.Nanoseconds > 0 && p.Value.Bytes >= 0), "Invalid first-use records.");
            foreach (var row in runs[i].Measurements)
            {
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) &&
                    s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid timed samples.");
                Require(row.MedianNanoseconds == row.Samples.Select(s => s.Nanoseconds).Order().ElementAt(2) &&
                    row.MedianBytes == row.Samples.Select(s => s.Bytes).Order().ElementAt(2) &&
                    row.Value == expected[(row.Direction, row.Method, row.Scope)].Value &&
                    row.TablesPerInvocation == expected[(row.Direction, row.Method, row.Scope)].Tables,
                    "Numeric value, scope normalization or median differs.");
            }
        }
        Require(JsonSerializer.Serialize(Check(data)) == JsonSerializer.Serialize(runs[0].Accuracy), "Current numerical evidence differs.");
        var report = new StringBuilder("# Prepared region coverage results\n\n");
        report.AppendLine($"Source `{runs[0].Revision}`; {runs[0].Utc:yyyy-MM-dd} UTC; {runs[0].Runtime}; {runs[0].OS}; {runs[0].CPU}. Three fresh sequential processes, five calibrated batches each. Tiered compilation `{runs[0].TieredCompilation}`; SDK and commands are recorded separately.\n");
        if (intersectionOnly)
        {
            var gates = (from direction in Directions(data)
                from scope in new[] { "warm-indexed-table", "prepare-plus-one-table" }
                let clipper = maps.Select(m => m[(direction.Name, "Clipper64-reused-data", scope)].MedianNanoseconds).Order().ElementAt(1)
                let narrow = maps.Select(m => m[(direction.Name, "Winding-intersection-only", scope)].MedianNanoseconds).Order().ElementAt(1)
                select new { Direction = direction.Name, Scope = scope, ClipperNanoseconds = clipper,
                    IntersectionNanoseconds = narrow, Ratio = clipper / narrow, Passed = clipper / narrow >= 1.25 }).ToArray();
            bool passed = gates.All(g => g.Passed);
            File.WriteAllText(Path.Combine(directory, "decision.json"), JsonSerializer.Serialize(new
                { Revision = runs[0].Revision, MinimumRatio = 1.25, Passed = passed, Cells = gates }, Json) + "\n");
            report.AppendLine($"Intersection-only predeclared gate: **{(passed ? "PASS" : "FAIL")}**. Every indexed/fresh-one-table ratio must be at least 1.25; accuracy is verified independently above. This is a local engineering gate, not statistical significance.\n");
            report.AppendLine("| Direction | Scope | Clipper / intersection-only | Pass |\n|---|---|---:|---|");
            foreach (var gate in gates)
                report.AppendLine(FormattableString.Invariant($"| {gate.Direction} | {gate.Scope} | {gate.Ratio:F3} | {gate.Passed} |"));
            report.AppendLine();
        }
        report.AppendLine(FormattableString.Invariant($"{data.Counties.Length} complete county rings, {data.Districts.Length} complete district rings. Common translated origin = ({data.OriginX:R}, {data.OriginY:R}) in EPSG:5070 metres. No per-shape alignment or simplification. See data/PROTOCOL.md for predeclared selection, exclusions and agreement budgets.\n"));
        foreach (var accuracy in runs[0].Accuracy)
        {
            report.AppendLine($"## {accuracy.Direction}: numeric contract\n\n{accuracy.Pairs.Length} pairs; {accuracy.Pairs.Count(p => p.BoundsCandidate)} bbox candidates; {accuracy.Pairs.Count(p => p.BoundsCandidate && p.Nested)} nested; {accuracy.Pairs.Count(p => p.BoundsCandidate && p.Convex)} convex-eligible. No difficult pairs omitted.\n");
            report.AppendLine("| Method | Max area disagreement m² | Max coverage disagreement | Outside budget |\n|---|---:|---:|---:|");
            foreach (var group in accuracy.Values.GroupBy(r => r.Method))
                report.AppendLine(FormattableString.Invariant($"| {group.Key} | {group.Max(r => r.AreaDelta):G8} | {group.Max(r => r.FractionDelta):G8} | {group.Count(r => !r.WithinBudget)} / {group.Count()} |"));
        }
        foreach (string direction in Directions(data).Select(d => d.Name))
        foreach (string scope in runs[0].Measurements.Select(m => m.Scope).Distinct())
        {
            report.AppendLine($"\n## {direction}: {scope}\n\n| Method | ms per table/preparation (min–max) | Managed B per table/preparation |\n|---|---:|---:|");
            foreach (string method in MeasuredMethods(intersectionOnly))
            {
                var values = maps.Select(m => m[(direction, method, scope)]).ToArray();
                report.AppendLine(FormattableString.Invariant($"| {method} | {values.Select(v => v.MedianNanoseconds).Order().ElementAt(1)/1e6:F4} ({values.Min(v => v.MedianNanoseconds)/1e6:F4}–{values.Max(v => v.MedianNanoseconds)/1e6:F4}) | {values.Max(v => v.MedianBytes):F0} |"));
            }
        }
        report.AppendLine("\nAll query times consume both quantities: intersection and coverage of the first region. Candidate rows run the entire candidate batch, not one pair. Indexed and linear rows traverse the complete pair population and accumulate a digest, without allocating an output matrix; zero rejected pairs need no overlay. Preparation includes both input catalogues, comparator buffers, own areas and the outer STRtree. Only the warm candidate microbenchmark precomputes its pair inventory, outside timing. The eight-table scope divides total time and bytes by eight. Lazy prepared-object indexes are charged when first queried; first-table-after-preparation records are retained separately in raw JSON. The process/JIT and Winding's thread-local workspace are already warm, so this is not cold-process timing. No intersection results are cached.\n");
        report.AppendLine("Unqualified indexed scopes use NTS STRtree (10); packed scopes use the example's independently written static 32-way bounds index; hilbert scopes use NTS HPRtree (16). Each preparation/session builds only its selected index. All preserve the same inclusive-AABB candidate IDs. This outer region index does not replace Winding's internal edge-pair traversal.\n");
        report.AppendLine("Times are medians of process medians, with their range; no significance claim. Managed allocation counts do not include native WPF allocations or retained memory. WPF rows outside the declared agreement budget are graphical-precision tradeoffs, not equal-quality speedups. NTS agreement is not exact arithmetic. ArcGIS/QGIS application workflows were not timed. See README.md for competitor scope and numerical contracts.");
        File.WriteAllText(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Validated {runs.Sum(r => r.Measurements.Sum(m => m.Samples.Length))} timing samples.");
    }

    private static bool SameDigest(Digest a, Digest b) => a.Candidates == b.Candidates &&
        Math.Abs(a.AreaSum - b.AreaSum) <= 1e-12 * Math.Max(1, Math.Abs(a.AreaSum)) &&
        Math.Abs(a.CoverageSum - b.CoverageSum) <= 1e-12 * Math.Max(1, Math.Abs(a.CoverageSum));
    private static void Keep(Digest value) => sink = value.AreaSum + value.CoverageSum + value.Candidates;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}
