[CmdletBinding()]
param(
    [string]$LegacyAssembly,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$baselineCommit = '969f8acc1c0f77c52654e81ace951367bf9b0743'
Push-Location $repository
try {
    $evidence = Join-Path $repository ('artifacts/consumer-verification-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $evidence | Out-Null

    function Build-CurrentConsumer([string]$Project) {
        & dotnet restore $Project --locked-mode
        if ($LASTEXITCODE) { throw "Locked restore failed: $Project" }
        & dotnet build $Project -c $Configuration --no-restore
        if ($LASTEXITCODE) { throw "Build failed: $Project" }
    }

    if ($LegacyAssembly) {
        if (![IO.Path]::IsPathFullyQualified($LegacyAssembly)) { throw '-LegacyAssembly must be an absolute path.' }
        $LegacyAssembly = (Resolve-Path -LiteralPath $LegacyAssembly).Path
        $legacyOrigin = 'provided assembly'
    } else {
        & git cat-file -e ($baselineCommit + '^{commit}')
        if ($LASTEXITCODE) { throw "Missing baseline commit $baselineCommit. Fetch full history, or pass -LegacyAssembly." }
        $archive = Join-Path $evidence 'monolithic-source.zip'
        $legacySource = Join-Path $evidence 'monolithic-source'
        & git archive --format=zip "--output=$archive" $baselineCommit -- Directory.Build.props global.json src/PolylineKit README.md
        if ($LASTEXITCODE) { throw 'Could not archive the monolithic baseline source.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $legacySource
        $legacyProject = Join-Path $legacySource 'src/PolylineKit/PolylineKit.csproj'
        & dotnet restore $legacyProject --locked-mode
        if ($LASTEXITCODE) { throw 'Locked restore failed for the monolithic baseline.' }
        & dotnet build $legacyProject -c $Configuration -f net10.0 --no-restore
        if ($LASTEXITCODE) { throw 'Build failed for the monolithic baseline.' }
        $LegacyAssembly = Join-Path $legacySource "src/PolylineKit/bin/$Configuration/net10.0/PolylineKit.dll"
        $legacyOrigin = $baselineCommit
    }

    # Build both parent target frameworks before replacing the host's matching DLL pairs below.
    Build-CurrentConsumer 'src/PolylineKit.Clipper/PolylineKit.Clipper.csproj'
    Build-CurrentConsumer 'tests/Consumers/LeafOnly/LeafOnly.csproj'
    & dotnet "tests/Consumers/LeafOnly/bin/$Configuration/net10.0/LeafOnly.dll"
    if ($LASTEXITCODE) { throw 'Standalone winding consumer failed.' }
    Build-CurrentConsumer 'tests/Consumers/PortableOnly/PortableOnly.csproj'
    Build-CurrentConsumer 'tests/Consumers/CompatibilityHost/CompatibilityHost.csproj'

    # Compile the legacy component once. Later stages execute precisely these bytes.
    $legacyOutput = Join-Path $evidence 'precompiled-consumer'
    $legacyConsumerProject = 'tests/Consumers/LegacyApiConsumer/LegacyApiConsumer.csproj'
    & dotnet restore $legacyConsumerProject --locked-mode "-p:LegacyAssembly=$LegacyAssembly"
    if ($LASTEXITCODE) { throw 'Restore failed for the legacy consumer.' }
    & dotnet build $legacyConsumerProject -c $Configuration --no-restore "-p:LegacyAssembly=$LegacyAssembly" --output $legacyOutput
    if ($LASTEXITCODE) { throw 'Compilation against the monolithic assembly failed.' }
    $legacyConsumer = Join-Path $legacyOutput 'LegacyApiConsumer.dll'
    $compiledHash = (Get-FileHash -LiteralPath $legacyConsumer -Algorithm SHA256).Hash
    $runs = @()
    foreach ($target in @('net10.0', 'netstandard2.0')) {
        $runtime = Join-Path $evidence $target
        New-Item -ItemType Directory -Path $runtime | Out-Null
        Get-ChildItem -LiteralPath "tests/Consumers/CompatibilityHost/bin/$Configuration/net10.0" -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $runtime
        }
        foreach ($component in @(@{ Project='PolylineKit.Clipper'; Assembly='PolylineKit' }, @{ Project='PolylineKit.Core'; Assembly='PolylineKit.Winding' })) {
            $source = "src/$($component.Project)/bin/$Configuration/$target/$($component.Assembly).dll"
            if (!(Test-Path -LiteralPath $source)) { throw "Missing matching assembly target: $source. Build the solution first." }
            Copy-Item -LiteralPath $source -Destination $runtime -Force
        }
        $framework = if ($target -eq 'net10.0') { '.NETCoreApp,Version=v10.0' } else { '.NETStandard,Version=v2.0' }
        $lines = @(& dotnet (Join-Path $runtime 'CompatibilityHost.dll') $legacyConsumer $framework 2>&1)
        $exitCode = $LASTEXITCODE
        $lines | ForEach-Object { Write-Output $_ }
        $lines | Set-Content -LiteralPath (Join-Path $runtime 'check.txt')
        if ($exitCode) { throw "Precompiled consumer failed against $target assemblies." }
        if ((Get-FileHash -LiteralPath $legacyConsumer -Algorithm SHA256).Hash -ne $compiledHash) {
            throw 'The legacy consumer changed after its one compilation.'
        }
        $runs += [ordered]@{
            Target = $target
            ParentSHA256 = (Get-FileHash -LiteralPath (Join-Path $runtime 'PolylineKit.dll') -Algorithm SHA256).Hash
            WindingSHA256 = (Get-FileHash -LiteralPath (Join-Path $runtime 'PolylineKit.Winding.dll') -Algorithm SHA256).Hash
            ConsumerSHA256 = $compiledHash
        }
    }
    [ordered]@{
        LegacyOrigin = $legacyOrigin
        LegacyAssembly = $LegacyAssembly
        LegacyAssemblySHA256 = (Get-FileHash -LiteralPath $LegacyAssembly -Algorithm SHA256).Hash
        PrecompiledConsumer = $legacyConsumer
        PrecompiledConsumerSHA256 = $compiledHash
        Configuration = $Configuration
        Runs = $runs
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'verification.json')
    Write-Output "PASS: standalone and portable consumers; unchanged legacy component on both target pairs. Evidence: $evidence"
} finally {
    Pop-Location
}
