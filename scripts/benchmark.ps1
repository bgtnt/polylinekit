param(
    [string]$OutputDirectory = 'artifacts/benchmarks',
    [ValidateSet('Graphs', 'Transforms', 'Winding', 'WindingVsClipper2')][string]$Suite = 'Graphs'
)
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('OutputDirectory')) {
    $OutputDirectory = Join-Path $OutputDirectory $Suite.ToLowerInvariant()
}
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
    $command = switch ($Suite) { 'Transforms' { 'benchmark-transforms' } 'Winding' { 'benchmark-winding' } 'WindingVsClipper2' { 'benchmark-clipper' } default { 'benchmark' } }
    for ($run = 1; $run -le 3; $run++) {
        dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll $command $OutputDirectory $run $revision
        if ($LASTEXITCODE) { throw "Benchmark run $run failed." }
    }
    if ($Suite -eq 'Winding') {
        dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll summarize-winding $OutputDirectory
        if ($LASTEXITCODE) { throw 'Winding summary failed.' }
    }
    if ($Suite -eq 'WindingVsClipper2') {
        dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll summarize-clipper $OutputDirectory
        if ($LASTEXITCODE) { throw 'Clipper comparison summary failed.' }
    }
} finally {
    $env:DOTNET_TieredCompilation = $oldTiered
    Pop-Location
}
