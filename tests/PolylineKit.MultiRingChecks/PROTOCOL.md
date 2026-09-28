# Prepared multi-ring region performance protocol

This protocol is fixed before measuring the public prepared-region API. It tests
applicability and preparation/reuse costs, with no speed threshold for success.
Unfavorable results and allocation costs remain part of the report.

## Inputs and operation

Use the complete frozen Census county/district source snapshots from
[`RegionCoverage/data`](../../examples/RegionCoverage/data/README.md). Verify all
six source hashes and the source selection protocol before loading. Retain all
100 counties (104 rings; 7,017 vertices) and 14 districts (19 rings; 5,165 vertices).
Preserve their Polygon/MultiPolygon shell/hole hierarchy. Remove one repeated
closing vertex, translate every feature by the same combined-bounds center, and
orient shells counterclockwise/holes clockwise. No repair, simplification,
pairwise alignment, downloads, or outcome-based exclusions are allowed. Check
original and translated geometries independently with NTS.

The two Census directions each visit all 1,400 pairs, including 285 inclusive
bounds candidates and 1,115 strict bounds rejections. No outer spatial index is
used: all adapters perform the same linear traversal. An invocation consumes
the sum of intersection areas and coverage of the first region, plus candidate
count. It allocates no output matrix and caches no pairwise results.

Eight generated scenarios each compare one region to a copy translated by
`(0.25, 0.25)`, at 16, 64, 256 and 1,024 rings per operand:

- **Disjoint squares:** unit squares on a four-unit lattice. For `r` rings,
  own area is `r` and intersection is `9r/16`.
- **Shells with holes:** each lattice cell has a 2-by-2 shell and a centered
  1-by-1 oppositely oriented hole. There are `r/2` cells. Own area is `3r/2`;
  intersection is `13r/16`. Per cell, the shell intersection is `49/16`,
  subtract the two unit holes, then add their intersection `9/16`: `13/8`.

The lattice spacing prevents intersections with other cells. All coordinates
and reference areas are exact dyadic values. These are valid disjoint polygon
collections under both fill rules, and stress component/hole count. They do not
establish behavior or performance for dense crossing/overlapping ring sets.

## Adapters and preparation

Measure these pinned .NET backends using NonZero filling:

1. **Core prepared region:** `PreparedRegion.FromRings` makes the immutable
   snapshot; `RegionArea.FilledArea` obtains each denominator. Each candidate
   calls `RegionArea.IntersectionArea` on the prepared operands.
2. **Clipper2 C# 2.0.0:** quantize every coordinate onto a `1e-6` unit grid,
   round midpoint away from zero, require scaled magnitudes at most `2^52`, and
   build role-specific `ReuseableDataContainer64` inputs. Cache quantized bounds
   and signed shell-minus-hole own areas. Reuse a `Clipper64` engine and output
   lists within a session; for each candidate, clear it, add both reusable
   inputs, execute intersection, and sum signed output areas. Copying/sorting
   minima and construction of result contours are charged for every query.
3. **NetTopologySuite 2.6.0:** construct Polygon/MultiPolygon objects preserving
   actual hierarchy, cache their envelopes and areas, and call
   `OverlayNGRobust.Overlay(..., Intersection).Area` for every candidate. No
   prepared-predicate early exits, repaired geometry, unioned input, or legacy
   overlay substitution is used.

All three adapters cache own-area denominators but no intersection results.
Core computes its denominator through the general fill operation. Clipper and
NTS can use signed ring areas because all inputs have independently validated
polygon hierarchy; this intentional API/cost difference is included in setup.
All per-method session objects, comparator buffers, copied coordinates, reusable
inputs, geometry objects, bounds and denominators belong to preparation.
Reading, parsing, validity checks and common coordinate/orientation preparation
are shared setup outside timing. Report both setup and query costs.

Before timing, validate every GIS pair in both directions and both fill rules,
plus every generated scenario under both rules and all three backends. Require
identical candidate membership even after Clipper quantization. GIS agreement
with NTS has fixed budgets of 1 square metre and `1e-8` coverage; NTS is an
independent comparison, not an exact oracle. Generated results use the formulas
above with tolerance `2e-12 * max(1, abs(expected))`. Accuracy-only reference
arrays are never passed into any timed session.

## Timing and acceptance

Run a Release .NET 10 build in three **sequential independent processes** with
`DOTNET_TieredCompilation=0`. Use the same frozen binaries and unchanged source
for all processes; record process start/end externally. Rotate backend order by
one slot per process. Each process records source hashes, binary hashes, source
revision, runtime, OS, CPU description, protocol hash and input hashes.

Each scenario/backend has three scopes:

- `prepare-catalogues`: prepare both complete catalogues and consume own-area
  sums and feature counts; perform no pairwise intersections.
- `warm-table`: repeatedly traverse the complete pair population using one
  prepared session and warmed engine workspaces.
