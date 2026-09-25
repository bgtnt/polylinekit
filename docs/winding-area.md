# Boundary winding areas

`WindingArea` computes area integrals directly from closed boundaries, without a decimal grid or polygon clipping. It returns areas, not resolved contours. The independent [PolylineKit.Winding assembly](../src/PolylineKit.Winding/README.md) has no external runtime dependencies; both `netstandard2.0` and `net10.0` expose the same API in namespace `PolylineKit`. Use the broader library's Clipper2-based operations in [comparison-api.md](comparison-api.md) when output boundaries are needed.

## Inputs and results

| Method | Input and result |
| --- | --- |
| `WindingArea.FilledArea(path, fillRule)` | One implicitly closed walk; returns the selected NonZero or EvenOdd area as a `double`. Default: NonZero. |
| `WindingArea.ClosedPath(path)` | One implicitly closed walk; returns `NonZero`, `EvenOdd`, `AbsoluteWinding` and `Signed`. |
| `WindingArea.EndpointBridged(first, second)` | The walk `first + reverse(second)` with straight endpoint connectors; returns the same four integrals. |
| `WindingArea.FilledRegions(first, second, fillRule)` | Independently filled closed paths; returns first, second, intersection, union and symmetric-difference areas, plus Jaccard distance and IoU. |
| `WindingArea.IntersectionArea(first, second, fillRule)` | The same independently filled intersection, returned as a `double`; no own/union/XOR areas or diagnostics are returned. |

Inputs are not mutated and must remain unchanged during a call. Coordinates must be finite and have magnitude at most `1e100`. Consecutive duplicates are removed. A repeated closing point is optional for `FilledArea`, `ClosedPath`, `FilledRegions` and `IntersectionArea`; all close their input rings implicitly. These methods need at least three vertices per ring after cleanup; each `EndpointBridged` input needs at least two vertices after consecutive duplicate removal. Self-intersections, loops, retracing and collinear overlap are accepted. Invalid inputs throw rather than produce an invented result.

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

### One selected fill

```csharp
Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] twice = [.. square, .. square];
double nonZero = WindingArea.FilledArea(twice); // 4.
double evenOdd = WindingArea.FilledArea(twice, PathFillRule.EvenOdd); // 0.
```

`FilledArea` exposes one fill definition without returning the other integrals
or diagnostics. It validates `fillRule` before accessing `path`; an undefined
value throws `ArgumentOutOfRangeException` for `fillRule`, even if the path is
also invalid. Null, invalid coordinates and too few retained vertices identify
the `path` parameter. Zero-area walks are accepted; a zero result is not itself
an input error.

The .NET 10 build selects between two implementations. Its current integer
specialization requires a `Point2[]` with 64–1024 supplied vertices, exact integer
coordinates in `[-2048, 2048]`, at most 16 distinct Y levels, and at least 16
nonhorizontal edges active per endpoint band on average. A 16-edge sample must
first contain at least eight proper crossings or eight identical undirected
edge pairs. The entire array is then checked before admission. These conditions
are internal performance heuristics, not restrictions on accepted public input;
reversing a path, changing its starting vertex or subdividing an edge can change
the selected implementation.

Other arrays and all other `IReadOnlyList<Point2>` representations use
`ClosedPath(path).NonZero` or `.EvenOdd`. The .NET Standard build always uses
that projection, including when the assembly runs on a modern runtime.
No decimal grid, scaling, translation or input rounding is introduced to qualify
for the integer path. Its topology decisions are exact in the admitted domain,
but its area accumulation still uses floating point. Consequently, equivalent
coordinates can have different final rounding across containers or target
assemblies. Bitwise equality with `ClosedPath`, a relative-error bound and a
speed improvement on every input are not part of the contract. Use `ClosedPath`
when all four integrals or crossing diagnostics are needed; that API is unchanged.

### Empty and degenerate inputs

