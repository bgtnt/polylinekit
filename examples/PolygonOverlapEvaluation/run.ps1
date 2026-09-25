param(
    [Parameter(Mandatory=$true)][string]$Runner,
    [Parameter(Mandatory=$true)][string]$InputFile,
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][string]$Revision
)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($Revision -notmatch '^[0-9a-fA-F]{40}$') { throw 'Supply a full committed revision.' }
$Revision = $Revision.ToLowerInvariant()
if ((git -C $repository rev-parse HEAD) -ne $Revision) { throw 'HEAD does not match the measured revision.' }
if (git -C $repository status --porcelain) { throw 'Commit all source changes before timing.' }
$runnerPath = (Resolve-Path -LiteralPath $Runner).Path
$inputPath = (Resolve-Path -LiteralPath $InputFile).Path
$outputPath = [System.IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new evidence directory; existing evidence is never overwritten.' }
$runnerName = [System.IO.Path]::GetFileNameWithoutExtension($runnerPath)
$binaryDirectory = [System.IO.Path]::GetDirectoryName($runnerPath)
$names = @($runnerName, 'PolylineKit.Winding', 'Clipper2Lib', 'NetTopologySuite')
$hashes = [ordered]@{}
foreach ($name in $names) {
    $path = Join-Path $binaryDirectory "$name.dll"
    $hashes[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($name -in @($runnerName, 'PolylineKit.Winding')) {
        if (![System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($path)).Contains("+$Revision")) {
            throw "Assembly does not embed the requested revision: $name"
        }
    }
}
$inputHash = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant()
$protocolSource = Join-Path $binaryDirectory 'PROTOCOL.md'
$protocolHash = (Get-FileHash -LiteralPath $protocolSource -Algorithm SHA256).Hash.ToLowerInvariant()
if ($protocolHash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PROTOCOL.md') -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Frozen protocol differs from the clean committed source.'
}
# Allowlisted immutable runtime copy. No PDBs, native launchers or machine paths in evidence.
$frozen = Join-Path $outputPath 'bin'
New-Item -ItemType Directory -Path $frozen -Force | Out-Null
foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $binaryDirectory "$name.dll") -Destination $frozen }
foreach ($suffix in @('.deps.json', '.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $binaryDirectory "$runnerName$suffix") -Destination $frozen
}
Copy-Item -LiteralPath $protocolSource -Destination (Join-Path $frozen 'PROTOCOL.md')
Copy-Item -LiteralPath $inputPath -Destination (Join-Path $outputPath 'corpus.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'summarize.py') -Destination $outputPath
$runtimeFiles = [ordered]@{}
foreach ($relative in @("bin/$runnerName.deps.json", "bin/$runnerName.runtimeconfig.json", 'summarize.py')) {
    $runtimeFiles[$relative] = (Get-FileHash -LiteralPath (Join-Path $outputPath $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$sdk = & dotnet --version
if ($LASTEXITCODE) { throw 'Could not identify the .NET SDK.' }
$identity = [ordered]@{ Revision=$Revision; Sdk=$sdk; Binaries=$hashes; RuntimeFiles=$runtimeFiles; FixtureHash=$inputHash; ProtocolHash=$protocolHash }
$identity | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'launch-identity.json') -Encoding utf8
$oldTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    $launches = @()
    foreach ($run in 1..3) {
        foreach ($name in $names) {
            if ((Get-FileHash -LiteralPath (Join-Path $frozen "$name.dll") -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) {
                throw "Frozen binary changed: $name"
            }
        }
        foreach ($relative in $runtimeFiles.Keys) {
            if ((Get-FileHash -LiteralPath (Join-Path $outputPath $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $runtimeFiles[$relative]) {
                throw "Frozen runtime metadata or summarizer changed: $relative"
            }
        }
        $start = [DateTimeOffset]::UtcNow.ToString('o')
        dotnet (Join-Path $frozen "$runnerName.dll") benchmark (Join-Path $outputPath 'corpus.json') `
            (Join-Path $outputPath "run-$run.json") $run $Revision | Tee-Object -FilePath (Join-Path $outputPath "run-$run-console.txt")
        $code = $LASTEXITCODE
        $launches += [pscustomobject]@{ Run=$run; Revision=$Revision; StartedUtc=$start; FinishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); ExitCode=$code; HarnessHash=$hashes[$runnerName] }
        $launches | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'process-order.json') -Encoding utf8
        if ($code) { throw "Benchmark process $run failed." }
        foreach ($relative in $runtimeFiles.Keys) {
            if ((Get-FileHash -LiteralPath (Join-Path $outputPath $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $runtimeFiles[$relative]) {
                throw "Frozen runtime metadata or summarizer changed during the process: $relative"
            }
        }
    }
    python (Join-Path $outputPath 'summarize.py') $outputPath
    if ($LASTEXITCODE) { throw 'Evidence validation failed.' }
} finally { $env:DOTNET_TieredCompilation = $oldTiering }
