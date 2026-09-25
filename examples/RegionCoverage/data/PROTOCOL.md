# Frozen RegionCoverage selection protocol

Initial population selection declared on 2026-09-25 before downloading this
benchmark's geometry. Workflow directions and numerical acceptance details were
finalized the same day after source inspection and before any RegionCoverage
timings. Source selection is independent of measured speed, overlap size,
topology, and numerical agreement.

## Fixed population

- Prepared zones: **all** features satisfying `STATE='37'` (North Carolina) in
  [Census Generalized ACS2024 Counties 5M, layer 12](https://tigerweb.geo.census.gov/arcgis/rest/services/Generalized_ACS2024/State_County/MapServer/12).
- Queries: **all** features satisfying the same predicate in
  [Census Generalized ACS2024 119th Congressional Districts 5M, layer 6](https://tigerweb.geo.census.gov/arcgis/rest/services/Generalized_ACS2024/Legislative/MapServer/6).
- Request every attribute and geometry, sorted by `GEOID`, with `outSR=5070`
  (NAD83 / Conus Albers; projected metres). Do not request simplification,
  densification, coordinate rounding, quantization, or geometry precision.
- Save the complete original query response bytes and layer metadata, retrieval
  UTC, query URLs, lengths, and SHA-256 hashes. Verify the returned spatial
  reference and that the response was not truncated.
- Keep every source feature in those snapshots. The single-contour benchmark
  admits only complete Polygon features with exactly one ring. Do not discard
  islands or holes, bridge independent rings, choose the largest component,
  repair geometry, or resample it. Record excluded IDs and reasons. Validation
  by the runner is separate; a one-ring source is not assumed valid.
- Preserve each admitted ring's coordinates and order exactly as parsed binary64
  values. Remove only one final point when it exactly equals the first point;
  closure becomes implicit. Do not translate or independently normalize rings.

## Pair and workflow population

Use every admitted zone against every admitted query. All comparisons use the
same frozen coordinate arrays. Reject disjoint bounding boxes using the same
test outside the geometry operation for every implementation. Preserve every
bounding-box candidate, including zero-area contacts, containment, convex
shapes, and difficult/shared boundaries. Do not select examples by results.
Report total pairs, bounding-box rejects, candidate count, source exclusions,
and independent geometry-validation failures before reporting performance.

Measure both explicitly named directions: county zones against district queries,
and district zones against county queries. The latter gives each prepared
district many county queries. Both directions contain the same complete set of
geometric pairs and bounding-box candidates; only operand roles and the coverage
denominator differ. The filenames retain the original county-zone/district-query
roles. This direction choice was declared before timings, not selected from
measured performance.

The complete prepared-zone/many-query workflow includes scanning all pairs and
the common bounding-box test. Candidate geometry timing and preparation costs
are reported separately. Common fixed precision, if used by the runner, must be
applied equally and reported as a distinct input contract; these frozen files
retain the original projected doubles.

Before timing, independently classify every admitted geometry with NetTopologySuite
(NTS) and record any invalid IDs without repairing them. For numeric operations,
subtract one common origin: the center of the combined eligible dataset bounding
box. Apply that same translation to every contour and every method; preserve the
untranslated source in these files. Acceptance against NTS requires both absolute
intersection-area disagreement at most **1 square metre** and absolute coverage
fraction disagreement at most **1e-8**, for every eligible pair in both directions.
NTS agreement is a cross-implementation check, not an exact arithmetic oracle.
Report failures before timings and do not remove failing pairs to improve a result.
Prepared and cold complete workflows must both be reported in both directions.
Any graphical/WPF or other precision variant is a separately named experiment
with its own contract; it cannot silently replace a failed comparison.

## Interpretation and provenance

These are two different administrative boundary layers from the **same Census
source**, with related and sometimes shared boundaries. They are not independent
survey measurements, land-cover classes, or observations of changing terrain.
The experiment concerns geometric county coverage by congressional regions,
not political or demographic analysis. This bounded convenience population does
not represent all GIS geometry.

The layer descriptions identify U.S. Census Bureau data. See its
[public-access policy, page 9](https://www2.census.gov/foia/ds_policies/ds027.pdf) and
[citation guidance](https://www.census.gov/about/policies/citation.html).
The benchmark's calculations and conclusions are the project's responsibility;
the Census Bureau is the source of the original geometry only.

The retrieval manifest will add observed counts and hashes without changing this
selection rule. A future source refresh is a new dataset and must not overwrite
the published identity silently.
