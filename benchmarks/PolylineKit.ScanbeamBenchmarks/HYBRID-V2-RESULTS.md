# Adaptive closed-area results: complementary backends, costly selection

The actual hybrid succeeds on all **22 dense-grid and repeated-traversal
input/fill targets**, including six target cells from newly added confirmation
inputs. It does not pass the complete acceptance gate: selecting the correct
Winding backend still slows six short control cells by **13.9–30.0%**. The
automatic selector remains experimental; the shipping library is unchanged.

This is now a combination of algorithms, not another attempt to make the
general double sweep faster than Winding on GIS intersections. The result
supports a specialized integer backend for the tested difficult walks. It does
not establish a universally faster replacement for Winding or Clipper.

Measured source:
[`87858da7cf41e44ad208b262efe8e57b39307b1d`](https://github.com/bgtnt/polylinekit/commit/87858da7cf41e44ad208b262efe8e57b39307b1d).
The [fixed second-stage protocol](HYBRID-V2-PROTOCOL.md) and
[complete public evidence](../scanbeam-hybrid-v2-evidence.json) retain every
input identity, output, aggregate timing row, process range, decision and failed gate.

## The operation and the combination

Every geometry method returns one requested NonZero or EvenOdd filled area of
the same implicitly closed walk. `HybridClosedArea` chooses between shipping
`WindingArea.ClosedPath` and the existing `IntegerScanbeam`. It includes selection
and backend preparation on every timed call; it does not cache an area result.

The integer route requires 64–1024 supplied vertices, exact integer coordinates
within ±2048, at least three retained vertices, at most 16 endpoint Y levels,
and mean active-edge count at least 16. An initial sample of 16 deterministic
edge positions must contain at least eight strict crossings or eight identical
undirected nonzero segment pairs. Only a positive sample triggers the complete
coordinate and structure scan. All other inputs use Winding. No coordinates
are rounded, translated or rescaled, and the selector cannot see fixture names,
families, hashes or timings.

The separate guarded-double candidate now has a dedicated closed-path mode,
using the original edges and a whole-call `ClosedPath` fallback. It is measured
as a forced alternative, not chosen automatically.

The current Winding API computes four integrals and diagnostics even though
this experiment requests one scalar. Clipper returns contours before their
area is reduced. These output-cost differences are explicit; this experiment
does not replace the existing four-result API or prove intrinsic algorithmic
superiority under identical internal work.

## Gains on the difficult inputs

Ratios below cover every cell in each target family; smaller is faster. Values
are medians of three process medians. Both fill rules participate.

| Target family | Cells | Hybrid / Winding | Hybrid / full-input Clipper | Hybrid / preloaded Clipper | Target gate |
|---|---:|---:|---:|---:|---|
| Dense integer grids | 14 | 0.510–0.594 | 0.368–0.791 | 0.359–0.868 | All pass |
| Repeated/reversed traversals | 8 | 0.00615–0.01893 | 0.0285–0.1623 | 0.0288–0.1689 | All pass |

Dense-grid calls take **40.6–49.0% less time than Winding** and **20.9–63.2%
less than full-input Clipper**, including selection. The stronger preloaded
Clipper comparison also favors the hybrid in every target cell, although not
always by the primary gate's 20% margin.

Representative target timings, in microseconds:

| Input | Fill | Winding | Hybrid | Full-input Clipper | Selector alone |
|---|---|---:|---:|---:|---:|
| frozen-grid-256 (512 supplied vertices) | NonZero | 7051.675 | 3819.775 | 5134.525 | 17.866 |
| frozen-grid-256 (512 supplied vertices) | EvenOdd | 7219.950 | 3680.088 | 9160.600 | 17.900 |
| confirm-grid-256 | NonZero | 1683.769 | 893.553 | 1260.463 | 10.540 |
| repeated-square-512 | NonZero | 6141.725 | 39.129 | 522.920 | 7.395 |
| confirm-diamond-256 | NonZero | 1633.206 | 30.909 | 190.435 | 4.279 |
| confirm-opposite-retrace-256 | EvenOdd | 1646.025 | 18.788 | 444.938 | 4.111 |

The especially large traversal gains are real for these synthetic repeated
contours; they are not evidence that ordinary polygons usually improve by
similar factors. All 56 hybrid timing cells and selector-only cells report
zero warm managed bytes per operation. Construction, growth and retained
integer crossing buffers are outside that allocation claim.

## Why the full gate fails

All six failed preservation cells choose Winding correctly. The roughly
4.2-microsecond sample costs too much relative to the underlying short call:

| Input | Fill | Winding us | Hybrid us | Selector us | Extra time |
|---|---|---:|---:|---:|---:|
| held-star-128 | NonZero | 16.798 | 21.065 | 4.318 | 25.4% |
| held-star-128 | EvenOdd | 16.755 | 21.074 | 4.245 | 25.8% |
| held-sparse-few-levels-512 | NonZero | 28.317 | 32.634 | 4.179 | 15.2% |
| held-sparse-few-levels-512 | EvenOdd | 28.589 | 32.565 | 4.198 | 13.9% |
| confirm-sparse-few-levels-256 | NonZero | 14.472 | 18.711 | 4.210 | 29.3% |
| confirm-sparse-few-levels-256 | EvenOdd | 14.425 | 18.753 | 4.186 | 30.0% |

The threshold was at most 10% extra time on every non-target cell. It remains
unchanged after measurement. Overall, 50 of 56 cells pass their applicable
gate. In this matrix the selector chooses the fastest forced backend among
Winding, integer sweep and double sweep in every cell; its own cost causes the
remaining loss. Worst hybrid / fastest-forced-backend ratio is 1.300. That
comparison excludes Clipper, whose separate full and preloaded ratios are also
published for every cell.

Clipper still wins some cases outside the integer specialization. For example,
NonZero on `held-binary-grid-128` takes 420.942 us for Hybrid versus 317.422 us
for full-input Clipper; `held-simple-512` takes 43.709 versus 36.842 us. An
arbitrary-double or simple-contour win has not been achieved by this router.

## What the forced double sweep establishes

Of 56 input/fill cases, 28 certify and 28 fall back: 22 report `endpoint-slope`
and six `endpoint-order`. Every dense-grid target falls back. Failed sweep work
and the full Winding call are inside its timings; those fallback rows allocate
768 managed bytes per operation. Certified rows allocate zero after warmup.

The double sweep succeeds on repeated traversals, but the integer sweep is
faster there. It supplies no fastest forced backend on this matrix and does
not justify an automatic double route. For example, the confirmation diamond
NonZero costs 134.354 us for the double sweep versus 30.909 us for the complete
hybrid. These findings concern this certified implementation, not all possible
double-coordinate scanline algorithms.

## The first failed policy is preserved

Stage one at
[`bb10c6b72a8a8a48f022740bf564ca79b7588ed4`](https://github.com/bgtnt/polylinekit/commit/bb10c6b72a8a8a48f022740bf564ca79b7588ed4)
passed all 12 dense-grid gates but added about 9.94 us, or 35.5–35.8%, to the
sparse 512-vertex controls. Its proper-crossing-only sample also missed the
roughly 30-us integer route for repeated contours and selected roughly 6-ms
Winding calls. Its [evidence and failed decision](../scanbeam-hybrid-evidence.json)
remain available.

Stage two moved the sample first and admitted coincident sampled segments.
It fixes the missed repeated-traversal route and reduces sparse-control
selection time, but introduces the star-control failures and still fails
preservation. The original 22 inputs are reused development evidence for this
revision: nine development fixtures and 13 originally held-out instances. Only
six confirmation inputs are newly added before these timings, and they still
belong to related shape families. Neither series is pooled with the other or
presented as broad independent generalization evidence.

## Numerical checks and reproduction

All hybrid results are bit-identical to their selected forced backend. The
integer and double results satisfy the unchanged comparison budget
`1e-10 * max(1, abs(Winding))`; this is not a proved arbitrary-input error bound.
Integer topology uses exact rational events with approximate final areas.
Guarded successes carry their documented certified area enclosure; fallback
carries only Winding's numerical contract. Normal and hardware-intrinsics-off
runs each pass 629 hybrid controls, including independent exact-rational small
paths, invalid inputs, unsampled invalid coordinates, reuse and mode changes.
Legacy filter-first validation remains byte-identical. Ten intentionally
corrupted evidence controls are rejected.

Clipper uses scale 1e6, with the existing converter truncating scaled inputs
toward zero and generated intersections quantized. Its value is therefore not
an exact oracle for original binary64 geometry. One retained NonZero result on
`held-near-coincident-512` is negative: -0.002847855882, versus Winding's
0.00004029272012038706. No clamping, excluded row or unverified defect diagnosis
is used to hide this numeric difference.

Measured environment: .NET 10.0.12, Windows 10.0.26200, x64 Intel family 6/model
158 with 16 logical processors, tiering disabled. Three fresh sequential
processes, five samples per row, 370 rows per process: **5,550 samples**. Method
order rotates between processes. There were no concurrent builds, profilers
or benchmark workloads; ordinary OS activity was not controlled. Process ranges
are descriptive, not confidence intervals or cross-hardware guarantees.

Build the measured revision in Release, then use its frozen executable:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll check-hybrid artifacts/scanbeam-hybrid-v2-check
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll benchmark-hybrid artifacts/scanbeam-hybrid-v2 1 87858da7cf41e44ad208b262efe8e57b39307b1d
# Repeat benchmark-hybrid with run numbers 2 and 3, sequentially.
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll summarize-hybrid artifacts/scanbeam-hybrid-v2
```

The public manifest records source, binaries, raw-file hashes, every output and
all aggregate timing rows. The verified local audit archive is
`artifacts/scanbeam-hybrid-v2-87858da.zip`, SHA-256
`4e31c2a410ef35660e80eed9ea414471e19eb5f992a9fd43320a6c927ab926f1`.
It is a retained local artifact, not an advertised public download.

## Decision

Keep the existing library behavior. The specialized backend has demonstrated
useful complementary performance, but this automatic policy fails its own
preservation requirement. Do not ship it or retune this protocol after seeing
the results. The next bounded investigation should reduce or amortize the
selection cost while retaining these exact controls: for example, compare a
cheaper early rejection against explicit immutable preparation when callers
reuse a path, charging preparation separately. General binary64 dense inputs
and the four-integral public API remain separate unresolved requirements.
