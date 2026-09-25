# AreaBenchmarks

A small maintained benchmark of `PolylineArea.FilledArea` on original projected real contours.
It references only the area library and benchmark-only Clipper2 2.0.0; the retired experimental
runners are not build dependencies. See [the fixed protocol](PROTOCOL.md) and
[fixture provenance](data/README.md).

From the repository root:

```powershell
dotnet build benchmarks/PolylineKit.AreaBenchmarks -c Release
dotnet run --project benchmarks/PolylineKit.AreaBenchmarks -c Release --no-build -- check artifacts/area-check
```

Before timing, commit the source, build Release again and copy the complete output directory
to an ignored immutable directory. The launcher checks the embedded commit and binary hashes:

```powershell
./benchmarks/PolylineKit.AreaBenchmarks/run.ps1 `
  -Revision <40-character-commit> `
  -Runner artifacts/area-measured-bin/PolylineKit.AreaBenchmarks.dll `
  -Output artifacts/area-real-contours
```

The script launches three sequential processes, then writes validated `evidence.json` and
`summary.md` alongside the raw per-process files. Do not run builds or other measurements in
parallel. The source is 8 complete county features from two states; all 9 constituent rings
are measured individually and in one complete batch. Areas are in square metres, not county
totals. EPSG:5070 coordinates remain exactly as returned by the service.

The current primary question is whether the public scalar API preserves the performance of
the existing `ClosedPath` fallback on a fresh real-coordinate population. Results for Clipper
provide context; this workload does not establish an integer-specialization speedup. Numerical
checks use an independent BigInteger simplicity check and translated/exact shoelace oracles
for simple rings. Inputs needing repair are reported without changing their geometry.

The original calibration and reusable Clipper adapter structure were adapted from PolylineKit's
MIT-licensed area experiments archived at commit `5ef33e8`; this runner has no source link or
runtime reference to those projects.

## Recorded real-contour result: primary gate failed

Measured source: [`0e53b0713b2d0c6ebd0b7603125010defccb81ed`](https://github.com/bgtnt/polylinekit/commit/0e53b0713b2d0c6ebd0b7603125010defccb81ed),
2026-09-25. Environment: .NET 10.0.12, Windows build 26200, x64,
Intel Family 6 Model 158 Stepping 12, 16 logical processors; tiered compilation disabled.
Three sequential processes produced 80 rows each and 1,200 samples under the frozen protocol.
These timings apply to that revision's frozen binaries, before the core/Clipper package
separation and retained-cache limit; they do not measure those subsequent changes.

The complete-batch criterion requires Public/ClosedPath ≤1.10 for both fills. NonZero passed
at **0.9808×**; EvenOdd failed at **1.2319× (+23.19%)**. All 18 individual ring/fill ratios
were within 1.10, ranging from 0.9513× to 1.0305×. The failure remains part of the result.

Each batch invokes all nine rings. Times below are medians of three process medians; ranges
show those process medians, not confidence intervals. Allocations are warm bytes per batch.

| Fill | Method | Median µs | Range µs | B/batch |
|---|---|---:|---:|---:|
| NonZero | Public | 1513.875 | 1511.119–1590.175 | 0 |
| NonZero | ClosedPath | 1543.525 | 1513.525–1780.550 | 0 |
| NonZero | Clipper-full | 2432.700 | 2275.325–2546.125 | 557610.5 |
| NonZero | Clipper-preloaded | 2048.463 | 2033.888–2118.913 | 289000 |
| EvenOdd | Public | 1877.419 | 1509.394–1895.531 | 0 |
| EvenOdd | ClosedPath | 1523.981 | 1518.081–1564.706 | 0 |
| EvenOdd | Clipper-full | 2377.925 | 2277.969–2761.325 | 557496 |
| EvenOdd | Clipper-preloaded | 2097.738 | 1980.838–2143.450 | 289000 |

All nine rings passed the independent simplicity checks. The exact dyadic oracle agrees
bit-for-bit with an independent Python `Fraction` calculation, and all 18 public results
passed the oracle tolerance and matched ClosedPath exactly. Clipper's two preparation
variants agreed and passed the documented grid-aware diagnostic allowance. Eight deliberately
malformed evidence controls were rejected without changing submitted files or producing
derived output; unchanged evidence was accepted.

The EvenOdd public batch varied substantially across processes; these measurements do not
isolate why its batch result differs from the small per-ring differences. No implementation
change or replacement timing was made in response. This small two-state population exercises
the general engine, and establishes no integer-route advantage or universal ranking against
Clipper. Clipper's coordinate grid and contour output remain different work; zero warm
allocations exclude first use and retained workspace memory.

Reproduce by checking out the full measured commit, building Release, freezing the output
directory and running the launcher above. The retained raw files are **local only**, under
`artifacts/area-real-contours-0e53b07/`; they are not downloadable from this repository.
SHA-256 values identify the exact local evidence:

| File | SHA-256 |
|---|---|
| `run-1.json` | `24d67fd9a254ca52b0536c5ab1f27f489efd59480978998826191c9c1193b21c` |
| `run-2.json` | `ac78cfdf0135776a2f2ca8885a2e43e8d43eafc7641289bc55036e87bc555b93` |
| `run-3.json` | `c47438f6855191ecbcd69a6bf5eb352f7ffc6217c57857c28bd170ef4391bbfe` |
| `validation.json` | `4bff02158669f97c8f43e0effd6ddd88a72c74c2e4d24610f61576715138ae89` |
| `process-order.json` | `19b63773b85c521d82ea0b65fd8625a6fde0b2863acc635574159481cb343b6a` |
| `evidence.json` | `6553cdf2344c7e1974d37b4f0e1e312d80ca685be90c069af269b7d7382da5ea` |
| `negative-controls.json` | `90d7c2f921805bdd5a20cba74784e614fabfba4f77820db0a4face9cb6c1936f` |
