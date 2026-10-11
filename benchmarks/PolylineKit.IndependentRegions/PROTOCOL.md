# Independent Helsinki region assessment

Declared before geometric comparison or timing, 11 October 2026. Success is a
reproducible assessment, including disagreements or slower execution. No speed
threshold, outcome-based input selection or geometry-engine change is allowed.

## Frozen population and meaning

Use the complete official Helsinki WFS snapshots of `Piirijako_peruspiiri`
(34 district features) and `VISTRA_alue` (1,095 planning-area features), requested
in EPSG:3067, in metres. The source freezer inventories and hashes the complete
responses and their metadata in [data/manifest.json](data/manifest.json).
It retains source Polygon/MultiPolygon hierarchy, every component and hole,
coordinate values, order and repeated closing vertices. Deterministic feature
order is by source identifier. This corpus was not used to develop the library.

The VISTRA layer contains several planning categories and potentially overlapping
features. The operation is a **pairwise area and coverage calculation**, not a
sum of disjoint parks, an estimate of current vegetation, or a policy assessment.
The two layers come from the same provider and are not statistically independent
observations. Independence here means a new input corpus for this implementation.

Do not translate, project locally, simplify, resample, repair, dissolve, align or
pre-orient the shared inputs. Report null, non-polygon and invalid geometries with
their source IDs and reasons. Invalid polygon topology is outside the common NTS
overlay contract and may be excluded only from the three-way comparable
population, with the exclusions retained. Numerical disagreement is a result or
failure, never a reason to remove an otherwise valid feature. Keep source and
admitted counts distinct. Never replace a Core/Clipper failure with NTS output.

## Published package and preparation

The primary Core backend is the actual `PolylineKit.Core` **0.1.0-alpha.2**
NuGet.org package, with no source ProjectReference, manual Core DLL reference or
linked implementation files. The net10.0 payload's recorded SHA256 is
`bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca`.
Use an isolated ordinary package consumer copied outside the repository, an
initially empty package cache and a NuGet config containing only nuget.org.
Verify the package graph, selected runtime asset, loaded binary identity and
source version. This is an independent package integration test, **not evidence
of adoption by an external developer**.

Validation-only dependencies are Clipper2 C# **2.0.0** and NetTopologySuite
**2.6.0**. None becomes a dependency of Core. Pin all package versions and lock
the restore graph. Record assembly identities and hashes in every report.

- Core: construct each feature through `PreparedRegion.FromPolygon` or
  `FromPolygons`, directly from its supplied shell/hole roles. Calculate and cache
  own area through `RegionArea.FilledArea`. Factory orientation cost is included.
- Clipper: build and role-orient its own coordinate copies. Quantize at a fixed
  **1e-6 metre** grid, using midpoint rounding away from zero. Require finite
  scaled magnitudes at most 2^52. Record maximum coordinate rounding and check
  quantized validity and bounding-box membership; do not silently repair or omit
  a quantization problem. Prepare reusable Clipper64 inputs and own-area sums.
  Per-query clipping and result-contour construction remain charged to queries.
- NTS: construct original Polygon/MultiPolygon objects by explicit hierarchy,
  cache bounds and own areas, and use `OverlayNGRobust` for independent operations.
  No geometry repair, prepared-predicate filter or Core/Clipper fallback.

All adapters retain their preparation objects, bounds and own-area denominators;
none caches pairwise results. Source loading, parsing and validity checking are
common setup outside timings. Backend-specific copying, orientation, reusable
inputs, own areas and storage construction belong to preparation.

## Geometric checks

For every valid feature, compare own areas under NonZero and EvenOdd. For every
admitted cross-layer pair, check selected intersection in both directions and
both fill rules, including disjoint bounds. Also check full Core comparison for
every original-bounds candidate and the first 64 disjoint pairs in lexicographic
source-ID order. This sample of disjoint full comparisons is selected before
outputs; the cheaper selected intersection covers every pair.

For those full comparisons, independently execute NTS and Clipper intersection,
union and symmetric difference; do not derive every reference from one
intersection result. Check each operand's area, intersection, union, XOR and
IoU. Valid source topology makes the two fill rules equivalent; this does not
test arbitrary self-crossing winding semantics.

Fixed acceptance budgets are **1 square metre** for areas and **1e-8** for
dimensionless fractions. Record maximum absolute differences, actual/reference
values and worst source IDs separately for each metric and comparator. Enforce
finite values and the API's defined/undefined result contract; report every
failure. NTS and Clipper are floating-point/grid references, not exact oracles.
Record original and quantized bounds membership and zero/positive intersections.
The public result explicitly carries unclamped floating-point rounding. Record
negative values and fractions above one without changing them. Domain excursions
fail when they exceed the same predeclared area/fraction budgets; exact domain
membership is not asserted for rounded numbers. Own-area denominators must be
strictly positive. This policy is fixed before the first corpus check.

## Measurements

Measure only after correctness checks, in a Release .NET 10 build. Three
sequential independent processes use identical binaries, inputs and protocol.
Set `DOTNET_TieredCompilation=0` and `DOTNET_TieredPGO=0`; preserve and restore
the caller's settings. Rotate backend order by process index. Stop other heavy
benchmarks or builds during measured processes. Record runtime, OS, CPU,
protocol/source hashes, package and assembly identities and process windows.

Both traversal directions include every admitted pair, with identical original
bounds rejection and no outer spatial index. Each query consumes intersection
and the covered fraction of its first feature. No result matrix is allocated in
timings. Report three scopes for each backend and direction:

1. `prepare-only`: build both complete catalogues and consume own areas.
2. `warm-table`: reuse catalogues and warmed query workspaces for one full table.
3. `prepare-plus-one-table`: fresh catalogue preparation plus one full table.

These are warm process/JIT measurements, not startup latency. For every cell,
warm and calibrate to at least 10 ms, capped at 16,384 iterations; record five
batches. Force GC outside time/allocation counters. Read both counters before
creating the sample record. Compare every returned digest without short-circuiting
later checks, then reject unstable results. Allocation counts are managed bytes
on the measuring thread, excluding initial/retained workspace and native memory.

The nominal matrix is 2 directions × 3 backends × 3 scopes × 5 samples × 3
processes = **270 samples**. Preserve every sample. Validate report identities
before summarizing, and report medians and minimum/maximum of the three process
medians. Retain unfavorable cells and outliers. There is no timing assertion in
CI and no claim that alpha.2 contains the later source preparation optimization.

## Delivery limits

Publish source/package identities, complete inventory and exclusions, maximum
observed discrepancies, all timing cells, environment, commands and evidence
hashes. Keep bulk local runs under ignored `artifacts/`; retain a compact public
summary and the frozen source needed to reproduce it. Do not change public APIs,
retune the engine or publish a new NuGet version as part of this assessment.
