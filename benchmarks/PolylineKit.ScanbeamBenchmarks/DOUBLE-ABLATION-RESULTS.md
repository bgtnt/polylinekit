# Double sweep: common-Y and cached-X results

A subsequent [scalar order filter](SCALAR-FILTER-RESULTS.md) saves about 20% against
this optimized sweep in a contemporary comparison. It still fails the competitor
gates. The source and historical measurements below remain unchanged.

Measured source: [`ab1c6bae6d0bcce7f4a757d9cf156f5539403a68`](https://github.com/bgtnt/polylinekit/commit/ab1c6bae6d0bcce7f4a757d9cf156f5539403a68).

The combined changes reduce full-query time by about **2.0–2.1x** relative to
the contemporary guarded baseline, from 102–103 ms to about 50 ms per table.
**Every variant still fails all four integration gates.** The combined sweep
takes about 3.8x Clipper time and 5.1–5.4x current Winding time on warm tables.
It remains a benchmark experiment; no shipping API or backend is changed.

## What changed

Common-Y restriction initializes every edge spanning the lower shared bound,
then processes only endpoint bands within the overlapping Y bounds. It retains
all winding contributions and validates both complete input arrays. Initial
status construction and all edge preparation are included in every call.

The independent lazy cache stores successful requested XAt interval values by
edge ID, exact level bits and call generation. It serves both insertion and
top-order comparisons. It avoids eager evaluations, preserves interval bits,
and does not change relative-coordinate area arithmetic. Storage is allocated
only when caching is enabled. Both flags default off in previous commands.

## Complete-query performance

One warm operation traverses all 1078 pairs in the indicated direction.
Values are medians of three independent process medians. Fresh-session results
and the full ranges are retained below; prepare-only speed is not an acceptance gate.

| Method | County zones, ms | District zones, ms | Warm B/op |
|---|---:|---:|---:|
| Guarded-baseline | 102.740 | 102.323 | 0 |
| Guarded-common-y | 60.885 | 60.614 | 0 |
| Guarded-cache | 79.757 | 79.586 | 0 |
| Guarded-combined | 49.687 | 49.827 | 0 |
| Clipper64-reused-data | 13.214 | 12.955 | 625088 |
| Winding-intersection-only | 9.810 | 9.176 | 0 |

Y restriction alone saves about 41% of warm time; caching alone saves about
22%. Combined, they save about 51%. These measured savings are not inferred
from operation counts, and the individual gains must not be added together.
Fresh preparation plus one table shows the same conclusion.

All guarded warm samples have zero managed allocations. Cache storage raises
fresh-session allocations: combined calls use 460776 B (county zones) or
445704 B (district zones), versus 390416/377768 B for the uncached baseline.
These are allocations on the measured thread, not retained or peak memory.

## Work, numerical checks and fallback

All four guarded variants certify all 422 geometry candidates, with no fallback.
All 2156 directional pairs satisfy the unchanged original-input NTS budgets:
1 m² intersection and 1e-8 absolute coverage fraction. NTS remains an independent
implementation, not an exact oracle; Clipper retains scale-1e6 quantization.

| Variant | Bands | Crossings | Active visits | Actual X evaluations | Cache hits |
|---|---:|---:|---:|---:|---:|
| Guarded-baseline | 194514 | 1504 | 5012652 | 2419968 | 0 |
| Guarded-common-y | 65932 | 1504 | 2230171 | 1081530 | 0 |
| Guarded-cache | 194514 | 1504 | 5012652 | 1171704 | 1248264 |
| Guarded-combined | 65932 | 1504 | 2230171 | 495764 | 585766 |

The band reduction matches the preceding untimed prediction: 194514 to 65932
across both directions. Actual X evaluations fall from 2419968 to 495764. Requests
equal evaluations plus cache hits, exactly matching each uncached counterpart.
Reduced work counts do not imply a proportional runtime reduction.

All 2156 pair results and error radii are identical across the four variants
on this frozen set. The contemporary baseline also preserves the historical
`a3eb54e` result bits, certificates and logical diagnostics for every pair. The
maximum guarded difference from NTS is 2.62260437012e-6 m², and the largest
certified radius is 0.000324964523315 m². The certificate budget is unchanged.

The independent controls passed 37984 assertions across all four configurations,
both normally and with hardware intrinsics disabled. They verify exact rational
area enclosure before rounding oracle results, cache bit identity and diagnostics,
fallback/exception parity, boundary crossings, spanning edges, skipped external
degeneracies, caller-input preservation and stale-cache state. Common-Y variants
certify 851 and fall back 234 times; unrestricted variants certify 823 and fall back 262
times in those controls. Avoiding outside-range ambiguity can legitimately change
fallback frequency; no fallback result receives the new interval guarantee.
These finite tests do not constitute a general formal proof.

## Sampled profile

Separate 20-second loops repeated the complete warm county table using the same
measured binary. `dotnet-trace 10.0.745401` used `dotnet-sampled-thread-time`;
tiering was disabled. The filter retains frames under `TraverseReal`, including
four initial validation/warmup calls. Their first-use work is therefore included.
Preparation outside traversal is excluded. Profiles never ran during benchmarks.

| Inclusive frame share | Baseline | Combined |
|---|---:|---:|
| ProcessBand | 61.47% | 57.11% |
| CompareAtEndpoint | 53.87% | 30.52% |
| XAt | 32.27% | 14.14% |
| Insert | 19.38% | 7.28% |
| Endpoint sort | 10.55% | 22.27% |
| AccumulateGap | 8.62% | 16.73% |
| AppendEdges | 4.04% | 8.66% |

Inclusive shares overlap; **do not sum these rows**. They are sampled managed
thread-time shares, not exact CPU percentages. JIT inlining changes attribution.
The documented summary script removes synthetic CPU_TIME/UNMANAGED_CODE_TIME
leaves only for nearest-managed-frame attribution. A rising relative share
does not by itself mean that an operation became slower.

The profile supports eliminating repeated ordering evaluations. After the two
changes, endpoint sorting, comparisons and gap accumulation remain substantial.
This is evidence about the implementation, not a measured language limitation.

## Decision

Keep the experiment outside the library. The optimizations are useful but leave
a large gap to both competitors. No SIMD, unsafe-code or C++ gain is claimed.
Further work should be a separate bounded experiment: reduce comparison and
interval-evaluation cost with a derived scalar error filter, or reuse sorted
immutable edge preparation under an explicitly measured preparation contract.
Each would need the same exact controls and full-query gates. Neither is
implemented or credited with a speedup here.

## Full measurements and gates

| Direction | Method | Scope | ms | Process range ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | Clipper64-reused-data | prepare | 0.4361 | 0.4351–0.4429 | 968888 |
| county-zones | Clipper64-reused-data | prepare-plus-one | 13.3724 | 13.3128–13.4232 | 1626168 |
| county-zones | Clipper64-reused-data | warm-table | 13.2144 | 13.0223–13.3875 | 625088 |
| county-zones | Guarded-baseline | prepare | 0.1297 | 0.1291–0.1339 | 10856 |
| county-zones | Guarded-baseline | prepare-plus-one | 103.0776 | 102.8138–106.5876 | 390416 |
| county-zones | Guarded-baseline | warm-table | 102.7398 | 102.6092–107.2994 | 0 |
| county-zones | Guarded-cache | prepare | 0.1298 | 0.1289–0.1301 | 10856 |
| county-zones | Guarded-cache | prepare-plus-one | 79.5790 | 78.8604–81.2245 | 460776 |
| county-zones | Guarded-cache | warm-table | 79.7570 | 78.9641–81.3509 | 0 |
| county-zones | Guarded-combined | prepare | 0.1295 | 0.1294–0.1302 | 10856 |
| county-zones | Guarded-combined | prepare-plus-one | 50.0133 | 49.8007–51.4138 | 460776 |
| county-zones | Guarded-combined | warm-table | 49.6873 | 49.2879–49.9741 | 0 |
| county-zones | Guarded-common-y | prepare | 0.1299 | 0.1297–0.1301 | 10856 |
| county-zones | Guarded-common-y | prepare-plus-one | 61.3145 | 61.0040–61.4783 | 390416 |
| county-zones | Guarded-common-y | warm-table | 60.8848 | 60.5956–60.9786 | 0 |
| county-zones | Winding-intersection-only | prepare | 0.1330 | 0.1293–0.1400 | 10544 |
| county-zones | Winding-intersection-only | prepare-plus-one | 9.9113 | 9.8545–10.0335 | 10480 |
| county-zones | Winding-intersection-only | warm-table | 9.8101 | 9.7186–9.8591 | 0 |
| district-zones | Clipper64-reused-data | prepare | 0.4429 | 0.4394–0.4551 | 968888 |
| district-zones | Clipper64-reused-data | prepare-plus-one | 13.4204 | 13.0974–14.4764 | 1624352 |
| district-zones | Clipper64-reused-data | warm-table | 12.9550 | 12.7977–13.0200 | 625088 |
| district-zones | Guarded-baseline | prepare | 0.1303 | 0.1297–0.1331 | 10856 |
| district-zones | Guarded-baseline | prepare-plus-one | 102.6153 | 102.4847–106.5958 | 377768 |
| district-zones | Guarded-baseline | warm-table | 102.3226 | 102.2365–106.5103 | 0 |
| district-zones | Guarded-cache | prepare | 0.1292 | 0.1292–0.1351 | 10856 |
| district-zones | Guarded-cache | prepare-plus-one | 79.6967 | 78.6926–81.7724 | 445704 |
| district-zones | Guarded-cache | warm-table | 79.5860 | 79.0746–81.6505 | 0 |
| district-zones | Guarded-combined | prepare | 0.1299 | 0.1298–0.1325 | 10856 |
| district-zones | Guarded-combined | prepare-plus-one | 50.7516 | 49.8825–52.2265 | 445704 |
| district-zones | Guarded-combined | warm-table | 49.8273 | 49.7530–49.9268 | 0 |
| district-zones | Guarded-common-y | prepare | 0.1306 | 0.1288–0.1344 | 10856 |
| district-zones | Guarded-common-y | prepare-plus-one | 61.1539 | 60.7614–61.3771 | 377768 |
| district-zones | Guarded-common-y | warm-table | 60.6136 | 60.5772–61.1953 | 0 |
| district-zones | Winding-intersection-only | prepare | 0.1295 | 0.1286–0.1301 | 10544 |
| district-zones | Winding-intersection-only | prepare-plus-one | 9.3643 | 9.3366–9.3859 | 10480 |
| district-zones | Winding-intersection-only | warm-table | 9.1756 | 9.1644–9.2046 | 0 |

Each full-query cell requires time <=0.8 times Clipper and <=0.9 times Winding.
Ratios below are competitor/baseline time divided by variant time; larger is better.

| Direction | Variant | Scope | Baseline/variant | Clipper/variant | Winding/variant | Pass |
|---|---|---|---:|---:|---:|---|
| county-zones | Guarded-baseline | prepare-plus-one | 1.000 | 0.130 | 0.096 | False |
| county-zones | Guarded-baseline | warm-table | 1.000 | 0.129 | 0.095 | False |
| county-zones | Guarded-cache | prepare-plus-one | 1.295 | 0.168 | 0.125 | False |
| county-zones | Guarded-cache | warm-table | 1.288 | 0.166 | 0.123 | False |
| county-zones | Guarded-combined | prepare-plus-one | 2.061 | 0.267 | 0.198 | False |
| county-zones | Guarded-combined | warm-table | 2.068 | 0.266 | 0.197 | False |
| county-zones | Guarded-common-y | prepare-plus-one | 1.681 | 0.218 | 0.162 | False |
| county-zones | Guarded-common-y | warm-table | 1.687 | 0.217 | 0.161 | False |
| district-zones | Guarded-baseline | prepare-plus-one | 1.000 | 0.131 | 0.091 | False |
| district-zones | Guarded-baseline | warm-table | 1.000 | 0.127 | 0.090 | False |
| district-zones | Guarded-cache | prepare-plus-one | 1.288 | 0.168 | 0.117 | False |
| district-zones | Guarded-cache | warm-table | 1.286 | 0.163 | 0.115 | False |
| district-zones | Guarded-combined | prepare-plus-one | 2.022 | 0.264 | 0.185 | False |
| district-zones | Guarded-combined | warm-table | 2.054 | 0.260 | 0.184 | False |
| district-zones | Guarded-common-y | prepare-plus-one | 1.678 | 0.219 | 0.153 | False |
| district-zones | Guarded-common-y | warm-table | 1.688 | 0.214 | 0.151 | False |

## Reproduction and validation

.NET 10.0.12; Microsoft Windows 10.0.26200; X64;
Intel64 Family 6 Model 158 Stepping 12, GenuineIntel; 16 logical processors.
Three sequential fresh processes, five calibrated batches per row, 36 rows per
process and 540 samples total. The fixed three method orders diversify placement.
There was no concurrent build, profile or benchmark; ordinary OS activity was
uncontrolled. Process ranges describe these runs, not confidence intervals.

Release solution build passed with no warnings or errors. Existing integer and
wide-coordinate controls passed too. Seven corrupt evidence copies (missing row,
order, median, output, binary hash, cache diagnostic, fallback record) were rejected
without changing submitted evidence. Source hashes, complete validation and
ordered matrix identities are checked before evidence is written.

See the [fixed protocol and commands](DOUBLE-ABLATION-PROTOCOL.md),
[runner](README.md) and [public evidence](../scanbeam-double-ablation-evidence.json).
The public manifest contains all aggregates, profile shares, compatibility checks
and raw-file hashes. Raw samples, complete pair records and traces are generated
under ignored artifacts and can be reproduced from the measured revision.

Local archive: `artifacts/scanbeam-double-ablation-ab1c6ba.zip` (899705 bytes);
SHA-256 `47d8f14df875f5d04a2e352fd8deb3776f199ddbfb4ef7cb8b030f0d865ea56d`. All 21 members were verified against
their source bytes. This archive is local, not a public download.
