using PolylineKit;

// Add the optional Clipper adapter when the application needs resolved output contours.
Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] shifted = AffineTransform2D.Translation(1, 0).Apply(square);
AreaComparisonResult result = PolylineComparison.FilledRegionDifference(square, shifted,
    decimalPrecision: 6, includeContours: true);
Console.WriteLine($"Difference area: {result.RawArea}; resolved contours: {result.Contours.Count}");
Console.WriteLine($"Decimal precision: {result.DecimalPrecision}; bounds-area ratio: {result.BoundsAreaRatio}");
if (result.RawArea != 4 || result.Contours.Count != 2)
    throw new InvalidOperationException("Unexpected quantized symmetric-difference contours.");

// The core methods remain available through the adapter's project reference.
RegionOverlapResult overlap = PolylineArea.CompareRegions(square, shifted);
if (overlap.IntersectionArea != 2 || overlap.UnionArea != 6)
    throw new InvalidOperationException("Unexpected core region overlap.");
