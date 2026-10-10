# Install verified packages and prepare a release

`0.1.0-alpha.1` is published as a [GitHub prerelease](https://github.com/bgtnt/polylinekit/releases/tag/v0.1.0-alpha.1),
with source `a07ac5528da4cd0fc00c29d9bff01fae4d76c031`. NuGet.org publication
has not been completed. Package and assembly names differ intentionally;
see the [release notes](releases/0.1.0-alpha.1.md).

## Download and install the delivered packages

Download [Core](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.1/PolylineKit.Core.0.1.0-alpha.1.nupkg)
to an empty local feed directory. If you need resolved contours, also download
the [optional adapter](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.1/PolylineKit.Clipper.0.1.0-alpha.1.nupkg).
The release's `SHA256SUMS` and `verification.json` identify the exact checked
files. A source build is not required to use them.

```powershell
dotnet add MyApp/MyApp.csproj package PolylineKit.Core --version 0.1.0-alpha.1 --source /absolute/path/to/feed
```

For the adapter, use the [two-source configuration below](#install-from-the-local-feed),
so Clipper2 can restore from NuGet.org. Core needs no third-party runtime package.

If you previously restored an older locally built package with this same version,
NuGet may reuse it from the global cache despite the new feed. Set `NUGET_PACKAGES`
to a fresh cache directory before adding/restoring the release packages:

```powershell
$env:NUGET_PACKAGES = '/absolute/path/to/fresh/package-cache'
```

The shipped pair includes the multi-ring API; do not substitute an earlier
local candidate. Public downloads were verified without authentication, including
all hashes and the tag/source identity. The four external consumers cover
.NET 8 and .NET 10; .NET 8 selects the .NET Standard 2.0 assets.

## Produce the packages and verify real consumers

This is a maintainer verification workflow, not a prerequisite for installation.
To rebuild the shipped source, use the `v0.1.0-alpha.1` tag. Fresh builds can have
different package bytes; the public release remains the originally verified pair.

Use a committed checkout, PowerShell 7, the .NET 10 SDK and .NET 8/10 runtimes.
Installing both SDKs also supplies their targeting packs. From the repository
root, choose a new or empty output directory:

```powershell
pwsh -File scripts/verify-packages.ps1 -OutputDirectory artifacts/alpha-0.1.0-alpha.1
```

The script runs `dotnet restore --locked-mode` and Release `dotnet pack` for
both projects, then inspects the actual package files. It copies the coverage
example outside the repository and executes four package-only consumers:
Core and Clipper, each on .NET 10 and .NET 8. All consumers exercise a prepared
shell, hole, island and empty region under both fill rules. The latter selects the packages'
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

Two evidence archives preserve different measurement series. The prepared-region
archive covers all 2,700 samples from the coordinated September 28 comparison;
the historical alpha archive below covers 2,940 earlier samples. Their frozen
binaries belong to the measured revisions, not the new release packages. Sidecar
JSON records describe archive generation; the GitHub release identifies delivery.

Public downloads:
[prepared-region evidence](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.1/polylinekit-prepared-region-evidence.zip)
and [historical alpha evidence](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.1/polylinekit-alpha-evidence.zip).

To curate the prepared-region series from the retained local originals:

```powershell
python scripts/bundle-prepared-region-evidence.py --output artifacts/polylinekit-prepared-region-evidence.zip
```

The script uses an explicit allowlist, checks the original hashes and verifies
the paired summary. Text metadata with personal build paths is redacted with
original and published hashes recorded in provenance. Raw timing JSON and frozen
DLLs are unchanged; the latter retain known public-repository CodeView build
paths. PDBs, executables, private files and unrelated artifacts are excluded.

After extracting this archive into a new directory, run its standalone verifier:

```powershell
python verify-prepared-region-evidence.py .
python verify-prepared-region-evidence.py . --summarize
```

Python's standard library verifies integrity and independently recomputes the
summary cells. `--summarize` additionally invokes the frozen .NET 10 summarizers
and requires byte-identical original summary JSON. It performs no timings,
restore, build or network requests. The manifest identifies the source/input
snapshots and explains metadata redactions. A fresh clone can verify a downloaded
archive; curating it requires the retained original evidence.

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

## NuGet publication and future releases

NuGet publication is still pending; the GitHub release does not reserve either
package ID. Both NuGet flat-container lookups returned HTTP 404
on 2026-10-10; that does not establish ownership or guarantee later availability.
Recheck package ownership and version availability immediately before pushing.

Use the already delivered, verified files as-is; do not replace them with a
later rebuild. Download the two packages and `verification.json` to the candidate
directory used below. In PowerShell, from the repository root:

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

For a future authorized GitHub prerelease, choose a new version/tag and target
the exact source recorded in the clean package verification. Upload the checked
packages, compact verification record, curated evidence archives and checksums
to a draft, verify its complete asset inventory, then publish. Preserve the
existing `v0.1.0-alpha.1` tag and assets.

For a future version, update release-note status to match the actual delivery
channel. After upload, verify each asset's public download hash and update install
instructions. Do not claim NuGet.org availability until publication succeeds.

Keep local per-consumer restore records out of release assets: they include
temporary machine paths. The compact `verification.json`, packages and curated
measurement archive are the intended deliverables.
