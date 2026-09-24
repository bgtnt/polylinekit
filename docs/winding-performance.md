# Winding performance assessment

The C# engine had avoidable work. Reducing that work improves complete API calls
by 1.23–1.39× on the similar-stroke fixtures and 1.33–1.48× on random walks,
relative to `9fff4d2`. Improvements on filled regions and dense degenerate input
are smaller. These measurements do not establish that the implementation is optimal.

Keep the managed engine. A C++ experiment improves an isolated bounds loop but
does not improve the compensated edge sum. No complete-engine C++ advantage has
been demonstrated. An experimental scalar FMA change also lacked a consistent
end-to-end gain and was removed from the final implementation.

## Revisions and environment

- Repository: [bgtnt/polylinekit](https://github.com/bgtnt/polylinekit).
- Baseline: `9fff4d2e7ea453c1c6a6c504485642164cf4065b`.
- Measured final runtime: `6fc1c35fc61ff4d116c65938daddf2255d0e6be2`, on
  `codex/winding-performance`. The evidence/documentation commit follows it.
- Intermediate FMA experiment: `97d1a97a64e592668ccd6c499b36609cab4833bd`.
- Windows build 26200, x64; Intel Family 6 Model 158 Stepping 12, 16 logical
  processors; .NET SDK 10.0.401, runtime 10.0.12, AVX2 available.
- Release builds, `DOTNET_TieredCompilation=0`. The core still targets
  .NET Standard 2.0 and .NET 10. No runtime dependency or public API was added.

The main comparison alternated fresh baseline and final processes in the order
B/C, C/B, B/C, with nine calibrated batch samples per workload in each process.
The table uses the median of the three process medians. Fixture construction is
outside timing. Input hashes, all samples, result values, allocations, revision,
runtime and CPU are retained in
[baseline](../results/winding/performance/baseline/summary.json) and
[optimized](../results/winding/performance/optimized/summary.json), with the
three `run-*.json` files alongside each summary. No other benchmark or build was
run concurrently. These are workstation measurements, not confidence intervals
or cross-platform guarantees; small differences deserve particular caution.

## What changed, and why

1. **Cached bounds before vertex access.** Three doubles per edge store the
   maximum sweep coordinate and the other axis's minimum/maximum, in sweep
   order. Rejected pairs no longer load endpoint indices and coordinates or
   repeat minimum/maximum arithmetic.
2. **Choose the wider input bounds axis.** A fixed X sweep scans millions of
   pairs on a heavily subdivided thin vertical path. The extent heuristic is
   cheap and fixes that case. It does not minimize candidates for every input
   and leaves the worst case quadratic.
3. **Skip work with a known result.** An unsplit whole edge has share 1; it needs
   no division. `EndpointBridged` validates and copies each input once while
   preserving each path's independent distinct-point check, even where the
   joining endpoint is shared.
4. **Reduce exact predicate arithmetic.** Duplicate points and shared horizontal
   or vertical coordinates can establish exact zero directly. Product components
   are reused when differences are exact; zero difference tails need no products.
   Filter bounds, symbolic perturbation rules and extreme-exponent fallback are
   unchanged. Dekker's product remains in both targets.

The compensated area expression, shared crossing points, event ordering and
boundary-chain netting introduced by the preceding numerical reviews are retained.
Changing the sweep axis can change summation order and diagnostic predicate
counts; bitwise equality with the baseline is not promised.

The additional bounds cache retains **24 bytes per edge slot**, plus an array
header, in the per-thread workspace. Warm operations measured here allocate
**0 bytes**, but first calls, buffer growth, nested calls and extreme-exponent
integer arithmetic can allocate. The workspace keeps its largest capacity.

## Complete-operation results

Times are microseconds per public Winding API call; speedup is baseline/final.

| Workload | Vertices per path | Baseline | Final | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Similar strokes | 64 | 13.82 | 9.98 | 1.39× |
| Similar strokes | 1024 | 216.15 | 175.42 | 1.23× |
| Random walks | 64 | 26.13 | 17.65 | 1.48× |
| Random walks | 1024 | 1279.82 | 964.10 | 1.33× |
| Filled regions | 256 | 66.31 | 56.66 | 1.17× |
| Filled regions | 1024 | 289.75 | 267.87 | 1.08× |
| Dense-crossing graphs | 1024 | 269.13 | 246.34 | 1.09× |
| Degenerate integer grid | 256 | 8256.38 | 7539.10 | 1.10× |

The [full comparison](../results/winding/performance/comparison.md) includes all
14 rows. Filled-region gains range from 1.05× to 1.17×, and grid gains from
1.10× to 1.12×. The grid remains slower than Clipper2: at 256 vertices the final
winding call takes 7.54 ms, versus 5.54 ms for the Clipper-based call in the same
run group. Improving the baseline is not the same as winning every workload.

### Orientation stress case and ablations

The [assembly comparison runner](../experiments/PolylineKit.WindingPerf/README.md)
also measures thin subdivided rectangles, using three fresh processes per variant
and five batch samples per workload. The variant order rotates between runs.

At 4096 vertices per path, the vertical rectangle changes from **17,838 µs to
428 µs (41.66×)** with the final scalar algorithm. The horizontal version changes
from 551 µs to 425 µs (1.29×). This is a constructed orientation stress case,
not a representative 41× library speedup. The baseline's candidate counts were
4,196,351 vertically and 10,235 horizontally, despite identical geometry under
rotation and no nonadjacent bounds survivors.

The [ablation table](../results/winding/performance/ablations/summary.md) separates:

- `baseline`: unchanged `9fff4d2`;
- `predicates-only`: baseline sweep/input processing, optimized predicates with FMA;
- `checkout`: all improvements with FMA, at `97d1a97`;
- `scalar`: all improvements with Dekker products, matching the final runtime
  logic. This source-copy experiment predates `6fc1c35`; its source/DLL hashes
  are in [manifest.json](../results/winding/performance/ablations/manifest.json),
  and its predicate source is archived alongside the results. A comment-only
  difference in `WindingArea.cs` does not affect this comparison.

The predicate changes account for much of the grid improvement, whereas bounds,
axis choice and per-edge/input work matter more on ordinary paths. This is not a
complete factorial experiment: it does not assign a separate gain to every change.
Scalar FMA results are generally close to the scalar version, with reversals
between workloads. That is insufficient evidence to retain another hardware path.
FMA is a fused scalar operation here, not packed SIMD.

## C++ experiment

The optional [native probe](../experiments/PolylineKit.NativeProbe/README.md)
implements two identical microkernels using safe C# arrays, C# pointers and C++
through P/Invoke. MSVC 14.51 uses `/O2 /fp:precise /arch:AVX2`, without
`/fp:contract`. Three fresh processes each collect nine samples in rotating
variant order. Buffers are pinned outside timing, no input copy is charged, and
the native transition is included. This is favorable to the native variant;
a public list-based adapter may cost more.

| Kernel, N=1024 | Safe C#, µs | C++ including P/Invoke, µs | C# / C++ |
| --- | ---: | ---: | ---: |
| Compensated edge sum | 4.944 | 5.217 | 0.95× |
| Sparse bounds pairs | 3.351 | 2.413 | 1.39× |
| Dense bounds pairs | 1584.813 | 1148.125 | 1.38× |

Results were checked for bit-identical edge sums and identical pair counts.
All measured variants allocate 0 bytes per operation. Safe C# arrays outperform
the tested C# pointer version on many rows. See the
[full native table and raw runs](../results/winding/performance/native/results.md).

This establishes an opportunity in a particular loop, not an advantage for a
native geometry backend. A port still needs sorting, exact predicates, event
storage, chain netting, input adaptation and workspace ownership. Bounds scan
speed also matters less after rejecting fewer candidates. A whole-engine port
and NativeAOT were not measured.

Do not relax floating-point semantics to obtain a faster but different algorithm:
[MSVC floating-point modes](https://learn.microsoft.com/en-us/cpp/build/reference/fp-specify-floating-point-behavior?view=msvc-170)
control transformations that can invalidate compensated difference/product tails.
[SIMD is also available in .NET](https://learn.microsoft.com/en-us/dotnet/standard/simd);
independent bounds comparisons are a possible experiment, whereas reassociating
compensated sums requires new numerical evidence. A future native backend would
also add [platform-specific packaging](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages).
[NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
is another separately measurable deployment option, not evidence of a speedup.

## Correctness and remaining limits

- Release solution builds pass for both library targets with zero warnings/errors.
- `verify-implementations.ps1` passes **31,611 assertions in each of five modes**,
  including 28,027 winding checks. The added 94 assertions cover axis selection,
  reversed tall subdivided paths, both fill rules, consecutive duplicate points,
  shared bridging endpoints and independent degenerate-input rejection.
- `verify-recognition.ps1` passes the existing 147 engine, 70 pipeline and 93
  consumer checks in all four modes after the final FMA removal. This is
  regression validation, not further recognition development.
- The predicate review exercised 860,000 triples and associated crossing cases
  per mode against exact BigInteger signs and baseline expansion/parameter
  values: modern FMA, disabled intrinsics and portable, with no failures. The
  final scalar arithmetic is the portable/no-intrinsics arithmetic tested there.
- An independent engine review at `97d1a97` passed 140,155 assertions over 2,500
  grid/continuous pairs, metamorphic changes, analytic extreme cases and nested
  list access. Maximum area difference against the baseline was 5.69e-14 in
  absolute terms, or 6.67e-16 after scaling by `max(1, |reference|)`.

The [validation archive](../results/winding/performance/validation/README.md)
contains the build/check logs and supplemental review probe sources/results.

These checks are evidence, not a proof for every input. Existing numerical limits
in [winding-area.md](winding-area.md#robustness) remain: notably about 1e-3
intersection relative error for the extremely long tilted strips at 1e16.
The axis heuristic does not remove quadratic cases, and hashing has expected,
not worst-case, linear cost. The frozen recognition-pair dataset evaluation was
not rerun; these changes make no new recognition claim.

## Reproduction

From each clean checkout, after a locked restore:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
pwsh -File scripts/verify-implementations.ps1
pwsh -File scripts/verify-recognition.ps1
pwsh -File scripts/benchmark.ps1 -Suite Winding -OutputDirectory artifacts/winding-performance
```

The benchmark script runs three processes and rejects uncommitted changes.
To alternate revisions as in this report, build isolated checkouts of the two
recorded commits, set `DOTNET_TieredCompilation=0`, then alternate this command
between checkouts for run numbers 1, 2 and 3:

```powershell
dotnet experiments/PolylineKit.Experiments/bin/Release/net10.0/PolylineKit.Experiments.dll `
    benchmark-winding artifacts/winding-performance <run-number> <full-commit-sha>
dotnet experiments/PolylineKit.Experiments/bin/Release/net10.0/PolylineKit.Experiments.dll `
    summarize-winding artifacts/winding-performance
```

Use actual numeric run numbers and source SHAs in place of the placeholders.
Run the summary after all three measurements. The source/DLL ablations and
native experiment have their own runner READMEs linked above. To regenerate the
checked-in comparison tables, use Python 3 (standard library only):

```powershell
python scripts/summarize-winding-performance.py
```

## Next decision

Review and retain the managed changes before another performance experiment.
The next candidates are initial edge sorting and candidate-search strategies
such as a packed BVH or R-tree, measured including index construction and memory.
An instrumented baseline profile places initial setup/sort near 55 µs at 2048
edges; on the dense grid, search and event sorting dominate instead. The
[profile snapshot](../results/winding/performance/profile.json) records those
phase times and candidate/event counts. Instrumented times locate work and are
not the speedup measurements above. A spatial index
cannot avoid the output cost when most edges really intersect.

Only pursue a native backend if an isolated hot kernel produces a worthwhile
gain through the complete public operation, on representative inputs, while
preserving the numerical contract and accounting for adaptation/deployment costs.
