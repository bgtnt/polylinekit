# Evaluate predicted polygons against reference annotations

This example measures intersection and IoU between building predictions and
reference polygons in image coordinates. It reproduces the provided six-image
Solaris/SpaceNet 2 evaluation sample and emits per-building scores, matches,
per-image TP/FP/FN and pair diagnostics. It trains no model.

## Run

From the repository root, with .NET 10 and Python 3.9+ (standard library only):

```sh
python examples/PolygonOverlapEvaluation/data/prepare-data.py
dotnet restore examples/PolygonOverlapEvaluation --locked-mode
dotnet run --project examples/PolygonOverlapEvaluation -c Release -- verify-reference
dotnet run --project examples/PolygonOverlapEvaluation -c Release --no-build -- run-example
```

The default output is `artifacts/polygon-overlap/example.json`. To select a
backend or another JSON input:

```sh
dotnet run --project examples/PolygonOverlapEvaluation -c Release --no-build -- run-example examples/PolygonOverlapEvaluation/data/generated/solaris.json core artifacts/polygon-overlap/core.json
```

## Review contours visually

Generate a standalone local HTML report from the same evaluation:

```sh
dotnet run --project examples/PolygonOverlapEvaluation -c Release --no-build -- review-report
```

Open `artifacts/polygon-overlap/review.html` in a browser. The file embeds its
data, JavaScript and SVG; it needs no server, internet connection or new package.
Choose an image, filter the tables to false positives/false negatives or excluded
inputs, then select a contour or row. A prediction shows its actual best available
reference, accepted-match status, intersection, IoU, missing/excess area and any
fallback. A reference shows its retained evaluation score and accepted prediction.
The detail panel preserves full IoU precision so that scores on either side of
the strict 0.5 threshold remain distinguishable; table values are rounded.

These are original image coordinates with Y down. The display fits the contours
without changing their geometry. No image raster or computed difference contours
are supplied. Empty sentinels and filtered inputs remain visible in the tables.
The report is a read-only review example, not an annotation editor.

