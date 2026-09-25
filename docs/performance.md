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

## Measured real contours

On the [nine-ring Census workload](../benchmarks/PolylineKit.AreaBenchmarks/README.md#current-real-contour-result-primary-gate-passed),
revision `eea1671c6288ea444e98cddcca9ee6d18669124b` passed the unchanged
Public/ClosedPath ≤1.10 criterion for both complete-batch fills. Every individual
ring/fill ratio also passed (0.9954–1.0152). The run used .NET 10.0.12 on Windows
x64, with three sequential processes and 1,200 samples.

| Complete nine-ring batch | Public | ClosedPath | Clipper incl. preparation | Clipper preloaded |
|---|---:|---:|---:|---:|
| NonZero | 1.471 ms | 1.468 ms | 2.206 ms | 1.915 ms |
| EvenOdd | 1.466 ms | 1.467 ms | 2.170 ms | 1.891 ms |

These are medians of three process medians. Public and ClosedPath allocated
**0 B/op in every warm sample** under the current cache policy. Public took about
the same time as ClosedPath; Clipper-full took 1.48–1.50× as long on this
workload. Clipper's output/grid contract differs, and this population exercises
the general engine, not the integer specialization. The detailed report includes
process ranges, all ring ratios, allocation counts and hashes.

The earlier EvenOdd batch discrepancy did not recur. This is not evidence that
the cache change fixed it: the unchanged Clipper binary also ran faster in the
new session. The earlier failed measurement remains documented below.

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
Compared with `0e53b07`, its retained workspace payload changes as follows:

| Engine | Earlier retained payload | Current retained payload | Approximate repeated-call allocations now |
|---|---:|---:|---:|
| General boundary engine | 65.12 MiB | 0 | 97.14 MiB |
| Integer sweep | 12.15 MiB | 0 | 24.15 MiB |

These are stress-case allocation checks, not typical-input costs or timings.
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
