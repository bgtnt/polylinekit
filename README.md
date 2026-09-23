# PolylineKit

Experimental C# methods for polyline area comparison, bounds normalization, arc-length resampling and similarity alignment. Each operation states what it measures and returns its transformation or diagnostics. The API is still open for review; NuGet packaging and publication are out of scope at this stage.

## Choose the operation

| Method | Meaning |
| --- | --- |
| `PolylineArea.BetweenGraphs(p, q)` | Integral of absolute vertical separation on a shared x interval; increasing-x graphs only. |
| `PolylineComparison.EndpointBridgedArea(p, q)` | Fill area of the closed walk `p + reverse(q)`, joined by straight endpoint connectors. |
| `PolylineComparison.FilledRegionDifference(p, q)` | Symmetric difference area of two independently filled closed contours. |
| `PolylineNormalization.ToUnitBounds(p)` | Center and fit a path inside a unit square; return transformed points and affine map. |
| `PolylineNormalization.MatchBounds(p, q)` | Center and fit `p` inside `q`'s bounds, with uniform or independent axis scaling. |
| `PolylineAlignment.FitSimilarity(p, q)` | Sample along arc length, then fit translation, rotation and optional uniform scale. |
| `PolylineSampling.ResampleByArcLength(p)` | Equidistant samples along an open or closed path. |

These area operations are different definitions, not interchangeable implementations of a universal distance. Read the [comparison and transformation contracts](docs/comparison-api.md), including fill rules, precision, closed-path phase and degenerate cases.

## Compare, normalize, align

```csharp
using PolylineKit;

Point2[] reference = [new(0, 0), new(2, 0), new(2, 1), new(1, 2), new(3, 3)];
Point2[] moving = AffineTransform2D.Scaling(2.5)
    .Then(AffineTransform2D.Rotation(0.4))
    .Then(AffineTransform2D.Translation(10, -7)).Apply(reference);

// Independent bounds normalization removes location and bounds size, not rotation.
var normalized = PolylineComparison.CompareNormalized(
    reference, moving, AreaComparisonKind.EndpointBridged);

// Fit the rotation, translation and uniform scale.
var fit = PolylineAlignment.FitSimilarity(moving, reference);
var comparison = PolylineComparison.EndpointBridgedArea(reference, fit.AlignedPoints);
double area = comparison.RawArea;          // Approximately zero, within clipping precision.
double? score = comparison.NormalizedArea; // Area / joint bounding rectangle area.
double rms = fit.RmsError;                 // Sampled residual in reference units.
AffineTransform2D transform = fit.Transform;
```

For closed filled shapes, use `FilledRegionDifference`. For closed stroke alignment, use `new AlignmentOptions { Closed = true }`; this searches discrete cyclic sample shifts. Reversed traversal is opt-in. Alignment minimizes sampled squared distances, **not area**. A runnable example is in [examples/Basic](examples/Basic/Program.cs).

Normalized area is a geometric ratio, **not a calibrated similarity percentage**. Parallel equal-length horizontal segments have ratio 1 for every positive gap in exact geometry (subject to clipping precision); identical collinear paths return `null` because the denominator is zero. Raw area and RMS retain different information. Area alone cannot bound the worst local deviation or distinguish every traversal.

## Build and verify

The core targets **.NET Standard 2.0** for consumer compatibility; examples and experiments target **.NET 10**. The core has one runtime package dependency, **Clipper2 2.0.0**, for general polygon fill/Boolean operations. The graph integral, transforms, normalization, resampling and fitting are original implementations. There is no dependency on RtTools or MPR001.

Install the .NET 10 SDK and run:

```sh
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
dotnet run --project experiments/PolylineKit.Experiments -c Release --no-build -- check
dotnet run --project examples/Basic -c Release --no-build
```

Checks use a deterministic console harness, **not `dotnet test`**. Failures exit nonzero. CI runs the checks and example on Windows and Linux. Use a project reference while the API is under review; no NuGet release is part of this work.

Three independent benchmark processes, recording timing and allocations:

```powershell
pwsh -File scripts/benchmark.ps1 -OutputDirectory artifacts/graph-benchmarks
pwsh -File scripts/benchmark.ps1 -Suite Transforms -OutputDirectory artifacts/transform-benchmarks
```

The script rejects tracked changes and non-ignored untracked files before stamping measurements with HEAD, and disables tiered compilation. Keep scratch outputs under ignored `artifacts/`. Fixture construction is outside timing. See the [transformation evaluation](docs/comparison-evaluation.md), original [LIP/GenLIP experiment](docs/report.md), and [independent-review follow-up](docs/review-follow-up.md).

## Research scope and provenance

The original graph experiment found a reproducible stability advantage over LIP's intersection-dependent area weights in a near-touch case. It does **not** establish scientific novelty, universal trajectory similarity, or an improvement to Clipper's polygon engine. The original [mathematical contract](docs/design.md), [baseline reconstruction limits](docs/baselines.md) and [inputs and measurements](results/geometry/geometry.json) remain available.

Original code is MIT-licensed. The author's unpublished RtTools.Geometry was inspected for ideas only. Neither it nor MPR001 is included, linked, or used as a test oracle or benchmark. No third-party algorithm source was copied. Clipper2 is a released dependency under its own [license](THIRD-PARTY-NOTICES.md).
