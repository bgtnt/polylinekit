# Allocation and SIMD evaluation

This experiment measures the cost of the existing comparison operations. It does
not test whether area ranks shapes better than RMS, change any area definition,
or claim to improve Clipper's clipping algorithm. No NuGet package was produced.

## Decision and scope

Accept the portable cleanup plus the .NET 10 SIMD target. The final SIMD
candidate passes every primary threshold and has no confirmed control regression.
Both tested source revisions pass; standalone cleanup and modern scalar do
not pass all primary thresholds. SIMD is useful here, but most of the total gain
must not be attributed to SIMD alone.

Final measurements for the complete normalize → align → area workflow:

| Input | Vertices | Original / SIMD time, µs | Paired time ratio, median [range] | Time reduction | Original / SIMD bytes/op |
| --- | ---: | ---: | --- | ---: | ---: |
| Exact transformed shape | 256 | 128.8 / 94.3 | 0.7265 [0.7012, 0.7332] | 27.3% | 235,168 / 189,248 |
| Perturbed shape | 256 | 150.6 / 114.2 | 0.7621 [0.7411, 0.7885] | 23.8% | 204,400 / 158,480 |
| Exact transformed shape | 1024 | 503.0 / 362.1 | 0.7235 [0.7046, 0.7361] | 27.7% | 905,552 / 724,368 |
| Perturbed shape | 1024 | 575.8 / 434.0 | 0.7492 [0.7345, 0.7538] | 25.1% | 747,296 / 566,112 |

Absolute times are medians of the three process medians; the decision uses the
median of paired ratios, which need not equal the ratio of those absolute
medians. Allocation reductions are 19.5%, 22.5%, 20.0% and 24.2%, respectively.
Thus acceptance is through the time criterion, not the 30% allocation criterion.

The acceptance rule was fixed in [protocol v1](optimization-protocol.md): each
of the exact and perturbed normalize → align → area workflows, at both 256 and
1024 vertices, must reduce median paired time by at least 20% or allocation by
at least 30%. Forty explicitly labeled control cases guard against regressions.
The 16/64-point primary cases remain visible, outside the primary threshold.

## What changed

The portable cleanup removes temporary lists and redundant point snapshots,
preallocates Clipper paths, specializes array traversal and shares validated
input inside the implementation. Normalization computes actual returned bounds
in its existing validation pass. Closed correspondence iteration replaces
per-sample remainder operations with equivalent cyclic index advancement;
sample order, sums, tie selection and returned point order stay unchanged.

The additional .NET 10 target vectorizes only independent affine applications
to contiguous `Point2[]` inputs of at least 32 points. It uses packed doubles,
two points per 256-bit vector or one point per 128-bit vector. Normalization and
alignment benefit when they call this operation. Covariance, RMS, graph area
and other reductions retain their scalar order. Clipper remains unchanged.

The dispatch threshold was selected before timing and was not tuned against
these results. It is not a claimed universal crossover. Inputs smaller than
32 points, other list implementations, unsupported hardware, an unexpected
point layout, and arrays too large to reinterpret as a double span fall back
to scalar. Invalid vector blocks replay scalar validation in original order.
There are no fused multiply-adds or horizontal reductions in this SIMD kernel.

## Source and measurement provenance

