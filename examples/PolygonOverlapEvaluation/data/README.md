# Pinned Solaris / SpaceNet 2 sample

These are the six-image evaluator fixtures supplied by
[CosmiQ Works Solaris](https://github.com/CosmiQ/solaris/tree/5315390942e05e919555088361bd3df42d4f5a18),
commit `5315390942e05e919555088361bd3df42d4f5a18`. They are an existing sample,
not a new independent production dataset. The source includes Las Vegas and
Khartoum building predictions and ground truth derived from SpaceNet 2.

Run from the PolylineKit repository root, with Python 3.8+ and no extra packages:

```text
python examples/PolygonOverlapEvaluation/data/prepare-data.py
python examples/PolygonOverlapEvaluation/data/prepare-data.py --offline
```

The first command downloads only missing files from the exact pinned Git revision.
Both commands verify byte lengths and SHA-256 values from [manifest.json](manifest.json)
before conversion. A changed file fails; it is not silently overwritten. The second
command never uses the network. The converter runs focused WKT controls, including
nonzero and underflowing Z values, malformed input, empties, holes and multipart.
It produces ignored `generated/solaris.json` and `generated/inventory.json`.
The CSV-to-JSON step is one-time preparation, outside runtime benchmark intervals.

## Data and attribution

The manifest pins the three CSV files, `tests/test_eval/evaluator_test.py`,
`solaris/eval/base.py`, `solaris/eval/iou.py`, the repository README and LICENSE.
The exact original bytes remain under ignored `original/` paths for inspection.
Solaris credits **CosmiQ Works, an IQT Lab**, and its pinned
[code license](https://github.com/CosmiQ/solaris/blob/5315390942e05e919555088361bd3df42d4f5a18/LICENSE.txt)
is Apache License 2.0, copyright 2019. The
[SpaceNet Las Vegas source](https://spacenet.ai/las-vegas/) describes the underlying
building dataset. The pinned sample CSV files do not separately explain their
data-redistribution terms. This example therefore commits the downloader and
hashes, **not the original CSVs or converted coordinate fixture**. The PolylineKit
MIT license does not replace the source terms. The downloaded evaluator source
and license are retained as references; the preparation tool does not execute or
modify that Python evaluator.

## JSON contract

`SchemaVersion` is 1, `SourceCommit` identifies the upstream commit and
`CoordinateSystem` is `ImagePixelXY`. `Predictions` and `Truth` contain rows:

```json
{
  "SourceIndex": 0,
  "ImageId": "image-key",
  "BuildingId": 1,
  "Confidence": 0.75,
  "IsEmpty": false,
  "Polygons": [[[[0, 0], [1, 0], [0, 1], [0, 0]]]]
}
```

This small triangle illustrates the schema; it is not a source record. `Polygons`
has four array dimensions: polygon, ring, vertex, XY. The first ring is the shell;
subsequent rings would be holes. `Confidence` is null for truth. SourceIndex is the
zero-based original CSV data-row position, retained separately for each input.
ImageId is a string and BuildingId an integer. No rows are reordered. `Expected`
contains `{SourceIndex, ImageId, BuildingId, IoU}` for every original expected row.
The converter verifies exact equality of expected/truth key order and pixel WKT.

Only `PolygonWKT_Pix` is converted. XY decimal values become binary64 coordinates
and round-trip through JSON. Every original third-coordinate token must be exactly
zero **before** it is discarded, including values too small to survive conversion
to double. Closing vertices, repeated/collinear vertices, image positions,
confidence values, empty sentinels and all identifiers remain intact. No geometry
is filtered, normalized, simplified, repaired or aligned during preparation.

An empty row has `Polygons: []` and `IsEmpty: true`; it is not a point or missing
image. The original empty-image convention is ImageId `AOI_5_Khartoum_img463`,
BuildingId `-1`, prediction Confidence `1`, and expected IoU `0`.

## Frozen population inventory

| Property | Predictions | Truth |
|---|---:|---:|
| Images | 6 | 6 |
| CSV rows, including sentinel | 145 | 172 |
| Nonempty single-ring polygons | 144 | 171 |
| Empty sentinel rows | 1 | 1 |
| Simple convex contours, allowing collinear vertices | 144 | 71 |
| Simple concave contours | 0 | 100 |
| Nonsimple contours / holes / multipart | 0 / 0 / 0 | 0 / 0 / 0 |
| Supplied vertices, including closing vertices | 4,147 | 1,624 |
| Vertices per nonempty row, min / median / max | 12 / 27 / 103 | 4 / 8 / 58 |
| Original zero Z values checked and removed | 4,147 | 1,624 |
| Rows eligible under original area filtering | 144 | 169 |

There are 172 expected-score rows, including zero scores for filtered rows. Truth
filtering uses area **>= 20 pixel²**, prediction filtering uses area **> 20 pixel²**.
Besides empties, it removes truth BuildingIds `3` and `38` in
`AOI_5_Khartoum_img130` (areas approximately 3.94935 and 3.18855 pixel²). Every
prediction Confidence is unique within its image. Filtering and matching belong
to the runtime evaluator; the generated fixture preserves every row.

The inventory uses exact integer arithmetic on the parsed binary64 coordinates
to inspect ring intersections and turn signs. It is a source diagnostic, not a
replacement for the C# backend's validity/repair contract. The independent runtime
validation must still report unsupported/invalid input and any fallback. Full
per-row counts and diagnostics are in `generated/inventory.json`. Both generated
file hashes are frozen in the manifest; no generated timestamps or machine paths
affect them.
