using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolylineKit;

namespace PolylineKit.Recognition;

public sealed class RecordedPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double? T { get; set; }
}

public sealed class StrokeRecord
{
    public string SampleId { get; set; } = "";
    public string Dataset { get; set; } = "";
    public string Split { get; set; } = "";
    public string? WriterId { get; set; }
    public string? Session { get; set; }
    public string Label { get; set; } = "";
    public string? Speed { get; set; }
    public int? Repetition { get; set; }
    public bool Supported { get; set; }
    public string? ExclusionReason { get; set; }
    public List<List<RecordedPoint>> Strokes { get; set; } = [];
    public Point2[] GetPoints()
    {
        if (!Supported || Strokes.Count != 1) throw new ArgumentException("Only supported unistrokes can be prepared.");
        return Strokes[0].Select(p => new Point2(p.X, p.Y)).ToArray();
    }
}

public sealed record RecognitionConfiguration(string Dataset, int TiltDegrees, double AreaWeight,
    double RmsScale, double AreaScale, bool ProtractorOrientationSensitive, int DtwWindow);
public sealed record BankDefinition(string Dataset, string Split, string? HeldOutWriter, int Seed,
    string[] TemplateIds, string[] QueryIds);
public sealed record FrozenEvaluation(string Protocol, string SourceRevision,
    Dictionary<string, string> InputHashes, RecognitionConfiguration[] Configurations,
    BankDefinition[] Banks);

public sealed record Prediction(string Dataset, int Seed, string SampleId, string? WriterId,
    string TrueLabel, string Method, string Status, string? PredictedLabel = null,
    string? TemplateId = null, double? Score = null, double? Rms = null, double? Area = null,
    int? RotationDegrees = null, double? Margin = null, string? Error = null);

public static class RecognitionFiles
{
    public const string Protocol = "polylinekit-recognition-v1";
    public static readonly int[] Seeds = [1729, 2718, 31415];
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Empty JSON: {path}");
    public static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(Json) { WriteIndented = true }) + "\n");
    }
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static string Rank(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static StrokeRecord[] Load(string directory, string dataset)
    {
        StrokeRecord[] rows = File.ReadLines(Path.Combine(directory, dataset + ".jsonl"))
            .Select(line => JsonSerializer.Deserialize<StrokeRecord>(line, Json) ?? throw new InvalidDataException("Null sample"))
            .OrderBy(r => r.SampleId, StringComparer.Ordinal).ToArray();
        if (rows.Length == 0 || rows.Select(r => r.SampleId).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new InvalidDataException("Empty dataset or duplicate IDs.");
        if (rows.Any(r => r.Dataset != dataset || r.Label.Length == 0 || r.SampleId.Length == 0))
            throw new InvalidDataException("Invalid dataset identity.");
        return rows;
    }
    public static StreamWriter PredictionWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new StreamWriter(new GZipStream(File.Create(path), CompressionLevel.SmallestSize), new UTF8Encoding(false));
    }
    public static string[] Methods(string dataset) => dataset == "dollar"
        ? ["rms", "area", "combined", "protractor"] : ["rms", "area", "combined", "protractor", "dtw"];
}

