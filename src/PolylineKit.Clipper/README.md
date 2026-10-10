# PolylineKit.Clipper

Optional Clipper2 integration for decimal-grid area comparisons and resolved
output contours. Supports .NET Standard 2.0 and .NET 10; depends on
`PolylineKit.Core` and Clipper2.

```sh
dotnet add package PolylineKit.Clipper --prerelease
```

```csharp
using PolylineKit;

Point2[] first = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] second = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
var difference = PolylineComparison.FilledRegionDifference(
    first, second, PathFillRule.NonZero,
    decimalPrecision: 6, includeContours: true);
// RawArea = 4; Contours contains the two resolved difference boundaries.
```

## Choose the output you need

| You need | Use |
|---|---|
| Area, intersection, IoU or coverage as numbers, with an explicit fill rule for self-intersecting input | `PolylineKit.Core` |
| Output polygons, offsets or general Boolean geometry | Clipper2, or this adapter for its supported comparisons and contours |
| A geometry model, topology validation, spatial predicates and geometry overlays | NetTopologySuite |

Every `PolylineComparison` call uses Clipper2, including `includeContours: false`.
Coordinates are quantized; features near or below the selected grid can collapse.
Each operand is one ordered walk, not a collection of rings. The examples use
C# 12 collection expressions; older compilers can use explicit `Point2[]` arrays.
See the [adapter guide](https://github.com/bgtnt/polylinekit/blob/main/docs/clipper.md)
for fill rules, precision limits and supported operations.

The [Core package](https://www.nuget.org/packages/PolylineKit.Core) returns numeric
areas directly from double coordinates and has no dependency on this adapter.
Its recorded building evaluator was effectively tied with direct Clipper2, while
its prepared intersection batch was 12% slower. Core's complete county/district
coverage took 24–25% less time; warm generated cases with 1,024 rings per operand
were 18–34% slower. These are historical workload results, not timings of alpha.2
or this adapter. See [the comparison scopes](https://github.com/bgtnt/polylinekit/blob/main/docs/performance.md#recorded-results-by-scenario).

For Polygon/MultiPolygon input held in NetTopologySuite, use the tested
[Core conversion example](https://github.com/bgtnt/polylinekit/tree/main/examples/NetTopologySuiteInterop).
It preserves exterior/hole roles without adding an NTS dependency to Core.

The assembly retains its `PolylineKit.dll` name for binary compatibility and
forwards moved public types into Core. PolylineKit is MIT licensed;
[Clipper2](https://github.com/AngusJohnson/Clipper2) is by Angus Johnson under the
[Boost Software License 1.0](https://github.com/AngusJohnson/Clipper2/blob/main/LICENSE).
