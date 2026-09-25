using PolylineKit;

// Choose the fill of a single walk. Repeating a loop changes EvenOdd, not NonZero.
Point2[] closedSquare = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] repeatedSquare = [.. closedSquare, .. closedSquare];
double nonZeroArea = PolylineArea.FilledArea(repeatedSquare);
double evenOddArea = PolylineArea.FilledArea(repeatedSquare, PathFillRule.EvenOdd);
Console.WriteLine($"Repeated-square area: NonZero={nonZeroArea}; EvenOdd={evenOddArea}");
if (nonZeroArea != 4 || evenOddArea != 0)
    throw new Exception("Unexpected selected filled area.");

// A filled-area consumer: removing a triangular notch changes one square unit.
Point2[] originalContour = [new(0, 0), new(2, 0), new(2, 2), new(1, 1), new(0, 2)];
Point2[] processedContour = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
var contourChange = PolylineArea.CompareRegions(originalContour, processedContour);
Console.WriteLine($"Contour change: {contourChange.SymmetricDifferenceArea}; Jaccard={contourChange.JaccardDistance}");
if (contourChange.SymmetricDifferenceArea != 1 || contourChange.JaccardDistance != .25)
    throw new Exception("Unexpected contour-change area.");

Point2[] baseline = [new(0, 0), new(2, 0)];
Point2[] triangle = [new(0, 0), new(1, 1), new(2, 0)];
double area = PolylineArea.BetweenGraphs(baseline, triangle);
Console.WriteLine($"Graph area: {area}");
if (area != 1) throw new Exception("Unexpected graph area.");

// Normalization is an explicit operation; it preserves proportions with Uniform scaling.
var resized = AffineTransform2D.Scaling(2.5).Then(AffineTransform2D.Translation(10, -7)).Apply(closedSquare);
var normalizedReference = PolylineNormalization.ToUnitBounds(closedSquare);
var normalizedMoving = PolylineNormalization.ToUnitBounds(resized, BoundsScaling.Uniform);
var normalized = PolylineArea.CompareRegions(normalizedReference.Points, normalizedMoving.Points);
Console.WriteLine($"After independent bounds normalization: difference area={normalized.SymmetricDifferenceArea:G6}");
if (normalized.SymmetricDifferenceArea > 1e-12) throw new Exception("Position/size normalization failed.");

// Alignment uses sampled correspondence and RMS, separately from any area operation.
Point2[] reference = [new(0, 0), new(2, 0), new(2, 1), new(1, 2), new(3, 3)];
var moved = AffineTransform2D.Rotation(.4).Then(AffineTransform2D.Scaling(2.5))
    .Then(AffineTransform2D.Translation(10, -7)).Apply(reference);
var fit = PolylineAlignment.FitSimilarity(moved, reference);
Console.WriteLine($"Similarity fit: scale={fit.Scale:G6}, rotation={fit.RotationRadians:G6}, RMS={fit.RmsError:G6}");
if (fit.RmsError > 1e-10) throw new Exception("Alignment failed.");
Point2[] samples = PolylineSampling.ResampleByArcLength(reference, sampleCount: 64);
Console.WriteLine($"Arc-length samples: {samples.Length}");

// Filled closed shapes have a separate, direction-independent comparison.
Point2[] square = [new(0,0), new(2,0), new(2,2), new(0,2)];
Point2[] shifted = AffineTransform2D.Translation(1, 0).Apply(square);
var overlap = PolylineArea.CompareRegions(square, shifted);
Console.WriteLine($"Filled-region difference: {overlap.SymmetricDifferenceArea}");
if (overlap.SymmetricDifferenceArea != 4) throw new Exception("Unexpected symmetric-difference area.");
Console.WriteLine($"Filled union: {overlap.UnionArea}; Jaccard distance={overlap.JaccardDistance:G6}; IoU={overlap.IntersectionOverUnion:G6}");
if (overlap.UnionArea != 6 || Math.Abs(overlap.IntersectionOverUnion!.Value - 1.0 / 3) > 1e-12)
    throw new Exception("Unexpected filled-region overlap.");
