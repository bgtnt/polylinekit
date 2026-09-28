# Prepared regions: complete GIS and many-ring performance

**The current ring grouping uses 6.3–9.9% less warm time on all eight generated
cases than the preceding implementation, while GIS performance is essentially
unchanged.** On the complete Census workload, Core retains its advantage over the
measured Clipper2 adapter: **30–33% less warm time** and **24–25% less
preparation-plus-one time**. At 1,024 rings per operand, Clipper still takes less
time: Core uses **18–34% more warm time**. The improvements and remaining gaps both
matter; these results do not establish a universal winner.

## Revision, environment and operation

- Assessed implementation: [`6de6d38e9c86f5402d77346531a5b3917256e7c7`](https://github.com/bgtnt/polylinekit/commit/6de6d38e9c86f5402d77346531a5b3917256e7c7).
- Remeasured baseline: [`70291d2c0db4898d15e601eb11a972895338dafa`](https://github.com/bgtnt/polylinekit/commit/70291d2c0db4898d15e601eb11a972895338dafa).
- Measured 2026-09-28, 10:17:25–10:19:04 UTC, in six sequential processes: baseline 1, current 1, current 2, baseline 2, baseline 3, current 3.
- Windows `10.0.26200`, x64; .NET SDK `10.0.401`, runtime `.NET 10.0.12`.
- Recorded CPU identifier: `Intel64 Family 6 Model 158 Stepping 12, GenuineIntel`.
- Release .NET 10; `DOTNET_TieredCompilation=0`, default SIMD mode, hardware Vector256 available. This is not a scalar-versus-SIMD comparison.
- Comparators: Clipper2 **C# 2.0.0**, with reusable inputs and engine; NetTopologySuite **2.6.0**, using `OverlayNGRobust`.

The benchmark implementation and [protocol](PROTOCOL.md) have identical Git blobs
in both revisions. Each revision uses frozen binaries and rotates backend order
across its three processes. All builds finish before timing begins. Each revision
contributes **1,350 samples**; all **2,700 samples** are retained.

The complete series ran under a shared-host benchmark lock from
**10:17:16.9258618 to 10:19:04.6432581 UTC**. Other cooperating benchmark tasks
waited outside this slot. The helper serializes cooperating tasks through an
exclusive file writer lock; it does not prevent unrelated system activity or
uncoordinated programs. Its copied source and external START/DONE log are hashed
below. An earlier pilot and first uncoordinated series were excluded after another
task's benchmark was found overlapping the pilot. Instrumented diagnostics are
not used for the performance claims here.

The [earlier broad grouping report](https://github.com/bgtnt/polylinekit/blob/70291d2c0db4898d15e601eb11a972895338dafa/tests/PolylineKit.MultiRingChecks/RESULTS.md)
remains in Git history. It measures a different change and session; its ratios
are not multiplied by these results or mixed into this comparison.

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
| County → district | Warm | 14.031 | 20.161 | 48.184 | 1.437× | 3.434× |
| County → district | Preparation + one | 15.642 | 20.486 | 49.198 | 1.310× | 3.145× |
| District → county | Warm | 13.352 | 19.885 | 48.441 | 1.489× | 3.628× |
| District → county | Preparation + one | 15.098 | 20.226 | 49.680 | 1.340× | 3.290× |

Core uses **30.4–32.9% less warm-table time** and **23.6–25.4% less
preparation-plus-one time** than this Clipper adapter. Against its own remeasured
baseline, warm GIS changes range from **0.76% less to 0.04% more time**; the two
preparation-plus-one changes are within 0.12%. These are effectively flat results,
not evidence of an additional GIS speedup from the bucket change.

Preparation alone still costs more in Core: **1.628–1.630 ms**, versus
**0.659–0.664 ms** for Clipper and **0.437–0.441 ms** for NTS. Core copies and
validates immutable inputs and calls its general fill operation for denominators.
The other adapters exploit the independently validated polygon hierarchy to
calculate signed shell-minus-hole areas. That intentional cost difference is
included in preparation. Preparation allocations are substantially smaller in
Core, as the complete tables below show.

The directly measured preparation-plus-one scope already favors Core on these
GIS tables. Reuse removes preparation from subsequent tables. Scope medians are
independently measured and are not additive; do not infer setup cost by subtracting
warm time from preparation-plus-one time. These results cannot be used as a
before/after comparison with the older, differently admitted single-ring dataset.

## Effect of bounded leaf buckets

Each generated scenario compares one region with a copy shifted by `(0.25, 0.25)`.
Ring count is **per operand**. Squares are disjoint unit cells. The hole family
has a 2×2 shell and centered 1×1 oppositely oriented hole in each cell, hence half
as many components as rings. These valid polygon collections stress component
count; they do not model dense crossing or overlapping shell collections.

The preceding implementation already grouped rings through a temporary bounds
hierarchy and packed groups through member lists. This change stops the hierarchy
at leaves containing at most four ring bounds and visits each unordered pair only
once. It retains exact inclusive bounds checks for each admitted leaf member.
The direct-pair threshold remains 16 **total** rings: a comparison of two 16-ring
operands uses a hierarchy for 32 rings. The public API, fill semantics and numeric
area calculations are unchanged. See the
[implementation and worst-case costs](../../docs/regions.md#costs-and-numerical-limits).

The following table covers **every Core scenario and scope**. Each cell is
**baseline → current microseconds (baseline/current ratio)**. A ratio above one
favors the current implementation. Values are medians of three process medians.

| Scenario | Preparation: µs (ratio) | Warm: µs (ratio) | Preparation + one: µs (ratio) |
|---|---:|---:|---:|
| County → district | 1,625.062 → 1,629.925 (0.997×) | 14,025.800 → 14,030.900 (1.000×) | 15,659.200 → 15,641.500 (1.001×) |
| District → county | 1,634.188 → 1,628.112 (1.004×) | 13,454.300 → 13,352.300 (1.008×) | 15,096.500 → 15,098.500 (1.000×) |
| Squares / 16 | 20.849 → 20.462 (1.019×) | 17.983 → 16.850 (1.067×) | 39.112 → 37.865 (1.033×) |
| Shells + holes / 16 | 18.834 → 18.917 (0.996×) | 19.642 → 17.706 (1.109×) | 39.417 → 37.599 (1.048×) |
| Squares / 64 | 95.499 → 88.405 (1.080×) | 78.830 → 72.775 (1.083×) | 183.750 → 170.719 (1.076×) |
| Shells + holes / 64 | 93.833 → 86.307 (1.087×) | 96.920 → 88.120 (1.100×) | 204.394 → 188.666 (1.083×) |
| Squares / 256 | 431.741 → 405.059 (1.066×) | 401.259 → 371.816 (1.079×) | 854.906 → 797.375 (1.072×) |
| Shells + holes / 256 | 444.503 → 411.775 (1.079×) | 467.025 → 421.625 (1.108×) | 919.869 → 850.487 (1.082×) |
| Squares / 1024 | 1,955.750 → 1,831.275 (1.068×) | 1,784.125 → 1,655.725 (1.078×) | 3,695.425 → 3,509.675 (1.053×) |
| Shells + holes / 1024 | 2,011.062 → 1,840.125 (1.093×) | 2,073.787 → 1,884.612 (1.100×) | 4,025.250 → 3,705.975 (1.086×) |

All eight generated warm medians improve, using **6.3–9.9% less time**. The two
16-ring comparisons improve by **6.3%** for squares and **9.9%** for holes; the
64-ring hole comparison improves by **9.1%**. Preparation-plus-one improves by
**3.2–7.9%** across all generated cases. The 16-ring hole preparation median is
**0.44% higher**, while the other generated preparation medians are lower. Small
changes should be interpreted with the process ranges below. Baseline and current
process ranges are disjoint for all eight generated warm rows and all eight
preparation-plus-one rows.

At 1,024 rings, current Core warm time is **1.656 ms** for squares and **1.885 ms**
for holes, down **7.2%** and **9.1%** from the matched baseline. Clipper still needs
only **1.237 ms** and **1.595 ms**: Core takes **33.8%** and **18.1% more time**,
respectively. Including preparation, Core takes **97.5%** and **46.0% more time**
than Clipper. The remaining preparation and geometry costs are not removed by
smaller hierarchy leaves.

The 16-ring hole warm case is close to parity: Core **17.706 µs**, Clipper
**18.009 µs**, a nominal 1.7% Core advantage. Clipper is faster on the other seven
generated warm rows and on every generated preparation-plus-one row. NTS robust
overlay remains slower than Core on every measured query row, although its own
preparation is faster. The measured comparisons support this bounded improvement,
not a guarantee for arbitrary arrangements or input sizes.

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
| County → district | 1,629.925 [1,623.513–1,636.225]; **233,144 B** | 664.400 [658.913–764.150]; **1,133,024 B** | 436.594 [394.050–446.622]; **585,248 B** |
| District → county | 1,628.112 [1,626.950–1,633.338]; **233,144 B** | 659.244 [658.144–673.825]; **1,133,024 B** | 440.503 [425.300–440.594]; **585,248 B** |
| Squares / 16 | 20.462 [20.394–20.879]; **4,552 B** | 8.859 [8.652–8.948]; **16,896 B** | 15.528 [15.070–16.747]; **27,160 B** |
| Shells + holes / 16 | 18.917 [18.792–19.451]; **4,552 B** | 8.827 [8.363–8.929]; **16,896 B** | 13.458 [13.017–24.681]; **23,192 B** |
| Squares / 64 | 88.405 [88.234–89.852]; **15,304 B** | 29.994 [29.837–30.214]; **62,400 B** | 61.284 [59.472–64.397]; **105,496 B** |
| Shells + holes / 64 | 86.307 [85.088–87.536]; **15,304 B** | 30.337 [30.304–30.887]; **62,400 B** | 51.522 [49.918–53.011]; **89,624 B** |
| Squares / 256 | 405.059 [401.897–421.041]; **58,312 B** | 118.374 [117.145–120.820]; **243,840 B** | 256.817 [248.592–266.386]; **418,840 B** |
| Shells + holes / 256 | 411.775 [407.434–419.169]; **58,312 B** | 118.276 [117.086–124.888]; **243,840 B** | 207.180 [205.678–208.798]; **355,352 B** |
| Squares / 1024 | 1,831.275 [1,802.400–1,839.588]; **230,344 B** | 480.103 [475.438–494.766]; **969,024 B** | 1,171.275 [1,131.787–1,180.750]; **1,672,216 B** |
| Shells + holes / 1024 | 1,840.125 [1,835.487–1,841.300]; **230,344 B** | 483.881 [478.747–487.966]; **969,024 B** | 978.675 [961.056–986.306]; **1,418,264 B** |

### Warm comparisons

One prepared session; the complete pair traversal is repeated.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 14,030.900 [14,024.400–14,085.600]; **0 B** | 20,160.700 [20,093.600–20,301.800]; **878,096 B** | 48,183.500 [47,991.400–48,420.500]; **36,271,104 B** |
| District → county | 13,352.300 [13,291.800–13,527.800]; **0 B** | 19,885.100 [19,671.500–19,923.400]; **878,096 B** | 48,440.700 [48,225.600–48,964.700]; **36,271,104 B** |
| Squares / 16 | 16.850 [16.604–17.080]; **0 B** | 13.237 [12.892–14.011]; **7,512 B** | 413.141 [403.462–426.553]; **325,152 B** |
| Shells + holes / 16 | 17.706 [17.527–17.776]; **0 B** | 18.009 [17.880–19.028]; **10,904 B** | 487.166 [477.772–489.231]; **359,096 B** |
| Squares / 64 | 72.775 [72.595–73.351]; **0 B** | 58.569 [57.584–62.887]; **23,640 B** | 1,777.612 [1,719.675–1,835.250]; **1,265,936 B** |
| Shells + holes / 64 | 88.120 [86.904–88.711]; **0 B** | 83.067 [82.712–84.540]; **33,368 B** | 2,064.650 [2,027.775–2,094.787]; **1,475,608 B** |
| Squares / 256 | 371.816 [361.006–372.666]; **0 B** | 265.975 [263.191–268.094]; **82,008 B** | 8,400.000 [7,844.950–8,531.350]; **5,067,136 B** |
| Shells + holes / 256 | 421.625 [421.044–426.425]; **0 B** | 350.547 [350.459–356.438]; **112,856 B** | 9,047.800 [8,784.650–9,168.400]; **7,085,560 B** |
| Squares / 1024 | 1,655.725 [1,655.412–1,658.062]; **0 B** | 1,237.300 [1,197.338–1,249.963]; **303,192 B** | 45,743.800 [44,036.900–45,930.800]; **20,329,248 B** |
| Shells + holes / 1024 | 1,884.612 [1,856.138–1,901.737]; **0 B** | 1,595.162 [1,572.737–1,595.850]; **411,096 B** | 76,313.000 [75,878.400–76,853.400]; **47,277,216 B** |

### Preparation plus one comparison table

A fresh session and one complete traversal; process, JIT and thread-local workspaces are already warm. This is not cold-process timing.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 15,641.500 [15,630.500–19,438.200]; **233,080 B** | 20,486.200 [20,332.000–20,592.800]; **2,044,496 B** | 49,197.900 [48,976.000–49,242.200]; **36,856,336 B** |
| District → county | 15,098.500 [15,063.800–16,961.300]; **233,080 B** | 20,226.100 [19,884.500–20,364.000]; **2,042,680 B** | 49,680.400 [49,541.200–50,028.100]; **36,856,336 B** |
| Squares / 16 | 37.865 [36.732–38.336]; **4,488 B** | 22.155 [21.639–22.588]; **33,976 B** | 425.344 [421.184–451.419]; **352,248 B** |
| Shells + holes / 16 | 37.599 [36.901–38.144]; **4,488 B** | 29.256 [28.549–29.366]; **40,744 B** | 499.863 [488.978–506.147]; **382,992 B** |
| Squares / 64 | 170.719 [167.692–172.484]; **15,240 B** | 90.908 [88.791–91.470]; **123,064 B** | 1,749.312 [1,726.325–1,762.025]; **1,371,368 B** |
| Shells + holes / 64 | 188.666 [186.622–190.773]; **15,240 B** | 111.737 [110.277–113.205]; **145,640 B** | 2,096.025 [2,078.825–2,132.988]; **1,568,240 B** |
| Squares / 256 | 797.375 [788.712–801.763]; **58,248 B** | 390.297 [380.453–392.247]; **471,352 B** | 8,096.000 [7,847.500–8,233.750]; **5,485,912 B** |
| Shells + holes / 256 | 850.487 [846.719–870.931]; **58,248 B** | 485.163 [484.812–496.850]; **552,424 B** | 9,037.800 [8,705.300–9,190.700]; **7,453,136 B** |
| Squares / 1024 | 3,509.675 [3,491.650–3,551.975]; **230,280 B** | 1,777.450 [1,717.588–1,837.312]; **1,849,528 B** | 49,381.100 [46,205.600–50,726.900]; **22,001,400 B** |
| Shells + holes / 1024 | 3,705.975 [3,684.250–3,724.225]; **230,280 B** | 2,538.575 [2,454.375–2,543.225]; **2,156,136 B** | 77,706.300 [77,137.900–87,300.300]; **48,744,568 B** |

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
output directory. On a shared host, coordinate all benchmark jobs through a common
lock or reserve an otherwise quiet interval. Run the complete series together:

```powershell
./tests/PolylineKit.MultiRingChecks/compare-grouping.ps1 `
    -BaselineRevision 70291d2c0db4898d15e601eb11a972895338dafa `
    -Output artifacts/my-paired-region-run
```

The [helper](compare-grouping.ps1) verifies identical benchmark and protocol Git
blobs, archives both source sets, and uses locked dependency restores with explicit
build revision identities. It builds the baseline from its extracted archive and
the current revision from the clean checkout, freezes both runners, checks their
hashes, records process windows, and validates each revision's three-process
summary. It rejects dirty or changed candidate source, existing output, failed
processes and changed frozen binaries. It does not itself acquire a host-wide
benchmark lock; this assessment wrapped the complete invocation in the cooperating
host's `Run-HostBenchmark.ps1`. That local helper and log are retained below. The
[protocol](PROTOCOL.md) describes the operations and individual repetitions.

The two current `packages.lock.json` files have CRLF line endings in the measured
checkout and LF in its Git archive; their JSON contents are identical. Recorded
source hashes describe the measured checkout bytes, not normalized archive bytes.

The assessed source, input snapshots, benchmark and comparison helper are public.
Raw files remain in the author's ignored local
`artifacts/paired-ring-buckets-coordinated-20260928/` directory; **they are not
published downloads**. Hashes identify retained evidence rather than providing
access. The host log is in its parent `artifacts/` directory. Re-run the comparison
for an independent result.

| Local evidence file | SHA-256 |
|---|---|
| baseline-run-1.json | `ae591e66ebf3df086c2cd07927599e26b5c2bdae80ae74d2adb2ce3eb596c152` |
| baseline-run-2.json | `568f5a0fb68e469fd561889b5a5d901b771065b447bfb89db55793a8d1c4f1a3` |
| baseline-run-3.json | `245f831474b47453395b90b5c7d4dc8a54fa7757321912b0426d2f2fdf1e1f85` |
| baseline-summary.json | `f27717a83a89c8da5491e5d05e3ce4598802de64589304214381c1efe123b561` |
| candidate-run-1.json | `16ec201dac29762862e1879734317ecd034c5b522b16b5a538a99c664691a816` |
| candidate-run-2.json | `a473c1968b270d7fbd51333ee02ca88bc5b5bd133d49545fd2188006890824ba` |
| candidate-run-3.json | `7686ea3bfbda18045679d06d9316052d54b595d9aef8eb7d4deed94d46ee5ff7` |
| candidate-summary.json | `abc4e49b89171040cf903894c4a2fde215b250be91fc02169d518e1dac643e56` |
| comparison.json | `f57279d04a35006b62f0dc34e3935e1d244896ecc9e32577570b96313e21cc58` |
| execution.json | `7d5cb5f7e56956322203ec7a34908b2ddd6123a66beaa4d2c5d96bdf634862c2` |
| baseline-source.zip | `f689bf16e3df8542f5491f0992ea19ec72da224c13be6765e2ee02621c667729` |
| candidate-source.zip | `2777ac79c78e8701e80e515a646342b6f3e4cb4dd89c45d59140af62c790ddf8` |
| Run-HostBenchmark.ps1 | `c3c7b888f10c3d86bd7f4572d4c21cb44cc3f4688a313c1a4d2285d1d9af5b69` |
| ../ring-buckets-coordinated-host.log | `6e889667f82c035b5b6b1d1f732a5fbc18aeac56da48680057f95bef7f9b5443` |

| Assessed source/binary | SHA-256 |
|---|---|
| RegionBenchmarks.cs (both) | `8830e0dacaca9f2b2c4dc312f4d147d5231828efff3683ae656009c5f8deba30` |
| PROTOCOL.md (both) | `c60902cd3337bcb8e8b53ee2f9610ed9f0f584525fa1ef14b234a4bb9ce36bf0` |
| Baseline PolylineKit.Experiments.dll | `9a9ef00a90a93c17699884f1eff48a20f4f4d35d56c80b3f3ddb3bf0f6f2c2fa` |
| Baseline PolylineKit.Winding.dll | `555ae1147bd98df8a22afeae80a414bf9c4b80a0ce0d06c865aaef03efefb191` |
| Current PolylineKit.Experiments.dll | `06db2f66ad4e56777259b2a9ad209323b262f7b760e480cfd1a4bc4314fd8328` |
| Current PolylineKit.Winding.dll | `296f5a42156903583770e0af87c576b90beda1b437c386be1ea35c236d9a2548` |
| Clipper2Lib.dll (both) | `e9bf01e01f494ef6072cd46c792ed4f3004288a92b1e98c1f44931e558b8a5d0` |
| NetTopologySuite.dll (both) | `98fa6bae8b75b5f9445236679883fa27750d990aec11e985b85b57a30c37a858` |

All six process windows are disjoint, fall inside the reserved host slot, and have exit code 0. Times below are UTC on 2026-09-28.

| Order | Revision | Run | Start | End |
|---:|---|---:|---|---|
| 1 | baseline | 1 | 10:17:25.571 | 10:17:42.054 |
| 2 | candidate | 1 | 10:17:42.261 | 10:17:58.610 |
| 3 | candidate | 2 | 10:17:58.809 | 10:18:14.954 |
| 4 | baseline | 2 | 10:18:15.159 | 10:18:31.503 |
| 5 | baseline | 3 | 10:18:31.704 | 10:18:47.881 |
| 6 | candidate | 3 | 10:18:48.073 | 10:19:04.030 |

Within each revision, binary, source, protocol, input and accuracy identities agree
across runs. Both summaries validate the complete scope matrix, candidate counts,
per-batch medians and stable digests. No performance run was made while writing
this report.

This compares intersection and coverage through one .NET backend per library.
It does not compare Clipper C++, every NTS strategy, output-geometry APIs,
concurrent throughput, maximum ring counts or dense crossing workloads. It
supports the measured prepared-region use case, records remaining costs, and
neither finalizes the alpha API nor demonstrates external production adoption.
