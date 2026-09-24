# Certified-simple active sweep experiment

This is a separate .NET 10 experiment in
[PolylineKit.ActiveSweep](../experiments/PolylineKit.ActiveSweep), not a replacement
for the library's general winding engine. It tests whether avoiding candidate-pair
enumeration is worthwhile before attempting a full crossing-event sweep.

**Decision: continue this architectural direction for large, simple, highly
nonmonotone contours. Do not enable the prototype unconditionally.** It has a
substantial measured advantage on some such inputs, but sorting and certification
cost more than the current engine on ordinary rings and narrow rectangles. A cheap
dispatch experiment reduces those regressions but remains a workload heuristic.
Self-touching/retraced contours and independent two-path Boolean areas remain
outside the fast path. No public API or runtime dependency changes are included.

Repository: [bgtnt/polylinekit](https://github.com/bgtnt/polylinekit).
The experiment is on the local `codex/winding-performance` branch. Initial measured
source is `91b9d6c1ae7dc62e9753537de56e9fc20acb0ff7`; the final cached-sort/dispatch
source is `917215e69690ba39ffb2aad6f7cae2a56514b5a1`. Both use the unchanged production
engine from `792bf01` (runtime implementation at `dcd836a`). These commits were not
pushed as part of the experiment.

## Algorithm and scope

`SimpleSweep.TryArea` copies and validates one implicitly closed path, removes
consecutive/closing duplicate samples, sorts its vertices lexicographically, and
maintains active edges in an array-backed treap. Whenever insertion or removal
makes two edges neighbors, it checks their full segments for intersection. Exact
orientation orders an input endpoint relative to an active edge; no interpolated
height or rounded crossing coordinate controls the status tree.

Lexicographic `(x,y)` events correspond to a symbolic shear `x + epsilon*y`,
without modifying coordinates. This handles vertical edges and different vertices
with the same X coordinate. Ending edges are removed before starting edges at a
vertex. Legal adjacent endpoints and straight collinear subdivision are supported.
Proper crossings, nonadjacent contacts, retracing, overlaps and repeated nonadjacent
vertices reject the certificate. A rejected input is recomputed by the general
engine; rejection is not a zero-area result.

For an accepted simple path, `NonZero`, `EvenOdd` and `AbsoluteWinding` all equal
the absolute signed area. The prototype retains the production midpoint-form
cross term, TwoDiff tails and compensated summation; `SignedArea` is recorded too.
It constructs neither output polygons nor crossing lists. The classic neighbor
sweep is established geometry, not a claim of algorithmic novelty. See the
[CGAL sweep manual](https://doc.cgal.org/latest/Surface_sweep_2/index.html) for the
broader certified sweep approach and treatment of degeneracies.

The status treap uses deterministic mixed edge IDs. Observed work is consistent
with balanced-tree behavior, but adversarial order can make it quadratic; there
is no probabilistic expected-time guarantee for arbitrary inputs. A guaranteed
balanced status tree would bound this crossing-free certification to `O(n log n)`
combinatorial work, with separate arithmetic bit complexity. Storage is `O(n)`:
44 payload bytes per retained vertex slot, plus array headers, workspace and sort
delegate. Thread-local storage grows geometrically and is retained. Nested calls
lease another workspace; a throwing indexer still returns its lease.

This code does **not** implement a general Bentley–Ottmann sweep. The independent
exact oracle intentionally enumerates all pairs and rescans every slab; it is a
small-input correctness reference, never a performance candidate.

## Measurements

Results table is below; complete process medians and individual samples are in
[initial summary](../results/winding/active-sweep/summary.md) and
[final dispatch summary](../results/winding/active-sweep/policy-summary.md).

Median of three process medians, microseconds per request. "Always" tries the
simple sweep on every input; "Guarded" includes the dispatch scan below.

| Workload | Vertices | Engine | Always | Guarded | Engine / Guarded |
| --- | ---: | ---: | ---: | ---: | ---: |
| Simple diagonal comb | 258 | 155.12 | 55.91 | 56.48 | 2.75x |
| Simple diagonal comb | 1026 | 2221.90 | 260.31 | 267.62 | 8.30x |
| Simple diagonal comb | 4098 | 34744.70 | 1228.95 | 1258.12 | 27.62x |
| Radial star | 1024 | 1242.87 | 335.48 | 344.80 | 3.60x |
| Radial star | 4096 | 18811.45 | 1455.78 | 1470.76 | 12.79x |
| Horizontal simple comb | 4098 | 3209.03 | 1212.37 | 3208.55 | 1.00x |
| Ring | 4096 | 215.85 | 819.83 | 232.35 | 0.93x |
| Vertical subdivided rectangle | 4096 | 248.64 | 530.35 | 268.50 | 0.93x |
| Similar-stroke closed walk | 2048 | 136.75 | 312.35 | 146.41 | 0.93x |
| Random closed walk | 512 | 143.55 | 181.89 | 188.97 | 0.76x |
| Random closed walk | 2048 | 734.80 | 892.39 | 925.02 | 0.79x |
| Self-touching diagonal bars | 4096 | 36491.40 | 36232.50 | 36850.90 | 0.99x |

The guard keeps the large diagonal/star gains and reduces ring/rectangle overhead
to approximately 7–8%. It still slows the two larger random-walk fixtures by
26–32%, and it skips the profitable 4098-vertex horizontal comb. That is evidence
against enabling this policy generally. All 1800 warm allocation samples in the
final phase are zero; the initial phase's 64 B/call issue is described below.

Each row is a complete `ClosedPath(...).NonZero` request, including copying,
validation, sorting, status construction and any fallback. The benchmark constructs
bridged walks from the earlier fixtures before either timed implementation; it
does not claim to measure the original `EndpointBridged` two-input API overhead.
Vertices in these tables count the combined walk, not vertices per original path.
The three `filled-regions` fixtures are excluded because two independently filled
regions are a different operation.

There are 40 workloads: 11 earlier single-walk/bridged fixtures, seven shape
families at 64/256/1024/4096 nominal vertices, and a late-crossing control. Simple
combs have two additional closing-lane vertices. The old stacked/diagonal bars
touch or retrace their closing edge and correctly reject the certificate. The
new simple combs add an outside closing lane; both families remain visible in
the table. They are synthetic stress cases, not evidence of a universal speedup
or a distribution of real customer inputs.

All methods use the same binary64 vertices, whose SHA-256 is recorded per row.
The [fixture generator](../experiments/PolylineKit.ActiveSweep/Fixtures.cs) and
original [inputs.json](../results/winding/benchmarks/inputs.json) reconstruct them.
Counters include active-tree comparisons, neighbor checks, exact predicates and
peak active edges; they exclude vertex-sort comparisons. The diagonal simple
comb at 4098 vertices needs only 8189 neighbor checks despite many overlapping
edge bounding boxes. This is the source of the architectural improvement.

Environment: Windows 10.0.26200, Intel64 Family 6 Model 158 Stepping 12, 16 logical
processors, .NET SDK 10.0.401/runtime 10.0.12, x64, Release. Tiered compilation is
disabled. Each phase has three fresh processes, five calibrated samples per
method/workload/process; the final phase rotates method order across processes.
Reported values are medians of process medians, not confidence intervals. No builds
or other benchmark processes overlap measurement. Allocation counts are read
before result objects are created.

### Allocation and dispatch follow-up

Initial measurements expose 64 B/call in the prototype. The `IComparer` array-sort
path forms a comparison delegate on each call (see the
[.NET runtime source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs)).
The final prototype caches a workspace-owned `Comparison<int>` and supplies it
directly to span sorting. The final measured warm calls allocate zero bytes;
this is not a claim about cold calls, growth, reentrancy or extreme-exponent
BigInteger predicates. The initial raw evidence is preserved rather than rewritten.

`SweepPolicy.ShouldTry` requires at least 256 input vertices and at least
`max(16, n/16)` strict direction changes on **both** coordinate axes, ignoring
zero-length projections. This cheap `O(n)` scan bypasses the prototype when one
axis is nearly monotone. The policy does not certify simplicity and cannot make
an incorrect area correct; it only chooses between two checked paths.

This heuristic was chosen after the first measurements, on the same workload
families. Its results are exploratory, not held-out validation. It misses some
profitable horizontal combs and still pays for failed certification on random
walks. It therefore remains experimental and is not promoted into the public API.
An adaptive strategy within the existing preparation pass would avoid a second
scan/copy, but needs its own benchmark and regression review.

## Validation

The final experiment passes 53,810 assertions in normal and no-intrinsics processes.
Across 22,335 path cases, the sweep accepts 4083 and rejects 18,252, matching an
independent exhaustive all-pairs simplicity checker. That checker shares exact
orientation arithmetic but not sweep ordering, neighbor selection or status code.
The corpus includes 20,000 seeded grid walks, 2000 anisotropic radial polygons,
200 exponent-scaled rings, and explicit reversal/rotation/axis-exchange cases.

There are 476 independent exact-rational area comparisons, plus two documented
numerical-limit probes below. The oracle converts binary64 coordinates exactly,
finds pairwise rational intersection cuts, groups coincident boundaries, and
integrates exact slab trapezoids. It rounds only the final area to nearest-even
binary64, including subnormals. Agreement uses `2e-11` relative tolerance, with no
absolute tolerance that could hide a lost tiny area. The translated `2^52` triangle
and triangle with a subnormal coordinate retain exact areas `0.5` and `2^-775`.
Nested indexer calls and an exception after nesting are checked.

The existing solution builds without warnings. Its maintained 31,851 assertions
pass in all five modes (portable, modern, forced scalar, no AVX, no intrinsics),
including all previous winding-area repairs. The experimental sort change is
outside that library. Experiment output is retained in
[validation](../results/winding/active-sweep/validation).

### Numerical limit inherited from the engine

A topological simplicity certificate does not guarantee relative area accuracy.
For the triangle `(0,0), (L,L), (2L, double.BitIncrement(2L))`, both the prototype
and current engine return the same inaccurate value:

| L | Exact area of binary64 input | Both implementations |
| ---: | ---: | ---: |
| 1e12 | 122070312.5 | 134217728 |
| 1e16 | 20000000000000000 | 18014398509481984 |

The approximately 10% relative errors come from cancellation within cross products;
compensated summation cannot recover those lost bits. These cases are explicitly
recorded as a limitation, not counted as numerical accuracy successes. The new
algorithm preserves rather than fixes this arithmetic. A filtered accurate-product
fallback deserves a separate change with a performance budget.

## Reproduce

From the repository root, at final measured source `917215e` or its documentation
descendant:

```powershell
dotnet build PolylineKit.slnx -c Release
dotnet build experiments/PolylineKit.ActiveSweep -c Release
dotnet experiments/PolylineKit.ActiveSweep/bin/Release/net10.0/PolylineKit.ActiveSweep.dll check
./scripts/verify-implementations.ps1
$env:DOTNET_TieredCompilation = '0'
foreach ($run in 1..3) {
  dotnet experiments/PolylineKit.ActiveSweep/bin/Release/net10.0/PolylineKit.ActiveSweep.dll bench results/winding/benchmarks/inputs.json "results/winding/active-sweep/policy-run$run.json" $run
  if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed' }
}
python scripts/summarize-active-sweep.py policy-
```

For the initial measurements use source `91b9d6c`, output names `run1.json` through
`run3.json`, and summarize without the `policy-` argument. The summarizer validates
workload identity, hashes, certificate decisions, counters and sample medians
before producing either table.

## Next architectural step

The measured reduction in candidate work justifies further managed-code work;
changing language alone would retain the original pair count. No new C++ or SIMD
kernel was tested here. The irregular status-tree operations differ from the
existing SIMD-friendly independent bounds comparisons.

A general area-only sweep should maintain winding-labeled gaps between adjacent
active edges and integrate a gap only when a boundary changes. It must accumulate
A-only, B-only and both directly; tiny XOR cannot be recovered by subtracting large
rounded totals. Crossing-event order needs certified comparisons, not a priority
queue of rounded points. Coincident events, touching and collinear overlaps need
explicit handling or checked fallback. Sorting/rescanning all active edges at every
slab would merely move the quadratic work elsewhere. That full algorithm remains
unimplemented; this experiment establishes a useful restricted case and its costs.
