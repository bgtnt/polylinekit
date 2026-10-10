using System.Reflection;
using System.Text.Json;
using PolylineKit;
using NetTopologySuiteInterop;

if (args.Length == 0 || args[0] is not ("check" or "check-gis") || args.Length > 3 || args[0] == "check" && args.Length > 2)
{
    Console.Error.WriteLine("Usage: NetTopologySuiteInterop check [output.json] | check-gis [repository-root] [output.json]");
    return 2;
}
bool gis = args[0] == "check-gis";
string root = gis && args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();
Assembly ntsAssembly = typeof(NetTopologySuite.Geometries.Geometry).Assembly;
string ntsInformationalVersion = ntsAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
InteropChecks.Require(ntsInformationalVersion.StartsWith("2.6.0 ", StringComparison.Ordinal) || ntsInformationalVersion == "2.6.0",
    "The example must use the locked NetTopologySuite 2.6.0 version.");
var report = new
{
    Utc = DateTime.UtcNow,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    CoreVersion = typeof(PreparedRegion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
    NtsAssemblyVersion = ntsAssembly.GetName().Version!.ToString(),
    NtsInformationalVersion = ntsInformationalVersion,
    Scope = "Valid Polygon/MultiPolygon conversion and observed invalid-input behavior; no timing measurements.",
    Contracts = InteropChecks.Run(),
    InvalidInputs = InvalidInputChecks.Run(),
    Gis = gis ? GisChecks.Run(root) : null
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
string? output = gis ? args.Length > 2 ? args[2] : null : args.Length > 1 ? args[1] : null;
if (output is not null)
{
    string path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json);
    Console.WriteLine("PASS: " + path);
}
else Console.WriteLine(json);
return 0;
