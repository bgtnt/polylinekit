param(
    [Parameter(Mandatory)][string[]]$Reports,
    [Parameter(Mandatory)][string]$CheckReport,
    [Parameter(Mandatory)][string]$PackageProof,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
if ($Reports.Count -ne 3) { throw 'Supply exactly three independent process reports.' }
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh summary output path.' }

function Finite([object]$value, [string]$description) {
    if ($null -eq $value) { throw "Missing $description" }
    $number = [double]$value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { throw "Nonfinite $description" }
    return $number
}
function Median([object[]]$values) {
    $ordered = @($values | Sort-Object)
    if ($ordered.Count % 2) { return [double]$ordered[[int][Math]::Floor($ordered.Count / 2)] }
    return ([double]$ordered[$ordered.Count / 2 - 1] + [double]$ordered[$ordered.Count / 2]) / 2
}
function Key($row) { return "$($row.Direction)/$($row.Backend)/$($row.Scope)" }
function StableMetadata($metadata, [switch]$IgnoreTiering) {
    # Copy before removing installation paths. Binary identities stay mandatory.
    $copy = $metadata | ConvertTo-Json -Depth 80 | ConvertFrom-Json
    foreach ($assembly in $copy.Assemblies) { $assembly.Location = '' }
    if ($IgnoreTiering) { $copy.TieredCompilation = ''; $copy.TieredPGO = '' }
    return $copy | ConvertTo-Json -Depth 80 -Compress
}
function BinaryIdentity($metadata) {
    $assemblies = @($metadata.Assemblies | Sort-Object Assembly)
    $expectedNames = @('Clipper2Lib', 'NetTopologySuite', 'PolylineKit.IndependentRegions', 'PolylineKit.Winding')
    if ($assemblies.Count -ne 4 -or (Compare-Object $expectedNames @($assemblies.Assembly))) {
        throw 'Expected the runner and the three declared package assembly identities.'
    }
    foreach ($assembly in $assemblies) {
        if ($assembly.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or !$assembly.InformationalVersion -or !$assembly.TargetFramework) {
            throw 'Missing or invalid loaded assembly identity.'
        }
    }
    $core = @($assemblies | Where-Object { $_.Assembly -ceq 'PolylineKit.Winding' })[0]
    if ($core.Sha256 -cne 'bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca' -or
        $core.InformationalVersion -cne '0.1.0-alpha.2+5e9dcc1a5bcb34b5b2d8ae60105d328aec66a80c' -or
        $core.TargetFramework -cne '.NETCoreApp,Version=v10.0') {
        throw 'The checked Core identity differs from the published alpha.2 net10.0 payload.'
    }
    return @($assemblies | ForEach-Object {
        [ordered]@{ Assembly = $_.Assembly; Sha256 = $_.Sha256; InformationalVersion = $_.InformationalVersion; TargetFramework = $_.TargetFramework }
    }) | ConvertTo-Json -Depth 10 -Compress
}
function EvidenceFile([string]$Path) {
    return [ordered]@{ File = [IO.Path]::GetFileName($Path); Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}

# Gate before reading any timings: an analytic smoke does not replace full corpus validation.
$check = Get-Content -LiteralPath $CheckReport -Raw | ConvertFrom-Json
if ($check.SchemaVersion -ne 1 -or $check.Validation.Passed -ne $true -or
    $null -eq $check.Validation.FailureCount -or $check.Validation.FailureCount -ne 0) {
    throw 'The full corpus check failed or is incomplete; timing reduction is forbidden.'
}
$proof = Get-Content -LiteralPath $PackageProof -Raw | ConvertFrom-Json
if ($proof.Complete -ne $true -or $proof.PackageCacheInitiallyEmpty -ne $true -or $proof.PublicSourceOnly -ne $true -or
    $proof.Package -cne 'PolylineKit.Core/0.1.0-alpha.2' -or
    $proof.ExpectedSourceRevision -cne '5e9dcc1a5bcb34b5b2d8ae60105d328aec66a80c' -or
    $proof.RestoredCore.Type -cne 'package' -or $proof.RestoredCore.Version -cne '0.1.0-alpha.2' -or
    $proof.RestoredCore.Source -cne 'https://api.nuget.org/v3/index.json' -or
    $proof.RestoredCore.RuntimeAsset -cne 'lib/net10.0/PolylineKit.Winding.dll' -or
    $proof.RestoredCore.SHA256 -cne 'bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca' -or
    $proof.PackageCheck.SchemaVersion -ne 1 -or $proof.PackageCheck.Validation.Passed -ne $true) {
    throw 'The published-package isolation proof failed or is incomplete.'
}
$proofIdentity = BinaryIdentity $proof.PackageCheck.Metadata
$runnerIdentity = @($proof.PackageCheck.Metadata.Assemblies | Where-Object { $_.Assembly -ceq 'PolylineKit.IndependentRegions' })[0]
if ($proof.ConsumerSHA256 -cne $runnerIdentity.Sha256 -or
    $proof.PackageCheck.Metadata.AssessmentRevision -cnotmatch '^[0-9a-f]{40}$') {
    throw 'The package proof lacks the checked runner hash or assessment source revision.'
}
if ((BinaryIdentity $check.Metadata) -cne $proofIdentity -or
    $check.Metadata.AssessmentRevision -cne $proof.PackageCheck.Metadata.AssessmentRevision) {
    throw 'The full corpus check is not bound to the proven package consumer and assessment revision.'
}
$checkIdentity = StableMetadata $check.Metadata -IgnoreTiering

$runs = @($Reports | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json })
if (@($runs.Run | Sort-Object -Unique).Count -ne 3 -or
    (Compare-Object @(1, 2, 3) @($runs.Run | Sort-Object))) {
    throw 'Run indices must be exactly 1, 2 and 3.'
}
if (@($runs.ProcessId | Sort-Object -Unique).Count -ne 3) { throw 'Reports do not identify three independent processes.' }
$identity = StableMetadata $runs[0].Metadata
$keys = @($runs[0].Measurements | ForEach-Object { Key $_ } | Sort-Object)
if ($keys.Count -ne 18 -or @($keys | Sort-Object -Unique).Count -ne 18) { throw 'Expected 18 unique measurement cells.' }
$expectedKeys = @(foreach ($direction in @('zones-to-queries', 'queries-to-zones')) {
    foreach ($backend in @('Core-alpha.2', 'Clipper64-reused-data', 'NTS-OverlayNGRobust')) {
        foreach ($scope in @('prepare-only', 'warm-table', 'prepare-plus-one-table')) { "$direction/$backend/$scope" }
    }
})
$expectedKeys = @($expectedKeys | Sort-Object)
if (Compare-Object $expectedKeys $keys) { throw 'The required direction/backend/scope matrix differs.' }
foreach ($run in $runs) {
    if ($run.SchemaVersion -ne 1 -or (StableMetadata $run.Metadata) -cne $identity) {
        throw 'Report schema or frozen metadata differs between processes.'
    }
    if ($run.Validation.Passed -ne $true) { throw 'A measurement report did not pass its validation.' }
    if ((StableMetadata $run.Metadata -IgnoreTiering) -cne $checkIdentity -or
        (BinaryIdentity $run.Metadata) -cne $proofIdentity) {
        throw 'Measured binaries, source revision, inputs, protocol or environment differ from the full corpus check.'
    }
    if ($run.Metadata.TieredCompilation -cne '0' -or $run.Metadata.TieredPGO -cne '0') {
        throw 'Measured processes did not disable tiered compilation and tiered PGO.'
    }
    $actualKeys = @($run.Measurements | ForEach-Object { Key $_ } | Sort-Object)
    if ($actualKeys.Count -ne 18 -or (Compare-Object $keys $actualKeys)) { throw 'Measurement inventory differs.' }
    foreach ($row in $run.Measurements) {
        if ($row.Samples.Count -ne 5) { throw "Expected five samples: $(Key $row)" }
        foreach ($sample in $row.Samples) {
            $iterations = Finite $sample.Iterations 'iteration count'
            $time = Finite $sample.NanosecondsPerOperation 'sample time'
            $bytes = Finite $sample.BytesPerOperation 'sample allocation'
            if ($iterations -lt 1 -or $iterations -gt 16384 -or $iterations -ne [Math]::Floor($iterations) -or
                $time -le 0 -or $bytes -lt 0) { throw "Invalid sample: $(Key $row)" }
        }
        $timeMedian = Median @($row.Samples.NanosecondsPerOperation)
        $bytesMedian = Median @($row.Samples.BytesPerOperation)
        if ((Finite $row.MedianNanoseconds 'recorded median time') -ne $timeMedian -or
            (Finite $row.MedianBytes 'recorded median allocation') -ne $bytesMedian) {
            throw "Recorded median differs from the five preserved samples: $(Key $row)"
        }
        if ($null -eq $row.Digest) { throw 'Missing consumed output digest.' }
    }
}
$cells = @()
foreach ($key in $keys) {
    $rows = @($runs | ForEach-Object { $_.Measurements | Where-Object { (Key $_) -ceq $key } })
    $digests = @($rows | ForEach-Object { $_.Digest | ConvertTo-Json -Depth 10 -Compress } | Sort-Object -Unique)
    if ($digests.Count -ne 1) { throw "Backend output digest differs between processes: $key" }
    $times = @($rows.MedianNanoseconds)
    $bytes = @($rows.MedianBytes)
    $cells += [ordered]@{
        Direction = $rows[0].Direction; Backend = $rows[0].Backend; Scope = $rows[0].Scope
        MedianNanoseconds = Median $times
        MinProcessMedianNanoseconds = ($times | Measure-Object -Minimum).Minimum
        MaxProcessMedianNanoseconds = ($times | Measure-Object -Maximum).Maximum
        MedianBytes = Median $bytes
        ProcessMedianNanoseconds = $times; ProcessMedianBytes = $bytes
        Digest = $rows[0].Digest
    }
}
$evidence = @($Reports | ForEach-Object { EvidenceFile $_ })
$summary = [ordered]@{
    SchemaVersion = 1
    Meaning = 'Median and range of three process medians; all 270 samples retained in the identified reports. No confidence intervals or universal ranking.'
    Metadata = $runs[0].Metadata
    Binding = [ordered]@{
        FullCorpusCheck = EvidenceFile $CheckReport
        PackageIsolationProof = EvidenceFile $PackageProof
        AssessmentRevision = $check.Metadata.AssessmentRevision
        ConsumerSHA256 = $proof.ConsumerSHA256
        Meaning = 'Successful full corpus validation, actual published-package isolation and all three timing reports share the recorded binaries, inputs and protocol. Only tiering settings may differ between checking and measurement.'
    }
    Evidence = $evidence
    Validation = @($runs | ForEach-Object { [ordered]@{ Run = $_.Run; Result = $_.Validation } })
    Measurements = $cells
}
$destination = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
$summary | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Output "PASS: 18 cells, three distinct processes, 270 validated samples. $destination"
