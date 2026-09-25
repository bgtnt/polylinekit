# Prepared region coverage

Given a zone A and a query region B, return intersection area `I = area(A ∩ B)`
and **zone coverage `I / area(A)`**. Coverage is directional and is not IoU
(`I / area(A ∪ B)`). A zero-area zone has undefined coverage, represented by
`null`. There is no alignment, normalization, recognition or matching here.

```csharp
double intersection = WindingArea.IntersectionArea(zone, query);
double? coverage = zoneArea > 0 ? intersection / zoneArea : null;
```

Cache `zoneArea` when preparing an immutable zone, using
`WindingArea.ClosedPath(zone).NonZero`. For this example's independently certified
simple rings, the adapters can use a cheaper translated shoelace area during
preparation. `IntersectionArea` accumulates only the requested intersection.
It still validates the inputs and computes their crossings per call; preparation
in this example does not cache Winding's internal edges or pair results.

The [intersection-only results](INTERSECTION-RESULTS.md) measure the dedicated
operation against unchanged full Winding and direct reusable Clipper. The
[earlier comparison](RESULTS.md) retains the full-metric and wider competitor
measurements. Both use three independent processes on the frozen population.

## Fixed workload

The [data protocol](data/PROTOCOL.md) specifies all North Carolina counties and
119th congressional districts in two Census generalized ACS2024 layers. The
snapshot contains all 100 counties and 14 districts. Eligibility requires a
complete single-ring polygon: 98 counties and 11 districts qualify. Multipart
polygons and polygons with holes remain in the original source snapshot and are
explicitly excluded; no islands are dropped or rings bridged.

The 10,588 admitted vertices are projected metres (EPSG:5070). The runner
subtracts one common combined-bounds center from all contours, checks simple
valid polygons with NTS, and aborts on validation failure. It performs all 1,078
pairs in **both directions**: prepared county zones with district queries, and
prepared district zones with county queries. Coverage denominators differ.
There are 211 bounding-box candidates, 867 rejects, 12 contained candidate
pairs, and **no convex-eligible candidate pairs** in either direction. The
convex adapter therefore measures its general fallback on this population;
its analytic controls exercise the specialized route separately.

These related administrative layers share some boundaries. They are not
independent land-cover observations or a representative sample of all GIS
geometry. The source is already generalized at 1:5M; measured planar areas are
properties of those polygons, not authoritative surveyed areas.

## Compared implementations

Clipper2 2.0.0 and NetTopologySuite 2.6.0 are dependencies of this experimental
executable only. `PolylineKit.Winding` gains no dependency. See the
[competitor assessment](COMPETITORS.md) for the scope of WPF, ArcGIS, QGIS,
GEOS and NTS Lab comparisons.

| Method | Prepared state and query operation |
|---|---|
| Winding | Immutable coordinate arrays and own areas; `FilledRegions` per candidate. |
| Winding-intersection-only | Identical preparation; `IntersectionArea` skips unused own/union/XOR area accumulation per candidate. |
| Clipper64-reused-data | Integer coordinates, local-minimum data and own areas; reusable engine executes one intersection and sums its output contours. |
| NTS-legacy | Polygon objects and own areas; explicitly selected legacy overlay. |
| NTS-OverlayNG | Same objects; robust OverlayNG intersection. |
| NTS-prepared-OverlayNG | Prepared zone predicates for containment/disjointness; OverlayNG for remaining intersections. Lazy indexes are built on first query. |
| convex-then-winding | Sutherland–Hodgman area accumulation if either input is certified convex; Winding fallback otherwise. |

Clipper uses scale `1e6`: nearest micrometre, ties away from zero. It receives
the same centred binary64 arrays and applies its documented integer-grid
contract; this is not exact arithmetic on the original doubles. Coordinates
must stay below `2^52` after scaling. Its reused-data route still clears and
adds both input containers on every query. It does not cache an intersection.

All methods cache their **own** measured zone areas. Every result is checked
against NTS legacy overlay: area disagreement must be at most **1 m²**, and
absolute coverage-fraction disagreement at most **1e-8**. This budget was fixed
before timings. NTS is an independent implementation, not an exact oracle.
An ordinary adapter failure aborts the experiment. Optional WPF failures remain
visible under its separate graphics-precision contract and cannot establish an
equal-quality speed advantage. No values are clamped or failed pairs removed.

