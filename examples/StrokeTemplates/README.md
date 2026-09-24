# StrokeTemplates

A .NET 10 console consumer for replaying one supported open stroke against a
frozen template bank. It calls the same `RecognitionEngine` methods as the
evaluation and writes a self-contained local HTML/SVG inspection page.
It has project references and no separately published package. An optional local
drawing page calls this same CLI; it does not implement another recognizer.

Run the following commands from the repository root.

## Draw your own stroke locally

After the normal [dataset import](../../scripts/datasets/README.md), build the
consumer and start the optional adapter with Python 3.10+ (standard library only;
Python is already used by the development tools):

```powershell
dotnet restore examples/StrokeTemplates/StrokeTemplates.csproj --locked-mode
dotnet build examples/StrokeTemplates/StrokeTemplates.csproj -c Release --no-restore
python examples/StrokeTemplates/serve.py --data artifacts/recognition/data --freeze results/recognition/frozen.json
```

Open the printed `http://127.0.0.1:<port>/` URL. Draw one continuous stroke,
choose a scorer and select **Compare**. The page shows the existing consumer's
top-three result with actual overlays and area contours. **Save query JSON**
exports the same schema used by `--query`. Clear before drawing again: a second
pen-down is rejected, and interrupted input is discarded instead of joining paths.
Mouse, pen and touch use pointer events. Consecutive identical positions are
omitted; other samples are neither smoothed nor resampled by the page. The C#
consumer performs its existing preparation. Captured elapsed timestamps are
optional metadata and do not enter the scores.

The default is Pendigits, seed 1729, 50 templates. `--dataset dollar` selects the
first frozen $1 bank (held-out writer s02), seed 1729, 48 templates and its included
scorers. External queries use `split: "external"`, an empty label and a fresh ID;
they are not additional held-out measurements. These are closed-set matchers:
an unfamiliar drawing still receives the nearest known labels, not an unknown
classification or confidence estimate.

Canvas coordinates use positive Y down. **Invert Y** explicitly exports/compares
`y = canvasHeight - capturedY`; the captured points remain unchanged. Select the
axis convention appropriate to the template source. The adapter does not infer
an axis convention from class labels or choose whichever reflection scores best.
The inspection page displays both paths with positive Y down, as documented below.

Each comparison saves `query.json` and `result.html` under a new directory inside
`artifacts/stroke-input/` (override with `--output`). Files are retained when the
server stops. The server binds only to loopback, validates same-origin JSON
requests, limits input to 1 MiB/10,000 points and calls the prebuilt consumer with
a 30-second timeout. Each request starts a process and prepares the bank again;
this interface is for inspection, not latency measurement. No runtime package is
added to PolylineKit. Stop with Ctrl+C. A fixed port can be selected with `--port`.

Adapter checks (no datasets or built consumer needed):

```powershell
python -m unittest discover -s examples/StrokeTemplates -p 'test_*.py'
```

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

HTML and export outputs must have distinct full paths and must not reuse the
query, frozen configuration or selected dataset JSONL input path.

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
