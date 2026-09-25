# PolylineKit.Winding

Compute areas from ordered 2D paths without creating output contours. This leaf
project targets .NET Standard 2.0 and .NET 10, with no external runtime packages.
Use a project reference; no NuGet package is released at this stage. The namespace
remains `PolylineKit`.

```csharp
using PolylineKit;

Point2[] a = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] b = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
var area = WindingArea.ClosedPath(a); // NonZero = EvenOdd = AbsoluteWinding = 4
var overlap = WindingArea.FilledRegions(a, b);
// IntersectionArea = 2; UnionArea = 6; SymmetricDifferenceArea = 4; IoU = 1/3.
double intersection = WindingArea.IntersectionArea(a, b); // 2, without the other areas.
```

`ClosedPath` permits self-intersections and implicit closure. `EndpointBridged`
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

Warm calls reuse per-thread workspace. First use, growth, nested calls and extreme
exponents can allocate. Workspace capacity remains retained on the thread. The
.NET 10 target uses optional SIMD bounds filtering and scalar fallback; the
portable target uses scalar code. Performance depends on crossings and shape;
see [measurements and reproduction](../../docs/performance.md).

For normalization, resampling, alignment, graph comparison or Clipper-generated
contours, reference the broader [`PolylineKit`](../PolylineKit) project. It
depends on this leaf and forwards the five extracted public types. An existing
application must deploy both assemblies and update its dependency manifest;
replacing the old parent DLL alone is insufficient. Reproducible checks are in
[`tests/Consumers`](../../tests/Consumers).
