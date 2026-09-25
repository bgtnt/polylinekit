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

## Current real-contour result: primary gate passed

Measured source: [`eea1671c6288ea444e98cddcca9ee6d18669124b`](https://github.com/bgtnt/polylinekit/commit/eea1671c6288ea444e98cddcca9ee6d18669124b),
2026-09-25, after the dependency-free Core split and 4 MiB retained-workspace policy.
Environment: .NET 10.0.12, Windows build 26200, x64, Intel Family 6 Model 158
Stepping 12, 16 logical processors; tiered compilation disabled. The same 9 rings,
oracle, timing code, rotations and 10% criterion were used in three sequential
processes (80 rows/process; 1,200 raw samples). No implementation or threshold was
changed in response to the timings.

**PASS:** complete-batch Public/ClosedPath is **1.0021× for NonZero** and
**0.9992× for EvenOdd**. Every individual ring/fill median also remains within 10%.
All Public and ClosedPath samples allocate **0 B/op** after warmup, including the
complete batches. This does not measure first use, peak or retained memory; the
separate oversized-workspace allocation tradeoff still applies.

Each batch calls all nine rings. Times are medians of three process medians;
ranges are observed process medians, not confidence intervals. B/batch includes
all nine calls. The final column compares with the earlier recorded median; it
is a historical comparison, **not a paired causal A/B measurement**.

| Fill | Method | Median µs | Range µs | B/batch | Time vs `0e53b07` |
|---|---|---:|---:|---:|---:|
| NonZero | Public | 1470.731 | 1465.656–1474.013 | 0 | -2.85% |
| NonZero | ClosedPath | 1467.688 | 1463.631–1468.800 | 0 | -4.91% |
| NonZero | Clipper-full | 2205.931 | 2165.944–2228.350 | 557496 | -9.32% |
| NonZero | Clipper-preloaded | 1914.525 | 1899.237–1916.669 | 289000 | -6.54% |
| EvenOdd | Public | 1465.669 | 1465.250–1469.237 | 0 | -21.93% |
| EvenOdd | ClosedPath | 1466.869 | 1464.969–1467.456 | 0 | -3.75% |
| EvenOdd | Clipper-full | 2169.500 | 2149.387–2174.256 | 557496 | -8.76% |
| EvenOdd | Clipper-preloaded | 1890.675 | 1888.944–1907.362 | 289000 | -9.87% |

For this workload Public takes about 1.47 ms per batch: Clipper-full takes
1.48–1.50× as long, and Clipper-preloaded 1.29–1.30× as long. Clipper produces
contours on its coordinate grid; these are not equivalent output contracts.
Both fills use the general engine here, so no integer-specialization speedup
is established.

The three per-process Public/ClosedPath batch ratios are:

| Process | NonZero | EvenOdd |
|---|---:|---:|
| 1 | 0.9979× | 1.0002× |
| 2 | 1.0071× | 0.9992× |
| 3 | 1.0021× | 1.0012× |

All individual ring ratios (Public/ClosedPath; lower is faster):

| Ring | NonZero | EvenOdd |
|---|---:|---:|
| 10001/ring-0 | 1.0061× | 0.9958× |
| 10003/ring-0 | 1.0046× | 1.0152× |
| 10005/ring-0 | 1.0022× | 1.0047× |
| 44001/ring-0 | 1.0073× | 1.0043× |
| 44003/ring-0 | 1.0051× | 0.9998× |
| 44005/ring-0 | 1.0108× | 1.0053× |
| 44007/ring-0 | 1.0034× | 0.9999× |
| 44009/ring-0 | 1.0020× | 0.9954× |
| 44009/ring-1 | 1.0011× | 1.0027× |

The earlier EvenOdd failure remains below. Its +23.19% batch discrepancy was
not reproduced in this run. Clipper also ran faster than in the earlier session,
despite unchanged Clipper binary and inputs. These observations do not isolate
the earlier discrepancy's cause or attribute the new timings to the cache cap.

All 18 validation records, including exact-oracle results, public/ClosedPath
area bits and Clipper results, are unchanged from the earlier revision. The
source-data and Clipper binary hashes are unchanged. Eight deliberately malformed
evidence controls were rejected; unchanged evidence was accepted. The recorded
binary hashes are:

| Binary | SHA-256 |
|---|---|
| `PolylineKit.AreaBenchmarks.dll` | `f70bbdae62dd9f6857975ef2244111f53e4283015f2f46f8e919e5df054eee78` |
| `PolylineKit.Winding.dll` | `0de54d6dc1bd844e8ad0d9c5a2bdd7f06636847595fb22938bcdb114fbdd05c6` |
| `Clipper2Lib.dll` | `e9bf01e01f494ef6072cd46c792ed4f3004288a92b1e98c1f44931e558b8a5d0` |

Reproduce by checking out the full measured commit and following the build/freeze
and launch commands above. Raw evidence remains **local only**, under
`artifacts/area-real-contours-eea1671/`, not downloadable from this repository:

| File | SHA-256 |
|---|---|
| `run-1.json` | `9e8af32f90a1e16ea1cc43f85a3ff9d5e201ce5968c4cb94d4531c29219f359a` |
| `run-2.json` | `42ab743b57b6c3ad2ad089baa811988f86b1150df0127bd45106197997b6588f` |
| `run-3.json` | `565732d450c50215e0efc5f454b1824f3a2b400894fe71d78dca0c7588a6065a` |
| `validation.json` | `4bff02158669f97c8f43e0effd6ddd88a72c74c2e4d24610f61576715138ae89` |
| `process-order.json` | `accec72b01d98f2aba794d023c846c533703b107458acb559adc13dc61f657cf` |
| `evidence.json` | `171a8d9893d23b6cd6a285288f68341c211a6056661a0ba597f0657d11500cc6` |
| `negative-controls.json` | `4f27d0e356b94aad12c3f913cb4a1b1f1a5a77c5f0f6fa18ba8d35e29b423b79` |

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
