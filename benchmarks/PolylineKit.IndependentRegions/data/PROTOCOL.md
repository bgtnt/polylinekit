# Independent Helsinki population protocol

Declared on 2026-10-11 before downloading the complete feature collections or
running any PolylineKit, Clipper2 or NetTopologySuite calculation on them.

## Population and operation

Include **every feature** in these two official City of Helsinki WFS layers:

- `avoindata:Piirijako_peruspiiri`: the complete basic-district division;
  the preliminary count query returned **34** features.
- `avoindata:Tavoitteellinen_viher_ja_virkistysverkosto_VISTRA_alue`: the
  complete prospective green/recreation plan's **area** layer;
  the preliminary count query returned **1,095** features.

The operation is pairwise area of intersection, union, symmetric difference,
IoU and directional coverage, in their supplied positions. The complete
population contains **37,230 pairs per direction**. An ordinary closed AABB
intersection may reject disjoint pairs; it does not redefine the population.
All remaining pairs participate, including touching bounds and zero-area
intersections. No feature, category, pair, vertex count or result is selected
after observing correctness or performance.

The VISTRA layer is a **prospective planning layer**, not an inventory of actual
park ownership or current vegetated land. It contains multiple categories,
including proposed construction pixels, islands and restricted-use areas.
Feature areas may overlap. Report each feature/district relationship; do not
sum features as if they partitioned a single disjoint green-space region.

## Source request and coordinates

Use the official endpoint
`https://kartta.hel.fi/ws/geoserver/avoindata/wfs`, WFS 2.0.0,
`request=GetFeature`, the exact layer name, `srsName=EPSG:3067`,
`outputFormat=application/json`, `sortBy=id`, `count=10000`.
There is **no attribute or spatial filter**, simplification, precision,
quantization or geometry-repair request. Request a hits count immediately
before and after each full response, plus the source feature schema and WFS
capabilities. Abort the freeze if the counts disagree or the response does not
contain the entire counted population.

EPSG:3067 is ETRS89 / TM35FIN(E,N), a projected plane in metres. The service
advertises native ETRS-GK25 / EPSG:3879 and performs the requested reprojection.
The benchmark uses the **exact binary64 values parsed from the returned
EPSG:3067 response**, not geographic degree coordinates and not a claim to
unrounded native survey coordinates. There is no subsequent local projection,
translation, scaling, rounding, reorientation, simplification or repair.

Retain the raw response bytes, request URL, UTC retrieval time, response Date,
content length and SHA-256. The frozen responses are the authority; a later
response from the same live URL must not silently replace them.

## Polygon contract and completeness

Preserve every GeoJSON Polygon/MultiPolygon component. Within each component,
the first ring is the explicit shell and the remaining rings are explicit
holes. Preserve source ring order, point order and duplicate closing vertices.
Never connect independent rings with synthetic edges. The recommended
`PreparedRegion.FromPolygon`/`FromPolygons` factories may orient their **owned
copies** as required by their existing contract.

Inventory every original feature by its numeric `properties.id`, source
feature identifier and original properties. Verify unique increasing IDs,
finite two-dimensional coordinates, closed source rings, and the absence of
null or unsupported geometry. If these structural requirements fail, abort
and report the failure; do not discard the offending feature.

Topology is **not inferred by the freezer**. Before timing, the independent
runner must validate each original polygonal feature through NTS and publish
all invalid IDs and reasons. Every original feature still participates in the
full inventory and PolylineKit fill-rule evaluation. NTS overlay comparisons
are meaningful only for the explicitly reported population of valid original
features. No `Buffer(0)`, `GeometryFixer`, fallback repair or post-result
exclusion is allowed. If a Clipper integer-grid comparator uses a different
coordinate population, report its grid and validation separately.

## Comparison and evidence

Test the actual published **0.1.0-alpha.2** package from NuGet in a fresh
external consumer. Record package/binary hashes, runtime, OS, processor,
commands, sample protocol, correctness tolerances and observed discrepancies.
Run correctness checks before recording timings. Include preparation using
the recommended factories, warmed repeated full traversals, preparation plus
one full traversal, and allocations. Run timing competitors sequentially,
with three independent processes and the same full pair population.

This is a new data source relative to the previous Census, Natural Earth and
building samples. Its two layers share a municipal provider and their own
source accuracy constraints; they are not independent measurement truth.
Agreement with NTS/Clipper is comparative evidence, not a proven absolute
error bound. A maintainer-authored fresh consumer does not demonstrate an
unaffiliated external user or production adoption.

## License and attribution

The official dataset pages list **Creative Commons Attribution 4.0**:

- [Helsinki district division](https://hri.fi/data/dataset/helsingin-piirijako)
- [Helsinki prospective green/recreation network](https://hri.fi/data/dataset/helsingin-tavoitteellinen-viher-ja-virkistysverkostokartta)
- [CC BY 4.0 license](https://creativecommons.org/licenses/by/4.0/)
- [City of Helsinki open geodata terms](https://www.hel.fi/fi/paatoksenteko-ja-hallinto/tietoa-helsingista/kartat-ja-paikkatieto/paikkatiedot-tehokayttoon/avoimet-paikkatiedot)

Attribution must name the datasets and maintainers, link the license, and
state the download date, requested service reprojection and derived JSON
repackaging. The repository's MIT license does not replace these data terms.
