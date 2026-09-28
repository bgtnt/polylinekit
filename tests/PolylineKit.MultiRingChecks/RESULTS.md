# Prepared regions: complete GIS and many-ring performance

**Core remains faster than the measured Clipper2 adapter on the complete Census
workload. Spatial grouping substantially reduces the large generated cases, but
does not close their Clipper gap and regresses several smaller cases.** On Census
tables, Core takes **28–33% less warm time** and **22–25% less preparation-plus-one
time** than Clipper. At 1,024 rings per operand, warm Core comparisons are
**3.53–5.10× faster than the remeasured previous implementation**, while Clipper
remains **1.31–1.44× faster than Core**. These are different comparisons; neither
implies a universal speed advantage.

## Revision, environment and operation

- Assessed implementation: [`78126b22b9f75ad79df6f7661ef7e9ec5cb8d76d`](https://github.com/bgtnt/polylinekit/commit/78126b22b9f75ad79df6f7661ef7e9ec5cb8d76d).
- Remeasured baseline: [`9cb08131e42ba84cf96190db20c60ae893afa969`](https://github.com/bgtnt/polylinekit/commit/9cb08131e42ba84cf96190db20c60ae893afa969).
- Measured 2026-09-28, 07:07:16–07:08:50 UTC, in six sequential processes: baseline 1, current 1, current 2, baseline 2, baseline 3, current 3.
- Windows `10.0.26200`, x64; .NET SDK `10.0.401`, runtime `.NET 10.0.12`.
- Recorded CPU identifier: `Intel64 Family 6 Model 158 Stepping 12, GenuineIntel`.
- Release .NET 10; `DOTNET_TieredCompilation=0`, default SIMD mode, hardware Vector256 available. This is not a scalar-versus-SIMD comparison.
- Comparators: Clipper2 **C# 2.0.0**, with reusable inputs and engine; NetTopologySuite **2.6.0**, using `OverlayNGRobust`.

The benchmark implementation and [protocol](PROTOCOL.md) have identical Git blobs
in both revisions. Each revision uses frozen binaries and rotates backend order
across its three processes. All builds finish before timing begins. Each revision
contributes **1,350 samples**; this report uses all **2,700 samples** and does not
combine the new baseline with the earlier historical measurements.
The [earlier report](https://github.com/bgtnt/polylinekit/blob/18eb9ab98da4bdc41343f49e873ddc35f0e299f8/tests/PolylineKit.MultiRingChecks/RESULTS.md)
remains available in Git history.

All 100 counties and 14 congressional districts participate, including components
and the hole excluded from the older single-ring benchmark. Each direction
traverses 1,400 pairs: 285 inclusive bounds candidates and 1,115 bounds rejections.
All adapters use the same common translation of EPSG:5070 metre coordinates and
the same linear outer pair traversal, without an outer spatial index. This is the
complete population of an already observed dataset, not an independent geographic
holdout.

Each query invocation consumes intersection-area and first-region coverage sums.
It constructs no output table and caches no pairwise results. Clipper uses an
explicit `1e-6` metre grid and sums result contours; Core returns numeric areas
from the original translated doubles. NTS retains Polygon/MultiPolygon hierarchy.
Generated coordinates use the same grid scale in their own units.

## Complete GIS result

Ratios are comparator time / current Core time; above one favors Core. Times are
milliseconds per complete directional table, including bounds rejections.

| Direction | Scope | Core ms | Clipper ms | NTS ms | Clipper/Core | NTS/Core |
|---|---|---:|---:|---:|---:|---:|
| County → district | Warm | 13.625 | 19.006 | 44.961 | 1.395× | 3.300× |
| County → district | Preparation + one | 15.206 | 19.393 | 47.047 | 1.275× | 3.094× |
| District → county | Warm | 13.051 | 19.451 | 46.020 | 1.490× | 3.526× |
| District → county | Preparation + one | 14.409 | 19.281 | 47.191 | 1.338× | 3.275× |

Core uses **28.3–32.9% less warm-table time** and **21.6–25.3% less
preparation-plus-one time** than this Clipper adapter. Against its own remeasured
baseline, the GIS improvement is modest: approximately **1.5–2.0% less warm time**
and **2.4–2.8% less preparation-plus-one time**. The large component-count speedups
below should not be attributed to these GIS tables. Unchanged comparator medians
also vary between the paired runs, so the small GIS changes do not establish a
broad optimization gain; the existing GIS advantage is retained.

Preparation alone still costs more in Core: **1.589–1.594 ms**, versus
**0.597–0.602 ms** for Clipper and **0.399–0.403 ms** for NTS. Core copies and
validates immutable inputs and calls its general fill operation for denominators.
The other adapters exploit the independently validated polygon hierarchy to
calculate signed shell-minus-hole areas. That intentional cost difference is
included in preparation. Preparation allocations are substantially smaller in
Core, as the complete tables below show.

The directly measured preparation-plus-one scope already favors Core on these
GIS tables. Reuse removes preparation from subsequent tables. Scope medians are
independently measured and are not additive; do not infer setup cost by subtracting
warm time from preparation-plus-one time. These results also cannot be used as a
before/after comparison with the older, differently admitted single-ring dataset.

## Effect of spatial grouping

Each generated scenario compares one region with a copy shifted by `(0.25, 0.25)`.
Ring count is **per operand**. Squares are disjoint unit cells. The hole family
has a 2×2 shell and centered 1×1 oppositely oriented hole in each cell, hence half
as many components as rings. These valid polygon collections stress component
count; they do not model dense crossing or overlapping shell collections.

The change uses a temporary bounds hierarchy for larger inputs and packs each
group through its member list, avoiding the old repeated full-ring scans. It also
skips isolated single-operand groups when only intersection is requested. The
[implementation and worst-case costs](../../docs/regions.md#costs-and-numerical-limits)
remain explicit: preparation does not cache a persistent spatial index, and the
winding-seed and crossing work within interacting groups is unchanged.

The following table covers **every Core scenario and scope**. Each cell is
**baseline → current microseconds (baseline/current ratio)**. A ratio above one
favors the current implementation. Values are medians of three process medians.

| Scenario | Preparation: µs (ratio) | Warm: µs (ratio) | Preparation + one: µs (ratio) |
|---|---:|---:|---:|
| County → district | 1,587.775 → 1,589.225 (0.999×) | 13,898.800 → 13,625.200 (1.020×) | 15,573.200 → 15,205.900 (1.024×) |
| District → county | 1,564.625 → 1,593.825 (0.982×) | 13,248.700 → 13,051.100 (1.015×) | 14,827.700 → 14,409.400 (1.029×) |
| Squares / 16 | 21.708 → 19.607 (1.107×) | 16.171 → 17.522 (0.923×) | 36.216 → 36.256 (0.999×) |
| Shells + holes / 16 | 18.622 → 17.832 (1.044×) | 16.498 → 18.521 (0.891×) | 37.177 → 35.658 (1.043×) |
| Squares / 64 | 98.802 → 90.954 (1.086×) | 90.322 → 73.873 (1.223×) | 191.767 → 172.911 (1.109×) |
| Shells + holes / 64 | 90.279 → 94.077 (0.960×) | 85.209 → 95.322 (0.894×) | 178.294 → 200.309 (0.890×) |
| Squares / 256 | 665.888 → 428.794 (1.553×) | 752.156 → 382.553 (1.966×) | 1,463.525 → 812.419 (1.801×) |
| Shells + holes / 256 | 528.953 → 425.534 (1.243×) | 636.225 → 449.641 (1.415×) | 1,142.006 → 887.931 (1.286×) |
| Squares / 1024 | 7,123.750 → 1,866.025 (3.818×) | 8,587.800 → 1,682.475 (5.104×) | 15,256.900 → 3,538.400 (4.312×) |
| Shells + holes / 1024 | 4,889.650 → 1,900.088 (2.573×) | 6,832.600 → 1,934.675 (3.532×) | 11,736.300 → 3,817.125 (3.075×) |

At 1,024 rings, Core's warm comparisons improve from **8.588 to 1.682 ms** for
squares and **6.833 to 1.935 ms** for shells with holes: **5.10×** and **3.53×**.
Including preparation, the improvements are **4.31×** and **3.07×**. Clipper still
needs only **1.168 ms** and **1.479 ms** for those warm comparisons, so it remains
**1.44×** and **1.31×** faster. Including preparation, Clipper's remaining advantage
is **2.11×** and **1.72×**. Faster grouping improves scalability here without making
Core the faster choice for these generated collections.

Several smaller cases regress. Warm comparisons at 16 rings take **8.4% more time**
for squares and **12.3% more** for holes. At 64 rings, the hole case takes **11.9%
more warm time** and **12.3% more preparation-plus-one time**; its preparation
median is also **4.2% higher**. The three warm regressions have disjoint baseline
and current process ranges; the 64-ring hole preparation and preparation-plus-one
ranges overlap. The 16-square preparation-plus-one result differs by only 0.1%.
The hierarchy and changed code
have costs as well as benefits; there is no universal improvement claim. The
full current ranges below preserve that variation.

Clipper remains faster in every generated current warm and preparation-plus-one
row. NTS robust overlay remains slower than Core in every measured query row,
although its own preparation is faster. Changing these rankings would require
further evidence, not extrapolation from the large-ring improvement.

## All current measured scopes

Each cell is **median microseconds [minimum–maximum of three process medians];
managed bytes per invocation**. Each process median uses five batches. Allocation
medians are identical across the three current processes for every row. The
complete current matrix contains 1,350 samples, with no discarded rows. Digest
consumption and per-iteration equality checks are included for all backends.
Ranges describe this run; they are not confidence intervals or latency percentiles.

### Preparation

Both complete catalogues, backend inputs and own-area denominators; no pair queries.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 1,589.225 [1,583.875–1,596.412]; **233,144 B** | 597.419 [580.666–631.956]; **1,133,024 B** | 398.656 [394.919–407.116]; **585,248 B** |
| District → county | 1,593.825 [1,570.862–1,598.912]; **233,144 B** | 601.894 [593.331–612.763]; **1,133,024 B** | 403.372 [399.381–425.625]; **585,248 B** |
| Squares / 16 | 19.607 [19.319–19.626]; **4,552 B** | 7.565 [7.449–7.901]; **16,896 B** | 14.624 [14.308–15.405]; **27,160 B** |
| Shells + holes / 16 | 17.832 [17.789–18.030]; **4,552 B** | 7.597 [7.413–7.615]; **16,896 B** | 12.198 [11.988–12.375]; **23,192 B** |
| Squares / 64 | 90.954 [88.966–93.415]; **15,304 B** | 26.956 [26.762–27.251]; **62,400 B** | 55.073 [53.693–56.742]; **105,496 B** |
| Shells + holes / 64 | 94.077 [84.098–95.243]; **15,304 B** | 28.018 [27.330–28.811]; **62,400 B** | 47.198 [46.766–49.652]; **89,624 B** |
| Squares / 256 | 428.794 [416.303–432.978]; **58,312 B** | 105.336 [105.311–112.197]; **243,840 B** | 230.791 [227.583–235.441]; **418,840 B** |
| Shells + holes / 256 | 425.534 [422.916–450.972]; **58,312 B** | 106.986 [106.845–107.817]; **243,840 B** | 198.141 [193.480–199.967]; **355,352 B** |
| Squares / 1024 | 1,866.025 [1,852.438–1,876.350]; **230,344 B** | 440.116 [435.938–447.422]; **969,024 B** | 1,038.400 [1,031.225–1,074.869]; **1,672,216 B** |
| Shells + holes / 1024 | 1,900.088 [1,891.000–1,906.412]; **230,344 B** | 437.059 [436.897–448.672]; **969,024 B** | 871.519 [861.438–925.569]; **1,418,264 B** |

### Warm comparisons

One prepared session; the complete pair traversal is repeated.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 13,625.200 [13,389.000–13,805.100]; **0 B** | 19,005.600 [18,955.000–19,913.500]; **878,096 B** | 44,960.600 [44,262.100–45,725.600]; **36,271,104 B** |
| District → county | 13,051.100 [12,869.400–13,268.000]; **0 B** | 19,450.500 [19,202.200–19,474.800]; **878,096 B** | 46,019.700 [44,935.900–47,724.900]; **36,271,104 B** |
| Squares / 16 | 17.522 [17.480–17.670]; **0 B** | 12.935 [12.298–14.475]; **7,512 B** | 398.119 [388.697–411.594]; **325,152 B** |
| Shells + holes / 16 | 18.521 [17.846–18.687]; **0 B** | 17.579 [16.532–17.728]; **10,904 B** | 455.609 [437.706–460.506]; **359,096 B** |
| Squares / 64 | 73.873 [73.468–76.938]; **0 B** | 54.217 [53.857–56.460]; **23,640 B** | 1,647.213 [1,621.000–1,692.412]; **1,265,936 B** |
| Shells + holes / 64 | 95.322 [91.505–95.453]; **0 B** | 82.040 [80.630–85.765]; **33,368 B** | 1,969.100 [1,921.700–2,027.100]; **1,475,608 B** |
| Squares / 256 | 382.553 [378.056–402.547]; **0 B** | 264.289 [263.623–266.406]; **82,008 B** | 7,509.800 [7,409.700–7,609.750]; **5,067,136 B** |
| Shells + holes / 256 | 449.641 [445.703–466.947]; **0 B** | 347.769 [334.425–372.969]; **112,856 B** | 8,534.350 [8,278.300–8,548.450]; **7,085,560 B** |
| Squares / 1024 | 1,682.475 [1,651.575–1,693.025]; **0 B** | 1,167.969 [1,148.612–1,228.781]; **303,192 B** | 39,705.200 [39,487.500–40,896.000]; **20,329,248 B** |
| Shells + holes / 1024 | 1,934.675 [1,924.500–1,977.075]; **0 B** | 1,479.125 [1,474.450–1,520.463]; **411,096 B** | 75,878.900 [69,814.600–75,988.900]; **47,277,216 B** |

### Preparation plus one comparison table

A fresh session and one complete traversal; process, JIT and thread-local workspaces are already warm. This is not cold-process timing.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 15,205.900 [15,156.100–15,515.300]; **233,080 B** | 19,392.700 [19,088.500–20,396.100]; **2,044,496 B** | 47,046.500 [45,793.600–47,988.600]; **36,856,336 B** |
| District → county | 14,409.400 [14,362.000–14,558.200]; **233,080 B** | 19,280.700 [19,069.100–20,169.300]; **2,042,680 B** | 47,191.300 [46,457.400–47,796.600]; **36,856,336 B** |
| Squares / 16 | 36.256 [36.154–37.026]; **4,488 B** | 21.406 [19.907–23.014]; **33,976 B** | 401.556 [397.819–403.312]; **352,248 B** |
| Shells + holes / 16 | 35.658 [34.908–35.745]; **4,488 B** | 25.882 [24.696–26.178]; **40,744 B** | 474.719 [458.703–478.334]; **382,992 B** |
| Squares / 64 | 172.911 [165.713–174.225]; **15,240 B** | 86.970 [84.356–88.532]; **123,064 B** | 1,631.375 [1,630.450–1,721.725]; **1,371,368 B** |
| Shells + holes / 64 | 200.309 [191.302–203.153]; **15,240 B** | 116.409 [114.786–119.397]; **145,640 B** | 1,957.525 [1,915.075–1,957.862]; **1,568,240 B** |
| Squares / 256 | 812.419 [797.587–841.081]; **58,248 B** | 373.431 [370.962–379.225]; **471,352 B** | 7,713.700 [7,577.450–7,988.850]; **5,485,912 B** |
| Shells + holes / 256 | 887.931 [857.750–946.388]; **58,248 B** | 480.741 [449.800–494.675]; **552,424 B** | 8,609.200 [8,502.750–8,612.300]; **7,453,136 B** |
| Squares / 1024 | 3,538.400 [3,507.775–3,573.075]; **230,280 B** | 1,680.737 [1,609.775–1,695.525]; **1,849,528 B** | 42,006.100 [41,602.800–42,375.100]; **22,001,400 B** |
| Shells + holes / 1024 | 3,817.125 [3,783.475–3,838.075]; **230,280 B** | 2,218.512 [2,203.350–2,320.475]; **2,156,136 B** | 76,640.900 [76,104.300–76,869.400]; **48,744,568 B** |

## Correctness and allocations

Before timing, every process validates all pairs and own areas under NonZero and
EvenOdd filling for all three adapters: **60 scenario/backend/fill groups**.
All recorded accuracy results are identical across processes **and across the
two revisions**. Every generated case agrees exactly with the dyadic analytic
own-area and intersection values; coverage agrees with the rounded rational
reference in the recorded doubles. This is a fixture result, not an
exact-arithmetic guarantee.

| GIS agreement against NTS | Core maximum | Clipper maximum | Fixed budget |
|---|---:|---:|---:|
| Own area, m² | 3.81469727e-05 | 0.0228462219 | 1 m² |
| Intersection, m² | 2.86102295e-06 | 0.0262260437 | 1 m² |
| Coverage fraction | 1.99840144e-15 | 2.24081864e-11 | 1e-8 |

NTS is an independent floating-point comparison, not an exact GIS oracle. Clipper's
quantization is explicit. Smaller disagreement with NTS in this population does
not establish universal numerical superiority. Candidate membership agrees across
all adapters despite the quantization.

Core allocation medians are **unchanged from the baseline in all 30 scope rows**.
All **150 current Core warm batches** report **0 B per invocation** on the executing
managed thread. Preparation and fresh sessions allocate, as shown above. These
figures do not measure native allocations, retained inputs or peak memory. First
use, workspace growth, nesting and oversized calls can allocate. Core's per-thread
cached engine array payload remains limited to 4 MiB, now including the bounds
hierarchy and component workspace. Array headers and active/peak memory are outside
that cap; zero warm allocations here are not a general API guarantee.

## Reproduction and evidence

Use a clean checkout of the assessed commit, the .NET 10 SDK, and a new ignored
output directory. Keep other builds, benchmarks and heavy workloads stopped:

```powershell
./tests/PolylineKit.MultiRingChecks/compare-grouping.ps1 -Output artifacts/my-paired-region-run
```

The [helper](compare-grouping.ps1) pins the baseline, verifies identical benchmark
and protocol Git blobs, archives both source sets, and uses locked dependency
restores with explicit build revision identities. It builds the baseline from
its extracted archive and the current revision from the clean checkout, freezes
both runners, checks their hashes, records process windows, and runs the existing
summary validator separately for both revisions. It rejects dirty or changed
candidate source, existing output, failed processes and changed frozen binaries.
The [protocol](PROTOCOL.md) also describes each operation and individual repetition.

The two current `packages.lock.json` files have CRLF line endings in the measured
checkout and LF in its Git archive; their JSON contents are identical. Recorded
source hashes describe the measured checkout bytes, not normalized archive bytes.

The source, input snapshots, benchmark and helper are public. Raw files remain in
the author's ignored local `artifacts/paired-ring-grouping-20260928/` directory;
**they are not published downloads**. The following hashes identify retained
evidence rather than providing access. Re-run the helper for an independent result.

| Local evidence file | SHA-256 |
|---|---|
| baseline-run-1.json | `02cf9dcdbbaa0ef79e96b97224d95cd2821063c5762044ec83674bd5841ac49e` |
| baseline-run-2.json | `a24e0271a3495078dba906b6cd28895cbff0bba451ac36d2726f28e160a79109` |
| baseline-run-3.json | `eae15f840fcb0977f97a30541283bca711abee914b982f433afcb014bb64184d` |
| baseline-summary.json | `bb1f44cac819fc127c9a71bc1d6f04db265d80f37740d62a4e127ab8035f23e9` |
| candidate-run-1.json | `bc9cc68f28a11cee302ce4fbf479b3e68f3f941ae2393d67dd421198ede4d8af` |
| candidate-run-2.json | `5d693e1e09a560e5d4bcb81843c6e75830fbc62da811e6eff3d6d29b1176215d` |
| candidate-run-3.json | `047de99e0e7598451034500b54ccdff41842a7630f6de7fd349379ad3c55d5c6` |
| candidate-summary.json | `59ec2288db182850b0ffd658cfd26a2d4588293b1f9a4e7879083a41e61a5b0d` |
| comparison.json | `555ea616ef6d43321a25f73b0d1f474c95d723b2f1623f45e5d36bbf0044ac59` |
| execution.json | `e35bad98fe5d8a6f53a781f7b0efb656518a9fcb4a0dfe4bec92170f1d75b51a` |
| baseline-source.zip | `3a398d7f576b5453253327180bc44f85913bc075525c6d8b186de86e8406c40a` |
| candidate-source.zip | `b39175552bbb54a5cc59c02b5283fd8eaaab1ee2e5fa9f78ec89b06717a74ead` |

| Assessed source/binary | SHA-256 |
|---|---|
| RegionBenchmarks.cs (both) | `8830e0dacaca9f2b2c4dc312f4d147d5231828efff3683ae656009c5f8deba30` |
| PROTOCOL.md (both) | `c60902cd3337bcb8e8b53ee2f9610ed9f0f584525fa1ef14b234a4bb9ce36bf0` |
| Baseline PolylineKit.Experiments.dll | `5fa2d2449178346b758764c19a979e8825a08f426f7d5ef2884ff31796e3ad9e` |
| Baseline PolylineKit.Winding.dll | `e202f7b38690ec155275d65f6f5d939eda367ed998114dc67fcee1a702d1dbaa` |
| Current PolylineKit.Experiments.dll | `421b476053bdb080be2ab504f4f548adb8d6c0beadcaa7f8e11c71f4be631cb0` |
| Current PolylineKit.Winding.dll | `ca38bcc5fe04bdab2a824bf92fa9d4b8d441cc7ea82d03f14ab2778d07d1d9ed` |
| Clipper2Lib.dll (both) | `e9bf01e01f494ef6072cd46c792ed4f3004288a92b1e98c1f44931e558b8a5d0` |
| NetTopologySuite.dll (both) | `98fa6bae8b75b5f9445236679883fa27750d990aec11e985b85b57a30c37a858` |

All six process windows are disjoint and have exit code 0. Times below are UTC on 2026-09-28.

| Order | Revision | Run | Start | End |
|---:|---|---:|---|---|
| 1 | baseline | 1 | 07:07:16.650 | 07:07:32.018 |
| 2 | candidate | 1 | 07:07:32.202 | 07:07:47.688 |
| 3 | candidate | 2 | 07:07:47.874 | 07:08:03.303 |
| 4 | baseline | 2 | 07:08:03.480 | 07:08:19.090 |
| 5 | baseline | 3 | 07:08:19.268 | 07:08:34.745 |
| 6 | candidate | 3 | 07:08:34.927 | 07:08:50.592 |

Within each revision, binary, source, protocol, input and accuracy identities agree
across runs. Both summaries validate the complete scope matrix, candidate counts,
per-batch medians and stable digests. No performance run was made while writing
this report.

This compares intersection and coverage through one .NET backend per library.
It does not compare Clipper C++, every NTS strategy, output-geometry APIs,
concurrent throughput, maximum ring counts or dense crossing workloads. It
supports the measured prepared-region use case, records remaining costs, and
neither finalizes the alpha API nor demonstrates external production adoption.
