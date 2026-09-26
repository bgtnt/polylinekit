# Territorial coverage: measured performance

**Intersection-only Core takes 24–26% less time than reusable prepared Clipper2
on this coverage workload.** All four unchanged acceptance cells pass the
Clipper/Core >=1.25 criterion. These are whole coverage traversals, not per-pair
microseconds or complete GIS application timings.

Measured source: [`2fe5f74`](https://github.com/bgtnt/polylinekit/commit/2fe5f74056590c7ce63cd7b34a6b0061e91f3fe7),
2026-09-26. This repeat uses the dependency-free Core with the 4 MiB retained
workspace cap. It measures a source build, not a packed alpha package. The later
documentation commit is not the measured binary.

## Coverage calculations

Each direction includes all 1,078 potential pairs from 98 county rings and 11
congressional district rings: the common bounds index admits 211 candidates and
rejects 867 pairs. The direction selects the coverage denominator. Every candidate
returns both intersection and coverage; no pair result is cached. The traversal
consumes a numeric digest without allocating an output matrix.

Times are medians of three independent process medians. Both warm indexed traversal
and preparation plus one traversal are required in both directions; no favorable
scope substitutes for a failed cell. The gate is an engineering criterion, not
a statistical significance test.

| Zone / scope | Core ms | Clipper ms | Clipper / Core | Less time |
|---|---:|---:|---:|---:|
| County / warm indexed | 9.760100 | 12.957250 | 1.328× | 24.67% |
| County / preparation + one | 9.920850 | 13.073800 | 1.318× | 24.12% |
| District / warm indexed | 9.412775 | 12.668950 | 1.346× | 25.70% |
| District / preparation + one | 9.580300 | 12.820000 | 1.338× | 25.27% |

All 90 raw intersection-only allocation samples in the three warm scopes were
**0 B per traversal**, in both directions. Reused Clipper allocated 625,088 B per
warm traversal. Preparation plus one table allocated 18,496 B (county zones) or
34,480 B (district zones) with Core, versus 1,634,000 B or 1,648,464 B with Clipper.
First use, retained storage, peak memory and native allocations are not included
in the zero warm-allocation claim. Fresh preparation is not a cold process:
correctness checks have already warmed JIT and Core's thread-local workspace.

## What is being compared

- `Winding-intersection-only` is the retained adapter name for Core's
  `WindingArea.IntersectionArea`, also exposed as `PolylineArea.IntersectionArea`.
- `Winding` is the general full-region comparison returning own/union/XOR areas
  as well as intersection; its unused work remains in the comparison below.
- `Clipper64-reused-data` uses Clipper2 **C# 2.0.0**, reusable quantized input
  vertices/local minima and a reused engine. Each pair still performs
  Clear/AddReuseableData, one Intersection and output-contour area summation.
  Its grid is 1e-6 metre; bounds and denominator use its quantized geometry.

The same NTS STRtree (fanout 10) selects candidates for every method. This is not
an index speedup. Core keeps the translated double inputs and a cached shoelace
denominator for the already validated simple zone. The common origin is the
center of the combined bounds; contours are not aligned individually. Clipper
produces contours while Core returns numbers. No C++ comparison is included.

## Every measured scope

Milliseconds per table or preparation; brackets contain the minimum and maximum
of the three process medians. The value after the semicolon is the maximum of the three median
managed-byte counts, matching the retained summarizer. Ranges are observed
variation, not confidence intervals. Eight-table sessions are divided by eight.

| Zone | Scope | Full-region Core ms [range]; B | Intersection-only Core ms [range]; B | Clipper ms [range]; B |
|---|---|---:|---:|---:|
| County | prepare-catalogues-and-index | 0.1482 [0.1478–0.1483]; 18512 | 0.1485 [0.1480–0.1485]; 18512 | 0.4133 [0.4116–0.4182]; 976856 |
| County | warm-candidate-geometry | 13.3194 [13.2813–13.4081]; 0 | 9.7277 [9.6802–9.7634]; 0 | 12.8770 [12.8652–12.9545]; 625088 |
| County | warm-linear-table | 13.3782 [13.3551–13.3978]; 0 | 9.7136 [9.6935–9.7371]; 0 | 12.9692 [12.9298–12.9839]; 625088 |
| County | warm-indexed-table | 13.4423 [13.4144–13.4960]; 0 | 9.7601 [9.7432–9.7731]; 0 | 12.9573 [12.9334–13.0202]; 625088 |
| County | prepare-plus-one-table | 13.5889 [13.5425–13.6793]; 18496 | 9.9208 [9.9067–9.9701]; 18496 | 13.0738 [13.0587–13.5152]; 1634000 |
| County | prepare-plus-eight-tables | 13.3942 [13.3639–13.4481]; 2312 | 9.7688 [9.7339–9.8326]; 2312 | 12.5869 [12.5858–12.6176]; 751202 |
| District | prepare-catalogues-and-index | 0.1740 [0.1737–0.1740]; 34496 | 0.1734 [0.1731–0.1741]; 34496 | 0.4391 [0.4385–0.4413]; 992840 |
| District | warm-candidate-geometry | 13.0584 [13.0345–13.0775]; 0 | 9.3416 [9.3105–9.4159]; 0 | 12.7613 [12.6702–12.7744]; 625088 |
| District | warm-linear-table | 13.0611 [12.9795–13.0717]; 0 | 9.3558 [9.3474–9.4067]; 0 | 12.6968 [12.6779–12.7853]; 625088 |
| District | warm-indexed-table | 13.0470 [13.0266–13.1527]; 0 | 9.4128 [9.3606–9.4270]; 0 | 12.6690 [12.6656–12.7575]; 625088 |
| District | prepare-plus-one-table | 13.2135 [13.1859–13.2692]; 34480 | 9.5803 [9.5634–9.6063]; 34480 | 12.8200 [12.7612–12.9286]; 1648464 |
| District | prepare-plus-eight-tables | 12.9976 [12.9968–13.0204]; 4310 | 9.3713 [9.3069–9.3753]; 4310 | 12.3174 [12.2793–12.3213]; 753010 |

## Accuracy and comparison with the earlier measurement

All **2,156 directional pairs** pass the unchanged 1 m² intersection and 1e-8
coverage disagreement budgets against NTS. Full-region and intersection-only
Core results agree bit-for-bit. The complete recorded accuracy records, input
hash, Clipper binary and NTS binary are unchanged from `f93cc25`.

Core's maximum intersection disagreement with NTS is 1.4305115e-6 m², and maximum
coverage disagreement is 1.4432899e-15. Clipper's corresponding maxima are
0.026165724 m² and 2.2365776e-11. This is independent implementation agreement,
not an exact oracle or a universal error bound. No tolerances or inputs changed.

The [earlier measurement](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/RegionCoverage/INTERSECTION-RESULTS.md)
reported 26–29% less time (1.35–1.41× ratios); this repeat reports 24–26%
(1.32–1.35×). Both pass the same four-cell criterion. This is not a paired causal
A/B experiment: timing changed for the unchanged Clipper binary too. The result
does not isolate the effect of the workspace cap or establish a performance
regression from it. The measured warm zero-allocation benefit remains present.

Both layers are related, generalized Census boundaries and may share segments.
This is the same previously observed workload, not new held-out data. The
single-ring contract excludes two source counties and three districts containing
holes or multiple components. It does not establish the same advantage on all
GIS inputs, geodesic areas or an external production deployment.

## Reproduce

SDK 10.0.401 / .NET 10.0.12, Windows build 26200, x64 Intel Core i9-9900K.
The unchanged runner uses 50 ms warmup, a power-of-two calibration to at least
20 ms (up to 16,384 iterations), five timed batches and forced GC before each
batch. Three fresh sequential processes rotate the three methods. Two directions
× three methods × six scopes × five batches × three processes give **540 samples**.
Separate first-table records remain in the raw data. No other builds or benchmarks
ran concurrently. Process windows and binary hashes were checked independently
of the runner's numeric and matrix validation.

Use a separate clean checkout at the measured revision to repeat this exact
source version. From the repository root:

```powershell
dotnet restore examples/RegionCoverage --locked-mode
if ($LASTEXITCODE) { throw 'Restore failed.' }
dotnet build examples/RegionCoverage -c Release --no-restore
if ($LASTEXITCODE) { throw 'Build failed.' }
python examples/RegionCoverage/data/freeze.py
if ($LASTEXITCODE) { throw 'Frozen input verification failed.' }
$revision = (git rev-parse HEAD).Trim()
$frozen = "artifacts/coverage-bin-$revision"
$output = "artifacts/coverage-$revision"
if ((Test-Path $frozen) -or (Test-Path $output)) { throw 'Use fresh destinations.' }
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
Copy-Item examples/RegionCoverage/bin/Release/net10.0 $frozen -Recurse
$runner = "$frozen/RegionCoverage.dll"
$oldTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    foreach ($run in 1..3) {
        dotnet $runner benchmark-intersection $output $run $revision
        if ($LASTEXITCODE) { throw "Coverage measurement failed: $run" }
    }
    dotnet $runner summarize-intersection $output
    if ($LASTEXITCODE) { throw 'Evidence validation failed.' }
} finally { $env:DOTNET_TieredCompilation = $oldTiering }
```

The runner regenerates numeric results and verifies input/binary identity,
the complete matrix, first-use records, digests and sample medians before writing
`decision.json` and `summary.md`. A different runtime or machine can produce
different timings. Preserve a failed decision rather than adjusting the gate.
Input loading, validation, file output and a user interface are outside timing.

## Evidence identity

Raw files and frozen binaries are retained locally under ignored `artifacts/`;
they are not public download links. Inputs and source are public and the commands
above produce new complete evidence. The per-run files retain every pair's area
and coverage, every timed sample and separate first-table observations.

| Binary or local evidence | SHA-256 |
|---|---|
| `RegionCoverage.dll` | `98434272ab26b4ecd2eb5444918597b2bf5c5d6941573c3f4eafc06f15ca27b7` |
| `PolylineKit.Winding.dll` | `14eaf6992d053eb524e511a3d3a21b5cd8d849e29b81ee14344d7f511ef64886` |
| `Clipper2Lib.dll` | `e9bf01e01f494ef6072cd46c792ed4f3004288a92b1e98c1f44931e558b8a5d0` |
| `NetTopologySuite.dll` | `98fa6bae8b75b5f9445236679883fa27750d990aec11e985b85b57a30c37a858` |
| `run-1.json` | `665b382e1ccbbbb669a91aac5a2e67be3d18a8148b51f4204aadde78b149d404` |
| `run-2.json` | `fb7f6c0ff4e768bc2249074180bcff47154177ede50874a4c4d9decb31948ff8` |
| `run-3.json` | `8bb49c5fd756fdff1b75352d332d350ddc320f9f2195c51010e9fc12e77b2df8` |
| `launch.json` | `1d5e12c83e103a08890f22ed37db4355a46c6f57d52981a795458f9838fdc848` |
| `process-order.json` | `0c6739cadd45a092a8b82e39077912e2e13959517e75bfdb67f4c6c5e59ad4dd` |
| `decision.json` | `44672ad1994011143bb1fb3406f3992fca0632dfabf95b1bb6fdf257edb9f293` |
| `summary.md` | `eddc67542b16003214158534be35e450648277849116bbcff9505f60c2040f8b` |

Translated input identity: `eac1fd8b704c4ba78f7ced0784b2ec3b6ba3eeb7bedd35d05eece811d15e845c`. The [data manifest](data/manifest.json)
pins the original source and admission/exclusion rules.
