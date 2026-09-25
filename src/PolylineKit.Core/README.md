# PolylineKit.Core

.NET methods for numeric area and region overlap, with **no external runtime
dependencies**. Includes self-intersecting paths with explicit fill rules, plus
normalization, transforms, resampling and alignment. Targets .NET Standard 2.0
and .NET 10. MIT licensed.

## Install the alpha candidate

Version `0.1.0-alpha.1` is prepared for evaluation. Until publication on NuGet.org,
use the verified local feed produced by the
[package verification guide](https://github.com/bgtnt/polylinekit/blob/main/docs/releasing.md):

```sh
dotnet add MyApp/MyApp.csproj package PolylineKit.Core --version 0.1.0-alpha.1 --source /absolute/path/to/feed
```

## Measure zone coverage

```csharp
using PolylineKit;

Point2[] zone = [new(0, 0), new(6, 0), new(6, 2),
                new(2, 2), new(2, 6), new(0, 6)];
Point2[] footprint = [new(1, 1), new(5, 1), new(5, 5), new(1, 5)];
const PathFillRule rule = PathFillRule.NonZero;

double zoneArea = PolylineArea.FilledArea(zone, rule); // 20; cache for this zone
double coveredArea = PolylineArea.IntersectionArea(zone, footprint, rule); // 7
double? coverage = zoneArea > 0 ? coveredArea / zoneArea : null; // 0.35
var overlap = PolylineArea.CompareRegions(zone, footprint, rule);
// FirstArea = 20; SecondArea = 16; UnionArea = 29; IoU = 7/29.
```

The example uses C# 12 collection expressions. Older compilers can use explicit
`new Point2[] { new Point2(...), ... }` arrays. Paths close implicitly; NonZero
and EvenOdd fill rules are supported. An undefined coverage ratio is represented
by `null`, not zero. Summing overlapping footprints' individual coverage can
double-count their shared area.

## Contracts and limits

Each operand is one ordered walk, with at least three retained vertices. Null,
empty, too-short and nonfinite inputs throw; valid collinear walks may have zero
area. Coordinates are planar, and areas use squared coordinate units. Input is
not quantized, projected, normalized or resampled implicitly. Calculations use
`double`; tiny features near large coordinates can lose precision.

Use `CompareRegions` for union, XOR, IoU and Jaccard distance; `BetweenGraphs`
for the integral of absolute vertical separation between increasing-x graphs.
Normalization and alignment are separate operations. For a trusted simple ring
alone, a shoelace sum is cheaper than general fill-aware processing.

See the [area guide](https://github.com/bgtnt/polylinekit/blob/main/docs/area.md),
[transformations](https://github.com/bgtnt/polylinekit/blob/main/docs/comparison-api.md),
and [performance and memory](https://github.com/bgtnt/polylinekit/blob/main/docs/performance.md).
The assembly retains its `PolylineKit.Winding` name for binary compatibility;
all public types use the `PolylineKit` namespace. The core never references
Clipper2. For resolved contours or decimal-grid comparisons, use the optional
[PolylineKit.Clipper adapter](https://github.com/bgtnt/polylinekit/blob/main/docs/clipper.md).
