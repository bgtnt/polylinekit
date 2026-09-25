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

## First use and retained workspace

Each active call owns its working storage. Consecutive calls on a thread reuse
buffers; nested calls receive separate workspaces. First use, buffer growth and
some exact-predicate fallbacks allocate. A thread retains its largest cache,
so zero warm B/op does not mean zero retained memory.

The general engine and integer sweep have separate caches. At the integer
route's 1024-vertex limit, its crossing buffer alone can have a **12 MiB capacity**;
other arrays and object headers add memory. This is a bound, not a typical-input
measurement. The [algorithm documentation](winding-area.md#cost-and-storage)
describes the buffer inventory and general engine complexity.

## Supporting measurements

Detailed prior measurements remain in an immutable
[public snapshot](https://github.com/bgtnt/polylinekit/tree/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3):
[single-area integration](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/benchmarks/PolylineKit.ScanbeamBenchmarks/FILLED-AREA-RESULTS.md),
[intersection-only coverage](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/RegionCoverage/INTERSECTION-RESULTS.md),
and [first-use/storage data](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/benchmarks/winding-review-evidence.json).
Each applies to its recorded code revision, hardware, inputs and requested work.
They preserve slower cases and are not new timings of subsequent API changes.
