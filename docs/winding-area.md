# Boundary winding areas

`WindingArea` computes area integrals of self-intersecting closed walks directly from their boundaries. It does not clip, resolve faces, build output contours or quantize to a decimal grid. It complements the Clipper2-based methods in [comparison-api.md](comparison-api.md); those remain unchanged and are still the way to obtain resolved contours.

| Method | Returns |
| --- | --- |
| `WindingArea.ClosedPath(path)` | `NonZero`, `EvenOdd`, `AbsoluteWinding` and `Signed` integrals of one closed walk |
| `WindingArea.EndpointBridged(first, second)` | the same four integrals for the walk `first + reverse(second)` filled by `PolylineComparison.EndpointBridgedArea` |
| `WindingArea.FilledRegions(first, second, fillRule)` | first, second, intersection, union and symmetric-difference areas of two independently filled paths, with Jaccard distance and IoU |

Results are `readonly struct`s with diagnostic counts: crossings, orientation tests evaluated exactly, and exactly degenerate tests resolved symbolically.

## What is measured

For a closed walk with winding number `w`:

```text
NonZero          = integral 1[w != 0] dA
EvenOdd          = integral (|w| mod 2) dA
AbsoluteWinding  = integral |w| dA
Signed           = integral w dA        (shoelace area)
```

`AbsoluteWinding` counts area covered repeatedly in the same direction repeatedly: two same-direction turns around an area-4 square give 8. Opposite windings still cancel at the same point, so it is not total traversal without cancellation. Clipper2's fill rules do not offer this integral. It is the definition that the author's earlier `RtTools.Geometry.MapPolygon.Area2` approached by tracing loops and summing their absolute areas; that code was not used here (see [design.md](design.md)).

For two filled paths the fill rule is applied to each path separately, as in `PolylineComparison.FilledRegionOverlap`.

## Method

Split every edge at its crossings. For any weight `F` of the winding numbers,

```text
integral F dA = 1/2 * sum over sub-edges of [F(left) - F(right)] * cross(a, b) * (t1 - t0)
```

where `a -> b` is the input edge, `[t0, t1]` the sub-edge's parameter range, and left and right differ by one in the winding of the edge's own path. All four single-path integrals, or all five region areas, come from one walk around each path.

Winding numbers start at each path's leftmost vertex, where only its two incident edges are nearby and their orientation determines the winding on either side. They are then propagated across crossings: crossing an edge changes the winding by `sign(cross(dir crossed, dir moving))`. For two paths the other path's winding at that vertex comes from a ray cast over its edges. Candidate edge pairs come from a sweep over x-sorted edge intervals.

### Robustness

Propagation is global: one wrong crossing decision shifts the winding of every later sub-edge. A first prototype that used plain floating-point orientation signs produced wrong areas on 3 of 23,040 real gesture pairs, by up to 106% and with negative results. A stroke vertex lay about `1e-14` from a vertical template segment on the normalized bound `x = -0.5`, and the rounded signs around it were mutually inconsistent.

Every decision therefore uses an orientation predicate that is never wrong for binary64 input:

1. A floating-point filter accepts the sign when it lies outside Shewchuk's orient2d error bound.
2. Otherwise the determinant is evaluated exactly with floating-point expansions (TwoDiff/TwoProduct/Grow-Expansion). A cheap exactness check comes first, and integer arithmetic handles magnitudes where expansion products could underflow.
3. An exact zero (shared vertices, a vertex on an edge, collinear overlap) is resolved by Simulation of Simplicity (Edelsbrunner and Mücke, 1990). Vertex `p` is perturbed by `y += e^(2^(2p))`, `x += e^(2^(2p+1))`. The first nonzero coefficient of `det(i, j, k)` for `i < j < k` is then `x_k - x_j`, `y_j - y_k`, `x_i - x_k`, then `+1`. Leftmost-vertex and ray-cast comparisons use the same perturbation.

