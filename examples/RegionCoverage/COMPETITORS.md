# Comparator scope and prior art

The measured operation is planar intersection area `I` and coverage `I / A(zone)`
for already positioned, valid simple rings without holes. A zero-area zone has
undefined coverage. The [frozen protocol](data/PROTOCOL.md) supplies the CRS,
selection and agreement budget. None of the following timings establishes a
universal geometry-engine ranking.

## Direct baselines in this experiment

| Row | Work performed and conditions |
|---|---|
| `Winding` | Public `WindingArea.FilledRegions(..., NonZero).IntersectionArea`; its current general operation also computes other region areas. Preparation caches bounds and the simple ring's denominator, without caching pair results. |
| `Clipper64-reused-data` | Clipper2 2.0.0 integer intersection, followed by output-contour area. Input vertices and local extrema are prepared in reusable containers; each pair still loads both inputs and executes clipping. Coordinates are rounded onto a `1e-6` metre grid after the common translation. This is a quantized contract, checked against the same acceptance budget. |
| `NTS-legacy` | NetTopologySuite 2.6.0 public `Polygon.Intersection(...).Area`, with an explicitly selected Legacy geometry factory. Input geometries are prepared once; the intersection geometry is constructed on each comparison. |
| `NTS-OverlayNG` | NTS `OverlayNGRobust.Overlay(..., Intersection).Area`. This is the robust OverlayNG entry point, including its fallback noding strategies, not a bare fast noder. |
| `NTS-prepared-OverlayNG` | A prepared zone first tests coverage and disjointness. Proven containment returns a cached own area; remaining cases use OverlayNGRobust. Prepared predicates do not themselves calculate arbitrary intersection area. Preparation and first-query costs are separate from warmed reuse. |
| `convex-then-winding` | Example-only half-plane clipping when at least one independently validated simple ring is convex; otherwise Winding runs. The clipping ring is oriented counterclockwise. Convexity is not assumed from a same-turn test on an arbitrary self-intersecting path. This row's population and fallback rate must accompany its timing. |

