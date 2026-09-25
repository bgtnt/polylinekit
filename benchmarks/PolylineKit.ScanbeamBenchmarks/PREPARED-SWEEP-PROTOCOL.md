# Prepared double sweep experiment

Declared before timings. The preceding scalar filter at `c9f0072` reduced warm
table time to about 39 ms, but remained slower than current Winding and Clipper.
This experiment moves immutable per-path work out of repeated pair operations.
It changes neither the shipping library nor the numerical certificate.

## Preparation contract

`GuardedDoubleSweep.PreparePath` owns a clone of the original binary64 points.
It records validated bounds, nonhorizontal edges with the same interval slopes
and scalar error bounds, and the existing totally ordered endpoint events.
The result exposes no mutable storage and can be shared by separate engines.
An engine still owns mutable scratch and is neither concurrent nor reentrant.
The fill rule and first/second role are chosen at query time.

The prepared query copies immutable edge metadata into scratch, assigns the
first/second winding roles and global edge IDs, and linearly merges the two
sorted endpoint streams with the original comparator. The active sweep,
crossing construction, area expressions, budgets and fallback remain unchanged.
There are no pair-result caches, new event keys or altered numeric thresholds.

Null preparation is an argument error. Unsupported input and uncertain slope
metadata retain the private snapshot for query-time fallback. Preserve the
unprepared validation order, including the combined vertex budget, validation
of both complete inputs before an AABB-zero answer, and a disjoint-zero answer
before any slope uncertainty can cause fallback. Fallback sees the owned
original-coordinate snapshot, even if the caller later changes its array.

Independent controls reuse the exact-rational area suite with preparation on.
Add snapshot mutation, fallback/recovery, same-path and swapped-role reuse,
separate-engine sharing, endpoint tie ordering, both fill rules, degenerate and
invalid inputs. Compare prepared/unprepared value and error-radius bits,
fallback state and logical diagnostics wherever equivalent input is supplied.
Run correctness normally and with hardware intrinsics disabled. Old commands
and their default algorithm remain available for historical parity checks.

## Frozen workload and scopes

Retain the original inputs and admission policy from
[DOUBLE-PROTOCOL.md](DOUBLE-PROTOCOL.md): 109 valid original Census rings,
10588 vertices, 2156 directional pairs and 422 geometry candidates. All methods
use identical candidate masks. Require <=1 m² intersection and <=1e-8 absolute
coverage error against original NTS, which is not an exact oracle. Clipper keeps
scale-1e6 quantization; the double methods receive unrounded original coordinates.
Every prepared/unprepared frozen pair must have identical value, coverage,
error-radius bits, fallback state and existing diagnostic counts.

Four methods A-D:

- A: `Winding-intersection-only`.
- B: `Guarded-filtered`, common-Y restriction, X cache and scalar filter enabled.
- C: `Guarded-prepared`, the same engine flags with immutable preparation.
- D: `Clipper64-reused-data`, including its existing reusable geometry data.

Each direction uses four scopes:

1. `prepare`: construct both catalogues and their own-area/bounds data, including
   all method-specific geometry preprocessing. Do not warm query workspaces.
2. `warm-table`: reuse both complete catalogues and warmed mutable workspaces.
3. `warm-zones-fresh-queries`: reuse the zone catalogue and warmed workspace,
   prepare every incoming query once inside the timed operation, then traverse
   the full table. Query preparation is per query, not per candidate pair. The
   source arrays are reused as deterministic stand-ins for incoming geometry;
   loading, parsing and network I/O are excluded for every method.
4. `prepare-plus-one`: construct both catalogues and a fresh comparator, then
   execute one table. This charges workspace growth owned by a new Guarded or
   Clipper comparator. Winding's thread-local workspace is warmed by validation
   and earlier batches; this is not a cold-process/first-call comparison.

These are full-table costs, not isolated single-pair latency. The immutable core
snapshot supports caller mutation; the existing coverage adapter still requires
its shared source catalogue to remain unchanged because it caches denominators
and outer bounds. Do not present the adapter as a new public immutable API.

Use Release, `DOTNET_TieredCompilation=0`, three fresh sequential processes and
five calibrated batches per row. Fixed orders `ABCD`, `CDAB`, `BDAC` give 32 rows
per process and 480 samples. No concurrent build, profiler or second benchmark.
Report process ranges, managed bytes allocated per operation and preparation
costs. Record each path's retained array-element payload, including the cloned
coordinates, slope/scalar metadata and sorted events. That sum excludes object
headers, the shared original catalogue and mutable engine scratch; it is neither
total retained managed memory nor peak memory. Allocation counters remain the
measure of complete-operation allocation.

## Decision and evidence

Keep the previous competitor thresholds in each full-query cell: prepared time
must be <=0.8 times Clipper and <=0.9 times Winding, with correctness required.
The added fresh-query scope extends these gates to six prepared cells; report
all twelve prepared/unprepared cells. A gain against the filtered sweep alone
does not justify replacing Winding. No C++ or SIMD gain is claimed by this work.

The shared summarizer must validate the ordered complete matrices, finite samples,
medians, output digests, source/binary hashes, exact recorded pair diagnostics,
and recomputed prepared metadata before writing evidence. Negative controls
must reject changed rows, order, median, output, binary identity, diagnostics,
fallback or prepared payload without overwriting submitted evidence. Raw files
stay under ignored `artifacts/`; publish compact evidence and reproducible steps.

Optional profiles run separately on the measured binary. Sampled managed thread
time and overlapping/inlined frames cannot establish exact per-routine CPU cost.
Any later optimization requires a separately declared experiment.

## Commands

Restore locked and build Release at the recorded clean source revision. Use
`benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll`:

```text
check-prepared-sweep artifacts/prepared-sweep-check
benchmark-prepared-sweep artifacts/prepared-sweep 1 <revision>
benchmark-prepared-sweep artifacts/prepared-sweep 2 <revision>
benchmark-prepared-sweep artifacts/prepared-sweep 3 <revision>
summarize-prepared-sweep artifacts/prepared-sweep
```

Run timings sequentially with tiering disabled; summarize using the measured
binaries. CI runs correctness only. No package/runtime dependency is added.
