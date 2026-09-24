# Sorting, SIMD and architecture follow-up

This experiment continues the [managed/native assessment](winding-performance.md).
It retains a change to the managed data flow: sort natural edge runs, store bounds
in separate coordinate arrays, compact candidate indices, then evaluate exact
geometry. SIMD belongs in the bounds stage, where four independent comparisons
can be done together without changing predicate or compensated-sum arithmetic.

The combined implementation helps larger inputs but is **not uniformly faster**.
The packed hierarchy prototype is retained as evidence, not used by the library.
A full active-edge rewrite has not been implemented or validated.

The later [cost-of-fixes investigation](winding-fix-cost.md) traces the slowdown
to exact-chain organization and tests two own-area specializations. Both remain
outside the library because their dense-case benefit comes with common-case
regressions.

## Compared implementations

The baseline is `7962d19`, whose runtime code is the previously measured
`6fc1c35`. The following alternatives all start from that baseline:

| Variant | Change |
| --- | --- |
| `sort` | Merge at most 64 natural ascending/strictly descending runs, with `Array.Sort` fallback; minimum 64 edges in this exploratory variant. |
| `bvh` | Build an O(n) packed hierarchy over consecutive groups of eight sweep-ordered edges; skip ranges whose other-axis bounds do not overlap. |
| `simd` | Separate bounds arrays and inline four-lane rejection with `Vector256<double>`, consuming surviving lanes in order. |
| `compact` | Natural runs from 256 edges, separate bounds arrays and a separate candidate-compaction stage; SIMD only for primary-axis windows of at least 16 candidates. |
| `compact-scalar` | The exact same combined assembly with `PolylineKit.DisableSimd=true`. |

The measured combined source is `befef28`; the retained source is identical at
`dcd836a`, followed by regression tests at `3bfe544`. Both use WindingEngine Git
blob `4cfed651a79b42a72abda664490ceaa459511802`. The intermediate commits belong
to isolated experimental worktrees; the [patches](../results/winding/search/patches/combined.patch)
reconstruct each candidate from `7962d19` without relying on their continued
availability. Timing results identify the loaded assembly path; source identity
comes from the recorded commit/patch, not from a timing-time DLL hash.

### Why the data flow matters

The original scalar loop mixes cheap bounds rejection with expensive predicates
and crossing construction. Simply inserting masks and lane iteration into that
loop adds branches to every surviving pair. The combined implementation first
writes eligible pair indices into a reusable array, then runs the unchanged
geometry body over those indices. The extra stores have a cost, but keep vector
dispatch out of the predicate loop. Short windows use a simple scalar scan.

The bounds layout changes from an array of three-double structs to three double
arrays. It retains the same 24-byte payload per edge slot and makes contiguous
vector loads possible. Natural-run merge storage adds 12 bytes per retained slot;
candidate indices add four. There is also a 65-int run buffer and additional
array headers. First use and growth allocate; the measured warm calls allocate
zero bytes. Per-thread buffers retain their largest capacities.

The .NET 10 implementation uses
[Vector256.LoadUnsafe](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256.loadunsafe?view=net-10.0)
with explicit complete-group bounds: `b <= n - 4`, and each coordinate array has
at least `n` elements. It needs no pointer blocks or new dependency. Hardware,
AppContext and target checks select the scalar fallback. The .NET Standard 2.0
build contains no vector path. This experiment measured x64 AVX2, not ARM64.
The [JIT disassembly](../results/winding/search/validation/candidates-disassembly.txt)
confirms packed-double comparisons, mask extraction and `tzcnt` in the measured
candidate-filter method; vector use is not inferred from source syntax alone.

SIMD preserves inclusive overlap comparisons, visits lanes in ascending order,
and handles remaining elements scalarly. It does not use float, FMA, approximate
predicates or reassociated area sums. Natural sorting may change the order of
equal sweep keys relative to the baseline's unstable sort; numerical equality
with the old implementation is checked within the existing rounding contract.

## Measurements

Same Windows/x64 workstation and .NET 10.0.12 environment as the preceding
assessment, Release, tiered compilation disabled. Each variant runs in **three
fresh processes**, with five calibrated samples per workload per process. Variant
order changes across the three groups. No builds or other benchmark processes
overlap the timed runs. Each sample includes validation/copying, sorting, index
construction where applicable, crossing work and area accumulation.

The 30 workloads include the original 14 fixtures, both orientations of thin
subdivided rectangles, rings, horizontal stacked bars, diagonal bars and radial
stars. Extra inputs are generated deterministically in the committed
[runner](../experiments/PolylineKit.WindingPerf/Program.cs). The diagonal bars
deliberately expose a limit: many disjoint segments have overlapping AABBs, so
neither an AABB hierarchy nor SIMD can avoid the exact predicate work.

Median of three process medians, microseconds per complete call:

| Workload | Vertices per path | Baseline | Combined | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Similar strokes | 64 | 9.56 | 9.89 | 0.97× |
| Random walks | 64 | 16.52 | 17.51 | 0.94× |
| Similar strokes | 1024 | 170.37 | 133.99 | 1.27× |
| Dense-crossing graphs | 1024 | 235.99 | 180.85 | 1.30× |
| Random walks | 1024 | 929.83 | 712.47 | 1.31× |
| Filled regions | 1024 | 259.23 | 227.67 | 1.14× |
| Degenerate grid | 256 | 7149.45 | 7099.65 | 1.01× |
| Vertical subdivided rectangle | 4096 | 422.90 | 247.46 | 1.71× |
| Ring | 4096 | 380.52 | 216.32 | 1.76× |
| Stacked bars | 4096 | 14100.05 | 4731.48 | 2.98× |
| Diagonal bars | 256 | 188.48 | 212.03 | 0.89× |

