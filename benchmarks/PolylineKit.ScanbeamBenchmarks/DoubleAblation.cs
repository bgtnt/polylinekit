using System.Diagnostics;
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
    private static readonly string[] AblationMethods = ["Winding-intersection-only", "Guarded-baseline",
        "Guarded-common-y", "Guarded-cache", "Guarded-combined", "Clipper64-reused-data"];
    private static readonly int[][] AblationOrders = [[0, 1, 2, 3, 4, 5], [3, 4, 5, 0, 1, 2], [2, 5, 1, 4, 0, 3]];
    private static readonly string[] FilterMethods = ["Winding-intersection-only", "Guarded-combined", "Guarded-filtered", "Clipper64-reused-data"];
    private static readonly int[][] FilterOrders = [[0, 1, 2, 3], [2, 3, 0, 1], [1, 3, 0, 2]];
    private static readonly string[] PreparedMethods = ["Winding-intersection-only", "Guarded-filtered", "Guarded-prepared", "Clipper64-reused-data"];
    private static readonly string[] DirectMethods = ["Winding-intersection-only", "Guarded-prepared", "Guarded-direct", "Clipper64-reused-data"];
    private static readonly string[] AreaMethods = ["Winding-intersection-only", "Guarded-prepared", "Guarded-area", "Clipper64-reused-data"];
    private static readonly string[] PreparedScopes = ["prepare", "warm-table", "warm-zones-fresh-queries", "prepare-plus-one"];
    private sealed record AblationValue(string Method, double Area, double Coverage, bool Fallback, string? Reason,
        double? ErrorBound, int Bands, long Events, long Visits, int Peak, long Work, long XEvaluations, long XCacheHits,
        long FilterAttempts, long FilterAccepted, long FilterInterval);
    private sealed record AblationPair(string Direction, string Zone, string Query, bool Candidate,
        double Reference, double ReferenceCoverage, AblationValue[] Values);
    private sealed record AblationValidation(string SourceHash, string GeometryHash, int DirectionalPairs, int Candidates,
        AblationPair[] Pairs);
    private sealed class SweepRegion(Point2[] points, RegionBounds bounds, double area,
        GuardedDoubleSweep.PreparedPath path) : PreparedRegion(points, bounds, area)
    {
        internal GuardedDoubleSweep.PreparedPath Path { get; } = path;
    }

    private sealed class AblationComparator(string name, bool commonY, bool cache, bool filter = false, bool prepared = false, bool direct = false, bool areaArithmetic = false) : CoverageComparator
    {
        internal readonly GuardedDoubleSweep Engine = new(commonY, cache, filter, direct, areaArithmetic);
        internal override string Name => name;
        internal override PreparedRegion Prepare(Point2[] points, bool zone)
        {
            RegionBounds bounds = RegionPreparation.Bounds(points);
            double area = Math.Abs(RegionPreparation.SignedArea(points));
            return prepared ? new SweepRegion(points, bounds, area, GuardedDoubleSweep.PreparePath(points)) : new PreparedRegion(points, bounds, area);
        }
        protected override double Intersection(PreparedRegion zone, PreparedRegion query) =>
            prepared ? Engine.MeasureIntersection(((SweepRegion)zone).Path, ((SweepRegion)query).Path) :
                Engine.MeasureIntersection(zone.Points, query.Points);
    }

    private static RealSession PrepareAblation(RealSource source, string direction, string method)
    {
        CoverageComparator comparator = method switch
        {
            "Guarded-baseline" => new AblationComparator(method, false, false),
            "Guarded-common-y" => new AblationComparator(method, true, false),
            "Guarded-cache" => new AblationComparator(method, false, true),
            "Guarded-combined" => new AblationComparator(method, true, true),
            "Guarded-filtered" => new AblationComparator(method, true, true, true),
            "Guarded-prepared" => new AblationComparator(method, true, true, true, true),
            "Guarded-direct" => new AblationComparator(method, true, true, true, true, true),
            "Guarded-area" => new AblationComparator(method, true, true, true, true, areaArithmetic: true),
            _ => Comparators.Create(method, 1e6)!
        };
        Require(comparator is not null, "Unknown ablation comparator.");
        var zones = new List<PreparedRegion>(); var queries = new List<PreparedRegion>();
        foreach (RealFeature feature in source.Features)
        {
            bool zone = feature.County == (direction == "county-zones");
            (zone ? zones : queries).Add(comparator!.Prepare(feature.Points, zone));
        }
        return new(comparator!, zones.ToArray(), queries.ToArray());
    }

    private static RealSession RefreshQueries(RealSource source, string direction, RealSession session)
    {
        // One new query catalogue per table, not one preparation per candidate pair.
        PreparedRegion[] queries = source.Features.Where(f => f.County != (direction == "county-zones"))
            .Select(f => session.Comparator.Prepare(f.Points, false)).ToArray();
        return new(session.Comparator, session.Zones, queries);
    }

    private static string PreparedMetadataJson(RealSource source)
    {
        var paths = source.Features.Select(f =>
        {
            GuardedDoubleSweep.PreparedPath p = GuardedDoubleSweep.PreparePath(f.Points);
            return new { f.Id, f.County, p.VertexCount, p.EdgeCount, p.PayloadBytes };
        }).ToArray();
        return JsonSerializer.Serialize(new { source.SourceHash,
            Description = "Retained prepared array element bytes, including cloned coordinates, edges, scalar bounds and sorted endpoints. Excludes array/object headers, shared source catalogue and mutable engine scratch; not peak memory.",
            Paths = paths, TotalPayloadBytes = paths.Sum(p => p.PayloadBytes) }, Json) + "\n";
    }

    private static void WritePreparedMetadata(RealSource source, string directory) =>
        File.WriteAllText(Path.Combine(directory, "prepared-paths.json"), PreparedMetadataJson(source));

    private static AblationValidation ValidateAblation(RealSource source, string? directory, bool filterExperiment = false, bool preparedExperiment = false, bool directExperiment = false, bool areaExperiment = false)
    {
        string[] methods = areaExperiment ? AreaMethods : directExperiment ? DirectMethods : preparedExperiment ? PreparedMethods : filterExperiment ? FilterMethods : AblationMethods;
        string GeometryHash() => Hash(JsonSerializer.SerializeToUtf8Bytes(source.Features));
        string inputHash = GeometryHash();
        var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
        var polygons = source.Features.ToDictionary(f => f.Id, f => RegionPreparation.Polygon(factory, f.Points));
        Require(polygons.Values.All(p => p.IsValid && p.Area > 0), "Invalid original polygon.");
        var pairs = new List<AblationPair>();
        foreach (string direction in RealDirections)
        {
            RealSession[] sessions = methods.Select(m => PrepareAblation(source, direction, m)).ToArray();
            RealFeature[] zones = source.Features.Where(f => f.County == (direction == "county-zones")).ToArray();
            RealFeature[] queries = source.Features.Where(f => f.County != (direction == "county-zones")).ToArray();
            for (int i = 0; i < zones.Length; i++)
            for (int j = 0; j < queries.Length; j++)
            {
                Polygon a = polygons[zones[i].Id], b = polygons[queries[j].Id];
                double expected = Intersection(a, b), coverage = expected / a.Area;
                bool candidate = !sessions[0].Zones[i].Bounds.Disjoint(sessions[0].Queries[j].Bounds);
                var values = new List<AblationValue>();
                foreach (RealSession session in sessions)
                {
                    Require(candidate == !session.Zones[i].Bounds.Disjoint(session.Queries[j].Bounds), "Candidate mask differs.");
                    CoverageValue value = session.Comparator.Compare(session.Zones[i], session.Queries[j]);
                    Require(double.IsFinite(value.Intersection) && value.Intersection >= 0 &&
                        double.IsFinite(value.Coverage!.Value) && Math.Abs(value.Intersection - expected) <= 1 &&
                        Math.Abs(value.Coverage.Value - coverage) <= 1e-8, "Ablation accuracy failure.");
                    GuardedDoubleSweep? engine = candidate ? (session.Comparator as AblationComparator)?.Engine : null;
                    bool fallback = engine?.LastUsedFallback ?? false;
                    double? bound = engine is null || fallback ? null : engine.LastErrorBound;
                    if (engine is not null)
                    {
                        if (fallback) Require(!string.IsNullOrEmpty(engine.LastFallbackReason) &&
                            Bits(value.Intersection) == Bits(values[0].Area), "Fallback does not match Winding.");
                        else Require(bound is >= 0 && double.IsFinite(bound.Value) &&
                            bound <= Math.Min(.25, 1e-10 * Math.Abs(value.Intersection)), "Certificate budget differs.");
                    }
                    values.Add(new(session.Comparator.Name, value.Intersection, value.Coverage!.Value, fallback,
                        engine?.LastFallbackReason, bound, engine?.BandCount ?? 0, engine?.EventCount ?? 0,
                        engine?.ActiveEdgeVisits ?? 0, engine?.PeakActiveCount ?? 0, engine?.WorkCount ?? 0,
                        engine?.XEvaluationCount ?? 0, engine?.XCacheHitCount ?? 0,
                        engine?.FilterAttemptCount ?? 0, engine?.FilterAcceptedCount ?? 0, engine?.FilterIntervalCount ?? 0));
                }
                // Caching changes only the number of interval evaluations, not their values or control flow.
                foreach ((int plain, int cached) in filterExperiment || preparedExperiment || directExperiment || areaExperiment ? Array.Empty<(int, int)>() : [(1, 3), (2, 4)])
                {
                    AblationValue p = values[plain], c = values[cached];
                    Require((p with { Method = c.Method, XEvaluations = c.XEvaluations, XCacheHits = c.XCacheHits }) == c &&
                        Bits(p.Area) == Bits(c.Area) && Bits(p.Coverage) == Bits(c.Coverage) &&
                        (!p.ErrorBound.HasValue || Bits(p.ErrorBound.Value) == Bits(c.ErrorBound!.Value)) &&
                        c.XEvaluations + c.XCacheHits == p.XEvaluations, "Cache changed certified behavior.");
                }
                if (filterExperiment)
                {
                    AblationValue unfiltered = values[1], filtered = values[2];
                    Require(filtered.FilterAttempts == filtered.FilterAccepted + filtered.FilterInterval &&
                        unfiltered.FilterAttempts == 0, "Filter accounting mismatch.");
                    if (!unfiltered.Fallback)
                    {
                        Require(!filtered.Fallback && Bits(unfiltered.Area) == Bits(filtered.Area) &&
                            unfiltered.ErrorBound == filtered.ErrorBound &&
                            (!unfiltered.ErrorBound.HasValue || Bits(unfiltered.ErrorBound.Value) == Bits(filtered.ErrorBound!.Value)) &&
                            unfiltered.Bands == filtered.Bands &&
                            unfiltered.Events == filtered.Events && unfiltered.Visits == filtered.Visits &&
                            unfiltered.Work == filtered.Work && unfiltered.Peak == filtered.Peak,
                            "Filter changed an already certified pair or its logical sweep.");
                        Require(filtered.XEvaluations + filtered.XCacheHits == 2 * filtered.FilterInterval &&
                            unfiltered.XEvaluations + unfiltered.XCacheHits == 2 * filtered.FilterAttempts,
                            "Certified filter request accounting mismatch.");
                    }
                }
                if (preparedExperiment || directExperiment || areaExperiment)
                {
                    AblationValue plain = values[1], prepared = values[2];
                    Require((plain with { Method = prepared.Method }) == prepared &&
                        Bits(plain.Area) == Bits(prepared.Area) && Bits(plain.Coverage) == Bits(prepared.Coverage) &&
                        (!plain.ErrorBound.HasValue || Bits(plain.ErrorBound.Value) == Bits(prepared.ErrorBound!.Value)),
                        "Prepared paths changed a frozen pair, certificate or diagnostic.");
                }
                pairs.Add(new(direction, zones[i].Id, queries[j].Id, candidate, expected, coverage, values.ToArray()));
            }
        }
        Require(inputHash == GeometryHash(), "Ablation mutated source arrays.");
        Require(pairs.Count == 2156 && pairs.Count(p => p.Candidate) == 422, "Frozen pair population changed.");
        var result = new AblationValidation(source.SourceHash, inputHash, pairs.Count, 422, pairs.ToArray());
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(result, Json) + "\n");
        }
        foreach (string method in methods.Where(m => m.StartsWith("Guarded-", StringComparison.Ordinal)))
        {
            AblationValue[] rows = pairs.Where(p => p.Candidate).Select(p => p.Values.Single(v => v.Method == method)).ToArray();
            Console.WriteLine($"{method}: {rows.Count(v => !v.Fallback)}/422 certified; bands={rows.Sum(v => v.Bands)}, X evaluations={rows.Sum(v => v.XEvaluations)}, filter={rows.Sum(v => v.FilterAccepted)}/{rows.Sum(v => v.FilterAttempts)}.");
        }
        if (filterExperiment) Require(pairs.Sum(p => p.Values[2].FilterAccepted) > 0, "Filter never certified an order.");
        return result;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static void RunAblation(string directory, int run, string revision, bool filterExperiment = false, bool preparedExperiment = false, bool directExperiment = false, bool areaExperiment = false)
    {
        string[] methods = areaExperiment ? AreaMethods : directExperiment ? DirectMethods : preparedExperiment ? PreparedMethods : filterExperiment ? FilterMethods : AblationMethods;
        int[][] orders = filterExperiment || preparedExperiment || directExperiment || areaExperiment ? FilterOrders : AblationOrders;
        string[] scopes = preparedExperiment || directExperiment || areaExperiment ? PreparedScopes : RealScopes;
        Require(run is >= 1 and <= 3 && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Use run1..3 and tiering0.");
        var source = LoadReal();
        ValidateAblation(source, directory, filterExperiment, preparedExperiment, directExperiment, areaExperiment);
        if (preparedExperiment || directExperiment || areaExperiment) WritePreparedMetadata(source, directory);
        var rows = new List<RealRow>();
        foreach (string direction in RealDirections)
        foreach (int index in orders[run - 1])
        {
            string method = methods[index];
            RealSession prepared = PrepareAblation(source, direction, method);
            foreach (string scope in scopes)
            {
                Func<double> operation = scope switch
                {
                    "prepare" => () => PreparationValue(PrepareAblation(source, direction, method)),
                    "warm-table" => () => TraverseReal(prepared),
                    "warm-zones-fresh-queries" => () => TraverseReal(RefreshQueries(source, direction, prepared)),
                    _ => () => TraverseReal(PrepareAblation(source, direction, method))
                };
                double value = operation();
                Sample[] samples = Measure(operation);
                Require(Bits(value) == Bits(operation()), "Unstable ablation digest.");
                rows.Add(new(direction, method, scope, value, Median(samples.Select(s => s.Nanoseconds)), Median(samples.Select(s => s.Bytes)), samples));
                Console.WriteLine($"ablation run{run}: {direction}/{method}/{scope}");
            }
        }
        var record = new DoubleRun(run, revision, DateTimeOffset.UtcNow, source.SourceHash,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount, Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            AssemblyHash(typeof(Program)), AssemblyHash(typeof(WindingArea)), AssemblyHash(typeof(Clipper64)), AssemblyHash(typeof(Polygon)), rows.ToArray());
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(record, Json) + "\n");
    }

    private static void SummarizeAblation(string directory, bool filterExperiment = false, bool preparedExperiment = false, bool directExperiment = false, bool areaExperiment = false)
    {
        string[] methods = areaExperiment ? AreaMethods : directExperiment ? DirectMethods : preparedExperiment ? PreparedMethods : filterExperiment ? FilterMethods : AblationMethods;
        int[][] orders = filterExperiment || preparedExperiment || directExperiment || areaExperiment ? FilterOrders : AblationOrders;
        string[] scopes = preparedExperiment || directExperiment || areaExperiment ? PreparedScopes : RealScopes;
        string baseline = areaExperiment ? "Guarded-prepared" : directExperiment ? "Guarded-prepared" : preparedExperiment ? "Guarded-filtered" : filterExperiment ? "Guarded-combined" : "Guarded-baseline";
        string selected = areaExperiment ? "Guarded-area" : directExperiment ? "Guarded-direct" : preparedExperiment ? "Guarded-prepared" : filterExperiment ? "Guarded-filtered" : "Guarded-combined";
        var source = LoadReal();
        string[] paths = Enumerable.Range(1, 3).Select(i => Path.Combine(directory, $"run-{i}.json")).ToArray();
        DoubleRun[] runs = paths.Select(p => JsonSerializer.Deserialize<DoubleRun>(File.ReadAllText(p))!).ToArray();
        string Identity(DoubleRun r) => JsonSerializer.Serialize(r with { Run = 0, Utc = default, Rows = [] });
        string Key(RealRow r) => $"{r.Direction}|{r.Method}|{r.Scope}";
        for (int i = 0; i < runs.Length; i++)
        {
            DoubleRun r = runs[i];
            Require(r.Run == i + 1 && Identity(r) == Identity(runs[0]) && r.TieredCompilation == "0", "Ablation identity mismatch.");
            Require(r.SourceHash == source.SourceHash && r.HarnessHash == AssemblyHash(typeof(Program)) &&
                r.WindingHash == AssemblyHash(typeof(WindingArea)) && r.ClipperHash == AssemblyHash(typeof(Clipper64)) &&
                r.NtsHash == AssemblyHash(typeof(Polygon)), "Use measured binaries and frozen source.");
            string[] order = RealDirections.SelectMany(d => orders[i].SelectMany(m => scopes.Select(s => $"{d}|{methods[m]}|{s}"))).ToArray();
            Require(r.Rows.Select(Key).SequenceEqual(order), "Ablation execution order changed.");
        }
        var expected = new Dictionary<string, double>();
        foreach (string direction in RealDirections)
        foreach (string method in methods)
        {
            RealSession session = PrepareAblation(source, direction, method);
            double digest = TraverseReal(session), preparation = PreparationValue(session);
            foreach (string scope in scopes)
            {
                if (scope == "warm-zones-fresh-queries")
                    Require(Bits(TraverseReal(RefreshQueries(source, direction, session))) == Bits(digest), "Fresh queries changed output.");
                expected.Add($"{direction}|{method}|{scope}", scope == "prepare" ? preparation : digest);
            }
        }
        foreach (DoubleRun run in runs)
        {
            Require(run.Rows.Select(Key).Order().SequenceEqual(expected.Keys.Order()), "Incomplete ablation matrix.");
            foreach (RealRow row in run.Rows)
            {
                Require(double.IsFinite(row.Value) && Bits(row.Value) == Bits(expected[Key(row)]), "Recomputed ablation output differs.");
                Require(row.Samples.Length == 5 && row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) &&
                    s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid ablation samples.");
                Require(row.MedianNs == Median(row.Samples.Select(s => s.Nanoseconds)) &&
                    row.MedianBytes == Median(row.Samples.Select(s => s.Bytes)), "Ablation median mismatch.");
            }
        }
        AblationValidation validation = ValidateAblation(source, null, filterExperiment, preparedExperiment, directExperiment, areaExperiment);
        Require(File.ReadAllText(Path.Combine(directory, "validation.json")) == JsonSerializer.Serialize(validation, Json) + "\n",
            "Recorded ablation validation differs from recomputed results.");
        if (preparedExperiment || directExperiment || areaExperiment)
            Require(File.ReadAllText(Path.Combine(directory, "prepared-paths.json")) == PreparedMetadataJson(source),
                "Recorded prepared metadata differs from recomputed paths.");
        RealAggregate[] aggregates = runs.SelectMany(r => r.Rows).GroupBy(Key).OrderBy(g => g.Key).Select(g =>
        {
            RealRow r = g.First();
            return new RealAggregate(r.Direction, r.Method, r.Scope, r.Value, Median(g.Select(v => v.MedianNs)),
                g.Min(v => v.MedianNs), g.Max(v => v.MedianNs), Median(g.Select(v => v.MedianBytes)));
        }).ToArray();
        var gates = aggregates.Where(r => r.Method.StartsWith("Guarded-", StringComparison.Ordinal) && r.Scope != "prepare").Select(r =>
        {
            double Time(string method) => aggregates.Single(c => c.Direction == r.Direction && c.Scope == r.Scope && c.Method == method).MedianNs;
            return new { r.Direction, r.Method, r.Scope, BaselineOverVariant = Time(baseline) / r.MedianNs,
                ClipperOverVariant = Time("Clipper64-reused-data") / r.MedianNs, WindingOverVariant = Time("Winding-intersection-only") / r.MedianNs,
                Pass = r.MedianNs <= .8 * Time("Clipper64-reused-data") && r.MedianNs <= .9 * Time("Winding-intersection-only") };
        }).ToArray();
        var diagnostics = methods.Select(method =>
        {
            var values = validation.Pairs.Where(p => p.Candidate).Select(p => p.Values.Single(v => v.Method == method)).ToArray();
            double maxError = validation.Pairs.Max(p => Math.Abs(p.Reference - p.Values.Single(v => v.Method == method).Area));
            return new { Method = method, Candidates = values.Length, Certified = values.Count(v => v.ErrorBound.HasValue),
                Fallbacks = values.Count(v => v.Fallback), MaxAreaError = maxError,
                MaxCoverageError = validation.Pairs.Max(p => Math.Abs(p.ReferenceCoverage - p.Values.Single(v => v.Method == method).Coverage)),
                MaxCertifiedRadius = values.Max(v => v.ErrorBound), Bands = values.Sum(v => v.Bands), Events = values.Sum(v => v.Events),
                Visits = values.Sum(v => v.Visits), XEvaluations = values.Sum(v => v.XEvaluations), XCacheHits = values.Sum(v => v.XCacheHits),
                FilterAttempts = values.Sum(v => v.FilterAttempts), FilterAccepted = values.Sum(v => v.FilterAccepted),
                FilterInterval = values.Sum(v => v.FilterInterval),
                FallbackReasons = values.Where(v => v.Fallback).GroupBy(v => v.Reason!).ToDictionary(g => g.Key, g => g.Count()) };
        }).ToArray();
        bool selectedPass = gates.Where(g => g.Method == selected).All(g => g.Pass) && diagnostics.Single(d => d.Method == selected).Certified > 0;
        var evidence = new { Protocol = areaExperiment ? "benchmarks/PolylineKit.ScanbeamBenchmarks/AREA-ARITHMETIC-PROTOCOL.md" : directExperiment ? "benchmarks/PolylineKit.ScanbeamBenchmarks/DIRECT-SWEEP-PROTOCOL.md" : preparedExperiment ? "benchmarks/PolylineKit.ScanbeamBenchmarks/PREPARED-SWEEP-PROTOCOL.md" : filterExperiment ? "benchmarks/PolylineKit.ScanbeamBenchmarks/SCALAR-FILTER-PROTOCOL.md" : "benchmarks/PolylineKit.ScanbeamBenchmarks/DOUBLE-ABLATION-PROTOCOL.md",
            Environment = runs[0] with { Rows = [] }, SampleCount = runs.Sum(r => r.Rows.Sum(row => row.Samples.Length)),
            BaselineMethod = baseline, SelectedMethod = selected, SelectedPass = selectedPass,
            CombinedPass = diagnostics.Any(d => d.Method == "Guarded-combined" && d.Certified > 0) &&
                gates.Where(g => g.Method == "Guarded-combined").All(g => g.Pass),
            RawFiles = paths.Select(p => new { File = Path.GetFileName(p), Sha256 = Hash(File.ReadAllBytes(p)) }),
            ValidationFileSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "validation.json"))),
            PreparedMetadataFileSha256 = preparedExperiment || directExperiment || areaExperiment ? Hash(File.ReadAllBytes(Path.Combine(directory, "prepared-paths.json"))) : null,
            Validation = validation with { Pairs = [] }, Diagnostics = diagnostics, Gates = gates, Measurements = aggregates };
        Require(gates.Length == (preparedExperiment || directExperiment || areaExperiment ? 12 : filterExperiment ? 8 : 16) &&
            evidence.SampleCount == (preparedExperiment || directExperiment || areaExperiment ? 480 : filterExperiment ? 360 : 540), "Unexpected ablation sample/gate count.");
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(evidence, Json) + "\n");
        var md = new StringBuilder($"# Double sweep experiment results\n\n{selected} gate: **{(selectedPass ? "PASS" : "FAIL")}**.\n\n");
        md.AppendLine("| Direction | Method | Scope | ms | Range ms | B/op |\n|---|---|---|---:|---:|---:|");
        foreach (RealAggregate r in aggregates) md.AppendLine(FormattableString.Invariant($"| {r.Direction} | {r.Method} | {r.Scope} | {r.MedianNs / 1e6:F4} | {r.MinNs / 1e6:F4}–{r.MaxNs / 1e6:F4} | {r.MedianBytes:F0} |"));
        File.WriteAllText(Path.Combine(directory, "summary.md"), md.ToString());
        Console.WriteLine($"Evidence: {methods.Length * RealDirections.Length * scopes.Length} rows/process, {evidence.SampleCount} samples; {selected} gate={selectedPass}.");
    }

    private static void ProfileAblation(string method, int seconds)
    {
        Require((AblationMethods.Contains(method) || FilterMethods.Contains(method) || PreparedMethods.Contains(method) || DirectMethods.Contains(method) || AreaMethods.Contains(method)) && seconds is >= 1 and <= 60, "Unknown method or duration outside1..60s.");
        RealSession session = PrepareAblation(LoadReal(), "county-zones", method);
        double expected = TraverseReal(session);
        for (int i = 0; i < 3; i++) Require(Bits(TraverseReal(session)) == Bits(expected), "Unstable profile output.");
        int count = 0;
        var watch = Stopwatch.StartNew();
        do { Require(Bits(TraverseReal(session)) == Bits(expected), "Unstable profile output."); count++; }
        while (watch.Elapsed.TotalSeconds < seconds);
        Console.WriteLine($"Profile workload: {method}, {count} complete warm county tables, digest={expected:R}. Profiled elapsed time is not benchmark evidence.");
    }
}
