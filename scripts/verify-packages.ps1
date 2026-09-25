[CmdletBinding()]
param(
    [string]$Version = '0.1.0-alpha.1',
    [string]$OutputDirectory,
    [switch]$AllowDirty,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if ($Version -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$') { throw 'Supply a complete alpha package version.' }
$runId = [Guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspace = Join-Path $tempRoot ('polylinekit-packages-' + $runId)
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repository ('artifacts/package-verification-' + $runId) }
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Use an empty evidence directory; existing packages are not overwritten.' }
if ($workspace.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Consumers must run outside the source repository.' }
$saved = @{}
foreach ($name in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$complete = $false

function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE) { throw ('dotnet failed: ' + ($Arguments -join ' ')) }
}
function Hash-File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Hash-Bytes([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
function Read-Zip([IO.Compression.ZipArchive]$Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    Require ($null -ne $entry) "Package entry missing: $Name"
    $stream = $entry.Open(); $copy = [IO.MemoryStream]::new()
    try { $stream.CopyTo($copy); return ,$copy.ToArray() } finally { $stream.Dispose(); $copy.Dispose() }
}
function Xml-Child([Xml.XmlNode]$Node, [string]$Name) { $Node.SelectSingleNode("*[local-name()='$Name']") }
function Inspect-Package([string]$Id, [string]$Assembly, [string]$Feed, [string]$Revision) {
    $path = Join-Path $Feed "$Id.$Version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $nuspecs = @($zip.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal) })
        Require ($nuspecs.Count -eq 1) 'Expected one nuspec.'
        [xml]$nuspec = [Text.Encoding]::UTF8.GetString((Read-Zip $zip $nuspecs[0].FullName)).TrimStart([char]0xfeff)
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        Require ((Xml-Child $metadata 'id').InnerText -eq $Id -and (Xml-Child $metadata 'version').InnerText -eq $Version) 'Package identity differs.'
        $license = Xml-Child $metadata 'license'
        Require ($license.GetAttribute('type') -eq 'expression' -and $license.InnerText -eq 'MIT') 'Expected MIT license expression.'
        Require ([Text.Encoding]::UTF8.GetString((Read-Zip $zip 'LICENSE')).Contains('Permission is hereby granted')) 'Missing MIT license text.'
        Require ((Xml-Child $metadata 'readme').InnerText -eq 'README.md' -and (Read-Zip $zip 'README.md').Length -gt 0) 'Package README missing.'
        $repositoryMetadata = Xml-Child $metadata 'repository'
        Require ($repositoryMetadata.GetAttribute('type') -eq 'git' -and $repositoryMetadata.GetAttribute('url') -eq 'https://github.com/bgtnt/polylinekit' -and
            $repositoryMetadata.GetAttribute('commit') -eq $Revision) 'Repository identity differs.'

        $assets = @{}
        foreach ($target in @('netstandard2.0', 'net10.0')) {
            $dll = "lib/$target/$Assembly.dll"; $xml = "lib/$target/$Assembly.xml"
            $assets[$target] = Hash-Bytes (Read-Zip $zip $dll)
            [xml]$documentation = [Text.Encoding]::UTF8.GetString((Read-Zip $zip $xml)).TrimStart([char]0xfeff)
            Require ($documentation.doc.assembly.name -eq $Assembly -and $documentation.doc.members.member.Count -gt 0) "XML documentation differs: $target"
        }
        $expectedLib = @('netstandard2.0', 'net10.0') | ForEach-Object { "lib/$_/$Assembly.dll"; "lib/$_/$Assembly.xml" } | Sort-Object
        $actualLib = @($zip.Entries | Where-Object { $_.FullName.StartsWith('lib/', [StringComparison]::Ordinal) } | ForEach-Object FullName | Sort-Object)
        Require (($expectedLib -join '|') -eq ($actualLib -join '|')) 'Unexpected libraries in package.'
        Require (@($zip.Entries | Where-Object { $_.FullName -match '(?i)(^|/)(tests|benchmarks)/' }).Count -eq 0) 'Test/benchmark files leaked into package.'
        $groups = @((Xml-Child $metadata 'dependencies').SelectNodes("*[local-name()='group']"))
        Require ($groups.Count -eq 2) 'Expected two framework dependency groups.'
        $dependencies = @()
        foreach ($group in $groups) {
            $target = $group.GetAttribute('targetFramework')
            Require ($target -in @('.NETStandard2.0', 'net10.0')) "Unexpected package target: $target"
            $items = @($group.SelectNodes("*[local-name()='dependency']"))
            if ($Id -eq 'PolylineKit.Core') { Require ($items.Count -eq 0) 'Core must have no package dependency groups containing dependencies.' }
            else {
                Require ($items.Count -eq 2) 'Adapter needs precisely Core and Clipper2.'
                foreach ($pair in @(@('PolylineKit.Core', $Version), @('Clipper2', '2.0.0'))) {
                    $matches = @($items | Where-Object { $_.GetAttribute('id') -eq $pair[0] })
                    Require ($matches.Count -eq 1 -and $matches[0].GetAttribute('version') -in @($pair[1], "[$($pair[1]), )", "[$($pair[1])]")) "Unexpected adapter dependency: $($pair[0])"
                }
            }
            $dependencies += [ordered]@{ Target=$target; Packages=@($items | ForEach-Object { [ordered]@{ Id=$_.GetAttribute('id'); Version=$_.GetAttribute('version') } }) }
        }
        Require (@($dependencies.Target | Sort-Object -Unique).Count -eq 2) 'Duplicate package dependency framework.'
        return [ordered]@{ Id=$Id; Version=$Version; File=[IO.Path]::GetFileName($path); Assembly=$Assembly; SHA256=(Hash-File $path);
            SHA512=[Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($path))); Assets=$assets; Dependencies=$dependencies }
    } finally { $zip.Dispose() }
}

Push-Location $repository
try {
    $revision = (& git rev-parse HEAD).Trim()
    Require ($LASTEXITCODE -eq 0 -and $revision -match '^[0-9a-f]{40}$') 'Could not identify source revision.'
    $dirty = @(& git status --porcelain).Count -ne 0
    Require (!$dirty -or $AllowDirty) 'Commit source changes first, or use -AllowDirty for explicitly marked local validation.'
    $runtimes = @(& dotnet --list-runtimes)
    foreach ($major in @(8, 10)) { Require (@($runtimes | Where-Object { $_ -match "^Microsoft.NETCore.App $major\." }).Count -gt 0) "Install .NET $major runtime to execute both package asset selections." }
    New-Item -ItemType Directory -Path $workspace | Out-Null
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $feed = Join-Path $workspace 'feed'; $cache = Join-Path $workspace 'packages'
    New-Item -ItemType Directory -Path $feed, $cache | Out-Null
    # Packing builds the two real source projects. Only the subsequent outside consumers use the isolated feed/cache.
    foreach ($project in @('PolylineKit.Core', 'PolylineKit.Clipper')) {
        $path = "src/$project/$project.csproj"
        Run-Dotnet @('restore', $path, '--locked-mode')
        Run-Dotnet @('pack', $path, '-c', 'Release', '--no-restore', '--output', $feed, "-p:Version=$Version", "-p:PackageVersion=$Version", "-p:RepositoryCommit=$revision")
    }
    $packages = @(Inspect-Package 'PolylineKit.Core' 'PolylineKit.Winding' $feed $revision; Inspect-Package 'PolylineKit.Clipper' 'PolylineKit' $feed $revision)
    $config = Join-Path $workspace 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="PolylineKit.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="Clipper2" /><package pattern="Microsoft.NETCore.App.*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config
    Copy-Item -LiteralPath global.json -Destination (Join-Path $workspace 'global.json')
    $env:NUGET_PACKAGES = $cache
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $workspace 'http-cache'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $workspace 'plugin-cache'
    $consumers = @()
    foreach ($target in @('net10.0', 'net8.0')) {
        $assetTarget = if ($target -eq 'net10.0') { 'net10.0' } else { 'netstandard2.0' }
        foreach ($kind in @('Core', 'Clipper')) {
            $name = "$kind-$target"; $directory = Join-Path $workspace $name
            New-Item -ItemType Directory -Path $directory | Out-Null
            $withClipper = if ($kind -eq 'Clipper') { 'true' } else { 'false' }
            $properties = @("-p:TargetFramework=$target", "-p:PolylineKitVersion=$Version", "-p:UseClipper=$withClipper", '-p:VerifyPackages=true')
            $project = Join-Path $directory 'Consumer.csproj'
            Copy-Item -LiteralPath examples/PackageCoverage/PackageCoverage.csproj -Destination $project
            Copy-Item -LiteralPath examples/PackageCoverage/Program.cs, tests/PackageConsumers/PackageVerification.cs -Destination $directory
            Require (!(Get-Content $project -Raw).Contains('ProjectReference')) 'A package consumer must not reference source projects.'
            Run-Dotnet (@('restore', $project, '--configfile', $config, '--packages', $cache, '--no-cache', '-p:NuGetAudit=false') + $properties)
            $assetsPath = Join-Path $directory 'obj/project.assets.json'
            $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
            Require ([IO.Path]::GetFullPath($assets.project.restore.packagesPath).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $cache) 'Restore used an unexpected package cache.'
            $expectedIds = if ($kind -eq 'Core') { @('PolylineKit.Core') } else { @('PolylineKit.Core', 'PolylineKit.Clipper', 'Clipper2') }
            $actualIds = @($assets.libraries.Keys | ForEach-Object { ($_ -split '/')[0] } | Sort-Object)
            Require (($actualIds -join '|') -eq (($expectedIds | Sort-Object) -join '|')) 'Unexpected package dependency graph.'
            foreach ($package in $packages | Where-Object { $_.Id -in $expectedIds }) {
                $key = "$($package.Id)/$Version"
                Require ($assets.libraries[$key].type -eq 'package' -and $assets.libraries[$key].sha512 -eq $package.SHA512) 'Restored package content/identity differs.'
                $selected = $assets.targets[$target][$key]
                $expectedAsset = "lib/$assetTarget/$($package.Assembly).dll"
                Require (@($selected.compile.Keys).Count -eq 1 -and $selected.compile.ContainsKey($expectedAsset) -and
                    @($selected.runtime.Keys).Count -eq 1 -and $selected.runtime.ContainsKey($expectedAsset)) "Incorrect compile/runtime assets: $key/$target"
                $cached = Join-Path $cache "$($package.Id.ToLowerInvariant())/$Version"
                $origin = Get-Content -LiteralPath (Join-Path $cached '.nupkg.metadata') -Raw | ConvertFrom-Json
                Require ([IO.Path]::GetFullPath($origin.source).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $feed) 'PolylineKit package did not originate in this local feed.'
                Require ((Hash-File (Join-Path $cached "$($package.Id.ToLowerInvariant()).$Version.nupkg")) -eq $package.SHA256) 'Cached nupkg differs from the produced package.'
            }
            Run-Dotnet (@('build', $project, '-c', 'Release', '--no-restore') + $properties)
            $lines = @(& dotnet (Join-Path $directory "bin/Release/$target/Consumer.dll") $assetTarget $Version 2>&1)
            Require ($LASTEXITCODE -eq 0) ("Package consumer failed: $name`n" + ($lines -join "`n"))
            $result = $lines[-1] | ConvertFrom-Json
            Require ($result.CoreSha256 -eq $packages[0].Assets[$assetTarget]) 'Runtime did not load the selected Core package DLL.'
            if ($kind -eq 'Clipper') { Require ($result.AdapterSha256 -eq $packages[1].Assets[$assetTarget]) 'Runtime did not load the selected adapter package DLL.' }
            $evidencePath = Join-Path $output $name; New-Item -ItemType Directory -Path $evidencePath | Out-Null
            Copy-Item -LiteralPath $project, (Join-Path $directory 'Program.cs'), (Join-Path $directory 'PackageVerification.cs'), $assetsPath -Destination $evidencePath
            $lines | Set-Content -LiteralPath (Join-Path $evidencePath 'run.log')
            $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidencePath 'run.json')
            $consumers += [ordered]@{ Name=$name; Package="PolylineKit.$kind"; ConsumerTarget=$target; SelectedAssetTarget=$assetTarget;
                AssetsSHA256=(Hash-File $assetsPath); Result=$result }
            Write-Output "PASS: $name restored and executed $assetTarget assets from the produced packages."
        }
    }
    Require ((& git rev-parse HEAD).Trim() -eq $revision) 'Source revision changed during verification.'
    Require ($AllowDirty -or @(& git status --porcelain).Count -eq 0) 'Working source changed during clean package verification.'
    foreach ($package in $packages) {
        Copy-Item -LiteralPath (Join-Path $feed $package.File) -Destination $output
        $metadata = Join-Path $cache "$($package.Id.ToLowerInvariant())/$Version/.nupkg.metadata"
        Copy-Item -LiteralPath $metadata -Destination (Join-Path $output "$($package.Id).metadata.json")
        $package['RestoreMetadataSHA256'] = Hash-File $metadata
    }
    Copy-Item -LiteralPath $config -Destination $output
    [ordered]@{ SourceRevision=$revision; DirtyWorkingTree=$dirty; Version=$Version; Utc=[DateTimeOffset]::UtcNow.ToString('o');
        SDK=(& dotnet --version); InstalledRuntimes=$runtimes; Packages=$packages; Consumers=$consumers } |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'verification.json')
    $complete = $true
    Write-Output "PASS: inspected and executed both real NuGet packages on net10.0 and net8.0. Evidence: $output"
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Pop-Location
    if ($complete -and !$KeepWorkspace -and (Test-Path -LiteralPath $workspace)) {
        $resolved = (Resolve-Path -LiteralPath $workspace).Path
        if ($resolved -ne $workspace -or [IO.Path]::GetDirectoryName($resolved) -ne $tempRoot -or ![IO.Path]::GetFileName($resolved).StartsWith('polylinekit-packages-', [StringComparison]::Ordinal)) {
            throw 'Refusing to remove an unexpected temporary path.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } elseif (Test-Path -LiteralPath $workspace) { Write-Output "External consumer workspace retained for inspection: $workspace" }
}
