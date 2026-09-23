param([string]$Directory = 'results/benchmarks')
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$runs = @(1..3 | ForEach-Object { Get-Content -LiteralPath (Join-Path $Directory "run-$_.json") -Raw | ConvertFrom-Json })
if (@($runs.Revision | Select-Object -Unique).Count -ne 1) { throw 'Source revisions differ.' }
foreach ($run in $runs) { if ($run.Measurements.Count -ne 72) { throw 'Expected 12 fixtures x 6 methods per run.' } }
$rows = @($runs | ForEach-Object { $_.Measurements })
foreach ($fixture in ($rows | Group-Object Fixture)) {
    if (@($fixture.Group.InputSha256 | Select-Object -Unique).Count -ne 1) { throw 'Fixture hashes differ.' }
}
$methods = @('UnsignedGraphArea','LipGraphSweep','GenLipP0','ClipperFull','ClipperPrepared','ClipperPreparedTransposed')
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Three-process benchmark summary')
$lines.Add('')
$lines.Add('Time: median of the three process medians in microseconds, followed by their minimum–maximum. Each process median contains nine batch samples. Vertices are per path. All numbers describe this machine and fixture family only.')
$lines.Add('')
$lines.Add('Measured source: `' + $runs[0].Revision + '`.')
$lines.Add('')
$lines.Add('| Vertices | Density | Unsigned | LIP sweep | GenLIP p=0 | Clipper full | Clipper prepared | Clipper transposed |')
$lines.Add('|---:|:---|---:|---:|---:|---:|---:|---:|')
foreach ($n in @(16,64,256,1024)) {
    foreach ($density in @('none','sparse','dense')) {
        $cells = @($n,$density)
        foreach ($method in $methods) {
            $values = @($rows | Where-Object { $_.VerticesPerPath -eq $n -and $_.Density -eq $density -and $_.Method -eq $method } | ForEach-Object { $_.MedianNanoseconds / 1000 } | Sort-Object)
            $cells += $values[1].ToString('F2',$culture) + ' (' + $values[0].ToString('F2',$culture) + '–' + $values[2].ToString('F2',$culture) + ')'
        }
        $lines.Add('| ' + ($cells -join ' | ') + ' |')
    }
}
$lines.Add('')
$lines.Add('## Managed bytes per operation')
$lines.Add('')
$lines.Add('Allocations were identical across the three process medians for each fixture/method. This is per-thread managed allocation, not peak memory or native allocation.')
$lines.Add('')
$lines.Add('| Vertices | Density | Unsigned | LIP sweep | GenLIP p=0 | Clipper full | Clipper prepared | Clipper transposed |')
$lines.Add('|---:|:---|---:|---:|---:|---:|---:|---:|')
foreach ($n in @(16,64,256,1024)) {
    foreach ($density in @('none','sparse','dense')) {
        $cells = @($n,$density)
        foreach ($method in $methods) {
            $values = @($rows | Where-Object { $_.VerticesPerPath -eq $n -and $_.Density -eq $density -and $_.Method -eq $method } | ForEach-Object { $_.MedianBytes } | Select-Object -Unique)
            if ($values.Count -ne 1) { throw 'Allocation medians differ; do not hide the difference.' }
            $cells += $values[0].ToString('F0',$culture)
        }
        $lines.Add('| ' + ($cells -join ' | ') + ' |')
    }
}
Set-Content -LiteralPath (Join-Path $Directory 'summary.md') -Value $lines -Encoding utf8
Write-Output ('Validated and summarized ' + $rows.Count + ' measurements (' + ($rows.Count * 9) + ' batch samples).')
