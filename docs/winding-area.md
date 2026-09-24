# Boundary winding areas

`WindingArea` computes area integrals directly from closed boundaries, without a decimal grid or polygon clipping. It returns areas, not resolved contours. The independent [PolylineKit.Winding assembly](../src/PolylineKit.Winding/README.md) has no external runtime dependencies; both `netstandard2.0` and `net10.0` expose the same API in namespace `PolylineKit`. Use the broader library's Clipper2-based operations in [comparison-api.md](comparison-api.md) when output boundaries are needed.

## Inputs and results

| Method | Input and result |
| --- | --- |
| `WindingArea.ClosedPath(path)` | One implicitly closed walk; returns `NonZero`, `EvenOdd`, `AbsoluteWinding` and `Signed`. |
| `WindingArea.EndpointBridged(first, second)` | The walk `first + reverse(second)` with straight endpoint connectors; returns the same four integrals. |
| `WindingArea.FilledRegions(first, second, fillRule)` | Independently filled closed paths; returns first, second, intersection, union and symmetric-difference areas, plus Jaccard distance and IoU. |

Inputs are not mutated. Coordinates must be finite and have magnitude at most `1e100`. Consecutive duplicates are removed and a repeated closing point is optional. `ClosedPath` and each `FilledRegions` input need at least three vertices after cleanup; each `EndpointBridged` input needs at least two. Self-intersections, loops, retracing and collinear overlap are accepted. Invalid inputs throw rather than produce an invented result.

All areas have squared coordinate units. The readonly result structs also expose `CrossingCount`, `ExactPredicateCount` and `SymbolicTieBreakCount`. These diagnose geometric work; predicate counts can change with dispatch and do not define the area.

For a closed walk with winding number `w`:

```text
NonZero          = integral 1[w != 0] dA
EvenOdd          = integral (|w| mod 2) dA
AbsoluteWinding  = integral |w| dA
Signed           = integral w dA
```

`Signed` is positive for counterclockwise coverage. Two same-direction traversals of an area-4 square give NonZero 4, EvenOdd 0 and AbsoluteWinding 8. A traversal followed by its reverse gives zero for all three unsigned integrals: opposite windings cancel locally. AbsoluteWinding measures net winding multiplicity, not total travel or the area of every bounded arrangement face regardless of traversal.

Input order and endpoints matter for `EndpointBridged`; zero area does not imply equal strokes. For independently filled regions, `PathFillRule.NonZero` or `EvenOdd` is applied to each path separately. `JaccardDistance = SymmetricDifferenceArea / UnionArea` and `IntersectionOverUnion = IntersectionArea / UnionArea`; both are null when the union is not positive. No clamp hides rounding errors. Normalization and alignment are separate, explicit operations.

## Algorithm

Edges are split at crossings and overlap endpoints. Each directed sub-edge contributes an area term weighted by the difference of the desired winding function on its two sides. Winding numbers start at a leftmost vertex and propagate across crossings. One closed walk supplies all four single-path integrals; two independently filled paths supply five boundary chains: each whole path, the two exclusive regions and their intersection. Union and symmetric difference are sums of disjoint parts, avoiding subtraction of nearly equal rounded totals.

Crossing decisions use a floating-point orientation filter followed, when needed, by exact expansion arithmetic. Extreme exponents use integer arithmetic. Exact zeros are resolved consistently by Simulation of Simplicity (Edelsbrunner and Mücke, 1990); all orientation and winding decisions use the same infinitesimal perturbation. The filter follows Shewchuk's orient2d error bound. These predicates decide topology exactly for the supplied binary64 coordinates under IEEE binary64 evaluation; they do not make area arithmetic exact.

Crossing points are shared between both edges and equal the actual vertex when incidence is exact. Events are ordered by their shared point along the edge's dominant axis, then its other axis. Collinear overlaps split at each other's endpoints; identical segments are netted using integer coefficients and exact-coordinate hashing. Non-overlapping sub-edges contribute their fraction of the original edge term, avoiding an artificial bend through a rounded crossing point.

Each closed chain has one local origin `o`. Area terms evaluate twice `orient2d(o,a,b)` with a value-error filter, compensated products and an exact fallback; the compensated sum is divided by four. This avoids rounding a world-coordinate midpoint or halving a subnormal coordinate. The [numerical derivation](winding-numerics.md) distinguishes the bounded fast value from the exact predicate sign and the remaining area errors. A single walk uses its bounds center; each two-path chain uses the center of its remaining segments. Shared boundaries cancel before accumulation.

Candidate pairs come from a sweep on the wider bounds axis. Cached bounds, natural-run sorting and candidate compaction reduce preparation and rejection work. The .NET 10 target uses packed-double bounds comparisons for sufficiently long candidate windows, with scalar fallback; exact predicates and compensated sums remain scalar. The portable target has no intrinsics path. No approximate predicate or reduced-precision coordinate representation is used.

