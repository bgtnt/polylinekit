# Polygon preparation results — 11 October 2026

Preparing all 114 Census regions through the recommended polygon factories took
**1.524 ms before and 0.369 ms after** the orientation change: 4.13× faster for
this construction batch, with managed allocations falling from 426,528 to
228,664 bytes. Preparing both layers and performing all 1,400 intersection
queries improved much less. The query engine was unchanged.

The baseline is the **actual published** `PolylineKit.Core` alpha.2 net10.0 DLL,
built from [5e9dcc1](https://github.com/bgtnt/polylinekit/commit/5e9dcc1a5bcb34b5b2d8ae60105d328aec66a80c).
The candidate is source commit
[a7cad8a](https://github.com/bgtnt/polylinekit/commit/a7cad8a858f354cad38398a9dab69ee23a51d243).
These results describe the candidate; they do not change the already published
alpha.2 binary or establish an improvement over Clipper2.

## Complete existing Census input

Both layers retain all 100 counties and 14 districts, including disconnected
components and the hole: 123 rings and 12,182 supplied vertices. The 1,400 pairs
include 285 overlapping closed bounds. Inputs are already oriented consistently
with their explicit roles; loading and that common input step are outside the
measurements. `PolygonFactories` selects `FromPolygon` for one component and
`FromPolygons` otherwise. This is the existing Census corpus, **not a new
independent dataset**.

Times below are the median of three independent process medians. Parenthesized
ranges contain the minimum and maximum **process medians**, not confidence
intervals. Allocations are managed bytes per entire batch or layer traversal.

| Preparation of all 114 regions | Baseline ms (range) | Candidate ms (range) | Baseline / candidate | Baseline bytes | Candidate bytes |
|---|---:|---:|---:|---:|---:|
| `PolygonFactories` | 1.524 (1.486–1.582) | 0.369 (0.362–0.371) | 4.13× | 426,528 | 228,664 |
| `FromPolygons` for every feature | 1.488 (1.480–1.494) | 0.378 (0.371–0.451) | 3.93× | 439,720 | 241,856 |
| `FromRings` control | 0.178 (0.170–0.227) | 0.168 (0.168–0.173) | 1.06× | 224,528 | 224,528 |

| Preparation plus 1,400 queries | Factory | Baseline ms (range) | Candidate ms (range) | Median time reduction |
|---|---|---:|---:|---:|
| Counties → districts | `PolygonFactories` | 15.214 (15.170–15.469) | 14.148 (14.071–17.664) | 7.0% |
| Districts → counties | `PolygonFactories` | 15.446 (15.277–15.494) | 14.136 (14.123–14.414) | 8.5% |
| Counties → districts | `FromPolygons` | 15.428 (15.356–15.469) | 14.236 (14.019–14.271) | 7.7% |
| Districts → counties | `FromPolygons` | 15.455 (15.330–15.559) | 14.150 (14.122–15.368) | 8.4% |
| Counties → districts | `FromRings` control | 14.287 (13.792–14.327) | 13.982 (13.813–14.022) | 2.1% |
| Districts → counties | `FromRings` control | 14.162 (13.987–14.288) | 14.120 (13.912–14.259) | 0.3% |

The candidate counties-to-districts polygon-factory run at **17.664 ms is retained**;
one of its three process medians was slower than every baseline median. There
are only three processes per version, so small end-to-end differences require
caution. `FromRings` does not use the changed orientation function: its timing
changes illustrate measurement variation, not a new optimization. Traversal
allocations through `PolygonFactories` were 427,712 → 229,848 bytes, including
both layer preparations and the arrays holding their prepared objects.

## What changed and what was checked

For a well-conditioned ring, orientation now sums robust edge terms directly
over the owned snapshot, using its bounds center and compensated accumulation.
It avoids another ring array and the full crossing/winding calculation. A
conservative arithmetic guard sends tiny, cancelling or uncertain sums to the
previous `ClosedPath(...).Signed` route. Raw `FromRings`, public signatures,
fill-rule defaults and the overlap engine are unchanged.

Every timing process first passed 11,557 factory assertions. Across both
versions, all 97 timed output digests were identical; the largest observed
factory area and IoU differences were zero. This checks the selected inputs,
not an independent area oracle or universal bitwise equivalence.

The committed candidate was also checked with 128,431 prepared-region
assertions, including 1,668 orientation variants with bitwise comparisons of
stored vertex order against the legacy orientation decision in each of the
portable target, modern target and forced scalar runs. Additional implementation
checks ran both targets with hardware intrinsics disabled. Fixtures include
cancellation, retracing, axis
exchange, reversed and cyclic orders, large translations, anisotropic and
subnormal coordinates. The NTS conversion/GIS check also passed 45,028 GIS
comparisons. These implementation checks supplement the benchmark; NTS is a
different floating-point reference, not exact arithmetic. The check sources are
in [PreparedRegionChecks.cs](../../tests/PolylineKit.MultiRingChecks/PreparedRegionChecks.cs)
and [the NTS conversion example](../../examples/NetTopologySuiteInterop/README.md).

## Synthetic construction costs and limits

The suite includes 16, 64, 256 and 1,024 vertices for simple dense boundaries,
crossing walks, four components and shell/hole inputs. On the synthetic
1,024-vertex step-five crossing walk, `FromPolygon` construction was
1.311 ms (1.299–1.330) → 0.0288 ms (0.0283–0.0301), or 45.6×. Including its fixed
overlap query reduced the difference to 4.066 ms (3.996–4.121) →
2.768 ms (2.766–2.778), or 1.47×. This is construction work avoided on one
synthetic input, not a faster intersection algorithm or a new application.

The timed inputs do not require reversal and do not exercise the uncertain-sign
fallback. Such inputs can pay for an additional direct pass before the old
engine. Therefore the change does not promise that every ring becomes faster.
Preparation remains an input snapshot; it does not cache intersections or a
persistent spatial index. Warm query workspace is retained and its initial
allocation/growth is outside this protocol.

## Environment, identities and reproduction

Measured on 11 October 2026, using .NET 10.0.12, Windows build 10.0.26300, X64,
16 reported logical processors, `Intel64 Family 6 Model 158 Stepping 12,
GenuineIntel`. All six processes used Release code,
`DOTNET_TieredCompilation=0` and `DOTNET_TieredPGO=0`; hardware intrinsics used
the runtime default. Three baseline processes ran first, then three candidate
processes, serially. Each process measured 97 cells with five samples per cell,
four explicit warmup calls and adaptive calibration capped at 2,048 iterations.
The 12 ms target can be missed by small cells at that cap. Validation also runs
before timing, so these are warm construction/query measurements. GC ran outside
each sample's counters, and `Sample` objects were created after both counters.

| Measured identity | SHA256 |
|---|---|
| Published Core DLL | `bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca` |
| Candidate Core DLL | `def6f37fc4128ecb5bf77a1d4fecbb9bf313d1daf0a78560339dcf908dc520f1` |
| Identical benchmark executable | `27ced35f06bf81c705fa046b74633fd21f32211b359510473da872f66bfab350` |
| Binary coordinate/role input hash | `09f367b2bb2d240a92ec40011c263957d438954a8d5f465a3e3ef4c0a19a6859` |

The common benchmark executable was built before the implementation commit;
its informational version records parent `1b72713`. Its executable source is
unchanged in `a7cad8a`, and the exact same runner was used for both DLLs.

Frozen source hashes are
`4e114c8d8168e782ac185cd1f1f7bc7c4a1772e218e9502d00b204837bc5441b`
for `nc-counties.geojson` and
`237b71fa2dd7cc73e4e46a0b624ab0256051bf4200e4920ecc805720f6a0886f`
for `nc-districts.geojson`.

[measurements.csv](measurements.csv) retains all 97 cells, all six process
medians in nanoseconds, operation sizes and each version's median allocations.
No outlier row or process median was removed. Use the
[runner protocol](README.md) to download and verify the published baseline,
build the runner once and perform its three baseline runs. Then build the
candidate in a separate checkout, without changing the active checkout:

```powershell
git clone --no-checkout https://github.com/bgtnt/polylinekit.git artifacts/preparation-source
git -C artifacts/preparation-source checkout --detach a7cad8a858f354cad38398a9dab69ee23a51d243
dotnet build artifacts/preparation-source/src/PolylineKit.Core/PolylineKit.Core.csproj -c Release -f net10.0 -o artifacts/preparation-core
New-Item -ItemType Directory -Force artifacts/preparation-candidate | Out-Null
Copy-Item artifacts/preparation-baseline/* artifacts/preparation-candidate -Recurse -Force
Copy-Item artifacts/preparation-core/PolylineKit.Winding.dll artifacts/preparation-candidate/PolylineKit.Winding.dll -Force
dotnet artifacts/preparation-candidate/PolylineKit.PreparationBenchmarks.dll check . artifacts/preparation-results/candidate-check.json
1..3 | ForEach-Object {
    dotnet artifacts/preparation-candidate/PolylineKit.PreparationBenchmarks.dll run . "artifacts/preparation-results/candidate-run-$_.json" $_
}
```

Keep the same environment settings for both sets and use `Compare-Reports.ps1`
as shown in the protocol. Copying the runner preserves its exact executable in
both halves of the comparison. Fresh builds can have different DLL hashes with
a different SDK or build metadata; the reports always identify the DLLs actually
loaded. Preserve those identities when recording a new result.
