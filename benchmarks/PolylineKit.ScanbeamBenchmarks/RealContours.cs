using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Valid;
using PolylineKit;
using RegionCoverage;

namespace PolylineKit.ScanbeamBenchmarks;

internal static partial class Program
{
    private static readonly string[] RealMethods = ["Winding-intersection-only", "IntegerScanbeam-intersection", "Clipper64-reused-data"];
    private static readonly string[] RealDirections = ["county-zones", "district-zones"];
    private static readonly string[] RealScopes = ["prepare", "warm-table", "prepare-plus-one"];
    private sealed record RealFeature(string Id, bool County, Point2[] Points);
    private sealed record RealSource(RealFeature[] Features, Point2 Origin, string SourceHash);
    private sealed record RecordedOrigin(double X, double Y);
    private sealed record RealSession(CoverageComparator Comparator, PreparedRegion[] Zones, PreparedRegion[] Queries);
    private sealed record RealRow(string Direction, string Method, string Scope, double Value, double MedianNs, double MedianBytes, Sample[] Samples);
    private sealed record RealRun(int Run, string Revision, DateTimeOffset Utc, string SourceHash, RecordedOrigin Origin,
        string Runtime, string OS, string Architecture, string? Cpu, int LogicalProcessors, string? TieredCompilation,
        string HarnessHash, string WindingHash, string ClipperHash, string NtsHash, RealRow[] Rows);
    private sealed record RealAggregate(string Direction, string Method, string Scope, double Value, double MedianNs,
        double MinNs, double MaxNs, double MedianBytes);
    private sealed record RealPair(string Direction, string Zone, string Query, double OriginalIntersection, double RoundedIntersection,
        double OriginalCoverage, double RoundedCoverage, double Winding, double Scanbeam, double Clipper);
    private sealed record RealValidation(string SourceHash, Point2 Origin, int Features, int Vertices, int CollapsedEdges,
        string[] InvalidOriginal, string[] InvalidRounded, string[] NonSimpleOriginal, string[] NonSimpleRounded,
        int OriginalEquivalenceFailures, int BackendFailures,
        double MaxRoundingDisplacement, double MaxOwnAreaChange, double MaxIntersectionChange, double MaxCoverageChange,
        double MaxWindingError, double MaxScanbeamError, double MaxClipperError, int RoundedCandidatesPerDirection,
        RealPair[] Pairs);

    private sealed class ScanbeamComparator : CoverageComparator
    {
        private readonly IntegerScanbeam engine = new();
        internal override string Name => "IntegerScanbeam-intersection";
        internal override PreparedRegion Prepare(Point2[] points, bool zone) =>
            new(points, RegionPreparation.Bounds(points), Math.Abs(RegionPreparation.SignedArea(points)));
        protected override double Intersection(PreparedRegion zone, PreparedRegion query) =>
            engine.MeasureIntersection(zone.Points, query.Points);
    }

