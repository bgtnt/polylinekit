using PolylineKit;

// A filled-area consumer: removing a triangular notch changes one square unit.
Point2[] originalContour = [new(0, 0), new(2, 0), new(2, 2), new(1, 1), new(0, 2)];
Point2[] processedContour = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
var contourChange = WindingArea.FilledRegions(originalContour, processedContour);
Console.WriteLine($"Winding contour change: {contourChange.SymmetricDifferenceArea}; Jaccard={contourChange.JaccardDistance}");
if (contourChange.SymmetricDifferenceArea != 1 || contourChange.JaccardDistance != .25)
    throw new Exception("Unexpected contour-change area.");

Point2[] baseline = [new(0, 0), new(2, 0)];
Point2[] triangle = [new(0, 0), new(1, 1), new(2, 0)];
double area = PolylineArea.BetweenGraphs(baseline, triangle);
Console.WriteLine($"Graph area: {area}");
if (area != 1) throw new Exception("Unexpected graph area.");

// Ordered strokes can contain vertical segments and move backwards in x.
Point2[] reference = [new(0, 0), new(2, 0), new(2, 1), new(1, 2), new(3, 3)];
var resized = AffineTransform2D.Scaling(2.5).Then(AffineTransform2D.Translation(10, -7)).Apply(reference);
var normalized = PolylineComparison.CompareNormalized(reference, resized, AreaComparisonKind.EndpointBridged);
Console.WriteLine($"After independent bounds normalization: bounds-area ratio={normalized.Comparison.BoundsAreaRatio:G6}");
if (normalized.Comparison.RawArea > 1e-7) throw new Exception("Position/size normalization failed.");

var moved = AffineTransform2D.Rotation(.4).Then(AffineTransform2D.Scaling(2.5))
    .Then(AffineTransform2D.Translation(10, -7)).Apply(reference);
var fit = PolylineAlignment.FitSimilarity(moved, reference);
var comparison = PolylineComparison.EndpointBridgedArea(reference, fit.AlignedPoints, decimalPrecision: 8);
Console.WriteLine($"Similarity fit: scale={fit.Scale:G6}, rotation={fit.RotationRadians:G6}, RMS={fit.RmsError:G6}");
Console.WriteLine($"Area after alignment: {comparison.RawArea:G6}; bounds-area ratio={comparison.BoundsAreaRatio:G6}");
if (fit.RmsError > 1e-10 || comparison.RawArea > 1e-7) throw new Exception("Alignment failed.");

// Filled closed shapes have a separate, direction-independent comparison.
Point2[] square = [new(0,0), new(2,0), new(2,2), new(0,2)];
Point2[] shifted = AffineTransform2D.Translation(1, 0).Apply(square);
var difference = PolylineComparison.FilledRegionDifference(square, shifted, includeContours: true);
Console.WriteLine($"Filled-region difference: {difference.RawArea}; bounds-area ratio={difference.BoundsAreaRatio:G6}");
if (difference.RawArea != 4) throw new Exception("Unexpected symmetric-difference area.");
var overlap = PolylineComparison.FilledRegionOverlap(square, shifted);
Console.WriteLine($"Filled union: {overlap.UnionArea}; Jaccard distance={overlap.JaccardDistance:G6}; IoU={overlap.IntersectionOverUnion:G6}");
if (overlap.UnionArea != 6 || Math.Abs(overlap.IntersectionOverUnion!.Value - 1.0 / 3) > 1e-12)
    throw new Exception("Unexpected filled-region overlap.");
