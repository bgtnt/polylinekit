using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using PolylineKit;
using PolylineKit.MultiRingChecks;

if (Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1")
    AppContext.SetSwitch("PolylineKit.DisableSimd", true);

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MultiRingChecks <repository-root> <output.json>");
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
    Scope = "Internal multiple-ring feasibility checks; not a public API or timing result.",
    Analytic = AnalyticChecks.Run(),
    Gis = GisChecks.Run(root)
};
if (!sources.SequenceEqual(SourceHashes())) throw new InvalidOperationException("Source changed during evaluation.");
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {output}");
return 0;

SortedDictionary<string, string> SourceHashes()
{
    var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (string directory in new[] { "src/PolylineKit.Core", "tests/PolylineKit.MultiRingChecks" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.TopDirectoryOnly)
                     .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".json", StringComparison.Ordinal)))
            result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    return result;
}
