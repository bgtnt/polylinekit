# Borrowed prepared scalar metadata: results

Measured source: [`e4ffc1092d14bc5a1c11f8122386475c13e972bb`](https://github.com/bgtnt/polylinekit/commit/e4ffc1092d14bc5a1c11f8122386475c13e972bb).
Historical control: [`bafb1d9ddc9226054bad7503d9467a064ebd1cf3`](https://github.com/bgtnt/polylinekit/commit/bafb1d9ddc9226054bad7503d9467a064ebd1cf3).

**Do not adopt the scalar-view variant.** It eliminates the intended copy but
makes all six complete-query medians **2.41–3.74% slower** than the same-binary
copied baseline, and **1.11–3.26% slower** than the historical baseline. Warm
tables take **11.31–11.62 ms**, versus **11.04–11.25 ms** for current copied
metadata. All 422 geometry calls retain identical result and certificate bits.
Keep copied scalar metadata for this sweep, the experiment flag off, and
shipping Winding unchanged. All six selected competitor gates still fail.

## Change and retained contract

The eighth constructor flag, `borrowPreparedScalars`, retains contiguous copied
geometry and its pair-specific Loop tags. Only scalar-order filter records are
read from the two immutable prepared snapshots. Global edge IDs map to the
first array below `first.EdgeCount`, then to the second array after subtracting
that count. The existing ref-readonly accessor returns exactly the same fields
that were previously copied into scratch.

Binding follows all input, AABB and deferred slope-preparation gates. Query
`finally` and the next query's reset clear borrowed references, including on
fallback or exception. Raw calls and filter-disabled calls retain their usual
behavior; full-direct mode still takes precedence. See the
[indexing and lifetime proof](SCALAR-VIEW-NUMERICS.md) and
[fixed protocol](SCALAR-VIEW-PROTOCOL.md). No arithmetic, topology guard, work
charge, dependency, shipping API or scratch-allocation policy changes.

| Scalar metadata across both directions | Guarded-active | Guarded-scalar-view |
|---|---:|---:|
| Records copied | 194936 | 0 |
| Records bound without copying | 0 | 194936 |
| Bytes per record | 40 | 40 |

The change removes **7797440 bytes of requested scalar-record copying** across
the two tables. This is record count times struct size, not measured memory
bandwidth or saved allocation. Borrowed counts describe available records,
not the number of distinct records actually read by the filter.

All previous counters match: 65932 bands, 1504 crossing events, 964611 active
visits, 1958864 charged work units, 62106 contributions, 17634 integrations,
44472 merges, 36772 horizontal differences and 25580 interval-X evaluations.
Scalar ordering accepts 527975 of 540765 attempts in either mode. Removing the
copy does not remove geometric work.

## Complete-query and preparation measurements

The workload is unchanged: 109 Census rings, 10588 vertices, 2156 directional
pairs and 422 candidate geometry calls. Each directional table visits 1078
pairs and runs 211 geometry calls. Double coordinates remain unrounded; Clipper
uses scale 1e6. Times are medians of three process medians, followed by their
observed ranges. Ranges are not confidence intervals. B/op is the allocation
median per complete table; abbreviated competitor names refer to
`Clipper64-reused-data` and `Winding-intersection-only`.

| Direction | Scope | Method | ms | Range, ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | warm-table | Guarded-active | 11.2460 | 11.2335–11.2515 | 0 |
| county-zones | warm-table | Guarded-scalar-view | 11.6219 | 11.6014–11.6687 | 0 |
| county-zones | warm-table | Clipper | 12.5980 | 12.3997–12.6063 | 625088 |
| county-zones | warm-table | Winding | 9.6640 | 9.5733–9.6943 | 0 |
| county-zones | fresh queries | Guarded-active | 12.1135 | 12.0021–12.1475 | 568952 |
| county-zones | fresh queries | Guarded-scalar-view | 12.5664 | 12.4714–12.6334 | 568952 |
| county-zones | fresh queries | Clipper | 12.7541 | 12.6740–12.9515 | 975040 |
| county-zones | fresh queries | Winding | 9.7430 | 9.6388–9.7560 | 1120 |
| county-zones | prepare + one | Guarded-active | 13.5603 | 13.5569–13.6878 | 2248040 |
| county-zones | prepare + one | Guarded-scalar-view | 14.0116 | 13.9530–14.1554 | 2248040 |
| county-zones | prepare + one | Clipper | 12.7670 | 12.6777–13.1878 | 1626168 |
| county-zones | prepare + one | Winding | 9.8313 | 9.7489–9.8428 | 10480 |
| district-zones | warm-table | Guarded-active | 11.0444 | 11.0432–11.0644 | 0 |
| district-zones | warm-table | Guarded-scalar-view | 11.3101 | 11.2897–11.5060 | 0 |
| district-zones | warm-table | Clipper | 12.3442 | 12.2863–12.3959 | 625088 |
| district-zones | warm-table | Winding | 9.1191 | 9.0292–9.1342 | 0 |
| district-zones | fresh queries | Guarded-active | 12.3610 | 12.3203–12.4228 | 986024 |
| district-zones | fresh queries | Guarded-scalar-view | 12.7757 | 12.7297–12.7780 | 986024 |
| district-zones | fresh queries | Clipper | 12.7352 | 12.7029–12.8227 | 1241472 |
| district-zones | fresh queries | Winding | 9.2114 | 9.1912–9.2756 | 7384 |
| district-zones | prepare + one | Guarded-active | 13.3222 | 13.2435–13.4381 | 2224856 |
| district-zones | prepare + one | Guarded-scalar-view | 13.6727 | 13.6150–13.8339 | 2224856 |
| district-zones | prepare + one | Clipper | 12.5820 | 12.4978–12.6134 | 1624352 |
| district-zones | prepare + one | Winding | 9.1951 | 9.1622–9.2637 | 10480 |

All six current full-query ranges are disjoint, with scalar views slower.
Fresh queries are prepared once per table. Preparation plus one charges a new
comparator, catalogues and workspace growth; Winding's thread-local scratch is
already warm. Parsing and I/O are excluded equally. This is not cold-process
startup. The unchanged adoption gate requires each selected cell to take at
most 0.8x Clipper and 0.9x Winding. Both methods fail all six cells.

| Direction | Preparation method | ms | Range, ms | B/op |
|---|---|---:|---:|---:|
| county-zones | Guarded-active | 2.2149 | 2.2022–2.2897 | 1557496 |
| county-zones | Guarded-scalar-view | 2.2160 | 2.2148–2.2870 | 1557496 |
| county-zones | Clipper | 0.4079 | 0.4025–0.4842 | 968888 |
| county-zones | Winding | 0.1260 | 0.1242–0.1304 | 10544 |
| district-zones | Guarded-active | 2.2231 | 2.1980–2.2889 | 1557496 |
| district-zones | Guarded-scalar-view | 2.1955 | 2.1868–2.2960 | 1557496 |
| district-zones | Clipper | 0.4113 | 0.4075–0.4132 | 968888 |
| district-zones | Winding | 0.1259 | 0.1247–0.1261 | 10544 |

Preparation is unchanged and its ranges overlap. Immutable prepared payload
remains **1524672 retained array-element bytes**. Existing coalescer arrays retain
at most **84840 bytes** on this workload. Neither figure includes headers, other
scratch or peak memory. The unused scalar scratch is deliberately retained:
this experiment adds no arrays and saves no retained array storage. Both sweep
variants allocate zero bytes in every warm-table sample and equal bytes in the
other scopes. Fresh-comparator scopes allocate **8 B more than the historical
build**, consistent with added scalar fields; this is an allocation delta, not
an independently measured object layout.

## What the profiles establish

Separate 20-second `dotnet-trace` sampled-thread-time runs selected warm stacks
under `TraverseReal` and preparation stacks under `PrepareAblation`. Four initial
calls are included; startup and unrelated threads are excluded by the selector.

| Warm profile | Memmove beneath CopyEdgesTo, sampled share |
|---|---:|
| Historical active baseline | 35.74% |
| Current active baseline | 33.51% |
| Scalar view | 0.00% |

The scalar copy disappears from the selected stacks, agreeing with the record
counters. These are **sampled thread-time shares, not CPU percentages**.
Inclusive shares overlap, and inlining/native transitions affect attribution.
They cannot be subtracted from benchmark time to predict a speedup. The
historical `CopyEdgesTo` exclusive share was only 0.62%; that frame alone does
not measure the full cost of geometry copying. No profile establishes the
precise cause of the observed regression or isolates the extra accessor cost.

The preparation profile shows `MemberwiseClone` at **61.69% exclusive** sampled
share and endpoint sorting at **22.93% inclusive**. That suggests snapshot
construction and sorting as possible subjects for another bounded experiment,
not a demonstrated improvement. Preserve snapshot isolation and measure the
complete operation before choosing any such change. Profiled elapsed time is
not used as benchmark evidence; trace and analysis-script hashes are recorded.

## Historical sensitivity and validation

The old binary was rerun with its unchanged active-pass matrix, interleaved
**old1, new1, new2, old2, old3, new3**. Each independent series has 480 samples;
samples are not pooled. Historical Guarded-active is method C, the current
baseline method B. Shipping source is unchanged, but compiled Winding DLL hashes
differ; the [manifest](../scanbeam-scalar-view-evidence.json) records identities.

| Direction | Scope | Old/current baseline | Old/view | Winding-normalized | Clipper-normalized |
|---|---|---:|---:|---:|---:|
| county-zones | warm-table | 1.004 | 0.971 | 0.974 | 0.971 |
| county-zones | fresh queries | 1.005 | 0.968 | 0.978 | 0.965 |
| county-zones | prepare + one | 1.001 | 0.969 | 0.979 | 0.963 |
| district-zones | warm-table | 1.009 | 0.986 | 0.994 | 0.994 |
| district-zones | fresh queries | 1.011 | 0.978 | 0.989 | 0.977 |
| district-zones | prepare + one | 1.015 | 0.989 | 1.004 | 0.987 |

Normalization for competitor C is `(old active / old C) / (new view / new C)`.
It is a sensitivity observation, not a correction for environment or JIT drift.
All six unnormalized historical comparisons also regress. Historical/view ranges
overlap for county fresh queries and district warm tables; the other four are
disjoint. The small normalized reversal in district preparation-plus-one does
not overturn the contemporaneous six-cell result.

Normal and hardware-intrinsics-disabled runs each passed **984 scalar-view
controls**, **7034 snapshot controls** (2977 copied, 4057 direct), and **51422
exact-rational/analytic geometry assertions**. Tests cover unequal edge counts,
role swaps, same-snapshot pairs, immutable source copies, separate-engine sharing,
raw/prepared/direct reuse, disabled filters, invalid input, fallback and recovery.
All **422 frozen calls certify**, with identical area/error-bound bits and every
existing diagnostic. Legacy validation remains byte-identical to the historical
file. Independent NTS area/coverage checks pass unchanged; NTS is not an exact
oracle. There is no accuracy improvement claim.

The positive evidence control passed. Twelve corrupted submissions were rejected:
row, order, median, output, binary hash, filter diagnostic, fallback record,
prepared payload, certificate, gap counter, pass counter and scalar-view counter.
Their JSON bytes remained intact and neither evidence nor summary was produced.

.NET 10.0.12; Microsoft Windows 10.0.26200; X64; Intel64 Family 6 Model 158
Stepping 12, GenuineIntel; 16 logical processors. Release,
`DOTNET_TieredCompilation=0`, three fresh processes, orders ABCD/CDAB/BDAC,
five calibrated samples per row and 32 rows per process. Profiles were collected
separately; no builds, profilers or competing benchmarks ran during timing.
Ordinary OS activity remains uncontrolled. Restore locked and build Release at
the measured revision, then use:

```text
dotnet <measured-dll> check-scalar-view artifacts/scanbeam-scalar-view-check
dotnet <measured-dll> benchmark-scalar-view artifacts/scanbeam-scalar-view <run 1..3> <revision>
dotnet <measured-dll> summarize-scalar-view artifacts/scanbeam-scalar-view
```

Repeat checks with `DOTNET_EnableHWIntrinsic=0`; use historical
`benchmark-active-passes` / `summarize-active-passes` in a separate directory.
Profile commands and provenance are in the manifest and protocol. This negative
result concerns this access pattern, workload and runtime; it does not prove
that copies are generally free or that all borrowed representations are slower.

Local archive: `artifacts/scanbeam-scalar-view-e4ffc10.zip`, **1581292 bytes,
49 members**; SHA-256
`968cd2804f6dca855ee2ee75d74bc7b18b7a579cfd410e8e1c596b742ce71a05`.
Raw samples and traces remain local; the archive is not publicly hosted.
