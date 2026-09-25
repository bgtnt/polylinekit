# Scalar filter before common support: results

Measured source: [`b260161ac4074969c91310a6ea361b92f75e0b7e`](https://github.com/bgtnt/polylinekit/commit/b260161ac4074969c91310a6ea361b92f75e0b7e).
Historical control: [`e4ffc1092d14bc5a1c11f8122386475c13e972bb`](https://github.com/bgtnt/polylinekit/commit/e4ffc1092d14bc5a1c11f8122386475c13e972bb).

**A useful improvement to the experimental sweep, insufficient for adoption.**
Moving the certified scalar-order filter before `SameSupport` reduces all six
complete-query medians by **6.59–9.45%** against the same-binary copied-data
baseline, and **7.82–11.14%** against the historical copied-data baseline.
These percentages are saved time, `1 - selected / baseline`, not the percentage
increase in throughput. Warm tables take **10.24–10.25 ms**, versus **10.97–11.31
ms** for the current baseline and **8.93–9.56 ms** for shipping Winding.
All six adoption gates fail. Keep this as an experimental option, default false;
shipping code, dependencies and public APIs are unchanged.

## Mechanism and limits

A strict certified X order excludes common support, so accepted scalar probes
can skip that test. Inconclusive probes retain the original support shortcut,
then interval/tie handling, without probing again. Geometry and scalar metadata
remain copied. Arithmetic, tolerances, topology decisions and work charges are
unchanged; see the [proof](FILTER-FIRST-NUMERICS.md) and
[fixed protocol](FILTER-FIRST-PROTOCOL.md).

| Endpoint operations, both directions | Guarded-active | Guarded-filter-first |
|---|---:|---:|
| Support tests | 540765 | 12790 |
| Support matches | 0 | 0 |
| Actual scalar probes | 540765 | 540765 |
| Logical scalar accepts | 527975 | 527975 |

The selected path skips **527975 support tests (97.63%)**. Totals are derived
from common comparison/support-match counters and existing logical filter
counters. Both variants execute identical added increment streams; there is no
counter increment attached only to the skipped support-test path. Historical
measurements separately expose the combined sensitivity to instrumentation,
generated code and environment.

**This workload has no support matches.** It therefore measures no cost from
the additional inconclusive probes on common support. Focused synthetic controls
exercise those probes and prove result parity, but supply no timing evidence for
coincident or heavily retraced inputs. A performance claim for such inputs needs
a separate measurement.

Both variants retain 65932 bands, 1504 crossings, 964611 active visits, 1958864
charged work units, 62106 contributions, 17634 integrations, 44472 merges,
36772 horizontal differences and 25580 interval-X evaluations. Each copies
194936 scalar records, or 7797440 bytes, across both tables. Removing support
tests changes none of this geometric work or copying.

## Complete-query measurements

The frozen workload contains 109 Census rings, 10588 vertices, 2156 directional
pairs and 422 candidate geometry calls. Each table visits 1078 pairs and executes
211 geometry calls. Double coordinates are unrounded; Clipper uses scale 1e6.
Times are medians of three process medians, with their observed ranges. Ranges
are not confidence intervals; B/op is allocation per complete table. Clipper
means `Clipper64-reused-data`, Winding means `Winding-intersection-only`.

| Direction | Scope | Method | ms | Range, ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | warm-table | Guarded-active | 11.3056 | 11.0887–11.4245 | 0 |
| county-zones | warm-table | Guarded-filter-first | 10.2368 | 10.1988–10.4716 | 0 |
| county-zones | warm-table | Clipper | 12.4963 | 12.3084–12.7395 | 625088 |
| county-zones | warm-table | Winding | 9.5631 | 9.5184–9.5984 | 0 |
| county-zones | fresh queries | Guarded-active | 12.2307 | 12.0886–12.4268 | 568952 |
| county-zones | fresh queries | Guarded-filter-first | 11.2424 | 11.1623–11.3979 | 568952 |
| county-zones | fresh queries | Clipper | 12.7753 | 12.7391–12.9578 | 975040 |
| county-zones | fresh queries | Winding | 9.6365 | 9.5210–9.6525 | 1120 |
| county-zones | prepare + one | Guarded-active | 13.9427 | 13.7962–14.1679 | 2248056 |
| county-zones | prepare + one | Guarded-filter-first | 12.6778 | 12.5994–12.6979 | 2248056 |
| county-zones | prepare + one | Clipper | 12.6581 | 12.5017–12.8056 | 1626168 |
| county-zones | prepare + one | Winding | 9.7657 | 9.6212–9.7716 | 10480 |
| district-zones | warm-table | Guarded-active | 10.9712 | 10.9624–11.5826 | 0 |
| district-zones | warm-table | Guarded-filter-first | 10.2485 | 10.0349–10.7311 | 0 |
| district-zones | warm-table | Clipper | 12.3104 | 12.1614–12.4830 | 625088 |
| district-zones | warm-table | Winding | 8.9282 | 8.9212–9.0870 | 0 |
| district-zones | fresh queries | Guarded-active | 12.3555 | 12.3460–12.6748 | 986024 |
| district-zones | fresh queries | Guarded-filter-first | 11.4184 | 11.3385–11.8172 | 986024 |
| district-zones | fresh queries | Clipper | 12.5081 | 12.4597–12.7713 | 1241472 |
| district-zones | fresh queries | Winding | 9.0915 | 8.9880–9.1248 | 7384 |
| district-zones | prepare + one | Guarded-active | 13.5236 | 13.4301–13.5291 | 2224872 |
| district-zones | prepare + one | Guarded-filter-first | 12.4749 | 12.2852–12.4833 | 2224872 |
| district-zones | prepare + one | Clipper | 12.6477 | 12.5831–12.6557 | 1624352 |
| district-zones | prepare + one | Winding | 9.0227 | 9.0169–9.4173 | 10480 |

All six selected-versus-baseline ranges are disjoint. Winding remains faster in
every complete-query cell. The fixed adoption gate requires each selected cell
to take at most 0.8 times Clipper and 0.9 times Winding; neither requirement is
met in any cell. Fresh queries are prepared once per table. Preparation plus
one includes a new comparator, catalogues and sweep-workspace growth; Winding's
thread-local scratch is warm. I/O is excluded equally; this is not cold startup.

| Preparation method | County ms (range) | District ms (range) | B/op, either direction |
|---|---:|---:|---:|
| Guarded-active | 2.2371 (2.2019–2.2675) | 2.2091 (2.1632–2.3067) | 1557512 |
| Guarded-filter-first | 2.1919 (2.1574–2.2180) | 2.1601 (2.1536–2.2554) | 1557512 |
| Clipper | 0.4041 (0.3898–0.4081) | 0.3980 (0.3961–0.3981) | 968888 |
| Winding | 0.1266 (0.1253–0.1272) | 0.1264 (0.1249–0.1270) | 10544 |

Preparation is unchanged and sweep preparation ranges overlap; differences in
these preparation-only timings establish no algorithmic improvement. Both sweep
variants allocate zero bytes in every warm-table sample and equal amounts in
other scopes. Fresh-comparator scopes allocate **16 B more than the historical
build**; this observed delta does not independently establish object layout.
Prepared payload remains 1524672 retained array-element bytes; coalescer arrays
retain at most 84840 bytes. These exclude headers and other scratch, and are not
peak-memory measurements. No arrays are added or removed.

## Historical sensitivity and validation

The frozen old binary ran its unchanged scalar-view matrix, with copied
Guarded-active at B in both matrices. Historical C is the previously rejected
scalar-view variant. Processes were interleaved old1/new1/new2/old2/old3/new3;
the two independent 480-sample series were not pooled.

| Direction | Scope | Old/current baseline | Old/selected | Winding-normalized | Clipper-normalized |
|---|---|---:|---:|---:|---:|
| county-zones | warm-table | 1.019 | 1.125 | 1.103 | 1.109 |
| county-zones | fresh queries | 1.016 | 1.106 | 1.084 | 1.095 |
| county-zones | prepare + one | 0.999 | 1.098 | 1.085 | 1.084 |
| district-zones | warm-table | 1.022 | 1.094 | 1.071 | 1.100 |
| district-zones | fresh queries | 1.007 | 1.090 | 1.074 | 1.079 |
| district-zones | prepare + one | 1.001 | 1.085 | 1.066 | 1.086 |

Normalization for competitor C is `(old active / old C) / (new selected / new C)`.
All six historical comparisons also improve, including both normalizations.
Current Winding medians are 1.22–2.05% lower than historical, while Clipper shifts
from 1.46% lower to 0.55% higher across the six cells.
These are sensitivity observations, not corrections eliminating JIT or system
drift. They do not isolate the cost of each instruction. No profiles were run.

Normal and hardware-intrinsics-disabled runs each passed **6656 focused parity
controls**, **2084 exact-sign controls**, **7034 snapshot/lifecycle controls** and
**51422 exact-rational/analytic geometry assertions**. The focused matrix covers
raw, copied, direct and scalar-view storage; both fill rules; filtering on/off;
support matches, unequal vertical extents, retracing, near ties, invalid inputs,
fallback and reuse. In 1280 controls the reordered variant executes extra probes
before a support shortcut; these establish correctness, not their runtime cost.

All 422 frozen calls certify with identical area, coverage and error-bound bits
and all previous diagnostics. The historical copied baseline matches too.
Legacy validation remains byte-identical. Independent NTS area/coverage checks
pass; NTS is not an exact oracle. The positive evidence control passed and all
13 corrupted submissions were rejected without changing submitted JSON or
producing derived evidence. Corruptions include the new operation counters.

## Reproduction and provenance

.NET 10.0.12; Microsoft Windows 10.0.26200; X64; Intel64 Family 6 Model 158
Stepping 12, GenuineIntel; 16 logical processors. Release,
`DOTNET_TieredCompilation=0`, three fresh sequential processes, orders
ABCD/CDAB/BDAC, five calibrated samples per row and 32 rows per process. No
builds, profilers or other timing workloads ran concurrently. Ordinary OS
activity remains uncontrolled. Restore locked, build at the measured revision,
freeze the resulting binary and run:

```text
dotnet <measured-dll> check-filter-first artifacts/scanbeam-filter-first-check
dotnet <measured-dll> benchmark-filter-first artifacts/scanbeam-filter-first <run 1..3> <revision>
dotnet <measured-dll> summarize-filter-first artifacts/scanbeam-filter-first
```

Repeat checks with `DOTNET_EnableHWIntrinsic=0`; use `check-scalar-view` for the
legacy check. The frozen historical binary uses `benchmark-scalar-view` and
`summarize-scalar-view` in a separate directory. Commands, binary identities,
per-run hashes, diagnostics and sensitivity calculations are recorded in the
[public evidence manifest](../scanbeam-filter-first-evidence.json).

Source dataset SHA-256:
`c42265beea7590918ecf06107263ad63f1395dc33f592653344c5980bda379b0`.
Transformed geometry SHA-256:
`3cc5dcac07871507da1187167d32ea11c1124accb652a062ce004e5731bf8f35`.
Measured harness SHA-256:
`7cea7fc4264b05a7fbd8ba8cbe8ae4836d730399abd80b34807cfc4ea6c5e379`.

Local raw-evidence archive: `artifacts/scanbeam-filter-first-b260161.zip`,
**242973 bytes, 37 members**; SHA-256
`66474b144e13de905a01d38711cf958bebb189369f4c8d67227c9971d16e5bc2`.
The archive is not publicly hosted. The repository contains the compact
manifest and reproduction code, while individual raw samples remain local.
Before broader use, measure a workload with frequent support matches and retain
the existing competitor gate for the complete operation.
