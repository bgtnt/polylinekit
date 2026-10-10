# PolylineKit.Core

Area, intersection area and IoU of 2D polygons and paths: numbers only, with no
external runtime dependencies. Supports .NET Standard 2.0 and .NET 10.

```sh
dotnet add package PolylineKit.Core --prerelease
```

## Compare two outlines

```csharp
using PolylineKit;

Point2[] first = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] second = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
var overlap = PolylineArea.CompareRegions(first, second);
// IntersectionArea = 2; UnionArea = 6.
double? iou = overlap.IntersectionOverUnion; // 1/3
```

Paths close implicitly. `NonZero` is the default fill rule; pass
`PathFillRule.EvenOdd` for parity filling. IoU is `null` when union area is not
positive. The examples use C# 12 collection expressions; older compilers can use
explicit `Point2[]` arrays.

## Polygons with holes

Supply the exterior and holes by role; their original directions do not matter
for valid simple polygon boundaries:

```csharp
Point2[] exterior = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
Point2[] hole = [new(2, 2), new(6, 2), new(6, 6), new(2, 6)];
var polygon = PreparedRegion.FromPolygon(
    exterior, new System.Collections.Generic.IReadOnlyList<Point2>[] { hole });
double area = RegionArea.FilledArea(polygon); // 84 under NonZero and EvenOdd
```

`FromPolygons` also accepts disconnected polygon parts. `FromRings` keeps raw
ring directions: these same two rings passed to it give **100 under NonZero**
and **84 under EvenOdd**. The factories do not infer nesting or repair geometry.
See the [region guide](https://github.com/bgtnt/polylinekit/blob/main/docs/regions.md)
and the tested [NetTopologySuite conversion example](https://github.com/bgtnt/polylinekit/tree/main/examples/NetTopologySuiteInterop).

## PolylineKit, Clipper2 or NetTopologySuite?

| You need | Use |
|---|---|
| Area, intersection, IoU or coverage as numbers, with an explicit fill rule for self-intersecting input | `PolylineKit.Core` |
| Output polygons, offsets or general Boolean geometry | Clipper2, or the optional `PolylineKit.Clipper` adapter for its supported operations |
| A geometry model, topology validation, spatial predicates and geometry overlays | NetTopologySuite |

Recorded workloads give a guide, not a universal ranking: the building evaluator
was **3.05× faster** than its configured NTS backend and effectively tied with
Clipper2; the complete county/district coverage took **24–25% less time** than
reusable Clipper2. At **1,024 rings per operand**, warm Core comparisons were
**18–34% slower** than Clipper2. Selected warm geometry calls allocated **0 B**;
preparation, first calls, growth and oversized workspaces can allocate. These
are historical measurements, not timings of alpha.2. See
[the measured scopes and limits](https://github.com/bgtnt/polylinekit/blob/main/docs/performance.md#recorded-results-by-scenario).

## Contracts and limits

- Coordinates are planar and areas use squared coordinate units. There is no
  implicit projection, normalization, alignment or decimal rounding grid.
- Every nonempty ring needs at least three vertices after consecutive and
  closing duplicates are removed. Null, too-short or nonfinite input throws;
  valid collinear rings can have zero area. Empty prepared regions are valid.
- Calculations use `double`. Exact geometric decisions do not make intersection
  positions or final areas exact; tiny features near large coordinates can lose
  precision. Self-intersecting rings are interpreted by the chosen fill rule,
  without geometry repair. Signed-area orientation cannot establish shell/hole
  topology for an arbitrary self-intersecting walk.
- Prepared regions copy coordinates and can be reused across calls and threads.
  Preparation does not cache own areas or intersections. Coverage of a zero-area
  region is undefined; summing overlapping objects' coverages can double-count.

The package also provides normalization, scaling, resampling and similarity
alignment as separate operations. For a trusted simple ring's own area alone, a
shoelace sum is cheaper than general fill-aware processing.

The assembly retains its `PolylineKit.Winding.dll` name for binary compatibility;
all public types use the `PolylineKit` namespace. Core never references Clipper2.
See the [area guide](https://github.com/bgtnt/polylinekit/blob/main/docs/area.md),
[transformations](https://github.com/bgtnt/polylinekit/blob/main/docs/comparison-api.md)
and [MIT license](https://github.com/bgtnt/polylinekit/blob/main/LICENSE).
