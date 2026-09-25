# PolylineKit.Winding

Compute areas from ordered 2D paths without creating output contours. This leaf
project targets .NET Standard 2.0 and .NET 10, with no external runtime packages.
Use a project reference; no NuGet package is released at this stage. The namespace
remains `PolylineKit`.

```csharp
using PolylineKit;

Point2[] a = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] b = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
double filled = WindingArea.FilledArea(a); // 4; NonZero is the default.
double odd = WindingArea.FilledArea([.. a, .. a], PathFillRule.EvenOdd); // 0.
var area = WindingArea.ClosedPath(a); // NonZero = EvenOdd = AbsoluteWinding = 4
var overlap = WindingArea.FilledRegions(a, b);
// IntersectionArea = 2; UnionArea = 6; SymmetricDifferenceArea = 4; IoU = 1/3.
double intersection = WindingArea.IntersectionArea(a, b); // 2, without the other areas.
```

`FilledArea` returns one selected NonZero or EvenOdd area as a `double`.
`ClosedPath` returns all four winding integrals and crossing diagnostics. Both
permit self-intersections, retracing and implicit closure, and require at least
three vertices after consecutive duplicate and optional closing-point removal.
`EndpointBridged`
closes `first + reverse(second)` with straight endpoint connectors; input direction
matters. `FilledRegions` applies NonZero or EvenOdd to each of two paths separately.
It returns both areas, intersection, union, XOR and optional IoU/Jaccard distance.
`IntersectionArea` returns only the intersection as a `double`, with the same
input and fill-rule contracts and numerical limits. It preserves the general
crossing/overlap processing but accumulates only the intersection boundary chain.
Zero union makes those ratios null. Area is not a metric on stroke trajectories
and cannot bound the largest local deviation.

Each operand is one walk, not a collection of rings. A hole can be encoded with
an exactly retraced bridge and suitable orientation/fill rule, but separate-ring
input is not implemented. Contours and offsets require another tool.

Coordinates must be finite with magnitude at most 1e100. Exact orientation signs
do not imply exact intersections or final areas. There is no snap grid; very long,
thin intersecting strips can lose intersection-area precision. See the complete
[contract and numerical limits](../../docs/winding-area.md) and
[edge-term derivation](../../docs/winding-numerics.md).

In the .NET 10 build, `FilledArea` can use a bounded integer sweep for suitable
`Point2[]` inputs. The current selector considers arrays with 64–1024 supplied
vertices, exact integer coordinates in `[-2048, 2048]`, few Y levels and sampled
crossings or repeated edges. Other arrays, other `IReadOnlyList<Point2>`
representations, and the .NET Standard build use the selected `ClosedPath` value.
These are implementation choices, not additional input restrictions. No rounding,
scaling or quantization prepares input for the sweep. Equivalent inputs can have
different final rounding across containers or builds; bitwise equality with
`ClosedPath` and a universal speed improvement are not promised.

Warm calls reuse per-thread workspace. First use, growth, nested calls and extreme
exponents can allocate. Workspace capacity remains retained on the thread. The
integer sweep has its own cache, separate from the boundary engine; its crossing
array alone can retain about 12 MiB at the 1024-vertex limit, plus other buffers.
The boundary engine's .NET 10 build uses optional SIMD bounds filtering and scalar
fallback; the portable target uses scalar code. Performance depends on crossings
and shape; see the [algorithm and storage details](../../docs/winding-area.md)
and [measurements and reproduction](../../docs/performance.md).

For normalization, resampling, alignment, graph comparison or Clipper-generated
contours, reference the broader [`PolylineKit`](../PolylineKit) project. It
depends on this leaf and forwards the five extracted public types. An existing
application must deploy both assemblies and update its dependency manifest;
replacing the old parent DLL alone is insufficient. Reproducible checks are in
[`tests/Consumers`](../../tests/Consumers).
