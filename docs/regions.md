# Measure regions with holes and disconnected components

Use `PreparedRegion` and `RegionArea` when one operand contains several closed
rings. They return filled area, intersection, union, XOR and overlap ratios
without constructing output contours. They are part of the dependency-free
`PolylineKit.Core` project and use the `PolylineKit` namespace.

This guide describes the current source API. Use a project reference to the
current Core; the previously verified `0.1.0-alpha.1` binaries predate it.
For one ordered walk per operand, the existing [PolylineArea methods](area.md)
remain available with their original input contract.

## A shell, a hole and a separate component

This complete example uses Cartesian XY coordinates with Y increasing upwards.
The shell and island run clockwise; the hole runs counterclockwise. Under
NonZero filling, their areas are `100 - 16 + 4 = 88`.

```csharp
using System.Collections.Generic;
using PolylineKit;

Point2[] shell = [new(0, 0), new(0, 10), new(10, 10), new(10, 0)];
Point2[] hole = [new(2, 2), new(6, 2), new(6, 6), new(2, 6)];
Point2[] island = [new(20, 0), new(20, 2), new(22, 2), new(22, 0)];

var zone = PreparedRegion.FromRings(
    new IReadOnlyList<Point2>[] { shell, hole, island });
double zoneArea = RegionArea.FilledArea(zone); // 88; cache across queries
// zone.RingCount = 3; zone.VertexCount = 12.

Point2[] rectangle = [new(4, 0), new(4, 8), new(12, 8), new(12, 0)];
var footprint = PreparedRegion.FromRings(
    new IReadOnlyList<Point2>[] { rectangle });

double sharedArea = RegionArea.IntersectionArea(zone, footprint); // 40
double? coverage = zoneArea > 0 ? sharedArea / zoneArea : null; // 5/11
var result = RegionArea.Compare(zone, footprint);
// FirstArea = 88; SecondArea = 64; IntersectionArea = 40;
// UnionArea = 112; SymmetricDifferenceArea = 72;
// IntersectionOverUnion = 5/14; JaccardDistance = 9/14.
```

The rectangle intersects 48 square units of the shell, including 8 square units
of its hole, and misses the island: `48 - 8 = 40`. Both fill rules produce the
same result for this example. The snippets use C# 12 collection expressions;
older compilers can use explicit `new Point2[] { new Point2(...), ... }` arrays.