public static class RecognitionSplits
{
    public static (StrokeRecord[] Pool, StrokeRecord[] Queries) Development(StrokeRecord[] rows, string dataset)
    {
        if (dataset == "dollar")
            return (rows.Where(r => r.Split == "pilot" && r.Repetition <= 5 && r.Supported).ToArray(),
                rows.Where(r => r.Split == "pilot" && r.Repetition > 5).ToArray());
        var pool = new List<StrokeRecord>(); var queries = new List<StrokeRecord>();
        foreach (var group in rows.Where(r => r.Split == "train" && r.Supported).GroupBy(r => r.Label).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(r => RecognitionFiles.Rank($"development|{r.Label}|{r.SampleId}"), StringComparer.Ordinal).ToArray();
            int cut = ordered.Length * 4 / 5;
            pool.AddRange(ordered.Take(cut)); queries.AddRange(ordered.Skip(cut));
        }
        return (pool.ToArray(), queries.ToArray());
    }
    public static StrokeRecord[] Select(StrokeRecord[] pool, string dataset, int seed, bool differentWriters)
    {
        var selected = new List<StrokeRecord>();
        foreach (var group in pool.Where(r => r.Supported).GroupBy(r => r.Label).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ranked = group.OrderBy(r => RecognitionFiles.Rank($"{dataset}|{seed}|{r.WriterId}|{r.Label}|{r.SampleId}"), StringComparer.Ordinal);
            var choices = differentWriters ? ranked.GroupBy(r => r.WriterId, StringComparer.Ordinal).Select(g => g.First()).Take(3)
                : dataset == "dollar" ? ranked.GroupBy(r => r.Speed, StringComparer.Ordinal).Select(g => g.First()).Take(3)
                : ranked.Take(5);
            selected.AddRange(choices);
        }
        int expected = dataset == "dollar" ? 48 : 50;
        int perClass = dataset == "dollar" ? 3 : 5;
        if (selected.Count != expected || selected.GroupBy(r => r.Label).Any(g => g.Count() != perClass))
            throw new InvalidDataException($"Expected {expected} templates with {perClass} per class, got {selected.Count}.");
        return selected.OrderBy(r => r.SampleId, StringComparer.Ordinal).ToArray();
    }
    public static void ValidateBank(BankDefinition bank, IReadOnlyDictionary<string, StrokeRecord> records)
    {
        if (bank.Dataset is not ("dollar" or "pendigits") || !RecognitionFiles.Seeds.Contains(bank.Seed))
            throw new InvalidDataException("Unknown bank dataset or seed.");
        if (bank.TemplateIds.Distinct(StringComparer.Ordinal).Count() != bank.TemplateIds.Length ||
            bank.QueryIds.Distinct(StringComparer.Ordinal).Count() != bank.QueryIds.Length ||
            bank.TemplateIds.Intersect(bank.QueryIds, StringComparer.Ordinal).Any()) throw new InvalidDataException("Bank IDs overlap or repeat.");
        if (bank.TemplateIds.Any(id => !records.ContainsKey(id) || !records[id].Supported || records[id].Dataset != bank.Dataset) ||
            bank.QueryIds.Any(id => !records.ContainsKey(id) || records[id].Dataset != bank.Dataset)) throw new InvalidDataException("Invalid bank sample.");
        if (bank.Dataset == "dollar" && (bank.Split != "main" || string.IsNullOrWhiteSpace(bank.HeldOutWriter) ||
                bank.TemplateIds.Any(id => string.IsNullOrWhiteSpace(records[id].WriterId))))
            throw new InvalidDataException("Dollar banks require main split and explicit template/held-out writer identities.");
        if (bank.Dataset == "pendigits" && (bank.Split != "test" || bank.HeldOutWriter is not null))
            throw new InvalidDataException("Pendigits banks use the official test split without inferred writer identities.");
        if (bank.Dataset == "dollar" && (bank.TemplateIds.Any(id => records[id].Split != "main" || records[id].WriterId == bank.HeldOutWriter) ||
            bank.QueryIds.Any(id => records[id].Split != "main" || records[id].WriterId != bank.HeldOutWriter))) throw new InvalidDataException("Writer leakage.");
        if (bank.Dataset == "pendigits" && (bank.TemplateIds.Any(id => records[id].Split != "train") ||
            bank.QueryIds.Any(id => records[id].Split != "test"))) throw new InvalidDataException("Official holdout leakage.");
        var templates = bank.TemplateIds.Select(id => records[id]).ToArray();
        int perClass = bank.Dataset == "dollar" ? 3 : 5, classes = bank.Dataset == "dollar" ? 16 : 10;
        if (templates.Length != perClass * classes || templates.GroupBy(r => r.Label).Count() != classes ||
            templates.GroupBy(r => r.Label).Any(g => g.Count() != perClass ||
                (bank.Dataset == "dollar" && g.Select(r => r.WriterId).Distinct().Count() != 3)))
            throw new InvalidDataException("Frozen bank class budget or writer diversity differs.");
        string[] expectedQueries = records.Values.Where(r => bank.Dataset == "dollar"
            ? r.Split == "main" && r.WriterId == bank.HeldOutWriter : r.Split == "test")
            .Select(r => r.SampleId).Order(StringComparer.Ordinal).ToArray();
        if (expectedQueries.Length == 0 || !expectedQueries.SequenceEqual(bank.QueryIds.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Frozen bank omits or adds held-out records.");
    }
}
