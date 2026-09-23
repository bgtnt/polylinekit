# Comparison and transformation contracts

All operations return immutable snapshots; caller input is never mutated. Returned affine maps reproduce the returned transformed points. Coordinates must be finite and have magnitude at most `1e100`. Floating-point input precision cannot be recovered later.

## Area definitions

`PolylineArea.BetweenGraphs(p, q)` integrates `|p(x)-q(x)|` over a common x domain. It accepts unequal discretization and crossings, but rejects vertical segments, backtracking and mismatched domains. Its linear merge sweep needs neither resampling nor Clipper. See [design.md](design.md).

`PolylineComparison.EndpointBridgedArea(p, q, fillRule, decimalPrecision, includeContours)` fills the closed walk consisting of `p`, a straight connector to the end of `q`, `q` in reverse order, and a connector to the beginning of `p`. Each path needs at least two points after adjacent duplicate removal. Vertical edges, crossings, loops, retracing and collinear paths are accepted. Input order and endpoint choice matter. For increasing-x graph pairs, this agrees with the graph integral up to clipping quantization.

For arbitrary walks, this is **not** the sum of every bounded face independent of traversal. Opposite winding can cancel. Same-direction repetitions contribute once under NonZero, while an even number of repetitions cancels under EvenOdd. Reversing one closed contour can change the endpoint-bridged result even when its geometric boundary is unchanged. Zero area does not imply identical strokes.

`PolylineComparison.FilledRegionDifference(p, q, ...)` implicitly closes each contour, fills them independently, then takes their Boolean XOR (symmetric difference). A repeated final copy of the first point is optional. Each contour needs at least three points after cleanup; collinear contours may have zero filled area. Cyclic starting vertex and traversal reversal do not affect the filled region under NonZero/EvenOdd. Repeated loops can still affect the chosen fill rule. Holes in the result subtract from the total area.

