# Prepared double sweep results

Measured source: [`57c519cba31c039ef3dd7f254665d8c26a8716e2`](https://github.com/bgtnt/polylinekit/commit/57c519cba31c039ef3dd7f254665d8c26a8716e2).

Immutable path preparation reduces warm table time by **47.05–47.16%**,
from 38.5–38.7 ms to **20.3–20.5 ms**. With prepared zones and fresh query
preparation inside each operation, it takes **21.4–22.2 ms**. Fresh catalogue
preparation plus one table improves by 41.54–42.22%.
**All six prepared competitor gates still fail.** Current Winding remains
faster on this workload; the prepared sweep stays outside the shipping library.

## What changes

Each path owns a private clone of its original coordinates, validated bounds,
nonhorizontal edges with the existing interval slopes and scalar error bounds,
and sorted endpoint events. A query copies edge records into mutable scratch,
assigns the two winding roles and merges the sorted streams using the exact
previous comparator and edge-ID order. Sweep logic, crossing construction,
fill rules, interval area arithmetic and final numerical budgets stay unchanged.
No pair-result cache is used.

The immutable snapshot supports caller mutation and sharing across separate
engines. Each engine remains non-thread-safe and non-reentrant. Invalid or
uncertifiable snapshots defer existing fallback to query time; preparation
rejects null. Input validation precedes an AABB zero, and disjoint zero precedes
slope uncertainty. Fallback uses the private original-coordinate clone.
The benchmark coverage adapter still requires its shared source catalogue to
remain unchanged because it separately caches denominators and outer bounds.

## Query costs

Each table traverses 1078 original pairs in one direction, of which 211 reach
geometry. Both directions use the same 109 Census rings and original vertices.
The unprepared baseline is measured in the same binary and process series.

| Method | County warm, ms | District warm, ms | County fresh queries, ms | District fresh queries, ms |
|---|---:|---:|---:|---:|
| Guarded-filtered | 38.673 | 38.498 | 39.094 | 39.036 |
| Guarded-prepared | 20.478 | 20.342 | 21.422 | 22.181 |
| Clipper64-reused-data | 12.429 | 12.226 | 12.863 | 12.783 |
| Winding-intersection-only | 9.741 | 9.110 | 9.702 | 9.127 |

Fresh-query scope prepares each query once for the complete zone traversal,
not once per candidate pair. All methods pay their own query preprocessing.
Parsing/I/O is excluded equally. Fresh preparation plus one table creates a
new comparator and charges its Guarded/Clipper workspace growth; Winding uses
its warmed thread-local workspace. This is not a cold-process comparison.

## Memory and preparation tradeoff

Preparing both catalogues takes **2.19–2.20 ms**, compared with about 0.126 ms
for the filtered baseline. It allocates **1557376 B** versus 10904 B.
The 109 snapshots retain **1524672 array-element bytes** (about 1.45 MiB),
including cloned points, interval slopes, scalar bounds and sorted endpoints.
Here that is 144 bytes per vertex; it is not a universal per-vertex contract.
Object/array headers, shared source arrays and mutable engine scratch are
excluded. This payload sum is not total retained or peak memory.

Prepared warm tables allocate **0 B/op** in every sample. Fresh-query tables
allocate 568952 B for county zones and 986024 B for district zones. The complete
fresh-session allocation reaches 2107568 / 2089112 B. The full measured first
table already benefits versus the unprepared sweep; the gain is not dependent
on omitting preparation. It still loses to the competing implementations.

## Correctness and compatibility

Both variants certify all **422 geometry candidates**, with no fallback.
All **2156 pair areas, coverage values and certificate radii are bit-identical**.
Every existing diagnostic also matches: 65932 bands, 1504 constructed crossing
events, 2230171 status visits, 25580 interval-X evaluations and 527975 accepted
scalar comparisons out of 540765 attempts. All methods meet the unchanged
original-NTS budgets of 1 m² intersection and 1e-8 absolute coverage fraction.
NTS is independent, not an exact oracle; Clipper retains scale-1e6 quantization.

The prepared snapshot suite passed **1517 controls** across all eight engine
flag combinations, including null/empty/point/segment/zero-area inputs, invalid
rules, closure duplicates, endpoint ties, caller mutation, overflowing slopes,
fallback recovery, swapped roles and separate-engine sharing. The existing
exact-rational/analytic area suite passed **51422 assertions** through prepared
inputs across four ROI/cache combinations. Both suites passed normally and
with hardware intrinsics disabled. Historical scalar-filter commands preserve
**8624 method outputs and diagnostics**, including result/radius bits.

## Profile and decision

Separate 20-second warm county profiles used dotnet-trace 10.0.745401 and the
same measured binary with tiering disabled. Samples under TraverseReal include
four initial validation/warmup calls; catalogue preparation outside traversal
is excluded. No profiler ran during the benchmark processes.

| Inclusive sampled frame share | Unprepared filtered | Prepared |
|---|---:|---:|
| ProcessBand | 41.59% | 65.19% |
| AccumulateGap | 20.92% | 33.34% |
| HorizontalDifference | 13.56% | 21.33% |
| Endpoint sort | 27.69% | not observed |
| AppendEdges | 19.70% | not observed |
| CopyEdgesTo | not observed | 20.64% |
| MergeEndpoints | not observed | 0.13% |

Inclusive shares overlap; JIT inlining affects attribution. These are sampled
managed thread-time shares, not exact CPU percentages or per-routine timings.
A missing frame does not establish zero cost. Warm queries no longer build
slopes or sort complete endpoint arrays. Band processing and interval area
integration remain substantial; copying prepared edge records also remains.

Keep the prepared prototype as a tested research variant. Do not replace
Winding: even the fully prepared warm case takes about 1.65–1.66x Clipper
time and 2.10–2.23x Winding time. The result demonstrates useful amortization
within this sweep architecture, not a library-level competitive advantage.
Any further experiment should isolate remaining copying or certified area
integration costs and keep the same complete-query gates. No C++ or SIMD
speedup is established here.

## Full measurements and gates

| Direction | Method | Scope | ms | Process range ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | Clipper64-reused-data | prepare | 0.4016 | 0.3964–0.4090 | 968888 |
| county-zones | Clipper64-reused-data | prepare-plus-one | 13.0682 | 12.8230–13.1011 | 1626168 |
| county-zones | Clipper64-reused-data | warm-table | 12.4291 | 12.3911–12.6107 | 625088 |
| county-zones | Clipper64-reused-data | warm-zones-fresh-queries | 12.8628 | 12.6677–12.8733 | 975040 |
| county-zones | Guarded-filtered | prepare | 0.1262 | 0.1260–0.1264 | 10904 |
| county-zones | Guarded-filtered | prepare-plus-one | 39.2313 | 38.8493–39.3414 | 561096 |
| county-zones | Guarded-filtered | warm-table | 38.6728 | 38.6567–38.8837 | 0 |
| county-zones | Guarded-filtered | warm-zones-fresh-queries | 39.0943 | 38.6440–39.3878 | 1120 |
| county-zones | Guarded-prepared | prepare | 2.1988 | 2.1739–2.2194 | 1557376 |
| county-zones | Guarded-prepared | prepare-plus-one | 22.9343 | 22.8831–23.5361 | 2107568 |
| county-zones | Guarded-prepared | warm-table | 20.4778 | 20.4728–21.1540 | 0 |
| county-zones | Guarded-prepared | warm-zones-fresh-queries | 21.4222 | 21.3903–22.0501 | 568952 |
| county-zones | Winding-intersection-only | prepare | 0.1258 | 0.1251–0.1260 | 10544 |
| county-zones | Winding-intersection-only | prepare-plus-one | 9.8244 | 9.7631–9.8336 | 10480 |
| county-zones | Winding-intersection-only | warm-table | 9.7409 | 9.5908–9.8526 | 0 |
| county-zones | Winding-intersection-only | warm-zones-fresh-queries | 9.7023 | 9.5985–9.9738 | 1120 |
| district-zones | Clipper64-reused-data | prepare | 0.3982 | 0.3974–0.3993 | 968888 |
| district-zones | Clipper64-reused-data | prepare-plus-one | 12.6339 | 12.3886–12.6791 | 1624352 |
| district-zones | Clipper64-reused-data | warm-table | 12.2256 | 12.1133–12.2278 | 625088 |
| district-zones | Clipper64-reused-data | warm-zones-fresh-queries | 12.7828 | 12.6437–13.1613 | 1241472 |
| district-zones | Guarded-filtered | prepare | 0.1260 | 0.1250–0.1261 | 10904 |
| district-zones | Guarded-filtered | prepare-plus-one | 39.1210 | 38.7373–39.2415 | 542640 |
| district-zones | Guarded-filtered | warm-table | 38.4978 | 38.4799–39.0750 | 0 |
| district-zones | Guarded-filtered | warm-zones-fresh-queries | 39.0364 | 39.0273–39.0816 | 7384 |
| district-zones | Guarded-prepared | prepare | 2.1906 | 2.1841–2.2135 | 1557376 |
| district-zones | Guarded-prepared | prepare-plus-one | 22.6029 | 22.3038–23.4447 | 2089112 |
| district-zones | Guarded-prepared | warm-table | 20.3417 | 20.1841–21.0484 | 0 |
| district-zones | Guarded-prepared | warm-zones-fresh-queries | 22.1809 | 21.7091–22.1931 | 986024 |
| district-zones | Winding-intersection-only | prepare | 0.1249 | 0.1244–0.1263 | 10544 |
| district-zones | Winding-intersection-only | prepare-plus-one | 9.2680 | 9.1368–9.4191 | 10480 |
| district-zones | Winding-intersection-only | warm-table | 9.1103 | 9.1075–9.1401 | 0 |
| district-zones | Winding-intersection-only | warm-zones-fresh-queries | 9.1267 | 9.0960–9.2279 | 7384 |

Every full-query cell requires time <=0.8 times Clipper and <=0.9 times Winding.
Ratios divide the comparison time by the variant time; larger is better.

| Direction | Variant | Scope | Baseline/variant | Clipper/variant | Winding/variant | Pass |
|---|---|---|---:|---:|---:|---|
| county-zones | Guarded-filtered | prepare-plus-one | 1.000 | 0.333 | 0.250 | False |
| county-zones | Guarded-filtered | warm-table | 1.000 | 0.321 | 0.252 | False |
| county-zones | Guarded-filtered | warm-zones-fresh-queries | 1.000 | 0.329 | 0.248 | False |
| county-zones | Guarded-prepared | prepare-plus-one | 1.711 | 0.570 | 0.428 | False |
| county-zones | Guarded-prepared | warm-table | 1.889 | 0.607 | 0.476 | False |
| county-zones | Guarded-prepared | warm-zones-fresh-queries | 1.825 | 0.600 | 0.453 | False |
| district-zones | Guarded-filtered | prepare-plus-one | 1.000 | 0.323 | 0.237 | False |
| district-zones | Guarded-filtered | warm-table | 1.000 | 0.318 | 0.237 | False |
| district-zones | Guarded-filtered | warm-zones-fresh-queries | 1.000 | 0.327 | 0.234 | False |
| district-zones | Guarded-prepared | prepare-plus-one | 1.731 | 0.559 | 0.410 | False |
| district-zones | Guarded-prepared | warm-table | 1.893 | 0.601 | 0.448 | False |
| district-zones | Guarded-prepared | warm-zones-fresh-queries | 1.760 | 0.576 | 0.411 | False |

## Reproduction and evidence

.NET 10.0.12; Microsoft Windows 10.0.26200; X64;
Intel64 Family 6 Model 158 Stepping 12, GenuineIntel; 16 logical processors.
Three fresh sequential processes, five batches per row, 32 rows per process
and **480 samples**. Fixed orders diversify method positions. No concurrent
build, profiler or second benchmark ran. Ordinary OS activity is uncontrolled;
process ranges are not confidence intervals.

Release solution build passed without warnings/errors. The summarizer accepted
an unchanged positive copy, then rejected eight controlled corruptions: missing
row, order, median, output, binary hash, filter diagnostic, fallback record and
prepared payload. It preserved submitted JSON and produced no evidence or
summary for rejected records.

See the [fixed protocol and commands](PREPARED-SWEEP-PROTOCOL.md),
[prepared core](GuardedDoubleSweep.Prepared.cs), [checks](PreparedSweepChecks.cs)
and [public evidence](../scanbeam-prepared-evidence.json). The manifest records
measurements, diagnostics, per-path payloads, compatibility, profiles and hashes.
Raw records/traces stay under ignored artifacts and can be reproduced from
the recorded source and frozen inputs.

Local archive: `artifacts/scanbeam-prepared-57c519c.zip` (978732 bytes, 27 members).
SHA-256 `81a62435f15c3f2719df13a697aeee7acb559bb3b0df75225d33dc3c5a2f129c`.
Every member was checked against its source bytes. The archive is local, not
a public download.
