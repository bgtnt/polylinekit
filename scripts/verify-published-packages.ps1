#requires -Version 7.0
<#
.SYNOPSIS
Checks NuGet.org packages against a clean verify-packages.ps1 release record.
.DESCRIPTION
Run after publication with the original verification.json and both original nupkg files.
Every original ZIP entry must remain byte-identical; only NuGet's repository signature
may be added. Four outside-checkout PackageReference consumers then restore, build and
execute using independent empty caches and NuGet.org alone. Evidence defaults to the
ignored artifacts directory. This checks published payloads and consumers, not gallery
ownership, publication workflow status, or release-note content.
.EXAMPLE
pwsh ./scripts/verify-published-packages.ps1 -Version 0.1.0-alpha.2 -VerifiedPackagesDirectory ./artifacts/alpha2-packages-5e9dcc1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-alpha\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$VerifiedPackagesDirectory,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$originals = [IO.Path]::GetFullPath($VerifiedPackagesDirectory)
$source = 'https://api.nuget.org/v3/index.json'
$runId = [Guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspace = Join-Path $tempRoot ('polylinekit-published-' + $runId)
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repository ('artifacts/published-package-verification-' + $runId) }
$recordPath = Join-Path $originals 'verification.json'
$saved = @{}
$workspaceOwned = $false
$report = $null