## Run

From the repository root, with the .NET 10 SDK:

```sh
dotnet restore PolylineKit.slnx --locked-mode
dotnet build examples/RegionCoverage -c Release --no-restore
dotnet run --project examples/RegionCoverage -c Release --no-build -- check
dotnet run --project examples/RegionCoverage -c Release --no-build -- run artifacts/coverage-accuracy
python examples/RegionCoverage/data/freeze.py
```

`check` exercises independent analytic adapter controls, validates frozen input
hashes and polygons, and compares every pair. The Python command verifies the
raw source, selection protocol and derivation offline. It does not download
or refresh the population. CI runs the core and optional Windows controls,
without timing assertions.

For WPF, build the separate Windows-only project and use its DLL below; it
includes the same six baseline adapters plus three WPF operations. See its
[README](../../benchmarks/PolylineKit.WpfBenchmarks/README.md).

```powershell
dotnet restore benchmarks/PolylineKit.WpfBenchmarks --locked-mode
dotnet build benchmarks/PolylineKit.WpfBenchmarks -c Release --no-restore
$runner = 'benchmarks/PolylineKit.WpfBenchmarks/bin/Release/net10.0-windows/PolylineKit.WpfBenchmarks.dll'
$revision = git rev-parse HEAD
$env:DOTNET_TieredCompilation = '0'
foreach ($run in 1..3) {
    dotnet $runner benchmark artifacts/coverage $run $revision
    if ($LASTEXITCODE) { throw "Coverage benchmark failed: $run" }
}
dotnet $runner summarize artifacts/coverage
```

Run the processes sequentially with other CPU-heavy work stopped. The native
WPF binaries and managed assemblies are hashed, alongside input identity,
runtime, OS, CPU and source revision. Preserve the measured build to summarize:
the summarizer demands the same binary hashes, recomputes the numerical
results, verifies every scope and digest, and recalculates all medians from raw
samples. Rebuilding at a later revision can change assembly hashes.

## Timing contract

Each direction/method reports preparation, candidate geometry, linear and
indexed full-population traversal, and preparation plus one/eight traversals.
Each traversal consumes both intersection and coverage in an aggregate digest;
it does **not** allocate a matrix of output values. A row measures all 211
candidates, not one pair. Disjoint boxes short-circuit uniformly.

The outer query indexes cover query-region boxes: NTS STRtree (default fanout
10), NTS Hilbert-packed HPRtree (16), and the example's static packed bounds
index (32). The latter is independently written, informed by the author's
private RtTools design: contiguous pages, reusable query storage and immediate
emission of wholly covered subtrees. Private implementation code is neither
copied nor required. It uses X/Y-sliced leaf packing and consecutive upper-level
groups, not Hilbert ordering or dynamic insertion/deletion. See the source for
its bounded experimental contract; this is not a new public indexing library.

Each preparation/session builds **only its selected index**. Construction and
prepared input catalogues are charged to preparation and fresh sessions.
All three indexes are checked against exact linear candidate IDs, including
touching and zero-extent boxes. An outer region index does not change the
area engine's internal segment search. No fastest index is assumed in advance.
Only the candidate microbenchmark precomputes a pair list,
outside its timer. Geometry-specific internal indexes belong to their adapters.
No area results are cached between traversals. Eight-query-table sessions
amortize one preparation and report time/bytes divided by eight.

All measured processes run correctness checks first. JIT and Winding's
thread-local workspace are therefore warm even in fresh-session measurements;
these are **not cold-process costs**. The first traversal after new prepared
objects is recorded separately in raw JSON, including lazy NTS index creation.

Three fresh processes rotate adapter order. Each row has five calibrated
batches, timed with allocation counters read before sample-object creation.
Reports use the median of process medians and their range. Managed allocation
counts exclude retained memory, native WPF allocations and output matrices.
They must not be described as total-memory measurements. No statistical
significance or universal speed claim follows from this bounded experiment.

## A different workload: changing polylines

Successive simplification changes the indexed segments. A dynamic R-tree with
deletion/insertion, including the author's private RtTools implementation, is a
serious candidate there. The present static region-catalogue measurements do
not assess it. See the [dynamic-index distinction](COMPETITORS.md#dynamic-simplification)
before extrapolating these results to a simplifier.
