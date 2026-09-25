# PolylineKit

C# methods for measuring filled areas, comparing regions, and normalizing and
aligning 2D polylines. Supports **.NET Standard 2.0** and **.NET 10**. MIT licensed.

## Add to your application

The library is available as source. Clone it and add a project reference:

```sh
git clone https://github.com/bgtnt/polylinekit.git
dotnet add MyApp/MyApp.csproj reference polylinekit/src/PolylineKit.Core/PolylineKit.Core.csproj
```

`PolylineKit.Core` includes area methods, normalization, scaling, resampling and
alignment with **zero third-party runtime dependencies**. All use the
`PolylineKit` namespace. A NuGet release is not available yet.

The optional `PolylineKit.Clipper` project adds Clipper2 for resolved output
contours and the existing quantized `PolylineComparison` methods. Only reference
it if you need those operations; see the [adapter guide](docs/clipper.md).

## Measure and compare areas

```csharp
using PolylineKit;

Point2[] first = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] second = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];

double area = PolylineArea.FilledArea(first);                       // 4
double intersection = PolylineArea.IntersectionArea(first, second); // 2
var change = PolylineArea.CompareRegions(first, second);
// FirstArea = 4; SecondArea = 4; UnionArea = 6;
// SymmetricDifferenceArea = 4; IntersectionOverUnion = 1/3.
```

Paths close implicitly. Self-intersections and repeated edges are accepted.
`PathFillRule.NonZero` is the default; pass `PathFillRule.EvenOdd` to use parity
fill. Areas use squared coordinate units. These methods calculate values without
producing output contours or quantizing input onto a decimal grid.

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
    .Then(AffineTransform2D.Translation(10, -5)).Apply(first);
var fit = PolylineAlignment.FitSimilarity(moving, first);
double rms = fit.RmsError;
var alignedChange = PolylineArea.CompareRegions(first, fit.AlignedPoints);
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
- [Optional contour output](examples/ClipperContours/Program.cs)
- [Measure change after contour simplification](examples/AreaChange/README.md)
- [Measure how much of a region is covered](examples/RegionCoverage/README.md)
- [Documentation index](docs/README.md)
- [Performance and memory](docs/performance.md)
- [Build, tests and contributing](CONTRIBUTING.md)

Original code is [MIT licensed](LICENSE). Dependencies, data and algorithm
attribution are documented in [third-party notices](THIRD-PARTY-NOTICES.md).
