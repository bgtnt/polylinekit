# Multiple-ring feasibility and complete GIS checks

**Feasible: the internal boundary engine handles the complete frozen county and
district population, including the five formerly excluded features.** No new
public API is exposed. Applications must continue to follow the current
[single-walk contract](../../docs/area.md#input-contract).

The implementation and checks are in
[`e05d5bb`](https://github.com/bgtnt/polylinekit/commit/e05d5bb16359c524cfd555bdeec8351bd4d3ae04).
The recorded checks ran on 2026-09-28, Windows x64 / .NET 10.0.12, before that
commit was created; the checked source contents are the implementation in that
commit. Reports retain source hashes as well as the build's base revision.
This is a correctness and applicability assessment, **not a performance result**.

## Meaning of a multiple-ring operand

Each ring closes independently, without connector segments. Its signed winding
contributes to the operand's aggregate winding field. The fill rule is applied
to that field, separately for each operand:

- **NonZero:** any nonzero total winding fills a point. A shell and its hole
  need opposite orientations. Two overlapping same-direction shells form a union;
  opposite directions can cancel their overlap.
- **EvenOdd:** odd total winding fills a point, independently of ring orientation.
  Nested rings alternate filled/empty regions; overlapping shells can remove
  their common area. This is not unconditional union of individually filled rings.
- Ring order, starting vertex and reversal of all rings preserve the filled
  region. Reversing just one ring can change NonZero filling.
- An empty collection represents an empty region. A null collection or null
  ring is rejected; every supplied ring still needs three vertices after the
  existing duplicate-removal rules. Collinear zero-area rings are accepted.
  Invalid input in the other operand is validated even beside an empty operand.

The GIS adapter retains Polygon/MultiPolygon hierarchy for NTS and maps shell/hole
roles to oriented rings for Core and Clipper. It never infers a hole from list
position alone. All source shells were already counterclockwise and the hole was
clockwise, so orientation normalization required no reversals. No repair,
simplification or independent alignment is applied.

## Complete real population

All features are read from the original, hash-verified
[frozen Census snapshots](../../examples/RegionCoverage/data/README.md).
These are additional parts of a previously observed dataset, not a new independent
geographic sample. Coordinates are EPSG:5070 metres, translated by one common
origin across both layers.

| Layer | Features | Components | Rings | Holes | Vertices without closing duplicates |
|---|---:|---:|---:|---:|---:|
| Counties | 100 | 104 | 104 | 0 | 7,017 |
| Congressional districts | 14 | 18 | 19 | 1 | 5,165 |

All 114 geometries validate before and after translation. No feature is omitted.
The added features are Dare/Hyde counties (`37055`, `37095`) and districts
`3702`, `3703`, `3713`. Each direction has **1,400 pairs**, with 285 inclusive
bounds candidates and 1,115 disjoint bounds. There are 322 pairs involving a
formerly excluded feature and 74 additional candidates beyond the old workload.

District 2 has a small component whose ten-point boundary is exactly the reverse
of district 13's hole. Core measures **404.0480771618909 m²**, versus
**404.0480771618907 m²** in NTS; its intersection with district 13 is zero under
both fills. Keeping or deleting that ring materially changes the answer.

## Checks and numeric agreement

Each of three implementations passes **16,204 analytic assertions** and
**123,432 GIS numeric comparisons**: .NET 10, .NET 10 with SIMD disabled, and
the .NET Standard 2.0 assembly hosted on .NET 10. The latter validates the
portable assembly, not every runtime that can load it.

The analytic suite includes nested holes/islands, overlapping and coincident
rings, repeated/opposite traversal, shared edges, contacts, self-intersections,
subdivision, input rejection, reentrant/throwing indexers and numerical extremes.
An independent integer-cell oracle checks 160 deterministic rectangle-collection
pairs under both fills, with order, direction and starting-point variations.
Its winding/count decisions do not reuse the engine's predicates. The analytic
tolerance is `2e-12 * max(1, expected)`.

The GIS suite runs 5,600 full comparisons and 5,600 selected intersections
(1,400 pairs × two operand directions × two fills), plus 228 selected own areas.
It checks all five areas, IoU and both directional coverage fractions. No pair
is removed because of a result. All Core results use the multiple-ring boundary
engine, without an external geometry fallback.

NTS 2.6.0 uses `OverlayNGRobust` on actual Polygon/MultiPolygon objects, including
independent union and XOR operations. Clipper2 C# 2.0.0 uses all subject/clip rings
on an explicit **1e-6 metre grid**, with signed output areas preserving holes.
These are independent implementations, not exact GIS oracles. The fixed agreement
budgets are **1 m²** and **1e-8** for area and dimensionless fractions respectively.

| Metric | Maximum Core–NTS difference | Maximum Clipper–NTS difference |
|---|---:|---:|
| Either own area, m² | 3.8147e-5 | 0.0228539 |
| Intersection, m² | 2.8610e-6 | 0.0262261 |
| Union or XOR, m² | 4.1962e-5 | 0.0403290 |
| IoU | 3.3307e-16 | 4.2543e-12 |
| Either directional coverage | 1.9985e-15 | 2.2411e-11 |

The maxima agree across the three tested implementations and both fill rules.
Selected intersection and the full comparison agree bit-for-bit on these inputs.
Smaller observed disagreement with NTS is not a universal accuracy guarantee.
Clipper here uses fresh engines for accuracy checks; this is not the reused-input
performance comparator from the territorial benchmark.

## Why disconnected components need care

Simply giving all rings a common area origin failed a regression. Two squares
with nominal side `.001`, at `(0,0)` and `(1e12,1e12)`, should total
`1.9536743164062497e-6` for their actual rounded input coordinates. The initial
prototype returned `1.9073486328125e-6`, approximately **2.37% low**. Compensated
summation could not restore information already lost when forming each term.

The retained implementation groups rings by inclusive bounding-box overlap.
Nested, touching and crossing rings stay together. Different groups have no
overlapping bounds, so their winding fields are independent and each group's
weighted boundary chains close. Each group can safely use its own area origins;
the resulting areas are added with compensation. This is different from giving
each input ring its own origin when its weighted chain may be open.

Both the reproduced case and a second case at distance `1e14` with nominal side
`.1` now pass without relaxing tolerances. Conservative bounds grouping does not
solve every numerical difficulty inside an interacting group. The existing
[floating-point limits](../../docs/winding-numerics.md) still apply.

## Architecture and remaining work

The prototype retains the same exact crossing decisions and five boundary chains.
Each ring has its own closure and initial winding seed, including the contribution
of other rings. Crossing another ring of the same operand updates that operand's
winding. Collinear pieces are netted across the complete interacting group.
Single-ring certification and integer specialization are bypassed on this route.

Grouping/packing costs O(R²) in the current simple implementation. Seed work costs
O(sum(Rg * Ng)) over groups, in addition to crossing discovery and accumulation.
Ring descriptors and group metadata allocate; there is **no zero-allocation or
speed claim** for this prototype. Large numbers of mutually overlapping rings
remain an unmeasured workload. Existing public one-walk paths remain separate;
their API snapshot and regression suite pass in all five existing runtime modes.

The assessment supports continuing toward a public region API. Before exposing it:

1. Define an explicit region input type and distinguish shell/hole hierarchy from
   arbitrary winding rings. Preserve the existing one-walk API and its validation.
2. Measure preparation, repeated queries, allocations and many-ring workloads;
   consider caching validated ring metadata only if the consumer benefits.
3. Re-run the complete coverage workflow with the supported API and compare equal
   preparation/output policies. The old 24–26% timing advantage cannot be transferred
   to these larger inputs without measurement.

## Reproduce

From the repository root, with .NET 10 and PowerShell 7; no data download is needed:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
if ($LASTEXITCODE) { throw 'Restore failed.' }
dotnet build PolylineKit.slnx -c Release --no-restore
if ($LASTEXITCODE) { throw 'Build failed.' }
./tests/PolylineKit.MultiRingChecks/run.ps1 -Output artifacts/multiring-checks
```

Use a fresh output directory. The runner freezes its executable files locally,
verifies the selected assembly target/scalar mode, and produces JSON with source
hashes, input hashes, every pair's reference values and maximum disagreements.
CI runs the same three modes without a timing threshold. Clipper2 and NTS are
test-only dependencies; Core still has no external runtime dependency.

Raw reports are retained locally, not public download links. Their SHA-256 values
identify the recorded evidence; executing the commands creates new evidence:

| Mode | Report SHA-256 |
|---|---|
| Modern | `ba7af46c5e67adb43cda924c66af45704aad55e0890c3849bb65c3bd9ca05f35` |
| Modern scalar | `dd4b596f6f78cff274aa06fae85a9a75b4aa44920e546f94115a940167b837bc` |
| Portable | `9ca7e20329df7c8726cdb6ca670b1734b459acfd352183507895d45d1e499006` |
