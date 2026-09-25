# Scalar edge-order filter results

Measured source: [`c9f0072a31fa89513edfcaf59365765b72c7f3d2`](https://github.com/bgtnt/polylinekit/commit/c9f0072a31fa89513edfcaf59365765b72c7f3d2).

A later [prepared-path experiment](PREPARED-SWEEP-RESULTS.md) reuses slopes and
sorted endpoint streams and measures preparation, warm tables and fresh queries.
The measurements below remain the historical scalar-filter result.

The scalar filter reduces warm table time by **19.95–20.24%**, from about 49 ms
to **39.4 ms**. Fresh preparation plus one table improves by 19.32–19.81%.
These gains include filter metadata preparation. **All four filtered competitor
gates still fail**: warm traversal remains 3.15–3.21x Clipper time and 4.11–4.35x
current Winding time. The filter remains outside the shipping library.

## Change and certificate

Only endpoint ordering gets a new scalar filter. The existing slope interval
yields a scalar slope and a conservatively rounded uniform error radius for each
edge. A strict comparison between the resulting enclosures can establish order
without computing the full endpoint-X intervals. Inconclusive comparisons use
the existing interval/tie path; they do not immediately abandon the whole call.
Same-support handling, event construction, area arithmetic and final area-error
budgets are unchanged. There is no extra scalar cache or endpoint-sort change.

The [derivation](SCALAR-FILTER-NUMERICS.md) accounts for subtraction rounding,
product underflow, bound construction, and a strict comparison of two rounded
expressions. It assumes binary64 round-to-nearest and gradual underflow.
No epsilon equality or approximate event key is introduced. Default behavior
keeps the filter disabled; its separate metadata is allocated only when enabled.

## Complete-query measurements

One warm operation traverses all 1078 original pairs in the indicated direction.
The baseline is the earlier common-Y plus cached-X sweep, measured in the same
binary/process series. Source arrays, candidate masks and competitors are fixed.

| Method | County zones, ms | District zones, ms | Warm B/op |
|---|---:|---:|---:|
| Guarded-combined | 49.480 | 49.230 | 0 |
| Guarded-filtered | 39.467 | 39.410 | 0 |
| Clipper64-reused-data | 12.537 | 12.276 | 625088 |
| Winding-intersection-only | 9.596 | 9.051 | 0 |

All guarded warm allocation samples are zero. The additional metadata raises
fresh-session allocations by 100272 B for county zones and 96888 B for district
zones. The isolated catalogue-preparation row constructs bounds/areas, while
filter metadata is built inside each geometry call; its cost therefore appears
in both full-table scopes. Managed allocation is not retained or peak memory.

## Numerical results and work

Both sweeps certify all 422 geometry candidates with no whole-call fallback.
All 2156 directional pair results, coverage values and error radii remain
bit-identical between filtered and unfiltered sweeps. Logical bands, crossings,
status visits and work counts also match. The filter-off historical commands
preserve all 12936 stored method results and diagnostics from the previous experiment.

| Diagnostic, both directions | Unfiltered | Filtered |
|---|---:|---:|
| Filter attempts |0|540765|
| Accepted strict orders |0|527975|
| Continued to interval comparison |0|12790|
| Actual interval X evaluations |495764|25580|
| Interval X cache hits |585766|0|
| Endpoint bands |65932|65932|
| Constructed crossing events |1504|1504|

The filter handles **97.63%** of attempted comparisons. Interval requests equal
twice the number of interval continuations; the corresponding unfiltered requests
equal twice the number of filter attempts. These identities hold per certified
pair. Acceptance rate and work reduction are not themselves speed measurements.

Every method satisfies the unchanged original-NTS budgets of 1 m² intersection
and 1e-8 absolute coverage fraction. NTS remains an independent implementation,
not an exact oracle. The maximum guarded area difference is 2.62260437012e-6 m²;
the largest certified radius is 0.000324964523315 m². Clipper keeps its scale-1e6
quantization contract. No source geometry is rounded or repaired for the sweeps.

The direct exact-dyadic controls checked 2084 signs: 1692 accepted and 392 rejected.
All accepted signs match independent BigInteger line comparisons; controls cover
ties, tiny products, subnormal coordinates, large offsets, extreme slopes and
excluded inputs. The full filtered area controls passed 51422 assertions over
the four previous flag combinations. Both suites passed normally and with
hardware intrinsics disabled. Filtered/unfiltered certified areas and cache
behavior are compared directly. These finite checks supplement the derivation;
they do not constitute a formal proof of the entire implementation.

## Profile and remaining cost

Separate 20-second repeated county-table profiles used `dotnet-trace 10.0.745401`,
`dotnet-sampled-thread-time`, and the same measured binary with tiering disabled.
The filter keeps samples under TraverseReal, including four initial validation/
warmup calls. Preparation outside traversal is excluded; no profiler ran during
benchmarks. The reporting script now includes the new ScalarOrderFilter frames;
this reporting-only change and its hash are recorded separately from measured code.

| Inclusive sampled frame share | Unfiltered | Filtered |
|---|---:|---:|
| ProcessBand | 58.38% | 41.15% |
| CompareAtEndpoint | 32.69% | 9.64% |
| XAt | 13.86% | 0.14% |
| Endpoint sort | 21.27% | 26.83% |
| AccumulateGap | 16.24% | 21.29% |
| AppendEdges | 7.14% | 20.15% |
| Scalar preparation | not observed | 4.17% |

Inclusive shares overlap and must not be added. They are sampled managed
thread-time shares, not exact CPU percentages; JIT inlining affects attribution.
A larger relative share does not prove a routine became slower. The profile
supports reduced comparison cost while exposing the added preparation work.
Endpoint sorting and area accumulation remain substantial.

## Decision

Retain the tested filter as an optional research variant. The roughly 20% gain
does not close the gap to either competitor and does not justify replacing the
shipping Winding implementation. Further work should test a different source of
cost, such as reusing sorted immutable edge preparation, under a separately fixed
preparation contract. No such prepared backend, SIMD gain or C++ speed claim is
delivered by this experiment.

## Full measurements and gates

| Direction | Method | Scope | ms | Process range ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | Clipper64-reused-data | prepare | 0.4008 | 0.3968–0.4016 | 968888 |
| county-zones | Clipper64-reused-data | prepare-plus-one | 12.8485 | 12.7449–12.9309 | 1626168 |
| county-zones | Clipper64-reused-data | warm-table | 12.5369 | 12.4282–12.5542 | 625088 |
| county-zones | Guarded-combined | prepare | 0.1265 | 0.1257–0.1274 | 10896 |
| county-zones | Guarded-combined | prepare-plus-one | 49.2595 | 49.1227–51.6186 | 460816 |
| county-zones | Guarded-combined | warm-table | 49.4798 | 48.9400–51.4542 | 0 |
| county-zones | Guarded-filtered | prepare | 0.1256 | 0.1249–0.1276 | 10896 |
| county-zones | Guarded-filtered | prepare-plus-one | 39.7441 | 39.4076–40.2111 | 561088 |
| county-zones | Guarded-filtered | warm-table | 39.4673 | 38.9717–40.0777 | 0 |
| county-zones | Winding-intersection-only | prepare | 0.1256 | 0.1250–0.1256 | 10544 |
| county-zones | Winding-intersection-only | prepare-plus-one | 9.7664 | 9.7467–9.8520 | 10480 |
| county-zones | Winding-intersection-only | warm-table | 9.5963 | 9.5062–9.6281 | 0 |
| district-zones | Clipper64-reused-data | prepare | 0.4003 | 0.3983–0.4013 | 968888 |
| district-zones | Clipper64-reused-data | prepare-plus-one | 12.5739 | 12.4729–12.7114 | 1624352 |
| district-zones | Clipper64-reused-data | warm-table | 12.2756 | 12.2424–12.3154 | 625088 |
| district-zones | Guarded-combined | prepare | 0.1253 | 0.1251–0.1260 | 10896 |
| district-zones | Guarded-combined | prepare-plus-one | 48.7748 | 48.5314–51.7367 | 445744 |
| district-zones | Guarded-combined | warm-table | 49.2301 | 48.2581–51.5112 | 0 |
| district-zones | Guarded-filtered | prepare | 0.1258 | 0.1256–0.1269 | 10896 |
| district-zones | Guarded-filtered | prepare-plus-one | 39.1145 | 38.8462–40.6419 | 542632 |
| district-zones | Guarded-filtered | warm-table | 39.4095 | 38.9626–40.3920 | 0 |
| district-zones | Winding-intersection-only | prepare | 0.1264 | 0.1249–0.1270 | 10544 |
| district-zones | Winding-intersection-only | prepare-plus-one | 9.2811 | 9.1844–9.2867 | 10480 |
| district-zones | Winding-intersection-only | warm-table | 9.0514 | 8.9544–9.1466 | 0 |

Each full-query cell requires time <=0.8 times Clipper and <=0.9 times Winding.
Each ratio divides the comparison time by the variant time; larger is better.

| Direction | Variant | Scope | Baseline/variant | Clipper/variant | Winding/variant | Pass |
|---|---|---|---:|---:|---:|---|
| county-zones | Guarded-combined | prepare-plus-one | 1.000 | 0.261 | 0.198 | False |
| county-zones | Guarded-combined | warm-table | 1.000 | 0.253 | 0.194 | False |
| county-zones | Guarded-filtered | prepare-plus-one | 1.239 | 0.323 | 0.246 | False |
| county-zones | Guarded-filtered | warm-table | 1.254 | 0.318 | 0.243 | False |
| district-zones | Guarded-combined | prepare-plus-one | 1.000 | 0.258 | 0.190 | False |
| district-zones | Guarded-combined | warm-table | 1.000 | 0.249 | 0.184 | False |
| district-zones | Guarded-filtered | prepare-plus-one | 1.247 | 0.321 | 0.237 | False |
| district-zones | Guarded-filtered | warm-table | 1.249 | 0.311 | 0.230 | False |

## Reproduction

.NET 10.0.12; Microsoft Windows 10.0.26200; X64;
Intel64 Family 6 Model 158 Stepping 12, GenuineIntel; 16 logical processors.
Three fresh sequential processes, five calibrated batches per row, 24 rows per
process and 360 samples total. Fixed orders diversify method positions. There
was no concurrent build, profiler or benchmark. Ordinary OS activity was
uncontrolled; process ranges describe these runs, not confidence intervals.

The Release solution build passed with no warnings or errors. The earlier
four-variant controls also passed. Seven corrupt evidence copies (missing row,
order, median, output, binary hash, filter diagnostic, fallback record) were
rejected without changing submitted files. Exact recorded diagnostic files,
ordered matrices and outputs are recomputed before evidence is written.

See the [fixed protocol/commands](SCALAR-FILTER-PROTOCOL.md),
[runner](README.md) and [public evidence](../scanbeam-scalar-filter-evidence.json).
The manifest contains all aggregates, diagnostics, profile shares, compatibility
checks and raw-file hashes. Raw samples, pair records and traces stay under
ignored artifacts and are reproducible from the recorded sources.

Local archive: `artifacts/scanbeam-scalar-filter-c9f0072.zip` (1016578 bytes);
SHA-256 `75be82dba45e918d606d2044642e5a583adcee045d88b9ec2a3f825642363c99`. All 23 members were verified against
their source bytes. The archive is local, not a public download.
