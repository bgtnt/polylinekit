# Experimental area sweeps

This is a research executable, not a library backend or supported public API.
It tests whether scanbeams and grouped active-edge crossings improve the dense
integer-grid cases where the general binary64 engine loses to Clipper.
See the [protocol](PROTOCOL.md) for the fixed inputs, numerical contract,
comparison baseline and go/no-go threshold.
The [measured results](RESULTS.md) retain two failed arithmetic variants and
the final passing grid experiment, including its losses on other shapes.

`IntegerScanbeam` accepts one implicitly closed walk, or the intersection of two
independently closed walks, with at least three supplied points per walk and
8192 in total, integer coordinates in [-524288, 524288], and NonZero or EvenOdd fill.
It rejects other inputs instead of quantizing or silently falling back.
The reusable instance owns mutable buffers and is neither thread-safe nor
reentrant. The shipping `WindingArea` APIs and their broader contracts are
unchanged. There is no new runtime dependency in either library project.
Within this accepted domain, coordinates entirely within ±2048 select proven
Int64 arithmetic; otherwise exact event arithmetic uses Int128. Neither branch
rounds intersections onto a grid. Final area integration remains binary64.

The extension adds a [real-contour intersection protocol](REAL-PROTOCOL.md) and
[wider-coordinate controls](WIDE-PROTOCOL.md). Run `check-real <directory>` and
`check-wide` for their untimed validation. Use `benchmark-real`/`summarize-real`
or `benchmark-wide`/`summarize-wide` with the same arguments as the original
runner below, in distinct artifact directories. The original results remain
historical measurements of their explicitly identified commits.
The [expanded results](EXPANDED-RESULTS.md) show that neither extension passes its
full gate. The separate [guarded double sweep protocol](DOUBLE-PROTOCOL.md)
tests the [double-input design](DOUBLE-SCANLINE.md) on original coordinates.
Its [measured results](DOUBLE-RESULTS.md) pass the numerical checks but fail all
four speed gates: about 101 ms per table versus 12–13 ms for Clipper and 9–10 ms
for existing Winding. There are no fallbacks or warm allocations on that set.
Use `check-double <directory>`, `benchmark-double <directory> <run> <revision>`
and `summarize-double <directory>`. This additional prototype does not change
the integer engine or the shipping APIs.

The [common-Y and cached-X ablations](DOUBLE-ABLATION-RESULTS.md) reduce that
prototype to about 50 ms per table, still slower than Clipper and Winding.
Their [fixed protocol](DOUBLE-ABLATION-PROTOCOL.md) uses all four flag variants
and both competitors. Commands `check-double-ablation`,
`benchmark-double-ablation` and `summarize-double-ablation` use the same arguments
as their double counterparts. `profile-double-ablation <method> <seconds>`
repeats the warm county table separately from timing; `profile-summary.py`
summarizes a dotnet-trace Speedscope trace. These optional tools are for research.

The [scalar filter experiment](SCALAR-FILTER-RESULTS.md) adds a certified strict
order filter ahead of interval comparisons, saving about 20% against the optimized
sweep. It remains slower than both competitors. See the
[numerical derivation](SCALAR-FILTER-NUMERICS.md) and
[fixed protocol](SCALAR-FILTER-PROTOCOL.md). Commands `check-scalar-filter`,
`benchmark-scalar-filter` and `summarize-scalar-filter` use the same arguments as
the double commands. Profile `Guarded-combined` and `Guarded-filtered` separately.

The [prepared sweep](PREPARED-SWEEP-RESULTS.md) reuses immutable slopes/scalar bounds
and merges previously sorted endpoint streams, saving another 47% in warm tables.
It still loses to the competitors. Its [fixed protocol](PREPARED-SWEEP-PROTOCOL.md)
also charges preparation of incoming queries and records retained array payload.
Commands `check-prepared-sweep`, `benchmark-prepared-sweep` and
`summarize-prepared-sweep` use the same argument shapes as the double commands.
Profile `Guarded-filtered` and `Guarded-prepared` separately.