    private static RealSource LoadReal()
    {
        var features = new List<RealFeature>();
        var hashes = new List<string>();
        foreach (string file in new[] { "zones.json", "queries.json" })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", file));
            string hash = Hash(bytes);
            Require(hash == (file == "zones.json" ? "072435edead13272c0ccb85166d5c34d13d9805db6497db43e74ede59c920aa3" :
                "677a8b54de744e20cf80046f45c865a1e750e62ac2612e95371530ffa249a648"), "Frozen source hash differs.");
            hashes.Add(hash);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            Require(root.GetProperty("SpatialReference").GetInt32() == 5070 && root.GetProperty("CoordinateUnit").GetString() == "metre", "Unexpected source units.");
            foreach (var feature in root.GetProperty("Regions").EnumerateArray())
            {
                Point2[] points = feature.GetProperty("Points").EnumerateArray().Select(p => new Point2(p[0].GetDouble(), p[1].GetDouble())).ToArray();
                features.Add(new(feature.GetProperty("Id").GetString()!, file == "zones.json", points));
            }
        }
        Require(features.Count(f => f.County) == 98 && features.Count(f => !f.County) == 11 && features.Sum(f => f.Points.Length) == 10588, "Frozen population differs.");
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        foreach (Point2 p in features.SelectMany(f => f.Points))
        {
            double x = RoundMetre(p.X), y = RoundMetre(p.Y);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        Point2 origin = new(Math.Floor(minX + (maxX - minX) / 2), Math.Floor(minY + (maxY - minY) / 2));
        return new(features.ToArray(), origin, Hash(Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
    }

    private static double RoundMetre(double value)
    {
        Require(double.IsFinite(value) && Math.Abs(value) < 4_503_599_627_370_496, "World coordinate outside exact integer conversion range.");
        return Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static Point2[] Local(RealFeature feature, Point2 origin, bool rounded) => feature.Points.Select(p =>
        new Point2((rounded ? RoundMetre(p.X) : p.X) - origin.X, (rounded ? RoundMetre(p.Y) : p.Y) - origin.Y)).ToArray();

    private static RealSession PrepareReal(RealSource source, string direction, string method)
    {
        CoverageComparator comparator = method == "IntegerScanbeam-intersection" ? new ScanbeamComparator() : Comparators.Create(method, 1e6)!;
        Require(comparator is not null, "Unknown real comparator.");
        bool counties = direction == "county-zones";
        var zones = new List<PreparedRegion>(); var queries = new List<PreparedRegion>();
        foreach (RealFeature feature in source.Features)
        {
            bool zone = feature.County == counties;
            PreparedRegion region = comparator!.Prepare(Local(feature, source.Origin, true), zone);
            (zone ? zones : queries).Add(region);
        }
        return new(comparator!, zones.ToArray(), queries.ToArray());
    }

    private static double PreparationValue(RealSession session) => session.Zones.Sum(z => z.Area) + session.Queries.Sum(q => q.Area);
    private static double TraverseReal(RealSession session)
    {
        double digest = 0;
        foreach (PreparedRegion zone in session.Zones)
        foreach (PreparedRegion query in session.Queries)
        {
            CoverageValue value = session.Comparator.Compare(zone, query);
            digest += value.Intersection + value.Coverage!.Value;
        }
        return digest;
    }

    private static double Intersection(Polygon a, Polygon b) => a.EnvelopeInternal.Intersects(b.EnvelopeInternal)
        ? OverlayNGRobust.Overlay(a, b, SpatialFunction.Intersection).Area : 0;

    private static double IntegerArea(Point2[] points)
    {
        Int128 twice = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Point2 a = points[i], b = points[(i + 1) % points.Length];
            twice += (Int128)(long)a.X * (long)b.Y - (Int128)(long)a.Y * (long)b.X;
        }
        return (double)Int128.Abs(twice) / 2;
    }

    private static RealValidation ValidateReal(RealSource source, string? directory)
    {
        var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
        static bool Admitted(Polygon polygon) => polygon.IsValid && polygon.Area > 0;
        Require(!Admitted(RegionPreparation.Polygon(factory,
            [new(0, 0), new(2, 2), new(0, 2), new(2, 0)])), "A crossing bowtie must fail polygon admission.");
        Require(!Admitted(RegionPreparation.Polygon(factory,
            [new(0, 0), new(1, 0), new(2, 0)])), "A zero-area polygon must fail admission.");
        var originals = new Dictionary<string, Polygon>(); var rounded = new Dictionary<string, Polygon>();
        var invalidOriginal = new List<string>(); var invalidRounded = new List<string>();
        var nonSimpleOriginal = new List<string>(); var nonSimpleRounded = new List<string>();
        var featureEvidence = new List<object>();
        int collapsed = 0;
        double maxDisplacement = 0, maxOwn = 0;
        foreach (RealFeature feature in source.Features)
        {
            Point2[] raw = Local(feature, source.Origin, false), grid = Local(feature, source.Origin, true);
            Polygon a = RegionPreparation.Polygon(factory, raw), b = RegionPreparation.Polygon(factory, grid);
            originals.Add(feature.Id, a); rounded.Add(feature.Id, b);
            // Polygon validity and boundary simplicity are different NTS predicates. Retained
            // consecutive duplicate vertices can fail IsSimple without invalidating the region.
            bool validA = Admitted(a), validB = Admitted(b);
            if (!validA) invalidOriginal.Add(feature.Id);
            if (!validB) invalidRounded.Add(feature.Id);
            if (!a.IsSimple) nonSimpleOriginal.Add(feature.Id);
            if (!b.IsSimple) nonSimpleRounded.Add(feature.Id);
            int duplicates = 0;
            double displacement = 0;
            for (int i = 0; i < grid.Length; i++)
            {
                Point2 p = grid[i], q = grid[(i + 1) % grid.Length];
                if (p.X == q.X && p.Y == q.Y) duplicates++;
                displacement = Math.Max(displacement, Math.Sqrt(Math.Pow(p.X - raw[i].X, 2) + Math.Pow(p.Y - raw[i].Y, 2)));
                Require(Math.Abs(p.X) <= 524288 && Math.Abs(p.Y) <= 524288, "Frozen contour outside expanded domain.");
            }
            double exact = IntegerArea(grid);
            if (feature.Id == "37113")
            {
                Require(validA && a.IsSimple && validB && !b.IsSimple && duplicates == 1 &&
                    new IsValidOp(b).ValidationError is null && grid[18].X == grid[19].X && grid[18].Y == grid[19].Y &&
                    grid[18].X + source.Origin.X == 1129830 && grid[18].Y + source.Origin.Y == 1400586,
                    "Frozen Macon duplicate-edge validity/simplicity distinction changed.");
            }
            if (validB)
            {
                Require(Math.Abs(b.Area - exact) <= 1, "Rounded NTS own area disagrees with exact shoelace.");
                foreach (PathFillRule rule in Enum.GetValues<PathFillRule>())
                    Require(Math.Abs(new IntegerScanbeam().Measure(grid, rule) - exact) <= 1, "Scanbeam own area disagrees with exact shoelace.");
            }
            collapsed += duplicates; maxDisplacement = Math.Max(maxDisplacement, displacement);
            maxOwn = Math.Max(maxOwn, Math.Abs(b.Area - a.Area));
            featureEvidence.Add(new { feature.Id, feature.County, Vertices = grid.Length, OriginalArea = a.Area, RoundedArea = b.Area,
                ExactRoundedArea = exact, OriginalValid = validA, RoundedValid = validB,
                OriginalSimple = a.IsSimple, RoundedSimple = b.IsSimple,
                OriginalValidityError = new IsValidOp(a).ValidationError?.ToString(),
                RoundedValidityError = new IsValidOp(b).ValidationError?.ToString(),
                CollapsedEdges = duplicates, MaxDisplacement = displacement });
        }
        if (directory is not null) Directory.CreateDirectory(directory);
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "features.json"), JsonSerializer.Serialize(featureEvidence, Json) + "\n");
        if (invalidOriginal.Count != 0 || invalidRounded.Count != 0)
            throw new InvalidDataException("Invalid real polygons; evidence records all IDs. No repair or filtering is permitted.");
        var pairs = new List<RealPair>();
        int equivalenceFailures = 0, backendFailures = 0, candidates = 0;
        double maxI = 0, maxC = 0, maxW = 0, maxS = 0, maxL = 0;
        foreach (string direction in RealDirections)
        {
            RealSession[] methods = RealMethods.Select(m => PrepareReal(source, direction, m)).ToArray();
            RealFeature[] z = source.Features.Where(f => f.County == (direction == "county-zones")).ToArray();
            RealFeature[] q = source.Features.Where(f => f.County != (direction == "county-zones")).ToArray();
            for (int i = 0; i < z.Length; i++)
            for (int j = 0; j < q.Length; j++)
            {
                Polygon oa = originals[z[i].Id], ob = originals[q[j].Id], a = rounded[z[i].Id], b = rounded[q[j].Id];
                double original = Intersection(oa, ob), expected = Intersection(a, b);
                double originalCoverage = original / oa.Area, roundedCoverage = expected / a.Area;
                double di = Math.Abs(original - expected), dc = Math.Abs(originalCoverage - roundedCoverage);
                maxI = Math.Max(maxI, di); maxC = Math.Max(maxC, dc);
                if (di > 1 || dc > 1e-8) equivalenceFailures++;
                if (a.EnvelopeInternal.Intersects(b.EnvelopeInternal)) candidates++;
                var values = methods.Select(m => m.Comparator.Compare(m.Zones[i], m.Queries[j])).ToArray();
                foreach (CoverageValue value in values)
                    if (!double.IsFinite(value.Intersection) || value.Intersection < 0 || !double.IsFinite(value.Coverage!.Value) ||
                        Math.Abs(value.Intersection - expected) > 1 || Math.Abs(value.Coverage.Value - roundedCoverage) > 1e-8) backendFailures++;
                maxW = Math.Max(maxW, Math.Abs(values[0].Intersection - expected));
                maxS = Math.Max(maxS, Math.Abs(values[1].Intersection - expected));
                maxL = Math.Max(maxL, Math.Abs(values[2].Intersection - expected));
                pairs.Add(new(direction, z[i].Id, q[j].Id, original, expected, originalCoverage, roundedCoverage,
                    values[0].Intersection, values[1].Intersection, values[2].Intersection));
            }
        }
        var result = new RealValidation(source.SourceHash, source.Origin, 109, 10588, collapsed,
            invalidOriginal.ToArray(), invalidRounded.ToArray(), nonSimpleOriginal.ToArray(), nonSimpleRounded.ToArray(),
            equivalenceFailures, backendFailures, maxDisplacement, maxOwn,
            maxI, maxC, maxW, maxS, maxL, candidates / 2, pairs.ToArray());
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(result, Json) + "\n");
        Require(backendFailures == 0, $"Rounded-input comparison failures: {backendFailures}.");
        Console.WriteLine($"Real contours: 109 valid rings, {collapsed} collapsed edges retained, {pairs.Count} directional pairs; backend failures {backendFailures}; original-equivalence failures {equivalenceFailures}.");
        return result;
    }

    private static void RunReal(string directory, int run, string revision)
    {
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Use run1..3 and tiering0.");
        RealSource source = LoadReal();
        ValidateReal(source, directory);
        var rows = new List<RealRow>();
        int offset = run - 1;
        foreach (string direction in RealDirections)
        foreach (string method in RealMethods.Skip(offset).Concat(RealMethods.Take(offset)))
        {
            RealSession prepared = PrepareReal(source, direction, method);
            foreach (string scope in RealScopes)
            {
                Func<double> operation = scope switch
                {
                    "prepare" => () => PreparationValue(PrepareReal(source, direction, method)),
                    "warm-table" => () => TraverseReal(prepared),
                    _ => () => TraverseReal(PrepareReal(source, direction, method))
                };
                double value = operation();
                Sample[] samples = Measure(operation);
                Require(value == operation(), "Unstable real result digest.");
                rows.Add(new(direction, method, scope, value, Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
                Console.WriteLine($"real run{run}: {direction}/{method}/{scope}");
            }
        }
        var record = new RealRun(run, revision, DateTimeOffset.UtcNow, source.SourceHash, new(source.Origin.X, source.Origin.Y),
            RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount, Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            AssemblyHash(typeof(Program)), AssemblyHash(typeof(WindingArea)), AssemblyHash(typeof(Clipper64)), AssemblyHash(typeof(Polygon)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(record, Json) + "\n");
    }

    private static void SummarizeReal(string directory)
    {
        var source = LoadReal();
        string[] paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, $"run-{i}.json")).ToArray();
        RealRun[] runs = paths.Select(p => JsonSerializer.Deserialize<RealRun>(File.ReadAllText(p))!).ToArray();
        string Identity(RealRun r) => JsonSerializer.Serialize(r with { Run = 0, Utc = default, Rows = [] });
        string Key(RealRow r) => $"{r.Direction}|{r.Method}|{r.Scope}";
        var expected = new Dictionary<string, double>();
        foreach (string direction in RealDirections)
        foreach (string method in RealMethods)
        {
            RealSession session = PrepareReal(source, direction, method);
            double digest = TraverseReal(session), preparation = PreparationValue(session);
            foreach (string scope in RealScopes) expected.Add($"{direction}|{method}|{scope}", scope == "prepare" ? preparation : digest);
        }
        for (int i = 0; i < runs.Length; i++)
        {
            RealRun r = runs[i];
            Require(r.Run == i + 1 && Identity(r) == Identity(runs[0]) && r.TieredCompilation == "0", "Real run identity mismatch.");
            Require(r.SourceHash == source.SourceHash && r.Origin.X == source.Origin.X && r.Origin.Y == source.Origin.Y, "Frozen source changed.");
            Require(r.HarnessHash == AssemblyHash(typeof(Program)) && r.WindingHash == AssemblyHash(typeof(WindingArea)) &&
                r.ClipperHash == AssemblyHash(typeof(Clipper64)) && r.NtsHash == AssemblyHash(typeof(Polygon)), "Use measured binaries.");
            Require(r.Rows.Select(Key).Order().SequenceEqual(expected.Keys.Order()), "Incomplete real matrix.");
            foreach (RealRow row in r.Rows)
            {
                Require(double.IsFinite(row.Value) && row.Value == expected[Key(row)], "Recomputed real output differs.");
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) && s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid real samples.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) && row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Real median mismatch.");
            }
        }
        // Recompute without overwriting recorded diagnostics, including when a stale run is rejected.
        RealValidation validation = ValidateReal(source, null);
        Require(File.ReadAllText(Path.Combine(directory, "validation.json")) == JsonSerializer.Serialize(validation, Json) + "\n",
            "Recorded real validation differs from recomputed results.");
        RealAggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(Key).OrderBy(g => g.Key).Select(g =>
        {
            RealRow r = g.First();
            return new RealAggregate(r.Direction, r.Method, r.Scope, r.Value, Median(g.Select(v => v.MedianNs)), g.Min(v => v.MedianNs), g.Max(v => v.MedianNs), Median(g.Select(v => v.MedianBytes)));
        }).ToArray();
        var gates = aggregates.Where(r => r.Method == "IntegerScanbeam-intersection" && r.Scope != "prepare").Select(r =>
        {
            double clipper = aggregates.Single(c => c.Direction == r.Direction && c.Scope == r.Scope && c.Method == "Clipper64-reused-data").MedianNs;
            return new { r.Direction, r.Scope, ClipperOverPrototype = clipper / r.MedianNs, Pass = clipper / r.MedianNs >= 1.25 };
        }).ToArray();
        var evidence = new { Protocol = "benchmarks/PolylineKit.ScanbeamBenchmarks/REAL-PROTOCOL.md", Environment = runs[0] with { Rows = [] },
            SampleCount = 270, SpeedPass = gates.All(g => g.Pass), OriginalEquivalent = validation.OriginalEquivalenceFailures == 0,
            IntegrationPass = gates.All(g => g.Pass) && validation.OriginalEquivalenceFailures == 0,
            RawFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }),
            DiagnosticFiles = new[] { "features.json", "validation.json" }.Select(file =>
                new { File = file, Sha256 = Hash(File.ReadAllBytes(Path.Combine(directory, file))) }),
            Validation = validation with { Pairs = [] }, Gates = gates, Measurements = aggregates };
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(evidence, Json) + "\n");
        var md = new StringBuilder($"# Real-contour scanbeam summary\n\nSpeed gate: **{(evidence.SpeedPass ? "PASS" : "FAIL")}**. Original-input equivalence: **{(evidence.OriginalEquivalent ? "PASS" : "FAIL")}**.\n\n");
        md.AppendLine("| Direction | Method | Scope | ms | Range ms | B/op |\n|---|---|---|---:|---:|---:|");
        foreach (RealAggregate r in aggregates) md.AppendLine(FormattableString.Invariant($"| {r.Direction} | {r.Method} | {r.Scope} | {r.MedianNs / 1e6:F4} | {r.MinNs / 1e6:F4}–{r.MaxNs / 1e6:F4} | {r.MedianBytes:F0} |"));
        md.AppendLine("\n| Direction | Scope | Clipper/prototype | Pass |\n|---|---|---:|---|");
        foreach (var gate in gates) md.AppendLine(FormattableString.Invariant($"| {gate.Direction} | {gate.Scope} | {gate.ClipperOverPrototype:F4} | {gate.Pass} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), md.ToString());
        Console.WriteLine($"Real evidence: 18 rows/process, 270 samples; speed={evidence.SpeedPass}; original equivalence={evidence.OriginalEquivalent}.");
    }
}
