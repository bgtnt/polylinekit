[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$KeepWorkspace
)

# An existing SDK library-packs feed must not enter the outside consumer's graph.
# This wraps one real package probe; it neither reruns the corpus nor measures time.
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$runId = [Guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$sdkFeed = Join-Path $tempRoot ('polylinekit-sdk-feed-regression-' + $runId)
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repository ('artifacts/package-isolation-regression-' + $runId) }
$variable = '_WorkloadLibraryPacksFolder'
$previousFeed = [Environment]::GetEnvironmentVariable($variable)
$injected = $false
$ownedFeed = $false
$ownedOutput = $false
$report = [ordered]@{ Passed = $false; Utc = [DateTimeOffset]::UtcNow.ToString('o'); InjectedSDKFeed = $sdkFeed; CorpusExecuted = $false; TimingExecuted = $false }

function Require-Isolation([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

try {
    Require-Isolation ($PSVersionTable.PSVersion.Major -ge 7) 'Use PowerShell 7 or later.'
    Require-Isolation (!(Test-Path -LiteralPath $output) -or @(Get-ChildItem -LiteralPath $output -Force).Count -eq 0) 'Use an empty isolation evidence directory.'
    Require-Isolation (!(Test-Path -LiteralPath $sdkFeed)) 'The newly generated SDK-feed test directory already exists.'
    $ownedOutput = $true
    New-Item -ItemType Directory -Path $sdkFeed | Out-Null
    $ownedFeed = $true
    Require-Isolation (@(Get-ChildItem -LiteralPath $sdkFeed -Force).Count -eq 0) 'The injected SDK feed must be empty.'
    [Environment]::SetEnvironmentVariable($variable, $sdkFeed)
    $injected = $true
    Require-Isolation ([Environment]::GetEnvironmentVariable($variable) -ceq $sdkFeed) 'The SDK-feed environment injection failed.'

    & (Join-Path $PSScriptRoot 'verify-package.ps1') -OutputDirectory $output -KeepWorkspace:$KeepWorkspace
    $proofPath = Join-Path $output 'verification.json'
    $assetsPath = Join-Path $output 'project.assets.json'
    $proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json -AsHashtable
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    Require-Isolation ($proof.Complete -eq $true -and $proof.PackageCacheInitiallyEmpty -eq $true -and $proof.PublicSourceOnly -eq $true) 'The real outside-checkout package proof did not complete.'
    $disableFlag = '-p:DisableImplicitLibraryPacksFolder=true'
    Require-Isolation (@($proof.RestoreArguments | Where-Object { $_ -ceq $disableFlag }).Count -eq 1) 'The SDK library-packs feed was not disabled exactly once on the actual restore command.'
    $source = 'https://api.nuget.org/v3/index.json'
    Require-Isolation (@($assets.project.restore.sources.Keys).Count -eq 1 -and $assets.project.restore.sources.ContainsKey($source)) 'The existing SDK feed entered the restore sources.'
    Require-Isolation (@($assets.packageFolders.Keys).Count -eq 1 -and
        [IO.Path]::GetFullPath(@($assets.packageFolders.Keys)[0]).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq [IO.Path]::GetFullPath($proof.PackageCache)) 'A shared SDK or fallback package cache entered the graph.'
    Require-Isolation ($proof.RestoredCore.Source -ceq $source -and
        $proof.RestoredCore.RuntimeAsset -ceq 'lib/net10.0/PolylineKit.Winding.dll' -and
        $proof.RestoredCore.SHA256 -ceq 'bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca' -and
        $proof.PackageCheck.Validation.Passed -eq $true) 'The published alpha.2 package identity or analytic smoke changed.'
    Require-Isolation ((Test-Path -LiteralPath $sdkFeed -PathType Container) -and @(Get-ChildItem -LiteralPath $sdkFeed -Force).Count -eq 0) 'The injected SDK feed was removed or modified during the probe.'
    $report.SDKSourceDisabledBy = $disableFlag
    $report.RestoreSources = @($assets.project.restore.sources.Keys)
    $report.CoreSHA256 = $proof.RestoredCore.SHA256
    $report.AnalyticChecks = $proof.PackageCheck.Validation.Checks
    $report.PackageProofSHA256 = (Get-FileHash -LiteralPath $proofPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.AssetsSHA256 = (Get-FileHash -LiteralPath $assetsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.Passed = $true
    Write-Output "PASS: an existing injected SDK library-packs feed was excluded from the fresh NuGet.org-only consumer. Evidence: $output"
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    if ($injected) { [Environment]::SetEnvironmentVariable($variable, $previousFeed) }
    $report.EnvironmentRestored = [Environment]::GetEnvironmentVariable($variable) -ceq $previousFeed
    if ($ownedFeed -and (Test-Path -LiteralPath $sdkFeed)) {
        # Delete only our verified empty immediate child of the temporary directory.
        $resolved = (Resolve-Path -LiteralPath $sdkFeed).Path
        Require-Isolation ($resolved -eq $sdkFeed -and [IO.Path]::GetDirectoryName($resolved) -eq $tempRoot -and
            [IO.Path]::GetFileName($resolved).StartsWith('polylinekit-sdk-feed-regression-', [StringComparison]::Ordinal) -and
            @(Get-ChildItem -LiteralPath $resolved -Force).Count -eq 0) 'Refusing to remove an unexpected SDK-feed test directory.'
        Remove-Item -LiteralPath $resolved -Force
    }
    if ($ownedOutput -and (Test-Path -LiteralPath $output -PathType Container)) {
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'isolation-check.json') -Encoding utf8
    }
    Require-Isolation $report.EnvironmentRestored 'The original SDK-feed environment setting was not restored.'
}
