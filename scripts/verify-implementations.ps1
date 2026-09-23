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
    $modernFeatures = $null
    foreach ($mode in $modes) {
        Copy-Item -LiteralPath "src/PolylineKit/bin/Release/$($mode.Target)/PolylineKit.dll" -Destination $directory
        $env:POLYLINEKIT_FORCE_SCALAR = $mode.Scalar
        $env:DOTNET_EnableHWIntrinsic = $mode.Hardware
        $env:DOTNET_EnableAVX = $mode.Avx
        Write-Output "Verifying $($mode.Name)"
        $checkLines = @(& dotnet (Join-Path $directory 'PolylineKit.Experiments.dll') check 2>&1)
        $checkExit = $LASTEXITCODE
        $checkLines | ForEach-Object { Write-Output $_ }
        if ($checkExit) { throw "Checks failed: $($mode.Name)" }
        $headers = @($checkLines | Where-Object { $_ -is [string] -and $_.StartsWith('Core target: ') })
        if ($headers.Count -ne 1) { throw "Missing or ambiguous implementation header: $($mode.Name)" }
        $header = [regex]::Match($headers[0], '^Core target: (?<framework>[^;]+); Vector128=(?<v128>True|False); Vector256=(?<v256>True|False); force-scalar=(?<scalar>[01]); simd-disabled=(?<disabled>True|False); process-arch=(?<architecture>[A-Za-z0-9]+)$')
        if (!$header.Success) { throw "Unrecognized implementation header: $($mode.Name)" }
        $features = @{
            Vector128 = [bool]::Parse($header.Groups['v128'].Value)
            Vector256 = [bool]::Parse($header.Groups['v256'].Value)
            Architecture = $header.Groups['architecture'].Value
        }
        $expectedFramework = if ($mode.Target -eq 'netstandard2.0') { '.NETStandard,Version=v2.0' } else { '.NETCoreApp,Version=v10.0' }
        if ($header.Groups['framework'].Value -ne $expectedFramework) {
            throw "Incorrect core assembly target: $($mode.Name)"
        }
        if ($header.Groups['scalar'].Value -ne $mode.Scalar -or
            [bool]::Parse($header.Groups['disabled'].Value) -ne ($mode.Scalar -eq '1')) {
            throw "Scalar environment/switch mismatch: $($mode.Name)"
        }
        if ($mode.Name -eq 'modern') { $modernFeatures = $features }
        if ($null -ne $modernFeatures -and $features.Architecture -ne $modernFeatures.Architecture) {
            throw "Child-process architecture changed: $($mode.Name)"
        }
        if ($mode.Name -eq 'modern-forced-scalar' -and
            ($features.Vector128 -ne $modernFeatures.Vector128 -or $features.Vector256 -ne $modernFeatures.Vector256)) {
            throw 'The application scalar switch must not change available hardware features.'
        }
        if ($mode.Name -eq 'modern-without-intrinsics' -and ($features.Vector128 -or $features.Vector256)) {
            throw 'The no-intrinsics process still reports hardware-accelerated vectors.'
        }
        # AVX is an x86-family capability. Do not require acceleration or an AVX
        # response on ARM; compare 128-bit availability with the actual baseline.
        if ($mode.Name -eq 'modern-without-avx' -and $features.Architecture -in @('X64', 'X86') -and
            ($features.Vector256 -or $features.Vector128 -ne $modernFeatures.Vector128)) {
            throw 'The x86-family no-AVX process did not expose the expected fallback features.'
        }
    }
} finally {
    $env:POLYLINEKIT_FORCE_SCALAR = $savedScalar
    $env:DOTNET_EnableHWIntrinsic = $savedHardware
    $env:DOTNET_EnableAVX = $savedAvx
    Pop-Location
}
