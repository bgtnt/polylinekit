# Coalescing filled sweep gaps: results

Measured source: [`4b8a131117264ec950a373bfae5462edd216b2c5`](https://github.com/bgtnt/polylinekit/commit/4b8a131117264ec950a373bfae5462edd216b2c5).
Historical control: [`5bc86fb238be2ea751aa3429f6eb37045cb2651b`](https://github.com/bgtnt/polylinekit/commit/5bc86fb238be2ea751aa3429f6eb37045cb2651b).

Coalescing contiguous filled gaps reduces warm table time by **24.56–25.11%**,
from **18.35–18.68 ms to 13.74–14.09 ms** against the same-binary baseline.
Across all six full-query cells, the reduction is **21.72–25.11%**. It removes
**71.61% of interval area integrations**, with all 422 geometry calls certified.
**All six selected competitor gates still fail**: Clipper takes 12.30–12.71 ms
and shipping Winding 9.10–9.60 ms for warm tables. The sweep remains experimental.

## Work removed and contract retained

Both methods use copied prepared geometry, ROI, endpoint caching, scalar-order
filtering and specialized area arithmetic. The sixth flag `coalesceGaps`, off
by default, stores one pending filled contribution per left edge. Contributions
join only when their ordered edge pair and consecutive full event Level match,
and all start/finish Y intervals are point-valued. Unfilled interruptions are
never bridged. The exact separation of a fixed edge pair is affine, so its
integral over adjoining filled intervals equals the integral over their union.

Endpoint handling, active status, winding prefixes and crossing decisions stay
unchanged. Captured immutable edge IDs permit later integration after an edge
leaves active status. Generations reset pending state before each query; invalid
input, fallback, scratch growth and raw/prepared reuse cannot carry old work
into the next call. No shipping API, dependency or default backend changes.
See the [derivation](GAP-COALESCING-NUMERICS.md) and [code](GuardedDoubleSweep.Gaps.cs).

| Work across both directions | Baseline area | Coalesced gaps |
|---|---:|---:|
| Filled contributions | 62106 | 62106 |
| Actual integration attempts | 62106 | 17634 |
| Merged contributions | 0 | 44472 |
| Horizontal-difference calls | 125716 | 36772 |

Horizontal-difference calls fall **70.75%**. Bands (65932), crossing events
(1504), active visits (2230171), interval-X evaluations (25580) and scalar-order
counters remain unchanged. Additional bookkeeping is conservatively charged:
work counts rise from 2967382 to 3224424 under the same work limit. This counter
is a budget policy, not a CPU-operation count or a predicted speedup.

## Numerical guard established before timing

An unrestricted, uncommitted prototype first failed the relative certificate
on county/query `37027`/`3714`. Fourteen integrations became nine, but the area
enclosure half-width grew from about **2.0335e-8 to 1.1280e-7**, exceeding the
roughly 9.09e-8 budget. The failed path returned Winding fallback, not a valid
new certificate. That diagnostic half-width is not the production `LastErrorBound`.
The discovery records retain their working-tree/DLL hashes; they are not results
for the measured commit above.

Extending a trapezoid adjacent to an uncertain crossing can amplify interval
dependency over its larger span. The measured variant therefore leaves such
boundary pieces separate, using a point-valued-Y guard rather than a tuned
tolerance. The same pair then certifies with ten integrations. No certificate
budget was increased. An independent analytic regression covers long crossing
wedges around Y=2^40 with exact area 2^35, across ROI, storage and orientation
variants; it does not depend on NTS or this Census pair.

Integration boundaries and addition order change, so area/radius bits need not
match the unmerged method. In the frozen data, 255 pair area bit patterns change;
the largest baseline/variant difference is 2.86102294921875e-6 m². Certificate
radii become narrower for 244 candidates, wider for 52 and equal for 126. Both variants
certify all **422 geometry calls**, with zero fallback, using the original
absolute 0.25 and conservative relative 1e-10 budgets. This is an enclosure
preservation result, **not a demonstrated improvement in numerical accuracy**.
Original-input NTS budgets also pass; NTS is independent, not an exact oracle.

## Complete-query costs

The same 109 Census rings, 10588 vertices, 2156 directional pairs and 422
candidate pairs are retained. Each table visits 1078 pairs and runs 211 geometry
calls. Double inputs are unrounded; Clipper keeps scale-1e6 quantization.

| Direction | Method | Warm table, ms | Fresh queries, ms | Prepare + one, ms |
|---|---|---:|---:|---:|
| county-zones | Guarded-area | 18.6750 | 19.6839 | 20.9598 |
| county-zones | Guarded-gaps | 14.0885 | 15.1965 | 16.4069 |
| county-zones | Clipper64-reused-data | 12.7096 | 12.9088 | 12.8887 |
| county-zones | Winding-intersection-only | 9.6029 | 9.6344 | 9.7383 |
| district-zones | Guarded-area | 18.3536 | 19.8656 | 20.7753 |
| district-zones | Guarded-gaps | 13.7442 | 15.1834 | 16.1192 |
| district-zones | Clipper64-reused-data | 12.2993 | 12.8669 | 12.6425 |
| district-zones | Winding-intersection-only | 9.0952 | 9.1587 | 9.1876 |

Fresh-query scope prepares each incoming query once per complete table.
Preparation-plus-one charges new catalogues/comparator and Guarded/Clipper
workspace growth. Winding keeps its warmed thread-local workspace; this is not
cold-process startup. Parsing and I/O are excluded equally. Warm gap time is
still **1.11–1.12x Clipper** and **1.47–1.51x Winding**.

All six full-query cells require <=0.8 times Clipper and <=0.9 times Winding.
Both sweep methods fail every cell, giving **twelve failed gate cells**. The
[public evidence](../scanbeam-gaps-evidence.json) contains all 32 measurement rows,
process ranges, allocation medians and all gate ratios. Current and historical
baseline ranges are disjoint from the variant in all six cells; those observed
ranges are not confidence intervals.

| Method | County preparation, ms | District preparation, ms | Preparation B/op, either direction |
|---|---:|---:|---:|
| Guarded-area | 2.2012 | 2.2088 | 1557456 |
| Guarded-gaps | 2.2089 | 2.1844 | 1557456 |
| Clipper64-reused-data | 0.4072 | 0.4050 | 968888 |
| Winding-intersection-only | 0.1263 | 0.1252 | 10544 |

The immutable prepared payload is unchanged at **1524672 array-element bytes**.
The coalescer adds at most **84840 retained array-element bytes** on this workload,
excluding headers and all existing scratch. This is not total or peak memory.
Both variants allocate **0 B/op in every warm-table sample**. Fresh-query bytes
remain 568952 / 986024. Preparation-plus-one charges the extra workspace growth:
2248000 / 2224816 B for gaps versus 2107648 / 2089192 B for the baseline. Growth
allocation includes replaced arrays and headers, unlike the retained payload.
The new shared engine fields add 48 B per fresh engine relative to the old build.

## Historical sensitivity

Frozen `5bc86fb` was rerun in three fresh processes with its original matrix.
Old/new series followed **old1, new1, new2, old2, old3, new3**, each with **480
samples** and its own measured DLL. Samples are never pooled. Historical
`Guarded-area` is method C; it is method B in the new matrix, so method positions
differ. Separately built Winding DLL hashes also differ despite unchanged
shipping source; actual identities and environment are recorded in the manifest.

For competitor C, normalized old/gaps is `(old area / old C) / (new gaps / new C)`.
These are sensitivity observations, not corrections that remove drift or isolate
JIT effects. The old baseline warm medians are 19.08–19.25 ms.

| Direction | Scope | Old area/new area | Old area/gaps | Winding-normalized old/gaps | Clipper-normalized old/gaps |
|---|---|---:|---:|---:|---:|
| county-zones | warm-table | 1.031 | 1.366 | 1.358 | 1.379 |
| county-zones | warm-zones-fresh-queries | 1.033 | 1.338 | 1.335 | 1.332 |
| county-zones | prepare-plus-one | 1.025 | 1.309 | 1.309 | 1.321 |
| district-zones | warm-table | 1.040 | 1.388 | 1.404 | 1.374 |
| district-zones | warm-zones-fresh-queries | 1.025 | 1.341 | 1.353 | 1.348 |
| district-zones | prepare-plus-one | 1.026 | 1.323 | 1.334 | 1.330 |

The variant improves against both baselines, with larger apparent gains against
the historical one. The decision still uses the same-binary comparison and the
unchanged competitor gates; cross-binary ratios cannot identify the cause of
changes in the control itself. No profiles were collected for this stage.

## Validation, reproduction and decision

Normal and hardware-intrinsics-disabled runs passed **1274 gap controls**, **7034
snapshot controls** (2977 copied, 4057 direct), and **51422 exact-rational/analytic
geometry assertions**. Gap controls include persistent affine spans, fill changes
with unchanged adjacency, holes, crossings, retracing, both fill rules, shifts,
scales, invalid inputs and recovery. Every successful call satisfies
`contributions = integrations + merges`; all frozen certificates and unchanged
topology diagnostics were checked before timing.

An unchanged positive evidence copy passed. Ten corruptions were rejected:
row, order, median, output, binary, filter diagnostic, fallback, prepared payload,
certificate and gap counter. Rejected submissions retain their JSON bytes and
produce neither evidence nor summary files.

.NET 10.0.12; Microsoft Windows 10.0.26200; X64; Intel64 Family 6 Model 158
Stepping 12, GenuineIntel; 16 logical processors. Release,
`DOTNET_TieredCompilation=0`, method orders ABCD/CDAB/BDAC, three fresh processes,
five calibrated samples per row and 32 rows/process in each independent series.
Report medians of process medians and process ranges. No concurrent build,
profiler or other benchmark ran; ordinary OS activity is uncontrolled.

From the measured revision, restore locked and build Release, then use:

```text
dotnet <measured-dll> check-gap-coalescing artifacts/scanbeam-gaps-check
dotnet <measured-dll> benchmark-gap-coalescing artifacts/scanbeam-gaps <run 1..3> <revision>
dotnet <measured-dll> summarize-gap-coalescing artifacts/scanbeam-gaps
```

Use the old binary's `benchmark-area-arithmetic` / `summarize-area-arithmetic`
in a separate directory and preserve the interleaved process order. See the
[fixed protocol](GAP-COALESCING-PROTOCOL.md) and [evidence](../scanbeam-gaps-evidence.json)
for raw-file hashes, memory definitions and both series. Raw runs stay local.

Keep coalescing as a measured research variant with its flag off by default.
The structural work reduction helps this sweep substantially, but the result
is specific to this hardware/runtime and dataset and still does not meet the
requirements for replacing shipping Winding. No C++ or SIMD benefit is implied.

Local archive: `artifacts/scanbeam-gaps-4b8a131.zip` (338179 bytes, 34 members).
SHA-256 `8511bf2bf5b6e7a4d02f135ca631d0ba802f8a830a3bd93c405632cbd286ff56`.
The archive is local, not publicly hosted.
