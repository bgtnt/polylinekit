$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$savedScalar = $env:POLYLINEKIT_FORCE_SCALAR
$savedHardware = $env:DOTNET_EnableHWIntrinsic
$savedAvx = $env:DOTNET_EnableAVX
try {
    $modes = @(
        @{Name='modern'; Scalar='0'; Hardware='1'; Avx='1'},
        @{Name='forced-scalar'; Scalar='1'; Hardware='1'; Avx='1'},
        @{Name='without-avx'; Scalar='0'; Hardware='1'; Avx='0'},
        @{Name='without-intrinsics'; Scalar='0'; Hardware='0'; Avx='0'}
    )
    foreach ($mode in $modes) {
        $env:POLYLINEKIT_FORCE_SCALAR = $mode.Scalar
        $env:DOTNET_EnableHWIntrinsic = $mode.Hardware
        $env:DOTNET_EnableAVX = $mode.Avx
        Write-Output "Recognition checks: $($mode.Name)"
        & dotnet experiments/PolylineKit.Recognition/bin/Release/net10.0/PolylineKit.Recognition.dll check
        if ($LASTEXITCODE) { throw "Engine/pipeline checks failed: $($mode.Name)" }
        & dotnet examples/StrokeTemplates/bin/Release/net10.0/StrokeTemplates.dll --check
        if ($LASTEXITCODE) { throw "Consumer checks failed: $($mode.Name)" }
    }
} finally {
    $env:POLYLINEKIT_FORCE_SCALAR = $savedScalar
    $env:DOTNET_EnableHWIntrinsic = $savedHardware
    $env:DOTNET_EnableAVX = $savedAvx
    Pop-Location
}
