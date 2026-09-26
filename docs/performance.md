# Performance and memory

Choose the operation whose output you need. `PolylineArea.IntersectionArea`
avoids calculating unused own/union/XOR areas. `PolylineArea.FilledArea` returns
one fill; `CompareRegions` calculates the complete region comparison. None of
these constructs output contours. Contour-producing methods have different costs.

## What affects execution time

| Input or operation | Relevant cost |
|---|---|
| Increasing-x graphs | `BetweenGraphs` uses a scalar linear merge, O(n+m) time and O(1) auxiliary storage. |
| Simple closed paths | The area engine can certify suitable boundaries and use a simpler accumulation path. |
| Crossings, overlapping edges and retracing | Work depends on geometric events as well as vertex count; worst-case event counts can be quadratic. |
| One filled area on suitable integer arrays | The .NET 10 build can select an Int64 sweep internally. |
| Region intersection or complete comparison | Costs include both operands and their shared crossings; single-path speed claims do not transfer automatically. |

The integer specialization uses exact input integers, without rounding or
changing coordinates. Its current admission conditions include 64–1024 supplied
vertices, coordinates in ±2048, few Y levels and sampled dense/repeated geometry.
Other inputs retain the general boundary engine. Other list representations
and the .NET Standard assembly also use that engine. The implementation choices
are explained in [the algorithm reference](winding-area.md#one-selected-fill).

The general .NET 10 engine uses SIMD bounds filtering with scalar fallback.
The integer sweep is scalar C#. Neither SIMD availability nor a low vertex
count guarantees the fastest result on every shape. Some contours remain faster
with Clipper; there is no universal speed ranking.

## Measure your workload

The [maintained area benchmark](../benchmarks/PolylineKit.AreaBenchmarks/README.md)
compares complete public calls on original real contours with the four-integral
API and Clipper, including conversion and a separate preloaded comparison.
It reports per-contour and complete-batch time, warm allocations, source hashes,
numeric results and process variation. The
[benchmark guide](../benchmarks/README.md) also covers transformation and
cross-build measurements.

Use the same input, requested result and preparation policy when comparing
implementations. Clipper uses quantized coordinates and produces contours;
an area-only result is not interchangeable with that output. Warm measurements
exclude initialization and retained storage.

## Recorded results by scenario

The advantage depends on both geometry and the requested operation. The table
compares with **Clipper2 C# 2.0.0**. Times are medians of three process medians;
the ratio is **Clipper time / Core time**, so a value above 1 favors Core.
The rows are different workloads, not values to average into one speedup.

| Workload and requested result | Core µs | Clipper µs | Clipper / Core |
|---|---:|---:|---:|
| County/district coverage: warm indexed traversal | 9760.100 | 12957.250 | **1.33×** |
| Solaris: complete building evaluator | 8185 | 8222 | **1.00× (effectively tied)** |
| Solaris: prepared intersection batch, 205 pairs | 2308 | 2062 | **0.89×** |
| Nine Census rings: batch of NonZero areas | 1470.731 | 1914.525 | **1.30×** |
| Simple spiky star, 4096 vertices: NonZero area | 2193.412 | 56820.700 | **25.91×** |
| Dense integer grid, 512 vertices: NonZero area | 3057.537 | 5039.538 | **1.65×** |
| Same grid: EvenOdd area | 2945.356 | 8933.675 | **3.03×** |
| Repeated square, 512 vertices: NonZero area | 29.726 | 493.163 | **16.59×** |
| Same repeated square: EvenOdd area | 28.382 | 1272.256 | **44.83×** |

Territorial coverage is the `2fe5f74` repeat with the current Core detailed below;
all four required warm/preparation-inclusive cells took **24–26% less time** than
Clipper. The Solaris rows are the recorded `2019112` application result. Its
complete evaluator times are effectively tied within observed process variation,
while Core's prepared intersection batch is **12% slower**. The nine-ring Census
row is the recorded `eea1671` own-area result detailed below.

The five synthetic rows measure `PolylineArea.FilledArea` in
[`cc888de`](../benchmarks/PolylineKit.AreaBenchmarks/SYNTHETIC-RESULTS.md).
They are selected synthetic cases, with all inputs and
[commands to reproduce](../benchmarks/PolylineKit.AreaBenchmarks/SYNTHETIC.md)
in the repository. All rows used .NET 10.0.12 on Windows x64. None is a
measurement of a subsequently packed release.

Territorial coverage reuses Clipper's prepared inputs and engine across the
indexed traversal. The complete Solaris evaluator includes input reading through
the warm file cache, validation, preparation, matching and result rows; final
serialization and disk writes are excluded. Its prepared intersection batch
includes the shared touching/disjoint predicate for zero-area pairs. For
single-area rows, the Clipper comparator excludes conversion and Add, then times
Union under the chosen fill rule plus summation of output area. Output and
coordinate contracts differ: Core returns numeric areas on the supplied double
coordinates; Clipper constructs contours on its integer grid. These are not C++
measurements.

The large synthetic gains have distinct explanations:

- **The star has no self-intersections.** The engine certifies its simple boundary
  and uses a simpler area calculation. If the application already guarantees a
  simple ring, a direct shoelace sum is the cheaper relevant alternative; that
  narrower operation is not benchmarked here.
- **The grid mixes crossings, contacts and repeated edges.** Its 512 supplied
  vertices visit 64 distinct integer points. It qualifies for the integer sweep;
  the measured 1.65–3.03× gain does not establish the same result for every dense
  self-intersecting path.
- **The square traverses the same four edges 128 times.** NonZero area is 49;
  EvenOdd area is 0. Its 16.59–44.83× gain uses the integer specialization, not
  the general four-integral engine. Recognizing and reducing this constructed
  repetition before clipping was not measured.

These optimizations remain reachable through the public method. The selected
integer route does not apply to pairwise intersection, arbitrary double inputs
or the portable assembly. All public warm samples in this five-cell measurement
allocated zero bytes; first calls, different inputs and oversized workspaces can
allocate. The [complete record](../benchmarks/PolylineKit.AreaBenchmarks/SYNTHETIC-RESULTS.md)
also shows the slower general-engine cases, process ranges and allocation costs.

## Measured real contours

On the [nine-ring Census workload](../benchmarks/PolylineKit.AreaBenchmarks/README.md#recorded-real-contour-result-primary-gate-passed),
revision `eea1671c6288ea444e98cddcca9ee6d18669124b` passed the unchanged
Public/ClosedPath ≤1.10 criterion for both complete-batch fills. Every individual
ring/fill ratio also passed (0.9954–1.0152). The run used .NET 10.0.12 on Windows
x64, with three sequential processes and 1,200 samples.

| Complete nine-ring batch | Public | ClosedPath | Clipper incl. preparation | Clipper preloaded |
|---|---:|---:|---:|---:|
| NonZero | 1.471 ms | 1.468 ms | 2.206 ms | 1.915 ms |
| EvenOdd | 1.466 ms | 1.467 ms | 2.170 ms | 1.891 ms |

These are medians of three process medians. Public and ClosedPath allocated
**0 B/op in every warm sample** under that revision's cache policy. Public took about
the same time as ClosedPath; Clipper-full took 1.48–1.50× as long on this
workload. Clipper's output/grid contract differs, and this population exercises
the general engine, not the integer specialization. The detailed report includes
process ranges, all ring ratios, allocation counts and hashes. These are recorded
measurements of `eea1671`, not timings of the alpha package. They do not establish
an advantage over a shoelace sum for a trusted simple ring.

The earlier EvenOdd batch discrepancy did not recur. This is not evidence that
the cache change fixed it: the unchanged Clipper binary also ran faster in the
new session. The earlier failed measurement remains documented below.

## Territorial coverage: measured application result

The [RegionCoverage example](../examples/RegionCoverage/README.md) answers how
many square metres and what fraction of one territory lie in another. The
recorded population is 98 single-ring North Carolina counties and 11 single-ring
congressional districts. Each traversal covers 1,078 potential pairs: 211 bounds
candidates undergo intersection and 867 are rejected by the common bounds check.
This is separate from the nine-ring own-area benchmark above.

Measured source **`2fe5f74056590c7ce63cd7b34a6b0061e91f3fe7`**, three sequential
processes and 540 samples. All four predeclared whole-traversal comparisons
passed: intersection-only coverage took **24–26% less time** than direct
reusable Clipper2, including preparation plus one traversal. The repeat uses the
current Core and the same inputs, adapters and acceptance threshold as before.

| Zone / scope | Core ms | Clipper ms | Clipper / Core |
|---|---:|---:|---:|
| County / warm indexed traversal | 9.760100 | 12.957250 | 1.328× |
| County / preparation + one traversal | 9.920850 | 13.073800 | 1.318× |
| District / warm indexed traversal | 9.412775 | 12.668950 | 1.346× |
| District / preparation + one traversal | 9.580300 | 12.820000 | 1.338× |

All methods use the same outer STRtree; these are not index speedups. Clipper
reuses prepared integer input data and its engine, performs its per-pair
Clear/AddReuseableData work, executes one intersection and sums output areas.
No pair area is cached and no output matrix is allocated. All 90 Core
intersection-only warm allocation samples were zero bytes versus 625,088 bytes
per warm traversal for Clipper;
preparation-inclusive calls allocate for both.

Every pair's numeric results and the Clipper/NTS binaries match the earlier
measurement. Its 26–29% time advantage is now 24–26%; this is not a paired causal
A/B test of the workspace cap. The unchanged Clipper binary's timings changed too.
The [current report](../examples/RegionCoverage/PERFORMANCE.md) includes every
scope, process ranges, allocations, evidence hashes, reproduction commands and
a link to the unchanged historical report.

The related Census layers may share boundaries, and the workload was already
observed before optimization. It does not establish the same gain on arbitrary
GIS data or a packed alpha package. Two multi-component counties and three
multi-ring districts were excluded under the single-ring contract. NTS agreement
is a diagnostic, not an exact oracle.

## Polygon annotation evaluation

The [Solaris building-evaluation example](../examples/PolygonOverlapEvaluation/RESULTS.md)
reproduces all 172 supplied reference scores. At revision `2019112`, its 205-pair
prepared geometry batch took 2.308 ms with Core versus 2.062 ms with Clipper2;
complete evaluator times were effectively tied (8.185 versus 8.222 ms). Core
allocated 15.8% fewer bytes in the complete evaluator and ran 3.05× faster than
the configured NTS backend. A simple convex method was faster on its eligible
49-pair subset. These are different inputs and work from the single-area figures
above; the report includes preparation, fallback, process ranges and limitations.

## First use and retained workspace

Each active call owns its working storage; nested calls receive separate
workspaces. A completed workspace is cached only when its array payload is at
most **4 MiB per engine per thread**. The general engine and integer sweep have
separate caches, so together they can retain up to **8 MiB of array payload per
thread**, plus array/object headers, small fixed metadata and 264 bytes of
thread-local predicate buffers. The portable build
has only the general engine. This is a retained-cache limit, not a bound on
active-call memory, concurrent calls, exact arithmetic or the process heap.

Larger workspaces are not cached and become eligible for garbage collection
after return. Repeated large dense inputs therefore allocate again; the policy
trades their previous zero warm allocations for bounded idle retention. Smaller
workspaces still reuse buffers. First use, growth, nesting and some exact
predicates can allocate. The policy changes retention, not geometry or fill rules.

At the integer route's 1024-vertex limit, its crossing buffer alone can reach
**12 MiB during a call**. It exceeds the cache limit and is not retained afterward.
The [algorithm documentation](winding-area.md#cost-and-storage) describes the
inventory and complexity. Historical warm B/op figures apply to their recorded
cache policy; they are not promises for oversized inputs under the current policy.

The [retention regression](../tests/PolylineKit.Checks/WorkspaceRetentionChecks.cs)
includes a deterministic 1024-vertex walk alternating between two Y levels.
A separate historical probe compared `0e53b07` with the uncommitted cache-policy
implementation before it was included in `eea1671`:

| Engine | Earlier retained payload | Probe with cache limit | Approximate repeated-call allocations with limit |
|---|---:|---:|---:|
| General boundary engine | 65.12 MiB | 0 | 97.14 MiB |
| Integer sweep | 12.15 MiB | 0 | 24.15 MiB |

These are historical stress-case allocation checks, not measurements of the
alpha packages, typical-input costs or timings. The probe used reflection to
invoke the engines; its allocations include that invocation's overhead.
Each engine's result bits were unchanged. First-call allocations did not decrease;
the cache limit prevents idle retention, not the large working set itself.

## Supporting measurements

The [earlier real-contour measurement](../benchmarks/PolylineKit.AreaBenchmarks/README.md#recorded-real-contour-result-primary-gate-failed)
at `0e53b0713b2d0c6ebd0b7603125010defccb81ed` **failed its primary preservation gate**:
the EvenOdd complete batch was 23.19% slower than ClosedPath, although all 18 individual
ring/fill ratios stayed within 10%. The recorded ranges, allocations, correctness checks
and local evidence hashes remain visible; the cause of the batch discrepancy is unisolated.
Those timings precede the core/Clipper separation and retained-cache limit; they
apply to the recorded binaries, not a new measurement of the current build.

Detailed prior measurements remain in an immutable
[public snapshot](https://github.com/bgtnt/polylinekit/tree/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3):
[single-area integration](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/benchmarks/PolylineKit.ScanbeamBenchmarks/FILLED-AREA-RESULTS.md),
[intersection-only coverage](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/RegionCoverage/INTERSECTION-RESULTS.md),
and [first-use/storage data](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/benchmarks/winding-review-evidence.json).
Each applies to its recorded code revision, hardware, inputs and requested work.
They preserve slower cases and are not new timings of subsequent API changes.

The original raw measurements, frozen summarizers and data have been assembled
into a verified local archive; see [the alpha evidence inventory](releases/0.1.0-alpha.1.md#measurement-evidence).
The archive is not yet uploaded publicly. Its planned release location is not
an available raw-data URL until publication is explicitly completed.
