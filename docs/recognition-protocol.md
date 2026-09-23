# Recognition evaluation protocol v1

Frozen before held-out recognition scoring. Source baseline: c8bac3f9ea8064e873569fb569462e66e52bcd25.

The implementation and consumer are new, use public source data and original
synthetic fixtures, and have no private template/source dependencies. No packaging
or publication to NuGet is part of this iteration. Core targets and dependencies
remain .NET Standard 2.0 / .NET 10 and Clipper2 2.0.0.

Performance sampling fixed before results: use the first ordinal held-out $1
writer (s02) for each of the three seeds, and all supported official Pendigits
test queries for each seed. These are complete 48/50-template queries. Unsupported
Pendigits inputs are reported in quality coverage, outside timed valid queries.
Warmup uses development-only queries. All three variants run in three fresh
processes each, rotating their order across repetitions. No geometry or
classification settings will be selected from these timing passes.

Import axes remain unchanged: $1 uses its screen coordinates; Pendigits keeps
the source tablet columns. Missing axis/writer documentation is not reconstructed
from class labels. The existing 7-Zip 26.02 executable is a development-only
UNIX-compress decoder, with its version recorded by the importer.

## Deliverable 2: one reproducible evaluation harness

Add `experiments/PolylineKit.Recognition` referencing the library. Keep external
recognizers, imports and analysis out of the runtime library. Keep import tools
under `scripts/datasets/` and downloaded/converted data under ignored `artifacts/`.
Explain and pin any development-only decompression dependency before adding it;
the original Pendigits files use Unix compress `.Z`, not gzip or the eight-point
feature-table format.

Use a shared record with `sampleId`, `dataset`, `split`, optional verified writer
and session identifiers, label, and a list of strokes containing ordered XY and
optional time values. Preserve original points, point order and pen boundaries.
Fix the axis convention explicitly from each format; do not infer rotations or
closure from the true class label. Preserve all records and mark exclusions.

The manifest records archive URL/SHA-256, parser revision, raw and supported
counts by class, point-count distribution, sample IDs, split assignments and
template-bank IDs. A changed archive hash requires inspection and a new manifest.
No raw downloads enter Git or a package. Published manifests/results contain
identifiers and derived scores; review permission before redistributing raw paths.

### $1: primary recognition endpoint

Official assets: https://depts.washington.edu/acelab/proj/dollar/
Archive: https://depts.washington.edu/acelab/proj/dollar/xml.zip

Expected archive SHA from the supplied brief:
`c5d81282e46571d813fdcab06d67ef96c848c736922deaba5189a9658b1e43f6`.
Expected size: 4,789,784 bytes, 5,280 XML records. The importer must independently
verify these values; the importer verified the downloaded archive bytes and all declared counts.

Use s01 pilot only for development: repetitions 1–5 form the template pool and
6–10 form validation, retaining all speeds/classes in each. Pick three templates
per class with a balance of speeds when available. No validation record is a
template. Source and augmented copies always remain in the same split.

Freeze configuration before scoring the 4,800 main records. For each of s02–s11,
hold that writer out and select three templates per class from three different
other main writers (48 templates). Evaluate every held-out sample, every class
and every speed. Use seeds 1729, 2718 and 31415 for all methods. Define selection
by stable SHA-256 ordering of seed plus canonical identifiers, not runtime Random
or file enumeration order. Save actual selections. Seeds are repeated template
banks, not independent subjects.

### Pendigits: separate digit scenario

Official description: https://archive.ics.uci.edu/dataset/81
DOI: https://doi.org/10.24432/C5MG6K
Archive: https://archive.ics.uci.edu/static/public/81/pen+based+recognition+of+handwritten+digits.zip

Use `pendigits-orig.tra.Z` and `pendigits-orig.tes.Z`. Expected archive SHA from the
brief is `1e02bea023613c2b11c9492f6f34caf975420455934f3527d270cee9a1f03b64`;
verify it and UNIPEN counts during import. Expected total train/test counts are
7,494/3,498; supported single-stroke counts are 5,564/2,744. Report support coverage
(expected about 78.44% of test records) and exclusions by class. Never draw
connectors between separate pen-down strokes.

For development, use a deterministic class-stratified 80/20 split of the supported
official training set. Unless a reliable writer mapping is found, describe it as
a record split, not writer-independent validation; do not infer writer identity
from undocumented COMMENT numbers. Development template banks contain five samples
per digit (50 templates) from the 80% pool. After configuration freeze, final
banks may use the entire official training set; the official test set stays
untouched until then. Use the same three seeds and recorded selection rule.
The official external train/test writer separation is the supported claim.

