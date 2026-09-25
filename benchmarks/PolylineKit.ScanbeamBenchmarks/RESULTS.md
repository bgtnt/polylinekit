# Bounded-integer scanbeam results

The final prototype passes all four predeclared grid gates. It takes **22–24%
less time for NonZero and 54–57% less time for EvenOdd** than the faster direct
Clipper64 variant on the two frozen grids. These are bounded integer inputs
with few endpoint levels, measured on one machine. The prototype remains an
experimental executable; it has not replaced or changed any library API.

The broader outcome is mixed: fresh grids also improve, whereas the integer
star and many-level walk are slower than both the existing engine and Clipper.
This supports investigating a specialized backend, not a general replacement.

## Frozen decision cases

Each input pair is prejoined into one closed walk with twice the listed point
count. Every method receives that complete walk. Medians are of three process
medians, five batches per row. The faster of reused and preloaded Clipper is
selected independently for each cell. The threshold was Clipper/prototype
>=1.25 in all four cells; the final values are 1.285–2.326.

| Path points per input | Fill | Current Winding us | Prototype us | Faster Clipper us | Clipper/prototype | Less time |
|---:|---|---:|---:|---:|---:|---:|
| 64 | NonZero | 404.145 | 217.412 | 279.314 | 1.285 | 22.16% |
| 64 | EvenOdd | 401.800 | 203.233 | 440.281 | 2.166 | 53.84% |
| 256 | NonZero | 6833.025 | 3763.137 | 4971.600 | 1.321 | 24.31% |
| 256 | EvenOdd | 6828.725 | 3574.412 | 8313.000 | 2.326 | 57.00% |

Both arithmetic dispatch paths retain exact integer/rational event ordering;
the area integration remains compensated binary64. All benchmark inputs use
the certified Int64 path (coordinate magnitudes <=2048). Wider-domain Int128
fallback has correctness coverage, not a demonstrated throughput advantage.

## What changed, and what the ablations establish

This is an independent implementation inspired by Clipper's active-edge and
scanbeam organization. It handles crossings at one exact point as a group and
integrates filled gaps directly, avoiding global per-edge crossing incidence
lists and shared-subedge hashing. It still buffers every crossing inversion
within the current band; it is not a streaming O(n)-memory general sweep.
No Clipper source was copied.

The initial checked-Int128 version at
`44571849e6a4da0a577aaa15f00c48980cd3c219` failed every gate. Removing overflow
checks only from four mathematically bounded Int128 expressions at
`94f5ecd64f6a13c7575aa84f07b9d04135d1b09c` gave a 3.2–3.7x grid improvement;
the NonZero gates still failed. The final version at
`168f5eb00b71413e675fc8cfc1a06e3209ae00da` uses the same expressions in Int64
when all input coordinates permit it, otherwise retaining the Int128 path.
The largest Int64 intermediate is bounded by 2^62. Input validation, array and
index checking, and all geometric invariant guards remain enabled.

These changes are scalar integer arithmetic improvements. No SIMD, unsafe
pointer code, C++ implementation, tolerance increase, crossing quantization or
changed fill rule is involved. All 56 rows' area/reference/diagnostic outputs
are bit-identical across the three versions. Each follow-up was declared before
its own three timing processes; all versions and failures are retained. This
is a development ablation on known fixtures, not a held-out evaluation.

| Input | Fill | Checked Int128 us | Bounded Int128 us | Certified Int64 us |
|---|---|---:|---:|---:|
| fresh-grid-128 | EvenOdd | 1065.750 | 336.236 | 223.843 |
| fresh-grid-128 | NonZero | 1332.362 | 393.764 | 238.526 |
| fresh-grid-512 | EvenOdd | 22108.600 | 6260.200 | 3989.213 |
| fresh-grid-512 | NonZero | 24605.700 | 6823.125 | 4168.337 |
| frozen-grid-256 | EvenOdd | 19807.750 | 5580.675 | 3574.412 |
| frozen-grid-256 | NonZero | 22560.400 | 6161.500 | 3763.137 |
| frozen-grid-64 | EvenOdd | 992.138 | 305.576 | 203.233 |
| frozen-grid-64 | NonZero | 1213.900 | 357.317 | 217.412 |
| integer-star-512 | EvenOdd | 2755.550 | 1280.481 | 1018.312 |
| integer-star-512 | NonZero | 2754.950 | 1271.175 | 1019.947 |
| many-levels-128 | EvenOdd | 2136.731 | 811.141 | 598.103 |
| many-levels-128 | NonZero | 2867.113 | 983.553 | 651.869 |
| repeated-square-512 | EvenOdd | 53.532 | 33.714 | 30.037 |
| repeated-square-512 | NonZero | 78.914 | 39.103 | 31.317 |

