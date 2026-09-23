param(
    [string]$DataDirectory = 'artifacts/recognition/data',
    [string]$Freeze = 'results/recognition/frozen.json',
    [string]$OutputDirectory = 'artifacts/recognition/performance'
)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '../..')
try {
    $changes = @(& git status --porcelain)
    if ($LASTEXITCODE -or $changes.Count) { throw 'Commit source changes before stamping application measurements.' }
    $revision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE) { throw 'Cannot resolve source revision.' }
    $assembly = 'experiments/PolylineKit.Recognition/bin/Release/net10.0/PolylineKit.Recognition.dll'
    if (!(Test-Path -LiteralPath $assembly)) { throw 'Build Release before timing.' }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    & dotnet --info | Set-Content -LiteralPath (Join-Path $OutputDirectory 'dotnet-info.txt') -Encoding utf8
    if ($LASTEXITCODE) { throw 'Cannot record SDK/runtime environment.' }
    $variants = @('uncached-scalar', 'cached-scalar', 'cached-simd')
    for ($run = 1; $run -le 3; $run++) {
        for ($step = 0; $step -lt 3; $step++) {
            $variant = $variants[($run - 1 + $step) % 3]
            & dotnet $assembly performance $DataDirectory $Freeze $OutputDirectory $revision $variant $run
            if ($LASTEXITCODE) { throw "Application measurement failed: $variant run $run" }
        }
    }
} finally { Pop-Location }
