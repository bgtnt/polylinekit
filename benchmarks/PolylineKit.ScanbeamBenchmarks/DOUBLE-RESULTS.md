# Guarded double intersection sweep

Measured source: [`a3eb54e7167bf17d8e989aaec1815b4507fef37d`](https://github.com/bgtnt/polylinekit/commit/a3eb54e7167bf17d8e989aaec1815b4507fef37d).

Predeclared performance gate: **FAIL**.
The guarded sweep takes about 101 ms per warm table, versus 12.6–12.9 ms for
Clipper and 9.2–9.7 ms for current Winding: roughly 8x and 10–11x slower.
All four complete-query gates fail. This candidate should not enter the library.

This is an experimental executable. The shipping Winding implementation and
public APIs are unchanged. Results describe this implementation and frozen workload,
not all double-coordinate scanline algorithms.

## What was implemented

The sweep preserves original binary64 coordinates and maintains its active edge
membership/order across endpoint levels. A sorted copy still enumerates inversions
per band; this is not a neighbor-event priority queue. It integrates filled gaps
directly, without constructing output polygons. Outward-rounded interval arithmetic
certifies decisions and encloses area values. Uncertified cases discard their
provisional result and call the existing Winding intersection method.

A certified result has error radius <=min(0.25,1e-10*abs(result)) in squared input
units; a zero result requires the exact enclosure [0,0]. Fallback results carry
only the existing Winding numerical contract, not a new interval guarantee.

## Original-input validation and fallback

All 109 original valid Census rings (10588 vertices) are retained. Coordinates
are neither rounded nor translated by the harness. All 2156 directional pairs
are checked against original-input NTS OverlayNG, with the existing 1 m² and
1e-8 coverage-fraction limits. NTS is an independent implementation, not an exact
oracle. Clipper retains its documented scale-1e6 quantization contract.

There are 422 geometry candidates across both directions:
**422 certified** (124 zero-area) and
**0 fallback** (0.00%).
AABB rejections are outside that fraction. Failed-sweep work and the complete
fallback call are both inside the measured operation.

| Comparison against original NTS | Maximum absolute difference |
|---|---:|
| Winding intersection, m² | 5.39477332495e-06 |
| Guarded intersection, m² | 2.62260437012e-06 |
| Clipper intersection, m² | 0.0229949951172 |
| Coverage fraction, all three methods | 2.8686941711e-11 |
| Guarded minus Winding intersection, m² | 5.48362731934e-06 |
| Largest certified error radius, m² | 0.000324964523315 |

Untimed diagnostic work, including abandoned sweeps:

| Outcome/reason | Calls | Bands | Events | Active-edge visits | Peak active |
|---|---:|---:|---:|---:|---:|
| certified-positive | 298 | 148430 | 1504 | 3936478 | 22 |
| certified-zero | 124 | 46084 | 0 | 1076174 | 14 |

The [public evidence](../scanbeam-double-evidence.json) contains every candidate
outcome and all aggregate measurements. Complete pair validation and raw samples
are reproducible from the frozen public inputs.

## Time and managed allocations

One operation is a full 1078-pair population traversal, or preparation as labelled.
Values are medians of three process medians, with their minimum–maximum range.
Allocation is managed bytes on the measured thread, not retained or peak memory.

| Direction | Method | Scope | ms | Range ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | Clipper64-reused-data | prepare | 0.4275 | 0.4263–0.4471 | 968888 |
| county-zones | Clipper64-reused-data | prepare-plus-one | 13.0116 | 12.8698–13.3127 | 1626168 |
| county-zones | Clipper64-reused-data | warm-table | 12.9284 | 12.5420–13.1671 | 625088 |
| county-zones | GuardedDouble-intersection | prepare | 0.1283 | 0.1281–0.1286 | 10808 |
| county-zones | GuardedDouble-intersection | prepare-plus-one | 101.4492 | 100.7414–101.7575 | 390368 |
| county-zones | GuardedDouble-intersection | warm-table | 101.5624 | 100.7516–101.9486 | 0 |
| county-zones | Winding-intersection-only | prepare | 0.1278 | 0.1273–0.1286 | 10544 |
| county-zones | Winding-intersection-only | prepare-plus-one | 9.9026 | 9.8672–9.9458 | 10480 |
| county-zones | Winding-intersection-only | warm-table | 9.7459 | 9.7266–9.7549 | 0 |
| district-zones | Clipper64-reused-data | prepare | 0.4302 | 0.4291–0.4353 | 968888 |
| district-zones | Clipper64-reused-data | prepare-plus-one | 12.7508 | 12.6605–12.7767 | 1624352 |
| district-zones | Clipper64-reused-data | warm-table | 12.6039 | 12.3834–12.7720 | 625088 |
| district-zones | GuardedDouble-intersection | prepare | 0.1277 | 0.1272–0.1285 | 10808 |
| district-zones | GuardedDouble-intersection | prepare-plus-one | 101.9239 | 101.0967–101.9705 | 377720 |
| district-zones | GuardedDouble-intersection | warm-table | 101.4329 | 100.7981–102.2929 | 0 |
| district-zones | Winding-intersection-only | prepare | 0.1296 | 0.1274–0.1303 | 10544 |
| district-zones | Winding-intersection-only | prepare-plus-one | 9.3597 | 9.3572–9.3809 | 10480 |
| district-zones | Winding-intersection-only | warm-table | 9.2268 | 9.1865–9.4276 | 0 |

Each cell requires at least 20% less time than Clipper and 10% less than
current Winding. Preparation-only rows cannot replace a failed full-query cell.

| Direction | Scope | Clipper / guarded | Winding / guarded | Pass |
|---|---|---:|---:|---|
| county-zones | prepare-plus-one | 0.1283 | 0.0976 | False |
| county-zones | warm-table | 0.1273 | 0.0960 | False |
| district-zones | prepare-plus-one | 0.1251 | 0.0918 | False |
| district-zones | warm-table | 0.1243 | 0.0910 | False |

## Interpretation and next ablation

There are no fallbacks and no warm allocations on this workload, so neither
explains the loss. Certification runs through general interval operations; the
implementation also still visits active edges and sorts a copy at each endpoint
band. Both are plausible costs. No sampling profile was collected, so these
counters cannot assign a CPU percentage to either cause.

An independent untimed count tests the potential of restricting processing to
the overlapping Y bounds. For each pair, it sorts the distinct original endpoint
levels and counts the consecutive bands inside that common interval. Both bounds
are existing endpoint levels. The current counts match `BandCount` for every
candidate. Per direction, across all 211 candidates:

| Subset | Candidates | Current bands | Bands within common Y bounds |
|---|---:|---:|---:|
| All | 211 | 97257 | 32966 |
| Zero-area | 62 | 23042 | 5194 |
| Positive-area | 149 | 74215 | 27772 |

This retains 33.90% of endpoint bands. It is **not a measured 66.10% CPU saving**.
A correct implementation must initialize all edges spanning the lower bound,
certify their order immediately above it and establish both winding prefixes.
The initial status has 4–14 edges (median 4) on this set. Input validation,
preparation, initial sorting and arithmetic costs remain. X-side edges cannot
be discarded without retaining their winding contributions.

The next bounded experiment should profile the complete warm workload, then
measure common-Y restriction and caching each active edge's top-level X enclosure
as separate ablations. A cheap scalar filter may replace common interval work
only with a derived error bound and the same exact controls. None of these
changes is implemented or credited with a speedup here. SIMD, unsafe code or a
C++ port have not been measured by this experiment.

## Verification performed

The Release solution build passed with no warnings or errors. The independent
double checks passed 5659 assertions, exercising 757 certified calls and 206
whole-call fallbacks, both normally and with hardware intrinsics disabled.
Exact rational convex clipping verifies the certificate against the actual
dyadic input, before rounding an oracle value. Analytic degeneracies, exceptions,
input preservation and state-reset checks are included. These tests are not a
formal proof of all inputs.

Existing integer controls, wider-coordinate checks and rounded real-contour
validation also passed. The evidence verifier rejected six corrupt copies:
a missing row, changed median, output, binary hash, diagnostic and fallback record.
Each rejection preserved the submitted validation file. The 270 samples,
18 aggregates and four failed gates were independently audited.

## Reproduction

.NET 10.0.12; Microsoft Windows 10.0.26200; X64;
Intel64 Family 6 Model 158 Stepping 12, GenuineIntel; 16 logical processors.
Tiering was disabled. Three fresh processes ran sequentially with five calibrated
batches per row: 18 rows/process and 270 samples. No concurrent build or second
benchmark ran. Ordinary OS activity was uncontrolled; process ranges are descriptive,
not confidence intervals. The measured binaries and all input hashes are recorded.

At the recorded revision, restore locked and build Release. Set
`DOTNET_TieredCompilation=0`; run `benchmark-double artifacts/scanbeam-double <run> <revision>`
for run 1, 2 and 3, then `summarize-double artifacts/scanbeam-double` using the
same measured DLL. `check-double artifacts/scanbeam-double-check` runs untimed
exact-rational/analytic controls and the original-input comparisons. See the
[full protocol](DOUBLE-PROTOCOL.md) and [runner instructions](README.md).

Local raw archive: `artifacts/scanbeam-double-a3eb54e.zip` (69902 bytes),
SHA-256 `5b6e782934c2ff802633a58c0c07e6781ee7fe4a9d54ca6daf4947edb6da9f6a`. Every archived member was verified against its source.
It is local, not a public download. Public source contains frozen inputs,
reproduction code, this report and the compact evidence manifest.
