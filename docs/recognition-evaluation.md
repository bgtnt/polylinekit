# Unistroke recognition evaluation

Evaluation date: 23 September 2026. Repository:
[bgtnt/polylinekit](https://github.com/bgtnt/polylinekit).

## Decision

Keep PolylineKit's comparison, normalization and transformation primitives and
the small replay consumer. This experiment does **not** justify making area the
default unistroke recognizer or expanding a handwriting-recognition product.
Use RMS as the inexpensive default for this gesture policy; DTW has the highest
held-out digit accuracy among the tested methods. Area remains useful as a separately named geometric
measurement and an optional diagnostic. No NuGet package was built or published.

The frozen digit combination shows a modest descriptive improvement over RMS,
but area alone is substantially worse on both datasets. The primary gesture
comparison selects zero area weight during development and therefore has no
incremental area benefit. This is outcome **2** of the protocol: useful preparation
and a runnable comparison tool, with limited recognition evidence for area.
There is no comparison against completely unprepared recognition, so this is an
engineering usefulness conclusion, not a measured causal gain from normalization.

## Protocol and provenance

The [protocol](recognition-protocol.md) predates held-out scoring. Imported data
come from the official [$1 project](https://depts.washington.edu/acelab/proj/dollar/)
and original [UCI Pendigits](https://archive.ics.uci.edu/dataset/81) UNIPEN files,
not the reduced eight-point feature tables. Their byte hashes, every record ID,
stroke counts, source axes and support flags are in the committed
[manifests](../results/recognition/manifests). Raw coordinates are downloaded
locally and are not redistributed. No private code, templates or old score is
used as a source or oracle.

| Milestone | Commit |
|---|---|
| Dataset importer | `14129e5332596707e3bdea7c1e6f22e4286a7b18` |
| Scoring implementation and protocol | `3f13fefd479fc6c4a7eccdb3c97e70fd79e29647` |
| Configuration and all bank/query IDs frozen before scoring | `238034882788b633409625b77c1dd1cc66c7cf80` |
| Application timing source and consumer | `97214c001b6d3526a6a3afc32d0b5ce39e5e1539` |

The $1 importer verified 5280 records: 480 pilot records from one person and
4800 main records from ten other people. Pilot repetitions 1–5 supplied templates;
6–10 supplied development queries. Main evaluation leaves one writer out,
selects three templates from different other writers per class (48 total),
and repeats the same banks for every method using three predefined seeds.
There are 14,400 query-bank trials, but only ten held-out writer blocks. Training
banks overlap between folds; the writer-block bootstrap is conditional on this
frozen policy and these banks.

Pendigits has 7494 training and 3498 official test records, with source-documented
disjoint train/test writers. Individual writer IDs are unavailable. Development
uses a stratified 80/20 record split of the supported training records, not a
writer-independent internal split. Five templates per digit (50 total) are
selected from training for each seed. All methods see identical banks.

Shared preparation uses uniform unit-bounds normalization and 64 arc-length
samples, preserving aspect ratio, open traversal order and orientation. RMS and
area use the same samples and transformation. Area is NonZero endpoint-bridged
fill at precision 6 in normalized coordinates. BoundsAreaRatio and region IoU do
not enter recognition. No reflection, reversal or additional fitted scale is used.

Development selected no rotation on either dataset. Gesture RMS and the 25%-area
combination both scored 720/720 pilot trials; the predefined tie rule chose area
weight zero. Digit development selected weight .25 and DTW window 63. Protractor
uses its native 16-point, orientation-sensitive vectorization on both datasets,
following the [official pseudocode](https://depts.washington.edu/acelab/proj/dollar/protractor.pdf),
including the angular domain of `atan(b/a)`. The complete candidate results and
scales remain in [development evidence](../results/recognition/development).
The protocol differs from the original recognizer papers; their reported accuracy
percentages are not direct comparators for this experiment.

## Held-out quality

Percentages below aggregate the three predetermined banks. Failures remain in
denominators. There were zero runtime/preparation failures on supported inputs.
The column denominators are respectively 14,400, 8232 and 10,494 query-bank
trials; table headings show unique input records, not independent repeated seeds.

| Method | $1, all 4800 records | Pendigits, supported 2744 | Pendigits, all 3498 |
|---|---:|---:|---:|
| RMS | 97.146% | 84.329% | 66.152% |
| Area | 85.319% | 72.631% | 56.975% |
| Frozen combination | 97.146% | 85.435% | 67.019% |
| Native Protractor | 94.500% | 83.005% | 65.113% |
| DTW | Not included | 89.043% | 69.849% |

On gestures, area fixes 234 RMS errors but introduces 1937. The combined score
produces exactly the RMS predictions. Its primary writer-mean paired difference
is 0 percentage points and the predefined 10,000-resample writer-block bootstrap
interval is [0,0]. That degenerate interval describes two identical frozen
decisions; it is **not** evidence that area and RMS are equivalent generally.
All ten writer results and speed/class confusions are published in the
[quality analysis](../results/recognition/analysis/summary.md).

On supported digits, combination fixes 229 RMS errors and introduces 138, a net
91 additional correct query-bank trials: +1.105 percentage points. Its gain is
positive for each seed (+0.510, +0.328 and +2.478 points), but bank variation
remains appreciable. The change over
all official test records is +0.867 points. DTW exceeds combination by 3.608
points on the supported subset. These are descriptive paired results; no writer
confidence interval is claimed without individual writer IDs.

### Coverage is part of the result

754 official test digits have multiple strokes and are explicitly unsupported,
giving 78.445% input coverage. They are retained as unsupported, not joined with
invented segments and not treated as classified failures. The last accuracy
column counts only correct predictions divided by every official record.

| Digit | Supported / official test records | Coverage |
|---|---:|---:|
| 0 | 361 / 363 | 99.45% |
| 1 | 350 / 364 | 96.15% |
| 2 | 362 / 364 | 99.45% |
| 3 | 333 / 336 | 99.11% |
| 4 | 181 / 364 | 49.73% |
| 5 | 82 / 335 | 24.48% |
| 6 | 335 / 336 | 99.70% |
| 7 | 72 / 364 | 19.78% |
| 8 | 334 / 336 | 99.40% |
| 9 | 334 / 336 | 99.40% |

The supported subset is strongly class-dependent. Its accuracy must not be
presented as unrestricted digit recognition or raster OCR accuracy.

## Application cost

The measurement unit is one raw query against a complete prepared 48/50-template
bank, including query conversion/preparation, scoring and top-three class result
allocation. Template construction/retained memory and scoring-only support
timings are separate. HTML rendering and optional contours are outside latency.
The replay CLI uses the same scorers but prepares its bank on each invocation;
these steady-state measurements represent the reusable in-process bank loop.
Bank construction is measured before each case's warmup; only first use of a
code path can include its JIT cost. Later cases in the process are not separate
cold-runtime starts. Retained bytes are incremental live managed memory after
full GC with raw dataset records already resident, not total process memory.

Three fresh processes per variant rotate uncached scalar, cached scalar and
cached SIMD order. Every case warms for at least three seconds and 100 complete
development queries. Measurements retain default tiering and run sequentially,
with builds/tests/profilers idle. Gesture timing covers writer s02 under all three
seeds; digit timing covers every supported official test query under each seed.
This timing sample is not all ten gesture writers. GC collection counts are
recorded; GC pause durations are not measured.

Measured on Windows 10.0.26200 x64, .NET SDK 10.0.401 / runtime 10.0.12,
Intel64 Family 6 Model 158, with 128/256-bit hardware vectors available. Each cell
below is the median of nine case statistics (three banks × three process runs),
not a pooled query percentile or a confidence interval. Times are microseconds;
allocations include result creation. The full minimum/maximum ranges, construction
costs, raw queries and hashes are in the
[performance analysis](../results/recognition/performance-summary/summary.md).

| Dataset / method | Cached scalar p50 | Cached SIMD p50 | SIMD p95 | Allocated bytes/query |
|---|---:|---:|---:|---:|
| $1 RMS | 11.1 | 10.9 | 13.5 | 11,472 |
| $1 area | 1247.8 | 1248.7 | 1461.2 | 1,731,856 |
| $1 combination (weight 0) | 11.2 | 11.0 | 13.5 | 11,472 |
| $1 Protractor | 7.4 | 7.4 | 9.0 | 6552 |
| Digit RMS | 9.4 | 9.4 | 11.4 | 7216 |
| Digit area | 1195.8 | 1187.3 | 1417.6 | 1,761,248 |
| Digit combination | 1220.1 | 1178.2 | 1417.7 | 1,762,336 |
| Digit Protractor | 6.5 | 6.4 | 7.6 | 4352 |
| Digit DTW | 1357.9 | 1357.7 | 1431.5 | 7304 |

Caching explains the large improvement: scalar RMS falls from 196.7 to 11.1 µs
for gestures and from 129.1 to 9.4 µs for digits, about 18× and 14× respectively.
Those are comparisons with deliberately uncached template preparation, not new
algorithmic speedups over another prepared recognizer. For cached scalar versus
SIMD, median shifts are small and mixed: gesture area is slightly slower, digit
RMS unchanged, and digit combination about 3.4% faster. Several ranges overlap.
There is no broad additional application-speed claim from SIMD here. Explicit
SIMD is in affine array transforms; RMS accumulation, DTW and Clipper's topology
processing are not newly vectorized by this experiment.

The digit combination buys +1.105 supported accuracy points over RMS at roughly
125× its latency and 244× its allocation volume. DTW offers another +3.608 points
over combination at about 15% more p50 time and much lower allocation volume.
These tradeoffs favor RMS for a very cheap comparison and DTW when this digit
accuracy/cost balance is acceptable. Area is not justified as the default scorer
or as a cheaper candidate filter by these measurements; no filtering experiment
was added after seeing the test results.

Across cached variants, median incremental retained banks are approximately
126–169 KB for shared preparation and 16.6–17.3 KB for native Protractor. Treat
these as noisy process-GC snapshots, not precise object-size guarantees: one
Protractor observation is **−4,428,760 bytes** because other managed lifetimes
changed between collections. The raw negative value is preserved. Per-query
allocation counters are a separate measurement and agree between cached scalar
and SIMD. Median cached-SIMD full passes are 5.45 ms for 480 gesture RMS queries,
605.4 ms for gesture area, 27.2 ms for 2744 digit RMS queries, 3289.9 ms for digit
combination and 3766.2 ms for DTW. These pass times include measurement bookkeeping.

All **243 cases and 422,280 timed queries** retain exactly the quality run's
winning class, template, status and binary64 score. No faster variant changes the
answer or drops failed inputs. Timing binaries were built immediately before
the timing-source commit; measured source content matches that commit, and exact
DLL hashes identify the binaries separately from generated Git/build metadata.

## Numerical agreement and checks

The complete scalar and accelerated evaluation produced **110,070 identical
prediction rows**, including unsupported outcomes. All 98,760 successful rows
have bitwise-identical exported score, RMS, area and margin fields, with unchanged
winning template, class and rotation. See the
[numerical audit](../results/recognition/simd-comparison/summary.md), including
ties and smallest positive class margins. This audits exported winners/diagnostics,
not every unexported candidate score. Analytical engine checks also exercise
nonzero transforms, ownership and one-ULP ranking differences.

Local validation includes 3584 existing/core geometry checks in five assembly/ISA
modes; 147 engine, 70 split/pipeline and 93 consumer checks in four runtime modes;
13 dataset parser checks and 25 analysis checks. The original documented
2^53 counterexample and clipping-grid limitations remain visible and unchanged.
Malformed arbitrary datasets can still stop evaluation on scorer exceptions;
the analyzer rejects incomplete prediction grids. Current pinned datasets had
no such exceptions or shared-preparation failures.

## Use and reproduce

Two deterministic replay examples from seed 1729 illustrate both directions of
the tradeoff: `pendigits/pendigits-orig.tes/000118` is a 1 predicted as 2 by RMS
and correctly as 1 by combination; `pendigits/pendigits-orig.tes/000003` is a 9
correct under RMS but predicted as 5 by combination. Complete winning-template
IDs and all five scores are in [replay cases](../results/recognition/analysis/replay-cases.json).
These are the first canonical IDs meeting each condition, not selected visual
showpieces. Pass either ID to the consumer's `--sample` with `--seed 1729`.

Run the [StrokeTemplates consumer](../examples/StrokeTemplates/README.md) for a
fresh synthetic demo, JSON import/export or exact frozen sample replay. It shows
three distinct labels, actual transformed paths, separate RMS and area, and
optional filled difference contours. Native-baseline overlays are explicitly
separate shared-preparation diagnostics, not native Protractor/DTW correspondence.

Commands, file hashes and source revisions are in the
[evidence README](../results/recognition/README.md). No external raw coordinates
are needed for the synthetic consumer checks or CI. Core runtime dependencies
and targets remain unchanged: Clipper2 2.0.0, .NET Standard 2.0 and .NET 10.

The original [LIP/GenLIP stability finding](report.md) and prior
[allocation/SIMD measurements](optimization-evaluation.md) remain separate
evidence. This recognition study does not establish scientific novelty, a
universal trajectory distance, maximum-deviation guarantees, or an improvement
to Clipper's polygon engine. No extra tuning round, candidate-filter experiment
or dataset was added to obtain an area win.