Crossing positions only need to be accurate, because swapping two crossings closer than the position error changes the winding on that short piece alone. Positions from ill-conditioned floating-point ratios are recomputed from exact determinants. For exactly collinear crossing edges, the perturbation of the lowest-index endpoint dominates, and its normal offset vanishes only at the other endpoint of its edge; the perturbed edges cross exactly there in the limit. An earlier choice (the midpoint of the overlap) was consistent for one pair but not for three mutually overlapping edges. A cyclic-start metamorphic check found it; the checks retain that case.

Area is continuous in the vertices, so the result is the exact area of the given input up to rounding in crossing positions, cross products and compensated summation. Values are not clamped, and a zero-area configuration can return a value of rounding size with either sign. Exactness assumes IEEE binary64 evaluation without extended precision, as on .NET Core and later.

### Cost

The work is `O(n log n + candidates)`; candidate pairs are `O(n^2)` in the worst case, for example when most long edges overlap in x. Working buffers are reused per thread, so steady-state calls on inputs decided by the filter or the expansion path allocate no managed memory. The integer path allocates, and is only reached for extreme exponents.

## Evidence

### Checks

`dotnet run --project experiments/PolylineKit.Experiments -c Release -- check` adds 22,685 winding checks and passes in all five implementation modes of `scripts/verify-implementations.ps1`, including the .NET Standard 2.0 build. They include:

- analytic walks and all contour fixtures, with `AbsoluteWinding` compared to the independent slab sweep in `ContourSweep`;
- 3,000 symbolic orientations against the exact sign of an explicitly perturbed determinant (`e = 2^-16`, BigInteger), including more than 500 exact ties;
- 4,000 exact signs against independent rational arithmetic, including true zeros and exponents from `2^-700` to `2^300`;
- graph fixtures against `PolylineArea.BetweenGraphs` (near-touch values agree to `1e-15`);
- random walks against the slab sweep (`1e-9`) and Clipper2 at precision 8 (`1e-7`);
- 600 generated integer-grid walks, where shared vertices, T-junctions and collinear overlap are the norm, against the slab sweep for all four integrals;
- metamorphic invariance under cyclic start, reversal, axis exchange, translation, scaling and duplicate points, on degenerate inputs where each renumbering changes the symbolic tie-breaks;
- filled regions against a new two-path slab sweep (`RegionSweep`) on generic and integer-grid inputs, inclusion–exclusion and exchange symmetry;
- zero steady-state allocation and input rejection.

### Clipper2 disagreements on degenerate input

Clipper2 2.0.0 at precision 8 disagreed with both the winding engine and the slab sweep in 5 of 1,490 degenerate grid comparisons. `winding-evidence` arbitrates each with a third, independent method: a scanline integral with exact interval lengths per row and the midpoint rule in y. It carries an explicit bound of (vertices + crossings + 1) × row height × width. See [clipper-disagreements.json](../results/winding/clipper-disagreements.json).

| Case | max \|winding − scanline\| | max \|Clipper − scanline\| | Scanline bound |
| --- | ---: | ---: | ---: |
| grid walk n=16 #68 NonZero | 5.0e-7 | 0.083 | 5.0e-4 |
| grid regions #81 NonZero | 6.7e-7 | 0.555 | 3.1e-3 |
| grid regions #137 NonZero | 6.0e-12 | 0.033 | 1.8e-3 |
| grid regions #137 EvenOdd | 8.6e-12 | 0.033 | 1.8e-3 |
| grid regions #146 NonZero | 3.3e-7 | 0.037 | 6.1e-3 |

In the walk case Clipper returns 3.83 to 3.85 at precisions 2 through 8, while the area is 3.75. In regions #81 its symmetric difference is 18% low. These inputs are exactly degenerate integer configurations. They say nothing about Clipper2's general reliability, but they do apply to PolylineKit's existing Clipper-based methods on such inputs.

### Every pair of the frozen recognition evaluation

