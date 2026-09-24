param([string]$OutputDirectory = 'artifacts/benchmarks/winding-storage')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$oldTiered = $env:DOTNET_TieredCompilation
try {
    $changes = @(git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -or $changes.Count) { throw 'Storage profiling requires a clean Git tree; use artifacts/ for output.' }
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE) { throw 'Cannot determine revision.' }
    dotnet build benchmarks/PolylineKit.Benchmarks -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $env:DOTNET_TieredCompilation = '0'
    $runner = 'benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll'
    $cases = @('similar-strokes/16/bridged-nonzero', 'similar-strokes/1024/bridged-nonzero',
        'star-regions/1024/overlap-metrics', 'degenerate-grid/256/bridged-nonzero', 'simple-spiky-star/4096/closed-nonzero')
    foreach ($case in $cases) {
        foreach ($run in 1..3) {
            dotnet $runner profile-winding $OutputDirectory $case $run $revision
            if ($LASTEXITCODE) { throw "Profile failed: $case / $run" }
        }
    }
    $commonEnvironment = $null
    $summary = @(foreach ($case in $cases) {
        $rows = @(foreach ($run in 1..3) {
            Get-Content -LiteralPath (Join-Path $OutputDirectory ('profile-' + $case.Replace('/', '-') + "-$run.json")) -Raw | ConvertFrom-Json
        })
        for ($index = 0; $index -lt 3; $index++) {
            if ($rows[$index].Run -ne $index + 1 -or $rows[$index].Case -ne $case -or $rows[$index].Revision -ne $revision) {
                throw 'Profile run/case/revision does not match the requested measurement.'
            }
            if (($rows[$index].RetainedArrays.PayloadBytes | Measure-Object -Sum).Sum -ne $rows[$index].RetainedArrayPayloadBytes) {
                throw 'Retained array payload total differs.'
            }
        }
        foreach ($property in @('Case','Revision','Runtime','OS','Architecture','CPU','TieredCompilation','InputSha256','WindingSha256','HarnessSha256','RetainedArrayPayloadBytes','RetainedArrays','Value')) {
            $expected = $rows[0].$property | ConvertTo-Json -Depth 10 -Compress
            if (@($rows | Where-Object { ($_.$property | ConvertTo-Json -Depth 10 -Compress) -ne $expected }).Count) {
                throw "Inconsistent profile identity or capacity: $case / $property"
            }
        }
        foreach ($row in $rows) {
            if (@($row.WarmSamples).Count -ne 9 -or
                @($row.WarmSamples.Nanoseconds | Sort-Object)[4] -ne $row.WarmMedianNanoseconds -or
                @($row.WarmSamples.Bytes | Sort-Object)[4] -ne $row.WarmMedianBytes) { throw 'Invalid warm sample medians.' }
        }
        $environment = $rows[0] | Select-Object Revision,Runtime,OS,Architecture,CPU,TieredCompilation,WindingSha256,HarnessSha256 | ConvertTo-Json -Compress
        if ($null -eq $commonEnvironment) { $commonEnvironment = $environment }
        elseif ($commonEnvironment -ne $environment) { throw 'Environment or assembly changed between storage cases.' }
        [ordered]@{
            Case = $case; Revision = $revision
            FirstCallMedianMicroseconds = @($rows.FirstCallNanoseconds | Sort-Object)[1] / 1000
            FirstCallRangeMicroseconds = @((@($rows.FirstCallNanoseconds | Sort-Object)[0] / 1000), (@($rows.FirstCallNanoseconds | Sort-Object)[2] / 1000))
            FirstCallMedianAllocatedBytes = @($rows.FirstCallAllocatedBytes | Sort-Object)[1]
            RetainedManagedHeapDeltaRange = @(@($rows.RetainedManagedHeapDelta | Sort-Object)[0], @($rows.RetainedManagedHeapDelta | Sort-Object)[2])
            RetainedArrayPayloadBytes = $rows[0].RetainedArrayPayloadBytes
            WarmMedianMicroseconds = @($rows.WarmMedianNanoseconds | Sort-Object)[1] / 1000
            WarmMedianBytes = @($rows.WarmMedianBytes | Sort-Object)[1]
        }
    })
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
    Write-Output "Profiles and validated summary: $OutputDirectory"
} finally {
    $env:DOTNET_TieredCompilation = $oldTiered
    Pop-Location
}
