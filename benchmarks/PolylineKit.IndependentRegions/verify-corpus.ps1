[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunnerPath,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$PackageProof,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('alpha.2', 'alpha.3')][string]$Release = 'alpha.2'
)

# A Core/NTS CI check is deliberately distinct from the unchanged three-way
# accuracy gate. The original check report, its exit code and all failures remain.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseProfiles.ps1')
$profile = Get-IndependentReleaseProfile $Release
$coreBackend = 'Core-' + $Release

function Require-Corpus([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function Corpus-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Corpus-Finite([object]$Value, [string]$Description) {
    Require-Corpus ($null -ne $Value) "Missing $Description."
    $number = [double]$Value
    Require-Corpus (![double]::IsNaN($number) -and ![double]::IsInfinity($number)) "Nonfinite $Description."
    return $number
}

function Assert-CorpusReport {
    param(
        [Parameter(Mandatory)]$Report,
        [Parameter(Mandatory)][int]$ActualExitCode,
        [Parameter(Mandatory)][string]$Runner,
        [Parameter(Mandatory)][string]$Data,
        [Parameter(Mandatory)]$Proof
    )
    Require-Corpus ($Report.SchemaVersion -eq 1 -and $null -ne $Report.Validation -and $null -ne $Report.Metadata) 'Incomplete corpus check report.'
    $validation = $Report.Validation
    $metadata = $Report.Metadata
    Require-Corpus ($Proof.Complete -eq $true -and $Proof.PublicSourceOnly -eq $true -and $Proof.PackageCacheInitiallyEmpty -eq $true -and
        $Proof.Package -ceq "PolylineKit.Core/$($profile.Version)" -and $Proof.ExpectedSourceRevision -ceq $profile.SourceRevision -and
        $Proof.RestoredCore.Type -ceq 'package' -and $Proof.RestoredCore.Version -ceq $profile.Version -and
        $Proof.RestoredCore.Source -ceq 'https://api.nuget.org/v3/index.json' -and
        $Proof.RestoredCore.RuntimeAsset -ceq 'lib/net10.0/PolylineKit.Winding.dll' -and
        $Proof.RestoredCore.SHA256 -ceq $profile.CoreNet10Sha256 -and
        $Proof.PackageCheck.SchemaVersion -eq 1 -and $Proof.PackageCheck.Validation.Passed -eq $true -and
        $Proof.PackageCheck.Validation.Checks -eq 36) 'Published-package isolation/analytic proof is incomplete or differs.'
    Require-Corpus ($metadata.AssessmentRevision -cmatch '^[0-9a-f]{40}$' -and
        $metadata.AssessmentRevision -ceq $Proof.PackageCheck.Metadata.AssessmentRevision) 'Assessment source revision differs from package proof.'
    if ($Release -eq 'alpha.3' -or $null -ne $metadata.ReleaseProfile) {
        Require-Corpus ($metadata.ReleaseProfile -ceq $Release -and $Proof.ReleaseProfile -ceq $Release -and
            $metadata.ReleaseProfilesSha256 -ceq (Corpus-Hash (Join-Path $PSScriptRoot 'release-profiles.json')) -and
            $metadata.ReleaseProfilesSha256 -ceq $Proof.ReleaseProfilesSHA256 -and
            $metadata.AssessmentSourceState -cin @('clean', 'development') -and $metadata.AssessmentSourceState -ceq $Proof.AssessmentSourceState -and
            $metadata.AssessmentSourceState -ceq $Proof.PackageCheck.Metadata.AssessmentSourceState) 'Release profile or clean/development runner provenance differs.'
    }
    $expectedAssemblies = @('Clipper2Lib', 'NetTopologySuite', 'PolylineKit.IndependentRegions', 'PolylineKit.Winding')
    foreach ($set in @($metadata.Assemblies, $Proof.PackageCheck.Metadata.Assemblies)) {
        Require-Corpus (@($set).Count -eq 4 -and !(Compare-Object $expectedAssemblies @($set.Assembly | Sort-Object))) 'Incomplete loaded assembly identity inventory.'
    }
    foreach ($assembly in $metadata.Assemblies) {
        $proven = @($Proof.PackageCheck.Metadata.Assemblies | Where-Object Assembly -CEQ $assembly.Assembly)[0]
        Require-Corpus ($assembly.Sha256 -cmatch '^[0-9a-f]{64}$' -and $assembly.Sha256 -ceq $proven.Sha256 -and
            $assembly.InformationalVersion -ceq $proven.InformationalVersion -and $assembly.TargetFramework -ceq $proven.TargetFramework -and
            [IO.Path]::GetFullPath($assembly.Location) -ceq [IO.Path]::GetFullPath($proven.Location) -and
            (Corpus-Hash $assembly.Location) -ceq $assembly.Sha256) ('Loaded binary differs from package proof: ' + $assembly.Assembly)
    }
    $runnerIdentity = @($metadata.Assemblies | Where-Object Assembly -CEQ 'PolylineKit.IndependentRegions')[0]
    $coreIdentity = @($metadata.Assemblies | Where-Object Assembly -CEQ 'PolylineKit.Winding')[0]
    Require-Corpus ([IO.Path]::GetFullPath($runnerIdentity.Location) -ceq [IO.Path]::GetFullPath($Runner) -and
        $Proof.ConsumerSHA256 -ceq $runnerIdentity.Sha256 -and (Corpus-Hash $Runner) -ceq $runnerIdentity.Sha256 -and
        $coreIdentity.Sha256 -ceq $profile.CoreNet10Sha256 -and
        $coreIdentity.InformationalVersion -ceq ($profile.Version + '+' + $profile.SourceRevision) -and
        $coreIdentity.TargetFramework -ceq '.NETCoreApp,Version=v10.0') 'The check did not use the proven published release consumer.'
    foreach ($pair in @(@('PolylineKit.Core', $profile.Version), @('Clipper2', '2.0.0'), @('NetTopologySuite', '2.6.0'))) {
        Require-Corpus ($metadata.PackageVersions.PSObject.Properties[$pair[0]].Value -ceq $pair[1]) ('Package version differs: ' + $pair[0])
    }
    Require-Corpus (@($metadata.PackageVersions.PSObject.Properties).Count -eq 3 -and $metadata.SpatialReference -ceq 'EPSG:3067' -and
        $metadata.CoordinateUnit -ceq 'metre' -and $metadata.AreaBudgetM2 -eq 1 -and $metadata.FractionBudget -eq 1e-8 -and
        $metadata.ClipperGridMetres -eq 1e-6) 'Package/coordinate/budget contract differs.'
    $manifestPath = Join-Path $Data 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Require-Corpus ($metadata.ManifestSha256 -ceq (Corpus-Hash $manifestPath) -and
        $metadata.SourceProtocolSha256 -ceq $manifest.ProtocolSha256 -and $metadata.SourceProtocolSha256 -ceq (Corpus-Hash (Join-Path $Data 'PROTOCOL.md')) -and
        $metadata.BenchmarkProtocolSha256 -ceq (Corpus-Hash (Join-Path $Data '../PROTOCOL.md'))) 'Check protocol or manifest differs from the frozen input.'
    $expectedFiles = @($manifest.SourceFiles) + @($manifest.Layers | ForEach-Object {
        [pscustomobject]@{ File = $_.DerivedFile; Sha256 = $_.DerivedSha256; Bytes = $_.DerivedBytes }
    })
    $recordedFiles = @($metadata.SourceFiles) + @($metadata.DerivedFiles)
    Require-Corpus ($expectedFiles.Count -eq 14 -and $recordedFiles.Count -eq 14 -and
        @($recordedFiles.File | Sort-Object -Unique).Count -eq 14) 'Incomplete source/derived snapshot inventory.'
    $dataPrefix = [IO.Path]::GetFullPath($Data).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($inputFile in $expectedFiles) {
        $recorded = @($recordedFiles | Where-Object File -CEQ $inputFile.File)
        $path = [IO.Path]::GetFullPath((Join-Path $Data $inputFile.File))
        Require-Corpus ($path.StartsWith($dataPrefix, [StringComparison]::Ordinal) -and $recorded.Count -eq 1 -and
            $recorded[0].Sha256 -ceq $inputFile.Sha256 -and $recorded[0].Bytes -eq $inputFile.Bytes -and
            (Get-Item -LiteralPath $path).Length -eq $inputFile.Bytes -and (Corpus-Hash $path) -ceq $inputFile.Sha256) ('Frozen snapshot differs: ' + $inputFile.File)
    }
    $originalIds = @('2','17','73','80','254','598','612','622','640','642','645','647','650','652','653','654','656','657','658','659','660','662','664','665','667','668','670','979','980','981','1000')
    $originalIssues = @($validation.InputIssues | Where-Object Stage -CEQ 'original-NTS')
    $quantizedIssues = @($validation.InputIssues | Where-Object Stage -CEQ 'quantized-NTS')
    Require-Corpus ($originalIssues.Count -eq 31 -and @($originalIssues | Where-Object Role -CNE 'queries').Count -eq 0 -and
        !(Compare-Object ($originalIds | Sort-Object) @($originalIssues.Id | Sort-Object)) -and
        @($validation.InputIssues).Count -eq $originalIssues.Count + $quantizedIssues.Count -and $quantizedIssues.Count -le 1 -and
        @($quantizedIssues | Where-Object { $_.Role -cne 'queries' -or $_.Id -cne '78' -or !$_.Reason }).Count -eq 0) 'Unexpected original/quantized validity population.'
    $issueKeys = @($validation.InputIssues | ForEach-Object { $_.Role + '/' + $_.Id + '/' + $_.Stage + '/' + $_.Reason } | Sort-Object)
    $metadataIssueKeys = @($metadata.InputIssues | ForEach-Object { $_.Role + '/' + $_.Id + '/' + $_.Stage + '/' + $_.Reason } | Sort-Object)
    Require-Corpus ($issueKeys.Count -eq $metadataIssueKeys.Count -and !(Compare-Object $issueKeys $metadataIssueKeys)) 'Validation and metadata validity populations differ.'
    foreach ($layer in $manifest.Layers) {
        $inventory = if ($layer.Role -ceq 'zones') { $metadata.Zones } else { $metadata.Queries }
        $admitted = @($layer.Features | Where-Object { $layer.Role -ceq 'zones' -or $_.Id -cnotin $originalIds })
        Require-Corpus ($inventory.SourceFeatures -eq $layer.SourceFeatures -and $inventory.Components -eq $layer.Components -and
            $inventory.Rings -eq $layer.Rings -and $inventory.Holes -eq $layer.Holes -and $inventory.SourceVertices -eq $layer.Vertices -and
            $inventory.EligibleFeatures -eq $admitted.Count -and $inventory.EligibleComponents -eq ($admitted.Components | Measure-Object -Sum).Sum -and
            $inventory.EligibleRings -eq ($admitted.Rings | Measure-Object -Sum).Sum -and $inventory.EligibleHoles -eq ($admitted.Holes | Measure-Object -Sum).Sum -and
            $inventory.EligibleSourceVertices -eq ($admitted.Vertices | Measure-Object -Sum).Sum) ('Source/admitted geometry inventory differs: ' + $layer.Role)
    }
    Require-Corpus ($validation.SourceZones -eq 34 -and $validation.SourceQueries -eq 1095 -and $validation.EligibleZones -eq 34 -and
        $validation.EligibleQueries -eq 1064 -and $validation.ExcludedOriginalInputs -eq 31 -and
        $validation.ComparisonsUseOnlyValidOriginalTopology -eq $true -and $validation.PairsPerDirection -eq 36176 -and
        $validation.BoundingBoxCandidatesPerDirection -eq 2426 -and $validation.QuantizedBoundsMembershipChanges -eq 0 -and
        $metadata.SourcePairsPerDirection -eq 37230 -and $metadata.SourceBoundingBoxCandidatesPerDirection -eq 2560 -and
        $metadata.EligiblePairsPerDirection -eq 36176 -and $metadata.EligibleBoundingBoxCandidatesPerDirection -eq 2426) 'Frozen source/admission/pair population differs.'
    Require-Corpus ($validation.CoreSelectedApiCalls -eq 144704 -and $validation.SelectedIntersectionCalls -eq 434112 -and
        $validation.FullPairs -eq 2490 -and $validation.FullCompareCalls -eq 29880 -and $validation.Assertions -eq 1090560 -and
        @($validation.DisjointFullPairIds).Count -eq 64 -and @($validation.DisjointFullPairIds | Sort-Object -Unique).Count -eq 64) 'Incomplete actual API calls or metric assertions.'
    $observations = @($validation.OriginalInvalidCoreObservations)
    Require-Corpus ($observations.Count -eq 62 -and @($observations | ForEach-Object { $_.Id + '/' + $_.FillRule } | Sort-Object -Unique).Count -eq 62) 'Incomplete original-invalid Core observations.'
    foreach ($observation in $observations) {
        Require-Corpus ($observation.Role -ceq 'queries' -and $observation.Id -cin $originalIds -and $observation.FillRule -cin @('NonZero','EvenOdd') -and
            $observation.CoreStructureAccepted -eq $true -and $observation.FactorySucceeded -eq $true -and $observation.FilledAreaInvoked -eq $true -and
            !$observation.ExceptionType -and !$observation.Exception -and (Corpus-Finite $observation.AreaM2 'unreferenced Core area') -ge -1) 'Core observation failed on a structurally accepted original-invalid input.'
    }
    $metrics = @('Coverage','FirstAreaM2','FullIntersectionM2','IoU','OwnAreaM2','SecondAreaM2','SelectedIntersectionM2','SelectedVsFullIntersectionM2','SymmetricDifferenceM2','UnionM2')
    $knownCoverageInputs = @('112827/606','112828/152','112828/606','112828/1065','112829/813','112830/168','112830/1065',
        '112831/128','112831/813','112834/258','112840/235','112840/261','112854/994','112854/995','112857/788','112857/848',
        '112857/919','112857/922','112857/924','112857/925','117935/136')
    $rows = @($validation.Errors)
    $keys = @($rows | ForEach-Object { $_.Backend + '/' + $_.FillRule + '/' + $_.Direction + '/' + $_.Metric })
    $expectedKeys = @(foreach ($backend in @($coreBackend,'Clipper64-reused-data','NTS-OverlayNGRobust')) {
        foreach ($rule in @('NonZero','EvenOdd')) { foreach ($direction in @('zones-to-queries','queries-to-zones')) {
            foreach ($metric in $metrics) { "$backend/$rule/$direction/$metric" }
        } }
    })
    Require-Corpus ($rows.Count -eq 120 -and @($keys | Sort-Object -Unique).Count -eq 120 -and
        !(Compare-Object ($expectedKeys | Sort-Object) ($keys | Sort-Object))) 'Incomplete numeric summary matrix.'
    foreach ($row in $rows) {
        $fraction = $row.Metric -cin @('Coverage','IoU')
        $budget = if ($fraction) { 1e-8 } else { 1 }
        $checks = if ($row.Metric -ceq 'OwnAreaM2') { 1098 } elseif ($row.Metric -cin @('SelectedIntersectionM2','Coverage')) { 36176 } else { 2490 }
        Require-Corpus ($row.Budget -eq $budget -and $row.Checks -eq $checks -and $row.NonfiniteChecks -eq 0 -and
            $row.ZeroValues + $row.PositiveValues + $row.NegativeValues -eq $checks -and $row.WorstInput) 'Incomplete/nonfinite numeric summary or changed budget.'
        $error = Corpus-Finite $row.MaximumAbsoluteError 'maximum error'
        $minimum = Corpus-Finite $row.MinimumActual 'minimum actual value'
        $maximum = Corpus-Finite $row.MaximumActual 'maximum actual value'
        $null = Corpus-Finite $row.ActualAtWorst 'worst actual value'
        $null = Corpus-Finite $row.ReferenceAtWorst 'worst reference value'
        Require-Corpus ($error -ge 0 -and $minimum -le $maximum) 'Invalid metric extrema.'
        $knownCoverage = $row.Backend -ceq 'Clipper64-reused-data' -and $row.Metric -ceq 'Coverage' -and $row.Direction -ceq 'queries-to-zones'
        if (!$knownCoverage) {
            Require-Corpus ($row.DomainBudgetViolations -eq 0 -and $error -le $budget -and $minimum -ge -$budget -and
                (!$fraction -or $maximum -le 1 + $budget)) ('Core/NTS or unexpected Clipper numerical failure: ' + $row.Backend + '/' + $row.Metric)
        } else {
            Require-Corpus ($row.DomainBudgetViolations -eq 0 -and $minimum -ge -$budget -and $maximum -le 1 + $budget -and
                ($error -le $budget -or $row.WorstInput -cin $knownCoverageInputs)) 'An unreviewed Clipper coverage extremum or domain failure was found.'
        }
    }
    $failures = @($validation.Failures)
    Require-Corpus ($validation.OmittedFailureDetails -eq 0 -and $validation.FailureCount -eq $failures.Count) 'Failure details were omitted or counts differ.'
    foreach ($failure in $failures) {
        $knownGrid = $failure.Stage -ceq 'quantized-NTS' -and $failure.Input -ceq 'queries/78' -and $quantizedIssues.Count -eq 1
        $knownCoverage = $failure.Stage -cmatch '^Clipper64-reused-data/(NonZero|EvenOdd)/queries-to-zones/Coverage$' -and
            $failure.Input -cin $knownCoverageInputs
        Require-Corpus (($knownGrid -or $knownCoverage) -and $failure.Reason) ('Core/NTS or unexpected comparator failure: ' + $failure.Stage + '/' + $failure.Input)
        if ($knownCoverage) {
            $null = Corpus-Finite $failure.Actual 'failed Clipper coverage'
            $null = Corpus-Finite $failure.Reference 'failed coverage reference'
        }
    }
    $threeWayPassed = $failures.Count -eq 0
    Require-Corpus ($validation.Passed -eq $threeWayPassed -and $ActualExitCode -eq $(if ($threeWayPassed) { 0 } else { 1 })) 'Original three-way status/exit code disagrees with its retained failures.'
    return [ordered]@{
        ReleaseProfile = $Release; Package = "PolylineKit.Core/$($profile.Version)"; AssessmentRevision = $metadata.AssessmentRevision
        AssessmentSourceState = $metadata.AssessmentSourceState
        CoreAgreementPassed = $true; ThreeWayPassed = $threeWayPassed; OriginalCheckExitCode = $ActualExitCode
        ThreeWayFailureCount = $failures.Count; KnownComparatorLimits = @($failures.Stage | Sort-Object -Unique)
        SourceFeatures = 1129; OriginalNtsInvalidFeatures = 31; AdmittedPairsPerDirection = 36176
        CoreSelectedApiCalls = 144704; FullCompareCalls = 29880
        Meaning = 'Core/NTS agreement only. Original three-way report and failures are retained; this result does not permit timings or claim absolute accuracy.'
    }
}

# Contract tests dot-source definitions only; ordinary invocation always calls
# the actual runner exactly once. No prerecorded-report execution mode exists.
if ($MyInvocation.InvocationName -eq '.') { return }

$runner = [IO.Path]::GetFullPath($RunnerPath)
$data = [IO.Path]::GetFullPath($DataRoot)
$proofPath = [IO.Path]::GetFullPath($PackageProof)
$destination = [IO.Path]::GetFullPath($Output)
$summaryPath = $destination + '.core-verification.json'
$logPath = $destination + '.log'
foreach ($path in @($destination, $summaryPath, $logPath)) {
    Require-Corpus (!(Test-Path -LiteralPath $path)) ('Refusing to overwrite evidence: ' + $path)
}
Require-Corpus (Test-Path -LiteralPath $runner -PathType Leaf) 'RunnerPath must name the proven consumer DLL.'
Require-Corpus (Test-Path -LiteralPath $data -PathType Container) 'DataRoot must name the frozen corpus.'
$proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
$savedNativePreference = $PSNativeCommandUseErrorActionPreference
try {
    $PSNativeCommandUseErrorActionPreference = $false
    $lines = @(& dotnet $runner check $data $destination 2>&1)
    $actualExitCode = $LASTEXITCODE
} finally { $PSNativeCommandUseErrorActionPreference = $savedNativePreference }
$lines | Set-Content -LiteralPath $logPath -Encoding utf8
$lines | ForEach-Object { Write-Output $_ }
Require-Corpus (Test-Path -LiteralPath $destination -PathType Leaf) 'Runner did not produce its original check report.'
$report = Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json
$result = Assert-CorpusReport -Report $report -ActualExitCode $actualExitCode -Runner $runner -Data $data -Proof $proof
$result.CheckReportSha256 = Corpus-Hash $destination
$result.PackageProofSha256 = Corpus-Hash $proofPath
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Output "PASS: published $Release Core/NTS agreement on the frozen corpus. Evidence: $destination"
if (!$result.ThreeWayPassed) {
    Write-Output "FAILED: original three-way accuracy gate; exit $actualExitCode and $($result.ThreeWayFailureCount) comparator failures retained. Timings remain forbidden."
}
# This script succeeded at its narrower Core-agreement contract. Do not let a
# caller's native-command exit guard reuse the retained three-way exit code.
$global:LASTEXITCODE = 0
