# Performance: measured scope and tradeoffs

PolylineKit's geometry contracts are the reason to choose an operation. Performance depends strongly on the shape, vertex count and crossing density. `BetweenGraphs` has a linear graph-specific algorithm; general winding and clipping operations solve broader problems. The results below are complete API-call measurements, not universal speed guarantees.

## Evidence and reproduction

The frozen evidence comes from Release builds on Windows build 26200, x64 AVX2, Intel Family 6 Model 158 Stepping 12, 16 logical processors, .NET SDK 10.0.401/runtime 10.0.12. Tiered compilation was disabled. Each implementation ran in three fresh processes; tables use medians of process medians, with calibrated batches inside each process. No build or other benchmark ran concurrently, but ordinary OS activity was not controlled. Small differences are descriptive, not statistical confidence bounds.

Benchmarks separate input generation from the timed call; validation/copying, sorting, candidate search and accumulation remain inside the timed winding call. Allocation is managed bytes on the measured thread for warm calls, not first-call, retained or peak memory. Current commands and fixed inputs are in [benchmarks/README.md](../benchmarks/README.md). Full historical inputs, raw samples, source revisions and rejected prototypes are preserved at [research/README.md](../research/README.md).

The optimizations were measured in separate stages. Do not multiply their speedups or attribute a comparison against an earlier stage to SIMD alone.

## Current numerical correction: measured cost

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

The extracted engine at `8a94077` was measured in **three independent processes**,
181 method rows per process, using `WindingVsClipper2`. This is the corrected
runtime. The [preparation contracts](../benchmarks/README.md#winding-versus-clipper2-operation-and-preparation-contracts)
distinguish conversion, Clear/Add and repeated Execute. The table reports the
range of **Clipper time / winding time** across the tested sizes, so above 1
favors winding. Preloaded inputs are an amortized repeated-query scenario;
WindingArea still prepares its input inside each call. These columns do not
measure equal preparation work.

| Family / requested result | Vertices per path | Clipper64 static | Clipper64 reused | Clipper64 preloaded | ClipperD p6 |
|---|---:|---:|---:|---:|---:|
| Similar strokes / bridged NonZero | 64–1024 | 1.85–1.90 | 1.71–1.73 | 1.60–1.69 | 1.90–2.02 |
| Random walks / bridged NonZero | 64–1024 | 1.91–2.82 | 1.88–2.76 | 1.81–2.66 | 2.00–2.89 |
| Degenerate grid / bridged NonZero | 64–256 | 0.74–0.77 | 0.73–0.77 | 0.73–0.76 | 0.76–0.78 |
| Tangled ring / NonZero | 64–1024 | 0.81–1.47 | 0.81–1.45 | 0.79–1.40 | 0.83–1.49 |
| Simple spiky star / NonZero | 1024–4096 | 4.78–33.79 | 4.47–25.47 | 4.34–25.51 | 4.83–33.89 |
| Star regions / full overlap metrics | 64–1024 | 1.71–2.15 | 1.52–1.86 | 1.45–1.80 | 1.85–2.20 |
| Star regions / XOR only | 64–1024 | 1.07–1.22 | 0.95–1.13 | 0.90–1.05 | 1.14–1.26 |
| Star regions / simple IoU | 64–1024 | 0.90–0.98 | 0.82–0.89 | 0.73–0.83 | 0.92–1.06 |
| Blobs / full overlap metrics | 64–1024 | 1.93–3.16 | 1.66–2.85 | 1.61–2.69 | 2.01–3.17 |
| Blobs / XOR only | 64–1024 | 1.19–1.81 | 1.06–1.71 | 1.01–1.55 | 1.25–1.89 |
| Blobs / simple IoU | 64–1024 | 0.99–1.53 | 0.91–1.49 | 0.84–1.37 | 1.01–1.60 |

All **837 winding allocation samples** (31 scenarios × 3 processes × 9 batches)
are 0 B/op for these warm calls. Clipper constructs contours and therefore solves
a broader output problem, even though this consumer only uses their area.
WindingArea computes all region metrics even when only XOR or IoU is consumed;
Clipper can execute a single Boolean operation in those narrower scenarios.
The large simple-star gains rely on the existing simplicity certificate, not
on the numerical fix. Clipper wins the grid controls and all preloaded star-IoU
rows. No universal fastest method follows from the favorable cases.

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

The [compact evidence manifest](../benchmarks/winding-evidence.json) records
source revisions, DLL hashes, the three-process summaries and the raw archive
checksum. Raw samples and inputs remain under ignored `artifacts/` for review;
the archive has not been published. Reproduce with the commands in
[benchmarks/README.md](../benchmarks/README.md). Historical `becc37d` results
remain in the [research archive](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/benchmarks/summary.md).

## Native-code experiments

The C++ prototype improved an isolated bounds loop but not the compensated edge sum; it never established a whole-engine advantage. Experimental scalar FMA had no consistent end-to-end gain and was not retained. A packed hierarchy helped one family and regressed most others after construction/traversal costs. These findings reject those measured prototypes, not every native or spatial-index design. Their [sources and assessment](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/winding-performance.md) remain public in the archive.