function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Hash-File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Hash-Bytes([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
function Sha512-File([string]$Path) { [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($Path))) }
function Save-Json($Value, [string]$Path) { $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8 }
function Run-Dotnet([string[]]$Arguments, [string]$Log) {
    $lines = @(& dotnet @Arguments 2>&1)
    $code = $LASTEXITCODE
    $lines | Set-Content -LiteralPath $Log -Encoding utf8
    Require ($code -eq 0) "dotnet failed (exit $code); see $Log"
    return ,$lines
}
function Read-Zip([string]$Path) {
    $entries = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            Require (!$name.Contains('\') -and !$name.StartsWith('/') -and '..' -notin ($name -split '/') -and !$entries.ContainsKey($name)) "Unsafe or duplicate ZIP entry: $name"
            $stream = $entry.Open(); $copy = [IO.MemoryStream]::new()
            try { $stream.CopyTo($copy); $entries.Add($name, $copy.ToArray()) } finally { $stream.Dispose(); $copy.Dispose() }
        }
        return ,$entries
    } finally { $zip.Dispose() }
}
function Download-Public([Net.Http.HttpClient]$Client, [string]$Url, [string]$Path) {
    $response = $Client.GetAsync($Url).GetAwaiter().GetResult()
    try {
        Require ([int]$response.StatusCode -eq 200 -and $response.RequestMessage.RequestUri.AbsoluteUri -eq $Url) "Public download failed or redirected: $Url ($($response.StatusCode))"
        [IO.File]::WriteAllBytes($Path, $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
        return [ordered]@{ Url=$Url; StatusCode=[int]$response.StatusCode; Utc=[DateTimeOffset]::UtcNow.ToString('o');
            SHA256=(Hash-File $Path); Headers=$response.Headers.ToString() + $response.Content.Headers.ToString() }
    } finally { $response.Dispose() }
}

Require (Test-Path -LiteralPath $recordPath -PathType Leaf) 'The verified package directory must contain verification.json.'
$record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json -AsHashtable
Require ($record.Version -eq $Version -and $record.SourceRevision -match '^[0-9a-f]{40}$' -and
    $record.ContainsKey('DirtyWorkingTree') -and $record.DirtyWorkingTree -is [bool] -and !$record.DirtyWorkingTree) 'The original record must identify this version and a clean source revision.'
Require (@($record.Packages).Count -eq 2 -and
    ((@($record.Packages.Id | Sort-Object) -join '|') -eq 'PolylineKit.Clipper|PolylineKit.Core') -and
    @($record.Consumers).Count -eq 4) 'Expected the two verified PolylineKit packages and four original consumers.'
foreach ($package in $record.Packages) {
    Require ($package.Version -eq $Version -and $package.File -eq "$($package.Id).$Version.nupkg" -and
        $package.Assembly -match '^[A-Za-z][A-Za-z0-9.]+$' -and
        ((@($package.Assets.Keys | Sort-Object) -join '|') -eq 'net10.0|netstandard2.0')) 'Original package identity or asset inventory differs.'
    $path = Join-Path $originals $package.File
    Require ((Hash-File $path) -eq $package.SHA256 -and (Sha512-File $path) -eq $package.SHA512) "Original package differs from verification.json: $($package.Id)"
}
Require (!(Test-Path -LiteralPath $output) -or ((Test-Path -LiteralPath $output -PathType Container) -and @(Get-ChildItem -LiteralPath $output -Force).Count -eq 0)) 'Use an empty evidence directory; existing evidence is not overwritten.'
Require (!$workspace.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Consumers must run outside the source checkout.'
if ($output.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    & git -C $repository check-ignore --quiet -- (Join-Path $output 'verification.json')
    Require ($LASTEXITCODE -eq 0) 'Evidence inside the checkout must be in a git-ignored directory.'
}
foreach ($name in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH', 'DOTNET_CLI_HOME', 'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}

try {
    New-Item -ItemType Directory -Path $workspace | Out-Null
    $workspaceOwned = $true
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $config = Join-Path $workspace 'NuGet.Config'
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
'@ | Set-Content -LiteralPath $config -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $repository 'global.json') -Destination $workspace
    Copy-Item -LiteralPath $recordPath -Destination (Join-Path $output 'original-verification.json')
    Copy-Item -LiteralPath $config, (Join-Path $workspace 'global.json') -Destination $output
    $env:DOTNET_CLI_HOME = Join-Path $workspace 'cli-home'
    $env:DOTNET_NOLOGO = 'true'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:NUGET_PACKAGES = Join-Path $workspace 'signature-packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $workspace 'signature-http-cache'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $workspace 'signature-plugin-cache'
    Push-Location $workspace
    try {
        $sdk = (& dotnet --version).Trim()
        Require ($LASTEXITCODE -eq 0) 'Could not select the repository SDK.'
        $runtimes = @(& dotnet --list-runtimes)
        foreach ($major in @(8, 10)) { Require (@($runtimes | Where-Object { $_ -match "^Microsoft.NETCore.App $major\." }).Count -gt 0) "Install .NET $major to execute the package consumers." }
        $report = [ordered]@{ Complete=$false; Version=$Version; SourceRevision=$record.SourceRevision; OriginalVerificationSHA256=(Hash-File $recordPath);
            ConsumerSourceRevision=(& git -C $repository rev-parse HEAD).Trim(); Utc=[DateTimeOffset]::UtcNow.ToString('o'); SDK=$sdk;
            InstalledRuntimes=$runtimes; PublicSource=$source; Packages=@(); Consumers=@() }
        # A new client has no cookies, Authorization header, or default credentials; redirects are rejected.
        $handler = [Net.Http.HttpClientHandler]::new()
        $handler.UseCookies = $false; $handler.UseDefaultCredentials = $false; $handler.AllowAutoRedirect = $false
        $client = [Net.Http.HttpClient]::new($handler)
        $client.Timeout = [TimeSpan]::FromSeconds(90)
        try {
            foreach ($package in $record.Packages) {
                $id = $package.Id.ToLowerInvariant(); $lowerVersion = $Version.ToLowerInvariant()
                $indexPath = Join-Path $output "$($package.Id).versions.json"
                $index = Download-Public $client "https://api.nuget.org/v3-flatcontainer/$id/index.json" $indexPath
                Require ($lowerVersion -cin (Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json).versions) "Version is not indexed on NuGet.org: $($package.Id)/$Version"
                $downloadPath = Join-Path $workspace $package.File
                $download = Download-Public $client "https://api.nuget.org/v3-flatcontainer/$id/$lowerVersion/$id.$lowerVersion.nupkg" $downloadPath
                Copy-Item -LiteralPath $downloadPath -Destination $output
                $original = Read-Zip (Join-Path $originals $package.File); $published = Read-Zip $downloadPath
                Require (!$original.ContainsKey('.signature.p7s')) 'Expected the unsigned originals from verify-packages.ps1.'
                $extra = @($published.Keys | Where-Object { !$original.ContainsKey($_) })
                Require ($extra.Count -eq 1 -and $extra[0] -ceq '.signature.p7s' -and $published['.signature.p7s'].Length -gt 0 -and
                    $published.Count -eq $original.Count + 1) "Published archive has missing or unexpected entries: $($package.Id)"
                $entries = @()
                foreach ($name in $original.Keys | Sort-Object) {
                    Require ($published.ContainsKey($name) -and $original[$name].Length -eq $published[$name].Length -and
                        [Convert]::ToBase64String($original[$name]) -ceq [Convert]::ToBase64String($published[$name])) "Published ZIP entry differs: $($package.Id)/$name"
                    $entries += [ordered]@{ Entry=$name; Length=$original[$name].Length; SHA256=(Hash-Bytes $original[$name]); ByteIdentical=$true }
                }
                $nuspecs = @($published.Keys | Where-Object { $_.EndsWith('.nuspec', [StringComparison]::Ordinal) })
                Require ($nuspecs.Count -eq 1) 'Expected exactly one nuspec.'
                [xml]$nuspec = [Text.Encoding]::UTF8.GetString($published[$nuspecs[0]]).TrimStart([char]0xfeff)
                $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
                $repo = $metadata.SelectSingleNode("*[local-name()='repository']")
                Require ($metadata.SelectSingleNode("*[local-name()='id']").InnerText -ceq $package.Id -and
                    $metadata.SelectSingleNode("*[local-name()='version']").InnerText -ceq $Version -and
                    $repo.GetAttribute('type') -ceq 'git' -and $repo.GetAttribute('url') -ceq 'https://github.com/bgtnt/polylinekit' -and
                    $repo.GetAttribute('commit') -ceq $record.SourceRevision) 'Published package version or source identity differs.'
                foreach ($target in $package.Assets.Keys) {
                    Require ((Hash-Bytes $published["lib/$target/$($package.Assembly).dll"]) -eq $package.Assets[$target]) "Published DLL differs: $($package.Id)/$target"
                }
                $signatureLog = "$($package.Id)-signature.log"
                $signature = Run-Dotnet @('nuget', 'verify', $downloadPath, '--all', '--configfile', $config, '--verbosity', 'normal', '--force-english-output') (Join-Path $output $signatureLog)
                $signatureText = $signature -join "`n"
                Require ($signatureText.Contains('Signature type: Repository') -and $signatureText.Contains("Service index: $source")) 'Expected a valid NuGet.org repository signature.'
                $report.Packages += [ordered]@{ Id=$package.Id; Version=$Version; File=$package.File; OriginalSHA256=$package.SHA256;
                    PublishedSHA256=$download.SHA256; PublishedSHA512=(Sha512-File $downloadPath); Index=$index; Download=$download;
                    Entries=$entries; AddedSignatureSHA256=(Hash-Bytes $published['.signature.p7s']); SignatureVerification=[ordered]@{ ExitCode=0; Log=$signatureLog } }
                Save-Json $report (Join-Path $output 'verification.json')
            }
        } finally { $client.Dispose(); $handler.Dispose() }

        $core = @($record.Packages | Where-Object Id -eq 'PolylineKit.Core')[0]
        $adapter = @($record.Packages | Where-Object Id -eq 'PolylineKit.Clipper')[0]
        foreach ($target in @('net10.0', 'net8.0')) {
            $assetTarget = if ($target -eq 'net10.0') { 'net10.0' } else { 'netstandard2.0' }
            foreach ($kind in @('Core', 'Clipper')) {
                $name = "$kind-$target"; $directory = Join-Path $workspace $name; $evidence = Join-Path $output $name
                New-Item -ItemType Directory -Path $directory, $evidence | Out-Null
                $cache = Join-Path $directory 'packages'
                New-Item -ItemType Directory -Path $cache | Out-Null
                $env:NUGET_PACKAGES = $cache
                $env:NUGET_HTTP_CACHE_PATH = Join-Path $directory 'http-cache'
                $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $directory 'plugin-cache'
                $project = Join-Path $directory 'Consumer.csproj'
                Copy-Item -LiteralPath (Join-Path $repository 'examples/PackageCoverage/PackageCoverage.csproj') -Destination $project
                Copy-Item -LiteralPath (Join-Path $repository 'examples/PackageCoverage/Program.cs'), (Join-Path $repository 'tests/PackageConsumers/PackageVerification.cs') -Destination $directory
                [xml]$projectXml = Get-Content -LiteralPath $project -Raw
                Require ($projectXml.SelectNodes("//*[local-name()='ProjectReference' or local-name()='Reference' or local-name()='Import']").Count -eq 0) 'Consumers must use PackageReference without project references, private DLLs, or custom imports.'
                Copy-Item -LiteralPath $project, (Join-Path $directory 'Program.cs'), (Join-Path $directory 'PackageVerification.cs') -Destination $evidence
                $withClipper = if ($kind -eq 'Clipper') { 'true' } else { 'false' }
                # The SDK adds library-packs separately from its fallback folder. Disable both.
                $properties = @("-p:TargetFramework=$target", "-p:PolylineKitVersion=$Version", "-p:UseClipper=$withClipper", '-p:VerifyPackages=true',
                    '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false', '-p:ImportDirectoryPackagesProps=false',
                    '-p:DisableImplicitNuGetFallbackFolder=true', '-p:DisableImplicitLibraryPacksFolder=true', '-p:RestoreFallbackFolders=',
                    '-p:RestoreAdditionalProjectFallbackFolders=', '-p:RestoreAdditionalProjectSources=', "-p:RestoreSources=$source", '-p:NuGetAudit=false', '-p:NuGetInteractive=false')
                Save-Json ([ordered]@{ FreshCache=$cache; Properties=$properties; PackageSource=$source }) (Join-Path $evidence 'invocation.json')
                $null = Run-Dotnet (@('restore', $project, '--configfile', $config, '--packages', $cache, '--no-cache', '--force', '--verbosity', 'normal') + $properties) (Join-Path $evidence 'restore.log')
                Copy-Item -LiteralPath (Join-Path $directory 'obj') -Destination $evidence -Recurse
                $assetsPath = Join-Path $directory 'obj/project.assets.json'
                $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
                Require ($assets.project.restore.sources.Count -eq 1 -and $assets.project.restore.sources.ContainsKey($source)) 'Restore must use NuGet.org alone.'
                Require ($assets.packageFolders.Count -eq 1 -and [IO.Path]::GetFullPath(@($assets.packageFolders.Keys)[0]).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $cache -and
                    [IO.Path]::GetFullPath($assets.project.restore.packagesPath).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $cache) 'Restore used a shared cache or fallback folder.'
                Require ($assets.project.restore.projectStyle -eq 'PackageReference' -and
                    @($assets.project.restore.frameworks.Values.projectReferences.Keys).Count -eq 0) 'Restore graph contains project references.'
                $expectedKeys = @("PolylineKit.Core/$Version")
                if ($kind -eq 'Clipper') {
                    $expectedKeys += "PolylineKit.Clipper/$Version"
                    $dependencyTarget = if ($assetTarget -eq 'net10.0') { 'net10.0' } else { '.NETStandard2.0' }
                    $dependencyGroup = @($adapter.Dependencies | Where-Object Target -eq $dependencyTarget)
                    Require ($dependencyGroup.Count -eq 1) 'Original dependency group is missing.'
                    foreach ($dependency in $dependencyGroup[0].Packages | Where-Object Id -ne 'PolylineKit.Core') {
                        Require ($dependency.Version -match '^\[?([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)(?:\]|, \))?$') 'Expected an exact dependency version or its minimum-version range.'
                        $expectedKeys += "$($dependency.Id)/$($Matches[1])"
                    }
                }
                Require ((@($assets.libraries.Keys | Sort-Object) -join '|') -eq (@($expectedKeys | Sort-Object) -join '|')) 'Unexpected dependency graph; Core must have zero third-party package dependencies.'
                $cacheProof = @()
                foreach ($key in $assets.libraries.Keys | Sort-Object) {
                    $library = $assets.libraries[$key]; $selected = $assets.targets[$target][$key]
                    $cached = Join-Path $cache $library.path
                    $originPath = Join-Path $cached '.nupkg.metadata'
                    $origin = Get-Content -LiteralPath $originPath -Raw | ConvertFrom-Json
                    Require ($library.type -eq 'package' -and $origin.source -eq $source) "Package did not originate from NuGet.org: $key"
                    $pair = $key -split '/'; $nupkg = Join-Path $cached "$($pair[0].ToLowerInvariant()).$($pair[1]).nupkg"
                    Require ((Get-Content -LiteralPath ($nupkg + '.sha512') -Raw).Trim() -eq (Sha512-File $nupkg)) "Cached ZIP checksum differs: $key"
                    $verifiedPackage = @($record.Packages | Where-Object Id -eq $pair[0])
                    if ($verifiedPackage.Count) {
                        $baseline = $verifiedPackage[0]; $publicPackage = @($report.Packages | Where-Object Id -eq $pair[0])[0]
                        $expectedAsset = "lib/$assetTarget/$($baseline.Assembly).dll"
                        Require ($library.sha512 -eq $baseline.SHA512 -and $origin.contentHash -eq $baseline.SHA512 -and
                            (Hash-File $nupkg) -eq $publicPackage.PublishedSHA256) "Restored package differs from the audited public ZIP: $key"
                        Require (@($selected.compile.Keys).Count -eq 1 -and $selected.compile.ContainsKey($expectedAsset) -and
                            @($selected.runtime.Keys).Count -eq 1 -and $selected.runtime.ContainsKey($expectedAsset) -and
                            (Hash-File (Join-Path $cached $expectedAsset)) -eq $baseline.Assets[$assetTarget]) "Compile/runtime DLL selection differs: $key"
                    }
                    Copy-Item -LiteralPath $originPath -Destination (Join-Path $evidence "$($pair[0]).metadata.json")
                    $cacheProof += [ordered]@{ Key=$key; Source=$origin.source; ContentHash=$origin.contentHash; NupkgSHA256=(Hash-File $nupkg);
                        MetadataSHA256=(Hash-File $originPath); CompileAssets=@($selected.compile.Keys); RuntimeAssets=@($selected.runtime.Keys) }
                }
                $null = Run-Dotnet (@('build', $project, '-c', 'Release', '--no-restore', '--verbosity', 'normal') + $properties) (Join-Path $evidence 'build.log')
                $bin = Join-Path $directory "bin/Release/$target"
                $deps = Get-Content -LiteralPath (Join-Path $bin 'Consumer.deps.json') -Raw | ConvertFrom-Json -AsHashtable
                Require ((@($deps.libraries.Keys | Where-Object { $deps.libraries[$_].type -eq 'package' } | Sort-Object) -join '|') -eq
                    (@($expectedKeys | Sort-Object) -join '|')) 'Runtime dependency graph differs.'
                $runtimeProof = @()
                foreach ($key in $expectedKeys) {
                    $library = $assets.libraries[$key]; $selected = $assets.targets[$target][$key]
                    foreach ($asset in $selected.runtime.Keys | Where-Object { $_.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) }) {
                        $cachedDll = Join-Path (Join-Path $cache $library.path) $asset
                        $runtimeDll = Join-Path $bin ([IO.Path]::GetFileName($asset))
                        Require ((Hash-File $runtimeDll) -eq (Hash-File $cachedDll)) "Runtime output DLL differs from the NuGet cache: $key/$asset"
                        $runtimeProof += [ordered]@{ Package=$key; Asset=$asset; SHA256=(Hash-File $runtimeDll) }
                    }
                }
                $lines = Run-Dotnet @((Join-Path $bin 'Consumer.dll'), $assetTarget, $Version) (Join-Path $evidence 'run.log')
                $result = $lines[-1] | ConvertFrom-Json -AsHashtable
                Require ($result.CoreSha256 -eq $core.Assets[$assetTarget] -and $result.CoreVersion -ceq "$Version+$($record.SourceRevision)") 'Runtime Core DLL or source revision differs.'
                if ($kind -eq 'Clipper') { Require ($result.AdapterSha256 -eq $adapter.Assets[$assetTarget]) 'Runtime adapter DLL differs.' }
                else { Require ($null -eq $result.AdapterAssembly -and $null -eq $result.AdapterSha256) 'Core execution loaded an adapter.' }
                Copy-Item -LiteralPath $bin -Destination (Join-Path $evidence 'runtime') -Recurse
                Save-Json $result (Join-Path $evidence 'run.json')
                $report.Consumers += [ordered]@{ Name=$name; ConsumerTarget=$target; SelectedAssetTarget=$assetTarget; FreshCache=$cache;
                    AssetsSHA256=(Hash-File $assetsPath); DependencyGraph=$expectedKeys; Packages=$cacheProof; RuntimeAssets=$runtimeProof; Result=$result }
                Save-Json $report (Join-Path $output 'verification.json')
                Write-Output "PASS: $name restored NuGet.org packages and executed $assetTarget DLLs."
            }
        }
        Require ((Hash-File $recordPath) -eq $report.OriginalVerificationSHA256) 'Original verification record changed during verification.'
        $report.Complete = $true
        Save-Json $report (Join-Path $output 'verification.json')
    } finally { Pop-Location }
} catch {
    if ($null -ne $report) {
        $report['Error'] = $_.Exception.Message
        $report['RetainedWorkspace'] = $workspace
        Save-Json $report (Join-Path $output 'verification.json')
    }
    throw
} finally {
    $environmentProof = @()
    foreach ($name in $saved.Keys) {
        # PowerShell converts $null to an empty string for this API unless NullString is used.
        $originalState = if ($null -eq $saved[$name]) { 'absent' } elseif ($saved[$name] -ceq '') { 'empty' } else { 'set' }
        $check = [ordered]@{ Name=$name; OriginalState=$originalState; Restored=$false }
        try {
            if ($null -eq $saved[$name]) { [Environment]::SetEnvironmentVariable($name, [NullString]::Value) }
            else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
            $actual = [Environment]::GetEnvironmentVariable($name)
            $check['RestoredState'] = if ($null -eq $actual) { 'absent' } elseif ($actual -ceq '') { 'empty' } else { 'set' }
            $check.Restored = if ($null -eq $saved[$name]) { $null -eq $actual } else { $null -ne $actual -and $actual -ceq $saved[$name] }
        } catch { $check['Error'] = $_.Exception.Message }
        $environmentProof += $check
    }
    $environmentRestored = @($environmentProof | Where-Object { !$_.Restored }).Count -eq 0
    if ($null -ne $report) {
        $report['EnvironmentRestored'] = $environmentRestored
        $report['Environment'] = $environmentProof
        if (!$environmentRestored) {
            $report.Complete = $false
            $report['Error'] = ($report.Error + ' Environment restoration failed; see the per-variable checks.').Trim()
            $report['RetainedWorkspace'] = $workspace
        }
        Save-Json $report (Join-Path $output 'verification.json')
    }
    if ($workspaceOwned -and $null -ne $report -and $report.Complete -and (Test-Path -LiteralPath $workspace)) {
        $resolved = (Resolve-Path -LiteralPath $workspace).Path
        Require ($resolved -eq $workspace -and [IO.Path]::GetDirectoryName($resolved) -eq $tempRoot -and
            [IO.Path]::GetFileName($resolved) -ceq ('polylinekit-published-' + $runId) -and
            !((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Refusing to delete an unexpected temporary workspace.'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } elseif ($workspaceOwned) { Write-Output "External workspace retained for inspection: $workspace" }
    Require $environmentRestored 'Environment restoration failed; saved evidence records the affected variables.'
}
Write-Output "PASS: both published ZIP payloads, signatures and four fresh package consumers. Evidence: $output"
