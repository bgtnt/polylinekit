[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$RunnerPath,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$PackageProof,
    [ValidateSet('alpha.2', 'alpha.3')][string]$Release = 'alpha.2'
)
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$proof = Get-Content -LiteralPath $PackageProof -Raw | ConvertFrom-Json
$unused = Join-Path ([IO.Path]::GetTempPath()) ('unused-corpus-contract-' + [Guid]::NewGuid().ToString('N') + '.json')
. (Join-Path $PSScriptRoot 'verify-corpus.ps1') -Release $Release -RunnerPath $RunnerPath -DataRoot $DataRoot -PackageProof $PackageProof -Output $unused
$tests = [Collections.Generic.List[string]]::new()
$actualExitCode = if ($source.Validation.Passed) { 0 } else { 1 }
$base = Assert-CorpusReport -Report $source -ActualExitCode $actualExitCode -Runner $RunnerPath -Data $DataRoot -Proof $proof
Require-Corpus ($base.CoreAgreementPassed -eq $true -and $base.ThreeWayPassed -eq $source.Validation.Passed -and
    $base.OriginalCheckExitCode -eq $actualExitCode -and $base.ThreeWayFailureCount -eq $source.Validation.FailureCount) 'The actual corpus report status was not retained.'
$tests.Add('Accept the actual complete Core/NTS result without changing its three-way status or failures')

function Reject-CorpusMutation([string]$Name, [scriptblock]$Change) {
    $report = $source | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $evidence = $proof | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $state = [pscustomobject]@{ Report = $report; Proof = $evidence; ExitCode = $actualExitCode }
    & $Change $state
    $rejected = $false
    try { $null = Assert-CorpusReport -Report $state.Report -ActualExitCode $state.ExitCode -Runner $RunnerPath -Data $DataRoot -Proof $state.Proof }
    catch { $rejected = $true }
    Require-Corpus $rejected ('Incorrectly accepted: ' + $Name)
    $tests.Add($Name)
}

