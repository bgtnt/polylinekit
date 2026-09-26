# Measure region coverage

**How many square metres of a county lie in a congressional district, and what
percentage of the county is that?** This example answers that question from two
existing boundary layers. The shapes represent different territories; their
boundary similarity is not the requested result.

Start with the [minimal package-based example](../PackageCoverage) for an
L-shaped zone: area 20, intersection 7 and coverage 35%. This larger example
uses frozen real Census contours and demonstrates candidate selection as well
as intersection. It is built from source, with benchmark-only dependencies.

Coverage is the intersection area divided by a positive-area zone:

```csharp
double zoneArea = PolylineArea.FilledArea(zone);
double intersection = PolylineArea.IntersectionArea(zone, footprint);
double? coveredFraction = zoneArea > 0 ? intersection / zoneArea : null;
```

The operands fill independently. Inputs must share a planar coordinate system;
the library does not project geographic data. This example also compares region
indexes and geometry engines for applications handling many region pairs.

## Run the example

```sh
dotnet run --project examples/RegionCoverage -c Release -- check
dotnet run --project examples/RegionCoverage -c Release -- run artifacts/coverage
```

`check` verifies input hashes, geometry contracts and candidate sets. `run`
writes `accuracy.json`, including every candidate and its coverage result.
The frozen inputs contain 98 single-ring North Carolina counties and 11
single-ring congressional districts in EPSG:5070, with metre coordinates.
Areas are planar square metres. Excluded multi-ring source features remain in
the source snapshot and are listed in the [data documentation](data/README.md).

The data originate from the U.S. Census Bureau and are public domain in the
United States. [Source metadata and hashes](data/manifest.json) and the
[offline verification script](data/freeze.py) accompany the example.

`accuracy.json` is a developer-facing numerical report for these frozen layers.
The example is not a general GIS layer-import application. A parcel/flood-zone
report would use the same intersection and coverage operations, but that dataset
and user workflow are not demonstrated here.

## Recorded performance

At measured revision `2fe5f74`, intersection-only coverage took **24–26% less
time** than reusable prepared Clipper2 C# in four predeclared whole-traversal
comparisons, including fresh preparation plus one traversal. Each direction
included all 1,078 potential county/district pairs: 211 bounds candidates needed
geometry and 867 were rejected by the common bounds check. Warm calls returned
numeric coverage without constructing an output matrix or caching pair areas.

This repeat uses the current Core with its retained-workspace cap. All 2,156
directional pair results remain identical to the earlier measurement. All 90
intersection-only warm allocation samples are zero bytes; preparation and first
use can allocate. The earlier 26–29% time advantage is retained as historical
evidence; this repeat gives 24–26% under the unchanged protocol.

These are related Census layers, not arbitrary GIS data or an external deployment.
The measurement uses a source build rather than a packed package. See the
[full measured report and reproduction commands](PERFORMANCE.md) and the
[cross-scenario comparison](../../docs/performance.md#recorded-results-by-scenario).

## Application considerations

Cache a fixed zone's area when serving repeated queries. A bounds index can
skip disjoint region pairs; it does not change the internal cost of an actual
intersection. The example includes NTS STRtree/HPRtree and a static packed
index to demonstrate this separation. These are example-only dependencies and
helpers, not additions to the area library's runtime or public API.

Each operand here is one ring. Do not treat a collection of islands or holes
as a single concatenated walk. Coverage ratios are not clamped; inspect numeric
error when a result lies just outside its expected range.

Adding the intersections of overlapping footprints may count an area more than
once. This does not measure coverage by their union. For the own area of a
trusted simple ring, shoelace is the inexpensive option; general fill-aware
methods address more complicated walks.

Benchmark commands remain available in the executable for maintainers. Prior
competitor results and protocols are in the
[development snapshot](https://github.com/bgtnt/polylinekit/tree/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/RegionCoverage).