For a single loop with at least 256 cleaned vertices, a bounded simplicity check can end candidate enumeration early. It is attempted once, after at least `8*n` bounding-box survivors and no crossing or overlap. It borrows the prepared vertices; acceptance retains the original area accumulation, while rejection resumes the same candidate pass. It rejects nonadjacent touches, retracing and crossings. `FilledRegions` does not use this optimization. To disable it for a workload comparison:

```csharp
AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", true);
```

`PolylineKit.DisableSimd` similarly forces scalar dispatch. These process-wide switches change implementation choices, not the area definitions.

## Numerical limits

Exact topology does not imply a small relative area error. Values retain ordinary binary64 rounding from crossing positions, products and accumulation. They are not quantized or clamped, and a theoretically zero area can have a small result of either sign. Input precision lost before the call cannot be recovered.

Known regression examples make these limits concrete:

| Input | Observed limit |
| --- | --- |
| Triangles `(0,0)`, `(L,L)`, `(2L,double.BitIncrement(2L))`, for `L=1e4,1e8,1e12,1e16` | Edge-product cancellation is corrected: areas now equal the exact binary64-input areas. The previous engine lost up to **32.9%** on these cases. Reversal, cyclic shifts, tiny scaling and containment have independent dyadic-oracle regressions. |
| Unit-wide crossing strips, tilted by `1/1000`, half-length `1e16` | Intersection relative error reaches **1e-3** across the checked orientations/rules; approximately `1.3e-8` at half-length `1e12` and `6e-12` at `1e8`. Own, union and XOR areas in these fixtures remain exact to rounding. |
| Two unit squares `1e8` apart, connected as one walk by a retraced bridge | Relative error is approximately `1e-8`; a single chain's distant parts share one accumulation origin. |

These are measured examples, not general error bounds. A tiny region formed by very long edges loses accuracy because crossing positions round relative to the edges' coordinates, not the tiny region. Translate/scale upstream while precision is available, and separate distant components where the intended semantics permit it. Neither normalization nor exact predicates guarantee a well-conditioned area for every shape.

Clipper-based methods apply an additional decimal grid and can differ. Agreement with a quantized result is therefore not a universal correctness oracle. The [frozen exact-rational arbitration](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/clipper-disagreements.json) preserves specific disagreements with pinned Clipper2 2.0.0; it is not a claim about every subsequent upstream version.

The [edge-term derivation](winding-numerics.md) describes the value-error filter,
exact fallback and remaining accumulation error. Exact topology alone does not
establish that numerical contract.

## Cost and storage

For `n` edges, `m` candidate pairs, `k` crossing/overlap-split events and `s` sub-edges of collinearly overlapping edges, work is `O(n log n + m + k log k + s)`. Hash netting is expected-linear in `s`, not a worst-case guarantee. Candidate pairs and events can be quadratic. The certificate has a `32*n*(floor(log2(n))+1)` traversal budget and falls back when exhausted: it adds bounded `O(n log n)` combinatorial work, plus exact-predicate bit cost, without improving the general engine's worst-case bound.

Each call leases its own workspace. Consecutive calls on one thread reuse storage; a nested call receives separate storage. Warm calls handled by filtered/expansion predicates allocate no managed memory, but first use, buffer growth, nesting and extreme-exponent integer arithmetic can allocate. A thread retains its largest workspace capacity; zero bytes allocated per warm call does not mean zero retained memory.

The bounds cache retains 24 payload bytes per edge slot. Merge buffers and candidate indices add up to 16 bytes per slot and a 65-int run buffer. The optional certificate is allocated lazily and adds 28 payload bytes per vertex slot. These are additions to the engine's other vertex, event and chain storage, not a total memory bound; object and array headers are extra.

## Checks and measured scope

Run the maintained correctness executable and five-mode verification script described in the repository README. The recorded Windows run passed **174,043 assertions for the portable target** and **173,955 for each of four modern modes** (normal, forced scalar, no AVX, no hardware intrinsics). Both parent and leaf targets are checked; the count difference comes from the resolved framework dependency closure. Tests include **6,087 new exact dyadic area checks**, analytic areas, exact signs, independent slab/rational oracles, 125,628 enumerated small-grid cycles, metamorphic changes, extreme coordinates, nested calls, workspace reuse, certificate budget exhaustion and warm allocations. Test sources and helper oracles live in [tests/PolylineKit.Checks](../tests/PolylineKit.Checks).

The independent fixture checker retains 424 assertions per target. The assembly extraction preserved all public result properties bit for bit on 120 recorded operations per target, compared with the corrected pre-split engine. Standalone and precompiled consumer checks cover the extracted API and forwarders. The [performance summary](performance.md) includes improvements and regressions. Commands for current reruns are in [benchmarks/README.md](../benchmarks/README.md); historical reports and raw evidence remain in the [versioned research archive](../research/README.md). A possible multiple-ring API is only a [design note](winding-multiple-rings.md).
