param(
    [string]$OutputDirectory = 'results/benchmarks',
    [ValidateSet('Graphs', 'Transforms')][string]$Suite = 'Graphs'
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$oldTiered = $env:DOTNET_TieredCompilation
try {
    $changes = @(git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE) { throw 'Cannot verify repository state before benchmarking.' }
    if ($changes.Count -gt 0) { throw 'Commit tracked changes and remove or commit untracked files before benchmarking. Put scratch output under ignored artifacts/.' }
    $revision = git rev-parse --verify HEAD
    if ($LASTEXITCODE) { throw 'Cannot determine the measured source revision.' }
    dotnet build PolylineKit.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $env:DOTNET_TieredCompilation = '0'
    $command = if ($Suite -eq 'Transforms') { 'benchmark-transforms' } else { 'benchmark' }
    for ($run = 1; $run -le 3; $run++) {
        dotnet experiments/PolylineKit.Experiments/bin/Release/net10.0/PolylineKit.Experiments.dll $command $OutputDirectory $run $revision
        if ($LASTEXITCODE) { throw "Benchmark run $run failed." }
    }
} finally {
    $env:DOTNET_TieredCompilation = $oldTiered
    Pop-Location
}
