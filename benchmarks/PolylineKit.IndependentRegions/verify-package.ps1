[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$DataRoot,
    [switch]$KeepWorkspace
)

# This verification uses NuGet.org, not a local build/feed or the user's package cache.
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$runId = [Guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspace = Join-Path $tempRoot ('polylinekit-independent-package-' + $runId)
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repository ('artifacts/independent-package-' + $runId) }
$projectName = 'PolylineKit.IndependentRegions.csproj'
$expectedCoreHash = 'bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca'
$expectedSource = '5e9dcc1a5bcb34b5b2d8ae60105d328aec66a80c'
$sourceUri = 'https://api.nuget.org/v3/index.json'
$complete = $false
$locationPushed = $false
$outputOwned = $false
$savedEnvironment = @{}
$evidence = [ordered]@{
    Complete = $false
    Utc = [DateTimeOffset]::UtcNow.ToString('o')
    Package = 'PolylineKit.Core/0.1.0-alpha.2'
    ExpectedSourceRevision = $expectedSource
    Workspace = $workspace
    PublicSourceOnly = $true
    IndependentHumanAdoption = $false
}

function Require([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function Hash-File([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Run-Dotnet([string]$Name, [string[]]$Arguments) {
    $lines = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines | Set-Content -LiteralPath (Join-Path $output ($Name + '.log')) -Encoding utf8
    Require ($exitCode -eq 0) ("dotnet $Name failed with exit code $exitCode. See the evidence log.`n" + (($lines | Select-Object -Last 12) -join "`n"))
}

function Copy-PublicSource {
    $project = Join-Path $PSScriptRoot $projectName
    $lock = Join-Path $PSScriptRoot 'packages.lock.json'
    Require (Test-Path -LiteralPath $lock -PathType Leaf) 'Restore the runner and commit its packages.lock.json before verifying a locked external consumer.'
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    Require ($projectXml.SelectNodes('//ProjectReference').Count -eq 0) 'A package consumer cannot contain ProjectReference.'
    Require ($projectXml.SelectNodes('//Import').Count -eq 0) 'The standalone runner cannot import external build files.'
    foreach ($pair in @(@('PolylineKit.Core', '[0.1.0-alpha.2]'), @('Clipper2', '[2.0.0]'), @('NetTopologySuite', '[2.6.0]'))) {
        $matches = @($projectXml.SelectNodes('//PackageReference') | Where-Object { $_.GetAttribute('Include') -eq $pair[0] })
        Require ($matches.Count -eq 1 -and $matches[0].GetAttribute('Version') -eq $pair[1]) ("Expected an exact package reference: " + $pair[0] + ' ' + $pair[1])
    }
    Require ($projectXml.SelectNodes('//PackageReference').Count -eq 3) 'Unexpected runner package dependency.'
    # Deliberately omit bin, obj, input data and all library/test source projects.
    $sourceFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName.Substring($PSScriptRoot.Length + 1) -notmatch '(^|[\\/])(bin|obj|data)([\\/]|$)' })
    Require ($sourceFiles.Count -gt 0) 'No public runner source was found.'
    $copied = @()
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($PSScriptRoot.Length + 1)
        $destination = Join-Path $workspace $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $copied += [ordered]@{ Path = $relative.Replace('\', '/'); SHA256 = Hash-File $destination }
    }
    foreach ($file in @($projectName, 'packages.lock.json', 'NuGet.Config')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $workspace $file)
        $copied += [ordered]@{ Path = $file; SHA256 = Hash-File (Join-Path $workspace $file) }
    }
    [xml]$config = Get-Content -LiteralPath (Join-Path $workspace 'NuGet.Config') -Raw
    Require ($null -ne $config.SelectSingleNode('/configuration/packageSources/clear')) 'NuGet.Config must clear inherited package sources.'
    Require ($null -ne $config.SelectSingleNode('/configuration/fallbackPackageFolders/clear')) 'NuGet.Config must clear inherited fallback package caches.'
    $sources = @($config.configuration.packageSources.add)
    Require ($sources.Count -eq 1 -and $sources[0].value -eq $sourceUri) 'NuGet.org must be the only package source.'
    $evidence.CopiedFiles = $copied
}

try {
    Require ($PSVersionTable.PSVersion.Major -ge 7) 'Use PowerShell 7 or later.'
    Require (!$workspace.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'The consumer must execute outside the source checkout.'
    Require (!(Test-Path -LiteralPath $workspace)) 'The newly generated external workspace already exists.'
    Require (!(Test-Path -LiteralPath $output) -or @(Get-ChildItem -LiteralPath $output -Force).Count -eq 0) 'Use an empty evidence directory.'
    if ($DataRoot) {
        $DataRoot = [IO.Path]::GetFullPath($DataRoot)
        Require (Test-Path -LiteralPath $DataRoot -PathType Container) 'DataRoot must be an existing frozen dataset directory.'
    }
    New-Item -ItemType Directory -Path $workspace | Out-Null
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $outputOwned = $true
    $cache = Join-Path $workspace 'packages'
    New-Item -ItemType Directory -Path $cache | Out-Null
    Require (@(Get-ChildItem -LiteralPath $cache -Force).Count -eq 0) 'The isolated package cache must initially be empty.'
    $evidence.PackageCache = $cache
    $evidence.PackageCacheInitiallyEmpty = $true
    Copy-PublicSource
    foreach ($name in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH')) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    $env:NUGET_PACKAGES = $cache
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $workspace 'http-cache'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $workspace 'plugin-cache'
    Push-Location $workspace
    $locationPushed = $true
    $project = Join-Path $workspace $projectName
    $config = Join-Path $workspace 'NuGet.Config'
    # Some SDK installations append their own library-packs feed even when the
    # explicit config clears sources. Disable that SDK feed rather than accepting
    # an extra source or shared cache in this isolated consumer.
    $isolatedBuild = @('-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false',
        '-p:DisableImplicitNuGetFallbackFolder=true', '-p:RestoreFallbackFolders=',
        '-p:DisableImplicitLibraryPacksFolder=true')
    $restoreArguments = @('restore', $project, '--configfile', $config, '--packages', $cache, '--no-cache', '--locked-mode', '-p:NuGetAudit=false') + $isolatedBuild
    $evidence.RestoreArguments = $restoreArguments
    Run-Dotnet 'restore' $restoreArguments
    $assetsPath = Join-Path $workspace 'obj/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    # Preserve the actual restore graph even if an isolation guard rejects it.
    Copy-Item -LiteralPath $assetsPath -Destination (Join-Path $output 'project.assets.json')
    Require ([IO.Path]::GetFullPath($assets.project.restore.packagesPath).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $cache) 'Restore selected an unexpected package cache.'
    Require (@($assets.packageFolders.Keys).Count -eq 1 -and
        [IO.Path]::GetFullPath(@($assets.packageFolders.Keys)[0]).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $cache) 'Restore added a shared or fallback package cache.'
    Require (@($assets.project.restore.sources.Keys).Count -eq 1 -and $assets.project.restore.sources.ContainsKey($sourceUri)) 'Restore used a package source other than NuGet.org.'
    $coreKey = 'PolylineKit.Core/0.1.0-alpha.2'
    Require ($assets.libraries[$coreKey].type -eq 'package') 'Core was not restored as a package.'
    Require (@($assets.libraries.Values | Where-Object { $_.type -ne 'package' }).Count -eq 0) 'A source project entered the dependency graph.'
    $expectedAsset = 'lib/net10.0/PolylineKit.Winding.dll'
    $selectedCore = $assets.targets['net10.0'][$coreKey]
    Require (@($selectedCore.runtime.Keys).Count -eq 1 -and $selectedCore.runtime.ContainsKey($expectedAsset)) 'The consumer did not select the net10.0 runtime asset.'
    Require (@($selectedCore.compile.Keys).Count -eq 1 -and $selectedCore.compile.ContainsKey($expectedAsset)) 'The consumer did not select the net10.0 compile asset.'
    $cachedCore = Join-Path $cache 'polylinekit.core/0.1.0-alpha.2'
    $metadataPath = Join-Path $cachedCore '.nupkg.metadata'
    $origin = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json -AsHashtable
    Require ($origin.source.TrimEnd('/') -eq $sourceUri.TrimEnd('/')) 'The restored Core archive did not originate at NuGet.org.'
    $cachedCoreDll = Join-Path $cachedCore $expectedAsset
    Require ((Hash-File $cachedCoreDll) -eq $expectedCoreHash) 'The NuGet Core DLL differs from the recorded published alpha.2.'
    $evidence.RestoredDependencies = @($assets.libraries.Keys | Sort-Object)
    $evidence.RestoredCore = [ordered]@{ Type = 'package'; Version = '0.1.0-alpha.2'; Source = $origin.source; RuntimeAsset = $expectedAsset; SHA256 = Hash-File $cachedCoreDll; PackageSHA256 = Hash-File (Join-Path $cachedCore 'polylinekit.core.0.1.0-alpha.2.nupkg') }
    $buildArguments = @('build', $project, '-c', 'Release', '--no-restore') + $isolatedBuild
    Run-Dotnet 'build' $buildArguments
    $consumerDll = Join-Path $workspace 'bin/Release/net10.0/PolylineKit.IndependentRegions.dll'
    $loadedCoreDll = Join-Path (Split-Path $consumerDll -Parent) 'PolylineKit.Winding.dll'
    Require ((Hash-File $loadedCoreDll) -eq $expectedCoreHash) 'The external build copied a different Core runtime DLL.'
    $packageCheckPath = Join-Path $output 'package-check.json'
    Run-Dotnet 'package-check' @($consumerDll, 'package-check', $packageCheckPath)
    $packageCheck = Get-Content -LiteralPath $packageCheckPath -Raw | ConvertFrom-Json -AsHashtable
    # Check the runtime's actual Assembly identity, not merely the expected constants in a report.
    $coreIdentities = @($packageCheck.Metadata.Assemblies | Where-Object { $_.Assembly -eq 'PolylineKit.Winding' })
    Require ($coreIdentities.Count -eq 1) 'The runtime report lacks a unique loaded Core assembly.'
    $coreIdentity = $coreIdentities[0]
    Require ($coreIdentity.Sha256 -eq $expectedCoreHash -and
        $coreIdentity.InformationalVersion.Split('+')[0] -eq '0.1.0-alpha.2' -and
        $coreIdentity.InformationalVersion.Contains($expectedSource) -and
        $coreIdentity.TargetFramework -eq '.NETCoreApp,Version=v10.0' -and
        [IO.Path]::GetFullPath($coreIdentity.Location) -eq [IO.Path]::GetFullPath($loadedCoreDll)) 'The runtime loaded a different published DLL/source or asset target.'
    Require ($packageCheck.Validation.Passed -eq $true -and $packageCheck.Validation.Checks -gt 0) 'The published package analytic smoke did not pass.'
    $evidence.PackageCheck = $packageCheck
    if ($DataRoot) {
        Run-Dotnet 'check' @($consumerDll, 'check', $DataRoot, (Join-Path $output 'check.json'))
        $evidence.DatasetCheck = Get-Content -LiteralPath (Join-Path $output 'check.json') -Raw | ConvertFrom-Json -AsHashtable
    }
    Copy-Item -LiteralPath $metadataPath -Destination (Join-Path $output 'core.nupkg.metadata.json')
    foreach ($file in @($projectName, 'packages.lock.json', 'NuGet.Config')) {
        Copy-Item -LiteralPath (Join-Path $workspace $file) -Destination (Join-Path $output $file)
    }
    $evidence.ConsumerSHA256 = Hash-File $consumerDll
    $evidence.SDK = (& dotnet --version).Trim()
    $evidence.Complete = $true
    $complete = $true
    Write-Output "PASS: outside-checkout consumer restored and ran the published alpha.2 from NuGet.org. Evidence: $output"
} catch {
    $evidence.Error = $_.Exception.Message
    throw
} finally {
    if ($locationPushed) { Pop-Location }
    foreach ($name in $savedEnvironment.Keys) {
        # PowerShell binds a null string argument as an empty string. .NET 10
        # retains empty environment values, so use a real null to restore absence.
        if ($null -eq $savedEnvironment[$name]) {
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
        } else {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
        }
    }
    $packageEnvironmentRestored = $true
    foreach ($name in $savedEnvironment.Keys) {
        $packageEnvironmentRestored = $packageEnvironmentRestored -and
            ([Environment]::GetEnvironmentVariable($name) -ceq $savedEnvironment[$name])
    }
    $evidence.PackageEnvironmentRestored = $packageEnvironmentRestored
    $evidence.Complete = $complete -and $packageEnvironmentRestored
    if ($outputOwned -and (Test-Path -LiteralPath $output -PathType Container)) {
        $evidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding utf8
    }
    if ($complete -and !$KeepWorkspace -and (Test-Path -LiteralPath $workspace)) {
        # Verify the resolved target before recursive deletion; retain failed workspaces for diagnosis.
        $resolved = (Resolve-Path -LiteralPath $workspace).Path
        Require ($resolved -eq $workspace -and [IO.Path]::GetDirectoryName($resolved) -eq $tempRoot -and
            [IO.Path]::GetFileName($resolved).StartsWith('polylinekit-independent-package-', [StringComparison]::Ordinal)) 'Refusing to remove an unexpected temporary directory.'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } elseif (Test-Path -LiteralPath $workspace) {
        Write-Output "External consumer workspace retained: $workspace"
    }
    Require $packageEnvironmentRestored 'The original NuGet environment settings were not restored.'
}
