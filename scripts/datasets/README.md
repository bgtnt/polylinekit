# Recognition dataset import

Python 3.10+ and its standard library are sufficient for download, XML/UNIPEN
parsing, manifest generation, and the synthetic checks. Pendigits additionally
uses **7-Zip 26.02**, a development-only command-line decompressor for Unix
compress `.Z`. Nothing is installed automatically or added to the C# runtime.
An existing Windows installation is detected; on other systems pass the 26.02
`7zz` executable using `--seven-zip`. The imported manifest records its version.
The parser does not implement a compression codec.

From the repository root:

```text
python scripts/datasets/import_datasets.py check
python scripts/datasets/import_datasets.py import --seven-zip "/path/to/7zz"
python scripts/datasets/import_datasets.py import --offline --seven-zip "/path/to/7zz" --parser-revision <source-commit>
```

On Windows `--seven-zip "C:/Program Files/7-Zip/7z.exe"` selects the existing
installation explicitly. `fetch` downloads/verifies archives without parsing;
`--dataset dollar` or `--dataset pendigits` selects one. `--data-root` changes the
output directory; its default is ignored `artifacts/recognition/data/`.

Outputs are `dollar.jsonl`, `pendigits.jsonl`, and matching `*-manifest.json` files.
The exact JSONL byte hash, parser file hash, source revision label, archive pins,
counts, class/split coverage, nearest-rank point-count distributions, and every
sample ID are recorded. A hash/size/count discrepancy fails the import rather
than silently refreshing a pin. Commit the parser before the frozen import and
pass that commit as `--parser-revision`; `working-tree` explicitly identifies an
uncommitted development import. Template-bank selections belong to the evaluation
protocol, so the importer leaves their manifest list empty with an explanation.

## Sources and format

- [$1 official gesture logs](https://depts.washington.edu/acelab/proj/dollar/),
  [XML archive](https://depts.washington.edu/acelab/proj/dollar/xml.zip): 4,789,784
  bytes, SHA-256 `c5d81282e46571d813fdcab06d67ef96c848c736922deaba5189a9658b1e43f6`.
  All 5,280 files are parsed: 480 pilot (`s01`) and 4,800 main (`s02`–`s11`).
  Subject, speed, repetition, file name and declared point count must agree.
- [UCI Pendigits](https://archive.ics.uci.edu/dataset/81),
  [DOI](https://doi.org/10.24432/C5MG6K),
  [archive](https://archive.ics.uci.edu/static/public/81/pen+based+recognition+of+handwritten+digits.zip):
  1,668,118 bytes, SHA-256
  `1e02bea023613c2b11c9492f6f34caf975420455934f3527d270cee9a1f03b64`.
  Only the original `pendigits-orig.tra.Z` and `pendigits-orig.tes.Z` are parsed;
  the eight-point feature tables are not used. The included `pendigits-orig.names`
  is retained under ignored `raw/` and hashed in the manifest.

One JSON object per UTF-8 line:

```text
sampleId: string                     stable archive member plus source segment ID
dataset: "dollar" | "pendigits"
split: "pilot" | "main" | "train" | "test"
label: string
strokes: array of arrays of {x: number, y: number, t?: number}
supported: boolean
exclusionReason: string | null
writerId?: string                    verified $1 Subject, e.g. s01
speed?: string                       $1 slow/medium/fast
repetition?: integer                 $1 1..10
sourceMember: string
sourceMetadata?: object              original $1 XML attributes
sourceSegment?: string               Pendigits UNIPEN stroke index/range
sourceComment?: array of strings     opaque Pendigits COMMENT payloads
sourceDt?: array of numbers          original Pendigits DT payloads
```

Records are sorted by canonical `sampleId` using ordinal character order. Example
ID forms are `dollar/xml_logs/s01 (pilot)/fast/arrow01.xml` and
`pendigits/pendigits-orig.tra/000006-000007`. Pendigits IDs are source **stroke
ranges**, not guessed writer identifiers or line numbers.

XY, duplicate points, order and stroke boundaries are preserved verbatim as
numeric values. There is no centering, resampling, axis swap, reflection or
label-dependent change. $1 retains screen-style X/Y and original point T values
without rebasing. Pendigits retains the original first/second coordinate columns;
its supplied original-data description calls them tablet x/y and does not provide
the referenced `dene.doc` coordinate definition. We therefore do not infer a
display orientation from digit labels. A viewer must declare its own axis display
policy. DT is retained as source metadata; no per-point timestamp is invented.

Only one-stroke records with at least two distinct XY positions are supported.
Every unsupported record is retained. Expected Pendigits counts are 7,494/3,498
total training/test and 5,564/2,744 supported; stroke ranges and all ten original
class totals are independently checked. Separate pen-down paths are never joined.

No Pendigits per-record writer/session mapping is documented in this archive.
COMMENT numbers remain opaque. The source description states that the official
training/test sets use different writers; it does not justify treating a new
random training-record split as writer-independent. $1 dates and times are raw
provenance rather than asserted session IDs.

Downloads and converted coordinates stay under ignored `artifacts/`. Only the
coordinate-free manifests are candidates for public results; raw-path
redistribution requires a separate permission/license review. Importing all
records checks format/coverage and does not evaluate held-out recognition labels.
