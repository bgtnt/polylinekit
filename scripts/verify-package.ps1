$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $revision = git rev-parse HEAD
    $feed = Join-Path (Get-Location) 'artifacts/packages'
    dotnet pack src/PolylineKit -c Release -o $feed -p:RepositoryCommit=$revision
    if ($LASTEXITCODE) { throw 'Pack failed.' }
    $package = Join-Path $feed 'PolylineKit.0.1.0-alpha.1.nupkg'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = $zip.GetEntry('PolylineKit.nuspec')
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.license.InnerText -ne 'MIT') { throw 'Missing MIT license metadata.' }
        if ($nuspec.package.metadata.repository.commit -ne $revision) { throw 'Missing repository commit.' }
        if (@($nuspec.GetElementsByTagName('dependency')).Count -ne 0) { throw 'Unexpected runtime package dependency.' }
        foreach ($name in @('README.md','lib/netstandard2.0/PolylineKit.dll','lib/netstandard2.0/PolylineKit.xml')) {
            if (-not $zip.GetEntry($name)) { throw "Missing package file: $name" }
        }
    } finally { $zip.Dispose() }
    $consumer = Join-Path (Get-Location) ('artifacts/consumer-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $consumer | Out-Null
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="PolylineKit" Version="0.1.0-alpha.1" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.csproj') -Encoding utf8
    @'
using PolylineKit;
Point2[] p = [new(0, 0), new(2, 0)];
Point2[] q = [new(0, 0), new(1, 1), new(2, 0)];
double result = PolylineArea.BetweenGraphs(p, q);
if (result != 1) throw new Exception($"Unexpected area: {result}");
Console.WriteLine($"Installed-package consumer passed: area={result}; framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
'@ | Set-Content -LiteralPath (Join-Path $consumer 'Program.cs') -Encoding utf8
    dotnet restore (Join-Path $consumer 'Consumer.csproj') --source $feed --packages (Join-Path $consumer 'isolated-cache')
    if ($LASTEXITCODE) { throw 'Local-feed-only restore failed.' }
    dotnet run --project $consumer -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Installed-package consumer failed.' }
    Write-Output ('Verified package: ' + $package)
    Get-FileHash -LiteralPath $package -Algorithm SHA256 | Select-Object Algorithm,Hash
} finally { Pop-Location }