## Geometry, methods and development choices

The shared RMS/area preparation is:

1. Validate, remove adjacent duplicates and preserve open traversal order.
2. Independently center bounds and uniformly fit longest extent to 1, preserving
   aspect ratio. Normalize once per template/query.
3. Resample to 64 arc-length points with endpoints included. Use these same
   64-point paths for the primary RMS and endpoint-bridged-area scores. Retain the
   original full path for overlays and a single diagnostic described below.
4. No reflection, Stretch, reversal, cyclic start search or extra fitted scale.
   Digits use no fitted rotation in the primary experiment.
5. Gestures may select between no rotation and an explicit ±15-degree tilt policy
   on the pilot only. The latter uses a fixed 1-degree angle grid about the
   normalized origin. Choose the query-to-template transform by RMS, reuse that
   exact transform for area, and report that it is a discrete bounded search.
   Keep this adapter in experiments, not a new general public registration API.
   Choose the geometry policy using development RMS accuracy; ties prefer no
   rotation. Do not clamp an unrestricted fit while retaining its old translation.

Use NonZero endpoint-bridged fill and decimal precision 6 for primary area.
The ranking value is raw area in this fixed normalized coordinate system.
IoU and BoundsAreaRatio do not enter the open-stroke classifier.

Compare five named methods on identical banks and query IDs:

- shared preparation + RMS;
- same preparation and transform + endpoint-bridged area;
- one frozen combined RMS/area score;
- 16-point Protractor following the official pseudocode, with explicitly named
  native orientation policy and validation against elementary orientation cases;
- 64-point DTW for the digit experiment, with native preparation documented.

For combination, use `(1-w)*RMS/r0 + w*Area/a0`, with w in
`{0, .25, .5, .75, 1}`. r0/a0 are positive medians from all development query-bank
pairs only; if a median is zero, use the declared fixed value 1 and report it.
Freeze one weight per dataset by development top-1 accuracy, ties preferring the
smaller area weight. A selected w=0 is evidence against incremental area benefit.
These scaled quantities are not calibrated confidence percentages.

Protractor uses its 16-point native vectorization. Digits use its orientation-
sensitive variant; gestures may select sensitive/invariant on the pilot. Preserve
the admitted rotations in the pseudocode's atan(b/a): replacing it with atan2
without accounting for the changed angular domain would change the recognizer.
Source: https://depts.washington.edu/acelab/proj/dollar/protractor.pdf
Keep attribution and source/version information with the experiment baseline.

Freeze DTW local cost as squared Euclidean distance, allowed monotone horizontal,
vertical and diagonal moves with forced endpoints, and score as accumulated cost
divided by 64. Do not add path-length normalization after optimizing a different
objective. Select only the Sakoe–Chiba window from `{8,16,63}` on digit development
data; ties prefer the cheaper window. Implement or adapt the documented baseline
with attribution and small hand-computable checks. It remains outside the library.

Rank templates by score, breaking exact ties by canonical sampleId (ordinal).
The winning template supplies the class. Top-three display means distinct classes,
using each class's best template. Record score gaps and alternative near ties;
do not hide numerical changes by epsilon clamping.

One predeclared diagnostic may compare full-original-vertex area with the matched
64-point area on the same banks/transforms. It must be clearly secondary and
cannot replace the primary result after inspecting held-out labels.

## Freeze, evaluation and decision

Commit protocol, archive/import manifests, development choices, implementation
IDs, tie policy and template-bank IDs before held-out scoring. Enforce split
disjointness in the harness. Run small import/correctness smoke tests while
developing; do not repeatedly evaluate the held-out set while selecting settings.

Export one prediction row per dataset/query/seed/method with true and predicted
label, chosen template, raw RMS/area, transform ID/parameters where applicable,
failure/unsupported status and verified writer information. Keep the same failed
queries in the comparison denominators. Record each external method's native
score as well; auxiliary RMS/area diagnostics may be joined from untimed analysis
and must not force evaluation of an unused score inside a method's latency run.
Pendigits reports both accuracy within
the supported single-stroke subset and correct predictions/all official test
records, with unsupported records explicitly distinguished from misclassification.

Primary statistical comparison: frozen combined score versus RMS on $1. Report
writer-mean paired accuracy difference and a paired 95% writer-block bootstrap
interval (10,000 replicates, seed 424242), retaining all seeds and records inside
each writer block. Area-alone and external-baseline comparisons are secondary;
report their paired errors and costs without selecting a new primary winner.
With only ten main writers, show every writer result and uncertainty prominently.

