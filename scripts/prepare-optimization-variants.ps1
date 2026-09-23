#Requires -Version 7.0
param(
    [string]$RepositoryRoot = '',
    [string]$OutputDirectory = 'artifacts/optimization/reproduced',
    [switch]$InitialCandidate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-CheckedNative {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed with exit code $LASTEXITCODE." }
}

function Get-ExactRevision {
    param([string]$Revision)
    $resolved = @(Invoke-CheckedNative git @('-C', $script:repository, 'rev-parse', '--verify', "$Revision^{commit}"))
    if ($resolved.Count -ne 1 -or $resolved[0].Trim() -cne $Revision) {
        throw "Expected the complete frozen commit $Revision. Fetch the published repository history first."
    }
    return $resolved[0].Trim()
}

function Assert-ChildPath {
    param([string]$Path, [string]$Parent)
    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $full.StartsWith($prefix, $comparison)) { throw "Path must stay below $Parent`: $full" }
    return $full
}

function Assert-NoReparseParents {
    param([string]$Path, [string]$StopAt)
    $current = [System.IO.Path]::GetFullPath($Path)
    while ($current -ne $StopAt) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Artifact paths must not traverse a symlink or junction: $current"
            }
        }
        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) { throw 'Invalid artifact ancestry.' }
        $current = $parent
    }
}

function Export-FrozenSource {
    param([string]$Name, [string]$Revision)
    $zipPath = Join-Path $script:archivesDirectory "$Name.zip"
    $destination = Assert-ChildPath (Join-Path $script:sourcesDirectory $Name) $script:runDirectory
    Invoke-CheckedNative git @('-C', $script:repository, 'archive', '--format=zip', "--output=$zipPath", $Revision)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $null = Assert-ChildPath (Join-Path $destination $entry.FullName) $destination
            # Refuse symbolic links even though these frozen revisions contain ordinary source files.
            $unixType = ($entry.ExternalAttributes -shr 16) -band 0xF000
            if ($unixType -eq 0xA000) { throw "Source archive includes a symlink: $($entry.FullName)" }
        }
    } finally { $archive.Dispose() }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $destination)
    return $destination
}

function Build-FrozenProject {
    param([string]$Project, [string]$Target, [string]$Revision)
    Invoke-CheckedNative dotnet @('restore', $Project, '--locked-mode')
    Invoke-CheckedNative dotnet @('build', $Project, '-c', 'Release', '-f', $Target, '--no-restore',
        "-p:SourceRevisionId=$Revision")
}

# Resolve sources from Git objects, never from potentially edited working files.
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $rootOutput = @(Invoke-CheckedNative git @('-C', $PSScriptRoot, 'rev-parse', '--show-toplevel'))
    if ($rootOutput.Count -ne 1) { throw 'Cannot determine the repository root.' }
    $RepositoryRoot = $rootOutput[0].Trim()
}
$script:repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$harnessRevision = Get-ExactRevision 'c74fed307eca6bc2715ef910238e3b475c381824'
$baselineRevision = Get-ExactRevision 'e6b4978522b3221b36f054024be338b8e3618577'
$cleanupRevision = Get-ExactRevision '3142658f08705500675615aa0bef4f49f59edc9e'
$modernRevision = if ($InitialCandidate) {
    Get-ExactRevision '53d9d2db12d91a68dcfcd53ef56563eedc896325'
} else {
    Get-ExactRevision '2d76cc72babbf29673252cb573b054946aba2632'
}

$artifactRoot = Join-Path $script:repository 'artifacts'
$requestedOutput = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory }
    else { Join-Path $script:repository $OutputDirectory }
$outputBase = Assert-ChildPath $requestedOutput $artifactRoot
Assert-NoReparseParents $outputBase $script:repository
$id = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$script:runDirectory = Assert-ChildPath (Join-Path $outputBase $id) $artifactRoot
if (Test-Path -LiteralPath $script:runDirectory) { throw 'The unique output directory already exists.' }
$script:archivesDirectory = Join-Path $script:runDirectory 'archives'
$script:sourcesDirectory = Join-Path $script:runDirectory 'sources'
$variantsDirectory = Join-Path $script:runDirectory 'variants'
$null = New-Item -ItemType Directory -Path $script:archivesDirectory, $script:sourcesDirectory, $variantsDirectory

