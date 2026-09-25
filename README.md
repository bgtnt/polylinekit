# PolylineKit

.NET methods for numeric area and region overlap, with **no external runtime
dependencies** in the core. Includes self-intersecting paths with explicit fill
rules, plus normalization, resampling and alignment. Supports **.NET Standard 2.0**
and **.NET 10**. MIT licensed.

## Add to your application

Choose `PolylineKit.Core` for numeric areas, intersection and coverage. Version
**0.1.0-alpha.1** is an alpha candidate; it has not been published on NuGet.org.
The [package guide](docs/releasing.md) produces and verifies a local feed:

```sh
pwsh -File scripts/verify-packages.ps1
dotnet add MyApp/MyApp.csproj package PolylineKit.Core --version 0.1.0-alpha.1 --source /absolute/path/to/feed
```

Alternatively, use a project reference:

```sh
git clone https://github.com/bgtnt/polylinekit.git
dotnet add MyApp/MyApp.csproj reference polylinekit/src/PolylineKit.Core/PolylineKit.Core.csproj
```

`PolylineKit.Core` includes area methods, normalization, scaling, resampling and
alignment. All use the `PolylineKit` namespace. Alpha status means the package
is ready for evaluation, with numerical and input limits described below.

The optional `PolylineKit.Clipper` project adds Clipper2 for resolved output
contours and the existing quantized `PolylineComparison` methods. Only reference
it if you need those operations; see the [adapter guide](docs/clipper.md).

## Measure zone coverage

```csharp
using PolylineKit;

Point2[] zone = [new(0, 0), new(6, 0), new(6, 2),
                new(2, 2), new(2, 6), new(0, 6)];
Point2[] footprint = [new(1, 1), new(5, 1), new(5, 5), new(1, 5)];
const PathFillRule rule = PathFillRule.NonZero;

double zoneArea = PolylineArea.FilledArea(zone, rule); // 20; cache for this zone
double intersection = PolylineArea.IntersectionArea(zone, footprint, rule); // 7
double? coverage = zoneArea > 0 ? intersection / zoneArea : null; // 35%
var change = PolylineArea.CompareRegions(zone, footprint, rule);
// FirstArea = 20; SecondArea = 16; UnionArea = 29;
// SymmetricDifferenceArea = 22; IntersectionOverUnion = 7/29.
```

Paths close implicitly. Self-intersections and repeated edges are accepted.
`PathFillRule.NonZero` is the default; pass `PathFillRule.EvenOdd` to use parity
fill. Areas use squared coordinate units. These methods calculate values without
producing output contours or quantizing input onto a decimal grid.

Coverage answers how much of the fixed zone is covered; IoU compares the shared
area with the union of both regions. A zero-area zone has undefined coverage
(`null`). Summing coverage from overlapping footprints can double-count area.
This small example explains the result, not its speed. For a trusted simple
ring's own area alone, a shoelace sum is cheaper than general fill-aware processing.
Run the [package-based example](examples/PackageCoverage) for checked results.

## Choose a method

| You need | Method |
|---|---|
| Area filled by one closed path | `PolylineArea.FilledArea(path, fillRule)` |
| Area shared by two regions | `PolylineArea.IntersectionArea(first, second, fillRule)` |
| Region change, union, XOR, IoU or Jaccard distance | `PolylineArea.CompareRegions(first, second, fillRule)` |
| Vertical separation between increasing-x graphs | `PolylineArea.BetweenGraphs(first, second)` |
| Centering and scaling | `PolylineNormalization.ToUnitBounds(path)` / `MatchBounds(path, reference)` |
| Equidistant samples along a path | `PolylineSampling.ResampleByArcLength(path)` |
| Translation, rotation and optional uniform-scale alignment | `PolylineAlignment.FitSimilarity(moving, reference)` |
| Resolved contours as well as area | Optional [Clipper adapter](docs/clipper.md): `PolylineComparison.EndpointBridgedArea(...)` / `FilledRegionDifference(...)`, with `includeContours: true` |

Normalization, alignment and measurement are separate operations. For example:

```csharp
Point2[] moving = AffineTransform2D.Scaling(2)
    .Then(AffineTransform2D.Translation(10, -5)).Apply(zone);
var fit = PolylineAlignment.FitSimilarity(moving, zone);
double rms = fit.RmsError;
var alignedChange = PolylineArea.CompareRegions(zone, fit.AlignedPoints);
```

Alignment minimizes sampled squared distances. Area measures accumulated region
difference; it does not bound maximum boundary displacement or identify every
distinct stroke. Choose invariances such as rotation or scaling to suit your data.

## Input and numerical limits

- Filled-area methods accept one ordered walk per operand, with at least three
  vertices after consecutive duplicates and an optional closing duplicate are
  removed. Null, empty and too-short inputs throw; valid collinear walks can have zero area.
- Coordinates must be finite and have magnitude at most `1e100`. Keep input
  unchanged during a call. No method silently repairs invalid input.
- Calculations use `double`. Exact geometric decisions do not make intersection
  positions or final areas exact. Tiny features near very large coordinates can
  lose precision. IoU/Jaccard are `null` when the union is not positive.
- Each operand is one walk. Collections of rings, geodesic areas and polygon
  offsets are not supported by these area methods.

Read the [area guide](docs/area.md) for fill rules, examples and contracts, or the
[transformation guide](docs/comparison-api.md) for normalization and alignment.

## Examples and documentation

- [Runnable quick start](examples/Basic/Program.cs)
- [Install and run the coverage example from NuGet packages](examples/PackageCoverage)
- [Optional contour output](examples/ClipperContours/Program.cs)
- [Measure change after contour simplification](examples/AreaChange/README.md)
- [Measure how much of a region is covered](examples/RegionCoverage/README.md)
- [Documentation index](docs/README.md)
- [Performance and memory](docs/performance.md)
- [Build, tests and contributing](CONTRIBUTING.md)
- [Alpha release notes](docs/releases/0.1.0-alpha.1.md)

Original code is [MIT licensed](LICENSE). Dependencies, data and algorithm
attribution are documented in [third-party notices](THIRD-PARTY-NOTICES.md).
