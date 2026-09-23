using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Versioning;
using PolylineKit;

namespace PolylineKit.Recognition;

/// <summary>Sequential application timings against committed frozen banks; no parameter selection occurs here.</summary>
public static class PerformanceRunner
{
    private const string DisableSimd = "PolylineKit.DisableSimd";
    private static double sink;
    private sealed record RankedClass(string Label, string TemplateId, double Score);
    private sealed record QueryResult(string SampleId, RankedClass[] TopThree, string? Failure = null);
    private sealed record QueryTiming(string SampleId, long ElapsedTicks, long AllocatedBytes,
        string Status, string? PredictedLabel, string? TemplateId, double? Score, string? Failure);
    private sealed record SupportTiming(long Queries, double Milliseconds, double NanosecondsPerQuery,
        double BytesPerQuery, string Scope);
    private sealed record PointCounts(int Count, int Minimum, int Median, int P95, int Maximum,
        double Mean, IReadOnlyDictionary<int, int> Histogram);
    private sealed record Measurement(string Dataset, int Seed, string? HeldOutWriter, string Method,
        string Variant, string[] TemplateIds, int TemplateCount, int SharedSamples, int NativeSamples,
        int OfficialQueryCount, int SupportedQueryCount, int UnsupportedQueryCount,
        RecognitionConfiguration Configuration, PointCounts TemplatePointCounts, PointCounts QueryPointCounts,
        double BankConstructionMilliseconds, long BankConstructionAllocatedBytes, long RetainedPreparedBankBytes,
        string RetainedMemoryScope, int WarmupQueries, double WarmupMilliseconds, string[] WarmupSequence,
        double P50Microseconds, double P95Microseconds, double MedianBytesPerQuery, double MeanBytesPerQuery,
        double TotalProcessingMilliseconds, int[] MeasuredGcCollections, int Failures,
        SupportTiming ScoringOnly, QueryTiming[] Queries);

