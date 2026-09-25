param(
    [Parameter(Mandatory=$true)][string]$Revision,
    [Parameter(Mandatory=$true)][string]$Runner,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
if ($Revision -notmatch '^[0-9a-fA-F]{40}$') { throw 'Supply a full measured commit SHA.' }
$runnerPath = (Resolve-Path -LiteralPath $Runner).Path
$outputPath = [System.IO.Path]::GetFullPath($Output)
if (git status --porcelain) { throw 'Commit source changes before timing.' }
if ((git rev-parse HEAD) -ne $Revision) { throw 'Working source is not the requested revision.' }
$binary = [System.IO.File]::ReadAllBytes($runnerPath)
if (![System.Text.Encoding]::UTF8.GetString($binary).Contains("1.0.0+$Revision")) { throw 'Binary does not embed the requested source revision.' }
if (Test-Path -LiteralPath (Join-Path $outputPath 'run-1.json')) { throw 'Existing measurements will not be overwritten.' }
$hash = (Get-FileHash -LiteralPath $runnerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$oldTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    $launches = @()
    foreach ($run in 1..3) {
        $start = [DateTimeOffset]::UtcNow.ToString('o')
        dotnet $runnerPath run $outputPath $run $Revision | Tee-Object -FilePath (Join-Path $outputPath "run-$run-console.txt")
        $code = $LASTEXITCODE
        $launches += [pscustomobject]@{ Run=$run; Revision=$Revision; HarnessHash=$hash; StartedUtc=$start; FinishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); ExitCode=$code }
        $launches | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'process-order.json')
        if ($code) { throw "Benchmark process $run failed." }
        if ((Get-FileHash -LiteralPath $runnerPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Measured binary changed.' }
    }
    dotnet $runnerPath summarize $outputPath
    if ($LASTEXITCODE) { throw 'Evidence validation failed.' }
} finally { $env:DOTNET_TieredCompilation = $oldTiering }
