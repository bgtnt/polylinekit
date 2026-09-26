# Selected-shape area measurements

Measured source: [`cc888de`](https://github.com/bgtnt/polylinekit/commit/cc888de062e0903b6d524656fe5612c219cb1538),
2026-09-26. The library implementation is unchanged from `6b13fa3`; this commit
adds the maintained reproducer and fixture. The later documentation commit is
not the measured binary. [Protocol and reproduction commands](SYNTHETIC.md).

.NET 10.0.12, Windows build 26200, x64, Intel Family 6 Model 158 Stepping 12,
16 logical processors, tiered compilation disabled. Clipper2 C# 2.0.0, scale 1e6.
Three sequential processes, five selected fill cases, four methods and seven
batches per method produced **420 samples**. This is a focused confirmation of
retained optimizations, not a population sample or a universal ranking.

## Requested work and interpretation

`Public` is `PolylineArea.FilledArea`. `ClosedPath` returns all four integrals
through the general engine. Clipper uses Union with the requested fill plus area
summation, producing contours. `Clipper-preloaded` excludes input conversion/Add;
`Clipper-full` includes conversion/Clear/Add with reusable engines. The timing
ratios below are Clipper / Public or ClosedPath / Public; above 1 favors Public.

The star is simple and has no self-intersections. A known-simple ring needs only
a shoelace sum, which this table does not time. The dense grid has 512 supplied
vertices and 64 distinct points with crossings, contacts and repeated edges.
The square repeats four edges 128 times; its fill areas are exactly 49 (NonZero)
and 0 (EvenOdd). Recognizing/simplifying this constructed repetition before
clipping is not measured. The grid and square qualify for the .NET 10 integer
specialization; the star uses the general engine. These gains do not imply an
intersection or IoU speedup, or apply to every input representation/runtime.

Times are medians of three process medians. Process ranges are observed variation,
not confidence intervals. All Public warm samples recorded **0 B/op**; first use
and retained memory are excluded. General ClosedPath allocates about 10 MB per
call on this grid under the 4 MiB retention policy. Its unfavorable rows remain
visible. The library retains no oversized workspace just to obtain zero B/op.

| Input | Fill | Public us | Preloaded Clipper / public | Full Clipper / public | ClosedPath / public |
|---|---|---:|---:|---:|---:|
| simple-spiky-star-4096 | NonZero | 2193.412 | 25.905 | 26.021 | 0.995 |
| frozen-grid-256 | NonZero | 3057.537 | 1.648 | 1.645 | 2.752 |
| frozen-grid-256 | EvenOdd | 2945.356 | 3.033 | 3.048 | 2.819 |
| repeated-square-512 | NonZero | 29.726 | 16.590 | 17.041 | 207.565 |
| repeated-square-512 | EvenOdd | 28.382 | 44.826 | 44.726 | 217.732 |

## Every measured method

| Input | Fill | Method | us | Process range us | B/op | Process range B/op |
|---|---|---|---:|---:|---:|---:|
| simple-spiky-star-4096 | NonZero | Public | 2193.412 | 2179.619–2250.356 | 0 | 0–0 |
| simple-spiky-star-4096 | NonZero | ClosedPath | 2181.713 | 2177.562–2195.256 | 0 | 0–0 |
| simple-spiky-star-4096 | NonZero | Clipper-preloaded | 56820.700 | 56478.100–57974.700 | 427032 | 427032–427032 |
| simple-spiky-star-4096 | NonZero | Clipper-full | 57074.100 | 56540.800–57197.900 | 558568 | 558568–558568 |
| frozen-grid-256 | NonZero | Public | 3057.537 | 3051.162–3082.637 | 0 | 0–0 |
| frozen-grid-256 | NonZero | ClosedPath | 8415.175 | 8410.475–8780.350 | 10045912 | 10045912–10045912 |
| frozen-grid-256 | NonZero | Clipper-preloaded | 5039.538 | 5036.950–5043.550 | 352240 | 352240–352240 |
| frozen-grid-256 | NonZero | Clipper-full | 5029.762 | 5001.113–5044.775 | 369016 | 369016–369016 |
| frozen-grid-256 | EvenOdd | Public | 2945.356 | 2928.781–2952.969 | 0 | 0–0 |
| frozen-grid-256 | EvenOdd | ClosedPath | 8302.225 | 8112.025–8309.650 | 10045912 | 10045912–10046416 |
| frozen-grid-256 | EvenOdd | Clipper-preloaded | 8933.675 | 8899.300–9024.300 | 2012488 | 2012488–2012488 |
| frozen-grid-256 | EvenOdd | Clipper-full | 8977.525 | 8913.675–9549.825 | 2029264 | 2029264–2029264 |
| repeated-square-512 | NonZero | Public | 29.726 | 29.351–30.268 | 0 | 0–0 |
| repeated-square-512 | NonZero | ClosedPath | 6170.137 | 6151.725–6203.438 | 0 | 0–0 |
| repeated-square-512 | NonZero | Clipper-preloaded | 493.163 | 492.052–496.897 | 47416 | 47416–47416 |
| repeated-square-512 | NonZero | Clipper-full | 506.572 | 506.144–509.006 | 64192 | 64192–64192 |
| repeated-square-512 | EvenOdd | Public | 28.382 | 28.348–28.484 | 0 | 0–0 |
| repeated-square-512 | EvenOdd | ClosedPath | 6179.700 | 6159.837–6197.337 | 0 | 0–0 |
| repeated-square-512 | EvenOdd | Clipper-preloaded | 1272.256 | 1262.441–1280.231 | 978240 | 978240–978240 |
| repeated-square-512 | EvenOdd | Clipper-full | 1269.431 | 1260.453–1294.944 | 995016 | 995016–995016 |


## Numerical checks

Public agrees with ClosedPath within `1e-12 * max(1, abs(area))`; the grid check
is cross-engine regression coverage, not an independent exact oracle. Analytic
star/square checks pass. Inputs, repeated outputs and all process identities
agree. Clipper preparation variants agree. The integer grid and square input
vertices remain unchanged at scale 1e6, but constructed intersections/output
still use Clipper's grid; star input vertices are quantized. Recorded differences
from the original-double area are not treated as equal exact geometry.

| Input | Fill | Public area | Clipper area | Clipper minus Public |
|---|---|---:|---:|---:|
| simple-spiky-star-4096 | NonZero | 0.47123871322667998 | 0.471238780912 | 6.768532e-08 |
| frozen-grid-256 | NonZero | 42.884971226325945 | 42.884968310096497 | -2.91622945e-06 |
| frozen-grid-256 | EvenOdd | 24.610695353308319 | 24.6106927448335 | -2.60847482e-06 |
| repeated-square-512 | NonZero | 49 | 49 | 0 |
| repeated-square-512 | EvenOdd | 0 | 0 | 0 |

Six malformed-evidence controls were rejected: missing row, changed area, changed
median, negative allocation, changed binary hash and overlapping process windows.
Each failed before writing derived output; unchanged evidence was accepted.

## Evidence identity

Full raw samples and the frozen measured binaries remain local under ignored
`artifacts/`; they are not public downloads. The table below identifies those
files. All inputs and the runner are public, so a clean checkout of the measured
commit can generate a new complete run using the commands in the protocol.

| Binary or local evidence file | SHA-256 |
|---|---|
| `PolylineKit.AreaBenchmarks.dll` | `e5bbb7dad25cec5325bfed8cc87d2a48e9ce71aed220169b14694df698e150d0` |
| `PolylineKit.Winding.dll` | `96567f5475d1a14f403c73211213285d22f0acdd6b8fdc77314d3d43f7a6c333` |
| `Clipper2Lib.dll` | `e9bf01e01f494ef6072cd46c792ed4f3004288a92b1e98c1f44931e558b8a5d0` |
| `run-1.json` | `5b49c7b005fe38b502cb512c8a501adae76e6fa64c04df7364d786ad50cfd310` |
| `run-2.json` | `a965febc110f2fc7ea1175eb72ec26564065599b34293d156bd39b221adfbb9a` |
| `run-3.json` | `f64d338efe8ccbd2b9caf879c7bac6e0e7d26fdeab5f1a02453552db687a4f3e` |
| `process-order.json` | `017804f7eab6f7f4492f380db94e829854841fd2142675e6210a871948907db1` |
| `validation.json` | `3e1aff6f16e457245398f01b40b6484afc9ef98c347f8c91efac0e336aabce97` |
| `evidence.json` | `2a56bff3526f93e443d6593708be452856760ae862ff3384f2f4342dd9350ac7` |