    private sealed class BankCache(StrokeRecord[] records, PreparedStroke[]? shared, PreparedProtractor[]? native)
    {
        internal readonly StrokeRecord[] Records = records;
        internal readonly PreparedStroke[]? Shared = shared;
        internal readonly PreparedProtractor[]? Native = native;
        internal readonly int Classes = records.Select(r => r.Label).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>
    /// Variants: uncached-scalar (template preparation per query, attribution only), cached-scalar,
    /// cached-simd. Start a fresh process per variant/run; this method never runs timings concurrently.
    /// </summary>
    public static void Run(string data, string freeze, string output, string revision, string variant, int run)
    {
        if (variant is not ("uncached-scalar" or "cached-scalar" or "cached-simd"))
            throw new ArgumentOutOfRangeException(nameof(variant));
        if (run < 1) throw new ArgumentOutOfRangeException(nameof(run));
        string destination = Path.Combine(output, $"{variant}-run-{run}.json");
        if (File.Exists(destination)) throw new IOException("A performance run with this identity already exists; choose a new output directory.");
        if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0" ||
            Environment.GetEnvironmentVariable("COMPlus_TieredCompilation") == "0")
            throw new InvalidOperationException("Application timings require normal tiered runtime settings. Clear the disabled-tiering override.");
        bool hadSwitch = AppContext.TryGetSwitch(DisableSimd, out bool previousSwitch);
        AppContext.SetSwitch(DisableSimd, variant != "cached-simd");
        try
        {
            FrozenEvaluation frozen = EvaluationRunner.LoadFrozen(data, freeze);
            var configurations = frozen.Configurations.ToDictionary(c => c.Dataset, StringComparer.Ordinal);
            var datasets = configurations.Keys.ToDictionary(dataset => dataset,
                dataset => RecognitionFiles.Load(data, dataset), StringComparer.Ordinal);
            var measurements = new List<Measurement>();
            BankDefinition[] selectedBanks = frozen.Banks.GroupBy(b => (b.Dataset, b.Seed))
                .Select(group => group.OrderBy(b => b.HeldOutWriter, StringComparer.Ordinal).First())
                .OrderBy(b => b.Dataset, StringComparer.Ordinal).ThenBy(b => b.Seed).ToArray();
            if (selectedBanks.Length != 6 || selectedBanks.Any(b => !RecognitionFiles.Seeds.Contains(b.Seed)))
                throw new InvalidDataException("Expected the three frozen seeds for each of two datasets.");
            var schedule = selectedBanks.SelectMany(bank => RecognitionFiles.Methods(bank.Dataset)
                .Select(method => (Bank: bank, Method: method))).ToArray();
            int offset = (run - 1) % schedule.Length;
            foreach (var item in schedule.Skip(offset).Concat(schedule.Take(offset)))
            {
                BankDefinition bank = item.Bank;
                StrokeRecord[] rows = datasets[bank.Dataset];
                var records = rows.ToDictionary(r => r.SampleId, StringComparer.Ordinal);
                RecognitionSplits.ValidateBank(bank, records);
                StrokeRecord[] templates = bank.TemplateIds.Select(id => records[id]).ToArray();
                StrokeRecord[] queries = bank.QueryIds.Select(id => records[id]).Where(r => r.Supported)
                    .OrderBy(r => r.SampleId, StringComparer.Ordinal).ToArray();
                // A fixed, label-independent ordering of development records is used only to warm code.
                StrokeRecord[] warmup = RecognitionSplits.Development(rows, bank.Dataset).Queries
                    .Where(r => r.Supported).OrderBy(r => r.SampleId, StringComparer.Ordinal).Take(100).ToArray();
                if (queries.Length == 0 || warmup.Length == 0) throw new InvalidDataException("Empty supported query or warmup sequence.");
                Measurement result = MeasureCase(bank, configurations[bank.Dataset], templates, queries,
                    warmup, item.Method, variant);
                measurements.Add(result);
                Console.WriteLine($"application {variant}/{run}: {bank.Dataset}/{bank.Seed}/{item.Method}, " +
                    $"{result.P50Microseconds:F2} us p50, {result.P95Microseconds:F2} us p95, {result.Failures} failures");
            }
            Assembly core = typeof(Point2).Assembly;
            RecognitionFiles.Write(destination, new
            {
                protocol = frozen.Protocol, implementationId = RecognitionEngine.ImplementationId,
                sourceRevision = revision, freezeSourceRevision = frozen.SourceRevision,
                freezeSha256 = RecognitionFiles.Hash(freeze), inputHashes = frozen.InputHashes,
                variant, run, utc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                cpu = CpuDescription(), processors = Environment.ProcessorCount,
                coreTarget = core.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
                coreDllSha256 = RecognitionFiles.Hash(core.Location),
                engineDllSha256 = RecognitionFiles.Hash(typeof(RecognitionEngine).Assembly.Location),
                forceScalar = AppContext.TryGetSwitch(DisableSimd, out bool disabled) && disabled,
                vector128 = Vector128.IsHardwareAccelerated, vector256 = Vector256.IsHardwareAccelerated,
                maximumAvailableVectorBits = Vector256.IsHardwareAccelerated ? 256 : Vector128.IsHardwareAccelerated ? 128 : 0,
                simdScope = "Optional PolylineKit affine array transforms; recognition score reductions and native Protractor/DTW recurrences remain scalar.",
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                readyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
                enableHardwareIntrinsics = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),
                serverGc = System.Runtime.GCSettings.IsServerGC,
                gcLatencyMode = System.Runtime.GCSettings.LatencyMode.ToString(),
                stopwatchFrequency = Stopwatch.Frequency,
                bankSelection = "First ordinal held-out dollar writer per seed; all three frozen Pendigits banks. All supported queries in those banks are measured.",
                warmupPolicy = "First 100 ordinal supported development-query IDs; cycle until >=3000 ms AND >=100 complete queries for each case.",
                schedule = schedule.Skip(offset).Concat(schedule.Take(offset)).Select(s => new { s.Bank.Dataset, s.Bank.Seed, s.Method }),
                latencyScope = "GetPoints + method-specific query preparation + all template scores + top-three distinct-class result creation. Uncached scalar additionally prepares every template per query.",
                totalTimeScope = "Whole measured pass, including timer/metric bookkeeping outside the individual query intervals.",
                gcPauseScope = "GC collection counts measured for the warm pass; pause durations are not measured.",
                quantileRule = "Nearest-rank empirical quantiles; every supported query remains in the timing denominator, including failures.",
                attributionOnly = variant == "uncached-scalar",
                measurements
            });
            GC.KeepAlive(sink);
        }
        finally { AppContext.SetSwitch(DisableSimd, hadSwitch && previousSwitch); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Measurement MeasureCase(BankDefinition definition, RecognitionConfiguration configuration,
        StrokeRecord[] templates, StrokeRecord[] queries, StrokeRecord[] warmup, string method, string variant)
    {
        bool cached = variant != "uncached-scalar";
        // Raw dataset records and this case's template-ID array already exist before both snapshots.
        // Keep the cache alive through the second full collection: retained bytes are not allocated bytes.
        long retainedBefore = GC.GetTotalMemory(true);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread(), constructionStart = Stopwatch.GetTimestamp();
        BankCache bank = BuildBank(templates, configuration, method, cached);
        long constructionTicks = Stopwatch.GetTimestamp() - constructionStart;
        long constructionBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        long retainedAfter = GC.GetTotalMemory(true);
        GC.KeepAlive(bank);

        var warmClock = Stopwatch.StartNew();
        int warmQueries = 0;
        while (warmClock.ElapsedMilliseconds < 3000 || warmQueries < 100)
        {
            QueryResult warmed = RankQuery(warmup[warmQueries % warmup.Length], bank, configuration, method, cached);
            if (warmed.Failure is not null) throw new InvalidDataException("Fixed development warmup failed: " + warmed.SampleId + ": " + warmed.Failure);
            Consume(warmed);
            warmQueries++;
        }
        warmClock.Stop();
        var timings = new QueryTiming[queries.Length];
        int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        long totalStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < queries.Length; i++)
        {
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread(), beforeTick = Stopwatch.GetTimestamp();
            QueryResult result = RankQuery(queries[i], bank, configuration, method, cached);
            long elapsedTicks = Stopwatch.GetTimestamp() - beforeTick;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            RankedClass? winner = result.TopThree.FirstOrDefault();
            timings[i] = new QueryTiming(result.SampleId, elapsedTicks, bytes, result.Failure is null ? "ok" : "failed",
                winner?.Label, winner?.TemplateId, winner?.Score, result.Failure);
            Consume(result);
        }
        double totalMilliseconds = (Stopwatch.GetTimestamp() - totalStart) * 1000.0 / Stopwatch.Frequency;
        for (int generation = 0; generation < collections.Length; generation++)
            collections[generation] = GC.CollectionCount(generation) - collections[generation];
        // This secondary support timing excludes preparation and ranking. It never replaces end-to-end timing.
        SupportTiming scoring = MeasureScoringOnly(bank, configuration, method, warmup, cached);
        GC.KeepAlive(bank);
        double[] microseconds = timings.Select(t => t.ElapsedTicks * 1e6 / Stopwatch.Frequency).Order().ToArray();
        double[] bytesPerQuery = timings.Select(t => (double)t.AllocatedBytes).Order().ToArray();
        return new Measurement(definition.Dataset, definition.Seed, definition.HeldOutWriter, method, variant,
            definition.TemplateIds, templates.Length, method == "protractor" ? 0 : 64, method == "protractor" ? 16 : 0,
            definition.QueryIds.Length, queries.Length, definition.QueryIds.Length - queries.Length, configuration,
            CountPoints(templates), CountPoints(queries), constructionTicks * 1000.0 / Stopwatch.Frequency,
            constructionBytes, retainedAfter - retainedBefore,
            "Incremental live managed bytes after full GC, with raw records already resident; negative noise is retained, not clamped. Construction precedes method warmup and may include first-use JIT.",
            warmQueries, warmClock.Elapsed.TotalMilliseconds, warmup.Select(r => r.SampleId).ToArray(),
            Percentile(microseconds, .5), Percentile(microseconds, .95), Percentile(bytesPerQuery, .5), bytesPerQuery.Average(),
            totalMilliseconds, collections, timings.Count(t => t.Status != "ok"), scoring, timings);
    }