The [full six-variant table](../results/winding/search/summary.md) and raw runs
include the regressions. Small original fixtures lose roughly 2–6%; filled
regions at 256 vertices lose about 4%; diagonal bars at 256 take about 12% more
time. The 1% grid difference should not be called an improvement. These are
finite workstation measurements, not statistical confidence bounds.

The forced-scalar ablation isolates SIMD within the combined implementation:
random walks at 1024 improve from 842.49 to 712.47 µs (1.18×), and stacked bars
at 4096 from 11010.15 to 4731.48 µs (2.33×). Other rows show little benefit or
small overhead. The larger ring/rectangle gain comes from sorting, not SIMD.

The packed hierarchy helps stacked bars (1.93× at 4096) but loses on most other
inputs after construction/traversal costs. This rejects **this prototype**, not
every BVH or R-tree design. Combining it with natural sorting, tuning leaf sizes,
and prepared-index reuse were not measured. The simple hierarchy is not included
in the production implementation.

All **2700 warm allocation samples** across the six variants are zero. Values
are checked against the baseline with relative/scaled tolerance 1e-10; scalar
and SIMD equality is also checked more strictly by maintained regression tests.

## Validation

- Independent review of the preceding scalar/bounds changes found no actionable
  defect. A separate review of natural-run sorting found no indexing/alias issue.
- Final solution and optional runner build without warnings/errors.
- **31,851 checks pass in each of five implementation modes**, including 28,267
  winding checks. The new 240 checks cover bounds contact, sparse vector masks,
  tails, sizes around sort/growth thresholds, axis exchange, reversal and exact
  scalar/SIMD result equality. They use an independent slab oracle and integer
  shoelace for signed area; slab cancellation is unsuitable as a tight signed-area
  oracle for the largest alternating-bar fixtures.
- Existing engine/pipeline/consumer regression checks pass in all four modes.
- The numerical review regressions, tiny XOR cases, extreme exponents and nested
  workspace tests remain enabled. No frozen recognition dataset evaluation was run.

Logs are in [validation](../results/winding/search/validation/implementation-checks.txt).
The numerical limits documented in [winding-area.md](winding-area.md#robustness)
remain. More throughput does not make rounded intersection coordinates exact.

## Architectural alternatives

The following are design proposals, not measured speedups or completed features.

**Prepared immutable geometry** is the lowest-risk larger change when a path is
compared repeatedly. Snapshot its vertices and cache links, bounds/order, own
areas, self-crossing events and local winding information. Each comparison then
finds cross-input intersections and combines the prepared topology. Account for
preparation time, retained storage and the number of comparisons needed to break
even. Arbitrary transformations invalidate geometric caches; rigid/uniform
transforms need an explicit policy rather than silently reusing stale bounds.
This does not help a one-shot input automatically.

**An active-edge, area-only sweep** is the more ambitious one-shot alternative.
Process endpoint/crossing event bundles in sweep order, update active-edge order
and integrate the changing winding regions incrementally. It could avoid the
current broad-phase false positives, full `Found`/`Sorted` event copies and
per-edge event sorts. Certified curve-intersection sweeps can achieve
`O((n+k) log n)` event processing, including degeneracies with suitable traits;
see [CGAL's surface-sweep description](https://doc.cgal.org/latest/Surface_sweep_2/index.html).
That bound does **not** automatically apply to a new all-integrals engine:
scanning every active edge after every event would lose it.

A credible design must specify exact comparison of intersection-event coordinates,
coincident events, vertical edges, overlapping multiplicities and local winding
updates for both paths. Exact orientation signs alone do not certify the order
of constructed crossing points; the distinction between predicates and
constructions also appears in
[CGAL's Boolean-operation requirements](https://doc.cgal.org/latest/Boolean_set_operations_2/index.html).
Preserve local compensated area contributions and direct exclusive-region
accumulation: recovering a tiny XOR by subtracting large rounded totals would
reintroduce an already fixed defect. A streaming sweep might reduce retained
event storage, but no O(n) memory guarantee is established here.

For the next larger experiment, first target the **no/low-intersection diagonal
bar family**, where the current bounds approach does unnecessary quadratic work.
Prototype only an internal active-edge ordering/event component against exact
integer/rational fixtures, then add coincident/overlapping events and compare
against the maintained oracles. Retain the current engine as the reference until
both correctness and complete-operation gains are demonstrated. Porting the
existing architecture to C++ would not itself remove this work.

## Reproduction

Build and run the existing verification scripts as in the previous assessment.
The [runner README](../experiments/PolylineKit.WindingPerf/README.md) describes
loading a chosen assembly and the scalar switch. To reproduce alternatives,
create isolated checkouts of `7962d19`, apply one patch from
`results/winding/search/patches`, restore/build each library, and use the current
runner. Each patch is an alternative against the baseline; do not stack them.

Run each variant three times in fresh processes on an otherwise idle machine.
The recorded group orders are baseline/sort/bvh/simd/compact/compact-scalar,
the reverse order, then bvh/sort/compact/baseline/compact-scalar/simd.
The `compact-scalar` variant uses the same assembly as `compact` with
`POLYLINEKIT_FORCE_SCALAR=1`. Include all preparation in the timed call.

Regenerate and validate the checked-in tables with:

```powershell
python scripts/summarize-winding-search.py
```
