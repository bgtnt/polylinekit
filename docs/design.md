# Mathematical contract and design

This document specifies the graph integral. General fill-based comparisons, normalization and sampled alignment are specified separately in [comparison-api.md](comparison-api.md). They do not extend the graph integral's mathematical guarantees to arbitrary strokes.

## Graph operation

For two piecewise linear graphs `p(x)` and `q(x)` on the **same** interval `[a,b]`, define

```text
A(p,q) = integral[a,b] |p(x) - q(x)| dx.
```

This is the unweighted sum of the nonnegative lobe areas. Each lobe is bounded by the two paths between consecutive contacts, or by a vertical connector at an interval endpoint. Collinear overlaps contribute zero. Opposite lobes never cancel. The result has squared coordinate units; smaller means less total separation. `A/(b-a)` is mean absolute vertical separation, in coordinate units, not a normalized similarity percentage.

The public method is `PolylineArea.BetweenGraphs(IReadOnlyList<Point2>, IReadOnlyList<Point2>)`. Inputs must increase in x, except consecutive identical points. Both domain endpoints must match **exactly**. Duplicate vertices are accepted without allocation. Vertical segments, backtracking, closed paths, empty/constant-x paths and nonfinite coordinates are rejected. Coordinates must have magnitude at most `1e100`, bounding intermediate arithmetic. This does not supply an absolute error guarantee: ordinary double rounding and underflow still apply, and lost input precision cannot be recovered.

Normalization and alignment are separate operations; see [comparison-api.md](comparison-api.md). An individual path reversal is rejected by `BetweenGraphs`. A common reversal can be reordered to increasing x by the caller. A common translation preserves the mathematical area; common uniform scaling by `s` multiplies it by `s²`. General rotations can destroy the graph contract. In exact arithmetic, zero means identical graphs, including different collinear subdivisions; finite-precision evaluation can introduce nonzero area or erase separation. It does not characterize arbitrary 2D strokes.

For a concrete numerical limit, let `m = 9007199254740992` (`2^53`), `p = [(-m,1),(m,-1)]`, and `q = [(-m,1),(1,-1/m),(m,-1)]`. All coordinates are exactly representable, and both describe `y=-x/m`, so exact area is zero. The current double implementation returns **1**: interpolation loses the small x offset and accumulates its tiny y error across the wide domain. The review regression retains this input and a case-specific error ceiling of 1; this is not an error guarantee for other inputs. No epsilon clamp hides the result. Arbitrary-precision integration is outside this implementation.

## Implementation and independent checks

Merge the two sorted sequences of x breakpoints. The difference is linear on every resulting interval. If its endpoint values `u,v` have the same sign, its absolute integral is `h(|u|+|v|)/2`. Otherwise split at the zero, with `t=|u|/(|u|+|v|)`, and add `h(|u|t+|v|(1-t))/2`. Compensated summation accumulates these nonnegative contributions. Complexity is `O(n+m)` time, `O(1)` auxiliary storage, including input validation. No dense resampling or polygon engine is necessary for this restricted domain. The graph integration kernel remains scalar in both targets.

Three separate constructions check this implementation:

1. `LipGraphs` builds lobe areas and arc lengths in a separate sweep.
2. `LipPolygons` enumerates segment intersections, constructs explicit polygons and evaluates translated shoelace areas and boundary lengths. It is a slow diagnostic reference, not the timed LIP baseline.
3. `ClipperOracle` resolves the combined closed walk with Clipper2 and sums signed output areas, retaining holes.

Analytic triangles/rectangles, 100 deterministic random pairs with unequal x grids, geometric transformations, subdivision and bounded perturbation checks supplement these oracles. Polygon intersection ordering is checked; unsupported arrangements are rejected instead of assigned an invented LIP score.

## Stability argument and its limits

For any two graph pairs on the same interval,

```text
|A(p,q) - A(p',q')| <= integral (|p-p'| + |q-q'|) dx
                    <= (b-a) (sup|p-p'| + sup|q-q'|).
```

This follows directly from the triangle inequality for absolute values. It concerns vertical perturbations of fixed-domain graphs and exact real arithmetic. It does not bound maximum separation from area: a tall narrow spike has arbitrarily small area. The raw area also accumulates widespread noise instead of suppressing it with small face weights. No universal notion of visual similarity follows.

## Complex walks are a separate question

`ContourSweep` is a test-only fixture oracle: split x at vertices and intersections, order edge crossings within each slab, and integrate winding levels. It is deliberately small and slow, uses ordinary doubles, and is not shipped. Its three outputs are

```text
NonZero:          integral 1[w != 0] dA
EvenOdd:          integral (|w| mod 2) dA
AbsoluteWinding:  integral |w| dA
```

The last counts **net winding multiplicity**, not total travel without cancellation. Traversing a square forward and then backward gives zero for all three, despite tracing a nonempty boundary. Counting all bounded arrangement faces once would give a different answer in that case. Two same-direction turns around a square of area 4 give 4, 0 and 8 respectively. No one of these is silently substituted for general stroke correspondence. The [archived contour fixtures](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/geometry/contours.json) record concrete inputs, holes, retraced bridges and overlapping loops. The corresponding maintained fixtures are in [tests/PolylineKit.Checks](../tests/PolylineKit.Checks).

For the supported graph pair, winding magnitude is at most one almost everywhere, so the three integrals agree. Only in this equivalent setting does Clipper serve as an area oracle for the public method. Complex contours compare Clipper NonZero/EvenOdd only with the matching named integrals.

## Dependencies and reference precision

The graph integral is available from the dependency-free area project on both
.NET Standard 2.0 and .NET 10. The complete PolylineKit project adds Clipper2
for contour-producing comparisons; it is not called by this graph method.

The independent Clipper oracle uses decimal precision 8 and limits fixture
coordinate magnitudes to 1e6. Small faces can disappear under quantization;
analytic checks retain authority below that scale. Taking the absolute signed
area of an unresolved crossing walk is not an unsigned-area oracle.

[Third-party notices](../THIRD-PARTY-NOTICES.md) record algorithm attribution.