$harnessSource = Export-FrozenSource 'harness' $harnessRevision
$baselineSource = Export-FrozenSource 'baseline' $baselineRevision
$cleanupSource = Export-FrozenSource 'cleanup' $cleanupRevision
$modernSource = Export-FrozenSource 'modern' $modernRevision

Build-FrozenProject (Join-Path $harnessSource 'experiments/PolylineKit.Experiments/PolylineKit.Experiments.csproj') 'net10.0' $harnessRevision
$harnessOutput = Join-Path $harnessSource 'experiments/PolylineKit.Experiments/bin/Release/net10.0'
Build-FrozenProject (Join-Path $baselineSource 'src/PolylineKit/PolylineKit.csproj') 'netstandard2.0' $baselineRevision
Build-FrozenProject (Join-Path $cleanupSource 'src/PolylineKit/PolylineKit.csproj') 'netstandard2.0' $cleanupRevision
Build-FrozenProject (Join-Path $modernSource 'src/PolylineKit/PolylineKit.csproj') 'net10.0' $modernRevision

$definitions = @(
    @{ Name = 'baseline'; Source = $baselineSource; Revision = $baselineRevision; Target = 'netstandard2.0'; ForceScalar = $false },
    @{ Name = 'cleanup'; Source = $cleanupSource; Revision = $cleanupRevision; Target = 'netstandard2.0'; ForceScalar = $false },
    @{ Name = 'modern-scalar'; Source = $modernSource; Revision = $modernRevision; Target = 'net10.0'; ForceScalar = $true },
    @{ Name = 'simd'; Source = $modernSource; Revision = $modernRevision; Target = 'net10.0'; ForceScalar = $false }
)
$variants = foreach ($definition in $definitions) {
    $destination = Assert-ChildPath (Join-Path $variantsDirectory $definition.Name) $script:runDirectory
    # Copy the complete common host/deps/runtimeconfig once per variant. Then replace only the core
    # assembly and its matching debug symbols: all variants execute the identical harness DLL.
    Copy-Item -LiteralPath $harnessOutput -Destination $destination -Recurse
    $coreOutput = Join-Path $definition.Source "src/PolylineKit/bin/Release/$($definition.Target)"
    foreach ($file in @('PolylineKit.dll', 'PolylineKit.pdb')) {
        Copy-Item -LiteralPath (Join-Path $coreOutput $file) -Destination (Join-Path $destination $file) -Force
    }
    [ordered]@{
        Name = $definition.Name
        CoreRevision = $definition.Revision
        CoreTarget = $definition.Target
        Directory = [System.IO.Path]::GetRelativePath($script:repository, $destination)
        ForceScalar = $definition.ForceScalar
        CoreDllSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'PolylineKit.dll') -Algorithm SHA256).Hash
        CorePdbSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'PolylineKit.pdb') -Algorithm SHA256).Hash
    }
}
$harnessHash = (Get-FileHash -LiteralPath (Join-Path $harnessOutput 'PolylineKit.Experiments.dll') -Algorithm SHA256).Hash
foreach ($variant in $variants) {
    $copiedHarness = Join-Path $script:repository (Join-Path $variant.Directory 'PolylineKit.Experiments.dll')
    $copiedHash = (Get-FileHash -LiteralPath $copiedHarness -Algorithm SHA256).Hash
    if ($copiedHash -cne $harnessHash) { throw "The common harness copy differs for $($variant.Name)." }
}
$manifest = [ordered]@{
    Protocol = 'polylinekit-optimization-v1'
    Candidate = $(if ($InitialCandidate) { 'Initial' } else { 'Final' })
    PreparedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    HarnessRevision = $harnessRevision
    HarnessDllSha256 = $harnessHash
    Sdk = (@(Invoke-CheckedNative dotnet @('--version')) -join "`n")
    RuntimeInfo = (@(Invoke-CheckedNative dotnet @('--info')) -join "`n")
    Build = 'Release; locked restore; SourceRevisionId supplied explicitly; Git archive source snapshots'
    HashNote = 'Hashes identify these actual builds. Source paths, SDK and build metadata can change hashes between reproductions.'
    SourceArchives = @(Get-ChildItem -LiteralPath $script:archivesDirectory -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    Variants = @($variants)
}
$manifestPath = Join-Path $script:runDirectory 'variants.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Prepared frozen variants: $manifestPath"
Write-Host 'No benchmark or preflight process was started. Run benchmark-optimization.ps1 separately.'
Write-Output $manifestPath
