# Normalization, transformation and alignment

All methods on this page are in `PolylineKit.Core`, with no third-party runtime
dependencies. Operations return new snapshots without mutating caller input;
`Apply` and `ResampleByArcLength` return writable arrays. Returned affine maps
reproduce the returned transformed points.
Coordinates must be finite and have magnitude at most `1e100`. Floating-point
input precision cannot be recovered later.

Preparation and measurement are separate operations. Choose the invariances
permitted by your application, then use the [area methods](area.md) when the
resulting filled geometry is the quantity you need:

```csharp
var first = PolylineNormalization.ToUnitBounds(original);
var second = PolylineNormalization.ToUnitBounds(modified);
var change = PolylineArea.CompareRegions(first.Points, second.Points);
```

## Bounds normalization

`ToUnitBounds(points, scaling = Uniform, size = 1)` centers a path in `[-size/2,size/2]^2`. Uniform scaling maps the longest source bounds side to `size`, preserving aspect ratio. Stretch scales nonzero axes independently; it can distort angles and proportions. A collapsed source axis stays collapsed and centered. Point-only paths are rejected because they do not define a scale.

`FitBounds(points, targetBounds, scaling)` and `MatchBounds(moving, reference, scaling)` use the same rule with an explicit/reference rectangle. Uniform scaling uses the smaller usable target/source side ratio. A nonzero source axis cannot fit into a zero target axis; fully collapsed targets are rejected.

Results provide `Points`, `Transform`, `OriginalBounds`, `Bounds` and `Strategy`. Maps compose source-center translation, scaling, and target-center translation. Unrepresentable scales and materially ill-conditioned affine results are rejected: a tiny extent relative to a large offset can lose enough double precision to miss the promised fitted bounds. Translate such coordinates closer to the origin while precision remains available upstream.

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
[measured scope and limits](performance.md).

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

1. Positioned graph signals: call `PolylineArea.BetweenGraphs`; divide by fixed domain length if mean absolute vertical separation is useful.
2. Closed filled silhouettes: optionally normalize or align, then use `PolylineArea.CompareRegions`. Choose NonZero or EvenOdd intentionally.
3. Ordered strokes: inspect alignment RMS and correspondence settings; advanced endpoint-bridged integrals are described in the [area-engine reference](winding-area.md). Zero area does not imply stroke identity.
4. Resolved output contours or existing decimal-grid comparisons: use the [optional Clipper adapter](clipper.md).
