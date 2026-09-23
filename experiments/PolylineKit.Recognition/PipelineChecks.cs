namespace PolylineKit.Recognition;

/// <summary>Synthetic protocol-integrity checks. No downloaded records or held-out scores are read.</summary>
public static class PipelineChecks
{
    private static int passed;

    public static int Run()
    {
        passed = 0;
        CheckDollarBank();
        CheckDigitBank();
        CheckDevelopmentSplits();
        CheckSelection();
        CheckRecordOwnership();
        Console.WriteLine($"Recognition pipeline checks: {passed}");
        return passed;
    }

    private static void CheckDollarBank()
    {
        var (bank, records) = DollarBank();
        Accept("valid writer-held-out bank", () => RecognitionSplits.ValidateBank(bank, records));
        Reject("duplicate template ID", () => RecognitionSplits.ValidateBank(bank with
            { TemplateIds = [bank.TemplateIds[0], .. bank.TemplateIds.Skip(1).SkipLast(1), bank.TemplateIds[0]] }, records));
        Reject("duplicate query ID", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = [.. bank.QueryIds, bank.QueryIds[0]] }, records));
        Reject("query/template self match", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = [.. bank.QueryIds, bank.TemplateIds[0]] }, records));
        Reject("missing template record", () => RecognitionSplits.ValidateBank(bank with
            { TemplateIds = ["dollar/not-present", .. bank.TemplateIds.Skip(1)] }, records));
        Reject("missing query record", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = [.. bank.QueryIds, "dollar/not-present"] }, records));
        Reject("omitted supported held-out query", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = bank.QueryIds.Skip(1).ToArray() }, records));
        string unsupported = bank.QueryIds.Single(id => !records[id].Supported);
        Reject("omitted unsupported held-out query", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = bank.QueryIds.Where(id => id != unsupported).ToArray() }, records));
        Reject("wrong held-out writer", () => RecognitionSplits.ValidateBank(bank with { HeldOutWriter = "s03" }, records));
        Reject("missing held-out writer", () => RecognitionSplits.ValidateBank(bank with { HeldOutWriter = null }, records));
        Reject("empty held-out writer", () => RecognitionSplits.ValidateBank(bank with { HeldOutWriter = " " }, records));
        Reject("incorrect dollar split metadata", () => RecognitionSplits.ValidateBank(bank with { Split = "pilot" }, records));
        Reject("unknown seed", () => RecognitionSplits.ValidateBank(bank with { Seed = 1 }, records));
        Reject("unknown dataset", () => RecognitionSplits.ValidateBank(bank with { Dataset = "unknown" }, records));
        Reject("template count changed", () => RecognitionSplits.ValidateBank(bank with
            { TemplateIds = bank.TemplateIds.Skip(1).ToArray() }, records));
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.WriterId = "s02", "template leaks held-out writer");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.WriterId = "s04", "same writer twice within class");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.WriterId = null, "missing template writer is not diversity");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.WriterId = "", "empty template writer is not diversity");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.Supported = false, "unsupported template excluded");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.Split = "pilot", "pilot template in main bank");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.Dataset = "pendigits", "foreign dataset template");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.Label = "extra-class", "class budget changed");
        MutateThenReject(bank, records, bank.QueryIds[0], r => r.WriterId = "s03", "query writer differs from held-out writer");
        MutateThenReject(bank, records, bank.QueryIds[0], r => r.Split = "pilot", "pilot query in main holdout");
        MutateThenReject(bank, records, bank.QueryIds[0], r => r.Dataset = "pendigits", "foreign dataset query");
    }

    private static void CheckDigitBank()
    {
        var (bank, records) = DigitBank();
        Accept("valid official train/test bank", () => RecognitionSplits.ValidateBank(bank, records));
        Reject("incorrect digit split metadata", () => RecognitionSplits.ValidateBank(bank with { Split = "train" }, records));
        Reject("digit writer identity must not be inferred", () => RecognitionSplits.ValidateBank(bank with { HeldOutWriter = "s02" }, records));
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.Split = "test", "test record used as template");
        MutateThenReject(bank, records, bank.QueryIds[0], r => r.Split = "train", "train record used as official test query");
        MutateThenReject(bank, records, bank.TemplateIds[0], r => r.Label = "other-digit", "digit class budget changed");
        Reject("omitted official test record", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = bank.QueryIds.SkipLast(1).ToArray() }, records));
        Reject("extra official test ID", () => RecognitionSplits.ValidateBank(bank with
            { QueryIds = [.. bank.QueryIds, "pendigits/absent"] }, records));
        Reject("duplicate digit template", () => RecognitionSplits.ValidateBank(bank with
            { TemplateIds = [.. bank.TemplateIds.SkipLast(1), bank.TemplateIds[0]] }, records));
        var missing = new Dictionary<string, StrokeRecord>(records, StringComparer.Ordinal);
        missing.Remove(bank.QueryIds[0]);
        Reject("missing record behind query ID", () => RecognitionSplits.ValidateBank(bank, missing));
    }

    private static void CheckDevelopmentSplits()
    {
        var pilot = new List<StrokeRecord>();
        for (int label = 0; label < 16; label++)
        foreach (string speed in new[] { "slow", "medium", "fast" })
        for (int repetition = 1; repetition <= 10; repetition++)
        {
            StrokeRecord record = Record($"dollar/pilot/{label:D2}/{speed}/{repetition:D2}", "dollar", "pilot", $"class-{label:D2}", "s01");
            record.Speed = speed; record.Repetition = repetition; pilot.Add(record);
        }
        pilot.Add(Record("dollar/main/not-development", "dollar", "main", "class-00", "s02"));
        var dollar = RecognitionSplits.Development(pilot.ToArray(), "dollar");
        True("pilot template/validation counts", dollar.Pool.Length == 240 && dollar.Queries.Length == 240);
        True("pilot disjoint repetitions", dollar.Pool.All(r => r.Repetition <= 5) && dollar.Queries.All(r => r.Repetition > 5));
        True("main records never enter development", dollar.Pool.Concat(dollar.Queries).All(r => r.Split == "pilot"));
        True("pilot IDs disjoint", !dollar.Pool.Select(r => r.SampleId).Intersect(dollar.Queries.Select(r => r.SampleId), StringComparer.Ordinal).Any());
        foreach (int seed in RecognitionFiles.Seeds)
        {
            StrokeRecord[] bank = RecognitionSplits.Select(dollar.Pool, "dollar", seed, false);
            True("pilot bank uses three distinct speeds", bank.GroupBy(r => r.Label).All(g => g.Select(r => r.Speed).Distinct().Count() == 3));
            True("pilot validation records are not templates", !bank.Select(r => r.SampleId).Intersect(dollar.Queries.Select(r => r.SampleId), StringComparer.Ordinal).Any());
        }
        var digits = new List<StrokeRecord>();
        for (int label = 0; label < 10; label++)
        for (int repetition = 0; repetition < 10; repetition++)
            digits.Add(Record($"pendigits/train/{label}/{repetition:D2}", "pendigits", "train", label.ToString(System.Globalization.CultureInfo.InvariantCulture), null));
        digits.Add(Record("pendigits/test/untouched", "pendigits", "test", "0", null));
        StrokeRecord unsupported = Record("pendigits/train/unsupported", "pendigits", "train", "0", null);
        unsupported.Supported = false; digits.Add(unsupported);
        var digitSplit = RecognitionSplits.Development(digits.ToArray(), "pendigits");
        var shuffled = RecognitionSplits.Development(digits.AsEnumerable().Reverse().ToArray(), "pendigits");
        True("digit development is class-stratified 80/20", digitSplit.Pool.Length == 80 && digitSplit.Queries.Length == 20 &&
            digitSplit.Pool.GroupBy(r => r.Label).All(g => g.Count() == 8) && digitSplit.Queries.GroupBy(r => r.Label).All(g => g.Count() == 2));
        True("digit split disjoint", !digitSplit.Pool.Select(r => r.SampleId).Intersect(digitSplit.Queries.Select(r => r.SampleId), StringComparer.Ordinal).Any());
        True("digit development excludes official test and unsupported records", digitSplit.Pool.Concat(digitSplit.Queries).All(r => r.Split == "train" && r.Supported));
        True("digit split independent of enumeration order", digitSplit.Pool.Select(r => r.SampleId).SequenceEqual(shuffled.Pool.Select(r => r.SampleId)) &&
            digitSplit.Queries.Select(r => r.SampleId).SequenceEqual(shuffled.Queries.Select(r => r.SampleId)));
        True("digit writer independence is not invented", digitSplit.Pool.Concat(digitSplit.Queries).All(r => r.WriterId is null));
    }

    private static void CheckSelection()
    {
        var (dollar, dollarRecords) = DollarBank();
        StrokeRecord[] dollarPool = dollar.TemplateIds.Select(id => dollarRecords[id]).ToArray();
        var (digit, digitRecords) = DigitBank();
        StrokeRecord[] digitPool = digit.TemplateIds.Select(id => digitRecords[id]).ToArray();
        foreach (int seed in RecognitionFiles.Seeds)
        {
            StrokeRecord[] forward = RecognitionSplits.Select(dollarPool, "dollar", seed, true);
            StrokeRecord[] reverse = RecognitionSplits.Select(dollarPool.Reverse().ToArray(), "dollar", seed, true);
            True("writer-diverse selection stable under enumeration", forward.Select(r => r.SampleId).SequenceEqual(reverse.Select(r => r.SampleId)));
            True("writer-diverse budget per class", forward.GroupBy(r => r.Label).All(g => g.Count() == 3 && g.Select(r => r.WriterId).Distinct().Count() == 3));
            StrokeRecord[] a = RecognitionSplits.Select(digitPool, "pendigits", seed, false);
            StrokeRecord[] b = RecognitionSplits.Select(digitPool.Reverse().ToArray(), "pendigits", seed, false);
            True("digit selection stable under enumeration", a.Select(r => r.SampleId).SequenceEqual(b.Select(r => r.SampleId)));
            True("digit selection is ordinal output", a.Select(r => r.SampleId).SequenceEqual(a.Select(r => r.SampleId).Order(StringComparer.Ordinal)));
        }
        Reject("insufficient digit pool cannot weaken budget", () => RecognitionSplits.Select(digitPool.Skip(1).ToArray(), "pendigits", 1729, false));
        Reject("insufficient writer-diverse pool cannot weaken budget", () => RecognitionSplits.Select(dollarPool.Skip(1).ToArray(), "dollar", 1729, true));
    }

    private static void CheckRecordOwnership()
    {
        StrokeRecord record = Record("synthetic/owned", "dollar", "pilot", "corner", "s01");
        var points = record.GetPoints();
        record.Strokes[0][0].X = 17;
        True("record conversion returns coordinate snapshot", points[0].X == 0);
        record.Supported = false;
        RejectArgument("unsupported record cannot silently become stroke", () => record.GetPoints());
        record.Supported = true;
        record.Strokes.Add([new RecordedPoint { X = 7, Y = 8 }, new RecordedPoint { X = 9, Y = 10 }]);
        RejectArgument("two strokes are never joined", () => record.GetPoints());
        True("gesture baseline set excludes digit DTW", RecognitionFiles.Methods("dollar").SequenceEqual(new[] { "rms", "area", "combined", "protractor" }));
        True("digit baseline set includes DTW", RecognitionFiles.Methods("pendigits").SequenceEqual(new[] { "rms", "area", "combined", "protractor", "dtw" }));
    }

    private static (BankDefinition Bank, Dictionary<string, StrokeRecord> Records) DollarBank()
    {
        var templates = new List<StrokeRecord>(); var queries = new List<StrokeRecord>();
        for (int label = 0; label < 16; label++)
        {
            foreach (string writer in new[] { "s03", "s04", "s05" })
                templates.Add(Record($"dollar/{writer}/{label:D2}", "dollar", "main", $"class-{label:D2}", writer));
            queries.Add(Record($"dollar/s02/{label:D2}", "dollar", "main", $"class-{label:D2}", "s02"));
        }
        var unsupported = Record("dollar/s02/unsupported", "dollar", "main", "class-00", "s02");
        unsupported.Supported = false; unsupported.ExclusionReason = "synthetic exclusion"; queries.Add(unsupported);
        return (new BankDefinition("dollar", "main", "s02", 1729, templates.Select(r => r.SampleId).ToArray(), queries.Select(r => r.SampleId).ToArray()),
            templates.Concat(queries).ToDictionary(r => r.SampleId, StringComparer.Ordinal));
    }

    private static (BankDefinition Bank, Dictionary<string, StrokeRecord> Records) DigitBank()
    {
        var templates = new List<StrokeRecord>(); var queries = new List<StrokeRecord>();
        for (int label = 0; label < 10; label++)
        {
            string name = label.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < 5; i++) templates.Add(Record($"pendigits/train/{label}/{i}", "pendigits", "train", name, null));
            queries.Add(Record($"pendigits/test/{label}", "pendigits", "test", name, null));
        }
        var unsupported = Record("pendigits/test/unsupported", "pendigits", "test", "0", null);
        unsupported.Supported = false; unsupported.ExclusionReason = "multiple pen-down strokes"; queries.Add(unsupported);
        return (new BankDefinition("pendigits", "test", null, 1729, templates.Select(r => r.SampleId).ToArray(), queries.Select(r => r.SampleId).ToArray()),
            templates.Concat(queries).ToDictionary(r => r.SampleId, StringComparer.Ordinal));
    }

    private static StrokeRecord Record(string id, string dataset, string split, string label, string? writer) => new()
    {
        SampleId = id, Dataset = dataset, Split = split, Label = label, WriterId = writer, Supported = true,
        Strokes = [[new RecordedPoint { X = 0, Y = 0 }, new RecordedPoint { X = 1, Y = 1 }]]
    };

    private static void MutateThenReject(BankDefinition bank, Dictionary<string, StrokeRecord> records,
        string id, Action<StrokeRecord> mutation, string name)
    {
        StrokeRecord original = records[id];
        StrokeRecord changed = Record(original.SampleId, original.Dataset, original.Split, original.Label, original.WriterId);
        changed.Supported = original.Supported;
        var altered = new Dictionary<string, StrokeRecord>(records, StringComparer.Ordinal) { [id] = changed };
        mutation(changed);
        Reject(name, () => RecognitionSplits.ValidateBank(bank, altered));
    }

    private static void Accept(string name, Action action)
    {
        try { action(); passed++; } catch (Exception e) { throw new InvalidOperationException("Pipeline rejected " + name, e); }
    }
    private static void Reject(string name, Action action)
    {
        try { action(); } catch (InvalidDataException) { passed++; return; }
        throw new InvalidOperationException("Pipeline accepted " + name);
    }
    private static void RejectArgument(string name, Action action)
    {
        try { action(); } catch (ArgumentException) { passed++; return; }
        throw new InvalidOperationException("Pipeline accepted " + name);
    }
    private static void True(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("Pipeline: " + name); passed++; }
}
