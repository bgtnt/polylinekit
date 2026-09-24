# Integrated simple-path certification

The [standalone sweep](winding-active-sweep.md) showed large gains on selected
simple contours, but its separate dispatch scan, copy, vertex sort and full-engine
retry penalized unrelated inputs. This follow-up integrates certification into
the engine's existing candidate pass. It is enabled for single closed walks,
including the walk constructed by `EndpointBridged`; `FilledRegions` does not use
the certificate. Public method signatures, fill semantics and area arithmetic are
unchanged. There are no new runtime dependencies.

**Result:** retain the integration as a measured optimization for review. It
preserves large simple-contour gains, demonstrates gains on independently chosen
new shapes, and removes the former 26–32% random-walk penalty on the measured
inputs. It is not uniformly faster: ordinary smooth inputs still pay several
percent, a 256-vertex star is 27% slower, and late certificate rejection has a cost.
These tradeoffs remain visible rather than being tuned away on the new data.

Repository: [bgtnt/polylinekit](https://github.com/bgtnt/polylinekit), local branch
`codex/winding-performance`. Measured integrated source:
`561482873463a57b141e2b8b18a83d07b6ff2a35`. Baseline runtime source is `9ecf6a7`,
whose engine implementation is `dcd836a`. No push, merge or package publication
was performed in this step.

## Selection based on work already performed

Input validation, duplicate cleanup, links, axis selection, sorted edge bounds
and candidate compaction run once. While processing rows of bounding-box
survivors, the engine counts the pairs it has already considered. The count includes
adjacent edges; it is a work estimate, not a count of proper crossings.

An attempt requires all of the following:

- One loop, at least 256 cleaned vertices.
- At least `8*n` accumulated bounding-box survivor pairs at the end of a row.
- No crossing or overlap-split events found so far.
- No collinear overlap found, including a coincident segment that emits no splits.
- No earlier attempt during this call.

These constants were fixed before timing the new fixture set. There is no separate
shape-analysis pass. Inputs with early crossings or retracing abandon eligibility;
ordinary low-candidate paths never reach the work threshold.

`PreparedSimpleSweep` borrows the validated vertex array and owns only event/status
arrays. It still sorts vertex events: the broad phase orders **edges by bounding-box
minimum**, which is not a complete vertex-event order. The implementation does not
claim to eliminate that necessary sort. It does avoid re-copying/re-validating the
input and redoing the broad-phase preparation or previously checked pairs.

The certificate uses exact orientation and lexicographic symbolic shear, supporting
verticals and collinear subdivision. It rejects self-crossings, nonadjacent touches,
retracing and overlap. On rejection the original pair loop resumes at the next row.
On acceptance, all crossing offsets are zeroed and the **original four-chain area
accumulation** runs unchanged. No substitute area formula, clamping, grid, float
conversion or altered product/summation order is introduced.

The certificate borrows storage from the already leased engine workspace, so
nested API calls cannot share a live status tree. It clears its vertex-array
reference in `finally` to avoid retaining a superseded engine buffer. Exact
predicate evaluations during successful and failed attempts contribute to the
existing diagnostic count. Crossing counts and numerical outputs retain their
meaning; diagnostic exact-predicate counts need not match the old implementation.

## Bounded work and memory

Deterministic treap priorities alone do not guarantee balanced depth. Each insertion
search, rotation, removal and predecessor/successor traversal consumes a budget of
`32*n*(floor(log2(n))+1)` steps. Exhaustion returns false and resumes the general
algorithm. Thus a certificate attempt adds bounded `O(n log n)` combinatorial work,
plus the bit cost of exact predicates; it does not give the whole engine a new
worst-case bound. The general candidate/event work can still be quadratic.

Modern .NET uses span sorting with a cached comparison delegate. The .NET Standard
2.0 path uses an allocation-free heap sort instead of the allocating comparer
adapter. Seven retained arrays add 28 payload bytes per certificate vertex slot,
plus array headers, the certificate object and a delegate on the modern target.
The object and arrays are allocated lazily only when an attempt is selected;
first use, growth, nesting and extreme-exponent integer predicates can allocate.
Warm measured calls allocate zero. No new unsafe code or SIMD arithmetic is needed;
the existing SIMD bounds compaction remains in use.

For an independent comparison or a workload that disfavors certification:

```csharp
AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", true);
```

This is an implementation control, not a different area contract. Tests compare
enabled and disabled results; benchmark baselines load the actual older assembly.

## Measurements

Three fresh processes per implementation, five samples per workload/process,
Release .NET 10.0.12 on Windows 10.0.26200, x64 AVX2, Intel64 Family 6 Model 158
Stepping 12, 16 logical processors. SDK: 10.0.401. Tiered compilation is disabled.
Process order is baseline/integrated, integrated/baseline, baseline/integrated.
No agent builds or other agent benchmarks overlap these runs. OS scheduling noise
is retained in raw samples; small percentage differences are not confidence bounds.

The runner records each DLL's SHA-256:

- Baseline: `619dee599ba01e842a410eb027b16de5c98ec812ece9941edcbf15820942d80e`.
- Integrated: `b32c6f5c6168575355dd77680c99c172f2b9b46615d3d4eec8bc311a4e397c68`.

There are 81 workloads: 42 development controls, 36 new paths and three independent
filled-region controls. The new set was generated separately before timings:
perturbed outlines, narrow ribbons, sheared canyon profiles, subdivided
parallelograms, retraced outlines, touching rectangles, late crossings, random
walks and rotated two-lobe outlines, each at 64/257/1024/2048 vertices. Seeds and
transforms are fixed in [FreshFixtures.cs](../experiments/PolylineKit.ActiveSweep/FreshFixtures.cs).
No constants or inputs were changed after their timings were observed. This is
synthetic geometric validation, not a claim about a customer-input distribution.

Median of process medians, microseconds per complete call:

| Workload | Vertices | Baseline | Integrated | Speedup |
| --- | ---: | ---: | ---: | ---: |
| New sheared canyon | 257 | 174.32 | 95.44 | 1.83x |
| New sheared canyon | 1024 | 2717.86 | 482.98 | 5.63x |
| New sheared canyon | 2048 | 10951.80 | 1020.46 | 10.73x |
| Simple diagonal comb | 4098 | 35211.80 | 1763.41 | 19.97x |
| Radial star | 4096 | 19202.20 | 2082.21 | 9.22x |
| Radial star | 256 | 85.20 | 108.02 | 0.79x |
| Ring | 4096 | 218.21 | 235.28 | 0.93x |
| Vertical subdivided rectangle | 4096 | 251.20 | 265.94 | 0.94x |
| Original random closed walk | 512 | 143.66 | 147.57 | 0.97x |
| Original random closed walk | 2048 | 750.32 | 726.53 | 1.03x |
| New random closed walk | 1024 | 338.30 | 339.31 | 1.00x |
| New random closed walk | 2048 | 750.58 | 731.99 | 1.03x |
| Late-crossing diagonal comb | 1026 | 2251.27 | 2486.73 | 0.91x |
| Late-crossing diagonal comb | 4098 | 35229.60 | 36421.60 | 0.97x |
| Independent filled regions | 2048 total | 233.82 | 237.10 | 0.99x |

The [complete table](../results/winding/integrated-sweep/summary.md) includes every
control and process range. All **2430 allocation samples are 0 B/call**. The
certificate is accepted for six development and three new inputs; two development
late-crossing controls reject it. No new random walk attempts a certificate.

The smaller star illustrates the remaining selection limitation: already-spent
candidate work does not prove that enough work remains to repay certification.
On smooth inputs the row-level accounting/branching still has overhead even when
no attempt occurs. A late crossing can require both the attempted certificate and
the complete general algorithm. These costs are measured, not hidden by timing
only successful certificates. The earlier standalone peak speedups are larger
because that prototype did not first spend `8*n` candidate checks; they are not
directly interchangeable with this adaptive result.

## Correctness evidence

The solution builds without warnings. **42,012 assertions pass in each of five
implementation modes**, including 10,161 added checks for prepared certification:

- 10,000 seeded grid paths checked against exhaustive segment-pair simplicity.
- Accepted and rejected attempts, reversal, coordinate exchange, cleanup,
  double traversal and retracing.
- A late crossing that rejects certification and resumes the prepared pass.
- Certified contours scaled by powers from `2^-500` through `2^280`, and translated
  by `2^52`, so extreme arithmetic is exercised on the new path rather than bypassed.
- Input indexers read exactly once, nested API calls and exceptions after nesting.
- Public bridging, workspace reuse, zero warm allocations and fresh diagnostics
  after a collapsed bridged walk.

The separate fixture checker passes 424 assertions over 78 single-path inputs,
both with the modern and portable library. All four integrals and crossing counts
equal the same assembly with certification disabled. Nine small new cases also
use the independent exact rational oracle; 16 cases use analytic controls (ribbon
area 1/8, parallelogram 2625/32, touching rectangles 30, retracing zero). Nonzero
analytic comparisons allow binary64 input rounding.

The benchmark summarizer additionally verifies exact equality of numerical values
and crossing counts against the **old DLL** on all 81 workloads, including all
five independent-region areas. It checks fixture hashes, stable certificate
outcomes and all sample medians. Validation logs are retained in
[validation](../results/winding/integrated-sweep/validation).

Area arithmetic is unchanged, including the existing
[skinny-triangle cancellation limitation](winding-active-sweep.md#numerical-limit-inherited-from-the-engine).
The optimization does not repair that approximately 10% error. It also does not
make self-intersecting area integration asymptotically faster or implement a full
crossing-event sweep.

## Reproduce

Use an isolated checkout of `9ecf6a7` to build the baseline library. Build current
source in a separate checkout. Keep both Release `net10.0/PolylineKit.dll` files;
the optional runner explicitly loads the selected file before compiling typed
calls. Example from the current repository root:

```powershell
dotnet build PolylineKit.slnx -c Release
dotnet build experiments/PolylineKit.PreparedSweepPerf -c Release
./scripts/verify-implementations.ps1
$currentDll = (Resolve-Path src/PolylineKit/bin/Release/net10.0/PolylineKit.dll).Path
$baselineDll = 'C:/path/to/baseline/src/PolylineKit/bin/Release/net10.0/PolylineKit.dll'
dotnet experiments/PolylineKit.PreparedSweepPerf/bin/Release/net10.0/PolylineKit.PreparedSweepPerf.dll $currentDll results/winding/benchmarks/inputs.json check 0 integrated
$env:DOTNET_TieredCompilation = '0'
foreach ($run in 1..3) {
  $order = if ($run -eq 2) { @('integrated','baseline') } else { @('baseline','integrated') }
  foreach ($variant in $order) {
    $dll = if ($variant -eq 'baseline') { $baselineDll } else { $currentDll }
    dotnet experiments/PolylineKit.PreparedSweepPerf/bin/Release/net10.0/PolylineKit.PreparedSweepPerf.dll $dll results/winding/benchmarks/inputs.json "results/winding/integrated-sweep/$variant-$run.json" $run $variant
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed' }
  }
}
python scripts/summarize-integrated-sweep.py
```

Each request includes validation/copy, current preparation, attempt if selected,
fallback if rejected, and final area accumulation. Fixture construction and
reflection used to read internal diagnostics are outside timed intervals. The
older bridged fixtures are materialized as single closed walks before timing;
the three `FilledRegions` controls measure the actual two-input API.

The implementation is ready for review with its measured limitations. A further
selection change should use a newly reserved input set; this fresh set has now
served its evaluation purpose and should not be repeatedly retuned against while
still being called held-out evidence.
