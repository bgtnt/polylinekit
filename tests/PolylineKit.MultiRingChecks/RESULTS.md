# Prepared regions: complete GIS and many-ring performance

**The prepared-region API is useful for the complete measured GIS workload, but its many-component costs
remain a clear limitation.** Core is 1.40–1.43× as fast as the reused-input Clipper2 adapter on warm Census
tables and 1.25–1.30× on preparation plus one table. With 1,024 generated rings, Clipper is instead
4.73–7.28× as fast on warm comparisons. NTS robust overlay is slower than Core on every measured query
workload. These conclusions apply to the operations, inputs and adapters below; they do not establish a
universal winner.

## Revision, environment and operation

- Implementation and runner: [`9cb08131e42ba84cf96190db20c60ae893afa969`](https://github.com/bgtnt/polylinekit/commit/9cb08131e42ba84cf96190db20c60ae893afa969).
- Measured 2026-09-28, 06:21:11–06:21:58 UTC, in three sequential processes.
- Windows `10.0.26200`, x64; .NET SDK `10.0.401`, runtime `.NET 10.0.12`.
- Recorded CPU identifier: `Intel64 Family 6 Model 158 Stepping 12, GenuineIntel`.
- Release .NET 10 Core assembly; `DOTNET_TieredCompilation=0`, default SIMD mode, hardware Vector256 available. This is not a scalar-versus-SIMD comparison.
- Comparators: Clipper2 **C# 2.0.0**, with reusable inputs and engine; NetTopologySuite **2.6.0**, using `OverlayNGRobust`.

The [fixed protocol and reproduction commands](PROTOCOL.md) define source verification, preparation,
comparator behavior and timing. All 100 counties and 14 congressional districts participate, including the
components and hole excluded from the older single-ring benchmark. Each direction traverses 1,400 pairs: 285
inclusive bounds candidates and 1,115 bounds rejections. All three adapters and both directions use the same
common translation of EPSG:5070 metre coordinates. No outer spatial index is used; all three adapters
perform the same linear pair traversal. This is the complete population of an already observed dataset, not
an independent geographic holdout.

Each query invocation consumes intersection-area and first-region coverage sums. It constructs no output
table and caches no pairwise results. Clipper uses an explicit `1e-6` metre grid and sums result contours;
Core returns numeric areas from the original translated doubles. NTS retains Polygon/MultiPolygon hierarchy.
Generated coordinates use the same grid scale in their own units.

## Complete GIS result

Ratios are comparator time / Core time; above one favors Core. Times are milliseconds per complete
directional table, including bounds rejections.

| Direction | Scope | Core ms | Clipper ms | NTS ms | Clipper/Core | NTS/Core |
|---|---|---:|---:|---:|---:|---:|
| County → district | Warm | 13.756 | 19.667 | 44.770 | 1.430× | 3.255× |
| County → district | Preparation + one | 15.314 | 19.189 | 45.988 | 1.253× | 3.003× |
| District → county | Warm | 13.387 | 18.744 | 45.136 | 1.400× | 3.372× |
| District → county | Preparation + one | 14.691 | 19.122 | 45.982 | 1.302× | 3.130× |

Core uses approximately **28.6–30.1% less warm-table time** and **20.2–23.2% less preparation-plus-one
time** than this Clipper adapter. The older single-ring results measure a different admitted population and
harness; their ratios must not be treated as a before/after speedup from this change.

Preparation alone is more expensive in Core: **1.55–1.57 ms**, versus **0.589–0.598 ms** for Clipper and
**0.395–0.398 ms** for NTS. Core copies and validates immutable inputs and calls the general fill operation
for own areas. The other adapters exploit the independently validated polygon hierarchy to calculate signed
shell-minus-hole denominators. That cost difference is intentional and included, not hidden as setup outside
the measured scope.

The directly measured preparation-plus-one results already favor Core on these GIS tables; multiple
repetitions are not required to recover its extra setup cost here. Reuse removes that preparation from
subsequent calls. A rough `preparation + N × warm-table` model is useful for planning, but is not a measured
multi-table session. The scopes are independently timed and their medians are not additive: for example, the
county-direction Clipper preparation-plus-one median is below its warm-table median. Fresh and reused engine
state can differ; use the observed scope for the intended lifecycle instead of inferring setup cost by
subtracting those two numbers.

## Many-component and hole cases

Each generated row is one comparison of two regions, not a GIS table. Ring count is **per operand**. Squares
are disjoint unit cells; the second operand is shifted by `(0.25, 0.25)`. The hole family has a 2×2 shell
and centered 1×1 hole in each cell, so it contains half as many components as rings. The independent
expected areas and intersections are given in the protocol. These do not model dense self-crossing or
overlapping shell collections.

The small hole cases are close to Clipper in warm timing; their process ranges overlap. Increasing ring
count exposes a material Clipper advantage. At 1,024 rings, Core needs **8.625 ms** for squares and **7.176
ms** for shells with holes; Clipper needs **1.185 ms** and **1.516 ms**. Including preparation, the gaps
become **9.04×** and **5.20×** in Clipper's favor. NTS remains slower than Core.

Preparing once does not remove the observed Clipper gap in the larger generated cases: Core has both greater
preparation time and greater per-comparison time. The prepared type stores coordinates and ring metadata,
not precomputed crossings or pairwise results. The current implementation still checks ring bounds pairs and
groups interacting rings on each evaluation. Improving that work is a concrete follow-up suggested by these
results; the measurements alone do not identify how much time each internal stage contributes.

## All measured scopes

Each cell is **median microseconds [minimum–maximum of the three process medians]; managed bytes per
invocation**. Each process median uses five batches. Allocation medians were identical in all three
processes for every row. The complete matrix contains **1,350 samples**, with no discarded timing rows.
Digest consumption and per-iteration equality checks are included for all backends. These are measurements
of this machine; the ranges are not confidence intervals or latency percentiles.

### Preparation

Both complete catalogues, backend inputs and own-area denominators; no pair queries.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 1,546.938 [1,539.625–1,574.862]; **233,144 B** | 597.812 [590.834–620.494]; **1,133,024 B** | 397.834 [395.006–405.753]; **585,248 B** |
| District → county | 1,571.800 [1,568.513–1,588.088]; **233,144 B** | 588.962 [584.237–616.131]; **1,133,024 B** | 395.225 [395.053–401.669]; **585,248 B** |
| Squares / 16 | 21.456 [20.599–34.240]; **4,552 B** | 7.988 [7.501–9.068]; **16,896 B** | 14.119 [14.071–15.020]; **27,160 B** |
| Shells + holes / 16 | 18.592 [18.446–19.301]; **4,552 B** | 7.657 [7.609–7.704]; **16,896 B** | 12.605 [12.266–13.731]; **23,192 B** |
| Squares / 64 | 100.998 [98.867–101.798]; **15,304 B** | 28.294 [27.292–29.563]; **62,400 B** | 54.913 [54.214–56.336]; **105,496 B** |
| Shells + holes / 64 | 90.672 [88.618–92.008]; **15,304 B** | 27.959 [27.592–28.253]; **62,400 B** | 48.099 [47.806–48.166]; **89,624 B** |
| Squares / 256 | 679.425 [677.275–697.800]; **58,312 B** | 105.809 [105.544–107.444]; **243,840 B** | 226.791 [226.480–236.161]; **418,840 B** |
| Shells + holes / 256 | 532.500 [528.672–538.194]; **58,312 B** | 108.058 [106.845–108.391]; **243,840 B** | 195.972 [194.067–198.877]; **355,352 B** |
| Squares / 1024 | 6,827.900 [6,784.300–7,144.950]; **230,344 B** | 446.391 [432.109–458.722]; **969,024 B** | 1,070.800 [1,039.825–1,092.400]; **1,672,216 B** |
| Shells + holes / 1024 | 4,995.650 [4,919.350–5,018.925]; **230,344 B** | 451.863 [447.344–467.159]; **969,024 B** | 892.388 [881.969–911.450]; **1,418,264 B** |

### Warm comparisons

One prepared session; the complete pair traversal is repeated.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 13,755.600 [13,738.000–13,756.100]; **0 B** | 19,666.600 [19,611.900–19,922.100]; **878,096 B** | 44,769.900 [44,596.800–45,231.700]; **36,271,104 B** |
| District → county | 13,387.000 [13,233.700–13,534.300]; **0 B** | 18,743.800 [18,682.400–19,565.800]; **878,096 B** | 45,136.500 [45,045.900–45,315.500]; **36,271,104 B** |
| Squares / 16 | 16.080 [15.318–25.920]; **0 B** | 12.766 [12.002–13.542]; **7,512 B** | 386.491 [379.756–415.878]; **325,152 B** |
| Shells + holes / 16 | 16.843 [16.321–17.041]; **0 B** | 16.757 [16.139–17.228]; **10,904 B** | 456.672 [436.275–480.613]; **359,096 B** |
| Squares / 64 | 90.546 [87.020–94.005]; **0 B** | 54.836 [53.657–61.445]; **23,640 B** | 1,669.662 [1,630.213–1,672.200]; **1,265,936 B** |
| Shells + holes / 64 | 86.794 [84.094–89.321]; **0 B** | 82.293 [81.713–84.109]; **33,368 B** | 1,889.763 [1,880.713–1,915.850]; **1,475,608 B** |
| Squares / 256 | 738.913 [737.831–817.119]; **0 B** | 253.080 [250.620–256.927]; **82,008 B** | 7,533.350 [7,267.350–7,915.100]; **5,067,136 B** |
| Shells + holes / 256 | 620.950 [605.747–627.466]; **0 B** | 343.769 [339.306–346.859]; **112,856 B** | 8,483.100 [8,119.950–8,543.400]; **7,085,560 B** |
| Squares / 1024 | 8,625.000 [8,479.900–8,892.750]; **0 B** | 1,184.856 [1,163.206–1,190.888]; **303,192 B** | 40,016.700 [39,664.000–40,090.800]; **20,329,248 B** |
| Shells + holes / 1024 | 7,175.600 [6,639.100–7,281.800]; **0 B** | 1,515.612 [1,471.862–1,600.575]; **411,096 B** | 76,049.200 [69,159.800–76,461.700]; **47,277,216 B** |

### Preparation plus one comparison table

A fresh session and one complete traversal; process, JIT and thread-local workspaces are already warm.

| Scenario | Core: µs [range]; B | Clipper: µs [range]; B | NTS: µs [range]; B |
|---|---:|---:|---:|
| County → district | 15,313.900 [15,279.600–15,516.900]; **233,080 B** | 19,188.900 [19,013.100–19,716.600]; **2,044,496 B** | 45,987.600 [45,320.500–46,593.800]; **36,856,336 B** |
| District → county | 14,691.200 [14,634.900–14,734.300]; **233,080 B** | 19,121.900 [19,074.700–19,351.500]; **2,042,680 B** | 45,982.200 [45,924.000–46,098.400]; **36,856,336 B** |
| Squares / 16 | 37.404 [36.877–51.266]; **4,488 B** | 21.310 [20.482–24.180]; **33,976 B** | 419.456 [400.050–462.497]; **352,248 B** |
| Shells + holes / 16 | 34.579 [33.984–34.918]; **4,488 B** | 26.767 [25.648–27.434]; **40,744 B** | 478.047 [469.675–488.134]; **382,992 B** |
| Squares / 64 | 190.408 [186.275–200.916]; **15,240 B** | 84.648 [82.811–94.002]; **123,064 B** | 1,663.425 [1,646.775–1,678.900]; **1,371,368 B** |
| Shells + holes / 64 | 185.694 [175.459–186.992]; **15,240 B** | 103.339 [103.164–103.455]; **145,640 B** | 1,913.650 [1,912.025–1,946.862]; **1,568,240 B** |
| Squares / 256 | 1,464.900 [1,397.525–1,491.625]; **58,248 B** | 382.262 [353.306–386.587]; **471,352 B** | 7,635.350 [7,581.800–7,696.900]; **5,485,912 B** |
| Shells + holes / 256 | 1,177.581 [1,158.681–1,184.625]; **58,248 B** | 467.541 [450.641–488.484]; **552,424 B** | 8,572.750 [8,457.950–8,660.350]; **7,453,136 B** |
| Squares / 1024 | 15,289.300 [15,242.100–15,484.700]; **230,280 B** | 1,691.275 [1,621.138–1,698.987]; **1,849,528 B** | 41,946.800 [41,888.600–42,194.100]; **22,001,400 B** |
| Shells + holes / 1024 | 11,869.100 [11,751.000–12,010.600]; **230,280 B** | 2,281.662 [2,242.025–2,323.000]; **2,156,136 B** | 76,194.200 [75,897.300–76,865.600]; **48,744,568 B** |

## Correctness and allocations

Before timing, each process validates all pairs and own areas with both NonZero and EvenOdd filling, for all
three adapters: **60 scenario/backend/fill groups**. All recorded accuracy results are identical across
processes. Every generated case agrees exactly with its dyadic analytic own-area and intersection values;
coverage agrees with the rounded rational reference in the recorded doubles. This is specific to those
fixtures, not an exact-arithmetic guarantee.

| GIS agreement against NTS | Core maximum | Clipper maximum | Fixed budget |
|---|---:|---:|---:|
| Own area, m² | 3.81469727e-05 | 0.0228462219 | 1 m² |
| Intersection, m² | 2.86102295e-06 | 0.0262260437 | 1 m² |
| Coverage fraction | 1.99840144e-15 | 2.24081864e-11 | 1e-8 |

NTS is an independent floating-point comparison, not an exact GIS oracle. Clipper's quantization is
explicit. Smaller disagreement with NTS in this population does not establish universal numerical
superiority. Candidate membership agrees across all adapters despite the quantization.

All **150 measured Core warm batches** report **0 B per invocation** on the executing managed thread.
Preparation and fresh sessions allocate, as shown above. The figures do not measure native allocations,
retained input memory or peak memory. First use, workspace growth, nested calls and calls exceeding the
workspace retention budget can allocate. Core's per-thread cached engine array payload is limited to 4 MiB
including its component workspace; headers and active/peak memory are outside that cap. Zero warm
allocations here do not imply a general zero-allocation API contract.

## Evidence and limits

The assessed implementation, input snapshots, runner and protocol are public. The raw run files below are
retained in the author's ignored local
`artifacts/prepared-region-benchmark-9cb08131e42ba84cf96190db20c60ae893afa969/` directory; **they are not
published downloads**. The hashes identify that evidence, rather than providing access to it. Use the
protocol commands to produce an independent repetition.

| Local evidence file | SHA-256 |
|---|---|
| run-1.json | `4a9ce5e3caeb7a2393383449d512016cfec6f1d55a6b80a2a05feafd5a273f35` |
| run-2.json | `49ef9459cf0d3614c166146f5e50f7ea47eec412f478d92512dce7e5b7a7a157` |
| run-3.json | `85295bd4fda57dff864c77d2148d9846128e1f95652be9deca8a6522ce3732ce` |
| summary.json | `6277ad1d464423e4557ddd0a410766bc132d1991d565d541b0155c328c11a264` |
| process-order.json | `ff155fafadb23ae82d9137f3229ebc7ff1376fb9b415a68bf927e59e74bdfb4b` |

| Assessed source/binary | SHA-256 |
|---|---|
| tests/PolylineKit.MultiRingChecks/PROTOCOL.md | `c60902cd3337bcb8e8b53ee2f9610ed9f0f584525fa1ef14b234a4bb9ce36bf0` |
| tests/PolylineKit.MultiRingChecks/RegionBenchmarks.cs | `8830e0dacaca9f2b2c4dc312f4d147d5231828efff3683ae656009c5f8deba30` |
| src/PolylineKit.Core/PreparedRegion.cs | `e04b629b3039ca00cb79e0a97b9c8be6fbb5ae9358a71da2055a088555228245` |
| src/PolylineKit.Core/RegionArea.cs | `058ea74860b313516ab9e89f38a3eefe18e6a64fcbb2e29028f88e378b277fbe` |
| PolylineKit.Experiments.dll | `f75e64e50fab5966469ca3b932a217012ff58834718ab4be19d9dbe8932bfaa4` |
| PolylineKit.Winding.dll | `8033b262a09aee49c2c8c2567655c67bb4c86c7114eb2f4dac2a409e7a0aa819` |
| Clipper2Lib.dll | `e9bf01e01f494ef6072cd46c792ed4f3004288a92b1e98c1f44931e558b8a5d0` |
| NetTopologySuite.dll | `98fa6bae8b75b5f9445236679883fa27750d990aec11e985b85b57a30c37a858` |

All three process windows are disjoint and all exit codes are zero. The recorded binary, source, protocol,
input and accuracy identities agree across runs; the summary verifies the complete scope matrix, candidate
counts, per-batch medians and stable digests. No additional performance run was made while writing this
report.

This evaluates intersection and coverage through one .NET backend per library. It does not compare Clipper
C++, every NTS strategy, output-geometry APIs, concurrent throughput, maximum ring counts, or dense crossing
workloads. It supports the measured prepared-region use case and identifies its scaling limits; it does not
make the alpha API final or demonstrate external production adoption.
