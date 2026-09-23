using System.Text.Json;

namespace PolylineKit.Recognition;

public static class EvaluationRunner
{
    private sealed record PairObservation(string Id, string Label, double Rms, double Area, int Angle);
    private sealed record DevelopmentRow(StrokeRecord Query, int Seed, PairObservation[] Pairs);
    private static PairObservation Winner(DevelopmentRow row, Func<PairObservation, double> score) =>
        row.Pairs.OrderBy(score).ThenBy(p => p.Id, StringComparer.Ordinal).First();

    public static void Develop(string data, string output, string revision)
    {
        Directory.CreateDirectory(output);
        var configurations = new List<RecognitionConfiguration>();
        var summaries = new List<object>();
        foreach (string dataset in new[] { "dollar", "pendigits" })
        {
            var all = RecognitionFiles.Load(data, dataset);
            var (pool, queries) = RecognitionSplits.Development(all, dataset);
            if (pool.Select(r => r.SampleId).Intersect(queries.Select(r => r.SampleId), StringComparer.Ordinal).Any())
                throw new InvalidDataException("Development leakage.");
            var banks = RecognitionFiles.Seeds.ToDictionary(seed => seed, seed => RecognitionSplits.Select(pool, dataset, seed, false));
            var selected = banks.Values.SelectMany(x => x).Concat(queries).DistinctBy(r => r.SampleId).ToArray();
            var prepared = selected.ToDictionary(r => r.SampleId, r => RecognitionEngine.Prepare(r.GetPoints()), StringComparer.Ordinal);
            var geometry = new List<(int Tilt, int Correct, List<DevelopmentRow> Rows)>();
            foreach (int tilt in dataset == "dollar" ? new[] { 0, 15 } : new[] { 0 })
            {
                var rows = new List<DevelopmentRow>();
                foreach (var bank in banks)
                foreach (var query in queries)
                {
                    var pairs = bank.Value.Select(template =>
                    {
                        var pair = RecognitionEngine.ScorePair(prepared[query.SampleId], prepared[template.SampleId], tilt);
                        return new PairObservation(template.SampleId, template.Label, pair.Rms, pair.Area, pair.Alignment.RotationDegrees);
                    }).ToArray();
                    rows.Add(new(query, bank.Key, pairs));
                }
                int correct = rows.Count(r => Winner(r, p => p.Rms).Label == r.Query.Label);
                geometry.Add((tilt, correct, rows));
                summaries.Add(new { dataset, method = "rms", tiltDegrees = tilt, correct, queries = rows.Count });
                Console.WriteLine($"development {dataset}, tilt {tilt}: {correct}/{rows.Count}");
            }
            var chosen = geometry.OrderByDescending(g => g.Correct).ThenBy(g => g.Tilt).First();
            double r0 = Median(chosen.Rows.SelectMany(r => r.Pairs).Select(p => p.Rms));
            double a0 = Median(chosen.Rows.SelectMany(r => r.Pairs).Select(p => p.Area));
            bool rmsScaleFallback = r0 == 0, areaScaleFallback = a0 == 0;
            if (rmsScaleFallback) r0 = 1;
            if (areaScaleFallback) a0 = 1;
            var weights = new List<(double Weight, int Correct)>();
            foreach (double weight in new[] { 0, .25, .5, .75, 1 })
            {
                int correct = chosen.Rows.Count(r => Winner(r, p => RecognitionEngine.Combine(p.Rms, p.Area, weight, r0, a0)).Label == r.Query.Label);
                weights.Add((weight, correct));
                summaries.Add(new { dataset, method = "combined", areaWeight = weight, correct, queries = chosen.Rows.Count, rmsScale = r0, areaScale = a0 });
            }
            double selectedWeight = weights.OrderByDescending(w => w.Correct).ThenBy(w => w.Weight).First().Weight;
            var protractorChoices = new List<(bool Sensitive, int Correct)>();
            foreach (bool sensitive in dataset == "dollar" ? new[] { true, false } : new[] { true })
            {
                var native = selected.ToDictionary(r => r.SampleId, r => RecognitionEngine.PrepareProtractor(r.GetPoints(), sensitive), StringComparer.Ordinal);
                int correct = 0;
                foreach (var bank in banks)
                foreach (var query in queries)
                {
                    var best = bank.Value.OrderBy(t => RecognitionEngine.ScoreProtractor(native[query.SampleId], native[t.SampleId]))
                        .ThenBy(t => t.SampleId, StringComparer.Ordinal).First();
                    if (best.Label == query.Label) correct++;
                }
                protractorChoices.Add((sensitive, correct));
                summaries.Add(new { dataset, method = "protractor", orientationSensitive = sensitive, correct, queries = queries.Length * banks.Count });
            }
            bool selectedOrientation = protractorChoices.OrderByDescending(c => c.Correct).ThenByDescending(c => c.Sensitive).First().Sensitive;
            int selectedWindow = 8;
            if (dataset == "pendigits")
            {
                var windows = new List<(int Window, int Correct)>();
                foreach (int window in new[] { 8, 16, 63 })
                {
                    int correct = 0;
                    foreach (var bank in banks)
                    foreach (var query in queries)
                    {
                        var best = bank.Value.OrderBy(t => RecognitionEngine.ScoreDtw(prepared[query.SampleId], prepared[t.SampleId], window))
                            .ThenBy(t => t.SampleId, StringComparer.Ordinal).First();
                        if (best.Label == query.Label) correct++;
                    }
                    windows.Add((window, correct));
                    summaries.Add(new { dataset, method = "dtw", window, correct, queries = queries.Length * banks.Count });
                    Console.WriteLine($"development DTW window {window}: {correct}/{queries.Length * banks.Count}");
                }
                selectedWindow = windows.OrderByDescending(w => w.Correct).ThenBy(w => w.Window).First().Window;
            }
            var config = new RecognitionConfiguration(dataset, chosen.Tilt, selectedWeight, r0, a0, selectedOrientation, selectedWindow);
            configurations.Add(config);
            RecognitionFiles.Write(Path.Combine(output, dataset + "-development.json"), new
            {
                protocol = RecognitionFiles.Protocol, sourceRevision = revision, configuration = config,
                scaleFallback = new { rmsScaleFallback, areaScaleFallback },
                poolIds = pool.Select(r => r.SampleId), queryIds = queries.Select(r => r.SampleId),
                templateBanks = banks.Select(b => new { seed = b.Key, templateIds = b.Value.Select(t => t.SampleId) }),
                inputSha256 = RecognitionFiles.Hash(Path.Combine(data, dataset + ".jsonl")),
                splitScope = dataset == "dollar" ? "Pilot repetition-disjoint; one writer only" : "Training-only stratified record split; writer IDs unavailable"
            });
        }
        RecognitionFiles.Write(Path.Combine(output, "configuration.json"), configurations);
        RecognitionFiles.Write(Path.Combine(output, "development-results.json"), summaries);
    }

