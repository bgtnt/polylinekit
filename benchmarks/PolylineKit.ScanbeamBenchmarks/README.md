# Experimental bounded-integer scanbeam

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
full gate. A [double-input incremental scanline](DOUBLE-SCANLINE.md) is proposed
as a separate experiment; it is not implemented by this integer prototype.

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