- `prepare-plus-one-table`: create a fresh complete session and evaluate one
  whole table. Process/JIT/thread-local workspaces are already warm; this is
  **not cold-process timing**.

For each row invoke once, warm for at least 10 ms, then double batch iterations
until at least 10 ms or 16,384 iterations. Record five batches, forcing GC before
each batch. Measure elapsed Stopwatch ticks and
`GC.GetAllocatedBytesForCurrentThread`; read allocation counters before creating
the Sample record. Consume every returned digest and compare it with the initial
invocation, including warm-up, calibration and every timed iteration. Accumulate
a stability flag without short-circuiting the comparisons; reject a failing
batch after reading the time/allocation counters. The common timer includes
digest consumption and equality-check overhead for every backend.
Amounts are per whole invocation, not per candidate. Allocated bytes measure
managed allocations on the executing thread, not native or retained memory.

The complete matrix is 10 scenarios × 3 backends × 3 scopes × 5 batches ×
3 processes = **1,350 samples**. Retain all five samples per row. Summarization
must verify all rows, metadata identity, stable digests and sample medians, then
report the median and range of the three process medians. Compare costs only
within matching scenario/scope rows. No timing assertion is added to CI.

The public API remains subject to alpha review. This assessment does not promise
zero allocations, universal speedup, exact arithmetic, validity repair, support
for all GIS coordinate reference systems, or external production adoption.

## Reproduce

Use PowerShell from a clean checkout of the revision being assessed. Install the
.NET 10 SDK first. Choose a new output directory for each independent repetition;
the commands refuse to overwrite existing evidence. Keep builds, other benchmarks
and heavy background tasks stopped during the three measured processes.

```powershell
$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
$revision = git rev-parse HEAD
if ($LASTEXITCODE) { throw 'Cannot read source revision.' }
$status = git status --porcelain
if ($LASTEXITCODE -or $status) { throw 'Use a clean committed checkout.' }
$output = Join-Path $repo "artifacts/prepared-region-benchmark-$revision"
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory.' }
$frozen = Join-Path $output 'runner'
New-Item -ItemType Directory -Path $frozen | Out-Null

dotnet restore tests/PolylineKit.MultiRingChecks --locked-mode --nologo
if ($LASTEXITCODE) { throw 'Restore failed.' }
dotnet build tests/PolylineKit.MultiRingChecks -c Release --no-restore --nologo
if ($LASTEXITCODE) { throw 'Build failed.' }
$built = Join-Path $repo 'tests/PolylineKit.MultiRingChecks/bin/Release/net10.0'
Get-ChildItem -LiteralPath $built | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $frozen -Recurse
}
$runner = Join-Path $frozen 'PolylineKit.Experiments.dll'
function BinaryHashes {
    Get-ChildItem -LiteralPath $frozen -Filter '*.dll' | Sort-Object Name |
        ForEach-Object { $_.Name + ' ' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
$binaryHashes = @(BinaryHashes)
$binaryHashes | Set-Content -LiteralPath (Join-Path $output 'binary-sha256.txt')
$revision | Set-Content -LiteralPath (Join-Path $output 'revision.txt')
dotnet --info | Set-Content -LiteralPath (Join-Path $output 'dotnet-info.txt')
if ($LASTEXITCODE) { throw 'Cannot record runtime information.' }

$previousTiering = [Environment]::GetEnvironmentVariable('DOTNET_TieredCompilation', 'Process')
$windows = [System.Collections.Generic.List[object]]::new()
try {
    $env:DOTNET_TieredCompilation = '0'
    foreach ($run in 1..3) {
        $start = [DateTime]::UtcNow
        $result = Join-Path $output "run-$run.json"
        dotnet $runner benchmark $repo $run $result
        $exitCode = $LASTEXITCODE
        $windows.Add([ordered]@{ Run = $run; StartUtc = $start; EndUtc = [DateTime]::UtcNow; ExitCode = $exitCode })
        $windows | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'process-order.json')
        if ($exitCode) { throw "Benchmark run $run failed." }
        if (Compare-Object $binaryHashes @(BinaryHashes)) { throw 'Frozen binaries changed.' }
    }
    dotnet $runner summarize (Join-Path $output 'run-1.json') (Join-Path $output 'run-2.json') (Join-Path $output 'run-3.json') (Join-Path $output 'summary.json')
    if ($LASTEXITCODE) { throw 'Summary validation failed.' }
}
finally {
    [Environment]::SetEnvironmentVariable('DOTNET_TieredCompilation', $previousTiering, 'Process')
}
```

Publish the assessed revision, environment, aggregate results, protocol and
evidence hashes together. Retain the three raw JSON files and process-order log
for independent inspection. The source hashes in every run must remain identical;
the runner rejects source changes during a process and the summarizer rejects
differences between processes.
