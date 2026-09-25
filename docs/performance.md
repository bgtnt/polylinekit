# Performance: measured scope and tradeoffs

The separate [integer scanbeam prototype](../benchmarks/PolylineKit.ScanbeamBenchmarks/RESULTS.md)
at `168f5eb` takes 22-24% less time for NonZero and 54-57% less time for EvenOdd
than the faster direct Clipper64 variant on the two frozen dense integer grids.
It passes the four fixed 1.25x gates after two documented arithmetic ablations.
It loses on the star and many-level controls, accepts only bounded integer
coordinates, and remains outside the shipping library. These are scalar
Int64/Int128 changes, not SIMD or C++ measurements.

The [expanded experiment](../benchmarks/PolylineKit.ScanbeamBenchmarks/EXPANDED-RESULTS.md)
at `9a947c7` limits that conclusion further: the prototype takes about 76 ms per
rounded real-contour table versus 12.5 ms for Clipper and 10 ms for existing
Winding intersection. All four real speed gates fail. Metre-grid conversion also
fails the original-input error budgets in 312 of 2156 directional comparisons.
Wider-coordinate grid controls pass the four EvenOdd gates but fail all four
NonZero gates. The prototype remains outside the shipping library.

The [guarded double sweep](../benchmarks/PolylineKit.ScanbeamBenchmarks/DOUBLE-RESULTS.md)
at `a3eb54e` preserves the original coordinates and certifies all 422 geometry
candidates without fallback. Its warm full tables take about 101 ms versus
12.6–12.9 ms for Clipper and 9.2–9.7 ms for existing Winding, with zero warm
allocations. All four performance gates fail. General interval arithmetic and
remaining per-band work are plausible costs; CPU shares have not been profiled.
An untimed common-Y count identifies a possible reduction in endpoint bands,
not a measured speedup. This candidate also remains outside the shipping library.

The [intersection-only coverage experiment](../examples/RegionCoverage/INTERSECTION-RESULTS.md)
at `f93cc25` establishes a newer, narrower result: the dedicated
`WindingArea.IntersectionArea` method takes 26-29% less time than direct reusable
Clipper64 in both whole-population directions, with and without fresh preparation.
All four predeclared 1.25x gates pass; warm traversal allocations are 0 versus
625,088 managed bytes. The old full-metric operation and its results below remain
unchanged. This gain does not establish a win on dense self-crossing inputs.

PolylineKit's geometry contracts are the reason to choose an operation. Performance depends strongly on the shape, vertex count and crossing density. `BetweenGraphs` has a linear graph-specific algorithm; general winding and clipping operations solve broader problems. The results below are complete API-call measurements, not universal speed guarantees.

## Evidence and reproduction

The frozen evidence comes from Release builds on Windows build 26200, x64 AVX2, Intel Family 6 Model 158 Stepping 12, 16 logical processors, .NET SDK 10.0.401/runtime 10.0.12. Tiered compilation was disabled. Each implementation ran in three fresh processes; tables use medians of process medians, with calibrated batches inside each process. No build or other benchmark ran concurrently, but ordinary OS activity was not controlled. Small differences are descriptive, not statistical confidence bounds.

Benchmarks separate input generation from the timed call; validation/copying, sorting, candidate search and accumulation remain inside the timed winding call. Allocation is managed bytes on the measured thread for warm calls, not first-call, retained or peak memory. Current commands and fixed inputs are in [benchmarks/README.md](../benchmarks/README.md). Full historical inputs, raw samples, source revisions and rejected prototypes are preserved at [research/README.md](../research/README.md).

The optimizations were measured in separate stages. Do not multiply their speedups or attribute a comparison against an earlier stage to SIMD alone.

## Sub-edge ratio correction: measured cost

