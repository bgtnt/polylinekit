# Verify packages and prepare a release

The `0.1.0-alpha.1` candidate is local. Neither NuGet.org publication nor a
public GitHub release has been completed. Package and assembly names differ
intentionally; see the [release notes](releases/0.1.0-alpha.1.md).

## Produce the packages and verify real consumers

Use a committed checkout, PowerShell 7, the .NET 10 SDK and .NET 8/10 runtimes.
Installing both SDKs also supplies their targeting packs. From the repository
root, choose a new or empty output directory:

```powershell
pwsh -File scripts/verify-packages.ps1 -OutputDirectory artifacts/alpha-0.1.0-alpha.1
```

The script runs `dotnet restore --locked-mode` and Release `dotnet pack` for
both projects, then inspects the actual package files. It copies the coverage
example outside the repository and executes four package-only consumers:
Core and Clipper, each on .NET 10 and .NET 8. The latter selects the packages'
.NET Standard 2.0 assets. This is not a .NET Framework compatibility run.

Consumers use an isolated temporary package/HTTP cache and explicit
[package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping).
The verifier checks both compile and runtime assets in `project.assets.json`,
package checksums and local-feed provenance, assembly identities and target
attributes, XML docs, README, license, repository commit and dependency groups.
Core-only restore and execution must not bring in the adapter or Clipper2.
The coverage check also exercises normalization and a zero-area zone.

The output directory contains:

- `PolylineKit.Core.0.1.0-alpha.1.nupkg`
- `PolylineKit.Clipper.0.1.0-alpha.1.nupkg`
- `verification.json`, with source commit, package hashes and four consumer results
- per-consumer project/source, assets and execution records

Successful temporary workspaces are removed; the packages and evidence remain.
`-KeepWorkspace` retains the temporary workspace for inspection. `-AllowDirty`
permits explicitly marked development checks; it is **not** release verification.
The final package pair must have `DirtyWorkingTree: false`. CI uses the default
clean-checkout mode on Windows and Linux. It checks correctness, not timing
thresholds on shared runners.

## Install from the local feed

For a minimal Core application:

```powershell
dotnet new console -n CoverageDemo
dotnet add CoverageDemo/CoverageDemo.csproj package PolylineKit.Core --version 0.1.0-alpha.1 --source ./artifacts/alpha-0.1.0-alpha.1
```

Copy the [coverage program](../examples/PackageCoverage/Program.cs) into the
application, or follow the [runnable example instructions](../examples/PackageCoverage/README.md).
Core requires no third-party runtime package. For the optional adapter, the
feed must also permit restoring Clipper2 from NuGet.org. Place this `NuGet.Config`
next to your consumer, replacing the absolute local path:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="C:/absolute/path/to/artifacts/alpha-0.1.0-alpha.1" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="PolylineKit.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="Clipper2" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Then run from that consumer directory:

```powershell
dotnet add package PolylineKit.Clipper --version 0.1.0-alpha.1
```

Add explicit mappings for your application's other packages if needed. Source
mapping alone does not override an already-populated global package cache;
the verifier avoids that ambiguity with its fresh isolated cache and byte checks.

## Original measurement evidence

The maintainer-side bundler uses only a named allowlist of retained original
files, Git source snapshots and third-party license notices:

```powershell
python scripts/bundle-alpha-evidence.py --output artifacts/polylinekit-alpha-evidence.zip
```

This command requires the original local evidence and frozen binaries. It fails
if they are absent; a fresh clone cannot reconstruct those samples. The adjacent
`polylinekit-alpha-evidence.json` records the archive checksum and verification
status. Do not substitute fresh timings or samples inferred from reported medians.

After extracting the archive into a new directory, a reviewer can verify the
inventory with Python's standard library. Byte-identical report reproduction
additionally requires the .NET 10 runtime on Windows, matching the original
reports' line endings. No build, restore or download is involved:

```powershell
python verify-alpha-evidence.py .
python verify-alpha-evidence.py . --summarize
```

The first command verifies the inventory; the second re-summarizes copied raw
records with the frozen binaries and compares the reports with the originals.
It launches no timed workloads. The archive preserves the adverse real-contour
result as well as the passes. Read its provenance for the historical memory
probe's dirty build and redacted path fields. Embedded binary debug records can
contain original build paths; the bundle excludes private project files and PDBs.

## Publication after approval

Publication is a separate authorized step. The local preparation does not
reserve either package ID. Both NuGet flat-container lookups returned HTTP 404
on 2026-09-25; that does not establish ownership or guarantee later availability.
Recheck package ownership and version availability immediately before pushing.

Use the verified files as-is. In PowerShell, from the repository root:

```powershell
$candidate = 'artifacts/alpha-0.1.0-alpha.1'
$record = Get-Content "$candidate/verification.json" -Raw | ConvertFrom-Json
if ($record.DirtyWorkingTree) { throw 'Release packages require a clean source record.' }
foreach ($package in $record.Packages) {
    $file = Join-Path $candidate $package.File
    if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.SHA256) {
        throw "Package checksum changed: $file"
    }
}
# Set NUGET_API_KEY privately in this shell; never put the key in source or logs.
dotnet nuget push "$candidate/PolylineKit.Core.0.1.0-alpha.1.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
if ($LASTEXITCODE) { throw 'Core publication failed.' }
dotnet nuget push "$candidate/PolylineKit.Clipper.0.1.0-alpha.1.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
if ($LASTEXITCODE) { throw 'Adapter publication failed.' }
```

For an authorized GitHub prerelease, install/authenticate GitHub CLI and verify
the evidence archive's hash against its sidecar before uploading. The command
below tags the exact source recorded in the package verification, not an
unrelated later HEAD:

```powershell
gh release create v0.1.0-alpha.1 --repo bgtnt/polylinekit --target $record.SourceRevision --prerelease --title 'PolylineKit 0.1.0-alpha.1' --notes-file docs/releases/0.1.0-alpha.1.md "$candidate/PolylineKit.Core.0.1.0-alpha.1.nupkg" "$candidate/PolylineKit.Clipper.0.1.0-alpha.1.nupkg" "$candidate/verification.json" artifacts/polylinekit-alpha-evidence.zip artifacts/polylinekit-alpha-evidence.json
```

Before that command, update the release-note status to match the completed NuGet
publication. After upload, verify that each asset downloads with its recorded
hash, replace the explicitly pending raw-data references with the actual asset
URL, and update install instructions to use NuGet.org. The intended evidence
address is `/releases/download/v0.1.0-alpha.1/polylinekit-alpha-evidence.zip` on
this GitHub repository; it is **not a live evidence link before upload**.

Keep local per-consumer restore records out of release assets: they include
temporary machine paths. The compact `verification.json`, packages and curated
measurement archive are the intended deliverables.