For Pendigits, report held-out accuracy, confusion matrix, paired wins/losses,
failures, coverage and variability across banks. Without verified per-record
writer IDs, do not offer a writer-bootstrap interval or pretend individual
records/seeds are independent writers.

No post-hoc accuracy target or acceptable-loss margin is introduced. A positive
recognition claim needs reproducible paired improvement with its uncertainty and
latency/memory tradeoff; an ambiguous interval is inconclusive. Diagnostic value
from the overlays is a separate engineering observation. Produce one verdict:

1. Area adds useful information at an explicit cost.
2. Preparation helps, while area adds little; default the consumer to the better
   scorer and retain area as a geometry primitive.
3. No demonstrated consumer benefit; preserve evidence and stop expanding scope.

Skip candidate-filter experiments by default. Admit one only if development
results show a competitive area cost and useful shortlist recall. Freeze k and
the comparison with full matching and RMS filtering before test evaluation.
Never add datasets, methods or tuning rounds simply to obtain an area win.

## Deliverable 3: a new usable consumer

Add `examples/StrokeTemplates`, a small .NET 10 console/replay application using
the actual library and a shared evaluation adapter. It loads a query and bank
from the common JSON/sample identifiers, returns top-three distinct labels, and
exports a local HTML/SVG inspection view. The view shows the actual transformed
query and selected templates, raw RMS and area under their correct names, and
optional resolved difference contours. Display/diagnostic generation is not part
of scoring latency. A drawing UI is a possible later addition, not a blocker.

The consumer and evaluation use the same preparation and scoring code. This is
real input-to-ranking wiring, not a second independent implementation for a demo.
Use fresh openly generated tiny fixtures for smoke examples; no legacy private
templates or source are copied. Support sample import/export, with labels and
only genuinely available provenance.

## Deliverable 4: application performance and numerical checks

Measure one query against the fixed 48/50-template bank. Prepare each bank once
and each query once where possible. All competing methods receive equivalent
caching opportunities. Keep prepared-bank internals immutable and local to the
experiment/consumer until a public caching API is justified. Avoid buffer pooling
in this iteration; do not cache Clipper geometry across incompatible pair origins
or clipping contexts.

Distinguish readable scalar, cached scalar and the same cached implementation
with SIMD enabled. Uncached scalar is an attribution diagnostic; the main method
comparison uses prepared banks for every method. Force scalar with the existing
application switch and record actual target/ISA/vector width.

Use three independent processes with normal runtime/tiered settings and recorded
environment. Warm each backend for at least three seconds and 100 complete queries
using a fixed development-data sequence before timed warm-query passes. Report
cold bank construction and retained
managed memory; warm query p50/p95 and bytes/query including result creation;
scoring-only support timings; total processing time and GC collections/pauses
when measured. Retained memory is not the sum of allocated bytes. Report raw
point-count distribution plus fixed sample/template counts. Keep the existing
16/64/256/1024 synthetic evidence and add small boundary checks as needed; do not
repeat it merely to fill another performance table.

Preserve all 3,341 existing checks and the documented 2^53 case. Add importer,
analytical area-contract, split-integrity and actual consumer-wiring checks. Test
scalar/SIMD scores, returned transformations, ownership, ties and predicted labels
on identical frozen query/bank pairs. Since this iteration does not reorder
floating-point reductions, expect bitwise equivalence where arithmetic order is
unchanged; document operation-specific analytic tolerances before comparisons.
Any genuine prediction change requires a concrete recorded case, not silent
acceptance. Exercise forced scalar and available accelerated paths on Windows
and Linux CI. No new runtime dependency is required.

## Implementation order and artifacts

Use a new branch for this iteration; the completed branches/worktree were deleted.
Keep commits reviewable:

1. API naming/filled-region result and analytical tests.
2. Importers, shared record schema, manifests and small parser tests.
3. Shared preparation, the bounded set of baselines and development runs.
4. Frozen configuration/banks/protocol commit, then one held-out evaluation cycle.
5. New consumer, identical scoring wiring and inspection output.
6. Application performance/numerical comparison, report and final CI.

Expected locations: `src/PolylineKit/`, existing comparison checks,
`experiments/PolylineKit.Recognition/`, `examples/StrokeTemplates/`,
`scripts/datasets/`, `results/recognition/` and `docs/recognition-evaluation.md`.
Update existing API/provenance/performance documents rather than creating a
parallel documentation framework. The final report identifies actual commits,
reproduction commands, manifests, prediction tables, environment, consumer entry
point, supported input scope, failures and selected verdict.

Completion means a reliable answer and a runnable small consumer, not an area win,
a new package release or a claim of general handwriting recognition.
