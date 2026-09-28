# Compare the fixed benchmark on the pre-grouping implementation and a clean HEAD.
param([string]$Output)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$baselineRevision = '9cb08131e42ba84cf96190db20c60ae893afa969'
$project = 'tests/PolylineKit.MultiRingChecks/PolylineKit.MultiRingChecks.csproj'
$benchmark = 'tests/PolylineKit.MultiRingChecks/RegionBenchmarks.cs'
$protocol = 'tests/PolylineKit.MultiRingChecks/PROTOCOL.md'
$archivePaths = @('Directory.Build.props', 'global.json', 'LICENSE',
    'src/PolylineKit.Core', 'tests/PolylineKit.MultiRingChecks', 'examples/RegionCoverage/data')
function Git-Text([string[]]$Arguments) {
    $value = @(& git -C $repository @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
    return ($value -join "`n")
}
function Require-CleanHead([string]$Revision) {
    if ((Git-Text @('rev-parse', 'HEAD')) -ne $Revision) { throw 'Candidate HEAD changed.' }
    if (Git-Text @('status', '--porcelain')) { throw 'Use an unchanged, clean committed candidate checkout.' }
}
function Write-NewText([string]$Path, [string]$Text) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text); $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}
function Write-NewJson([string]$Path, $Value) { Write-NewText $Path (ConvertTo-Json -InputObject $Value -Depth 20) }
function File-Hashes([string]$Directory) {
    $prefix = $Directory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($prefix.Length).Replace('\', '/') + ' ' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
function Require-Frozen($Case) {
    if (Compare-Object $Case.Hashes @(File-Hashes $Case.RunnerDirectory)) { throw "Frozen runner changed: $($Case.Name)" }
}
function Invoke-Dotnet([string[]]$Arguments, [string]$Log) {
    Write-NewText $Log ''
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $Log -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
}
$candidateRevision = Git-Text @('rev-parse', 'HEAD')
Require-CleanHead $candidateRevision
$identical = @()
foreach ($path in @($benchmark, $protocol)) {
    $baseBlob = Git-Text @('rev-parse', "${baselineRevision}:$path")
    $candidateBlob = Git-Text @('rev-parse', "${candidateRevision}:$path")
    if ($baseBlob -ne $candidateBlob) { throw "Benchmark/protocol differs between revisions: $path" }
    $identical += [ordered]@{ Path = $path; BaselineBlob = $baseBlob; CandidateBlob = $candidateBlob }
}
if ([string]::IsNullOrWhiteSpace($Output)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $Output = "artifacts/paired-ring-grouping-$stamp-$($candidateRevision.Substring(0, 12))"
}
$destination = if ([IO.Path]::IsPathRooted($Output)) { [IO.Path]::GetFullPath($Output) } else { [IO.Path]::GetFullPath((Join-Path $repository $Output)) }
$artifactsPrefix = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be a fresh directory beneath this repository artifacts/.' }
if (Test-Path -LiteralPath $destination) { throw 'Output already exists; choose a fresh directory. Nothing is overwritten or deleted.' }
New-Item -ItemType Directory -Path $destination | Out-Null
Write-Host "Paired comparison evidence: $destination"
$savedTiering = [Environment]::GetEnvironmentVariable('DOTNET_TieredCompilation', 'Process')
$savedScalar = [Environment]::GetEnvironmentVariable('POLYLINEKIT_FORCE_SCALAR', 'Process')
$started = [DateTime]::UtcNow
$finished = $false
$failure = $null
$cases = @()
Push-Location $repository
try {
    Write-NewJson (Join-Path $destination 'comparison.json') ([ordered]@{
        BaselineRevision = $baselineRevision; CandidateRevision = $candidateRevision
        EqualGitBlobs = $identical; ArchivePaths = $archivePaths
        ProcessOrder = @('baseline-1', 'candidate-1', 'candidate-2', 'baseline-2', 'baseline-3', 'candidate-3')
        TieredCompilation = '0'; ForceScalar = '0'
        Scope = 'Paired runs of the unchanged region benchmark; summaries retain all backends, scenarios and scopes.'
    })
    Invoke-Dotnet @('--info') (Join-Path $destination 'dotnet-info.txt')

    foreach ($definition in @(@{ Name = 'baseline'; Revision = $baselineRevision }, @{ Name = 'candidate'; Revision = $candidateRevision })) {
        $name = $definition.Name
        $revision = $definition.Revision
        $archive = Join-Path $destination "$name-source.zip"
        & git -C $repository archive --format=zip "--output=$archive" $revision -- @archivePaths
        if ($LASTEXITCODE -ne 0) { throw "Source archive failed: $name" }
        Write-NewText (Join-Path $destination "$name-source-tree.txt") (Git-Text (@('ls-tree', '-r', $revision, '--') + $archivePaths))
        Write-NewJson (Join-Path $destination "$name-source-archive.json") ([ordered]@{
            Revision = $revision; Archive = [IO.Path]::GetFileName($archive); Sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        })
        $sourceRoot = $repository
        if ($name -eq 'baseline') {
            $sourceRoot = Join-Path $destination 'baseline-source'
            Expand-Archive -LiteralPath $archive -DestinationPath $sourceRoot
        }
        Push-Location $sourceRoot
        try {
            Invoke-Dotnet @('restore', $project, '--locked-mode', '--nologo') (Join-Path $destination "$name-restore.log")
            Invoke-Dotnet @('build', $project, '-c', 'Release', '-f', 'net10.0', '--no-restore', '--nologo', "-p:SourceRevisionId=$revision") (Join-Path $destination "$name-build.log")
        } finally { Pop-Location }
        $built = Join-Path $sourceRoot 'tests/PolylineKit.MultiRingChecks/bin/Release/net10.0'
        $runnerDirectory = Join-Path $destination "$name-runner"
        New-Item -ItemType Directory -Path $runnerDirectory | Out-Null
        Get-ChildItem -LiteralPath $built | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $runnerDirectory -Recurse }
        $runner = Join-Path $runnerDirectory 'PolylineKit.Experiments.dll'
        if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw "Missing runner: $name" }
        $hashes = @(File-Hashes $runnerDirectory)
        Write-NewText (Join-Path $destination "$name-runner-sha256.txt") ($hashes -join "`n")
        $case = [pscustomobject]@{ Name = $name; Revision = $revision; SourceRoot = $sourceRoot; RunnerDirectory = $runnerDirectory; Runner = $runner; Hashes = $hashes }
        $cases += $case
        Require-Frozen $case
    }
    Require-CleanHead $candidateRevision
    # No builds after this point. Keep other heavy processes stopped during measurement.
    $env:DOTNET_TieredCompilation = '0'
    $env:POLYLINEKIT_FORCE_SCALAR = '0'
    $sequence = 0
    foreach ($run in 1..3) {
        $order = if ($run -eq 2) { @('candidate', 'baseline') } else { @('baseline', 'candidate') }
        foreach ($name in $order) {
            $sequence++
            $case = $cases | Where-Object Name -EQ $name
            Require-CleanHead $candidateRevision
            foreach ($frozenCase in $cases) { Require-Frozen $frozenCase }
            $report = Join-Path $destination "$name-run-$run.json"
            $stem = "$sequence-$name-$run"
            if (Test-Path -LiteralPath $report) { throw "Refusing to overwrite $report" }
            $log = Join-Path $destination "$stem.log"
            Write-NewText $log ''
            $start = [DateTime]::UtcNow
            Write-NewJson (Join-Path $destination "$stem-start.json") ([ordered]@{ Sequence = $sequence; MethodRevision = $case.Revision; Run = $run; StartUtc = $start })
            $returned = $false; $exitCode = $null
            try {
                # Foreground synchronous invocation: cancellation exits this sequence; no retries.
                & dotnet $case.Runner benchmark $case.SourceRoot $run $report 2>&1 | Tee-Object -FilePath $log -Append | Out-Host
                $exitCode = $LASTEXITCODE
                $returned = $true
            } finally {
                Write-NewJson (Join-Path $destination "$stem-window.json") ([ordered]@{
                    Sequence = $sequence; Name = $name; Revision = $case.Revision; Run = $run; StartUtc = $start
                    EndUtc = [DateTime]::UtcNow; Returned = $returned; ExitCode = $exitCode
                })
            }
            if (-not $returned -or $exitCode -ne 0) { throw "Benchmark stopped or failed: $name run $run (exit $exitCode)." }
            foreach ($frozenCase in $cases) { Require-Frozen $frozenCase }
            Require-CleanHead $candidateRevision
            $record = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
            if (-not $record.CoreVersion.EndsWith($case.Revision, [StringComparison]::OrdinalIgnoreCase)) { throw "Wrong embedded revision in $name report." }
            if ($record.CoreTarget -ne '.NETCoreApp,Version=v10.0' -or $record.TieredCompilation -ne '0' -or $record.ForceScalar) { throw "Wrong runtime mode in $name report." }
        }
    }
    foreach ($case in $cases) {
        Require-Frozen $case
        $name = $case.Name
        $summary = Join-Path $destination "$name-summary.json"
        if (Test-Path -LiteralPath $summary) { throw "Refusing to overwrite $summary" }
        Invoke-Dotnet @($case.Runner, 'summarize', (Join-Path $destination "$name-run-1.json"),
            (Join-Path $destination "$name-run-2.json"), (Join-Path $destination "$name-run-3.json"), $summary) (Join-Path $destination "$name-summary.log")
        Require-Frozen $case
    }
    Require-CleanHead $candidateRevision
    $finished = $true
} catch {
    $failure = $_.Exception.Message
    throw
} finally {
    [Environment]::SetEnvironmentVariable('DOTNET_TieredCompilation', $savedTiering, 'Process')
    [Environment]::SetEnvironmentVariable('POLYLINEKIT_FORCE_SCALAR', $savedScalar, 'Process')
    Pop-Location
    Write-NewJson (Join-Path $destination 'execution.json') ([ordered]@{
        StartUtc = $started; EndUtc = [DateTime]::UtcNow; Completed = $finished; Failure = $failure
        Note = 'Completed=false also covers interruption; inspect per-process start/window records. No failed process is retried.'
    })
    Write-Host "Evidence retained at $destination"
}