| Input | Contract |
|---|---|
| A null path in any argument | `ArgumentNullException`. |
| An empty path, one surviving point, or all coincident points | `ArgumentException`; no implicit empty-region result. |
| Two surviving vertices | Invalid for `FilledArea`, `ClosedPath`, `FilledRegions` and `IntersectionArea`; a valid stroke for `EndpointBridged`. |
| Three collinear vertices, or an adequately long fully retraced walk | Valid input with zero filled area. Vertex count after cleanup is not a count of distinct positions. |
| Nonfinite coordinates or magnitude above `1e100` | `ArgumentException`, including nonempty inputs that are also too short. |
| An undefined `PathFillRule` value | `ArgumentOutOfRangeException`. |

Zero area is a valid result. Two valid coincident segments passed to `EndpointBridged`
return four zero integrals. With one zero-area fill and one positive-area fill,
`FilledRegions` returns zero intersection, union and XOR equal to the positive area,
Jaccard distance 1, and IoU 0. With two zero-area fills, all five areas are zero and
both ratios are `null`. A positive exact area below binary64's representable range
can also round to zero. The ratio condition uses the returned union, not an exact
area hidden from the caller. Default result structs likewise contain zero areas,
and the default overlap result has null ratios.

A zero **signed** area is different: a bow-tie can have cancelling signed lobes and
positive NonZero/EvenOdd area. A twice-traversed square has a positive NonZero fill
and an empty EvenOdd fill. Neither `Signed == 0` nor a fill-independent vertex test
can replace the chosen area definition.

`EndpointBridged` closes only the combined walk; appending a stroke's first point
changes that stroke. For example, an implicitly closed square and its retained
three-vertex triangle have positive region XOR but can produce a fully retraced
endpoint-bridged walk with zero area. Use `FilledRegions` for independently filled
rings. To form the oriented difference walk explicitly, close both input rings
before passing them to `EndpointBridged`; AbsoluteWinding then equals region XOR
for consistently oriented simple rings.

## Algorithms