## Numerical evidence and limits

The first implementation exposed cancellation on a large-coordinate triangle
with exact area 0.5. The final integration forms exact rational widths between
adjacent edges and exact differences of event heights before converting them
to doubles. It then sums nonnegative trapezoids. The tolerance was not relaxed.

The checks pass 3,681 assertions, including 400 small-grid and ten full-domain
walks checked with an independent BigInteger-rational oracle, analytic fills,
multiway events, simultaneous crossings at different x positions, collinear
overlaps, retracing, and 2048/2049 dispatch boundaries. A further 56 benchmark
method/fill checks pass. Final results agree with current Winding within the
predeclared 1e-10 * max(1, abs(reference)) experiment budget. This budget is not
a proved arbitrary-input error guarantee; large benchmark references use the
existing Winding engine, not the slow exact oracle.

Clipper2 2.0.0 is direct C#, with coordinate scale 1e6. Integer inputs convert
exactly; generated intersections still lie on Clipper's output grid. The
prototype's rational intersections are not snapped. Thus the compared area
contracts have explicitly different numerical behavior. For example, unscaled
Clipper64 reports EvenOdd area 1.5 on frozen-grid-256, versus about
24.6106953533 from Winding and the prototype. Integer input alone does not make
unscaled integer clipping an equivalent oracle. The full table reports p6
errors and retains the unscaled diagnostic separately.

Every final prototype timing sample reports 0 managed B/op after warmup. This
does not describe construction, growth, retained buffers, native or peak memory.
Clipper returns contours, a broader output than area alone, and allocates them.
The current Winding call computes four integrals, whereas the prototype
computes only the requested fill. These differences are part of the intended
area-only use case and prohibit universal algorithm-speed claims.

The frozen 512-point walk uses seven bands, 20,044 pair inversion events and
8,291 exact crossing-point groups (peak active count 246). These are untimed
algorithm counts, not a CPU profile. Events exactly on band boundaries are
handled by rebuilding the next band's order. Rebuilding hurts many-level input;
the final star is about 3.4x slower than direct Clipper and the many-level walk
about 1.1–1.8x slower. The repeated-square control favors the prototype strongly,
but repeated identical traversal is a narrow synthetic case, not market evidence.

## Reproduction and evidence

Repository: https://github.com/bgtnt/polylinekit. Measured final implementation:
`168f5eb00b71413e675fc8cfc1a06e3209ae00da`. Windows 10.0.26200, x64,
Intel Core i9-9900K (8 cores/16 logical processors), .NET 10.0.12,
SDK 10.0.401, DOTNET_TieredCompilation=0. Three fresh sequential processes per
version, 840 samples/version, 2,520 samples total. No concurrent benchmark/build
jobs, CPU affinity or frequency locking. Ranges below are descriptive, not
confidence intervals or a hardware-independent guarantee.

Follow [README commands](README.md) at the measured commit and the
[protocol](PROTOCOL.md). The [public evidence manifest](../scanbeam-evidence.json)
contains every aggregate row from all three versions, raw-file and assembly
hashes, gates, validation and untimed counts. Raw JSON is reproducible using
the committed inputs/runner and is retained locally, outside the source tree.
The summarizer recomputes every output and checks the complete matrix, hashes,
medians and tiering. Fifteen corruption controls (five for each version) reject
missing rows, changed medians, input hashes, coordinated output changes and
invalid tiering. Both failed gates produce valid FAIL reports.

Local audit archive: `artifacts/scanbeam-evidence-168f5eb.zip`,
78,567 bytes, SHA256 `d02ce5da9bfc5d93ccaed1934116a511695cb04f1e62a8181ee2c5a5c4f5840a`.

The archive contains only the listed JSON/Markdown evidence; its membership
and every member's bytes were checked. It is a local audit artifact, not an
advertised public download.

## Decision

Continue only as a specialized integer-coordinate investigation. The benchmark
does not justify silently routing general binary64 inputs, imposing rounding on
callers, replacing existing APIs, or claiming C++ is needed. Before integration,
test representative real contours, larger/more varied coordinates, first-use
and retained-memory costs, and a defensible dispatch policy. Preserve the current
engine for its already demonstrated coverage advantage and broader contract.

## Complete final timing matrix

