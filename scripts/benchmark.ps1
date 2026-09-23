param([string]$OutputDirectory = 'results/benchmarks')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$oldTiered = $env:DOTNET_TieredCompilation
try {
    if (git status --porcelain --untracked-files=no) { throw 'Commit tracked source changes before benchmarking.' }
    $revision = git rev-parse HEAD
    dotnet build PolylineKit.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $env:DOTNET_TieredCompilation = '0'
    for ($run = 1; $run -le 3; $run++) {
        dotnet experiments/PolylineKit.Experiments/bin/Release/net10.0/PolylineKit.Experiments.dll benchmark $OutputDirectory $run $revision
        if ($LASTEXITCODE) { throw "Benchmark run $run failed." }
    }
} finally {
    $env:DOTNET_TieredCompilation = $oldTiered
    Pop-Location
}
