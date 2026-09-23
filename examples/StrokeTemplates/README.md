# StrokeTemplates

A .NET 10 console consumer for replaying one supported open stroke against a
frozen template bank. It calls the same `RecognitionEngine` methods as the
evaluation and writes a self-contained local HTML/SVG inspection page.
It has project references, no separately published package or drawing UI.

Run the following commands from the repository root.

## Fresh synthetic demonstration

```powershell
dotnet run --project examples/StrokeTemplates -c Release -- --demo artifacts/consumer/demo.html --contours --export artifacts/consumer/demo-query.json
dotnet run --project examples/StrokeTemplates -c Release -- --demo artifacts/consumer/demo-area.html --method area
dotnet run --project examples/StrokeTemplates -c Release -- --check
```

The demonstration uses newly generated wave, arch and corner paths. It checks
input-to-ranking wiring and exact template-ID ties; it is not an accuracy study.
Its exported record has `dataset: "synthetic"` and does not belong to an official
dataset or its frozen bank.

## Replay a frozen dataset sample

Import the datasets and obtain the frozen protocol as described in the repository
[recognition report](../../docs/recognition-evaluation.md). Downloaded and converted
paths remain local under ignored `artifacts/`; they are not included in this example.

```powershell
dotnet run --project examples/StrokeTemplates -c Release -- --data artifacts/recognition/data --freeze results/recognition/frozen.json --sample "<canonical sampleId>" --seed 1729 --method rms --output artifacts/consumer/replay.html --contours --export artifacts/consumer/query.json
```

Use a canonical query ID listed in the frozen bank. The consumer finds the unique
matching bank for that ID and seed, verifies the selected dataset's file hash and
bank invariants, and uses its frozen settings. Optional `--dataset dollar` or
`--dataset pendigits` makes the dataset explicit. Available seeds are those in the
freeze. The default method is `rms`; select other methods explicitly:

| Method | Ranking value |
| --- | --- |
| `rms` | RMS on the shared 64-point representation. |
| `area` | Raw endpoint-bridged area in normalized coordinates, under the same RMS-selected tilt policy. |
| `combined` | Frozen weighted RMS/area combination and development-derived scales. |
| `protractor` | Native 16-point optimal-cosine distance and frozen orientation policy. |
| `dtw` | Native 64-point DTW cost and frozen window; included only in the digit protocol. |

The page shows the best template from each of the top three distinct labels.
Ascending score and then ordinal template ID determine the ordering. Scores are
distances, not confidence percentages. The query label, when known, is displayed
as provenance and never used to choose a template.

## Import or export a standalone query

`--export` writes the selected query using the shared `StrokeRecord` JSON schema,
retaining its recorded metadata, point order and optional timestamps. A standalone
query can use `--query path.json` instead of `--sample`:

```powershell
dotnet run --project examples/StrokeTemplates -c Release -- --data artifacts/recognition/data --freeze results/recognition/frozen.json --query my-query.json --seed 1729 --method rms --output artifacts/consumer/imported.html
```

Minimal fresh-input example:

```json
{
  "sampleId": "user-example-001",
  "dataset": "pendigits",
  "split": "external",
  "label": "",
  "supported": true,
  "strokes": [[
    { "x": 0, "y": 0, "t": 0 },
    { "x": 1, "y": 2, "t": 20 },
    { "x": 2, "y": 0, "t": 40 }
  ]]
}
```

For an external query, `dataset` selects the frozen bank family; it does not make
the input an official dataset sample. Use `split: "external"` and omit unknown
writer/session metadata. `t` is optional and does not enter the geometric scores.
There must be exactly one supported stroke with finite coordinates and nonzero
extent. Multiple strokes are rejected rather than connected artificially.

Standalone queries select the first frozen bank for their dataset/seed, ordered
by held-out writer; the selected bank is printed and recorded in the page. A
query ID that also occurs among that bank's templates is rejected. For a proper
held-out replay of a known dataset record, use `--sample` so its designated bank
is selected. Standalone replay is not an additional held-out evaluation.

## Interpreting the inspection page

Red is the query, blue the template. Solid paths contain the actual 64 samples
used for RMS/area diagnostics. Faint paths retain the original vertices; the
query map composes its bounds normalization with the selected rotation. Filled
and hollow endpoint markers indicate traversal start and end. The page prints
the applied affine coefficients and frozen settings. `--contours` adds resolved
endpoint-bridged NonZero area regions.

For Protractor and DTW, the native score alone determines ranking. The displayed
RMS/area overlay is explicitly a separate diagnostic, not a visualization of
Protractor's native rotation or DTW's warping correspondence. Neither IoU nor
bounds-area ratio ranks these open strokes.

The CLI prepares a bank for each replay and generates extra diagnostics for the
display. Its total runtime is **not** the application's optimized warm-query
latency: the performance harness reuses prepared banks and calls the same engine
without HTML/diagnostic work. `POLYLINEKIT_FORCE_SCALAR=1` selects the existing
scalar core switch for checks and replay.

`--check` uses fresh synthetic fixtures and temporary files to verify scorer
wiring, ranking/ties, transforms, serialization, hash/bank validation, input
rejections and HTML escaping. It does not read or score held-out datasets.
Exported real paths remain local; check their source permissions before sharing
them or committing generated pages containing them.
