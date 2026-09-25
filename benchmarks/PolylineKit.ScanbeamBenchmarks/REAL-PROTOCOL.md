# Real-contour scanbeam intersection experiment

Declared before this experiment's timings. The prior small integer-grid gain
does not establish usefulness for real contours or intersection-area queries.
This extension remains outside the shipping library.

## Input and coordinate contract

Use all 98 county and 11 district rings already frozen in
`examples/RegionCoverage/data/{zones,queries}.json`, with the original provenance,
source exclusions and hashes. No new download, feature selection, repair,
simplification or discarded failing pair. Original source coordinates are
EPSG:5070 binary64 metres. Evaluate every county/district pair, both directions.

The new experiment explicitly rounds each WORLD coordinate to the nearest
integer metre, midpoint away from zero, on a grid anchored at EPSG:5070 (0,0).
Then subtract one common integer origin: floor of the midpoint of the combined
rounded bounding box, separately in x and y. Original reference contours use
the same translation without rounding. Preserve vertex counts/order, including
any duplicates introduced by rounding; report these and all validity failures.
This is a different input contract from the original RegionCoverage experiment.

The core accepts local integers up to magnitude 524288, retaining the proven
Int64 arithmetic choice for magnitudes <=2048 and Int128 otherwise. The bound
proof covers every intermediate; the global vertex limit remains 8192.
Add two independently closed loops for intersection, with separate winding
counts and the same exact event ordering and positive-gap integration. Do not
connect the rings with artificial bridge segments. Single-walk results on the
previous fixtures must remain bit-identical. No automatic dispatch from the
public binary64 APIs is introduced.

## Correctness and geometric error

Validate original and rounded polygons independently with the existing pinned
NetTopologySuite 2.6.0 benchmark dependency. Record invalid/collapsed rings rather
than silently fixing them. Compute original-versus-rounded own-area and all-pair
intersection/coverage changes separately from backend errors. Exact integer
shoelace areas independently check own areas of validated simple rounded rings;
they are not a general self-intersection algorithm or an intersection oracle.

Validation clarification before any timings: admit polygons with NTS `IsValid`
and positive area; record `IsSimple` separately. The first harness accidentally
conjoined these predicates and rejected Macon County (37113). Its consecutive
vertices 18 and 19 both round to (1129830,1400586), so the retained zero-length
edge triggers NTS 2.6's boundary simplicity check. `IsValid` remains true with no
validation error. This is not evidence of polygon invalidity or a proper crossing.
Correcting the predicate changes no input, grid, feature selection or repair
policy; all 109 rings and all six collapsed edges remain in the experiment.
The distinction is pinned by a regression before pair validation and timing.

For all pairs and both directions, compare scanbeam, current intersection-only
Winding and direct Clipper64 against NTS on the SAME rounded input. Preserve the
previous acceptance budgets: <=1 square metre intersection disagreement and
<=1e-8 absolute coverage-fraction disagreement. A numerical failure prevents
timing claims for that method; no failed case is removed. Check both fill rules
in the new general intersection tests. Real timings use NonZero only; validated
simple rings have the same filled set under EvenOdd.

Separately apply those same budgets to original-versus-rounded NTS results.
A failure here means this experiment cannot justify substitution for the original
binary64 coverage contract, even if every rounded-input engine agrees and is fast.
Such failure does not prevent measuring the explicitly rounded-input workload.

## Timings and decision

Three methods: current `WindingArea.IntersectionArea`, experimental scanbeam
intersection, and direct reusable-data Clipper64 (the existing RegionCoverage
adapter). Clipper's output grid is 1e-6 metre, even though the supplied input
is now whole metres; integer inputs can have fractional intersections. Record
the difference in intersection construction semantics. NTS is an untimed
cross-implementation reference, not exact arithmetic ground truth.

Each direction traverses all 1078 pairs with the same inclusive bounding-box
rejection. No cached pair results, selective early match exits or changed outer
index. A numerical digest consumes each intersection and directional coverage.
Measure these scopes for all three methods in both directions:

1. Prepare: round/translate the full source population, construct prepared
   regions, bounds/own areas and engine-specific buffers/data.
2. Warm full table: prepared data, complete traversal, ordinary internal
   geometry preparation still inside each API call.
3. Prepare plus one full table: new prepared session from the source doubles,
   including rounding and method setup. This is a warm process/JIT; Winding's
   thread cache can persist, and it is not a cold-process startup claim.

Three fresh sequential processes, five batches/row, >=20ms calibration target,
rotated method order, DOTNET_TieredCompilation=0. Record all samples, time,
per-thread managed allocations, input hashes, outputs, commit and binary hashes.
Preparation allocation reports construction cost, not retained/peak/native memory.
No claim about retained memory follows from these counters.
Source JSON parsing and selection of the fixed common origin are outside timing;
all per-session rounding, translation and backend preparation are timed.

The speed gate requires >=20% less time than direct Clipper for both directions
in both warm and prepare-plus-one scopes (four gates). All methods' rounded-input
correctness gates must pass. Real-contour integration additionally requires
original-input equivalence; a coarse-grid speed win alone is insufficient.
Publish a negative result with all cases if either condition fails. Do not tune
the grid, select features or add alternative algorithm heuristics after timings.
