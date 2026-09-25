using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using PolylineKit;
using RegionCoverage;

namespace PolylineKit.ScanbeamBenchmarks;

internal static partial class Program
{
    private static readonly string[] DoubleMethods = ["Winding-intersection-only", "GuardedDouble-intersection", "Clipper64-reused-data"];
    private sealed record DoubleRun(int Run, string Revision, DateTimeOffset Utc, string SourceHash,
        string Runtime, string OS, string Architecture, string? Cpu, int LogicalProcessors, string? TieredCompilation,
        string HarnessHash, string WindingHash, string ClipperHash, string NtsHash, RealRow[] Rows);
    private sealed record DoublePair(string Direction, string Zone, string Query, bool Candidate,
        double ReferenceIntersection, double ReferenceCoverage, double Winding, double Guarded, double Clipper,
        double WindingCoverage, double GuardedCoverage, double ClipperCoverage,
        bool Fallback, string? Reason, double? ErrorBound, int Bands, long Events, long ActiveEdgeVisits, int PeakActive, long WorkUnits);
    private sealed record DoubleValidation(string SourceHash, string GeometryHash, int Features, int Vertices,
        int DirectionalPairs, int Candidates, int Certified, int CertifiedZero, int Fallbacks,
        SortedDictionary<string, int> FallbackReasons, int BackendFailures,
        double MaxWindingError, double MaxGuardedError, double MaxClipperError, double MaxGuardedMinusWinding,
        double MaxCoverageError,
        double MaxCertifiedErrorBound, DoublePair[] Pairs);

    private sealed class GuardedComparator : CoverageComparator
    {
        internal readonly GuardedDoubleSweep Engine = new();
        internal override string Name => "GuardedDouble-intersection";
        internal override PreparedRegion Prepare(Point2[] points, bool zone) =>
            new(points, RegionPreparation.Bounds(points), Math.Abs(RegionPreparation.SignedArea(points)));
        protected override double Intersection(PreparedRegion zone, PreparedRegion query) =>
            Engine.MeasureIntersection(zone.Points, query.Points);
    }

    private static RealSession PrepareDouble(RealSource source, string direction, string method)
    {
        CoverageComparator comparator = method == "GuardedDouble-intersection" ? new GuardedComparator() : Comparators.Create(method, 1e6)!;
        Require(comparator is not null, "Unknown double comparator.");
        bool counties = direction == "county-zones";
        var zones = new List<PreparedRegion>(); var queries = new List<PreparedRegion>();
        foreach (RealFeature feature in source.Features)
        {
            bool zone = feature.County == counties;
            PreparedRegion region = comparator!.Prepare(feature.Points, zone);
            (zone ? zones : queries).Add(region);
        }
        return new(comparator!, zones.ToArray(), queries.ToArray());
    }

