# Hybrid selection: bounded gates pass with unchanged routing

The optimized selector passes the complete frozen matrix: **26 target cells and
42 preservation cells**, including all 12 cells from six new confirmation
inputs. On the six previously failing cells, selection now takes **0.399–0.481
us**, and the complete hybrid adds **1.5–4.4%** over Winding. The routing policy,
backends and numeric results are unchanged. Shipping library code and APIs are
unchanged; this remains an experimental scalar-area wrapper.

Measured source:
[`be96dea41ee3216933699a887670f5f7dde107f6`](https://github.com/bgtnt/polylinekit/commit/be96dea41ee3216933699a887670f5f7dde107f6).
The [fixed protocol](HYBRID-V3-PROTOCOL.md) and
[public evidence](../scanbeam-hybrid-v3-evidence.json) retain every decision,
output, aggregate timing row and observed process range. Earlier
[stage-one](HYBRID-V1-RESULTS.md) and [stage-two](HYBRID-V2-RESULTS.md) failures
remain separate evidence.

## What changed and what improved

Each sampled edge's admitted integer endpoints and bounding box are cached
once. Disjoint boxes avoid endpoint comparisons and orientation predicates;
overlapping boxes still receive the exact predicates. Bounded Int32 arithmetic
is sufficient for these sample orientations. The selected integer backend and
its Int64 event arithmetic are unchanged. This uses safe scalar C#, without
SIMD, unsafe code or new dependencies; sample stack storage grows from 64 to
640 bytes.

The old selector is retained verbatim, apart from its class name and shared
diagnostic record, in the **same measured binary**. Both wrappers include
selection and backend execution on every call. Their comparison attributes
the benefit to selection without comparing timings from different revisions.

Times below are microseconds; each range covers the two fill rules. The six
previously failing cells are now within the unchanged 10% preservation limit.

| Input | Selector v2 | Selector v3 | Full hybrid v2 | Full hybrid v3 | v3 overhead over Winding |
|---|---:|---:|---:|---:|---:|
| held-star-128 | 4.026–4.056 | 0.480–0.481 | 20.788–20.832 | 17.120–17.206 | 2.7–3.2% |
| held-sparse-few-levels-512 | 3.949–3.956 | 0.399–0.400 | 31.949–32.045 | 28.421–28.508 | 1.5–1.7% |
| confirm-sparse-few-levels-256 | 3.969–3.973 | 0.414–0.419 | 18.573–18.592 | 15.105–15.132 | 3.1–4.4% |

Selection is 8.4–9.9 times faster on these cells; full calls take 11.0–18.7%
less time than the retained v2 wrapper. Early rejection of fractional or wide
coordinates is slightly slower in absolute nanoseconds, and not every full
call improves in the observed timings. All rows are retained. Both wrappers
and both selectors report zero warm managed bytes per operation; construction,
buffer growth and retained workspace are outside this claim.

## Complementary backends remain useful

Smaller ratios mean faster execution. The target gate requires both
Hybrid/Winding and Hybrid/full-input Clipper to be at most 0.8 for every cell.

| Target family | Cells | Hybrid / Winding | Hybrid / full-input Clipper | Hybrid / preloaded Clipper |
|---|---:|---:|---:|---:|
| Dense integer grids | 16 | 0.495–0.592 | 0.369–0.782 | 0.386–0.854 |
| Repeated traversals | 10 | 0.00607–0.01858 | 0.0289–0.1623 | 0.0289–0.1701 |

All 26 pass, including the new grid and repeated parallelogram. These large
repeated-traversal gains describe deliberately difficult synthetic walks,
not typical ordinary polygons. Dense-grid selection itself falls to
34.8–67.7% of v2's cost, but this is a small part of their complete call.

The other four new inputs—a star, sparse sawtooth, subdivided rectangle and
simple comb with broadly overlapping edge boxes—also pass preservation under
both fills. The broad-box control still invokes exact predicates and takes
about 1 us to select; its complete hybrid adds less than 1% over Winding.

There is little margin in one new control. `confirm-v3-simple-192` NonZero
takes **16.960 us versus 15.492 us** for Winding: **9.47%** extra, just below
the 10% gate. Hybrid's process medians range from 15.876 to 17.048 us, while
Winding's range from 15.429 to 15.688 us; the gate uses the declared median
of process medians. The three paired process ratios are 1.0810, 1.10043 and
1.0290: one process narrowly exceeds 10%, even though the declared aggregate
gate passes. It also exposes a missed faster route: forced integer sweep is
11.417 us, making Hybrid/best admissible forced backend **1.486**. The selector
still chooses Winding because its policy is deliberately unchanged. Passing
the preservation gate does not imply choosing the fastest backend everywhere.

Clipper still wins outside the targeted specialization. For example,
`held-binary-grid-128` NonZero costs 415.681 us for Hybrid and 295.366 us for
full-input Clipper. The guarded-double candidate certifies 38 cells and falls
back on 30; it remains a forced comparison, not an automatic route.

## Scope and numerical validation

The operation returns one requested NonZero or EvenOdd filled area of an
implicitly closed walk. The integer route still requires 64–1024 supplied
vertices, exact integer coordinates within ±2048, at least three retained
vertices, at most 16 endpoint Y levels, mean active-edge count at least 16,
and at least eight sampled proper crossings or eight identical undirected
nonzero segment pairs. No coordinates are rounded, translated or scaled.
Other inputs use Winding. Winding's public call computes four integrals plus
diagnostics; Clipper builds output contours before area reduction. These
different output costs limit claims of intrinsic algorithmic superiority.

All 68 cells have identical complete selection records and bit-identical
hybrid outputs against retained v2. Original 28 input identities and all prior
numeric result bits are preserved. Normal and hardware-intrinsics-off checks
each pass **1,315 differential selector controls plus 629 hybrid controls**,
including exact-rational geometry checks. Legacy filter-first validation is
byte-identical. Twelve deliberately corrupted evidence controls are rejected,
including altered retained-v2 output and selection. The integer/double comparison budget remains
`1e-10 * max(1, abs(Winding))`, not a proof for arbitrary geometry.

Clipper's scale-1e6 conversion truncates inputs and quantizes intersections.
Its retained negative NonZero value on `held-near-coincident-512` remains
-0.002847855882 versus Winding's 0.00004029272012038706; it is neither clamped
nor treated as a valid area oracle. Its cause has not been isolated here.

## Reproduction and decision

The 28 existing inputs are reused controls; only six instances were added
before these timings, and they remain related shape families. Measurements
use three fresh sequential .NET 10.0.12 Release processes on Windows
10.0.26200, x64 Intel family 6/model 158, 16 logical processors, with tiering
disabled. Five samples per row give **590 rows per process and 8,850 samples**.
Reported values are medians of process medians. Ordinary OS activity was not
controlled; the published process ranges are not confidence intervals, and
the close rectangle result is not a cross-machine guarantee.

Build the measured commit in Release, then run its frozen executable:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll check-hybrid artifacts/scanbeam-hybrid-v3-check
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll benchmark-hybrid artifacts/scanbeam-hybrid-v3 1 be96dea41ee3216933699a887670f5f7dde107f6
# Repeat benchmark-hybrid with run numbers 2 and 3, sequentially.
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll summarize-hybrid artifacts/scanbeam-hybrid-v3
```

The public manifest records source and binary identities, raw hashes and all
aggregates. The verified local audit archive is
`artifacts/scanbeam-hybrid-v3-be96dea.zip`, SHA-256
`98c7028ffe3937a1bd833185f2b1a174c854a5d6e9c29ad2172ba9a2148c92d7`.
It is not an advertised public download.

This optimization resolves the measured selection-cost failure without
changing routing or numerical semantics. Keep the successful bounded result
and its near-threshold control. Library integration still needs a deliberate
scalar-operation contract and broader geometry/architecture validation;
fractional dense inputs and the existing four-integral API remain unresolved.
No routing thresholds were retuned after examining these timings.