The later review found a distinct intermediate-underflow error: a tiny fraction
could become zero before multiplication by a large edge term. The exceptional
ratio is now scaled; ordinary fractions retain their original evaluation.
See the [derivation](winding-numerics.md#sub-edge-ratio-underflow) and
[review disposition](winding-review.md).

Same harness; only leaf DLL replaced. Baseline `e4444cc`, corrected `cd23a11`. Three fresh processes each, nine batches.

| Workload | n per path | Before, us | Corrected, us | Time change |
|---|---:|---:|---:|---:|
| degenerate-grid | 64 | 408.66 | 403.05 | -1.4% |
| degenerate-grid | 256 | 6912.65 | 6881.93 | -0.4% |
| dense-graph | 64 | 14.26 | 13.78 | -3.4% |
| dense-graph | 256 | 49.70 | 48.99 | -1.4% |
| dense-graph | 1024 | 193.50 | 189.72 | -2.0% |
| filled-regions | 64 | 16.79 | 17.62 | +4.9% |
| filled-regions | 256 | 70.94 | 69.76 | -1.7% |
| filled-regions | 1024 | 277.61 | 274.17 | -1.2% |
| random-walks | 64 | 20.00 | 20.06 | +0.3% |
| random-walks | 256 | 153.15 | 152.98 | -0.1% |
| random-walks | 1024 | 760.79 | 747.91 | -1.7% |
| similar-strokes | 64 | 12.44 | 11.45 | -7.9% |
| similar-strokes | 256 | 39.70 | 40.83 | +2.8% |
| similar-strokes | 1024 | 160.46 | 160.19 | -0.2% |

All warm winding allocation samples are zero. Changes range from -7.9% to
+4.9%, mostly within 3%. These descriptive differences show no large consistent
cost in the measured set; they do not establish a speed improvement or prove
zero overhead. This is separate from the earlier area-term correction below.

## Earlier edge-term correction: measured cost

The area-term correction fixes product cancellation in thin triangles; it is not
a general performance optimization. It uses a derived value-error filter,
compensated products (scalar FMA when supported, Dekker otherwise), then exact
expansion/integer fallbacks. See [the derivation](winding-numerics.md).

The comparison below keeps the original Winding harness binary identical and
replaces only `PolylineKit.dll`: `969f8ac` versus corrected pre-extraction
`50fa1b5`. Three fresh processes for each implementation ran serially, alternating
order in the middle round. Values may change because this is a numerical fix.
The later assembly move preserved the corrected outputs bit for bit on 120
operations for each target; its packaging layout is not credited with speedups.

| Workload | Vertices per path | Before, us | Corrected, us | Time change |
|---|---:|---:|---:|---:|
| degenerate-grid | 64 | 403.41 | 400.18 | -0.8% |
| degenerate-grid | 256 | 6883.10 | 6936.77 | +0.8% |
| dense-graph | 64 | 13.57 | 13.15 | -3.1% |
| dense-graph | 256 | 49.13 | 47.97 | -2.4% |
| dense-graph | 1024 | 196.70 | 190.90 | -2.9% |
| filled-regions | 64 | 15.28 | 16.78 | +9.8% |
| filled-regions | 256 | 61.33 | 70.80 | +15.4% |
| filled-regions | 1024 | 238.61 | 276.33 | +15.8% |
| random-walks | 64 | 19.29 | 19.75 | +2.3% |
| random-walks | 256 | 145.13 | 152.45 | +5.0% |
| random-walks | 1024 | 725.51 | 754.36 | +4.0% |
| similar-strokes | 64 | 10.86 | 11.96 | +10.1% |
| similar-strokes | 256 | 36.59 | 41.25 | +12.7% |
| similar-strokes | 1024 | 146.14 | 160.47 | +9.8% |

All measured warm winding calls remain **0 B/op**. Differences of a few percent
can include process/OS variation; there are no confidence intervals here. The
first, expansion-heavy correction at `1049790` cost 50–71% on similar strokes and
29–34% on regions in a separate six-process comparison. It was replaced without
loosening the 8u contribution-error budget or regression tolerances. These data
justify the compensated intermediate path, not a claim that correctness is free.

## Historical optimization stages

| Stage | Compared runtime sources | Result and limitation |
| --- | --- | --- |
| Bounds/predicate cleanup | `9fff4d2` → `6fc1c35` | Similar strokes 1.23–1.39x and random walks 1.33–1.48x faster. Filled-region gains were only 1.05–1.17x; degenerate grids still lost to Clipper. [All 14 rows](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/performance/comparison.md). |
| Natural sorting, bounds layout and candidate compaction | `7962d19` (runtime `6fc1c35`) → `dcd836a` | Larger similar/random inputs about 1.27–1.31x faster; ring 4096 about 1.76x. Small original fixtures lost 2–6%, diagonal bars 256 about 12%. [All variants](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/search/summary.md). |
| Integrated simple-path certificate | `9ecf6a7` (runtime `dcd836a`) → `5614828` | Large selected simple contours improve strongly; some small/simple or late-rejection cases get slower. Detailed table below. [All 81 rows](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/integrated-sweep/summary.md). |

The certificate-stage runtime is the implementation retained in the archived `00f9624` merge. Later review added tests without changing that measured runtime. The following historical tables precede the numerical correction above.

## Integrated certificate: gains and costs

This comparison used 42 development controls, 36 independently generated new paths and three independent filled-region controls. The new inputs and selection constants were frozen before their measurements. There were five samples per workload per process, alternating baseline/integrated process order between rounds. Both implementations returned exactly the same recorded numerical values on all 81 workloads. Source identity, input hashes and DLL SHA-256 values are in the [full report](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/winding-integrated-sweep.md).

Times are microseconds per complete call; speedup is baseline/integrated, so below 1 means slower.

| Workload | Vertices | Baseline | Integrated | Speedup |
| --- | ---: | ---: | ---: | ---: |
| New sheared canyon | 257 | 174.32 | 95.44 | 1.83x |
| New sheared canyon | 1024 | 2717.86 | 482.98 | 5.63x |
| New sheared canyon | 2048 | 10951.80 | 1020.46 | 10.73x |
| Simple diagonal comb | 4098 | 35211.80 | 1763.41 | 19.97x |
| Radial star | 4096 | 19202.20 | 2082.21 | 9.22x |
| Radial star | 256 | 85.20 | 108.02 | 0.79x |
| Ring | 4096 | 218.21 | 235.28 | 0.93x |
| Vertical subdivided rectangle | 4096 | 251.20 | 265.94 | 0.94x |
| Original random walk | 512 | 143.66 | 147.57 | 0.97x |
| Original random walk | 2048 | 750.32 | 726.53 | 1.03x |
| New random walk | 1024 | 338.30 | 339.31 | 1.00x |
| New random walk | 2048 | 750.58 | 731.99 | 1.03x |
| Late-crossing diagonal comb | 1026 | 2251.27 | 2486.73 | 0.91x |
| Late-crossing diagonal comb | 4098 | 35229.60 | 36421.60 | 0.97x |
| Independent filled regions | 2048 total | 233.82 | 237.10 | 0.99x |

All **2,430 allocation samples were 0 B/call**. This excludes first use, growth, nested calls, extreme-exponent integer predicates and retained workspace capacity; see [winding-area.md](winding-area.md#cost-and-storage).

The 256-vertex star takes about **27% longer**: work already spent does not guarantee enough remaining work to repay certification. Ordinary low-candidate paths pay row-accounting overhead even without an attempt. A late crossing can require an attempted certificate plus the general pass. The earlier standalone prototype's 26–32% random-walk penalty disappeared on these measured inputs, but that does not guarantee every random input is unaffected. The new set is now observed data; future tuning requires a separately reserved set.

## What SIMD contributes

The .NET 10 winding engine vectorizes independent bounds comparisons, then processes survivors with unchanged exact geometry. Scalar and vector versions use the same numerical contracts. The forced-scalar ablation of the sorting/compaction stage measured:

| Workload | Modern scalar, microseconds | SIMD, microseconds | Speedup |
| --- | ---: | ---: | ---: |
| Random walks, 1024 | 842.49 | 712.47 | 1.18x |
| Stacked bars, 4096 | 11010.15 | 4731.48 | 2.33x |

Other rows show little benefit or small overhead. The large ring/rectangle gains came mainly from sorting. These ablations precede the certificate stage and should not be presented as its SIMD gain. See the [frozen assessment and disassembly links](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/winding-search.md).

Affine array application also has a separate SIMD path. Its earlier controlled evaluation found approximately 7–10% lower time from explicit SIMD in complete preparation/comparison workflows at 256–1024 vertices; much larger local `Apply` speedups did not transfer to whole workflows. Scalar cleanup reduced allocations independently. Individual slower rounds remain in the [frozen optimization assessment](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/optimization-evaluation.md). These figures are not a new measurement of the reorganized benchmark runner.

## Current comparison with Clipper2

The current engine at `2410a6b` was measured in **three independent processes**,
238 method rows per process, using `WindingVsClipper2`. It includes the sub-edge
ratio correction. The [preparation contracts](../benchmarks/README.md#winding-versus-clipper2-operation-and-preparation-contracts)
distinguish conversion, Clear/Add and repeated Execute. The table reports the
range of **Clipper time / winding time** across the listed sizes, so above 1
favors winding. Preloaded inputs are an amortized repeated-query scenario;
WindingArea still prepares its input inside each call. These columns do not
measure equal preparation work. `n` is vertices per input path, not total.

| Family / operation | n per path | Clipper64 static | Reused | Preloaded | ClipperD p6 | Wrapper |
|---|---|---:|---:|---:|---:|---:|
| blobs / overlap-metrics | 16,64,256,1024 | 1.92–3.18 | 1.68–2.89 | 1.65–2.75 | 2.02–3.16 | 2.61–3.21 |
| blobs / simple-iou | 16,64,256,1024 | 0.96–1.57 | 0.88–1.51 | 0.79–1.39 | 1.02–1.58 | 2.55–3.22 |
| blobs / xor-only | 16,64,256,1024 | 1.23–1.83 | 1.09–1.72 | 1.02–1.57 | 1.24–1.90 | 1.48–1.84 |
| degenerate-grid / bridged-nonzero | 64,256 | 0.74–0.77 | 0.73–0.76 | 0.73–0.75 | 0.75–0.77 | 0.75–0.79 |
| random-walks / bridged-nonzero | 16,64,256,1024 | 1.91–2.81 | 1.86–2.75 | 1.71–2.66 | 1.97–2.93 | 2.06–2.91 |
| similar-strokes / bridged-nonzero | 16,64,256,1024 | 1.80–2.08 | 1.68–1.91 | 1.51–1.84 | 1.96–2.15 | 2.11–2.36 |
| simple-spiky-star / closed-nonzero | 1024,4096 | 4.91–33.36 | 4.62–25.49 | 4.47–25.45 | 4.86–33.25 | — |
| star-regions / overlap-metrics | 16,64,256,1024 | 1.74–2.23 | 1.59–1.93 | 1.48–1.82 | 1.84–2.29 | 2.53–3.10 |
| star-regions / simple-iou | 16,64,256,1024 | 0.89–1.03 | 0.82–0.96 | 0.74–0.91 | 0.93–1.23 | 2.53–3.04 |
| star-regions / xor-only | 16,64,256,1024 | 1.08–1.35 | 0.96–1.16 | 0.89–1.09 | 1.15–1.41 | 1.45–1.76 |
| tangled-ring / closed-nonzero | 16,64,256,1024 | 0.79–1.46 | 0.80–1.42 | 0.77–1.37 | 0.82–1.45 | — |

All **1,188 winding allocation samples** (44 scenarios × 3 processes × 9 batches)
are 0 B/op for these warm calls. Clipper constructs contours and therefore
solves a broader output problem. WindingArea computes all region metrics even
when only XOR or IoU is consumed; direct Clipper can execute a single Boolean
operation in those narrower scenarios. The public wrapper's IoU still computes
its complete overlap result. The large simple-star gains rely on the existing
simplicity certificate, not the numerical repair. Clipper wins the grid
controls and all preloaded star-IoU rows; the tangled-ring result varies with
size. No universal fastest method follows from the favorable cases.

AbsoluteWinding is reported separately because fill area is not its equivalent.
These tangled-ring calls compute all four single-path integrals and consume
AbsoluteWinding; they are not an optimized absolute-only kernel.

| n | Winding us (range) | B/op |
|---:|---:|---:|
| 16 | 9.39 (9.18–9.62) | 0 |
| 64 | 22.63 (21.33–23.65) | 0 |
| 256 | 140.59 (138.66–145.77) | 0 |
| 1024 | 743.05 (732.68–755.42) | 0 |

Integer-grid controls give both methods the same exactly representable input
geometry; generated crossing coordinates can still differ. Other prepared
Clipper rows use a decimal grid of `10^-6`, while winding uses the supplied
binary64 points. The largest primary-value absolute disagreement in this
matrix is about `3.77e-6` for direct Clipper64/ClipperD, and `1.42e-5` for the
wrapper, both on the 1024-vertex random walks. The wrapper additionally
recenters coordinates and may exchange axes before quantization. These
observed differences are not accuracy bounds;
the manifest preserves per-family, per-metric maxima, and raw records retain
each result. The area-change consumer below separately includes double-to-grid
conversion for direct Clipper64 callers.

The reproducible `accuracy-clipper` controls give zero winding error on all four
thin triangles and on the distant-square bridge. Across the five selected
archived integer-grid disagreements, maximum winding absolute error is about
`8.9e-16`; pinned Clipper2 2.0.0 has larger discrepancies on the already known
cases. These are deliberately selected regressions, not an unbiased accuracy
survey. Tilted strips retain winding intersection relative errors of about
`6e-12`, `1.3e-8`, and `9.8e-4` at lengths `1e8`, `1e12`, and `1e16`.
Their corresponding ClipperD precision settings are recorded individually;
neither a grid step nor an exact topology predicate bounds final relative area
error. See [numeric limits](winding-area.md#numerical-limits).

The [current compact evidence manifest](../benchmarks/winding-review-evidence.json)
records source revisions, DLL hashes, three-process summaries and the local raw
archive checksum. The [earlier extraction manifest](../benchmarks/winding-evidence.json)
retains the `8a94077` measurements. Raw archives remain local under ignored
`artifacts/`; they have not been published. Commands and fixed inputs are in
[benchmarks/README.md](../benchmarks/README.md).

## First use and retained workspace

At `a50dade`, five cases each ran in three new processes, serially. No winding
runtime code changed since the external comparison. First-call timing includes
JIT and workspace creation, but excludes input generation, assembly loading
and counter setup. It is not process-start latency or a cold-start comparison
with Clipper. Warm timing comes from nine subsequent batches per process.

| Case | First call ms (range) | First allocated B | Retained array payload B | Warm us | Warm B/op |
|---|---:|---:|---:|---:|---:|
| similar-strokes/16/bridged-nonzero | 27.81 (27.51–27.94) | 42240 | 41105 | 3.46 | 0 |
| similar-strokes/1024/bridged-nonzero | 26.53 (26.52–27.14) | 279080 | 261009 | 158.89 | 0 |
| star-regions/1024/overlap-metrics | 29.36 (29.16–30.97) | 278704 | 260745 | 275.27 | 0 |
| degenerate-grid/256/bridged-nonzero | 39.56 (39.39–40.31) | 10046416 | 7013261 | 6845.25 | 0 |
| simple-spiky-star/4096/closed-nonzero | 36.87 (36.64–37.36) | 556536 | 538249 | 2166.47 | 0 |

Retained payload inventories the cached workspace, certificate and predicate
arrays using managed element sizes, without array headers, object/delegate
fields or native/JIT memory. Forced-collection managed-heap deltas are retained
separately in the manifest; they may include runtime caches. Neither number is
peak or total memory. The grid's roughly 7 MB retained payload is material even
though its repeated calls allocate zero bytes. A long-lived thread keeps the
largest workspace capacity it has needed. See the
[profiling contract](../benchmarks/README.md#first-use-and-retained-workspace).

## Real-contour area-change consumer

The comparison below measures general area scorers. Consumers with containment or
edit-history guarantees may use much cheaper specialized operations. The
[specialized comparison](../examples/AreaChange/SPECIALIZED.md) distinguishes exact
area results from threshold filtering and charges prerequisite validation separately.
Its [three-process results](../examples/AreaChange/SPECIALIZED-RESULTS.md) show ordinary
formulas 30.0–39.8 times faster on constructed nested contours and 26.4–46.8 times
faster on constructed disjoint local changes. The real-pair threshold filter helps
resolved prepared requests but adds cost to fallbacks; at a 1% threshold the aggregate
of one request per pair is slower. No unconditional filter is enabled in the runtime.

[AreaChange](../examples/AreaChange/README.md) compares four public-domain
Natural Earth contours with three simplifications each. It uses a pinned
existing simplifier and measures changed filled area directly. Both scorers
receive identical pairs and request XOR plus union/Jaccard. Direct Clipper64
includes input conversion and quantization to `10^-8`, two Boolean operations,
and output area summation without converting output vertices to doubles.

Three independent processes at `a50dade` use five batches each. Across the
twelve pairs, Clipper time / winding time is **1.06–1.94 for isolated area**
and **1.06–1.78 for the complete geometry consumer**. The smallest differences
are modest and some process ranges overlap; these are descriptive ratios.
Isolated winding allocates zero bytes; the full winding consumer allocates
3,728–10,888 B/op for simplification and conversions. The corresponding
Clipper consumers allocate 34,032–142,504 B/op. Data loading and report writing
are outside both complete geometry operations.

For example, Bulgaria at tolerance 4 removes 90/178 vertices and changes
0.6013% of the union. The complete winding consumer takes 87.58 us versus
122.02 us for clipping on this machine. Analytic controls and all twelve real
pairs pass their declared checks; maximum p8 XOR/union disagreement is about
`1.10e-6` squared normalized map units. The coordinates describe local map
planes, not geodesic land area. All pairs, time ranges, allocation figures,
provenance and three overlays are in the [example results](../examples/AreaChange/RESULTS.md).
This demonstrates a useful bounded area operation, without establishing
customer demand, maximum-deviation guarantees or recognition quality.

## Native-code experiments

The C++ prototype improved an isolated bounds loop but not the compensated edge sum; it never established a whole-engine advantage. Experimental scalar FMA had no consistent end-to-end gain and was not retained. A packed hierarchy helped one family and regressed most others after construction/traversal costs. These findings reject those measured prototypes, not every native or spatial-index design. Their [sources and assessment](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/winding-performance.md) remain public in the archive.
