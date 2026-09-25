# Direct prepared-edge access results

Measured source: [`bc6184ce4b57598ca39999ff50ff279d62db8b2f`](https://github.com/bgtnt/polylinekit/commit/bc6184ce4b57598ca39999ff50ff279d62db8b2f).
Historical control: [`57c519cba31c039ef3dd7f254665d8c26a8716e2`](https://github.com/bgtnt/polylinekit/commit/57c519cba31c039ef3dd7f254665d8c26a8716e2).

Removing the per-pair copies of prepared edge and scalar-bound arrays does
**not show a performance advantage**. Direct access takes **19.87–20.03 ms**
per warm table, versus **19.55 ms** for the copied variant in the same binary.
Its median is **1.63–2.95% slower in all six full-query cells**. Process ranges
overlap; these descriptive medians do not establish statistical significance.
**All six direct competitor gates fail.** Keep copied preparation as the
preferred experimental variant; neither variant justifies replacing Winding.

## What was isolated

The optional `directPreparedEdges` flag is off by default. In prepared queries,
it borrows the two snapshots' private immutable edge and scalar-bound arrays.
Safe inlined `ref readonly` accessors map the existing global edge ID using the
first path's nonhorizontal edge count. Winding roles follow that split: a
standalone snapshot's stored `Loop = 0` cannot identify its role in a pair.
Borrowed references are cleared in `finally` on every return or exception,
and reset also clears them before a subsequent raw or prepared query.

Snapshot preparation, interval slopes and scalar bounds, endpoint merging and
order, active status, crossing construction, area arithmetic, certificates,
work budgets and fallback are unchanged. No pair-result cache, unsafe code,
new dependency or shipping-library API is introduced. The raw-input overload
and the copied prepared control remain available. Engine instances remain
non-thread-safe and non-reentrant; snapshots may be shared across engines.

This isolates removal of edge-record and scalar-bound copying. Endpoint and active
status scratch still exists. The original `EnsureVertices` allocation policy
is deliberately retained, including the copied geometry buffers unused by the
direct variant. Shared accessors also change code generation for the copied
control, which is why the historical binary is measured separately below.

## Complete-query measurements

Each directional table visits 1078 pairs, with 211 reaching geometry. The
frozen inputs contain 109 original Census rings and 10588 vertices. Both
variants use the same outer candidate mask and unrounded double coordinates;
Clipper keeps scale-1e6 quantization. There is no parsing or file I/O in timing.

| Method | County warm, ms | District warm, ms | County fresh queries, ms | District fresh queries, ms |
|---|---:|---:|---:|---:|
| Guarded-prepared | 19.555 | 19.553 | 20.525 | 20.778 |
| Guarded-direct | 20.029 | 19.872 | 21.048 | 21.347 |
| Clipper64-reused-data | 12.613 | 12.384 | 13.042 | 12.779 |
| Winding-intersection-only | 9.711 | 9.087 | 9.633 | 9.126 |

Fresh-query scope prepares each incoming query once for the complete table.
Preparation-plus-one builds both catalogues and a fresh comparator, charging
Guarded/Clipper workspace growth. Winding keeps its warmed thread-local
workspace; these measurements do not represent cold-process startup.

Warm direct time is about **1.59–1.60x Clipper** and **2.06–2.19x Winding**.
The same-binary copied variant is faster than direct in all measured query
scopes. The full timing ranges and all twelve copied/direct gates appear below.

## Historical-binary sensitivity

The original copied binary was rerun in three fresh processes rather than
using its earlier published timings. Old and new series followed the fixed
sequence **old1, new1, new2, old2, old3, new3**, without overlapping processes.
Each series has **480 samples** and its own four-method matrix and fixed
method orders. The old matrix compares filtered/copied modes; the new matrix
compares copied/direct modes. Therefore method positions differ between the
series. Each is summarized with its own frozen DLL; samples are never pooled.
Shipping-library source is unchanged between the two revisions, but the
separate builds do not have identical Winding DLL hashes. The manifest records
the actual binaries and runtime environment for each series; unchanged source
must not be interpreted as byte-identical competitor binaries.

The historical copied warm medians are **20.34–20.77 ms**, versus 19.55 ms for
copied and 19.87–20.03 ms for direct in the new binary. That makes direct look
faster than the historical control, while it loses to its contemporary control.
The earlier binary's result alone would therefore give an incomplete picture.

For a competitor C, the normalized ratio below is
`(old copied / old C) / (new copied / new C)`. Larger than one describes a lower
relative cost for new copied. These are sensitivity ratios, not corrections
that remove environmental drift or isolate an accessor/JIT effect. Ordinary
OS activity and differing method positions remain possible influences.

| Direction | Scope | Old copied/new copied | Winding-normalized | Clipper-normalized | Old copied/new direct |
|---|---|---:|---:|---:|---:|
| county-zones | warm-table | 1.062 | 1.057 | 1.076 | 1.037 |
| county-zones | warm-zones-fresh-queries | 1.058 | 1.045 | 1.075 | 1.031 |
| county-zones | prepare-plus-one | 1.043 | 1.040 | 1.052 | 1.014 |
| district-zones | warm-table | 1.040 | 1.041 | 1.048 | 1.023 |
| district-zones | warm-zones-fresh-queries | 1.055 | 1.051 | 1.059 | 1.026 |
| district-zones | prepare-plus-one | 1.038 | 1.038 | 1.041 | 1.020 |

Neither the raw nor competitor-normalized cross-binary ratios establish why
the copied control changed. The direct-versus-copied decision uses the new
same-binary comparison and the unchanged complete-query competitor gates.
No causal speedup or regression is attributed to inlining or a particular
accessor from these measurements.

## Preparation and memory

Both new variants prepare the same immutable snapshots: about **2.19–2.21 ms**
and **1557408 B** for both catalogues. Their retained payload is unchanged at
**1524672 array-element bytes**. This includes cloned coordinates, edges,
scalar bounds and sorted endpoints; it excludes object/array headers, shared
source arrays, mutable engine scratch and peak/transient allocations.

Both variants allocate **0 B/op in every warm-table sample**. Fresh-query
allocations match at 568952 B for county zones and 986024 B for district zones.
Fresh preparation plus one table allocates 2107600 / 2089144 B. Added engine
fields account for **32 B more per fresh engine/session than the old binary**,
visible in both new variants; removing array copies does not save retained
memory under the deliberately unchanged workspace allocation policy.

## Correctness and compatibility

Copied and direct certify all **422 geometry calls**, with no fallback. All
**2156 pair areas, coverage values and certificate radii match bit for bit**,
and every recorded diagnostic agrees. Both have 65932 bands, 1504 constructed
crossing events, 2230171 active-status visits, 25580 interval-X evaluations,
540765 scalar attempts, 527975 accepted orders and 12790 interval continuations.

The maximum error against original-input NTS is 2.6226043701171875e-6 m² for
intersection and 2.7755575615628914e-15 for coverage; the largest certified
radius is 0.00032496452331542974 m². All methods satisfy the fixed 1 m² and
1e-8 coverage budgets. NTS is an independent comparison, not the exact oracle.

The direct suite passed **4057 snapshot/reuse/input/diagnostic controls**,
including all eight ROI/cache/filter combinations, both fill rules, swapped
roles, unequal edge/vertex counts, same-object pairs, caller mutation,
raw/prepared alternation, scratch growth and recovery after fallback or
exceptions. The exact-rational/analytic area suite passed **51422 assertions**
in direct mode across four ROI/cache combinations. Both suites passed normally
and with hardware intrinsics disabled. Historical command compatibility
preserves **8624 method outputs and diagnostics**, including value/radius bits.
Prepared payload and metadata remain identical to the historical binary.

## Profile observations

Separate 20-second warm county profiles used dotnet-trace 10.0.745401 and the
new measured binary with tiering disabled. They ran after all timing processes.
Samples under `TraverseReal` include four initial validation/warmup calls and
exclude catalogue preparation outside that traversal.

| Inclusive sampled frame share | Copied prepared | Direct prepared |
|---|---:|---:|
| CopyEdgesTo | 21.43% | not observed |
| SweepCommonY | 78.36% | 95.97% |
| ProcessBand | 64.16% | 75.49% |
| AccumulateGap | 34.50% | 36.65% |
| HorizontalDifference | 22.51% | 23.73% |
| CompareAtEndpoint | 11.60% | 17.68% |
| MergeEndpoints | 0.06% | 3.81% |

Inclusive shares overlap and do not add to 100%. They are sampled managed
thread-time shares, not exact CPU percentages or per-routine timing. JIT
inlining affects attribution; missing frames do not establish zero cost.
The unmodified merge routine's very different attributed shares illustrate
why frame percentages cannot be treated as isolated operation costs.

Source inspection confirms removal of the bulk geometry-array copy in direct
mode, but the complete-query measurements show no corresponding net benefit.
The larger attributed sweep/ordering shares do not identify a causal slowdown.
No benchmark ratio is derived from elapsed profile-loop time or iteration
counts, and these profiles do not establish a SIMD or C++ advantage.

## Decision

Retain direct access as a reproducible negative experiment, with its flag off.
Do not select it over the copied prepared mode, and do not adopt either sweep
variant in the shipping library on this evidence. Immutable preparation remains
a useful optimization of this prototype, as shown by the previous experiment;
the extra indirection introduced here does not demonstrate additional value.
Any further structural change requires its own fixed scope and full-query
measurements rather than a projected saving from inclusive profile shares.

## Full measurements and gates

| Direction | Method | Scope | ms | Process range ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | Clipper64-reused-data | prepare | 0.4026 | 0.3961–0.4044 | 968888 |
| county-zones | Clipper64-reused-data | prepare-plus-one | 12.8805 | 12.8114–13.3288 | 1626168 |
| county-zones | Clipper64-reused-data | warm-table | 12.6134 | 12.4352–12.7203 | 625088 |
| county-zones | Clipper64-reused-data | warm-zones-fresh-queries | 13.0419 | 12.7296–13.0483 | 975040 |
| county-zones | Guarded-direct | prepare | 2.1862 | 2.1818–2.2122 | 1557408 |
| county-zones | Guarded-direct | prepare-plus-one | 22.4368 | 22.1673–22.9606 | 2107600 |
| county-zones | Guarded-direct | warm-table | 20.0285 | 19.8219–21.0276 | 0 |
| county-zones | Guarded-direct | warm-zones-fresh-queries | 21.0482 | 20.9809–21.5552 | 568952 |
| county-zones | Guarded-prepared | prepare | 2.2067 | 2.2016–2.2311 | 1557408 |
| county-zones | Guarded-prepared | prepare-plus-one | 21.7948 | 21.6842–22.9416 | 2107600 |
| county-zones | Guarded-prepared | warm-table | 19.5548 | 19.5386–20.4755 | 0 |
| county-zones | Guarded-prepared | warm-zones-fresh-queries | 20.5251 | 20.4780–21.0628 | 568952 |
| county-zones | Winding-intersection-only | prepare | 0.1279 | 0.1270–0.1284 | 10544 |
| county-zones | Winding-intersection-only | prepare-plus-one | 9.8082 | 9.7507–9.8356 | 10480 |
| county-zones | Winding-intersection-only | warm-table | 9.7110 | 9.6894–9.7126 | 0 |
| county-zones | Winding-intersection-only | warm-zones-fresh-queries | 9.6330 | 9.6077–9.6362 | 1120 |
| district-zones | Clipper64-reused-data | prepare | 0.4005 | 0.3980–0.4007 | 968888 |
| district-zones | Clipper64-reused-data | prepare-plus-one | 12.6194 | 12.6116–12.6729 | 1624352 |
| district-zones | Clipper64-reused-data | warm-table | 12.3845 | 12.2907–12.3962 | 625088 |
| district-zones | Clipper64-reused-data | warm-zones-fresh-queries | 12.7785 | 12.7030–12.8431 | 1241472 |
| district-zones | Guarded-direct | prepare | 2.2010 | 2.1765–2.2037 | 1557408 |
| district-zones | Guarded-direct | prepare-plus-one | 22.1632 | 21.8705–22.8605 | 2089144 |
| district-zones | Guarded-direct | warm-table | 19.8717 | 19.5570–20.7986 | 0 |
| district-zones | Guarded-direct | warm-zones-fresh-queries | 21.3472 | 21.2609–22.3555 | 986024 |
| district-zones | Guarded-prepared | prepare | 2.2046 | 2.2015–2.2089 | 1557408 |
| district-zones | Guarded-prepared | prepare-plus-one | 21.7673 | 21.6603–22.6318 | 2089144 |
| district-zones | Guarded-prepared | warm-table | 19.5526 | 19.1779–20.2900 | 0 |
| district-zones | Guarded-prepared | warm-zones-fresh-queries | 20.7777 | 20.4196–21.6790 | 986024 |
| district-zones | Winding-intersection-only | prepare | 0.1249 | 0.1247–0.1264 | 10544 |
| district-zones | Winding-intersection-only | prepare-plus-one | 9.2647 | 9.1802–9.3486 | 10480 |
| district-zones | Winding-intersection-only | warm-table | 9.0866 | 8.9930–9.1533 | 0 |
| district-zones | Winding-intersection-only | warm-zones-fresh-queries | 9.1260 | 9.1199–9.2409 | 7384 |

Every full-query cell requires time <=0.8 times Clipper and <=0.9 times Winding.
Ratios divide the comparison time by variant time; larger is better. Both
variants fail all six cells, giving twelve failed copied/direct cells in total.

| Direction | Variant | Scope | Copied/variant | Clipper/variant | Winding/variant | Pass |
|---|---|---|---:|---:|---:|---|
| county-zones | Guarded-direct | prepare-plus-one | 0.971 | 0.574 | 0.437 | False |
| county-zones | Guarded-direct | warm-table | 0.976 | 0.630 | 0.485 | False |
| county-zones | Guarded-direct | warm-zones-fresh-queries | 0.975 | 0.620 | 0.458 | False |
| county-zones | Guarded-prepared | prepare-plus-one | 1.000 | 0.591 | 0.450 | False |
| county-zones | Guarded-prepared | warm-table | 1.000 | 0.645 | 0.497 | False |
| county-zones | Guarded-prepared | warm-zones-fresh-queries | 1.000 | 0.635 | 0.469 | False |
| district-zones | Guarded-direct | prepare-plus-one | 0.982 | 0.569 | 0.418 | False |
| district-zones | Guarded-direct | warm-table | 0.984 | 0.623 | 0.457 | False |
| district-zones | Guarded-direct | warm-zones-fresh-queries | 0.973 | 0.599 | 0.428 | False |
| district-zones | Guarded-prepared | prepare-plus-one | 1.000 | 0.580 | 0.426 | False |
| district-zones | Guarded-prepared | warm-table | 1.000 | 0.633 | 0.465 | False |
| district-zones | Guarded-prepared | warm-zones-fresh-queries | 1.000 | 0.615 | 0.439 | False |

## Reproduction and evidence

.NET 10.0.12; Microsoft Windows 10.0.26200; X64;
Intel64 Family 6 Model 158 Stepping 12, GenuineIntel; 16 logical processors.
Release build with `DOTNET_TieredCompilation=0`. Each independent series uses
three fresh processes, five calibrated samples per row and 32 rows per process.
Reported values are medians of process medians, with min/max process medians.
Ranges are not confidence intervals. No build, profiler or second benchmark
ran concurrently with timing.

The Release build and checks passed. The evidence validator accepted an
unchanged positive copy and rejected all eight corruptions: missing row,
method order, median, output, binary hash, filter diagnostic, fallback record
and prepared payload. Rejection preserved submitted JSON bytes and created
neither an evidence file nor a summary.

See the [fixed protocol and commands](DIRECT-SWEEP-PROTOCOL.md),
[direct access implementation](GuardedDoubleSweep.Direct.cs),
[prepared queries](GuardedDoubleSweep.Prepared.cs),
[controls](PreparedSweepChecks.cs) and [public evidence](../scanbeam-direct-evidence.json).
The manifest records both measured revisions/binary identities, independent
series, sensitivity ratios, unchanged payload summary and historical reference,
compatibility, profiles and
raw-record hashes. Reproduce from the frozen source and inputs; local raw
records and traces remain ignored artifacts.

For the new frozen DLL, run `check-direct-sweep <directory>`, then
`benchmark-direct-sweep <directory> <run 1..3> <revision>` and
`summarize-direct-sweep <directory>`. For the historical DLL, use
`benchmark-prepared-sweep` and `summarize-prepared-sweep` in a separate directory.
Keep the process order above and use each DLL to summarize only its own runs.

Local archive: `artifacts/scanbeam-direct-bc6184c.zip` (995609 bytes, 39 members).
SHA-256 `f81ab6183df151efd263d0a9ac983100f0aea410c68eaf31678f048f697f743a`.
The archive is local, not a public download.
