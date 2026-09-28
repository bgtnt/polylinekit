param([string]$Output = 'artifacts/multiring-checks')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$savedScalar = $env:POLYLINEKIT_FORCE_SCALAR
Push-Location $root
try {
    $destination = [IO.Path]::GetFullPath($Output)
    if (Test-Path -LiteralPath $destination) { throw 'Use a fresh output directory.' }
    New-Item -ItemType Directory -Path $destination | Out-Null
    $runner = Join-Path $destination 'runner'
    Copy-Item -LiteralPath 'tests/PolylineKit.MultiRingChecks/bin/Release/net10.0' -Destination $runner -Recurse
    $modes = @(
        @{ Name='modern'; Target='net10.0'; Scalar='0' },
        @{ Name='modern-scalar'; Target='net10.0'; Scalar='1' },
        @{ Name='portable'; Target='netstandard2.0'; Scalar='0' }
    )
    foreach ($mode in $modes) {
        Copy-Item -LiteralPath "src/PolylineKit.Core/bin/Release/$($mode.Target)/PolylineKit.Winding.dll" -Destination $runner
        $binary = Join-Path $runner 'PolylineKit.Winding.dll'
        $hash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
        $env:POLYLINEKIT_FORCE_SCALAR = $mode.Scalar
        $reportPath = Join-Path $destination "$($mode.Name).json"
        & dotnet (Join-Path $runner 'PolylineKit.Experiments.dll') $root $reportPath
        if ($LASTEXITCODE) { throw "Multi-ring checks failed: $($mode.Name)" }
        if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $hash) { throw 'Core binary changed during validation.' }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $expected = if ($mode.Target -eq 'netstandard2.0') { '.NETStandard,Version=v2.0' } else { '.NETCoreApp,Version=v10.0' }
        if ($report.CoreTarget -ne $expected -or $report.ForceScalar -ne ($mode.Scalar -eq '1')) {
            throw "Wrong implementation or scalar mode: $($mode.Name)"
        }
        Write-Output "PASS $($mode.Name): $($report.Analytic.assertions) analytic assertions; $($report.Gis.FullMetricCalls) full GIS comparisons. Core SHA-256 $hash"
    }
} finally {
    $env:POLYLINEKIT_FORCE_SCALAR = $savedScalar
    Pop-Location
}