Reject-CorpusMutation 'Reject a missing full-corpus validation' { param($s) $s.Report.Validation = $null }
Reject-CorpusMutation 'Reject the wrong report schema' { param($s) $s.Report.SchemaVersion = 2 }
Reject-CorpusMutation 'Reject an incomplete package isolation proof' { param($s) $s.Proof.Complete = $false }
Reject-CorpusMutation 'Reject a shared initial package cache' { param($s) $s.Proof.PackageCacheInitiallyEmpty = $false }
Reject-CorpusMutation 'Reject a different package source' { param($s) $s.Proof.RestoredCore.Source = 'https://example.invalid/index.json' }
Reject-CorpusMutation 'Reject an unproven assessment revision' { param($s) $s.Report.Metadata.AssessmentRevision = '0' * 40 }
Reject-CorpusMutation 'Reject a different Core binary' { param($s) ($s.Report.Metadata.Assemblies | Where-Object Assembly -CEQ 'PolylineKit.Winding').Sha256 = '0' * 64 }
Reject-CorpusMutation 'Reject a different comparator binary' { param($s) ($s.Report.Metadata.Assemblies | Where-Object Assembly -CEQ 'Clipper2Lib').Sha256 = '0' * 64 }
Reject-CorpusMutation 'Reject a different frozen manifest' { param($s) $s.Report.Metadata.ManifestSha256 = '0' * 64 }
Reject-CorpusMutation 'Reject a changed source snapshot hash' { param($s) $s.Report.Metadata.SourceFiles[0].Sha256 = '0' * 64 }
Reject-CorpusMutation 'Reject a missing derived input identity' { param($s) $s.Report.Metadata.DerivedFiles = @($s.Report.Metadata.DerivedFiles | Select-Object -Skip 1) }
Reject-CorpusMutation 'Reject a loosened fraction budget' { param($s) $s.Report.Metadata.FractionBudget = 1e-7 }
Reject-CorpusMutation 'Reject a changed original source population' { param($s) $s.Report.Validation.SourceQueries = 1094 }
Reject-CorpusMutation 'Reject a silently omitted eligible feature' { param($s) $s.Report.Validation.EligibleQueries = 1063 }
Reject-CorpusMutation 'Reject a silently omitted eligible ring' { param($s) $s.Report.Metadata.Queries.EligibleRings-- }
Reject-CorpusMutation 'Reject inconsistent invalid-input inventories' { param($s) $s.Report.Metadata.InputIssues = @($s.Report.Metadata.InputIssues | Select-Object -Skip 1) }
Reject-CorpusMutation 'Reject a missing original-invalid observation' { param($s) $s.Report.Validation.OriginalInvalidCoreObservations = @($s.Report.Validation.OriginalInvalidCoreObservations | Select-Object -Skip 1) }
Reject-CorpusMutation 'Reject a nonfinite original-invalid Core observation' { param($s) $s.Report.Validation.OriginalInvalidCoreObservations[0].AreaM2 = [double]::NaN }
Reject-CorpusMutation 'Reject missing actual selected API calls' { param($s) $s.Report.Validation.CoreSelectedApiCalls = 0 }
Reject-CorpusMutation 'Reject an incomplete full-comparison population' { param($s) $s.Report.Validation.FullCompareCalls-- }
Reject-CorpusMutation 'Reject an omitted metric summary' { param($s) $s.Report.Validation.Errors = @($s.Report.Validation.Errors | Select-Object -Skip 1) }
Reject-CorpusMutation 'Reject nonfinite Core results' { param($s) ($s.Report.Validation.Errors | Where-Object Backend -CEQ $coreBackend | Select-Object -First 1).NonfiniteChecks = 1 }
Reject-CorpusMutation 'Reject Core error exceeding its area budget' { param($s) ($s.Report.Validation.Errors | Where-Object { $_.Backend -ceq $coreBackend -and $_.Metric -ceq 'OwnAreaM2' } | Select-Object -First 1).MaximumAbsoluteError = 2 }
Reject-CorpusMutation 'Reject NTS error exceeding its fraction budget' { param($s) ($s.Report.Validation.Errors | Where-Object { $_.Backend -ceq 'NTS-OverlayNGRobust' -and $_.Metric -ceq 'Coverage' } | Select-Object -First 1).MaximumAbsoluteError = 1e-7 }
Reject-CorpusMutation 'Reject an unreviewed Clipper coverage extremum' {
    param($s)
    $row = $s.Report.Validation.Errors | Where-Object { $_.Backend -ceq 'Clipper64-reused-data' -and $_.Metric -ceq 'Coverage' -and $_.Direction -ceq 'queries-to-zones' } | Select-Object -First 1
    $row.MaximumAbsoluteError = 1e-7
    $row.WorstInput = '112827/999999'
}
Reject-CorpusMutation 'Reject a Clipper coverage domain failure' {
    param($s)
    ($s.Report.Validation.Errors | Where-Object { $_.Backend -ceq 'Clipper64-reused-data' -and $_.Metric -ceq 'Coverage' -and $_.Direction -ceq 'queries-to-zones' } | Select-Object -First 1).DomainBudgetViolations = 1
}
Reject-CorpusMutation 'Reject a Core failure hidden among comparator limits' {
    param($s)
    $s.Report.Validation.Failures += [pscustomobject]@{ Stage = "$coreBackend/NonZero/queries-to-zones/Coverage"; Input = '112827/606'; Reason = 'Injected Core error' }
    $s.Report.Validation.FailureCount++
}
Reject-CorpusMutation 'Reject an unexpected Clipper metric failure' {
    param($s)
    $s.Report.Validation.Failures += [pscustomobject]@{ Stage = 'Clipper64-reused-data/NonZero/queries-to-zones/UnionM2'; Input = '112827/606'; Reason = 'Injected unexpected comparator error' }
    $s.Report.Validation.FailureCount++
}
Reject-CorpusMutation 'Reject an unreviewed Clipper coverage input' {
    param($s)
    $s.Report.Validation.Failures += [pscustomobject]@{ Stage = 'Clipper64-reused-data/NonZero/queries-to-zones/Coverage'; Input = '112827/999999'; Reason = 'Injected new input'; Actual = '1.1'; Reference = '1' }
    $s.Report.Validation.FailureCount++
}
Reject-CorpusMutation 'Reject omitted original failure details' { param($s) $s.Report.Validation.OmittedFailureDetails = 1 }
Reject-CorpusMutation 'Reject relabeling a failed three-way gate as passed' { param($s) $s.Report.Validation.Passed = !$s.Report.Validation.Passed }
Reject-CorpusMutation 'Reject an exit code inconsistent with the original gate' { param($s) $s.ExitCode = 1 - $s.ExitCode }
Reject-CorpusMutation 'Reject a different published release' { param($s) $s.Proof.RestoredCore.Version = '0.1.0-alpha.999' }
if ($null -ne $source.Metadata.ReleaseProfile) {
    Reject-CorpusMutation 'Reject a relabeled release profile' { param($s) $s.Report.Metadata.ReleaseProfile = 'alpha.999' }
    Reject-CorpusMutation 'Reject mismatched clean/development runner provenance' { param($s) $s.Report.Metadata.AssessmentSourceState = 'unproven' }
}

Write-Output "PASS: $($tests.Count) corpus-wrapper evidence contracts; no geometry calculation or timing was run."
$tests | ForEach-Object { Write-Output "  $_" }
