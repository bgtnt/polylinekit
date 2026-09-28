using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using PolylineKit;
using PolylineKit.MultiRingChecks;

if (Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1")
    AppContext.SetSwitch("PolylineKit.DisableSimd", true);

if (args.Length == 5 && args[0] == "summarize")
{
    Write(args[4], RegionBenchmarks.Summarize(args.Skip(1).Take(3).ToArray()));
    return 0;
}
if (args.Length == 4 && args[0] == "benchmark")
{
    Write(args[3], RegionBenchmarks.Run(Path.GetFullPath(args[1]), int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)));
    return 0;
}
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MultiRingChecks <repository-root> <output.json> | benchmark <root> <run:1..3> <output.json> | summarize <run1.json> <run2.json> <run3.json> <output.json>");
    return 2;
}
string root = Path.GetFullPath(args[0]);
var sources = SourceHashes();
var report = new
{
    Utc = DateTime.UtcNow,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    CoreTarget = typeof(WindingArea).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName,
    CoreVersion = typeof(WindingArea).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
    ForceScalar = Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1",
    Vector256 = System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated,
    SourceSHA256 = sources,
    Scope = "Public prepared-region contracts and multiple-ring geometry checks; not a timing result.",
    Analytic = AnalyticChecks.Run(),
    Prepared = PreparedRegionChecks.Run(),
    RingPartition = RingPartitionChecks.Run(),
    RingGrouping = RingGroupingChecks.Run(),
    Gis = GisChecks.Run(root)
};
if (!sources.SequenceEqual(SourceHashes())) throw new InvalidOperationException("Source changed during evaluation.");
Write(args[1], report);
return 0;

static void Write(string path, object report)
{
    string output = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS: {output}");
}

SortedDictionary<string, string> SourceHashes()
{
    var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (string directory in new[] { "src/PolylineKit.Core", "tests/PolylineKit.MultiRingChecks" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.TopDirectoryOnly)
                     .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".json", StringComparison.Ordinal)))
            result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    return result;
}