    private static BankCache BuildBank(StrokeRecord[] templates, RecognitionConfiguration config, string method, bool cached)
    {
        if (!cached) return new BankCache(templates, null, null);
        return method == "protractor"
            ? new BankCache(templates, null, templates.Select(r => RecognitionEngine.PrepareProtractor(r.GetPoints(), config.ProtractorOrientationSensitive)).ToArray())
            : new BankCache(templates, templates.Select(r => RecognitionEngine.Prepare(r.GetPoints())).ToArray(), null);
    }

    private static QueryResult RankQuery(StrokeRecord record, BankCache bank, RecognitionConfiguration config, string method, bool cached)
    {
        try
        {
            Point2[] raw = record.GetPoints();
            PreparedStroke? query = method == "protractor" ? null : RecognitionEngine.Prepare(raw);
            PreparedProtractor? nativeQuery = method == "protractor" ? RecognitionEngine.PrepareProtractor(raw, config.ProtractorOrientationSensitive) : null;
            var bestByClass = new Dictionary<string, RankedClass>(bank.Classes, StringComparer.Ordinal);
            for (int i = 0; i < bank.Records.Length; i++)
            {
                StrokeRecord template = bank.Records[i];
                double score;
                if (method == "protractor")
                {
                    PreparedProtractor native = cached ? bank.Native![i] : RecognitionEngine.PrepareProtractor(template.GetPoints(), config.ProtractorOrientationSensitive);
                    score = RecognitionEngine.ScoreProtractor(nativeQuery!, native);
                }
                else
                {
                    PreparedStroke shared = cached ? bank.Shared![i] : RecognitionEngine.Prepare(template.GetPoints());
                    score = ScoreShared(query!, shared, config, method);
                }
                if (!double.IsFinite(score)) throw new ArithmeticException("The scorer returned a nonfinite value.");
                if (!bestByClass.TryGetValue(template.Label, out RankedClass? previous) ||
                    RecognitionEngine.CompareRank(score, template.SampleId, previous.Score, previous.TemplateId) < 0)
                    bestByClass[template.Label] = new RankedClass(template.Label, template.SampleId, score);
            }
            RankedClass[] ordered = bestByClass.Values.ToArray();
            Array.Sort(ordered, (x, y) => RecognitionEngine.CompareRank(x.Score, x.TemplateId, y.Score, y.TemplateId));
            return new QueryResult(record.SampleId, ordered.Take(3).ToArray());
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException)
        {
            // Keep the failed query in latency/coverage denominators; do not silently drop it.
            return new QueryResult(record.SampleId, [], error.GetType().Name + ": " + error.Message);
        }
    }

