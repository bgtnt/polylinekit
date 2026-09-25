using PolylineKit;

// Planar coordinates in metres. Areas are square metres; each operand is one closed walk.
Point2[] zone = [new(0, 0), new(6, 0), new(6, 2), new(2, 2), new(2, 6), new(0, 6)];
Point2[] footprint = [new(1, 1), new(5, 1), new(5, 5), new(1, 5)];
const PathFillRule fillRule = PathFillRule.NonZero;
double zoneArea = PolylineArea.FilledArea(zone, fillRule); // Fixed zone: calculate once.
double intersection = PolylineArea.IntersectionArea(zone, footprint, fillRule);
double? coverage = zoneArea > 0 ? intersection / zoneArea : null;

Console.WriteLine($"Zone area: {zoneArea} m²; intersection: {intersection} m².");
Console.WriteLine(coverage.HasValue ? $"Zone coverage: {coverage.Value:P0}." : "Zone coverage is undefined: the zone has zero area.");
RegionOverlapResult overlap = PolylineArea.CompareRegions(zone, footprint, fillRule);
Console.WriteLine($"Union: {overlap.UnionArea} m²; IoU: {overlap.IntersectionOverUnion}.");
// Coverage divides by zone area (35%); IoU divides by union area (7/29).

#if PACKAGE_ADAPTER
// Enable UseClipper only when output contours or the adapter's decimal grid are needed.
AreaComparisonResult difference = PolylineComparison.FilledRegionDifference(zone, footprint, fillRule,
    decimalPrecision: 6, includeContours: true);
Console.WriteLine($"Difference: {difference.RawArea} m²; resolved contours: {difference.Contours.Count}.");
#endif

#if PACKAGE_VERIFICATION
// The verifier adds this test-only source file to an external copy of this example.
PackageVerification.Run(args, zone, footprint, fillRule);
#endif
