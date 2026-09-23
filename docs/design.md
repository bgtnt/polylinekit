# Mathematical contract and design

This document preserves the graph-integral foundation of the original experiment. General fill-based comparisons, normalization and sampled alignment are specified separately in [comparison-api.md](comparison-api.md). They do not extend the graph integral's mathematical guarantees to arbitrary strokes.

## Candidate operation

For two piecewise linear graphs `p(x)` and `q(x)` on the **same** interval `[a,b]`, define

```text
A(p,q) = integral[a,b] |p(x) - q(x)| dx.
```

This is the unweighted sum of the nonnegative lobe areas. Each lobe is bounded by the two paths between consecutive contacts, or by a vertical connector at an interval endpoint. Collinear overlaps contribute zero. Opposite lobes never cancel. The result has squared coordinate units; smaller means less total separation. `A/(b-a)` is mean absolute vertical separation, in coordinate units, not a normalized similarity percentage.

The public method is `PolylineArea.BetweenGraphs(IReadOnlyList<Point2>, IReadOnlyList<Point2>)`. Inputs must increase in x, except consecutive identical points. Both domain endpoints must match **exactly**. Duplicate vertices are accepted without allocation. Vertical segments, backtracking, closed paths, empty/constant-x paths and nonfinite coordinates are rejected. Coordinates must have magnitude at most `1e100`, bounding intermediate arithmetic. This does not supply an absolute error guarantee: ordinary double rounding and underflow still apply, and lost input precision cannot be recovered.

Normalization and alignment are separate operations; see [comparison-api.md](comparison-api.md). An individual path reversal is rejected by `BetweenGraphs`. A common reversal can be reordered to increasing x by the caller. A common translation preserves the mathematical area; common uniform scaling by `s` multiplies it by `s²`. General rotations can destroy the graph contract. Zero means identical graphs, including different collinear subdivisions; it does not characterize arbitrary 2D strokes.

## Implementation and independent checks

Merge the two sorted sequences of x breakpoints. The difference is linear on every resulting interval. If its endpoint values `u,v` have the same sign, its absolute integral is `h(|u|+|v|)/2`. Otherwise split at the zero, with `t=|u|/(|u|+|v|)`, and add `h(|u|t+|v|(1-t))/2`. Compensated summation accumulates these nonnegative contributions. Complexity is `O(n+m)` time, `O(1)` auxiliary storage, including input validation. No dense resampling or polygon engine is necessary for this restricted domain.

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

`ContourSweep` is an experimental fixture oracle: split x at vertices and intersections, order edge crossings within each slab, and integrate winding levels. It is deliberately small and slow, uses ordinary doubles, and is not shipped. Its three outputs are

```text
NonZero:          integral 1[w != 0] dA
EvenOdd:          integral (|w| mod 2) dA
AbsoluteWinding:  integral |w| dA
```

The last counts **net winding multiplicity**, not total travel without cancellation. Traversing a square forward and then backward gives zero for all three, despite tracing a nonempty boundary. Counting all bounded arrangement faces once would give a different answer in that case. Two same-direction turns around a square of area 4 give 4, 0 and 8 respectively. No one of these is silently substituted for general stroke correspondence. `results/geometry/contours.json` records concrete inputs, holes, retraced bridges and overlapping loops.

For the supported graph pair, winding magnitude is at most one almost everywhere, so the three integrals agree. Only in this equivalent setting does Clipper serve as an area oracle for the public method. Complex contours compare Clipper NonZero/EvenOdd only with the matching named integrals.

## Dependency and source decisions

The core targets `netstandard2.0`. The original graph-only implementation had no external runtime dependency; the current general fill-based API references Clipper2 **2.0.0**, locked with a NuGet content hash. The SDK's NETStandard.Library reference assets are build inputs. No source from Clipper, RtTools, MPR001 or a third-party LIP implementation is embedded. Packaging/publication is not part of the current scope.

Clipper's `PathsD` operations quantize internally. The oracle explicitly uses decimal precision 8 and limits fixture coordinate magnitudes to `1e6`. Very small faces can disappear; tolerances account for this, and analytic checks retain authority below that scale. `Abs(Area(unresolvedCombinedPath))` is never used as an unsigned-area oracle.

The author's unpublished `RtTools.Geometry`, including its .NET 10 version, was inspected **for ideas only**. Its intersection ordering, graph traversal and per-region accumulation motivated making region diagnostics independently inspectable. No private source, binaries or original fixtures are included. The old MPR001 application did not participate in correctness or speed measurements.

Clipper's active-edge processing illustrates why a general robust arrangement engine is substantial. We reuse its released binary for the named general fill/Boolean operations. The small graph sweep exploits the stronger graph contract. The broader API explicitly specifies fill and correspondence policies instead of silently loosening the graph integral's input contract.
