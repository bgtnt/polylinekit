# NTS interoperability checks

This report covers correctness and observed behavior only. It makes no timing
claim and does not add a runtime dependency to PolylineKit.Core.

The [runner](Program.cs) writes two reproducible JSON reports:
`check` for converter contracts and invalid inputs, and `check-gis` for the
complete existing Census snapshot. The commands, fixed tolerances and input
scope are in the [README](README.md#run-the-checks).

The GIS reference is NetTopologySuite **2.6.0**, using `OverlayNGRobust` for
intersection, union and symmetric difference. Every source polygon hierarchy is
validated before conversion. No repair, feature filtering or coordinate
transformation is applied. The original six fixture files and their hashes are
preserved. The whole population participates: 100 counties, 14 districts,
104 county rings and 19 district rings, including one district hole.

The acceptance budgets are unchanged from the complete GIS checks:
1 m² absolute area error and 1e-8 absolute IoU error. The runner records the
largest observed difference and its input for every checked metric.

Invalid walks are a different contract. Their observations bypass
`PolygonInterop.Convert`, which rejects invalid NTS geometries. Core interprets
each raw walk using an explicit `NonZero` or `EvenOdd` winding field. NTS's
ordinary area or overlay result on an invalid polygon must not be mistaken for
the author's intended geometry. The report records numbers or actual exception
types for both ordinary and robust NTS intersection; it does not assume that NTS
always throws.

The raw Core reference areas/intersections have small analytic definitions:

| Walk | NonZero area | EvenOdd area | NonZero intersection | EvenOdd intersection |
|---|---:|---:|---:|---:|
| Bow tie | 8 | 8 | 4.5 | 4.5 |
| Outer square with a same-direction internal loop | 36 | 32 | 16 | 12 |
| Triangle with a retraced self-touch branch | 8 | 8 | 4.5 | 4.5 |

The mask is `(1,1)`–`(5,5)`. Input coordinates are recorded in the JSON. A fill
rule defines the covered set; it does not infer whether the loop was accidental.

## Observed results

Run on **2026-10-10**, Windows x64, **.NET 10.0.12**, against the alpha.2 working
tree based on [`49bbdb4`](https://github.com/bgtnt/polylinekit/commit/49bbdb42aa2fcea298d8b10ffadc71c29832e6c0).
This is the role-aware converter/factory change, not a new geometry engine.
The package lock fixes NTS at **2.6.0**; its assembly version is **2.0.0.0**.
Commands and inputs are preserved in the example so these observations can be
reproduced on later revisions.

The converter passed **62 contract checks**, including all four shell/hole
direction combinations returning **84 under both rules**, empty IoU, and rejection
of null, nonpolygon and invalid hierarchies. The valid MultiPolygon containing a
hole, an island and a disconnected polygon returned **92**.

The complete GIS run passed **45,028 comparisons**: 5,600 full-metric calls,
5,600 selected-intersection calls and 228 own-area checks. It retained all
114 features, 122 components, 123 rings and one hole. Of the 1,400 geometric
pairs, 285 have intersecting closed envelopes and 1,115 are bounds rejections.
Both directions and both fill rules participate.

| Metric | Largest absolute difference | Fixed budget | Worst input |
|---|---:|---:|---|
| Own/operand area | 3.814697265625e-5 m² | 1 m² | District `3711` |
| Intersection, full or selected | 6.9141387939453125e-6 m² | 1 m² | County `37049` / district `3703` |
| Union | 4.1961669921875e-5 m² | 1 m² | County `37033` / district `3711` |
| Symmetric difference | 4.1961669921875e-5 m² | 1 m² | County `37033` / district `3711` |
| IoU | 9.43689570931383e-16 | 1e-8 | County `37135` / district `3704` |
| Selected versus full Core intersection | 0 m² | 1 m² | All checked pairs |

The raw source hashes match the existing manifest, including county snapshot
`4e114c8d8168e782ac185cd1f1f7bc7c4a1772e218e9502d00b204837bc5441b`
and district snapshot
`237b71fa2dd7cc73e4e46a0b624ab0256051bf4200e4920ecc805720f6a0886f`.
See the [source manifest](../RegionCoverage/data/manifest.json) for all six files
and their provenance. The original metre coordinates were copied unchanged.

## What NTS did on the invalid inputs

These are observations of the locked package and the recorded inputs. Ordinary
NTS intersection uses an explicitly **Legacy** geometry factory;
`OverlayNGRobust` is invoked separately. All three geometries report
`IsValid == false`. Every ordinary intersection throws
`NetTopologySuite.Geometries.TopologyException`.

| Walk | NTS `Area` | NTS ordinary intersection | NTS `OverlayNGRobust` intersection | Core intersection NonZero / EvenOdd |
|---|---:|---|---|---:|
| Bow tie | 0 | `TopologyException` | `TopologyException` | 4.5 / 4.5 |
| Internal loop | 40 | `TopologyException` | 4 | 16 / 12 |
| Retraced self-touch branch | 8 | `TopologyException` | 11.5 | 4.5 / 4.5 |

Robust overlay therefore does **not** always throw on these invalid inputs. It
also does not define the same raw winding contract as Core on the two cases that
return a number. This is not a claim of an NTS defect on valid polygons, nor a
recommendation to treat either result as the intended geometry. The converter
rejects these inputs; an application using Core directly must choose its fill
rule explicitly. The six raw Core area/intersection pairs agree exactly with
the analytic table above (12 assertions).

The local reports are `artifacts/alpha2-checks/nts-contracts.json` and
`artifacts/alpha2-checks/nts-gis.json`; generated evidence is not a runtime input.
CI repeats both commands without a timing threshold. No NTS operation was timed
for this report, and no performance claim follows from these checks.
