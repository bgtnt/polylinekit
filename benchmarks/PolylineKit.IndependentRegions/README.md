# A published-package check on new Helsinki regions

This optional assessment answers a concrete numerical question: **how much of
each planning-area feature lies in each city district?** It uses the published
`PolylineKit.Core` **0.1.0-alpha.2** package, its role-aware polygon factories,
and a real corpus not used to develop the library. It compares area results and
preparation/query costs with Clipper2 C# 2.0.0 and NetTopologySuite 2.6.0.

**[Recorded result](RESULTS.md): Core agrees with the configured NTS reference;
the three-way gate fails on the fixed Clipper grid. No timings were run.**
The cost measurement commands below describe the gated protocol, not a completed
performance result.

The historical commands and reports below default to alpha.2. A separate
[alpha.3 replay supplement](REPLAY-alpha.3.md) selects the actual published
alpha.3 identity while preserving this frozen geometry, protocol and alpha.2
assessment. The repeat performs no timings.

The fixture contains all 34 basic districts and 1,095 prospective green/recreation
planning features from the City of Helsinki. It preserves separate components,
836 holes and all supplied coordinate values in EPSG:3067, in metres. Read the
[source inventory and CC BY 4.0 attribution](data/README.md) before interpreting
results. Planning categories may overlap; summing feature coverage is not the
union area of current parks.

The [protocol](PROTOCOL.md) fixes the population, comparators, acceptance budgets
and timing policy before results. Invalid source topology is reported separately;
there is no silent repair, fallback or removal of numerical disagreements.
The runner has exact PackageReferences and **no source ProjectReference**. Its
NTS and Clipper dependencies are only assessment tools; Core remains unchanged.

## Verify the frozen input

From the repository root, using Python's standard library:

```sh
python benchmarks/PolylineKit.IndependentRegions/data/freeze.py
```

This is offline. It verifies source and protocol hashes, complete count queries,
the explicit shell/hole hierarchy, every coordinate and both derived files.

## Run a fresh ordinary package consumer

Install the .NET 10 SDK and PowerShell 7. Run:

```powershell
$tool = 'benchmarks/PolylineKit.IndependentRegions'
$data = (Resolve-Path "$tool/data").Path
$revision = (git rev-parse HEAD).Trim()
$evidence = [IO.Path]::GetFullPath("artifacts/independent-helsinki-$revision")
if (Test-Path -LiteralPath $evidence) { throw 'Choose a fresh evidence directory.' }
$env:POLYLINEKIT_ASSESSMENT_REVISION = $revision
& "$tool/verify-package.ps1" -OutputDirectory "$evidence/package" -KeepWorkspace
```

The script copies only public runner code/configuration/lock files into a new
directory outside the checkout. It restores from **nuget.org only**, with an
initially empty package cache, builds and runs an analytic package probe. It
checks the dependency graph, runtime asset, archive origin, loaded Core DLL hash
and package source revision. Failed workspaces remain available for diagnosis.
The retained path is in `package/verification.json`.

The probe disables the SDK's implicit `library-packs` feed as well as inherited
fallback folders. CI uses `check-package-isolation.ps1` to inject an existing SDK
feed directory and verify that the same isolated restore still uses only
NuGet.org. It runs the ordinary probe once and preserves its proof; this does not
relax the source checks or change any geometric input.

An outside-checkout consumer verifies package integration; it does **not** prove
adoption by an external developer. Do not call it an external production user.

Next, use that same frozen consumer for the full corpus checks:

```powershell
$proof = Get-Content "$evidence/package/verification.json" -Raw | ConvertFrom-Json
if (!$proof.Complete) { throw 'Package isolation proof failed.' }
$runner = Join-Path $proof.Workspace 'bin/Release/net10.0/PolylineKit.IndependentRegions.dll'
dotnet $runner check $data "$evidence/check.json"
if ($LASTEXITCODE) { throw 'Full corpus check failed; retain the report and do not time it.' }
```

The JSON records source/admitted populations, every invalid-input ID/reason,
quantization changes and maximum observed discrepancies with worst pair IDs.
Selected intersection covers every admitted pair under both fills and directions;
full metrics cover all original-bounds candidates and the fixed first 64 disjoint
pairs. Reference union and XOR use separate overlay operations. There is no
numeric pass threshold hidden in the timing code.

For continuous **Core/NTS agreement** checks while retaining known comparator
limits, run the separate wrapper against the same proven consumer:

```powershell
& "$tool/verify-corpus.ps1" -RunnerPath $runner -DataRoot $data -PackageProof "$evidence/package/verification.json" -Output "$evidence/core-check.json"
& "$tool/check-verify-corpus.ps1" -ReportPath "$evidence/core-check.json" -RunnerPath $runner -DataRoot $data -PackageProof "$evidence/package/verification.json"
```

It runs the original full check once, retains its exit code/report/failures and
checks that Core/NTS agreement is complete and bound to the package proof. It
explicitly reports the failed three-way gate; wrapper success **does not allow
timings**. Windows/Linux CI uses this distinction and retains both reports.

## Measure three independent processes

Stop other builds/benchmarks. After successful full checks, retain the same
runner, package binaries, fixture and protocol for all three processes:

```powershell
$previousTiering = $env:DOTNET_TieredCompilation
$previousPgo = $env:DOTNET_TieredPGO
$windows = @()
try {
    $env:DOTNET_TieredCompilation = '0'
    $env:DOTNET_TieredPGO = '0'
    foreach ($run in 1..3) {
        $started = [DateTimeOffset]::UtcNow
        dotnet $runner benchmark $data $run "$evidence/run-$run.json"
        $exitCode = $LASTEXITCODE
        $windows += [ordered]@{ Run = $run; StartUtc = $started; EndUtc = [DateTimeOffset]::UtcNow; ExitCode = $exitCode }
        $windows | ConvertTo-Json | Set-Content "$evidence/process-order.json"
        if ($exitCode) { throw "Benchmark process $run failed." }
    }
    & "$tool/summarize.ps1" -Reports @("$evidence/run-1.json", "$evidence/run-2.json", "$evidence/run-3.json") -CheckReport "$evidence/check.json" -PackageProof "$evidence/package/verification.json" -Output "$evidence/summary.json"
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    $env:DOTNET_TieredPGO = $previousPgo
    Remove-Item Env:POLYLINEKIT_ASSESSMENT_REVISION -ErrorAction SilentlyContinue
}
```

There are 18 matching direction/backend/scope cells, five samples each, across
three processes: 270 samples. Preparation, warm table and preparation plus one
table are separate. Managed allocation counters exclude initial and retained
workspace. The table consumes intersection and first-feature coverage, not
output polygons or a materialized result matrix. Report unfavorable cells and
the range of process medians alongside median timings; do not turn one corpus
into a universal ranking.

The reducer requires both a successful **full corpus check with zero failures**
and the published-package isolation proof before reading timing reports. It
binds their runner/Core/Clipper/NTS binaries and assessment revision, then checks
that inputs, protocol and environment match every measured process. Only tiered
compilation and tiered PGO settings may differ between the check and measurements.
The summary records the hashes of both prerequisite reports. A failed comparator
or quantization check blocks timing reduction even when Core agrees with the
reference; retain the failure instead of publishing a performance summary.

The assessment's alpha.2 binary does not include the later source preparation
optimization. This runner makes no new speed claim for current `main`, changes no
public library API and does not publish a new NuGet package.
