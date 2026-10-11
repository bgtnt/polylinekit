param(
    [Parameter(Mandatory)][string[]]$Reports,
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
function StableMetadata($metadata) {
    # Copy before removing installation paths. Binary identities stay mandatory.
    $copy = $metadata | ConvertTo-Json -Depth 80 | ConvertFrom-Json
    foreach ($assembly in $copy.Assemblies) { $assembly.Location = '' }
    return $copy | ConvertTo-Json -Depth 80 -Compress
}

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
$evidence = @($Reports | ForEach-Object {
    [ordered]@{ File = [IO.Path]::GetFileName($_); Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$summary = [ordered]@{
    SchemaVersion = 1
    Meaning = 'Median and range of three process medians; all 270 samples retained in the identified reports. No confidence intervals or universal ranking.'
    Metadata = $runs[0].Metadata
    Evidence = $evidence
    Validation = @($runs | ForEach-Object { [ordered]@{ Run = $_.Run; Result = $_.Validation } })
    Measurements = $cells
}
$destination = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
$summary | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Output "PASS: 18 cells, three distinct processes, 270 validated samples. $destination"