    private static double ScoreShared(PreparedStroke query, PreparedStroke template, RecognitionConfiguration config, string method) => method switch
    {
        "rms" => RecognitionEngine.ScoreRms(query, template, config.TiltDegrees),
        "area" => RecognitionEngine.ScoreArea(query, template, config.TiltDegrees),
        "combined" => RecognitionEngine.ScoreCombined(query, template, config.AreaWeight, config.RmsScale, config.AreaScale, config.TiltDegrees),
        "dtw" => RecognitionEngine.ScoreDtw(query, template, config.DtwWindow),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static SupportTiming MeasureScoringOnly(BankCache measuredBank, RecognitionConfiguration config,
        string method, StrokeRecord[] warmup, bool cached)
    {
        BankCache bank = cached ? measuredBank : BuildBank(measuredBank.Records, config, method, true);
        StrokeRecord[] source = warmup.Take(32).ToArray();
        PreparedStroke[]? queries = method == "protractor" ? null : source.Select(r => RecognitionEngine.Prepare(r.GetPoints())).ToArray();
        PreparedProtractor[]? native = method == "protractor" ? source.Select(r => RecognitionEngine.PrepareProtractor(r.GetPoints(), config.ProtractorOrientationSensitive)).ToArray() : null;
        // Warm the support wrapper before its allocation counter and stopwatch interval.
        for (int i = 0; i < source.Length; i++) sink = ScoreAll(i);
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        int completed = 0;
        do { sink = ScoreAll(completed % source.Length); completed++; }
        while (completed < 200 || Stopwatch.GetTimestamp() - start < Stopwatch.Frequency / 10);
        long ticks = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        GC.KeepAlive(bank); GC.KeepAlive(queries); GC.KeepAlive(native);
        return new SupportTiming(completed, ticks * 1000.0 / Stopwatch.Frequency,
            ticks * 1e9 / Stopwatch.Frequency / completed, (double)bytes / completed,
            "Prepared development query against prepared bank; scores only, no raw conversion, preparation, ranking or result. Uncached variant uses a separate temporary cache for this attribution diagnostic.");

        double ScoreAll(int index)
        {
            double total = 0;
            for (int i = 0; i < bank.Records.Length; i++)
                total += method == "protractor" ? RecognitionEngine.ScoreProtractor(native![index], bank.Native![i])
                    : ScoreShared(queries![index], bank.Shared![i], config, method);
            if (!double.IsFinite(total)) throw new ArithmeticException("Nonfinite scoring-only observation.");
            return total;
        }
    }

    private static void Consume(QueryResult result)
    {
        foreach (RankedClass rank in result.TopThree) sink += rank.Score;
        GC.KeepAlive(result);
    }

    private static PointCounts CountPoints(StrokeRecord[] records)
    {
        int[] counts = records.Select(r => r.Strokes[0].Count).Order().ToArray();
        return new PointCounts(counts.Length, counts[0], counts[(counts.Length - 1) / 2],
            counts[(int)Math.Ceiling(.95 * counts.Length) - 1], counts[^1], counts.Average(),
            counts.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count()));
    }

    private static double Percentile(double[] sorted, double proportion) =>
        sorted[Math.Max(0, (int)Math.Ceiling(proportion * sorted.Length) - 1)];

    private static string CpuDescription()
    {
        string? windows = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(windows)) return windows;
        if (File.Exists("/proc/cpuinfo"))
        {
            string? line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(x => x.StartsWith("model name", StringComparison.Ordinal));
            if (line is not null) return line[(line.IndexOf(':') + 1)..].Trim();
        }
        return "Unavailable; see architecture and runtime metadata.";
    }
}
