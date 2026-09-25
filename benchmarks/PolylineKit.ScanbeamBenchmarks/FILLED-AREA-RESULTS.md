# Public FilledArea: hybrid integration passes the fixed gates

`WindingArea.FilledArea(path, fillRule)` now exposes one NonZero or EvenOdd area.
The public array call passes all **26 target**, **42 preservation**, and **68
integration** gates. On dense integer grids it takes **40.5–47.6% of Winding's
time** and **28.2–62.9% of full-input Clipper's time**. The largest aggregate
overhead over Winding on a preservation control is **4.75%**; the largest
overhead over the retained hybrid prototype is **2.36%**.

Measured source:
[`dce72509df4d536a2f379cc1509ee2cbc4b24f38`](https://github.com/bgtnt/polylinekit/commit/dce72509df4d536a2f379cc1509ee2cbc4b24f38).
The [fixed protocol](FILLED-AREA-PROTOCOL.md) and
[compact evidence](../filled-area-evidence.json) preserve the input identities,
all 408 aggregate method rows, outputs, ranges, and individual gate decisions.
This is an integration check on the same 34 previously inspected inputs, not a
new held-out workload study. Earlier [v3 prototype results](HYBRID-V3-RESULTS.md)
remain separately recorded.

## API and implementation

```csharp
double area = WindingArea.FilledArea(points, PathFillRule.EvenOdd);
```

Both target assemblies expose this API with the existing closed-path input
contract. In the .NET 10 build, suitable `Point2[]` inputs use the bounded
single-walk Int64 sweep; other arrays and other list representations use the
selected `ClosedPath` result. The .NET Standard 2.0 build always uses that
projection. Existing APIs and their four-integral/overlap results are unchanged.
There are no new runtime dependencies or input quantization.

The selector preserves v3's decisions. It requires 64–1024 supplied vertices,
integer coordinates in ±2048, at most 16 endpoint Y levels, sufficient active
edge density and sampled crossings or identical segments. These are internal
implementation conditions, not additional public input restrictions. The
extraction removes the prototype's unused wider-coordinate and two-walk state,
and rents its own per-thread workspace. The new sweep and selector are safe
scalar C#; this is not a new SIMD, unsafe or C++ result.

## Complete-call measurements

Ratios below are public array time divided by the comparator's time; smaller
is faster. Each time is the median of three process medians. Winding computes
four integrals and diagnostics; Clipper2 2.0.0's C# implementation constructs
contours. This compares APIs that can supply the requested area, not identical
internal work or native C++ implementations.

| Target family | Cells | Public / Winding | Public / full-input Clipper | Public / preloaded Clipper | Public / hybrid v3 |
|---|---:|---:|---:|---:|---:|
| Dense integer grids | 16 | 0.405–0.476 | 0.282–0.629 | 0.280–0.695 | 0.781–0.896 |
| Repeated traversals | 10 | 0.00468–0.01461 | 0.0223–0.1264 | 0.0223–0.1326 | 0.784–0.844 |

Every target beats both primary comparators by at least 20%, including
selection, public validation and workspace rental. The specialized public
implementation also takes less time than its retained prototype on these
targets. The extraction changes several internal costs together; no isolated
ablation attributes that gain to a particular field, branch or buffer.
The very large repeated-traversal gains describe deliberately difficult
synthetic walks, not ordinary contour populations.

All 42 other cells satisfy the ≤1.10 Winding preservation gate. The worst is
`confirm-v3-star-96`/EvenOdd at 1.04753. All 68 cells satisfy the separate ≤1.10
hybrid integration gate; its worst is `held-perturbed-grid-128`/NonZero at 1.02363.
These aggregate gates are not a promise about every individual process or call.

Four cells breach a corresponding limit in process 1; none do in processes 2
or 3. These are retained observations, not discarded samples:

| Process 1 input | Fill | Public / hybrid v3 | Public / Winding |
|---|---|---:|---:|
| many-levels-128 | NonZero | 1.2574 | 1.1902 |
| dev-simple-128 | EvenOdd | 1.1309 | 1.1225 |
| held-perturbed-grid-128 | NonZero | 1.1029 | 1.0810 |
| held-perturbed-grid-512 | NonZero | 1.1500 | 1.1283 |

For example, `many-levels-128`/NonZero public times span 277.195–375.586 us;
its median is 278.819 us versus Winding's 278.809 us. The cause of the variation
was not isolated. All 26 targeted cells also pass in each individual process.

There are still losses to Clipper outside the targeted families. On the six
simple-contour cells the public method takes **1.069–1.182 times** full-input
Clipper's time and **1.582–1.720 times** preloaded Clipper's time. Some fractional
and wide-grid controls also remain slower. The hybrid does not select the
fastest conceivable backend on every input, nor make all polylines faster than
Clipper. Every unfavorable row remains in the evidence.

The public array and read-only-wrapper calls each report **0 warm B/op** on all
68 cells. `Public-list` specifically measures a precreated `Array.AsReadOnly`
wrapper; it has no speed gate and does not characterize arbitrary indexers.
First use, growth and retained cache memory are excluded from these warm
allocation figures. A thread can retain both engine caches; the integer
crossing buffer alone can reach a 12 MiB capacity at the admission limit. See
the [storage and numerical contract](../../docs/winding-area.md).

## Correctness and audit

- All 68 public array results are bit-identical to retained hybrid v3; every
  read-only-wrapper result is bit-identical to the Winding projection.
- All 1,315 selector controls agree with the retained reference, including
  invalid unsampled points and threshold boundaries. Normal/no-intrinsics
  validation is identical. Prior filter-first GIS results remain identical.
- Release builds pass with zero warnings/errors. The five implementation modes
  pass 182,653 checks for the portable target and 182,567 for each modern mode,
  including 1,381 selected-fill checks. These cover independent slab areas,
  transformed grids near ±2048, analytic degeneracies and numeric extremes,
  reentrancy, exception recovery, input ownership and separate threads.
- Standalone, portable and previously compiled consumers pass. The runnable
  example checks repeated-loop NonZero and EvenOdd results.
- An unchanged raw-evidence copy is accepted; all 13 deliberately corrupted
  copies are rejected without changing submitted data or writing derived
  results. The independent publisher verifies all 6,120 samples, every output,
  aggregate and gate, coordinate/binary hashes and sequential process times.

Equivalent coordinates can still have different final rounding between the
integer and general engines outside these bit-parity controls. No universal
relative-error bound or cross-container bit identity is promised.

The existing negative Clipper result remains visible:
`held-near-coincident-512`/NonZero gives Winding
`4.029272012038706e-5` and Clipper `-0.002847855882`. It is not clamped and cannot
satisfy a targeted speed gate. This experiment does not isolate its cause.

## Environment and reproduction

Measured on 2026-09-25, Intel Core i9-9900K (16 logical processors), Windows
10.0.26200 x64, .NET 10.0.12, Release, `DOTNET_TieredCompilation=0`. Three
sequential processes rotate the six methods by offsets 0, 2 and 4. Each method
has a 40 ms warmup and five samples with a calibrated iteration count. There
were no competing builds or tests during timing. Observed process ranges are
retained and are not confidence intervals.

To reproduce from the measured source (use separate output directories):

```powershell
git checkout dce72509df4d536a2f379cc1509ee2cbc4b24f38
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
$runner = 'benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll'
dotnet $runner check-filled-area artifacts/filled-area-check-repro
$env:DOTNET_TieredCompilation = '0'
1..3 | ForEach-Object {
    dotnet $runner benchmark-filled-area artifacts/filled-area-repro $_ dce72509df4d536a2f379cc1509ee2cbc4b24f38
    if ($LASTEXITCODE) { throw 'Benchmark failed.' }
}
dotnet $runner summarize-filled-area artifacts/filled-area-repro
```

The complete local archive is `artifacts/filled-area-dce7250.zip`, SHA-256
`2300d9e471ee1b919bc4f9e02d4835dd9185a627af53878188ea4854f5efd833`.
It contains raw samples, summaries, validation, audit scripts and process logs.
It is **not publicly hosted**; the public compact evidence records its members
and checksums. Coordinates and the benchmark runner are versioned in this
repository, so the measurements can be rerun independently.
