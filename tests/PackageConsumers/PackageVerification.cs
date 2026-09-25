using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;

internal static class PackageVerification
{
    internal static void Run(string[] args, Point2[] zone, Point2[] footprint, PathFillRule rule)
    {
        if (args.Length != 2) throw new ArgumentException("Expected library target framework and package version.");
        string expectedTarget = args[0] == "net10.0" ? ".NETCoreApp,Version=v10.0" : ".NETStandard,Version=v2.0";
        Assembly core = typeof(PolylineArea).Assembly;
        Require(core.GetName().Name == "PolylineKit.Winding", "Core assembly identity changed.");
        Require(Target(core) == expectedTarget, "Incorrect Core target selected at runtime.");
        string coreVersion = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Require(coreVersion.Split('+')[0] == args[1],
            "Unexpected Core version.");

        double zoneArea = PolylineArea.FilledArea(zone, rule); // Fixed zone: calculate this once.
        double intersection = PolylineArea.IntersectionArea(zone, footprint, rule);
        double? coverage = zoneArea > 0 ? intersection / zoneArea : null;
        RegionOverlapResult overlap = PolylineArea.CompareRegions(zone, footprint, rule);
        Near(20, zoneArea); Near(16, PolylineArea.FilledArea(footprint, rule)); Near(7, intersection);
        Near(.35, coverage!.Value); Near(29, overlap.UnionArea); Near(7.0 / 29, overlap.IntersectionOverUnion!.Value);
        // Coverage divides by the fixed zone; IoU divides by the union. They answer different questions.

        Point2[] zeroZone = [new(0, 0), new(1, 0), new(2, 0)];
        double zeroArea = PolylineArea.FilledArea(zeroZone, rule);
        double zeroIntersection = PolylineArea.IntersectionArea(zeroZone, footprint, rule);
        double? zeroCoverage = zeroArea > 0 ? zeroIntersection / zeroArea : null;
        Require(zeroArea == 0 && zeroIntersection == 0 && zeroCoverage is null, "Zero-area coverage must be undefined.");

        Point2[] shifted = AffineTransform2D.Translation(10, -3).Apply(zone);
        Near(20, PolylineArea.FilledArea(shifted, rule));
        NormalizationResult normalized = PolylineNormalization.ToUnitBounds(shifted);
        Near(1, normalized.Bounds.Width); Near(1, normalized.Bounds.Height);
        Require(typeof(AffineTransform2D).Assembly == core && typeof(NormalizationResult).Assembly == core,
            "Transforms and normalization must belong to Core.");

        Assembly? adapter = null;
        #if PACKAGE_ADAPTER
        adapter = typeof(PolylineComparison).Assembly;
        Require(adapter.GetName().Name == "PolylineKit" && Target(adapter) == expectedTarget, "Incorrect adapter identity or target.");
        AreaComparisonResult contours = PolylineComparison.FilledRegionDifference(zone, footprint, rule,
            decimalPrecision: 6, includeContours: true);
        Near(22, contours.RawArea);
        Require(contours.Contours.Count > 0, "The adapter must produce resolved contours.");
        FilledRegionOverlapResult gridOverlap = PolylineComparison.FilledRegionOverlap(zone, footprint, rule);
        Near(29, gridOverlap.UnionArea); Near(7.0 / 29, gridOverlap.IntersectionOverUnion!.Value);
        Require(adapter.GetForwardedTypes().Length == 15 && adapter.GetForwardedTypes().All(t => t.Assembly == core),
            "The adapter must forward the existing Core type identities.");
        #else
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "PolylineKit" or "Clipper2Lib"),
            "Core-only execution loaded the adapter or Clipper.");
        #endif

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ConsumerTarget = AppContext.TargetFrameworkName,
            Runtime = RuntimeInformation.FrameworkDescription,
            CoreAssembly = core.GetName().Name,
            CoreVersion = coreVersion,
            CoreTarget = Target(core),
            CoreSha256 = Hash(core),
            AdapterAssembly = adapter?.GetName().Name,
            AdapterTarget = adapter is null ? null : Target(adapter),
            AdapterSha256 = adapter is null ? null : Hash(adapter),
            ZoneArea = zoneArea,
            FootprintArea = overlap.SecondArea,
            Intersection = intersection,
            Coverage = coverage,
            Union = overlap.UnionArea,
            IoU = overlap.IntersectionOverUnion,
            ZeroAreaCoverage = zeroCoverage
        }));

        static string? Target(Assembly assembly) => assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        static string Hash(Assembly assembly) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Near(double expected, double actual) => Require(Math.Abs(expected - actual) <= 1e-12 * Math.Max(1, Math.Abs(expected)),
            $"Expected {expected:R}; got {actual:R}.");
    }
}
