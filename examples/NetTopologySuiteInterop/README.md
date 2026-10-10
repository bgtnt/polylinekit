# Use NTS polygons with PolylineKit

If an application already holds a NetTopologySuite `Polygon` or `MultiPolygon`,
this example converts it into a `PreparedRegion` for area, intersection, coverage
or IoU calculations. Shell and hole roles survive arbitrary ring directions.
There is no adapter package: copy the small [converter](PolygonInterop.cs) into
your application. Only this example references NetTopologySuite 2.6.0; it does not
add a dependency to PolylineKit.Core.

```csharp
using NetTopologySuiteInterop;
using PolylineKit;

PreparedRegion prediction = PolygonInterop.Convert(predictedPolygon);
PreparedRegion reference = PolygonInterop.Convert(referencePolygon);
RegionOverlapResult overlap = RegionArea.Compare(prediction, reference);
double? iou = overlap.IntersectionOverUnion;
```

`Convert` accepts valid `Polygon` and `MultiPolygon` inputs, including empty ones.
It rejects null, other geometry types and invalid polygon hierarchies. An invalid
input is never silently repaired. `PreparedRegion.FromPolygon` and `FromPolygons`
assign shell/hole orientation; `FromRings` remains the raw-winding alternative.

The converter copies XY coordinates unchanged. It does not transform coordinates
according to `SRID`, project geographic coordinates, simplify, normalize or align.
Both operands must use the same planar coordinate reference. Areas are in squared
input units; longitude/latitude areas are not square metres. Z and M are outside
these 2D metrics. Do not mutate the NTS geometry during conversion; afterwards
the prepared snapshot is independent.

## Run the checks

From the repository root, using the locked dependencies:

```text
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
dotnet run --project examples/NetTopologySuiteInterop -c Release --no-build -- check artifacts/nts-interop/contracts.json
dotnet run --project examples/NetTopologySuiteInterop -c Release --no-build -- check-gis . artifacts/nts-interop/gis.json
```

`check` needs no GIS files. It tests four shell/hole direction combinations, a
hole containing an island, disconnected components, empty polygons, null,
nonpolygon and invalid inputs, unchanged coordinates, and the nullable IoU result.
It also records NTS `IsValid`, `Area`, ordinary `Intersection`, and
`OverlayNGRobust` intersection on three invalid walks. The Core observations use
raw rings with each explicit fill rule, separately from the valid-only converter.
The fixed mask is the square from `(1,1)` to `(5,5)`.

`check-gis` adds the complete existing Census snapshot: **100 counties and 14
congressional districts**, including all disconnected parts and the actual hole.
It verifies the six frozen source hashes and protocol before and after running.
All **1,400 pairs** are checked in both directions under both fill rules, without
filtering features by geometry complexity or results. NTS reference values use
`OverlayNGRobust`; disjoint bounding boxes imply zero intersection. Original
EPSG:5070 metre coordinates are copied unchanged, with no shared translation.

Acceptance budgets are the existing fixed GIS budgets: **1 m²** absolute area
error and **1e-8** absolute IoU error. The JSON records counts, maximum differences
and the worst input for each metric. These are independent floating-point
comparisons, not exact-arithmetic proofs. This already used dataset checks
interoperability; it is not a new geographic holdout or a performance claim.

Both commands run in Windows/Linux CI without time thresholds. The results and
limitations are described in [RESULTS.md](RESULTS.md).