Repository: [bgtnt/polylinekit](https://github.com/bgtnt/polylinekit).

| Component | Commit |
| --- | --- |
| Original portable core | `e6b4978522b3221b36f054024be338b8e3618577` |
| Common frozen benchmark harness | `c74fed307eca6bc2715ef910238e3b475c381824` |
| Portable cleanup core | `3142658f08705500675615aa0bef4f49f59edc9e` |
| First SIMD candidate | `53d9d2db12d91a68dcfcd53ef56563eedc896325` |
| Final core, scalar/SIMD switch | `2d76cc72babbf29673252cb573b054946aba2632` |

Every variant executes the same frozen harness DLL; only the core DLL changes.
Modern scalar and SIMD use the same core binary, differing by the
`PolylineKit.DisableSimd` application switch. This keeps the effect of portable
cleanup, target framework and explicit SIMD separate. All four run on the same
.NET 10.0.12 runtime, with tiered compilation disabled, on Windows x64 build
26200, Intel Family 6 Model 158 Stepping 12, 16 logical processors, AVX2 available.
The build SDK was 10.0.401. No build, test or profiling job ran concurrently with
timing; ordinary operating-system activity was not controlled.

There are two separately preserved three-round sets. The first candidate already
passed the SIMD performance gates. Review then identified an extreme-size
compatibility issue: `MemoryMarshal.Cast<Point2,double>` requires twice the point
count to fit an `int` span length. The final core adds scalar fallback above
`int.MaxValue/2`; this is a correctness guard, not a threshold adjustment based on
timing. It also adds boundary tests and verification-header assertions. One final
complete measurement set validates that revision. The sets are not pooled across
different binaries, and neither set replaces unfavorable observations.

Both sets contain 48 cases × 4 variants × 3 fresh processes, each with nine
timed batches. Across both sets this is 1,152 case/process records and 10,368
batch measurements. Analysis uses the median of three paired process ratios;
batch samples are not treated as independent processes. Reported ranges are
descriptive, not confidence intervals. Allocations mean managed bytes allocated
on the current thread, not retained or peak memory.

The [initial](../results/optimization/initial/analysis.md) and
[final](../results/optimization/final/analysis.md) analyses report every paired
ratio; raw run JSON is adjacent. The `variants.json` files contain source and
binary SHA-256 values; `inputs.json` contains exact coordinates. Its byte hash is
`88544e92592d961e323908103fdf8152a5cc789213de51960b81baadc544eeae` in both sets.
Git attributes preserve the original evidence bytes, including line endings.

## Attribution and controls

The following time ratios separate the three steps. Lower is better. These are
medians of paired ratios, so multiplying table columns is not an exact identity.

| Input / vertices | Cleanup / original | Modern scalar / cleanup | SIMD / modern scalar |
| --- | ---: | ---: | ---: |
| Exact / 256 | 0.8026 | 1.0042 | 0.9047 |
| Perturbed / 256 | 0.8277 | 1.0071 | 0.9071 |
| Exact / 1024 | 0.8183 | 0.9998 | 0.9015 |
| Perturbed / 1024 | 0.8165 | 0.9934 | 0.9293 |

Portable cleanup supplies substantial time and allocation reductions. Changing
the target while keeping scalar dispatch has little consistent effect. Explicit
SIMD contributes approximately 7–10% lower time to these complete workflows and
does not reduce allocation further. Standalone cleanup and modern scalar fail
the predeclared complete-workflow acceptance gate; their useful stage-level
improvements are retained as part of the accepted combined implementation.

For array `Apply`, SIMD/modern-scalar time ratios are 0.2120, 0.1602 and 0.1355
at 64/256/1024 points. These larger local gains do not imply the same speedup for
the complete workflow. At 16 points `Apply` stays scalar and the corresponding
ratio is 0.9983. Normalization's SIMD/modern-scalar ratios at 256/1024 are
0.6440/0.6385. Closed phase search with 256 samples is dominated by scalar search;
cleanup/original at 1024 vertices is 0.4950, while SIMD/modern-scalar is 0.9547.
The reduction in cyclic-index overhead must not be called SIMD acceleration.

All forty final SIMD control medians are at or below 1.0004 of the original.
There are still isolated slower rounds: at 64 vertices, exact and perturbed
bridged area have ratios 1.1473 and 1.1811 in one round, and closed XOR reaches
1.0575 in another. Their other rounds and medians are below 1, so none meets the
predeclared regression rule. These observations are retained, not removed as
outliers. Modern scalar has unresolved graph-control medians at 16/64; it already
fails the primary gate and is not selected as an independent improvement.

Even the unchanged graph kernel varies by several percent between processes.
That variation is not evidence of vectorizing the graph algorithm. Allocation
counts are stable across all three rounds, and graph allocation remains zero.

## Correctness and generated instructions

All 1,851 prior assertions remain, including the documented floating-point
counterexample. The extra 1,490 assertions cover scalar/SIMD agreement, actual
returned transforms and bounds, input/output validation, degeneracy, duplicate
sampling, reversal/phase choices, snapshot independence and vector boundaries.
Signed zeros, subnormals, minimum normal values and exact ±1e100 limits are checked
bit for bit in packed lanes and odd scalar tails. No mathematical accuracy claim
is inferred from faster execution.

The suite passes 3,341 checks per mode: portable, modern, forced application
scalar, AVX disabled and all hardware intrinsics disabled. The script asserts
the actual loaded target, switch and ISA flags. The ordinary contract checks
obey the forced scalar switch; equivalence checks intentionally toggle it in
both directions. The no-intrinsics process stays scalar throughout. The enormous
array fallback is reviewed by its count guard; allocating a 16+ GB fixture was
not attempted.

Windows and Linux CI run the same five modes and the runnable example. The
validation record below identifies the actual reviewed source and CI run:
[verify run 35888708031](https://github.com/bgtnt/polylinekit/actions/runs/35888708031)
passed at `2d76cc72babbf29673252cb573b054946aba2632`. The subsequent documentation
and evidence commit does not alter the measured core sources.

The final [256-bit](../results/optimization/final/simd-256-jit.txt) disassembly
contains `vmulpd`/`vaddpd` on YMM registers; the
[128-bit fallback](../results/optimization/final/simd-128-jit.txt) contains
`mulpd`/`addpd` on XMM registers. These are actual generated packed instructions,
not deductions from feature availability. Neither listing contains fused
multiply-add instructions. JIT metadata records the tested DLL hash and commands.

## Profiling and reproduction

Baseline [profile reports](../results/optimization/profiles/metadata.json) guided
the bounded changes. They show Clipper work alongside our copies, array/list
growth, resampling and transformations. The profiler combined sampled thread
stacks with GC events; its inclusive/exclusive percentages are approximate stack
attribution, not exact CPU or wall-time fractions. Stage timings and allocation
measurements above supply the quantitative evidence. Raw traces remain local;
their hashes and regeneration commands are recorded.

From a full clone, with the .NET 10 SDK, PowerShell 7 and Python 3 installed:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
./scripts/verify-implementations.ps1
dotnet run --project examples/Basic -c Release --no-build

# Read archived evidence without rebuilding historical binaries.
python scripts/analyze-optimization.py results/optimization/initial --evidence-only --output artifacts/initial-analysis.json --markdown artifacts/initial-analysis.md
python scripts/analyze-optimization.py results/optimization/final --evidence-only --output artifacts/final-analysis.json --markdown artifacts/final-analysis.md

# Recreate common harness and all frozen core versions from Git objects.
# Commit any tracked edits before timing; outputs stay under ignored artifacts/.
$manifest = ./scripts/prepare-optimization-variants.ps1 | Select-Object -Last 1
./scripts/benchmark-optimization.ps1 -VariantsFile $manifest -OutputDirectory artifacts/optimization/recheck
python scripts/analyze-optimization.py artifacts/optimization/recheck --manifest $manifest --verify-binaries
```

Use `-InitialCandidate` when preparing the first candidate. Builds occur in unique
source snapshots; they do not change the working checkout or delete directories.
The manifest records actual regenerated binary hashes, which can differ with
source paths, SDK/build metadata and platform. Recorded source revisions remain
fixed. PowerShell/Python and `dotnet-trace` are development tools only; the library
gains no runtime package dependency. Python uses only its standard library.
The preparation script was itself run successfully: all four archived-source
builds passed and all 48 observations per rebuilt variant matched the original
baseline bit for bit; see [reproduction validation](../results/optimization/reproduction-validation.json).

To regenerate the baseline diagnostic profiles, install the optional local tool
once, then run each named workload (use the baseline runner from the manifest):

```powershell
dotnet tool install dotnet-trace --version 10.0.745401 --tool-path artifacts/tools
$env:DOTNET_TieredCompilation = '0'
artifacts/tools/dotnet-trace collect --profile dotnet-sampled-thread-time,gc-verbose --output artifacts/baseline.nettrace -- dotnet <baseline-runner.dll> profile-optimization exact/normalize-align-area 1024 4000
artifacts/tools/dotnet-trace report artifacts/baseline.nettrace topN -n 20 --verbose
```

The other profiled workloads are `perturbed/normalize-align-area` and
`closed/fit256`. Profiling is separate from timing. To inspect packed JIT output
on an AVX-capable x64 machine, start a fresh shell and use the SIMD runner:

```powershell
$env:DOTNET_TieredCompilation = '0'
$env:POLYLINEKIT_FORCE_SCALAR = '0'
$env:DOTNET_JitDisasm = '*Apply256*'
$env:DOTNET_JitStdOutFile = "$PWD/artifacts/simd-256-jit.txt"
dotnet <simd-runner.dll> profile-optimization exact/apply 1024 300
$env:DOTNET_EnableAVX = '0'
$env:DOTNET_JitDisasm = '*Apply128*'
$env:DOTNET_JitStdOutFile = "$PWD/artifacts/simd-128-jit.txt"
dotnet <simd-runner.dll> profile-optimization exact/apply 1024 300
```

Close that diagnostic shell before benchmarks; the ISA and disassembly settings
are diagnostic overrides. These commands describe the measured x64 paths and do
not promise that another architecture emits the same instruction mnemonics.

## Limits and next work

Performance evidence is from one Windows x64 machine. Linux CI verifies behavior,
not equivalent speed; Arm64 speed and machine instructions have not been measured.
The three fresh-process rounds reduce some variability but do not provide broad
hardware or workload coverage. The portable implementation remains useful, but
its own primary threshold result must not be presented as a SIMD result.

These fixtures exercise exact and nonzero-residual open fits, closed phase search,
graph integration, bridged fill and XOR. They are not a large corpus of arbitrary
self-intersecting trajectories or a shape-ranking study. Numerical conditioning,
Clipper quantization, sampled correspondence and the lack of a worst-deviation
guarantee remain as described in the [API contract](comparison-api.md).

The next independent review can reproduce this experiment and then evaluate
area versus RMS as ranking signals. Neither that study nor NuGet publication is
part of this completed optimization scope.