To review your own inputs, use the same [JSON schema](data/README.md#json-contract)
with `Expected` omitted. Use pixel XY coordinates; an explicit `CoordinateSystem`
other than `ImagePixelXY` is rejected by `review-report`.

```sh
dotnet run --project examples/PolygonOverlapEvaluation -c Release --no-build -- review-report my-polygons.json core artifacts/my-review.html
```

The existing confidence/area/IoU matching policy still applies. This command does
not claim that a custom input passed the pinned Solaris reference check. Its
report embeds the input contours and identifiers, so share it only with recipients
who should receive that data. Report generation is outside the frozen benchmark;
the recorded timing results remain measurements of revision `2019112`.

## Select an evaluation backend

Choices are `core`, `clipper`, `nts`, and `convex`. The
[input schema, downloader and attribution](data/README.md) explain the original
data. No CSV/WKT parser is added to Core. Custom inputs use the same schema;
`Expected` can be omitted for ordinary evaluation. `verify-reference` requires
the exact pinned Solaris fixture and all of its expected keys.

## What the results mean

For a prediction P and truth G, using cached own areas:

```csharp
double intersection = PolylineArea.IntersectionArea(prediction, truth, PathFillRule.NonZero);
double union = predictionArea + truthArea - intersection;
double iou = intersection / union;
double missing = truthArea - intersection;
double excess = predictionArea - intersection;
```

Coordinates remain in the original image, and areas are **square pixels**.
Alignment, bounds normalization and resampling would change the localization
being evaluated. Missing/excess area are additional annotation diagnostics, not
original Solaris metrics.

Solaris processes predictions in descending confidence order, against available
truths. Only the winning truth retains the current score, even below the match
threshold. A match consumes that truth only when **IoU >0.5**. Thus the expected
per-building CSV is not a table of independent maximum pair IoUs.

The truth area filter is **>=20**; prediction area filter is **>20**. Output keeps
all 172 original truth keys, including tiny filtered polygons and the empty-image
sentinel. A zero denominator is an error, not a fabricated zero IoU; eligible
valid polygons have positive areas. Confidence ties use original source order
as an explicit deterministic extension. The historic pandas sort did not
specify stable ties; this fixture has no ties within an image. Full details and
source links are in [the fixed protocol](PROTOCOL.md).

## Correctness and coverage

All four C# backends reproduce every one of the **172 expected scores** with
absolute error below the original **1e-9** criterion. Core's maximum error is
`1.33e-15`; Clipper's is `5.03e-12` on its selected grid. Match decisions agree:
**87 TP, 57 FP, 82 FN**. The verifier checks exact key sets/counts, per-pair areas,
symmetry, bounds, matching decisions, and focused geometry/protocol controls.

There are 145 prediction rows and 172 truth rows across six images. After the
original filters, 144 predictions and 169 truths are eligible. All 315 nonempty
polygons are valid single shells; no repair, holes or multipart fallback is
needed by Core, Clipper or NTS. The shared bounds check rejects 3,931 comparisons.
The actual matching run visits **205 bounds candidates**, of which **162 have
positive intersection** and 43 are disjoint despite overlapping bounds.

All predictions are convex; 71 truths are convex and 100 concave. Only **49 of
the 205 visited pairs** have both operands convex. The conservative convex
backend uses Sutherland–Hodgman for those pairs and explicitly falls back to
NTS for the other **156**. Its full-run cost must not be described as a pure
convex-kernel result. This does not rule out a separate algorithm exploiting
one convex operand; that is not the method measured here.

## Dependencies and unsupported inputs

This executable depends on Core, **Clipper2 2.0.0** and **NetTopologySuite 2.6.0**.
NTS supplies common input validation and cached own areas, an independent
intersection baseline and explicit fallback. Therefore this evaluator is not
dependency-free. The PolylineKit Core package itself remains dependency-free.
For already trusted simple rings, own areas can instead use shoelace.

Valid holes and multipart inputs use explicit NTS fallback in the narrower
backends. Invalid geometry is rejected with its role/image/building key.
Solaris's GEOS `buffer(0)` prediction repair is not silently approximated by
winding fill or NTS repair. Such a custom input therefore does not receive a
complete score report. All data in the pinned fixture are supported.

Clipper uses a preselected **1e10 scale**, rounding to a 1e-10 pixel grid; the
other backends keep original doubles. Grid quantization and floating-point
area errors are distinct. Values are not clamped to conceal discrepancies.
Prepared inputs are immutable during use; backend instances own reusable
scratch buffers and are not thread-safe.

## Measure

Commit source, build Release, and copy the output to an ignored frozen directory
before measuring. Use a new results directory:

```powershell
$revision = git rev-parse HEAD
dotnet build examples/PolygonOverlapEvaluation -c Release --no-restore
$frozen = "artifacts/polygon-overlap-bin-$($revision.Substring(0,7))"
New-Item -ItemType Directory -Path $frozen
Copy-Item examples/PolygonOverlapEvaluation/bin/Release/net10.0/* $frozen -Recurse
./examples/PolygonOverlapEvaluation/run.ps1 -Runner "$frozen/PolygonOverlapEvaluation.dll" -InputFile examples/PolygonOverlapEvaluation/data/generated/solaris.json -Output artifacts/polygon-overlap-measured -Revision $revision
python examples/PolygonOverlapEvaluation/summarize.py artifacts/polygon-overlap-measured
```

The three sequential processes rotate backend order. Raw elapsed ticks,
allocations, output checksums, fixture/protocol/binary hashes and process order
remain under `artifacts/`. Kernel batches reuse prepared inputs but recompute
every intersection. Complete-evaluator batches read the JSON, validate and
prepare fresh inputs, reset matching, and create output rows; final JSON
serialization and disk writing are excluded equally. Preparation is measured
separately. These are warmed repeats of the same small fixture, not cold-start
or production-population estimates. CI runs correctness and startup only.

See [the evaluation report](RESULTS.md) for the recorded source, times,
allocations, raw-evidence location and practical conclusion.