| Input | Fill | Method | us | Range us | B/op | Area minus Winding |
|---|---|---|---:|---:|---:|---:|
| fresh-grid-128 | EvenOdd | Clipper64-preloaded-p6 | 512.769 | 509.622–516.177 | 149144 | -2.675E-06 |
| fresh-grid-128 | EvenOdd | Clipper64-reused-p6 | 517.642 | 508.883–521.117 | 149232 | -2.675E-06 |
| fresh-grid-128 | EvenOdd | IntegerScanbeam | 223.843 | 222.785–224.291 | 0 | 0 |
| fresh-grid-128 | EvenOdd | WindingArea | 447.777 | 443.411–448.652 | 0 | 0 |
| fresh-grid-128 | NonZero | Clipper64-preloaded-p6 | 318.173 | 314.642–318.320 | 50216 | -3.58501E-07 |
| fresh-grid-128 | NonZero | Clipper64-reused-p6 | 321.147 | 318.770–325.409 | 50304 | -3.58501E-07 |
| fresh-grid-128 | NonZero | IntegerScanbeam | 238.526 | 236.839–238.554 | 0 | 0 |
| fresh-grid-128 | NonZero | WindingArea | 446.427 | 443.445–456.331 | 0 | 0 |
| fresh-grid-512 | EvenOdd | Clipper64-preloaded-p6 | 10077.300 | 9779.550–10118.450 | 2214104 | -1.66845E-06 |
| fresh-grid-512 | EvenOdd | Clipper64-reused-p6 | 10023.550 | 9639.900–10235.725 | 2214192 | -1.66845E-06 |
| fresh-grid-512 | EvenOdd | IntegerScanbeam | 3989.213 | 3986.488–4013.387 | 0 | 7.10543E-14 |
| fresh-grid-512 | EvenOdd | WindingArea | 7492.850 | 7486.550–7638.850 | 0 | 0 |
| fresh-grid-512 | NonZero | Clipper64-preloaded-p6 | 5965.100 | 5937.725–5968.325 | 494552 | -1.35932E-06 |
| fresh-grid-512 | NonZero | Clipper64-reused-p6 | 5974.750 | 5932.925–6178.750 | 494640 | -1.35932E-06 |
| fresh-grid-512 | NonZero | IntegerScanbeam | 4168.337 | 4152.938–4231.975 | 0 | 1.42109E-14 |
| fresh-grid-512 | NonZero | WindingArea | 7525.725 | 7491.750–7604.625 | 0 | 0 |
| frozen-grid-256 | EvenOdd | Clipper64-preloaded-p6 | 9009.025 | 8649.000–9059.475 | 2012488 | -2.60847E-06 |
| frozen-grid-256 | EvenOdd | Clipper64-reused-p6 | 8313.000 | 8241.700–9126.600 | 2012576 | -2.60847E-06 |
| frozen-grid-256 | EvenOdd | IntegerScanbeam | 3574.412 | 3572.825–3617.312 | 0 | 3.55271E-15 |
| frozen-grid-256 | EvenOdd | WindingArea | 6828.725 | 6799.075–6867.825 | 0 | 0 |
| frozen-grid-256 | NonZero | Clipper64-preloaded-p6 | 4971.600 | 4934.137–4985.450 | 352240 | -2.91623E-06 |
| frozen-grid-256 | NonZero | Clipper64-reused-p6 | 5019.613 | 4987.825–5021.488 | 352328 | -2.91623E-06 |
| frozen-grid-256 | NonZero | IntegerScanbeam | 3763.137 | 3758.662–3809.338 | 0 | -1.42109E-14 |
| frozen-grid-256 | NonZero | WindingArea | 6833.025 | 6820.450–6845.300 | 0 | 0 |
| frozen-grid-64 | EvenOdd | Clipper64-preloaded-p6 | 440.281 | 436.098–441.562 | 135136 | -3.60387E-07 |
| frozen-grid-64 | EvenOdd | Clipper64-reused-p6 | 442.123 | 441.772–497.547 | 135224 | -3.60387E-07 |
| frozen-grid-64 | EvenOdd | IntegerScanbeam | 203.233 | 201.977–203.731 | 0 | -1.42109E-14 |
| frozen-grid-64 | EvenOdd | WindingArea | 401.800 | 400.670–406.572 | 0 | 0 |
| frozen-grid-64 | NonZero | Clipper64-preloaded-p6 | 279.314 | 279.309–282.650 | 45936 | 8.68671E-08 |
| frozen-grid-64 | NonZero | Clipper64-reused-p6 | 310.666 | 306.686–311.538 | 46024 | 8.68671E-08 |
| frozen-grid-64 | NonZero | IntegerScanbeam | 217.412 | 215.068–218.966 | 0 | -7.10543E-15 |
| frozen-grid-64 | NonZero | WindingArea | 404.145 | 400.295–407.981 | 0 | 0 |
| integer-star-512 | EvenOdd | Clipper64-preloaded-p6 | 296.927 | 294.624–298.614 | 43544 | 0 |
| integer-star-512 | EvenOdd | Clipper64-reused-p6 | 321.319 | 318.586–322.828 | 43632 | 0 |
| integer-star-512 | EvenOdd | IntegerScanbeam | 1018.312 | 1008.562–1021.578 | 0 | 0 |
| integer-star-512 | EvenOdd | WindingArea | 243.791 | 242.450–245.614 | 0 | 0 |
| integer-star-512 | NonZero | Clipper64-preloaded-p6 | 297.521 | 295.978–297.769 | 43544 | 0 |
| integer-star-512 | NonZero | Clipper64-reused-p6 | 316.529 | 316.272–321.508 | 43632 | 0 |
| integer-star-512 | NonZero | IntegerScanbeam | 1019.947 | 1010.575–1085.197 | 0 | 0 |
| integer-star-512 | NonZero | WindingArea | 242.352 | 241.954–246.596 | 0 | 0 |
| many-levels-128 | EvenOdd | Clipper64-preloaded-p6 | 541.667 | 533.413–543.500 | 162832 | -0.00241946 |
| many-levels-128 | EvenOdd | Clipper64-reused-p6 | 543.728 | 543.320–545.466 | 162920 | -0.00241946 |
| many-levels-128 | EvenOdd | IntegerScanbeam | 598.103 | 594.055–603.859 | 0 | 2.79397E-09 |
| many-levels-128 | EvenOdd | WindingArea | 272.615 | 269.638–276.802 | 0 | 0 |
| many-levels-128 | NonZero | Clipper64-preloaded-p6 | 363.789 | 363.747–365.198 | 59232 | -0.00330256 |
| many-levels-128 | NonZero | Clipper64-reused-p6 | 364.606 | 364.055–368.759 | 59320 | -0.00330256 |
| many-levels-128 | NonZero | IntegerScanbeam | 651.869 | 648.916–654.056 | 0 | 1.86265E-09 |
| many-levels-128 | NonZero | WindingArea | 274.123 | 268.776–276.701 | 0 | 0 |
| repeated-square-512 | EvenOdd | Clipper64-preloaded-p6 | 1264.463 | 1248.544–1280.594 | 978240 | 0 |
| repeated-square-512 | EvenOdd | Clipper64-reused-p6 | 1242.141 | 1239.078–1264.475 | 978328 | 0 |
| repeated-square-512 | EvenOdd | IntegerScanbeam | 30.037 | 29.652–30.062 | 0 | 0 |
| repeated-square-512 | EvenOdd | WindingArea | 6029.500 | 6014.400–6258.925 | 0 | 0 |
| repeated-square-512 | NonZero | Clipper64-preloaded-p6 | 492.120 | 490.906–495.317 | 47416 | 0 |
| repeated-square-512 | NonZero | Clipper64-reused-p6 | 505.353 | 501.261–505.448 | 47504 | 0 |
| repeated-square-512 | NonZero | IntegerScanbeam | 31.317 | 31.136–31.604 | 0 | 0 |
| repeated-square-512 | NonZero | WindingArea | 6030.300 | 5981.800–6140.350 | 0 | 0 |