Call `IntersectionArea` for coverage when the other areas are unused. Call
`Compare` when you need the complete [RegionOverlapResult](area.md#compare-two-regions).
All three methods accept an optional `PathFillRule`, defaulting to `NonZero`.
One selected rule is applied independently to each operand. Coordinates retain
their supplied position and scale; areas use squared coordinate units.

## How rings combine

Every ring closes independently. The method adds the rings' signed winding
numbers at each location, then applies the fill rule to that total:

- **NonZero:** fills where the total is nonzero. An ordinary hole must have
  the opposite orientation to its enclosing shell. Reversing every ring of an
  operand preserves its fill; reversing only one ring can change it.
- **EvenOdd:** fills where the total is odd. Ring orientation is irrelevant;
  nested rings alternate filled and empty, and overlapping rings toggle their
  common area out of the fill.

Ring order does not assign shell/hole roles. The library does not infer nesting,
reverse rings, repair topology or connect components with invented segments.
Self-intersections, contacts, shared edges and retracing are accepted under the
chosen winding definition.

This is not a general union of independently filled polygons. Under NonZero,
overlapping consistently oriented simple shells do produce their union, but
opposite windings can cancel. Two identical same-direction rings fill once under
NonZero and cancel under EvenOdd. Preserve shell/hole roles when converting
Polygon or MultiPolygon data, and orient rings deliberately for NonZero.

## Preparation, reuse and empty regions

`PreparedRegion.FromRings` copies the coordinates, validates each ring, removes
consecutive duplicates and an optional duplicate closing vertex, and records
bounds. Keep source collections stable during preparation. Afterwards they may
be edited without changing the snapshot. Prepared instances are immutable and
can be shared across threads.

`RingCount` includes zero-area rings; `VertexCount` counts retained vertices.
`Bounds` covers every stored coordinate and is `null` only for an empty
collection. A collinear region can therefore have bounds while its area is zero.

```csharp
using System;
using System.Collections.Generic;
using PolylineKit;

var empty = PreparedRegion.FromRings(Array.Empty<IReadOnlyList<Point2>>());
double area = RegionArea.FilledArea(empty); // 0
var comparison = RegionArea.Compare(empty, empty);
// All areas are 0; IntersectionOverUnion and JaccardDistance are null.
// RingCount = 0; VertexCount = 0; Bounds = null.
```

An empty collection is a valid empty region; an empty ring inside a collection
is invalid. Every supplied ring needs at least three vertices after cleanup.
This is a retained-vertex count, not a distinct-position count. Collinear rings
and sufficiently long retraced walks are valid and may contribute zero area.

| Input | Behavior |
|---|---|
| Null collection, or null prepared operand | `ArgumentNullException` |
| Null ring, or fewer than three retained vertices in a ring | `ArgumentException` |
| Nonfinite coordinate or magnitude above `1e100` | `ArgumentException` |
| Undefined fill rule | `ArgumentOutOfRangeException`, before operand access |
| Empty collection | Zero filled area and intersection |

Comparing an empty region with a positive-area region gives zero intersection,
union and XOR equal to the positive area, IoU 0 and Jaccard distance 1. Both
ratios are `null` whenever the returned union is not positive. Coverage of a
zero-area zone is likewise undefined; calculate it conditionally as above.

Preparation reuses a validated coordinate snapshot and bounds. It does **not**
cache own areas, crossings, intersection results or a persistent search index.
Cache a zone's area yourself when its fill rule is fixed. Each measurement runs
crossing detection and winding propagation again, using per-call working storage.

## Costs and numerical limits

Preparation takes work and storage proportional to the input vertices and rings.
For a measurement with `R` rings across both operands, up to 16 rings use direct
pairwise bounds checks. Larger inputs build a balanced hierarchy of ring bounds
in reusable working arrays, with up to four rings per leaf. Each leaf candidate
still receives its own inclusive bounds check. Construction takes **O(R log² R)**
because each internal tree level sorts its ring ranges. Queries can reject whole
branches of spatially separated rings, but still require **O(R²)** bounds checks
in the worst case.
The hierarchy is rebuilt for each measurement; it is not stored in
`PreparedRegion`.

Once group membership is known, packing the groups visits each ring and vertex
at most a constant number of times: **O(R + N)** for `N` total vertices. Each
interacting group still seeds every ring's winding against the other edges,
adding **O(Rg × Ng)** work for `Rg` rings and `Ng` edges in that group. Crossing
detection, sorting and integration add the
[boundary engine costs](winding-area.md#cost-and-storage). Many mutually
overlapping rings can be expensive; the hierarchy does not guarantee near-linear
measurement time or a speed advantage over clipping.

Groups have no intersecting ring bounds between them. They are accumulated
separately to limit cancellation between distant components. Touching, nested and overlapping bounds stay in the
same group. This is not permission to assign unrelated origins to individual
rings inside an interacting group. `IntersectionArea` skips groups containing
rings from only one operand: strict bounds separation from every other group
proves their intersection contribution is zero. `FilledArea` and `Compare`
retain those groups. The single-walk integer specialization and simplicity
shortcut are not used by `RegionArea`.

Calculations use `double`, without an input grid or output clamping. Exact
topological decisions do not make intersection coordinates and final areas
exact. Tiny features near large coordinates, long thin intersections and
cancellation can lose relative precision; there is no universal error bound.
Use planar coordinates appropriate to the application: the library does not
project geographic data or calculate geodesic area. It returns no repaired
polygons, clipped contours or offsets.

First use, growth, nesting and some exact-predicate operations allocate. Prepared
snapshot storage belongs to the caller; engine working storage has a separate
[retained-cache policy](performance.md#first-use-and-retained-workspace).
The hierarchy's O(R) node array and group metadata count towards the general
engine's existing 4 MiB retained array budget, together with the group workspace.
Recorded single-walk benchmarks do not measure this multi-ring API. See the
[multi-ring checks and measurement protocol](../tests/PolylineKit.MultiRingChecks/README.md)
for the applicable evidence.