The [direct-access experiment](DIRECT-SWEEP-RESULTS.md) reads the two immutable
edge/scalar arrays directly. It does not improve on copied mode. Its
[fixed protocol](DIRECT-SWEEP-PROTOCOL.md) includes separate reruns of the previous
binary because shared accessors can also change the copied baseline's timing.
Commands `check-direct-sweep`, `benchmark-direct-sweep` and `summarize-direct-sweep`
use the same argument shapes. Profile `Guarded-prepared` and `Guarded-direct`
separately. The direct flag defaults off; original commands remain available.

The [area arithmetic experiment](AREA-ARITHMETIC-RESULTS.md) replaces generic
interval multiplication with specialized endpoint products inside trapezoid
integration. Its [derivation](AREA-ARITHMETIC-NUMERICS.md) preserves every bound
bit, including subnormals and negative outward lower bounds. It saves 2.3–3.4%
against the contemporary baseline, but remains slower than Winding and Clipper.
The [protocol](AREA-ARITHMETIC-PROTOCOL.md) also reruns the preceding binary.
Commands `check-area-arithmetic`, `benchmark-area-arithmetic` and
`summarize-area-arithmetic` use the same argument shapes. The option defaults
off, and both measured sweep variants use copied prepared geometry.

The [filled-gap coalescer](GAP-COALESCING-RESULTS.md) combines contiguous filled
intervals between the same ordered edges, provided all boundary Y enclosures
are point-valued. This cuts integrations by 72% and complete-query time by
22–25% against its contemporary baseline, with changed but still certified area
rounding. See the [proof](GAP-COALESCING-NUMERICS.md) and
[protocol](GAP-COALESCING-PROTOCOL.md). Commands `check-gap-coalescing`,
`benchmark-gap-coalescing` and `summarize-gap-coalescing` use the same argument
shapes. The flag defaults off; the shipping engine and original commands remain
unchanged. The new buffer cost and the failed competitor gates are reported.

The [active-pass experiment](ACTIVE-PASSES-RESULTS.md) streams winding values
through bands without crossings and builds the sorted copy incrementally.
It removes 57% of counted active visits and saves 18�22% of complete-query
time, retaining area/certificate bits. Warm tables now beat Clipper on this
workload but still lose to Winding, and the original competitor gates fail.
The [protocol](ACTIVE-PASSES-PROTOCOL.md) includes a historical-binary control.
Commands `check-active-passes`, `benchmark-active-passes` and
`summarize-active-passes` use the same argument shapes. The seventh option
defaults off; there are no additional workspace arrays.

The [scalar-view experiment](SCALAR-VIEW-PROTOCOL.md) borrows immutable scalar
filter metadata while retaining copied geometry and existing scratch arrays.
It isolates the scalar copy observed in sampled profiles. Commands
`check-scalar-view`, `benchmark-scalar-view` and `summarize-scalar-view` use the
same argument shapes. The eighth option defaults off. Profiling accepts an
optional `warm-table` or `prepare` scope after the duration.

From the repository root, using PowerShell:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
$runner = 'benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll'
dotnet $runner check
if ($LASTEXITCODE) { throw 'Correctness failed.' }
if (git status --porcelain) { throw 'Commit the measured implementation first.' }
$revision = git rev-parse HEAD
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    foreach ($run in 1..3) {
        dotnet $runner benchmark artifacts/scanbeam $run $revision
        if ($LASTEXITCODE) { throw 'Benchmark failed.' }
    }
    dotnet $runner summarize artifacts/scanbeam
    if ($LASTEXITCODE) { throw 'Evidence validation failed.' }
} finally { $env:DOTNET_TieredCompilation = $previousTiering }
```

The summarizer requires the measured binaries, validates the entire matrix,
sample medians and identities, and recomputes output values. It writes
`summary.md` and `evidence.json`; a valid failed performance gate is reported as
FAIL rather than treated as a tool failure. Raw JSON belongs in ignored
`artifacts/`, not the public source tree. CI runs correctness, never timings.

Clipper2's active-edge/scanbeam strategy is a conceptual reference; no Clipper
source is copied. The pinned benchmark dependency and the existing exact area
oracle remain separate from the prototype. This experiment does not measure
a C++ implementation or justify claims about language speed.
