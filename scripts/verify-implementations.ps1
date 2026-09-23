$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$savedScalar = $env:POLYLINEKIT_FORCE_SCALAR
$savedHardware = $env:DOTNET_EnableHWIntrinsic
$savedAvx = $env:DOTNET_EnableAVX
try {
    $directory = Join-Path (Get-Location) ('artifacts/implementation-checks-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    Get-ChildItem -LiteralPath 'experiments/PolylineKit.Experiments/bin/Release/net10.0' -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $directory
    }
    $modes = @(
        @{Name='portable'; Target='netstandard2.0'; Scalar='0'; Hardware='1'; Avx='1'},
        @{Name='modern'; Target='net10.0'; Scalar='0'; Hardware='1'; Avx='1'},
        @{Name='modern-forced-scalar'; Target='net10.0'; Scalar='1'; Hardware='1'; Avx='1'},
        @{Name='modern-without-avx'; Target='net10.0'; Scalar='0'; Hardware='1'; Avx='0'},
        @{Name='modern-without-intrinsics'; Target='net10.0'; Scalar='0'; Hardware='0'; Avx='0'}
    )
    foreach ($mode in $modes) {
        Copy-Item -LiteralPath "src/PolylineKit/bin/Release/$($mode.Target)/PolylineKit.dll" -Destination $directory
        $env:POLYLINEKIT_FORCE_SCALAR = $mode.Scalar
        $env:DOTNET_EnableHWIntrinsic = $mode.Hardware
        $env:DOTNET_EnableAVX = $mode.Avx
        Write-Output "Verifying $($mode.Name)"
        dotnet (Join-Path $directory 'PolylineKit.Experiments.dll') check
        if ($LASTEXITCODE) { throw "Checks failed: $($mode.Name)" }
    }
} finally {
    $env:POLYLINEKIT_FORCE_SCALAR = $savedScalar
    $env:DOTNET_EnableHWIntrinsic = $savedHardware
    $env:DOTNET_EnableAVX = $savedAvx
    Pop-Location
}