| Gate | Fill | Clipper/prototype | Pass |
|---|---|---:|---|
| frozen-grid-256 | EvenOdd | 2.326 | True |
| frozen-grid-256 | NonZero | 1.321 | True |
| frozen-grid-64 | EvenOdd | 2.166 | True |
| frozen-grid-64 | NonZero | 1.285 | True |

Unscaled Clipper64 diagnostic (not timed, different crossing grid):

| Input | Fill | Winding | Clipper scale 1 |
|---|---|---:|---:|
| fresh-grid-128 | EvenOdd | 22.949050173983753 | 7.5 |
| fresh-grid-128 | NonZero | 36.52219404998997 | 38 |
| fresh-grid-512 | EvenOdd | 24.695247192556135 | 3.5 |
| fresh-grid-512 | NonZero | 42.965345756177143 | 41.5 |
| frozen-grid-256 | EvenOdd | 24.610695353308316 | 1.5 |
| frozen-grid-256 | NonZero | 42.884971226325959 | 43.5 |
| frozen-grid-64 | EvenOdd | 22.882808769145115 | 6 |
| frozen-grid-64 | NonZero | 35.785905083304918 | 33.5 |
| integer-star-512 | EvenOdd | 6282768 | 6282768 |
| integer-star-512 | NonZero | 6282768 | 6282768 |
| many-levels-128 | EvenOdd | 6017948.7760665873 | 6014880.5 |
| many-levels-128 | NonZero | 9528901.2748766877 | 9524834 |
| repeated-square-512 | EvenOdd | 0 | 0 |
| repeated-square-512 | NonZero | 49 | 49 |