    public static void Freeze(string data, string development, string output, string revision)
    {
        var configs = RecognitionFiles.Read<RecognitionConfiguration[]>(Path.Combine(development, "configuration.json"));
        ValidateConfigurations(configs);
        var banks = new List<BankDefinition>(); var hashes = new Dictionary<string, string>();
        foreach (var config in configs)
        {
            var rows = RecognitionFiles.Load(data, config.Dataset);
            hashes[config.Dataset] = RecognitionFiles.Hash(Path.Combine(data, config.Dataset + ".jsonl"));
            using var developmentDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(development, config.Dataset + "-development.json")));
            var developmentRoot = developmentDocument.RootElement;
            if (developmentRoot.GetProperty("protocol").GetString() != RecognitionFiles.Protocol ||
                developmentRoot.GetProperty("inputSha256").GetString() != hashes[config.Dataset] ||
                developmentRoot.GetProperty("configuration").Deserialize<RecognitionConfiguration>(RecognitionFiles.Json) != config)
                throw new InvalidDataException("Development settings or input hash differ; cannot freeze.");
            var records = rows.ToDictionary(r => r.SampleId, StringComparer.Ordinal);
            if (config.Dataset == "dollar")
            {
                foreach (var writer in rows.Where(r => r.Split == "main").Select(r => r.WriterId).Distinct().Order(StringComparer.Ordinal))
                foreach (int seed in RecognitionFiles.Seeds)
                {
                    var pool = rows.Where(r => r.Split == "main" && r.WriterId != writer).ToArray();
                    var selected = RecognitionSplits.Select(pool, config.Dataset, seed, true);
                    banks.Add(new(config.Dataset, "main", writer, seed, selected.Select(r => r.SampleId).ToArray(),
                        rows.Where(r => r.Split == "main" && r.WriterId == writer).Select(r => r.SampleId).ToArray()));
                }
            }
            else
                foreach (int seed in RecognitionFiles.Seeds)
                    banks.Add(new(config.Dataset, "test", null, seed,
                        RecognitionSplits.Select(rows.Where(r => r.Split == "train").ToArray(), config.Dataset, seed, false).Select(r => r.SampleId).ToArray(),
                        rows.Where(r => r.Split == "test").Select(r => r.SampleId).ToArray()));
            foreach (var bank in banks.Where(b => b.Dataset == config.Dataset)) RecognitionSplits.ValidateBank(bank, records);
        }
        RecognitionFiles.Write(output, new FrozenEvaluation(RecognitionFiles.Protocol, revision, hashes, configs, banks.ToArray()));
        Console.WriteLine($"Frozen {banks.Count} banks; no held-out scoring performed.");
    }

    public static void Evaluate(string data, string frozenPath, string output, string revision)
    {
        var frozen = LoadFrozen(data, frozenPath);
        Directory.CreateDirectory(output);
        foreach (var config in frozen.Configurations)
        {
            var rows = RecognitionFiles.Load(data, config.Dataset);
            var records = rows.ToDictionary(r => r.SampleId, StringComparer.Ordinal);
            var prepared = new Dictionary<string, PreparedStroke>(StringComparer.Ordinal);
            var native = new Dictionary<string, PreparedProtractor>(StringComparer.Ordinal);
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            // No held-out label is used by preparation. Unsupported records are retained below.
            foreach (var row in rows.Where(r => r.Supported))
                try
                {
                    prepared[row.SampleId] = RecognitionEngine.Prepare(row.GetPoints());
                    native[row.SampleId] = RecognitionEngine.PrepareProtractor(row.GetPoints(), config.ProtractorOrientationSensitive);
                }
                catch (ArgumentException e) { errors[row.SampleId] = e.GetType().Name + ": " + e.Message; }
            using var writer = RecognitionFiles.PredictionWriter(Path.Combine(output, config.Dataset + "-predictions.jsonl.gz"));
            long count = 0;
            foreach (var bank in frozen.Banks.Where(b => b.Dataset == config.Dataset))
            {
                RecognitionSplits.ValidateBank(bank, records);
                if (bank.TemplateIds.Any(errors.ContainsKey)) throw new InvalidDataException("A frozen template cannot be prepared; stop rather than silently replacing it.");
                foreach (string id in bank.QueryIds)
                {
                    var query = records[id];
                    string[] methods = RecognitionFiles.Methods(config.Dataset);
                    if (!query.Supported || errors.ContainsKey(id))
                    {
                        foreach (string method in methods) Write(new(config.Dataset, bank.Seed, id, query.WriterId, query.Label, method,
                            query.Supported ? "failed" : "unsupported", Error: errors.GetValueOrDefault(id) ?? query.ExclusionReason));
                        continue;
                    }
                    var pairs = bank.TemplateIds.Select(t =>
                    {
                        var value = RecognitionEngine.ScorePair(prepared[id], prepared[t], config.TiltDegrees);
                        return new PairObservation(t, records[t].Label, value.Rms, value.Area, value.Alignment.RotationDegrees);
                    }).ToArray();
                    foreach (string method in methods)
                    {
                        double Score(PairObservation p) => method switch
                        {
                            "rms" => p.Rms, "area" => p.Area,
                            "combined" => RecognitionEngine.Combine(p.Rms, p.Area, config.AreaWeight, config.RmsScale, config.AreaScale),
                            "protractor" => RecognitionEngine.ScoreProtractor(native[id], native[p.Id]),
                            "dtw" => RecognitionEngine.ScoreDtw(prepared[id], prepared[p.Id], config.DtwWindow),
                            _ => throw new InvalidOperationException()
                        };
                        var ranked = pairs.Select(p => (Pair: p, Score: Score(p))).OrderBy(p => p.Score).ThenBy(p => p.Pair.Id, StringComparer.Ordinal).ToArray();
                        if (ranked.Any(r => !double.IsFinite(r.Score))) throw new InvalidDataException("Nonfinite score.");
                        var best = ranked[0];
                        double otherClass = ranked.First(r => r.Pair.Label != best.Pair.Label).Score;
                        Write(new(config.Dataset, bank.Seed, id, query.WriterId, query.Label, method, "ok", best.Pair.Label,
                            best.Pair.Id, best.Score, best.Pair.Rms, best.Pair.Area, best.Pair.Angle, otherClass - best.Score));
                    }
                }
                Console.WriteLine($"evaluated {config.Dataset}/{bank.HeldOutWriter ?? "test"}/seed {bank.Seed}");
            }
            void Write(Prediction prediction) { writer.WriteLine(JsonSerializer.Serialize(prediction, RecognitionFiles.Json)); count++; }
            writer.Flush();
            RecognitionFiles.Write(Path.Combine(output, config.Dataset + "-evaluation.json"), new
            {
                protocol = frozen.Protocol, sourceRevision = revision, freezeSha256 = RecognitionFiles.Hash(frozenPath),
                inputSha256 = frozen.InputHashes[config.Dataset], configuration = config, predictions = count,
                scalarForced = AppContext.TryGetSwitch("PolylineKit.DisableSimd", out bool disabled) && disabled,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                vector128 = System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated,
                vector256 = System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated,
                auxiliaryScoreMeaning = "RMS/area/angle are shared-preparation diagnostics; Protractor/DTW native scores use their declared preparation."
            });
        }
    }

    public static FrozenEvaluation LoadFrozen(string data, string path)
    {
        var frozen = RecognitionFiles.Read<FrozenEvaluation>(path);
        if (frozen.Protocol != RecognitionFiles.Protocol) throw new InvalidDataException("Unknown frozen protocol.");
        ValidateConfigurations(frozen.Configurations);
        if (!frozen.InputHashes.Keys.Order(StringComparer.Ordinal).SequenceEqual(new[] { "dollar", "pendigits" }) ||
            frozen.Banks.Length != 33 || frozen.Banks.Count(b => b.Dataset == "dollar") != 30 ||
            frozen.Banks.Select(b => (b.Dataset, b.HeldOutWriter, b.Seed)).Distinct().Count() != 33)
            throw new InvalidDataException("Incomplete frozen evaluation.");
        foreach (var pair in frozen.InputHashes)
        {
            if (RecognitionFiles.Hash(Path.Combine(data, pair.Key + ".jsonl")) != pair.Value) throw new InvalidDataException("Frozen input hash differs.");
            var records = RecognitionFiles.Load(data, pair.Key).ToDictionary(r => r.SampleId, StringComparer.Ordinal);
            foreach (var bank in frozen.Banks.Where(b => b.Dataset == pair.Key)) RecognitionSplits.ValidateBank(bank, records);
        }
        return frozen;
    }
    private static void ValidateConfigurations(RecognitionConfiguration[] configurations)
    {
        if (!configurations.Select(c => c.Dataset).Order(StringComparer.Ordinal).SequenceEqual(new[] { "dollar", "pendigits" }) ||
            configurations.Any(c => (c.TiltDegrees != 0 && (c.Dataset != "dollar" || c.TiltDegrees != 15)) ||
                !new[] { 0d, .25, .5, .75, 1 }.Contains(c.AreaWeight) ||
                !double.IsFinite(c.RmsScale) || c.RmsScale <= 0 || !double.IsFinite(c.AreaScale) || c.AreaScale <= 0 ||
                !new[] { 8, 16, 63 }.Contains(c.DtwWindow) || (c.Dataset == "pendigits" && !c.ProtractorOrientationSensitive)))
            throw new InvalidDataException("Configuration falls outside the frozen protocol.");
    }
    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
