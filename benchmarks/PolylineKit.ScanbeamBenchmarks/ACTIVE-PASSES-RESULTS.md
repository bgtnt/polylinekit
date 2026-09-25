# Fewer active-status passes: results

Measured source: [`bafb1d9ddc9226054bad7503d9467a064ebd1cf3`](https://github.com/bgtnt/polylinekit/commit/bafb1d9ddc9226054bad7503d9467a064ebd1cf3).
Historical control: [`4b8a131117264ec950a373bfae5462edd216b2c5`](https://github.com/bgtnt/polylinekit/commit/4b8a131117264ec950a373bfae5462edd216b2c5).

Removing redundant active-status passes reduces complete-query time by
**17.80–21.59%** against the same-binary coalesced-gap baseline. Warm tables take
**11.07–11.31 ms**, down from **14.12–14.17 ms**, with unchanged area and
certificate bits for all 422 geometry calls. They now take **9.27–10.49% less
time than Clipper**, but **18.85–22.49% more than shipping Winding**. Preparation
plus one table remains slower than both competitors. **All six selected
competitor gates fail; shipping Winding is unchanged and the new option remains off by default.**

## What changed

Both variants use copied prepared geometry, common-Y restriction, endpoint-X
caching, scalar-order filtering, specialized area arithmetic and gap coalescing.
The seventh flag, `optimizeActivePasses`, builds the sorted top-order prefix
incrementally. It preserves every comparison, inversion and crossing-construction
order. A band without crossings streams winding and filled gaps once, omitting
prefix/gap-start initialization and the redundant final top-order check. A band
with crossings initializes prefixes and gap starts together, then retains the
original event processing and separate verification and final gap walks.

The filled-gap helper evaluates height only after the fill predicate. It emits
the same IDs, Levels and contribution order; no arithmetic, tolerance or topology
guard changes. No new arrays, dependencies or shipping APIs are introduced.
See the [proof and counter definitions](ACTIVE-PASSES-NUMERICS.md),
[implementation](GuardedDoubleSweep.Active.cs), and [frozen protocol](ACTIVE-PASSES-PROTOCOL.md).

| Work across both directions | Guarded-gaps | Guarded-active |
|---|---:|---:|
| No-crossing bands | 64434 | 64434 |
| Crossing bands | 1498 | 1498 |
| Initial copy/prefix visits | 856384 | 9508 |
| Top-order writes, including shifts | 791956 | 429696 |
| Final top-order verification visits | 428192 | 9508 |
| All active-edge visits | 2230171 | 964611 |
| Charged work | 3224424 | 1958864 |

**97.73% of bands have no crossing.** Active visits fall **56.75%**, removing
1265560 visits; work falls by the same count (**39.25%**). For a completed band
with `n` active edges, the saving is `3n` visits without crossings and `n` with
crossings. These are source-level counters, not CPU instructions or predicted
timings. Both modes charge actual visits under the unchanged work limit.

Geometry work stays identical: 1504 crossing events, 62106 filled contributions,
17634 integrations, 44472 coalesced contributions, 36772 horizontal differences,
25580 interval-X evaluations and 527975 accepted scalar orders out of 540765
attempts. The optimization removes traversal and buffer work, not geometric tests.

## Complete-query and preparation costs

The frozen workload retains 109 Census rings, 10588 vertices, 2156 directional
pairs and 422 candidate calls. Each table visits 1078 pairs and runs 211 geometry
calls. Double inputs retain their original coordinates; Clipper uses scale 1e6.
Times below are medians of three process medians, with their observed range.
Ranges are descriptive, not confidence intervals. B/op is the allocation median
per complete table. Method labels abbreviate the full names in the
[public evidence](../scanbeam-active-evidence.json).

| Direction | Scope | Method | ms | Range, ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | warm-table | Guarded-gaps | 14.1663 | 14.1382–15.2325 | 0 |
| county-zones | warm-table | Guarded-active | 11.3092 | 11.1316–11.3330 | 0 |
| county-zones | warm-table | Clipper | 12.4645 | 12.4253–12.7218 | 625088 |
| county-zones | warm-table | Winding | 9.5154 | 9.5103–9.6303 | 0 |
| county-zones | fresh queries | Guarded-gaps | 15.2802 | 15.0557–16.1380 | 568952 |
| county-zones | fresh queries | Guarded-active | 12.1959 | 12.1886–12.2130 | 568952 |
| county-zones | fresh queries | Clipper | 12.8674 | 12.7465–12.9166 | 975040 |
| county-zones | fresh queries | Winding | 9.6274 | 9.6214–9.6625 | 1120 |
| county-zones | prepare + one | Guarded-gaps | 16.9179 | 16.5339–17.5601 | 2248032 |
| county-zones | prepare + one | Guarded-active | 13.6561 | 13.4451–13.7782 | 2248032 |
| county-zones | prepare + one | Clipper | 12.8441 | 12.7760–12.8531 | 1626168 |
| county-zones | prepare + one | Winding | 9.6285 | 9.6205–9.7523 | 10480 |
| district-zones | warm-table | Guarded-gaps | 14.1184 | 14.0750–14.9223 | 0 |
| district-zones | warm-table | Guarded-active | 11.0706 | 11.0242–11.1813 | 0 |
| district-zones | warm-table | Clipper | 12.3677 | 12.0678–12.3720 | 625088 |
| district-zones | warm-table | Winding | 9.0376 | 8.9701–9.0391 | 0 |
| district-zones | fresh queries | Guarded-gaps | 15.5076 | 15.4070–16.3007 | 986024 |
| district-zones | fresh queries | Guarded-active | 12.5032 | 12.4101–12.5988 | 986024 |
| district-zones | fresh queries | Clipper | 12.7294 | 12.6469–12.7491 | 1241472 |
| district-zones | fresh queries | Winding | 9.1449 | 9.1306–9.1565 | 7384 |
| district-zones | prepare + one | Guarded-gaps | 16.4104 | 16.2841–17.1988 | 2224848 |
| district-zones | prepare + one | Guarded-active | 13.4900 | 13.4268–13.5083 | 2224848 |
| district-zones | prepare + one | Clipper | 12.6105 | 12.4971–12.6410 | 1624352 |
| district-zones | prepare + one | Winding | 9.1668 | 9.0934–9.1790 | 10480 |

Fresh-query scope prepares each incoming query once per table. Preparation plus
one charges a new comparator, catalogues and workspace growth. Winding retains
its warmed thread-local workspace; this is not cold-process startup. Parsing
and I/O are excluded equally. Guarded-active has disjoint, lower observed ranges
than both current and historical Guarded-gaps in all six full-query cells.

The fixed gate requires each selected cell to take at most **0.8x Clipper** and
**0.9x Winding**. Warm-table improvement over Clipper is below that target, and
every selected cell remains slower than Winding. All six baseline cells also
fail, giving twelve failed gate cells in the evidence.

| Direction | Preparation method | ms | Range, ms | B/op |
|---|---|---:|---:|---:|
| county-zones | Guarded-gaps | 2.2041 | 2.1850–2.2152 | 1557488 |
| county-zones | Guarded-active | 2.1983 | 2.1893–2.2053 | 1557488 |
| county-zones | Clipper | 0.4044 | 0.3996–0.4055 | 968888 |
| county-zones | Winding | 0.1262 | 0.1251–0.1264 | 10544 |
| district-zones | Guarded-gaps | 2.2208 | 2.1927–2.2237 | 1557488 |
| district-zones | Guarded-active | 2.2090 | 2.2036–2.2276 | 1557488 |
| district-zones | Clipper | 0.4063 | 0.4007–0.4142 | 968888 |
| district-zones | Winding | 0.1252 | 0.1250–0.1267 | 10544 |

Preparation is unchanged. Its immutable retained payload remains **1524672
array-element bytes**; existing coalescer arrays retain at most **84840 bytes**
on this workload. Both figures exclude headers, other scratch and peak memory.
Existing prefix and gap-start arrays remain allocated even for no-crossing bands.
All warm-table samples allocate zero bytes in both sweep variants. Their fresh
query and preparation-plus-one allocations also match each other. Fresh-comparator
scopes allocate **32 B more than the historical build**, consistent with added
scalar fields; this is an observed allocation delta, not an object-layout measurement.

## Historical sensitivity

The frozen old binary was rerun in three fresh processes, interleaved as
**old1, new1, new2, old2, old3, new3**. Each series contains 480 samples and is
summarized separately. Historical Guarded-gaps is method C; the new baseline is
method B. Method position and compiled code therefore differ. Shipping source
is unchanged, but separately built Winding DLL hashes differ; actual binary
identities and environments are recorded in the manifest.

| Direction | Scope | Old gaps/new gaps | Old gaps/active | Winding-normalized | Clipper-normalized |
|---|---|---:|---:|---:|---:|
| county-zones | warm-table | 0.990 | 1.240 | 1.233 | 1.220 |
| county-zones | fresh queries | 0.978 | 1.226 | 1.236 | 1.208 |
| county-zones | prepare + one | 0.973 | 1.205 | 1.208 | 1.204 |
| district-zones | warm-table | 0.983 | 1.254 | 1.251 | 1.247 |
| district-zones | fresh queries | 0.985 | 1.221 | 1.224 | 1.204 |
| district-zones | prepare + one | 0.992 | 1.206 | 1.204 | 1.199 |

For competitor C, normalization is `(old gaps / old C) / (new active / new C)`.
These are sensitivity observations, not corrections that isolate causality or
remove environmental/JIT drift. Historical warm medians are 13.88–14.03 ms;
the new variant reduces their cost by 19.37–20.24%, slightly less than against
the current baseline. Across all six historical cells the reduction is
17.02–20.24%. The decision still uses current competitors and the fixed gate.

## Validation, reproduction and decision

Normal and hardware-intrinsics-disabled runs each passed **2219 active-pass
controls**, **7034 snapshot controls** (2977 copied, 4057 direct), and **51422
exact-rational/analytic geometry assertions**. Active controls include 736
no-crossing successes, 232 crossing successes and 960 fallback controls, plus
a formerly work-limited case that now certifies under the same budget. They cover
both fill rules, winding changes, crossings, mixed raw/prepared/direct reuse,
invalid inputs, growth and recovery.

All **422 frozen geometry calls certify with zero fallback** in both variants,
retaining identical area/error-bound bits and geometric/integration diagnostics.
Normal/no-intrinsics validation agrees; the legacy baseline validation is
byte-identical to the historical file. Independent NTS checks retain the existing
1 m² / 1e-8 coverage tolerances. NTS is not an exact oracle, and this experiment
does not change or improve numerical accuracy.

The unchanged positive evidence control passed. Eleven corruptions were rejected:
missing row, method order, median, output, binary hash, filter diagnostic, fallback
record, prepared payload, certificate, gap counter and pass counter. Rejected
submissions retain their JSON bytes and produce neither summary nor evidence.

.NET 10.0.12; Microsoft Windows 10.0.26200; X64; Intel64 Family 6 Model 158
Stepping 12, GenuineIntel; 16 logical processors. Release,
`DOTNET_TieredCompilation=0`, orders ABCD/CDAB/BDAC, three fresh processes,
five calibrated samples per row, 32 rows per process. No profiles were collected
and no build, profiler or competing benchmark ran during timing; ordinary OS
activity remains uncontrolled.

Restore locked and build Release at the measured revision, then run:

```text
dotnet <measured-dll> check-active-passes artifacts/scanbeam-active-check
dotnet <measured-dll> benchmark-active-passes artifacts/scanbeam-active <run 1..3> <revision>
dotnet <measured-dll> summarize-active-passes artifacts/scanbeam-active
```

Repeat the check with `DOTNET_EnableHWIntrinsic=0`. Use the historical binary's
`benchmark-gap-coalescing` / `summarize-gap-coalescing` in a separate directory,
preserving the process order above. The [public manifest](../scanbeam-active-evidence.json)
records both series, hashes, allocation medians, counters, compatibility and
negative controls. Full samples and validation rows remain in the local archive.

Keep the variant as measured experimental code with its flag off by default.
It is a useful structural improvement to this sweep on this dataset/runtime,
but does not justify replacing shipping Winding. No performance result for C++, other
hardware, dense-crossing workloads or other polygon catalogues is established.

Local archive: `artifacts/scanbeam-active-bafb1d9.zip`, **232423 bytes, 33 members**.
SHA-256 `70cdeea26d2bb46bb589ba2120d1dbb967b67b8518a99a9457312c5bf317882b`.
The archive is local, not publicly hosted.