Clipper's integer design and coordinate limits are described in its
[official overview](https://angusj.com/clipper2/Docs/Overview.htm). NTS distinguishes
[Legacy and NG explicitly in the pinned source](https://github.com/NetTopologySuite/NetTopologySuite/blob/v2.6.0/src/NetTopologySuite/Geometries/GeometryOverlay.cs).
Its [OverlayNGRobust documentation](https://nettopologysuite.github.io/NetTopologySuite/api/NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.html)
describes progressive snapping fallbacks, and
[PreparedPolygon](https://nettopologysuite.github.io/NetTopologySuite/api/NetTopologySuite.Geometries.Prepared.PreparedPolygon.html)
documents the reusable spatial predicates. NTS agreement is an independent
implementation comparison, not an exact arithmetic oracle.

## Optional Windows graphics comparison

The [WPF host](../../benchmarks/PolylineKit.WpfBenchmarks/README.md) compares direct
area scans and explicit `Geometry.Combine(Intersect)`. For two simple rings,
EvenOdd on both figures yields XOR, so `I=(A+B-XOR)/2`; equally oriented Nonzero
figures yield union, so `I=A+B-union`. These identities require the stated input
contract and can lose small intersections through subtraction and scanner
rounding. Values are not clamped. WPF's
[GetArea tolerance](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.geometry.getarea?view=windowsdesktop-10.0)
controls polygonal approximation, with a minimum of `1e-6`; it does not guarantee
that intersection-area error is below that number. The
[pinned native scanner](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/geometry/scanner.cpp)
uses a scaled integer workspace. Rows failing the common agreement budget are
graphics-precision comparisons. Managed allocation counters omit native memory.

## Related products and further comparisons

[ArcGIS Tabulate Intersection](https://doc.esri.com/en/arcgis-pro/latest/tool-reference/analysis/tabulate-intersection.html)
already cross-tabulates area and zone coverage, with grouping fields, feature
storage, output tables, and spatial-reference tolerances. Matching this experiment
would require unique zone/class IDs and equivalent projected-area settings.
[QGIS Overlap Analysis](https://docs.qgis.org/3.44/en/docs/user_manual/processing_algs/qgis/vectoranalysis.html#overlap-analysis)
adds total overlap area and percentage per overlay layer; its documented
ellipsoid settings and optional grid also matter. These are useful existing
consumer workflows, but their complete application costs cannot be compared
directly with an in-memory pair kernel. Neither application is timed here.

[GEOS](https://libgeos.org/usage/c_api/) is a reasonable future native comparison:
its C API supplies overlay, prepared predicates, and spatial indexes. Such an
experiment must include input marshaling, preparation, memory ownership, and
call overhead, with a pinned binary and the same accuracy gate. C++ implementation
alone does not establish a speed advantage. GEOS is not a dependency here.

Area-only overlay is established prior art. NTS 2.6.0's separate
[Lab `OverlayArea` source](https://github.com/NetTopologySuite/NetTopologySuite/blob/v2.6.0/src/NetTopologySuite.Lab/Operation/OverlayArea/OverlayArea.cs)
accumulates intersection area without constructing output topology and prepares
segment/vertex indexes. Its base geometry currently must be a Polygon without
holes. This is research code outside the pinned `NetTopologySuite` runtime
assembly, not a measured shipping baseline in this experiment. Any future port
or benchmark needs the same shared-boundary, containment and numerical checks;
the existence of area-only computation is not a novelty claim for PolylineKit.

## Choosing the outer rectangle index

The frozen workload needs bounding-box **candidates**, followed by the same area
operation; a point k-d tree does not directly answer this rectangle-overlap query.
Compare a linear scan, NTS `STRtree` (capacity 10), NTS
[`HPRtree`](https://github.com/NetTopologySuite/NetTopologySuite/blob/v2.6.0/src/NetTopologySuite/Index/HPRtree/HPRtree.cs)
(capacity 16; Hilbert ordering and contiguous arrays), and an independent packed
index (capacity 32) inspired by RtTools ideas. HPRtree already belongs to the pinned
NTS dependency. The independent implementation does not import unpublished RtTools
code. No index is assumed fastest before measuring this population and its build
cost.

[`Flatbush`](https://github.com/mourner/flatbush) provides static packed-index ideas;
its JavaScript implementation and linked C++/C# ports are useful references, not
cross-language timing evidence. Dynamic
[`RBush`](https://github.com/mourner/rbush) and
[libspatialindex's R*-tree](https://libspatialindex.org/) are relevant when objects
are inserted, removed or moved. Those capabilities do not automatically benefit
this immutable catalogue. All tested indexes must return the same complete
candidate set, including boundary contacts, and charge construction separately.

## Dynamic simplification

Replacing adjacent segments AB and BC by AC changes the index after each accepted
proposal. Dynamic deletion/insertion can then avoid rebuilding a static index.
The private RtTools implementation was reviewed for ideas: deletion shrinks
envelopes, condenses underfilled pages through reinsertion and collapses the root
when appropriate. It has not been benchmarked here; static catalogue results
provide no evidence against that implementation.

A proposed shortcut should be queried before modifying the index, excluding the
two replaced edge IDs and explicitly handling allowed adjacent endpoint contacts.
After acceptance, verify both old removals before inserting AC. Preserve unique
edge identities and old bounds; changing coordinates before removing their old
entry can leave stale candidates. Broad-phase queries must retain contacts and
zero-extent segment boxes, followed by the same exact intersection decisions.

A focused dynamic AABB/BVH or a static packed index with tombstones, a delta set
and periodic rebuilding could fit this narrower task. None is automatically
faster. Compare the entire identical proposal/update sequence: accepted removals,
candidate counts, exact predicates, build/update/query costs and allocations.
RtTools can remain a local private baseline without becoming a public dependency.
That experiment is separate from the prepared region coverage measured here.
