# Performance: measured scope and tradeoffs

PolylineKit's geometry contracts are the reason to choose an operation. Performance depends strongly on the shape, vertex count and crossing density. `BetweenGraphs` has a linear graph-specific algorithm; general winding and clipping operations solve broader problems. The results below are complete API-call measurements, not universal speed guarantees.

## Evidence and reproduction

The frozen evidence comes from Release builds on Windows build 26200, x64 AVX2, Intel Family 6 Model 158 Stepping 12, 16 logical processors, .NET SDK 10.0.401/runtime 10.0.12. Tiered compilation was disabled. Each implementation ran in three fresh processes; tables use medians of process medians, with calibrated batches inside each process. No build or other benchmark ran concurrently, but ordinary OS activity was not controlled. Small differences are descriptive, not statistical confidence bounds.

Benchmarks separate input generation from the timed call; validation/copying, sorting, candidate search and accumulation remain inside the timed winding call. Allocation is managed bytes on the measured thread for warm calls, not first-call, retained or peak memory. Current commands and fixed inputs are in [benchmarks/README.md](../benchmarks/README.md). Full historical inputs, raw samples, source revisions and rejected prototypes are preserved at [research/README.md](../research/README.md).

The optimizations were measured in separate stages. Do not multiply their speedups or attribute a comparison against an earlier stage to SIMD alone.

| Stage | Compared runtime sources | Result and limitation |
| --- | --- | --- |
| Bounds/predicate cleanup | `9fff4d2` → `6fc1c35` | Similar strokes 1.23–1.39x and random walks 1.33–1.48x faster. Filled-region gains were only 1.05–1.17x; degenerate grids still lost to Clipper. [All 14 rows](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/performance/comparison.md). |
| Natural sorting, bounds layout and candidate compaction | `7962d19` (runtime `6fc1c35`) → `dcd836a` | Larger similar/random inputs about 1.27–1.31x faster; ring 4096 about 1.76x. Small original fixtures lost 2–6%, diagonal bars 256 about 12%. [All variants](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/search/summary.md). |
| Integrated simple-path certificate | `9ecf6a7` (runtime `dcd836a`) → `5614828` | Large selected simple contours improve strongly; some small/simple or late-rejection cases get slower. Detailed table below. [All 81 rows](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/integrated-sweep/summary.md). |

The certificate-stage runtime is the implementation retained in the archived `00f9624` merge. Later review added tests without changing the measured runtime. The current repository reorganization does not claim new measurements.

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

## Comparison with Clipper and native code

A preserved comparison at `becc37d` measured `WindingArea` around 1.5–2.1x faster than Clipper-based endpoint-bridged areas and 2.5–3.6x faster for filled regions, but **1.4–1.5x slower on dense degenerate grid inputs**. That is an earlier runtime, not the final certificate implementation. See [all original rows](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/results/winding/benchmarks/summary.md). Quantization and returned quantities must match before treating Clipper as a baseline; absolute winding has no equivalent fill rule. Use current benchmark commands for a comparison on your machine.

The C++ prototype improved an isolated bounds loop but not the compensated edge sum; it never established a whole-engine advantage. Experimental scalar FMA had no consistent end-to-end gain and was not retained. A packed hierarchy helped one family and regressed most others after construction/traversal costs. These findings reject those measured prototypes, not every native or spatial-index design. Their [sources and assessment](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/winding-performance.md) remain public in the archive.
