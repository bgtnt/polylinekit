# Optional Clipper adapter

Reference `PolylineKit.Clipper` only when you need resolved output contours or
the existing quantized `PolylineComparison` operations:

```sh
dotnet add MyApp/MyApp.csproj reference polylinekit/src/PolylineKit.Clipper/PolylineKit.Clipper.csproj
```

It adds **one third-party runtime package, Clipper2 2.0.0**, and references
`PolylineKit.Core`. Both projects use the `PolylineKit` namespace.
`PolylineComparison` always uses Clipper2, including calls with
`includeContours: false`. Its decimal-grid contract differs from the core's
area methods; choosing not to return contours does not switch its engine.

For the `0.1.0-alpha.1` NuGet candidate, follow the
[local-feed package guide](releasing.md). It verifies the adapter and its core
dependency using package references on both supplied target assemblies.

For areas, normalization, resampling and alignment alone, reference only
[the core](../README.md#add-to-your-application). It does not load this adapter
or Clipper2. Benchmark/example references do not become core dependencies.

```csharp
var difference = PolylineComparison.FilledRegionDifference(
    first, second, PathFillRule.NonZero,
    decimalPrecision: 6, includeContours: true);
double area = difference.RawArea;
IReadOnlyList<IReadOnlyList<Point2>> contours = difference.Contours;
```

Run the [complete contour example](../examples/ClipperContours/Program.cs):

```sh
dotnet run --project examples/ClipperContours -c Release
```

## Comparison contracts

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

PolylineComparison methods default to six decimal places and accept `decimalPrecision` in `[-8,8]`. They shift the joint bounds center to the origin before clipping, and exchange axes when x extent is larger. Results and contour orientation are converted back afterwards. Signed output areas are summed to retain holes; taking the absolute area of an unresolved walk would be incorrect.

Clipper quantizes coordinates internally. The implementation conservatively requires `max(width,height) * 10^decimalPrecision <= 1e14`; otherwise it throws and asks the caller to normalize or use coarser precision. Features near/below the grid can disappear. This operating limit is not a formal error bound. See [Clipper robustness](https://www.angusj.com/clipper2/Docs/Robustness.htm). The graph-only integral does not have this grid.

## Normalized comparison

`CompareNormalized(p, q, kind, scaling, ...)` independently normalizes both paths to centered unit bounds and compares them, returning both maps and the area result. It removes absolute location and bounds size. It does not remove rotation or infer starting correspondence. Uniform normalization retains aspect ratio; Stretch discards it. Choose only the invariances your application permits.

For a grid-free comparison, call `PolylineNormalization.ToUnitBounds` for
each input and pass their `Points` to `PolylineArea.CompareRegions` instead.
See [the transformation guide](comparison-api.md).

## Compatibility

The project retains the assembly name `PolylineKit`; the core retains
`PolylineKit.Winding`. Type forwarders keep old compiled references to moved
geometry and transformation types resolvable. Public method names, the
`PolylineKit` namespace and the Clipper-based methods' behavior remain unchanged.
Projects referencing the old source paths must update their project references.
Deploy the complete dependency graph; replacing an isolated DLL is insufficient.
No NuGet package has been published.
