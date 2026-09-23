# Frozen recognition evidence

This directory contains identifiers, aggregate metadata and derived measurements.
Raw archives and coordinate records are downloaded locally into ignored
`artifacts/recognition/data/`; they are not redistributed here.

- `manifests/`: source pins, parser revision/hash, every input ID, support and class counts.
- `development/`: development-only configurations, candidate settings and selected IDs.
- `frozen.json`: exact configurations and 33 final template/query banks, committed
  at `238034882788b633409625b77c1dd1cc66c7cf80` **before** held-out scoring.
- `quality/`: one gzip JSONL prediction per query, seed and method, including unsupported records.
- `scalar/`: the same frozen evaluation with forced scalar transforms.
- `analysis/` and `simd-comparison/`: deterministic quality and numerical comparisons.
- `performance/`: three independent processes per application variant, including every timed query.

The scoring implementation used for development and quality is
`3f13fefd479fc6c4a7eccdb3c97e70fd79e29647`; the importer revision is
`14129e5332596707e3bdea7c1e6f22e4286a7b18`. Each performance file records its own
source revision, assembly hashes and runtime configuration. Git attributes preserve
evidence bytes because the manifests include file SHA-256 values.

The [protocol](../../docs/recognition-protocol.md) defines the primary comparison,
development policy, exact tie rule, unsupported denominators and timing scope.
The final [evaluation report](../../docs/recognition-evaluation.md) interprets the evidence.

## Reproduce

Use .NET 10, Python 3.10+ and the development-only 7-Zip 26.02 decoder described
in [dataset import instructions](../../scripts/datasets/README.md). From the root:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
python scripts/datasets/import_datasets.py import --seven-zip 'C:/Program Files/7-Zip/7z.exe' --parser-revision 14129e5332596707e3bdea7c1e6f22e4286a7b18
$runner = 'experiments/PolylineKit.Recognition/bin/Release/net10.0/PolylineKit.Recognition.dll'
$revision = git rev-parse HEAD
dotnet $runner evaluate artifacts/recognition/data results/recognition/frozen.json artifacts/reproduction/quality $revision
$env:POLYLINEKIT_FORCE_SCALAR = '1'
dotnet $runner evaluate artifacts/recognition/data results/recognition/frozen.json artifacts/reproduction/scalar $revision
Remove-Item Env:POLYLINEKIT_FORCE_SCALAR
python scripts/recognition/analyze.py analyze --data artifacts/recognition/data --results artifacts/reproduction/quality --output artifacts/reproduction/analysis
python scripts/recognition/analyze.py compare --left artifacts/reproduction/quality --right artifacts/reproduction/scalar --output artifacts/reproduction/scalar-comparison
pwsh -File scripts/recognition/benchmark.ps1 -OutputDirectory artifacts/reproduction/performance
python scripts/recognition/analyze.py performance --results artifacts/reproduction/performance --quality artifacts/reproduction/quality --output artifacts/reproduction/performance-summary
```

Performance requires a clean committed tree and a Release build, and runs
sequentially. Keep other builds, tests and profilers idle during measurement.
It retains normal runtime tiering. This differs deliberately from the previous
synthetic microbenchmarks, whose tiering policy is documented separately.

To reproduce development selection separately, use `develop <data> <output>
<source-revision>` and then `freeze <data> <development-output> <freeze-path>
<source-revision>`. Do not replace the committed freeze while reviewing held-out
results. Unsupported Pendigits records remain in the official-test denominator;
seeds represent repeated template banks, not additional independent writers.
