# Measuring areas and region changes

Use `PolylineArea` when you need numerical areas rather than output contours.
These methods are available from `PolylineKit.Core`, which has no third-party
runtime dependencies. All areas are in squared input-coordinate units.

## One filled area

```csharp
Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
double area = PolylineArea.FilledArea(square); // 4

Point2[] twice = [.. square, .. square];
double nonZero = PolylineArea.FilledArea(twice, PathFillRule.NonZero); // 4
double evenOdd = PolylineArea.FilledArea(twice, PathFillRule.EvenOdd); // 0
```

The final point connects to the first implicitly. NonZero fills locations where
net winding is nonzero; EvenOdd fills locations where winding is odd. Traversing
a loop forward and then backward cancels under both rules. Reversing the entire
walk preserves its filled area. This is not the sum of every visible face
regardless of traversal, or a distance between stroke samples.

## Compare two regions

```csharp
var result = PolylineArea.CompareRegions(original, modified);
double changedArea = result.SymmetricDifferenceArea;
double? changedFraction = result.JaccardDistance;
```

Each input closes and fills independently. There is no connector between the
operands. Reversing one operand does not cancel the other region.

| `RegionOverlapResult` property | Meaning |
|---|---|
| `FirstArea`, `SecondArea` | Filled area of each operand |
| `IntersectionArea` | Area filled by both operands |
| `UnionArea` | Area filled by at least one operand |
| `SymmetricDifferenceArea` | Area filled by exactly one operand (XOR) |
| `IntersectionOverUnion` | Intersection divided by union; greater means more overlap |
| `JaccardDistance` | XOR divided by union; smaller means less region change |
| `FillRule` | Rule applied independently to both operands |

Both ratios are `null` if the union is not positive. Values are not clamped to
hide numerical errors. Small area change does not bound maximum local boundary
deviation: an arbitrarily thin spike can have a very small area.

If you only need the common area, use
`PolylineArea.IntersectionArea(first, second, fillRule)` to avoid calculating
the unused areas. For coverage of a positive-area zone:

```csharp
double zoneArea = PolylineArea.FilledArea(zone);
double coveredArea = PolylineArea.IntersectionArea(zone, footprint);
double? coverage = zoneArea > 0 ? coveredArea / zoneArea : null;
```

## Increasing-x graphs

`PolylineArea.BetweenGraphs(first, second)` integrates absolute vertical
separation over a shared x interval. Unequal vertex counts and crossings are
accepted; x must increase and both domain endpoints must match exactly. Vertical
segments and backtracking are rejected. See the [formula and limits](design.md).

## Input contract

The filled-area methods accept `IReadOnlyList<Point2>`. Inputs are not mutated
and must remain stable throughout the call. At least three vertices must remain
after consecutive duplicates and an optional final duplicate of the first point
are removed. This is a count of retained vertices, not distinct positions.

| Input | Behavior |
|---|---|
| Null | `ArgumentNullException` |
| Empty, one or two retained vertices | `ArgumentException` |
| Nonfinite coordinate or magnitude above `1e100` | `ArgumentException` |
| Undefined fill rule | `ArgumentOutOfRangeException`, checked before input access |
| Three collinear vertices or an adequately long retraced walk | Accepted; filled area can be zero |
| Self-intersections or overlapping edges | Accepted under the selected fill rule |

Each operand is one ordered walk. A hole can be represented with an exactly
retraced bridge and appropriate traversal/fill rule. A collection-of-rings API
is not provided; do not concatenate unrelated rings with invented segments.
Coordinates are planar: project geographic data appropriately before measuring
physical areas. The library does not perform a map projection.

## Precision and implementation choices

Inputs are not normalized, resampled, snapped or rounded to qualify for an
optimization. Use the separate transformation methods when your application
requires those operations.

Area accumulation and intersection positions use `double`. Very long thin
intersections, small components far apart, and tiny features near large
coordinates can have significant relative error. Precision lost in input cannot
be recovered. There is no universal relative-error guarantee.

The library chooses its implementation internally. Equivalent coordinates in
different containers or target assemblies may have different final rounding;
bitwise equality across those choices is not guaranteed. See
[performance and memory](performance.md) for fast-path and workspace details.

## Advanced integrals and compatibility

`WindingArea.ClosedPath` and `EndpointBridged` remain available for signed area,
absolute winding multiplicity or engine diagnostics. Existing `WindingArea`
callers continue to work. `PolylineArea` is the general entry point for filled
areas and region comparison.

The engines are described in the [algorithm documentation](winding-area.md).
The [numerical derivation](winding-numerics.md) explains exact predicates,
cancellation and precision limits. Applications do not need to choose engines.
