# Polygon factory preparation measurements

This small .NET 10 runner measures the construction cost of `PreparedRegion` and
construction followed by a fixed query. It uses **only public APIs** and an
explicit DLL reference. It does not rebuild the library, restore its package, or
take a source `ProjectReference`. The runner has no NuGet dependencies.

The purpose is to compare the already published `0.1.0-alpha.2` payload with a
candidate using identical inputs and executable code. It is not a timing test for
CI, a new polygon validity checker, or a comparison with another library.

## Inputs and scopes

- Synthetic cases have exactly 16, 64, 256 or 1024 total supplied vertices:
  an alternating-radius simple boundary, a circle visited in steps of five
  (self-intersecting), four separate simple components, and a shell with one hole.
  The fixed synthetic query translates the same geometry by `(3, 5)`.
- The complete frozen Census inputs have 100 counties and 14 districts, 123
  rings and one hole. Both original GeoJSON byte hashes are checked against fixed
  constants before use. No region is omitted. The source, licence and selection
  history are in [the existing input documentation](../../examples/RegionCoverage/data/README.md).
  This is the **existing** Census dataset, not independent new validation.
- The loader keeps the original double coordinates, removes the exact repeated
  closing vertex, and orients valid Census exteriors positively and holes
  negatively outside all measurements. Synthetic inputs already have these
  directions. This gives raw `FromRings` and the role-aware factories identical
  geometry. The benchmark does not measure loading or this common input step.
- Synthetic scopes are `prepare-only` and
  `prepare-one+compare-fixed-query`. The second constructs the first operand then
  calls `RegionArea.Compare` against an already prepared second operand.
- Census scopes are one preparation of all 114 features and preparation of both
  layers followed by all 1400 `RegionArea.IntersectionArea` calls. The latter is
  measured in both directions, with no external index or candidate filtering.
  Core's own bounds rejection remains part of the operation; 285 pairs have
  overlapping closed bounds.
- `PolygonFactories` means `FromPolygon` for a single component and
  `FromPolygons` for a disconnected feature. A separate `FromPolygons` row uses
  that factory for every feature.

All measurements use `NonZero`. The algebraic sign of a self-intersecting ring
does not establish valid polygon topology; that case tests construction cost and
an explicit fill rule only. Inputs, ring roles and coordinates are hashed in a
fixed binary format in every report.

## Build against the published DLL

Download and extract the published `PolylineKit.Core.0.1.0-alpha.2.nupkg`.
Its `lib/net10.0/PolylineKit.Winding.dll` has SHA256
`bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca`.
Use that exact DLL as `LibraryPath`; the build intentionally fails without an
existing explicit path.

From the repository root, PowerShell:

```powershell
New-Item -ItemType Directory -Force artifacts/preparation-published | Out-Null
Invoke-WebRequest https://api.nuget.org/v3-flatcontainer/polylinekit.core/0.1.0-alpha.2/polylinekit.core.0.1.0-alpha.2.nupkg -OutFile artifacts/preparation-published/package.zip
Expand-Archive artifacts/preparation-published/package.zip -DestinationPath artifacts/preparation-published/extracted -Force
$libraryPath = (Resolve-Path artifacts/preparation-published/extracted/lib/net10.0/PolylineKit.Winding.dll).Path
if ((Get-FileHash $libraryPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca') { throw 'Published DLL differs' }
dotnet build benchmarks/PolylineKit.PreparationBenchmarks/PolylineKit.PreparationBenchmarks.csproj -c Release -p:LibraryPath=$libraryPath -o artifacts/preparation-baseline
dotnet artifacts/preparation-baseline/PolylineKit.PreparationBenchmarks.dll check . artifacts/preparation-results/baseline-check.json
```

The `check` command verifies all factories against role-oriented `FromRings`,
including every Census pair, fixed analytic shell/hole orientations, vertex and
ring counts, and the **exact delegates subsequently timed**. Factory digests
must match exactly for those already oriented inputs. Full metric agreement is
also checked with the existing GIS budgets of 1 m² and `1e-8` for IoU. This
comparison isolates factory behavior; it is not an independent area oracle.

## Three independent process runs

Use Release, a quiet machine, and the same runtime/environment for both DLLs.
Disable tiering for this small focused experiment to avoid reporting the order
in which startup tier promotion happened as a factory improvement. The settings
are included in the JSON report.

```powershell
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_TieredPGO = '0'
1..3 | ForEach-Object {
    dotnet artifacts/preparation-baseline/PolylineKit.PreparationBenchmarks.dll run . "artifacts/preparation-results/baseline-run-$_.json" $_
}
```

For the candidate, build the same project to a separate directory with its DLL
as `LibraryPath`, run `check`, then launch three **new** `dotnet` processes with
the same settings. Reports record the actual runtime-loaded library SHA256,
informational version, runner SHA256, source hashes, OS, architecture and
processor information. Confirm the harness and input hashes match across the
two sets before comparing times. Check the metric digests as well as checks.

`Compare-Reports.ps1` verifies all six reports have the same harness, coordinate
hash, source hashes, environment, protocol, row scopes and output digests. It
rejects mixed DLLs inside either set and repeated process IDs. It then writes
the median of the three process medians, the process spread and allocations:

```powershell
$baselineReports = @(1..3 | ForEach-Object { "artifacts/preparation-results/baseline-run-$_.json" })
$candidateReports = @(1..3 | ForEach-Object { "artifacts/preparation-results/candidate-run-$_.json" })
./benchmarks/PolylineKit.PreparationBenchmarks/Compare-Reports.ps1 -BaselineReports $baselineReports -CandidateReports $candidateReports -OutputPath artifacts/preparation-results/comparison.json
```

The default protocol is four warmup calls, adaptive power-of-two calibration
with a 2048-iteration cap, five measured samples per cell and a 12 ms target per
sample. Every measured sample follows a full GC outside the counters. The CLI
accepts an optional target in milliseconds and sample count; use identical
values for both versions. One process usually takes approximately 15 seconds on
the development machine; this is not a runtime guarantee.

The bytes counter is read **before** constructing each `Sample` object.
Parsing, closures, input arrays, fixed synthetic query preparation and JSON
serialization are outside the counters. Prepared-region arrays are inside them.
The report distinguishes a batch/table operation from a single-region operation
using `RegionsPrepared` and `Queries`. Warm query workspace may remain retained;
0 B for a query is not zero total memory consumption.

There are no timing thresholds. Compare medians within each process, then the
three process medians, and inspect their spread. Small factory improvements may
be unimportant beside the downstream query; the preparation-plus-query rows are
included to expose that tradeoff.

The timed inputs are already role-oriented, so reversal cost is not measured.
The iteration cap can leave small cells below the nominal sample target. Tiny
or cancelling rings can take the legacy fallback after an additional direct
pass; these cases are covered by numerical checks but are not timed here.
