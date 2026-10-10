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

        // Exercise the multi-ring API from the packaged assembly, not a source project.
        Point2[] shell = [new(0, 0), new(0, 10), new(10, 10), new(10, 0)];
        Point2[] hole = [new(2, 2), new(6, 2), new(6, 6), new(2, 6)];
        Point2[] island = [new(20, 0), new(20, 2), new(22, 2), new(22, 0)];
        Point2[] rectangle = [new(4, 0), new(4, 8), new(12, 8), new(12, 0)];
        var preparedZone = PreparedRegion.FromRings(new IReadOnlyList<Point2>[] { shell, hole, island });
        var preparedFootprint = PreparedRegion.FromRings(new IReadOnlyList<Point2>[] { rectangle });
        var emptyRegion = PreparedRegion.FromRings(Array.Empty<IReadOnlyList<Point2>>());
        // Both packaged target assemblies expose the additive polygon factories.
        var roleZone = PreparedRegion.FromPolygons(new
            (IReadOnlyList<Point2> Exterior, IReadOnlyList<IReadOnlyList<Point2>>? Holes)[]
            { (shell, new IReadOnlyList<Point2>[] { hole }), (island, null) });
        var roleFootprint = PreparedRegion.FromPolygon(rectangle);
        var polygonFactoryAreas = new List<double>();
        foreach (bool reverseShell in new[] { false, true })
        foreach (bool reverseHole in new[] { false, true })
        {
            var rolePolygon = PreparedRegion.FromPolygon(reverseShell ? shell.Reverse().ToArray() : shell,
                new IReadOnlyList<Point2>[] { reverseHole ? hole.Reverse().ToArray() : hole });
            Near(84, RegionArea.FilledArea(rolePolygon));
            Near(84, RegionArea.FilledArea(rolePolygon, PathFillRule.EvenOdd));
            polygonFactoryAreas.Add(RegionArea.FilledArea(rolePolygon));
        }
        Require(PreparedRegion.FromPolygons(Array.Empty<
            (IReadOnlyList<Point2> Exterior, IReadOnlyList<IReadOnlyList<Point2>>? Holes)>()).RingCount == 0,
            "Empty polygon collection must prepare an empty region.");
        Require(typeof(PreparedRegion).Assembly == core && typeof(RegionArea).Assembly == core,
            "Prepared regions and their area methods must belong to Core.");
        Require(preparedZone.RingCount == 3 && preparedZone.VertexCount == 12,
            "Prepared shell, hole and island metadata differs.");
        Require(emptyRegion.RingCount == 0 && emptyRegion.VertexCount == 0 && emptyRegion.Bounds is null,
            "Empty prepared region metadata differs.");
        var preparedChecks = new List<object>();
        foreach (PathFillRule preparedRule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            Near(88, RegionArea.FilledArea(preparedZone, preparedRule)); // 100 - 16 + 4.
            Near(88, RegionArea.FilledArea(roleZone, preparedRule));
            Near(40, RegionArea.IntersectionArea(roleZone, roleFootprint, preparedRule));
            Near(64, RegionArea.FilledArea(preparedFootprint, preparedRule));
            Near(40, RegionArea.IntersectionArea(preparedZone, preparedFootprint, preparedRule)); // 48 - 8.
            RegionOverlapResult preparedOverlap = RegionArea.Compare(preparedZone, preparedFootprint, preparedRule);
            Near(88, preparedOverlap.FirstArea); Near(64, preparedOverlap.SecondArea);
            Near(40, preparedOverlap.IntersectionArea); Near(112, preparedOverlap.UnionArea);
            Near(72, preparedOverlap.SymmetricDifferenceArea);
            Near(5.0 / 14, preparedOverlap.IntersectionOverUnion!.Value);
            Near(9.0 / 14, preparedOverlap.JaccardDistance!.Value);
            Require(preparedOverlap.FillRule == preparedRule, "Prepared comparison fill rule differs.");
            Near(0, RegionArea.FilledArea(emptyRegion, preparedRule));
            Near(0, RegionArea.IntersectionArea(emptyRegion, preparedZone, preparedRule));
            RegionOverlapResult emptyOverlap = RegionArea.Compare(emptyRegion, emptyRegion, preparedRule);
            Require(emptyOverlap.FirstArea == 0 && emptyOverlap.SecondArea == 0 && emptyOverlap.IntersectionArea == 0 &&
                emptyOverlap.UnionArea == 0 && emptyOverlap.SymmetricDifferenceArea == 0 &&
                emptyOverlap.IntersectionOverUnion is null && emptyOverlap.JaccardDistance is null,
                "Empty prepared regions must have zero areas and undefined overlap ratios.");
            preparedChecks.Add(new
            {
                FillRule = preparedRule.ToString(),
                ZoneArea = preparedOverlap.FirstArea,
                FootprintArea = preparedOverlap.SecondArea,
                Intersection = preparedOverlap.IntersectionArea,
                Union = preparedOverlap.UnionArea,
                SymmetricDifferenceArea = preparedOverlap.SymmetricDifferenceArea,
                IoU = preparedOverlap.IntersectionOverUnion,
                JaccardDistance = preparedOverlap.JaccardDistance,
                EmptyUnion = emptyOverlap.UnionArea,
                EmptyIoU = emptyOverlap.IntersectionOverUnion,
                EmptyJaccardDistance = emptyOverlap.JaccardDistance
            });
        }

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
            ZeroAreaCoverage = zeroCoverage,
            PreparedRegionRingCount = preparedZone.RingCount,
            PreparedRegionVertexCount = preparedZone.VertexCount,
            PolygonFactoryFourDirections = polygonFactoryAreas,
            PreparedRegionChecks = preparedChecks
        }));

        static string? Target(Assembly assembly) => assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        static string Hash(Assembly assembly) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Near(double expected, double actual) => Require(Math.Abs(expected - actual) <= 1e-12 * Math.Max(1, Math.Abs(expected)),
            $"Expected {expected:R}; got {actual:R}.");
    }
}
