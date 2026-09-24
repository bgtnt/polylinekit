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
integral F dA = 1/2 * sum over sub-edges of [F(left) - F(right)] * cross(a - o, b - a) * s
```

where `a -> b` is the input edge, `s` the sub-edge's share of it, and left and right differ by one in the winding of the edge's own path. The sub-edges with a nonzero coefficient form a closed boundary chain, and the sum is its shoelace area. One walk around each path gives all four single-path integrals, or all five chains of two filled paths.

Each chain is formed exactly before any area term is evaluated:

- a crossing point is computed once and shared by both edges. Where the crossing lies on a vertex (an exactly zero orientation, or the collinear limit below), the point is that vertex, not a parameter that rounds to 0 or 1;
- the events along each edge are ordered by these points, along the edge's dominant axis and then the other axis, so no sub-edge runs backwards;
- collinearly overlapping edges are split at each other's endpoints, so a shared boundary piece is the same segment in every path that contains it;
- identical segments are netted by their integer coefficients, found by hashing their exact coordinates.

A sub-edge of an edge that overlaps nothing takes its share `s` of the edge's term, measured between its two points along the dominant axis. The sub-edges of one edge therefore lie on the edge's own line, and a rounded crossing point shifts the area only by its distance from the line times its distance from `o`, not times the edge's length. Netted segments of overlapping edges must cancel exactly and are summed as segments `p -> q`, with `s = 1`.

The origin `o` may be any point, as long as one closed chain uses one origin; it only affects rounding. One path sums all four integrals around the center of its bounds. Two filled paths form five chains: each path, the first without the second, the second without the first, and both. Each chain is summed around the center of its own remaining segments. Union is the sum of the three disjoint parts and symmetric difference the sum of the two exclusive parts, never a difference of rounded totals.

Each term is evaluated as `cross(d, b - a)` with `d = (a - o) + (b - o)`, twice the offset of the midpoint `m` from `o`, and the sums are divided by 4. Products are |m − o| · |b − a| rather than |a − o| · |b − o|, so a short segment far from the origin is not lost to cancellation. `d` includes the rounding errors of both differences, so a long segment passing close to `o` keeps its small offset. Nothing is halved, so a tiny coordinate does not underflow, and a segment traversed backwards gives exactly the negated term. A shared boundary therefore cancels exactly, and a small region or a small difference keeps its area next to large or distant geometry. Earlier versions lacked these properties and failed on such inputs; see [Independent reviews](#independent-reviews).

Winding numbers start at each path's leftmost vertex, where only its two incident edges are nearby and their orientation determines the winding on either side. They are then propagated across crossings: crossing an edge changes the winding by `sign(cross(dir crossed, dir moving))`. For two paths the other path's winding at that vertex comes from a ray cast over its edges. Candidate edge pairs come from a sweep along the wider input bounds axis. This inexpensive heuristic avoids the fixed-X sweep's poor behavior on densely subdivided vertical paths, but does not minimize candidates for every input. Bounds are cached in sweep order so rejected pairs need no vertex lookups.

### Robustness

Propagation is global: one wrong crossing decision shifts the winding of every later sub-edge. A first prototype that used plain floating-point orientation signs produced wrong areas on 3 of 23,040 real gesture pairs, by up to 106% and with negative results. A stroke vertex lay about `1e-14` from a vertical template segment on the normalized bound `x = -0.5`, and the rounded signs around it were mutually inconsistent.

Every decision therefore uses an orientation predicate that is never wrong for binary64 input:

1. A floating-point filter accepts the sign when it lies outside Shewchuk's orient2d error bound.
2. Otherwise the determinant is evaluated exactly with floating-point expansions (TwoDiff/TwoProduct/Grow-Expansion). A cheap exactness check comes first, and integer arithmetic handles magnitudes where expansion products could underflow.
3. An exact zero (shared vertices, a vertex on an edge, collinear overlap) is resolved by Simulation of Simplicity (Edelsbrunner and Mücke, 1990). Vertex `p` is perturbed by `y += e^(2^(2p))`, `x += e^(2^(2p+1))`. The first nonzero coefficient of `det(i, j, k)` for `i < j < k` is then `x_k - x_j`, `y_j - y_k`, `x_i - x_k`, then `+1`. Leftmost-vertex and ray-cast comparisons use the same perturbation.

Crossing points only need to be accurate, because swapping two crossings closer than their error changes the winding on that short piece alone. Positions from ill-conditioned floating-point ratios are recomputed from exact determinants. The integer path keeps relative precision rather than a fixed absolute grid. A point is evaluated from the nearer end of each edge, since `1 - t` cannot represent a crossing next to the far end of a long edge, and takes each coordinate from the edge that spans less along that axis, so axis-parallel edges meet exactly. Events are ordered by these points, not by their parameters: on a long edge two crossings can have parameters in one order and points in the other, and a sub-edge running backwards reversed part of an intersection boundary (see [Independent reviews](#independent-reviews)).

For exactly collinear crossing edges, the perturbation of the lowest-index endpoint dominates, and its normal offset vanishes only at the other endpoint of its edge; the perturbed edges cross exactly there in the limit. That vertex's parameter is taken along the dominant coordinate, without squaring lengths, so tiny or anisotropic coordinates cannot underflow to 0/0. Two earlier choices were wrong:

- the midpoint of the overlap, which was consistent for one pair but not for three mutually overlapping edges; a cyclic-start metamorphic check found it;
- a dot-product projection whose NaN was replaced by 0.5; see [Independent reviews](#independent-reviews).

A position that still cannot be computed raises an exception rather than being guessed; no input in the checks reaches it.

Area is continuous in the vertices, so the result is the exact area of the given input up to rounding in crossing positions, cross products and compensated summation. That rounding grows with the distance of a chain's remaining segments from its origin; netted shared boundaries do not contribute. In one closed path whose parts lie far apart relative to their size, such as two unit squares 10⁸ apart joined by a retraced bridge, the relative error is about 10⁻⁸.

A crossing point is rounded relative to the coordinates of the edges that form it, not relative to the region it bounds. Where long edges cross far from their endpoints next to a small region, that region loses relative accuracy. Take unit-wide strips of half-length `l`, tilted by 1/1000 and crossing at the coordinate origin. The exact intersection of the binary64 input is 4000000/1000001, and the relative error of the result, over both fill rules and four orientations, is at most 1.0·10⁻³ at `l = 10¹⁶`, 1.3·10⁻⁸ at `10¹²` and 6·10⁻¹² at `10⁸`. The strips' own, union and symmetric-difference areas remain exact to rounding. Values are not clamped, and a zero-area configuration can return a value of rounding size with either sign. Exactness of the decisions assumes IEEE binary64 evaluation without extended precision, as on .NET Core and later.

### Cost

For `n` edges, `m` candidate pairs from the chosen-axis sweep, `k` events (two per crossing, plus the splits of overlapping edges) and `s` sub-edges of collinearly overlapping edges, the work is `O(n log n + m + k log k + s)`. The `k log k` term orders the events along each edge with keyed sorts, and holds when many events share a rounded parameter; the earlier `becc37d` measurement of a zigzag of 256 to 16,384 vertices across a strip 2¹⁰¹ long took 330–370 ns per vertex on this workstation. Netting by hashing is linear in `s` in expectation, not in the worst case. `m`, `k` and `s` remain `O(n^2)` in the worst case, for example when most long edges overlap along both axes.

Exact-zero predicates recognize duplicate vertices and shared horizontal/vertical coordinates before expansion arithmetic. Existing product components are reused, and zero difference tails need no products. Both targets retain Dekker's product. An experimental hardware scalar FMA variant produced no consistent end-to-end gain and was not retained; see the [performance assessment](winding-performance.md). Predicate error bounds and symbolic rules are unchanged.

Whole edges bypass division by their own extent, and `EndpointBridged` validates and removes consecutive duplicate points while copying each input once. These changes do not alter the fill definitions. The bounds cache adds three doubles (24 bytes) per retained edge slot, plus its array header, to the per-thread workspace; it trades retained memory for fewer repeated loads and comparisons.

Each call uses its own working storage. A per-thread workspace is reused by consecutive calls, and a call made while another call is active on the same thread, for example from a custom list's indexer, gets a separate one. Warm calls whose predicates are decided by the filter or by expansion arithmetic allocate no managed memory. Allocation does happen:

- on first use and when a buffer grows;
- in nested calls;
- on the integer path, reached only for extreme exponents.

The per-thread workspace keeps its largest size. Earlier benchmark tables showed 1–10 bytes per operation in some rows and attributed them to buffer growth. The second review traced them to the benchmark's own 40-byte sample record, allocated inside each measured batch. The counter is now read before the record is created, and every row measures 0.

The subsequent [sorting/SIMD follow-up](winding-search.md) stores those bounds in
three coordinate arrays, merges a bounded number of natural runs, and compacts
bounds survivors before exact geometry. The .NET 10 path uses packed comparisons
for sufficiently long candidate windows, with a scalar fallback. Merge buffers
and candidate indices add up to 16 bytes per retained edge slot, plus a 65-int
run buffer and array headers, beyond the bounds cache above. These changes retain
the same worst-case complexity; they do not make dense intersections linear.

## Evidence

### Checks

`dotnet run --project experiments/PolylineKit.Experiments -c Release -- check` adds 28,267 winding checks and passes in all five implementation modes of `scripts/verify-implementations.ps1`, including the .NET Standard 2.0 build. They include:

- analytic walks and all contour fixtures, with `AbsoluteWinding` compared to the independent slab sweep in `ContourSweep`;
- 3,000 symbolic orientations against the exact sign of an explicitly perturbed determinant (`e = 2^-16`, BigInteger), including more than 500 exact ties;
- 4,000 exact signs against independent rational arithmetic, including true zeros and exponents from `2^-700` to `2^300`;
- graph fixtures against `PolylineArea.BetweenGraphs` (near-touch values agree to `1e-15`);
- random walks against the slab sweep (`1e-9`) and Clipper2 at precision 8 (`1e-7`);
- 600 generated integer-grid walks, where shared vertices, T-junctions and collinear overlap are the norm, against the slab sweep for all four integrals;
- metamorphic invariance under cyclic start, reversal, axis exchange, translation, scaling and duplicate points, on degenerate inputs where each renumbering changes the symbolic tie-breaks;
- filled regions against a new two-path slab sweep (`RegionSweep`) on generic and integer-grid inputs, inclusion–exclusion and exchange symmetry;
- regressions for the independent review: separated regions (10⁶ to 10¹⁴ apart), a small region in a distant corner of a large one, large translations, anisotropic and tiny scales down to 2⁻⁶⁰⁰ against unscaled oracles, integer-path crossing parameters down to 2⁻⁵⁰⁰, and calls nested in list indexers of all three methods, including cleanup after an exception; each group fails on `7dee036`;
- regressions for the second review: a corner cut and a hole of side 2⁻²⁷ in the unit square under both fill rules, swapped and with either path reversed; a unit hole in squares of side 10⁴ to 10⁸ joined by a retraced bridge; squares of side 10⁴ to 10¹² shifted by 10⁻⁸ and 3·10⁻⁷, against the exact area of the rounded coordinates; and perpendicular unit-wide strips of half-length 10⁶ to 10¹⁶. Each group has failing cases on `3f365f2`, and every case now returns its exact value;
- regressions for the third review: a triangle at (2⁵², 2⁵²) and a triangle with legs 2⁻¹⁰⁷⁴ and 2³⁰⁰, whose areas 1/2 and 2⁻⁷⁷⁵ are returned exactly by all methods; tilted strips of half-length 10⁸ to 10¹⁶, also scaled by 2⁻⁵⁴, under both fill rules, swapped and reversed, against the exact intersection within `8 · (l/1000) · 2⁻⁵³` relative and the exact symmetric difference to rounding; and zigzags across a strip 2¹⁰¹ long. Every group except the zigzag, which checked a cost, fails on `474db55`;
- zero warm-call allocation and input rejection.

The performance follow-up adds 94 checks for tall subdivided paths in both
orientations, both fill rules, duplicate runs, shared bridging endpoints and
independent rejection of collapsed input paths.

Another 240 checks cover bounds contacts, vector masks/tails, natural-sort and
buffer-growth thresholds, reversed/axis-swapped alternating bars, and exact
equality with the forced-scalar result.

### Independent reviews

An independent review of `7dee036` reproduced the method's advantages and found three public failures, fixed in `a0b3295`:

| Input | Before | After |
| --- | --- | --- |
| `FilledRegions` of unit squares at (0,0) and (10⁸,10⁸) | all areas 0, Jaccard undefined | 1, 1, 0, 2, 2; Jaccard 1 |
| closed path with a retraced horizontal part of length 3·2⁻⁶⁰⁰ | NonZero, EvenOdd, AbsoluteWinding at half of 2.5·2⁻⁶⁰⁰ | all four at 2.5·2⁻⁶⁰⁰ |
| unit square read through a list whose indexer calls `ClosedPath` | NonZero 9, Signed −9 | 1, 1 |

The same review's independent Python `Fraction` integration agrees with the exact values quoted below for the Clipper2 cases (it reports 15/4 and 56561/18480 explicitly).

A follow-up review of `3f365f2` accepted those fixes, with three findings:

- the inclusion–exclusion that the fixes introduced lost small symmetric differences: `first + second - 2 * intersection` of totals rounded to 1 cannot recover 2⁻⁵⁵;
- the benchmark's nonzero allocation rows came from the benchmark itself (see [Cost](#cost));
- nearly coincident crossing parameters on very long edges could lose a small intersection, in both reviewed versions.

Exact chain formation fixes these in `533e9ec`, and `1adefca` and `1094dca` restore most of its speed. Testing the fix also found the shifted squares below: rounded crossing points along long shared boundaries left rounding of their large terms. They fail on `3f365f2` too.

| Input | `3f365f2` | Now |
| --- | --- | --- |
| corner of side 2⁻²⁷ cut from the unit square: symmetric difference, Jaccard distance | 0 | 2⁻⁵⁵ |
| hole of side 2⁻²⁷ in the unit square, both fill rules, swapped or reversed (8 cases): symmetric difference | 0 | 2⁻⁵⁴ |
| unit hole in a square of side 10⁸, joined by a retraced bridge: symmetric difference | 0 | 1 |
| square of side 10⁴ to 10¹² shifted by 10⁻⁸ or 3·10⁻⁷: symmetric difference | relative error 2·10⁻⁵ to 1 | exact for the rounded coordinates |
| perpendicular unit-wide strips of half-length 10⁶ to 10¹⁶: intersection | relative error 3·10⁻¹¹ to 1 | 1 |
| benchmark bytes per operation | 1–10 in some rows (40 per batch) | 0 |

A third review, of `474db55`, accepted these repairs and found two numerical regressions they introduced, and a complexity gap:

- the midpoint `(a + b) / 2` in world coordinates rounded `2⁵² + 1/2` to `2⁵²` and lost a whole triangle, and halving a subnormal coordinate underflowed;
- events were ordered by parameters but connected by points: two crossings on a long edge had parameters in one order and points in the other, and part of an intersection boundary ran backwards;
- ties in the parameter were finished by an insertion pass, quadratic when many events share one rounded parameter.

The fixes are the relative midpoint, ordering by points and keyed sorts for every event. While adding the strips regression, the individual areas of the strips turned out to be wrong too. Every sub-edge was summed as a segment between rounded crossing points off the long edges' lines, which moved the area by that distance times the edge's length. Sub-edges of edges that overlap nothing now take their share of the edge's term.

| Input | `474db55` | Now |
| --- | --- | --- |
| triangle (2⁵², 2⁵²), (2⁵² + 1, 2⁵²), (2⁵², 2⁵² + 1): every area | 0 | 1/2 |
| triangle (0, 0), (2⁻¹⁰⁷⁴, 0), (0, 2³⁰⁰): every area | 2⁻⁷⁷⁶ | 2⁻⁷⁷⁵ |
| tilted strips, half-length 10¹⁶: intersection (exact 3.999996) | −4.0078 | 4.0039 |
| tilted strips, half-length 10¹⁶: each strip's area, symmetric difference | relative error 1.6·10⁻⁴; up to 6·10⁻⁴ over the orientations | exact to rounding |
| zigzag of 256 vertices across a strip 2¹⁰¹ long: ordering | insertion, 20,608 moves | keyed sort |

The remaining intersection error of the strips is the conditioning of their crossing points (see [Robustness](#robustness)).

### Clipper2 disagreements on degenerate input

Clipper2 2.0.0 at precision 8 disagreed with both the winding engine and the slab sweep in 5 of 1,490 degenerate grid comparisons. `winding-evidence` arbitrates each in exact rational arithmetic: vertical slabs split at every vertex and pairwise crossing of the integer input, with rational heights and areas. See [clipper-disagreements.json](../results/winding/clipper-disagreements.json).

| Case | Exact values (first, second, intersection, union, XOR) | max \|winding − exact\| | max \|Clipper − exact\| |
| --- | --- | ---: | ---: |
| grid walk n=16 #68 NonZero | 15/4 | 0 | 0.0833 |
| grid regions #81 NonZero | 953/312, 15469/2640, 10653/3640, 1438391/240240, 56561/18480 | 8.9e-16 | 0.5546 |
| grid regions #137 NonZero | 203/60, 27571/6160, 227/112, 53891/9240, 70327/18480 | 8.9e-16 | 0.0333 |
| grid regions #137 EvenOdd | 13/4, 39439/9240, 449/240, 20873/3696, 1454/385 | 0 | 0.0333 |
| grid regions #146 NonZero | 1229/280, 661307/120120, 14623/5720, 13561/1848, 287191/60060 | 8.9e-16 | 0.0367 |

In the walk case Clipper returns 3.83 to 3.85 at precisions 2 through 8, while the area is 3.75. In regions #81 its symmetric difference is 18% low. These inputs are exactly degenerate integer configurations. They say nothing about Clipper2's general reliability, but they do apply to PolylineKit's existing Clipper-based methods on such inputs.

**Upstream status (checked 2026-09-24).** Clipper2's open pull request [#1109](https://github.com/AngusJohnson/Clipper2/pull/1109) (head `c14564a`) changes `DoSplitOp` so that a reversed split lying inside the remaining path is kept instead of discarded.

| Build (C#) | Wrong among the 5 cases above and the 4 real pairs below |
| --- | ---: |
| NuGet 2.0.0 | all 9 |
| upstream `main` at `f9c5eb6` | 7: #68, #137 NonZero, #137 EvenOdd and the 4 real pairs |
| with #1109 | none (≤ 6.9e-7, within precision-8 quantization) |

Clipper2's own C# tests pass with the change. A minimal 7-vertex case, `(0,0) (1,3) (3,0) (0,3) (3,2) (1,1) (2,1)` under NonZero, fills a winding-0 triangle of area 1/6 only while vertex (2,1) lies exactly on edge (3,0)→(0,3). As a `Tests/Polygons.txt` entry it fails on `main` and passes with #1109.

### Every pair of the frozen recognition evaluation

`winding-validate` in `experiments/PolylineKit.Recognition` (last run at `a0b3295`, with the same results as before the first review's fixes) recomputes every query/template pair of the frozen evaluation ([protocol](recognition-protocol.md)), using the evaluation's own preparation and transforms. Raw coordinates are not redistributed; [the report](../results/winding/recognition-pairs/winding-validation.json) records sample IDs, derived values and input hashes.

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

The validation was not rerun after exact chain formation. The converted datasets were no longer available locally, and importing them again means downloading the source archives. The later changes decide no orientation differently. They change which sub-edges are netted, how crossing points are ordered and where they lie within their rounding, and how area terms are evaluated and summed. Unchanged decisions do not by themselves validate the area accumulation on every historical pair. On the exactly arbitrated Clipper2 cases the values moved by at most `8.9e-16` since `a0b3295`.

About 6–7% of real normalized stroke pairs contain exact degeneracies. Pixel coordinates put extreme points and straight runs exactly on the normalized bounds. A floating-point shortcut that fails there would not be rare.

### Time and allocation

This section records the earlier `becc37d` measurements. The later
[performance assessment](winding-performance.md) compares `9fff4d2` with the
optimized managed engine and reports the C++ experiment separately.

Three fresh processes, tiered compilation disabled, nine batch samples each, measured at `becc37d`; full table in [summary.md](../results/winding/benchmarks/summary.md), inputs in `inputs.json`. Reproduce with `pwsh -File scripts/benchmark.ps1 -Suite Winding -OutputDirectory artifacts/winding-benchmarks`.

| Workload (vertices per path) | Clipper-based, µs / bytes | WindingArea, µs / bytes | Time ratio |
| --- | ---: | ---: | ---: |
| similar open strokes, 64 (recognition size) | 27.00 / 38,960 | 13.27 / 0 | 2.03 |
| similar open strokes, 1024 | 364.83 / 464,936 | 213.33 / 0 | 1.71 |
| dense-crossing graphs, 1024 | 477.79 / 1,034,408 | 262.82 / 0 | 1.82 |
| random walks, 1024 | 2,203.45 / 1,019,432 | 1,268.01 / 0 | 1.74 |
| filled regions (XOR and union), 64 | 52.23 / 63,992 | 14.44 / 0 | 3.62 |
| filled regions (XOR and union), 1024 | 724.23 / 816,920 | 285.57 / 0 | 2.54 |
| degenerate integer grid, 256 | 5,444.20 / 2,036,144 | 7,999.57 / 0 | 0.68 |

Exact chain formation costs time. Compared with the measurement at `a0b3295`, the bridged workloads take 14–26% longer. Filled regions take 60–77% longer, because two paths are walked twice and form five chains instead of three, and each edge term is compensated. On the degenerate grid family nearly every edge pair crosses or overlaps, so exact predicates dominate and many sub-edges are netted. There `WindingArea` takes 1.4–1.5× the time of Clipper2 (1.0× before exact chain formation) and does not allocate.

The specialized `PolylineArea.BetweenGraphs` remains 4–5× faster than `WindingArea` on graphs, since it needs no crossing search. Bytes are warm-call allocations; first-call allocation and retained per-thread workspace size are not measured here. Each review reran the suite from an isolated checkout and reproduced the ratios measured then:

- on `7dee036`: 2.08–2.38× for strokes, 4.13–5.96× for regions, 0.99× on the degenerate grid;
- on `3f365f2`: 2.06–2.30×, 4.17–6.44× and 1.00×;
- on `474db55`, after exact chain formation: 1.76–1.95×, 2.74–4.00× and 0.67–0.75×.

Timings describe this workstation and these fixtures only.

## Consequence for area-only recognition

The original MPR001 experiment ranked templates by a loop-sum area close to `AbsoluteWinding`, while the frozen evaluation used NonZero. No new held-out evaluation is needed to bound the difference, and none was run. A prediction can change only where the area-only winning template changes. Those counts come from the table above and use no labels. Added to the published area-only accuracy in [recognition-evaluation.md](recognition-evaluation.md), they give an upper bound for `AbsoluteWinding` area alone on the same frozen banks:

| | Published NonZero area | Rankings with a changed winner | Upper bound, AbsoluteWinding area | Published RMS | Published DTW |
| --- | ---: | ---: | ---: | ---: | ---: |
| $1, all 4,800 records (14,400 trials) | 85.319% | 218 (1.514%) | 86.833% | 97.146% | not included |
| Pendigits, supported 2,744 (8,232 trials) | 72.631% | 381 (4.628%) | 77.259% | 84.329% | 89.043% |

Even if every changed ranking became correct, absolute-winding area alone would remain well below RMS on both datasets. A combined RMS/AbsoluteWinding score would need a new weight selection, a tuning round that [protocol v1](recognition-protocol.md) excludes unless it is registered in advance. It is not pursued, because DTW already exceeds the published digit combination by 3.6 points.

## Limits

- No contours are produced; use the Clipper2-based methods for resolved boundaries.
- One closed path whose parts are far apart relative to their size loses relative accuracy with that distance; split such input into separate paths where the question allows it.
- A crossing point is rounded relative to the coordinates of its edges, so a small region cut out by edges very long relative to it loses relative accuracy (10⁻³ for the tilted strips at 10¹⁶ above).
- Candidate search is quadratic in the worst case. On adversarial degenerate integer input, where most edge pairs cross or overlap, `WindingArea` is 1.4–1.5× slower than the Clipper-based methods.
- Values carry floating-point rounding. Results differ from the Clipper2-based methods by their quantization, which is `1e-6` by default.
- `AbsoluteWinding` changes 1.5% ($1) to 4.6% (Pendigits) of area-only template winners; the bound above caps what that can mean for accuracy. [Recognition protocol v1](recognition-protocol.md) and its verdict are unchanged.