The winding-weighted area definition has prior art. Kronenfeld and Deng (2019)
describe shift displacement using a difference walk and winding-weighted shoelace
accumulation after inserting crossings. See the
[definition and API distinction](../examples/AreaChange/SPECIALIZED.md#related-area-definition)
before equating AbsoluteWinding with independent filled-region XOR. No mathematical
novelty is claimed for these area definitions.

### Boundary engine

This algorithm serves `ClosedPath`, `EndpointBridged`, `FilledRegions` and
`IntersectionArea`, and the fallback path of `FilledArea`.

Edges are split at crossings and overlap endpoints. Each directed sub-edge contributes an area term weighted by the difference of the desired winding function on its two sides. Winding numbers start at a leftmost vertex and propagate across crossings. One closed walk supplies all four single-path integrals; `FilledRegions` supplies five boundary chains: each whole path, the two exclusive regions and their intersection. Union and symmetric difference are sums of disjoint parts, avoiding subtraction of nearly equal rounded totals. `IntersectionArea` uses the same crossing and winding decisions but only evaluates the intersection chain's area. It keeps that chain's own net bounds origin and shared-boundary cancellation; it does not subtract rounded whole-region areas.

Crossing decisions use a floating-point orientation filter followed, when needed, by exact expansion arithmetic. Extreme exponents use integer arithmetic. Exact zeros are resolved consistently by Simulation of Simplicity (Edelsbrunner and Mücke, 1990); all orientation and winding decisions use the same infinitesimal perturbation. The filter follows Shewchuk's orient2d error bound. These predicates decide topology exactly for the supplied binary64 coordinates under IEEE binary64 evaluation; they do not make area arithmetic exact.

Crossing points are shared between both edges and equal the actual vertex when incidence is exact. Events are ordered by their shared point along the edge's dominant axis, then its other axis. Collinear overlaps split at each other's endpoints; identical segments are netted using integer coefficients and exact-coordinate hashing. Non-overlapping sub-edges contribute their fraction of the original edge term, avoiding an artificial bend through a rounded crossing point.

Each closed chain has one local origin `o`. Area terms evaluate twice `orient2d(o,a,b)` with a value-error filter, compensated products and an exact fallback; the compensated sum is divided by four. This avoids rounding a world-coordinate midpoint or halving a subnormal coordinate. The [numerical derivation](winding-numerics.md) distinguishes the bounded fast value from the exact predicate sign and the remaining area errors. A single walk uses its bounds center; each two-path chain uses the center of its remaining segments. Shared boundaries cancel before accumulation.

Candidate pairs come from a sweep on the wider bounds axis. Cached bounds, natural-run sorting and candidate compaction reduce preparation and rejection work. The .NET 10 target uses packed-double bounds comparisons for sufficiently long candidate windows, with scalar fallback; exact predicates and compensated sums remain scalar. The portable target has no intrinsics path. No approximate predicate or reduced-precision coordinate representation is used.

For a single loop with at least 256 cleaned vertices, a bounded simplicity check can end candidate enumeration early. It is attempted once, after at least `8*n` bounding-box survivors and no crossing or overlap. It borrows the prepared vertices; acceptance retains the original area accumulation, while rejection resumes the same candidate pass. It rejects nonadjacent touches, retracing and crossings. `FilledRegions` does not use this optimization. To disable it for a workload comparison:

```csharp
AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", true);
```

`PolylineKit.DisableSimd` similarly forces scalar dispatch. These process-wide switches change implementation choices, not the area definitions.

### Bounded integer sweep

The specialized `FilledArea` path sweeps between consecutive endpoint Y levels.
It orders the active edges and enumerates their crossing events inside each band,
using bounded integer and rational comparisons for topology and event order.
Coincident supports contribute their winding deltas together. Gaps whose winding
satisfies the requested fill rule contribute local trapezoid areas, converted to
binary64 and accumulated with compensation. It constructs no output contours
and computes only the requested fill. This implementation is scalar; the SIMD
and simple-boundary switches above do not disable its selection.

## Numerical limits

Exact topology does not imply a small relative area error. Values retain ordinary binary64 rounding from crossing positions, products and accumulation. They are not quantized or clamped, and a theoretically zero area can have a small result of either sign. Input precision lost before the call cannot be recovered.

The following boundary-engine regression examples make these limits concrete;
they also apply when `FilledArea` uses that engine. They are not error bounds for
the separate bounded integer sweep:

| Input | Observed limit |
| --- | --- |
| Triangles `(0,0)`, `(L,L)`, `(2L,double.BitIncrement(2L))`, for `L=1e4,1e8,1e12,1e16` | Edge-product cancellation is corrected: areas now equal the exact binary64-input areas. The previous engine lost up to **32.9%** on these cases. Reversal, cyclic shifts, tiny scaling and containment have independent dyadic-oracle regressions. |
| Intersection of `[-1e100,1e100] x [-0.5,0.5]` and `[0,w] x [-1,1]`, `w=1e-250` or `1e-300` | Sub-edge fraction underflow is corrected: intersection now equals the supplied width exactly, instead of half of it. Exceptional fractions use power-of-two scaling; ordinary fractions keep the existing fast path. This does not bound all extreme-scale area errors. |
| Crossing strips bounded by `y=x/1000 ± 1` and `x=-y/1000 ± 1`, half-length `1e16` | Intersection relative error reaches **1e-3** across the checked orientations/rules; approximately `1.3e-8` at half-length `1e12` and `6e-12` at `1e8`. Own, union and XOR areas in these fixtures remain exact to rounding. |
| Two unit squares `1e8` apart, connected as one walk by a retraced bridge | The corrected engine returns exactly 2 on this control (previous relative error was about `1e-8`). This does not establish an error bound for arbitrary distant components sharing one origin. |

These are measured examples, not general error bounds. A tiny region formed by very long edges loses accuracy because crossing positions round relative to the edges' coordinates, not the tiny region. Translate/scale upstream while precision is available, and separate distant components where the intended semantics permit it. Neither normalization nor exact predicates guarantee a well-conditioned area for every shape.

Clipper-based methods apply an additional decimal grid and can differ. Agreement with a quantized result is therefore not a universal correctness oracle. The [frozen exact-rational arbitration](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/clipper-disagreements.json) preserves specific disagreements with pinned Clipper2 2.0.0; it is not a claim about every subsequent upstream version.

The [edge-term derivation](winding-numerics.md) describes the value-error filter,
exact fallback and remaining accumulation error. Exact topology alone does not
establish that numerical contract.

## Cost and storage

For the boundary engine, with `n` edges, `m` candidate pairs, `k` crossing/overlap-split events and `s` sub-edges of collinearly overlapping edges, work is `O(n log n + m + k log k + s)`. Hash netting is expected-linear in `s`, not a worst-case guarantee. Candidate pairs and events can be quadratic. The certificate has a `32*n*(floor(log2(n))+1)` traversal budget and falls back when exhausted: it adds bounded `O(n log n)` combinatorial work, plus exact-predicate bit cost, without improving the general engine's worst-case bound.

Each call leases its own workspace. Consecutive calls on one thread reuse storage; a nested call receives separate storage. In the boundary engine, warm calls handled by filtered/expansion predicates allocate no managed memory, but first use, buffer growth, nesting and extreme-exponent integer arithmetic can allocate. A thread retains its largest workspace capacity; zero bytes allocated per warm call does not mean zero retained memory.

The boundary engine's bounds cache retains 24 payload bytes per edge slot. Merge buffers and candidate indices add up to 16 bytes per slot and a 65-int run buffer. The optional certificate is allocated lazily and adds 28 payload bytes per vertex slot. These are additions to the engine's other vertex, event and chain storage, not a total memory bound; object and array headers are extra.

The [measured first-use/storage profile](performance.md#first-use-and-retained-workspace)
reports the complete cached array inventory separately from warm allocations.
For example, the 256-vertex-per-path degenerate grid retains 7,013,261 payload
bytes on one thread, despite allocating zero bytes during subsequent measured
calls. Capacity depends on crossings and overlaps as well as vertex count.

The integer sweep uses a separate per-thread workspace. A thread that calls both
routes can retain both caches. First use and buffer growth allocate; later calls
reuse vertices, edges, active ordering and crossing buffers. Crossings within one
band can be quadratic in its active edge count. At the 1024-vertex admission
limit, the pair-count bound is 523,776; capacity growth can round the crossing
buffer to 524,288 records of 24 payload bytes each, or **12 MiB for that buffer
alone**. This is a capacity bound, not a measurement of typical input or a total
workspace bound. Other arrays and object headers add storage. Integer-sweep
memory is not included in the historical boundary-engine storage figures above.

## Checks and measured scope

Run the maintained correctness executable and five-mode verification script described in the repository README. The recorded Windows run passed **182,653 assertions for the portable target** and **182,567 for each of four modern modes** (normal, forced scalar, no AVX, no hardware intrinsics). Both parent and leaf targets are checked; the count difference comes from the resolved framework dependency closure. Tests include **6,879 exact dyadic area-value checks**, **4,032 sub-edge ratio checks** and **468 public boundary-contract checks**, analytic areas, exact signs, independent slab/rational oracles, 125,628 enumerated small-grid cycles, metamorphic changes, extreme coordinates, nested calls, workspace reuse, certificate budget exhaustion and warm allocations. Test sources and helper oracles live in [tests/PolylineKit.Checks](../tests/PolylineKit.Checks).

The independent fixture checker retains 424 assertions per target. The assembly extraction preserved all public result properties bit for bit on 120 recorded operations per target, compared with the corrected pre-split engine. Standalone and precompiled consumer checks cover the extracted API and forwarders. New `FilledArea` checks cover analytic fill rules, dispatch boundaries, independent slab-oracle areas, fallback bit identity, invalid inputs, nested and concurrent calls, exception recovery and input ownership. The recorded counts include 1,381 selected-fill checks per target. The [performance summary](performance.md) includes improvements and regressions. Commands for current reruns are in [benchmarks/README.md](../benchmarks/README.md); historical reports and raw evidence remain in the [versioned research archive](../research/README.md). A possible multiple-ring API is only a [design note](winding-multiple-rings.md).

The [v3 adaptive experiment](../benchmarks/PolylineKit.ScanbeamBenchmarks/HYBRID-V3-RESULTS.md)
measured the prototype that motivated the new entry point. Its gates and ratios
describe that frozen experiment, not the subsequently integrated public method.
The separate [public API measurements](../benchmarks/PolylineKit.ScanbeamBenchmarks/FILLED-AREA-RESULTS.md)
pass the target, preservation and integration gates while retaining process
outliers and remaining losses to Clipper.
Both compare a requested scalar fill against the existing four-integral engine;
the difference in requested work remains relevant when interpreting timings.
