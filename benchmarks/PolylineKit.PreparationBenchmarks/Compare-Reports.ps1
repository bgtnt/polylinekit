param(
    [Parameter(Mandatory = $true)][string[]]$BaselineReports,
    [Parameter(Mandatory = $true)][string[]]$CandidateReports,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Median([double[]]$Values) {
    $ordered = @($Values | Sort-Object)
    return $ordered[[int][Math]::Floor($ordered.Count / 2)]
}
function RowKey($Row) { return $Row.Input + '|' + $Row.Scope + '|' + $Row.Factory }
function ReadSet([string[]]$Paths, [string]$Name) {
    Require ($Paths.Count -eq 3) "$Name must contain three independent process reports."
    $reports = @($Paths | ForEach-Object { Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json })
    Require ((@($reports.Run | Sort-Object) -join ',') -ceq '1,2,3') "$Name run numbers must be 1, 2, 3."
    Require (@($reports.ProcessId | Sort-Object -Unique).Count -eq 3) "$Name must have three distinct process IDs."
    $reference = $reports[0]
    foreach ($report in $reports) {
        Require ($report.SchemaVersion -eq 1) 'Unsupported report schema.'
        Require ($report.Library.Sha256 -ceq $reference.Library.Sha256) "$Name mixes library DLLs."
        Require ($report.Harness.Sha256 -ceq $reference.Harness.Sha256) "$Name mixes benchmark executables."
        Require ($report.InputHash -ceq $reference.InputHash) "$Name mixes coordinate inputs."
        Require ($report.Check.TimedCellsChecked -eq $report.Rows.Count) "$Name has unchecked timed cells."
        Require ($report.Check.GisFeatures -eq 114 -and $report.Check.GisPairs -eq 1400 -and
            $report.Check.BoundsCandidates -eq 285) "$Name changed the complete Census inventory."
        $keys = @($report.Rows | ForEach-Object { RowKey $_ })
        Require (@($keys | Sort-Object -Unique).Count -eq $keys.Count) "$Name contains duplicate cells."
        Require (($keys -join ';') -ceq (@($reference.Rows | ForEach-Object { RowKey $_ }) -join ';')) "$Name changed row order or cells."
    }
    return ,$reports
}

$baseline = ReadSet $BaselineReports 'Baseline'
$candidate = ReadSet $CandidateReports 'Candidate'
$first = $baseline[0]
$all = @($baseline) + @($candidate)
foreach ($report in $all) {
    Require ($report.Harness.Sha256 -ceq $first.Harness.Sha256) 'Baseline and candidate must use the identical benchmark executable.'
    Require ($report.InputHash -ceq $first.InputHash) 'Baseline and candidate input hashes differ.'
    foreach ($property in @('Runtime', 'OS', 'Architecture', 'Processors', 'Cpu', 'Frequency',
        'TieredCompilation', 'TieredPGO', 'HardwareIntrinsics', 'FillRule',
        'TargetMillisecondsPerSample', 'SamplesPerCell', 'MaximumIterations', 'WarmupInvocations')) {
        Require ($report.$property -ceq $first.$property) "Benchmark environment/protocol differs: $property."
    }
    Require (($report.Sources | ConvertTo-Json -Depth 5 -Compress) -ceq
        ($first.Sources | ConvertTo-Json -Depth 5 -Compress)) 'Frozen source hashes differ.'
    Require ($report.Rows.Count -eq $first.Rows.Count) 'Baseline and candidate row counts differ.'
}

$rows = @()
for ($i = 0; $i -lt $first.Rows.Count; $i++) {
    $reference = $first.Rows[$i]
    foreach ($report in $all) {
        $row = $report.Rows[$i]
        Require ((RowKey $row) -ceq (RowKey $reference)) 'Baseline and candidate cells differ.'
        Require (($row.Value | ConvertTo-Json -Compress) -ceq ($reference.Value | ConvertTo-Json -Compress)) (
            'Output digest changed: ' + (RowKey $row))
        Require ($row.RegionsPrepared -eq $reference.RegionsPrepared -and $row.Queries -eq $reference.Queries -and
            $row.Vertices -eq $reference.Vertices -and $row.Rings -eq $reference.Rings) 'Per-operation scope changed.'
    }
    [double[]]$before = @($baseline | ForEach-Object { $_.Rows[$i].MedianNanoseconds })
    [double[]]$after = @($candidate | ForEach-Object { $_.Rows[$i].MedianNanoseconds })
    $beforeMedian = Median $before
    $afterMedian = Median $after
    Require ($beforeMedian -gt 0 -and $afterMedian -gt 0) 'Non-positive timing result.'
    $rows += [ordered]@{
        Input = $reference.Input; Scope = $reference.Scope; Factory = $reference.Factory
        Vertices = $reference.Vertices; Rings = $reference.Rings
        RegionsPrepared = $reference.RegionsPrepared; Queries = $reference.Queries; Value = $reference.Value
        BaselineNanoseconds = $beforeMedian; CandidateNanoseconds = $afterMedian
        BaselineMinNanoseconds = ($before | Measure-Object -Minimum).Minimum
        BaselineMaxNanoseconds = ($before | Measure-Object -Maximum).Maximum
        CandidateMinNanoseconds = ($after | Measure-Object -Minimum).Minimum
        CandidateMaxNanoseconds = ($after | Measure-Object -Maximum).Maximum
        BaselineOverCandidate = $beforeMedian / $afterMedian
        TimeReductionFraction = 1 - $afterMedian / $beforeMedian
        BaselineBytes = Median @($baseline | ForEach-Object { $_.Rows[$i].MedianBytes })
        CandidateBytes = Median @($candidate | ForEach-Object { $_.Rows[$i].MedianBytes })
    }
}
$report = [ordered]@{
    SchemaVersion = 1
    Description = 'Median of three independent process medians; identical harness, source inputs, scope and output digests. No timing acceptance threshold.'
    BaselineLibrarySha256 = $baseline[0].Library.Sha256
    BaselineInformationalVersion = $baseline[0].Library.InformationalVersion
    CandidateLibrarySha256 = $candidate[0].Library.Sha256
    CandidateInformationalVersion = $candidate[0].Library.InformationalVersion
    HarnessSha256 = $first.Harness.Sha256; InputHash = $first.InputHash; SourceFiles = $first.Sources
    Runtime = $first.Runtime; OS = $first.OS; Architecture = $first.Architecture; Cpu = $first.Cpu
    TieredCompilation = $first.TieredCompilation; TieredPGO = $first.TieredPGO
    TargetMillisecondsPerSample = $first.TargetMillisecondsPerSample; SamplesPerCell = $first.SamplesPerCell
    RunsPerLibrary = 3; Rows = $rows
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
Write-Output "PASS: $($rows.Count) matched cells; $resolvedOutput"