    private static DoubleValidation ValidateDouble(RealSource source, string? directory)
    {
        string GeometryHash() => Hash(JsonSerializer.SerializeToUtf8Bytes(source.Features));
        string inputHash = GeometryHash();
        var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
        var polygons = source.Features.ToDictionary(f => f.Id, f => RegionPreparation.Polygon(factory, f.Points));
        Require(polygons.Values.All(p => p.IsValid && p.Area > 0), "Invalid original source polygon.");
        var pairs = new List<DoublePair>();
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int failures = 0, candidates = 0, certified = 0, certifiedZero = 0, fallbacks = 0;
        double maxW = 0, maxG = 0, maxC = 0, maxGW = 0, maxBound = 0, maxCoverage = 0;
        foreach (string direction in RealDirections)
        {
            RealSession[] sessions = DoubleMethods.Select(m => PrepareDouble(source, direction, m)).ToArray();
            var engine = ((GuardedComparator)sessions[1].Comparator).Engine;
            RealFeature[] zones = source.Features.Where(f => f.County == (direction == "county-zones")).ToArray();
            RealFeature[] queries = source.Features.Where(f => f.County != (direction == "county-zones")).ToArray();
            for (int i = 0; i < zones.Length; i++)
            for (int j = 0; j < queries.Length; j++)
            {
                Polygon a = polygons[zones[i].Id], b = polygons[queries[j].Id];
                double expected = Intersection(a, b), expectedCoverage = expected / a.Area;
                bool candidate = !sessions[0].Zones[i].Bounds.Disjoint(sessions[0].Queries[j].Bounds);
                foreach (RealSession session in sessions)
                    Require(candidate == !session.Zones[i].Bounds.Disjoint(session.Queries[j].Bounds), "Frozen candidate mask differs.");
                CoverageValue[] values = sessions.Select(s => s.Comparator.Compare(s.Zones[i], s.Queries[j])).ToArray();
                foreach (CoverageValue value in values)
                {
                    if (!double.IsFinite(value.Intersection) || value.Intersection < 0 || !double.IsFinite(value.Coverage!.Value) ||
                        Math.Abs(value.Intersection - expected) > 1 || Math.Abs(value.Coverage.Value - expectedCoverage) > 1e-8) failures++;
                    maxCoverage = Math.Max(maxCoverage, Math.Abs(value.Coverage!.Value - expectedCoverage));
                }
                maxW = Math.Max(maxW, Math.Abs(values[0].Intersection - expected));
                maxG = Math.Max(maxG, Math.Abs(values[1].Intersection - expected));
                maxC = Math.Max(maxC, Math.Abs(values[2].Intersection - expected));
                maxGW = Math.Max(maxGW, Math.Abs(values[1].Intersection - values[0].Intersection));
                bool fallback = candidate && engine.LastUsedFallback;
                string? reason = fallback ? engine.LastFallbackReason : null;
                double? bound = candidate && !fallback ? engine.LastErrorBound : null;
                if (candidate)
                {
                    candidates++;
                    if (fallback)
                    {
                        fallbacks++;
                        Require(!string.IsNullOrEmpty(reason), "Fallback reason missing.");
                        reasons[reason!] = reasons.GetValueOrDefault(reason!) + 1;
                        Require(values[1].Intersection == values[0].Intersection, "Fallback differs from ordinary Winding.");
                    }
                    else
                    {
                        certified++;
                        if (values[1].Intersection == 0) certifiedZero++;
                        Require(bound is >= 0 && double.IsFinite(bound.Value) &&
                            bound.Value <= Math.Min(.25, 1e-10 * Math.Abs(values[1].Intersection)), "Certified result exceeds its declared budget.");
                        maxBound = Math.Max(maxBound, bound.GetValueOrDefault());
                    }
                }
                pairs.Add(new(direction, zones[i].Id, queries[j].Id, candidate, expected, expectedCoverage,
                    values[0].Intersection, values[1].Intersection, values[2].Intersection,
                    values[0].Coverage!.Value, values[1].Coverage!.Value, values[2].Coverage!.Value, fallback, reason, bound,
                    candidate ? engine.BandCount : 0, candidate ? engine.EventCount : 0,
                    candidate ? engine.ActiveEdgeVisits : 0, candidate ? engine.PeakActiveCount : 0, candidate ? engine.WorkCount : 0));
            }
        }
        Require(inputHash == GeometryHash(), "A comparator mutated the original source arrays.");
        Require(pairs.Count == 2156 && candidates == 422, "Frozen original population or candidate count changed.");
        var result = new DoubleValidation(source.SourceHash, inputHash, 109, 10588, pairs.Count, candidates,
            certified, certifiedZero, fallbacks, reasons, failures, maxW, maxG, maxC, maxGW, maxCoverage, maxBound, pairs.ToArray());
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(result, Json) + "\n");
        }
        Require(failures == 0, $"Original-input backend accuracy failures: {failures}.");
        Console.WriteLine($"Double sweep: {pairs.Count} original-input directional pairs; {candidates} geometry calls, {certified} certified ({certifiedZero} zero), {fallbacks} fallback; no backend accuracy failures.");
        return result;
    }

    private static void RunDouble(string directory, int run, string revision)
    {
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Use run1..3 and tiering0.");
        var source = LoadReal();
        ValidateDouble(source, directory);
        var rows = new List<RealRow>();
        int offset = run - 1;
        foreach (string direction in RealDirections)
        foreach (string method in DoubleMethods.Skip(offset).Concat(DoubleMethods.Take(offset)))
        {
            RealSession prepared = PrepareDouble(source, direction, method);
            foreach (string scope in RealScopes)
            {
                Func<double> operation = scope switch
                {
                    "prepare" => () => PreparationValue(PrepareDouble(source, direction, method)),
                    "warm-table" => () => TraverseReal(prepared),
                    _ => () => TraverseReal(PrepareDouble(source, direction, method))
                };
                double value = operation();
                Sample[] samples = Measure(operation);
                Require(value == operation(), "Unstable double result digest.");
                rows.Add(new(direction, method, scope, value, Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
                Console.WriteLine($"double run{run}: {direction}/{method}/{scope}");
            }
        }
        var record = new DoubleRun(run, revision, DateTimeOffset.UtcNow, source.SourceHash,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount, Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            AssemblyHash(typeof(Program)), AssemblyHash(typeof(WindingArea)), AssemblyHash(typeof(Clipper64)), AssemblyHash(typeof(Polygon)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(record, Json) + "\n");
    }

    private static void SummarizeDouble(string directory)
    {
        var source = LoadReal();
        string[] paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, $"run-{i}.json")).ToArray();
        DoubleRun[] runs = paths.Select(p => JsonSerializer.Deserialize<DoubleRun>(File.ReadAllText(p))!).ToArray();
        string Identity(DoubleRun r) => JsonSerializer.Serialize(r with { Run = 0, Utc = default, Rows = [] });
        string Key(RealRow r) => $"{r.Direction}|{r.Method}|{r.Scope}";
        for (int i = 0; i < runs.Length; i++)
        {
            DoubleRun r = runs[i];
            Require(r.Run == i + 1 && Identity(r) == Identity(runs[0]) && r.TieredCompilation == "0", "Double run identity mismatch.");
            Require(r.SourceHash == source.SourceHash && r.HarnessHash == AssemblyHash(typeof(Program)) &&
                r.WindingHash == AssemblyHash(typeof(WindingArea)) && r.ClipperHash == AssemblyHash(typeof(Clipper64)) &&
                r.NtsHash == AssemblyHash(typeof(Polygon)), "Use measured binaries and frozen source.");
        }
        var expected = new Dictionary<string, double>();
        foreach (string direction in RealDirections)
        foreach (string method in DoubleMethods)
        {
            RealSession session = PrepareDouble(source, direction, method);
            double digest = TraverseReal(session), preparation = PreparationValue(session);
            foreach (string scope in RealScopes) expected.Add($"{direction}|{method}|{scope}", scope == "prepare" ? preparation : digest);
        }
        foreach (DoubleRun run in runs)
        {
            Require(run.Rows.Select(Key).Order().SequenceEqual(expected.Keys.Order()), "Incomplete double matrix.");
            foreach (RealRow row in run.Rows)
            {
                Require(double.IsFinite(row.Value) && row.Value == expected[Key(row)], "Recomputed double output differs.");
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) &&
                    s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid double samples.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) &&
                    row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Double median mismatch.");
            }
        }
        DoubleValidation validation = ValidateDouble(source, null);
        Require(File.ReadAllText(Path.Combine(directory, "validation.json")) == JsonSerializer.Serialize(validation, Json) + "\n",
            "Recorded double validation differs from recomputed results.");
        RealAggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(Key).OrderBy(g => g.Key).Select(g =>
        {
            RealRow r = g.First();
            return new RealAggregate(r.Direction, r.Method, r.Scope, r.Value, Median(g.Select(v => v.MedianNs)),
                g.Min(v => v.MedianNs), g.Max(v => v.MedianNs), Median(g.Select(v => v.MedianBytes)));
        }).ToArray();
        var gates = aggregates.Where(r => r.Method == "GuardedDouble-intersection" && r.Scope != "prepare").Select(r =>
        {
            double clipper = aggregates.Single(c => c.Direction == r.Direction && c.Scope == r.Scope && c.Method == "Clipper64-reused-data").MedianNs;
            double winding = aggregates.Single(c => c.Direction == r.Direction && c.Scope == r.Scope && c.Method == "Winding-intersection-only").MedianNs;
            return new { r.Direction, r.Scope, ClipperOverPrototype = clipper / r.MedianNs, WindingOverPrototype = winding / r.MedianNs,
                Pass = r.MedianNs <= .8 * clipper && r.MedianNs <= .9 * winding };
        }).ToArray();
        var evidence = new { Protocol = "benchmarks/PolylineKit.ScanbeamBenchmarks/DOUBLE-PROTOCOL.md", Environment = runs[0] with { Rows = [] },
            SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)), Pass = gates.All(g => g.Pass) && validation.Certified > 0,
            RawFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }),
            ValidationFileSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "validation.json"))),
            Validation = validation with { Pairs = [] }, Gates = gates, Measurements = aggregates };
        Require(gates.Length == 4 && evidence.SampleCount == 270, "Unexpected double gate or sample count.");
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(evidence, Json) + "\n");
        var md = new StringBuilder($"# Guarded double sweep results\n\nPredeclared gate: **{(evidence.Pass ? "PASS" : "FAIL")}**.\n\n");
        md.AppendLine($"Original-input geometry calls: {validation.Candidates}; certified: {validation.Certified} ({validation.CertifiedZero} zeros); fallback: {validation.Fallbacks}.\n");
        md.AppendLine("| Direction | Method | Scope | ms | Range ms | B/op |\n|---|---|---|---:|---:|---:|");
        foreach (RealAggregate r in aggregates) md.AppendLine(FormattableString.Invariant($"| {r.Direction} | {r.Method} | {r.Scope} | {r.MedianNs / 1e6:F4} | {r.MinNs / 1e6:F4}–{r.MaxNs / 1e6:F4} | {r.MedianBytes:F0} |"));
        md.AppendLine("\n| Direction | Scope | Clipper/prototype | Winding/prototype | Pass |\n|---|---|---:|---:|---|");
        foreach (var g in gates) md.AppendLine(FormattableString.Invariant($"| {g.Direction} | {g.Scope} | {g.ClipperOverPrototype:F4} | {g.WindingOverPrototype:F4} | {g.Pass} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), md.ToString());
        Console.WriteLine($"Double evidence: 18 rows/process, 270 samples; gate={evidence.Pass}; fallback={validation.Fallbacks}/{validation.Candidates}.");
    }
}
