# Install verified packages and prepare a release

## Install alpha.2 from GitHub

`0.1.0-alpha.2` is available in the
[GitHub prerelease](https://github.com/bgtnt/polylinekit/releases/tag/v0.1.0-alpha.2).
NuGet.org publication is pending; alpha.1 remains the latest NuGet version.
See the [alpha.2 release notes](releases/0.1.0-alpha.2.md) for its additive
polygon-role factories and tested NTS conversion example.

Download the original
[Core package](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.2/PolylineKit.Core.0.1.0-alpha.2.nupkg)
into a local directory such as `./packages`, then install it:

```powershell
dotnet add MyApp/MyApp.csproj package PolylineKit.Core --version 0.1.0-alpha.2 --source ./packages
```

For the optional adapter, also download its
[matching package](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.2/PolylineKit.Clipper.0.1.0-alpha.2.nupkg)
and permit restoring Clipper2 2.0.0 from NuGet.org. See the
[local-feed source mapping](#install-from-the-local-feed), using alpha.2 and your
download directory. Core itself has no third-party runtime dependencies.

The original pair is pinned to `5e9dcc1a5bcb34b5b2d8ae60105d328aec66a80c`.
Anonymous downloads of both packages, `verification.json` and `SHA256SUMS`
matched the verified originals. No packages were rebuilt for upload.

## Verify a release candidate

After committing source, verify a new package pair and four external consumers.
The following commands describe the alpha.2 checks; rebuilding does not replace
the original release assets:

```powershell
pwsh -File scripts/check-package-readme.ps1
pwsh -File scripts/verify-packages.ps1 -Version 0.1.0-alpha.2 -OutputDirectory artifacts/alpha2-packages
```

`-Offline` restores outside consumers from the produced package feed and the
already cached Clipper2 2.0.0 archive, without a remote package source. It requires
the .NET 8/10 targeting packs and runtimes to be installed. Source projects still
use locked restore; no dependency versions are changed. `-KeepWorkspace` preserves
the outside applications for inspection.

Package inspection reads the actual archived README and nuspec. It rejects
release-process wording, complete version literals differing from the package's
version, missing description/tags/license/README metadata and missing polygon,
IoU or overlap tags. `check-package-readme.ps1` exercises rejection controls;
external applications exercise the new factories from both package targets.

Pin the clean verification's source, package hashes and record hash in
`publish-nuget.yml`. Do not substitute a rebuild after verification. Creating a
new tag, creating its GitHub prerelease and dispatching publication each require
the owner's separate instruction. Alpha.2 tagging and GitHub delivery are complete;
NuGet publication remains pending. The workflow remains manual and uses the
current confirmed NuGet username until support completes any rename.

## Install the published alpha.1

`0.1.0-alpha.1` is available on NuGet.org:
[Core](https://www.nuget.org/packages/PolylineKit.Core/0.1.0-alpha.1) and
[optional Clipper adapter](https://www.nuget.org/packages/PolylineKit.Clipper/0.1.0-alpha.1).
The [GitHub prerelease](https://github.com/bgtnt/polylinekit/releases/tag/v0.1.0-alpha.1)
preserves the original packages from source
`a07ac5528da4cd0fc00c29d9bff01fae4d76c031`. Package and assembly names differ
intentionally; see the [release notes](releases/0.1.0-alpha.1.md).

## Install from NuGet.org

A source build is not required:

```powershell
dotnet add MyApp/MyApp.csproj package PolylineKit.Core --version 0.1.0-alpha.1 --source https://api.nuget.org/v3/index.json
```

Core needs no third-party runtime package. If you need resolved contours, add
the adapter instead; NuGet also restores Core and Clipper2:

```powershell
dotnet add MyApp/MyApp.csproj package PolylineKit.Clipper --version 0.1.0-alpha.1 --source https://api.nuget.org/v3/index.json
```

Alternatively, download the original
[Core](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.1/PolylineKit.Core.0.1.0-alpha.1.nupkg)
and [adapter](https://github.com/bgtnt/polylinekit/releases/download/v0.1.0-alpha.1/PolylineKit.Clipper.0.1.0-alpha.1.nupkg)
files to a local feed and follow the [local-feed instructions](#install-from-the-local-feed).
The release's `SHA256SUMS` and `verification.json` identify these original files.
NuGet.org adds a repository signature, so its outer ZIP hashes can differ; the
original package entries and assemblies are unchanged. The embedded READMEs
retain the package-source snapshot and can describe the earlier GitHub-only
delivery; this guide gives the current installation instructions.

If you previously restored an older locally built package with this same version,
NuGet may reuse it from the global cache despite the changed source. Set `NUGET_PACKAGES`
to a fresh cache directory before adding/restoring the release packages:

```powershell
$env:NUGET_PACKAGES = '/absolute/path/to/fresh/package-cache'
```

The shipped pair includes the multi-ring API; do not substitute an earlier
local candidate. The GitHub originals were verified without authentication,
including all hashes and the tag/source identity. The four external consumers cover
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

Both `0.1.0-alpha.1` packages were published on NuGet.org on 2026-10-10 through
GitHub Trusted Publishing, using the verified GitHub files without rebuilding.
This version is immutable: do not repeat `publish=true` or push it again.
For a new release, recheck package ownership and version availability before
pushing, and update the workflow's version, source and pinned hashes together.

### Publish through GitHub Trusted Publishing

The manual [publish-nuget.yml](../.github/workflows/publish-nuget.yml) workflow
downloads the delivered `v0.1.0-alpha.1` package pair and verification record,
checks their pinned SHA256 values and clean source identity, then uses
[NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
to obtain a temporary credential. It does not rebuild the packages. No permanent
API key or GitHub secret is needed. The workflow runs only on this repository's
`main` branch and serializes publication attempts.

The configured policy in the `wowbagger` NuGet account uses these fields:

| Field | Value |
| --- | --- |
| Policy Name | `PolylineKit publishing` |
| Package Owner | `wowbagger` |
| CI/CD Provider | `GitHub Actions` |
| Repository Owner | `bgtnt` |
| Repository | `polylinekit` |
| Workflow File | `publish-nuget.yml` (file name only) |
| Environment | Leave empty; the workflow does not use a GitHub environment |
| Scope | Push new packages and package versions |
| Glob Patterns and Packages | `PolylineKit.*` |

Save the policy with **Create**. An empty policy list means none has been created;
this process does not issue a key for you to copy. The workflow's login username
is the NuGet profile name `wowbagger`, distinct from the GitHub owner `bgtnt`.

From GitHub **Actions**, select **Publish verified alpha to NuGet.org**, then
**Run workflow** on `main`. Leave `publish` false to verify public downloads
without requesting a credential or pushing. For a new version, update the pinned
release inputs first; set `publish` true only for its authorized publication after
the policy is active. Publishing is not triggered by a commit,
pull request or tag.

After a successful push, wait for NuGet validation and indexing, verify both
public package identities and assembly contents, and check an external consumer
before reporting the NuGet release as delivered. NuGet.org repository-signs
packages, so the served ZIP checksum can differ from the uploaded file; compare
contents while accounting for the added signature. A partial upload or an
existing version is a failure requiring inspection, not a silent duplicate skip.
For future releases, update the version, source and pinned download hashes together.

### Publish locally with an API key

For a future authorized version, use its verified files as-is; do not replace
them with a later rebuild. Download its package pair and `verification.json`
to the candidate directory. This does not republish the existing alpha.
In PowerShell, from the repository root:

```powershell
$candidate = 'artifacts/<new-version>'
$record = Get-Content "$candidate/verification.json" -Raw | ConvertFrom-Json
if ($record.DirtyWorkingTree) { throw 'Release packages require a clean source record.' }
foreach ($package in $record.Packages) {
    $file = Join-Path $candidate $package.File
    if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.SHA256) {
        throw "Package checksum changed: $file"
    }
}
# Set NUGET_API_KEY privately in this shell; never put the key in source or logs.
foreach ($package in $record.Packages) {
    dotnet nuget push (Join-Path $candidate $package.File) --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
    if ($LASTEXITCODE) { throw "Publication failed: $($package.File)" }
}
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