`winding-validate` in `experiments/PolylineKit.Recognition` recomputes every query/template pair of the frozen evaluation ([protocol](recognition-protocol.md)), using the evaluation's own preparation and transforms. Raw coordinates are not redistributed; [the report](../results/winding/recognition-pairs/winding-validation.json) records sample IDs, derived values and input hashes.

| | $1 | Pendigits |
| --- | ---: | ---: |
| Unique pairs | 682,560 | 406,112 |
| Pairs with an exact zero resolved symbolically | 38,080 (5.6%) | 28,263 (7.0%) |
| Max \|winding − Clipper precision 8\| outside arbitrated pairs | 1.6e-8 | 1.3e-8 |
| Pairs beyond 1e-6, arbitrated | 4 | 0 |
| Max \|winding − Clipper precision 6\| (the evaluation's area) | 4.9e-5 | 1.1e-6 |
| Area rankings whose winner changes with winding NonZero | **0 of 14,400** | **0 of 8,232** |
| Area rankings whose winner changes with AbsoluteWinding | 218 | 381 |
| Pairs where AbsoluteWinding exceeds NonZero | 65% | 79% |
| AbsoluteWinding / NonZero − 1: median, p90, max | 1.6%, 33%, 92% | 7.9%, 37%, 130% |

The four arbitrated $1 pairs are not degenerate (no symbolic tie-breaks). An independent exact horizontal-slab integral agrees with the winding value to about `1e-16` and differs from Clipper at precision 8 by `4.5e-6` to `6.5e-5`. At precision 6, the setting the evaluation used, Clipper was within `4.1e-7` on these pairs. Replacing the evaluation's Clipper area with winding NonZero would change none of the frozen area predictions.

About 6–7% of real normalized stroke pairs contain exact degeneracies. Pixel coordinates put extreme points and straight runs exactly on the normalized bounds. A floating-point shortcut that fails there would not be rare.

### Time and allocation

Three fresh processes, tiered compilation disabled, nine batch samples each, measured at `2bf4182`; full table in [summary.md](../results/winding/benchmarks/summary.md), inputs in `inputs.json`. Reproduce with `pwsh -File scripts/benchmark.ps1 -Suite Winding -OutputDirectory artifacts/winding-benchmarks`.

| Workload (vertices per path) | Clipper-based, µs / bytes | WindingArea, µs / bytes | Time ratio |
| --- | ---: | ---: | ---: |
| similar open strokes, 64 (recognition size) | 25.71 / 38,960 | 11.25 / 0 | 2.28 |
| similar open strokes, 1024 | 370.07 / 464,937 | 178.03 / 0 | 2.08 |
| dense-crossing graphs, 1024 | 495.70 / 1,034,409 | 214.23 / 0 | 2.31 |
| random walks, 1024 | 2,287.31 / 1,019,434 | 1,065.37 / 1 | 2.15 |
| filled regions (XOR and union), 64 | 53.46 / 63,992 | 8.72 / 0 | 6.13 |
| filled regions (XOR and union), 1024 | 726.37 / 816,921 | 192.18 / 0 | 3.78 |
| degenerate integer grid, 256 | 5,588.80 / 2,036,154 | 5,791.12 / 10 | 0.97 |

The specialized `PolylineArea.BetweenGraphs` remains 3–4× faster than `WindingArea` on graphs, since it needs no crossing search. On the degenerate grid family nearly every edge pair crosses and exact predicates dominate. There `WindingArea` is not faster than Clipper2, only allocation-free. Timings describe this workstation and these fixtures only.

## Limits

- No contours are produced; use the Clipper2-based methods for resolved boundaries.
- Candidate search is quadratic in the worst case.
- Values carry floating-point rounding. Results differ from the Clipper2-based methods by their quantization, which is `1e-6` by default.
- `AbsoluteWinding` changes 1.5% ($1) to 4.6% (Pendigits) of area-only template winners. Whether it ranks better has not been tested by the frozen protocol. [Recognition protocol v1](recognition-protocol.md) and its verdict are unchanged.
