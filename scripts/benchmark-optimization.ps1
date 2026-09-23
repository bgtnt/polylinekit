param(
    [Parameter(Mandatory=$true)][string]$VariantsFile,
    [string]$OutputDirectory = 'artifacts/optimization/measured',
    [int]$FirstRound = 1
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$savedTiered = $env:DOTNET_TieredCompilation
$savedScalar = $env:POLYLINEKIT_FORCE_SCALAR
try {
    $changes = @(git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -or $changes.Count) { throw 'Commit all tracked and non-ignored untracked changes before benchmarking.' }
    $manifest = Get-Content -LiteralPath $VariantsFile -Raw | ConvertFrom-Json
    $variants = @($manifest.Variants)
    if ($variants.Count -ne 4) { throw 'Expected baseline, cleanup, modern-scalar and simd frozen variants.' }
    foreach ($variant in $variants) {
        $core = Join-Path $variant.Directory 'PolylineKit.dll'
        $harness = Join-Path $variant.Directory 'PolylineKit.Experiments.dll'
        if ((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash -ne $variant.CoreDllSha256) { throw "Core hash mismatch: $($variant.Name)" }
        if ((Get-FileHash -LiteralPath $harness -Algorithm SHA256).Hash -ne $manifest.HarnessDllSha256) { throw "Harness hash mismatch: $($variant.Name)" }
    }
    $env:DOTNET_TieredCompilation = '0'
    foreach ($variant in $variants) {
        $env:POLYLINEKIT_FORCE_SCALAR = if ($variant.ForceScalar) { '1' } else { '0' }
        dotnet (Join-Path $variant.Directory 'PolylineKit.Experiments.dll') probe-optimization $OutputDirectory $variant.Name $variant.CoreRevision $manifest.HarnessRevision
        if ($LASTEXITCODE) { throw "Preflight failed: $($variant.Name)" }
    }
    for ($round = $FirstRound; $round -lt $FirstRound + 3; $round++) {
        for ($index = 0; $index -lt 4; $index++) {
            $variant = $variants[($round - 1 + $index) % 4]
            $env:POLYLINEKIT_FORCE_SCALAR = if ($variant.ForceScalar) { '1' } else { '0' }
            dotnet (Join-Path $variant.Directory 'PolylineKit.Experiments.dll') benchmark-optimization $OutputDirectory $round $variant.Name $variant.CoreRevision $manifest.HarnessRevision
            if ($LASTEXITCODE) { throw "Benchmark failed: $($variant.Name), round $round" }
        }
    }
} finally {
    $env:DOTNET_TieredCompilation = $savedTiered
    $env:POLYLINEKIT_FORCE_SCALAR = $savedScalar
    Pop-Location
}