`PathFillRule.NonZero` (default) counts points where net winding is nonzero. `EvenOdd` counts odd winding parity. Neither counts absolute traversal multiplicity. See [Clipper fill rules](https://www.angusj.com/clipper2/Docs/Units/Clipper/Types/FillRule.htm).

| Result property | Contract |
| --- | --- |
| `RawArea` | Nonnegative area in squared input-coordinate units. |
| `UnionBounds`, `UnionBoundsArea` | Joint axis-aligned bounding **rectangle**, not the filled union of contours. |
| `BoundsAreaRatio` | `RawArea / UnionBoundsArea`; `null` when the rectangle has zero area. No clamping. |
| `NormalizedArea` | Warning-free compatibility alias for `BoundsAreaRatio`. |
| `Kind`, `FillRule`, `DecimalPrecision` | Requested operation and clipping grid. |
| `Contours` | Optional resolved boundaries in original coordinates; signed outer area positive, hole area negative. |

For parallel segments `(0,0)-(L,0)` and `(0,h)-(L,h)`, both raw area and rectangle area equal `L*|h|`, so the normalized ratio is 1 for every nonzero gap in exact geometry. At `h=0`, it is undefined (`null`). Subprecision gaps may collapse under clipping quantization. This denominator is **not a universal closeness measure**. Raw area, a fixed reference scale, or sampled RMS may better answer the application question.

### Filled-region Jaccard and IoU

`FilledRegionOverlap(p, q, fillRule, decimalPrecision)` returns the filled XOR and
union areas computed with the same cleaned inputs, origin shift, axis exchange,
fill rule and precision. Each input is filled independently; opposite contour
orientations do not cancel their overlap. Holes subtract from each result.

`JaccardDistance = SymmetricDifferenceArea / UnionArea`, and
`IntersectionOverUnion = 1 - JaccardDistance`. Both are nullable and return null
when the quantized filled union has zero area. A zero-area contour against a
positive-area contour gives distance 1 / IoU 0. No clamp hides numeric errors.
The XOR-only method still avoids an unrequested union operation.

Two unit squares translated by (100,100) have XOR area 2, bounds ratio 2/10201
and Jaccard distance 1. Translation by (.5,.5) gives XOR 1.5, bounds ratio 2/3,
union 1.75 and Jaccard 6/7. These region scores have no open-stroke interpretation.

## Clipping precision

General area methods default to six decimal places and accept `decimalPrecision` in `[-8,8]`. They shift the joint bounds center to the origin before clipping, and exchange axes when x extent is larger. Results and contour orientation are converted back afterwards. Signed output areas are summed to retain holes; taking the absolute area of an unresolved walk would be incorrect.

Clipper quantizes coordinates internally. The implementation conservatively requires `max(width,height) * 10^decimalPrecision <= 1e14`; otherwise it throws and asks the caller to normalize or use coarser precision. Features near/below the grid can disappear. This operating limit is not a formal error bound. See [Clipper robustness](https://www.angusj.com/clipper2/Docs/Robustness.htm). The graph-only integral does not have this grid.

## Bounds normalization

`ToUnitBounds(points, scaling = Uniform, size = 1)` centers a path in `[-size/2,size/2]^2`. Uniform scaling maps the longest source bounds side to `size`, preserving aspect ratio. Stretch scales nonzero axes independently; it can distort angles and proportions. A collapsed source axis stays collapsed and centered. Point-only paths are rejected because they do not define a scale.

`FitBounds(points, targetBounds, scaling)` and `MatchBounds(moving, reference, scaling)` use the same rule with an explicit/reference rectangle. Uniform scaling uses the smaller usable target/source side ratio. A nonzero source axis cannot fit into a zero target axis; fully collapsed targets are rejected.

Results provide `Points`, `Transform`, `OriginalBounds`, `Bounds` and `Strategy`. Maps compose source-center translation, scaling, and target-center translation. Unrepresentable scales and materially ill-conditioned affine results are rejected: a tiny extent relative to a large offset can lose enough double precision to miss the promised fitted bounds. Translate such coordinates closer to the origin while precision remains available upstream.

`CompareNormalized(p, q, kind, scaling, ...)` independently normalizes both paths to centered unit bounds and compares them, returning both maps and the area result. It removes absolute location and bounds size. It does not remove rotation or infer starting correspondence. Uniform normalization retains aspect ratio; Stretch discards it. Choose only the invariances your application permits.

## Affine maps and resampling

`AffineTransform2D` stores six coefficients for `x' = M11*x + M12*y + OffsetX`, `y' = M21*x + M22*y + OffsetY`. `a.Then(b)` applies `a` first, then `b`. Factories provide identity, translation, rotation in radians and uniform/axis scaling. `Apply` preserves count, duplicate points and ordering. The default struct value is a zero map; use `Identity` explicitly.

The .NET 10 target optionally uses packed-double SIMD for `Point2[]` arrays of
at least 32 points. Other lists, small arrays, unsupported hardware/layout and
arrays too large for double-span reinterpretation use scalar dispatch. The
.NET Standard 2.0 target stays portable. SIMD retains the scalar expression order,
validation and output snapshot contract; no FMA or reduction reordering is used.
To force scalar application for diagnosis, set
`AppContext.SetSwitch("PolylineKit.DisableSimd", true)`. Set it to `false` to allow
hardware dispatch again. The switch is process-wide. See the
[measured scope and limits](optimization-evaluation.md).

`PolylineSampling.ResampleByArcLength(path, sampleCount = 64, closed = false)` places equidistant samples along cumulative segment length. Open sampling includes both endpoints; closed sampling includes the closing edge and omits the repeated final sample. Adjacent duplicates are ignored. Count limits are 2..1,000,000 (open) and 3..1,000,000 (closed). Zero-length paths are rejected. Length arithmetic is rescaled against unnecessary underflow/overflow.

## Similarity alignment

```csharp
var fit = PolylineAlignment.FitSimilarity(moving, reference, new AlignmentOptions
{
    SampleCount = 64,
    Closed = false,
    AllowScaling = true,
    AllowReversal = false,
    SearchClosedPhase = true
});
```

Both paths are sampled at equal arc-length fractions. For centered moving samples `x_i` and reference samples `y_i`, each allowed correspondence is fitted using:

```text
a = sum(x_i.x*y_i.x + x_i.y*y_i.y)
b = sum(x_i.x*y_i.y - x_i.y*y_i.x)
theta = atan2(b, a)
scale = sqrt(a*a+b*b) / sum(|x_i|^2)   // or 1 for rigid fitting
translation = mean(y) - scale*R(theta)*mean(x)
```

Intermediate coordinates are rescaled for conditioning. The candidate with least squared sample residual is selected. Rotation is proper (no reflection). With `Closed = true`, the default search checks all `SampleCount` cyclic shifts: a **discrete phase approximation**, not continuous optimization. Reversal is tested only when enabled. Counts range from 3 to 1024. Zero-length inputs, effectively zero covariance and unrepresentable transformations are rejected instead of returning an arbitrary fit.

`Transform` maps original moving coordinates into reference coordinates. `AlignedPoints` preserves the original moving point count and order, including an optional repeated closing point. `Scale`, `RotationRadians`, `RmsError`, `Reversed`, `PhaseShift`, `SampleCount` and `Strategy` describe the selected fit. RMS is recomputed from the actual returned map at the sampled correspondences, in reference-coordinate units.

`Reversed` and `PhaseShift` describe correspondence; they do not reorder `AlignedPoints`. Before open endpoint-bridged comparison after a reversed fit, reverse the aligned traversal to match reference endpoints. Closed filled-region comparison is insensitive to that order. Closed endpoint-bridged stroke comparison may require explicitly choosing a corresponding starting point.

The fit minimizes sampled Euclidean residuals, **not area**. It is a preparation step, not a guaranteed global minimum of the final area score. Sampling can miss narrow features; different vertex counts are supported but different polygonal approximations remain different curves. Closed phase search costs `O(sampleCount^2)` in addition to input traversal. General affine/shear fitting, reflection search, projective fitting, nonlinear warping and partial-stroke correspondence are not implemented.

## Suggested workflows

1. Positioned graph signals: call `BetweenGraphs`; divide by fixed domain length if mean absolute vertical separation is useful.
2. Open strokes with location/size invariance: normalize independently, optionally fit a similarity, then compute endpoint-bridged area and inspect RMS alongside it.
3. Closed filled silhouettes: optionally normalize/align, then compare filled-region difference. Choose NonZero or EvenOdd intentionally.
4. Ambiguous inputs: request contours and inspect concrete examples. Do not interpret zero area as universal stroke identity or infer a maximum-deviation bound.
