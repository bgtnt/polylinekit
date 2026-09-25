# Solaris polygon-overlap evaluation

**Useful working example; no demonstrated speed advantage over Clipper2.**
PolylineKit reproduces the supplied Solaris scores and provides a faster,
lower-allocation alternative to the configured NTS intersection backend. Clipper2
is faster in the prepared geometry batch; complete evaluator times are effectively
tied. A simple convex algorithm wins on its eligible subset. No Core engine or
public API changes were made for this experiment.

## Correctness and coverage

Measured source: [`2019112c6fd7cdede0e921ef7115ad6d280b2297`](https://github.com/bgtnt/polylinekit/commit/2019112c6fd7cdede0e921ef7115ad6d280b2297).
The [input manifest](data/manifest.json) pins Solaris commit
`5315390942e05e919555088361bd3df42d4f5a18`, original CSVs, evaluation sources and
SHA-256 hashes. This is the supplied six-image evaluator sample, not an independent
production population. Image XY coordinates remain unchanged.

There are 145 prediction rows and 172 truth rows, including an empty sentinel in
each. Original filters leave **144 predictions and 169 truths**. All 315 nonempty
polygons are valid single shells, with 5,771 supplied vertices including closure.
All predictions are convex; truth contains 71 convex and 100 concave polygons.
No invalid geometry, holes or multipart inputs occur.

All four backends reproduce **all 172 expected per-building scores**, with the
original strict absolute-error criterion `<1e-9`; exact key/count checks pass.
Matching decisions agree: **87 TP, 57 FP, 82 FN**. The verifier also passes 2,032
geometry assertions and 94 protocol assertions. Maximum expected-score errors:

| Core | Clipper2 | NTS | Convex with fallback |
|---:|---:|---:|---:|
| 1.33e-15 | 5.03e-12 | 1.55e-15 | 1.67e-15 |

The source semantics include truth area `>=20`, prediction area `>20`, confidence
ordering, removal only at **IoU >0.5**, and retained scores below the threshold.
There are 34 positive subthreshold truth scores. The nearest selected score to
the match threshold is 0.5017826829568274; synthetic controls cover equality and
both sides of the threshold. Historic Python dependencies were not recreated;
the fixed expected CSV is the reference, with C# behavior checked against the
pinned source. [Protocol and numerical tolerances](PROTOCOL.md).

The matching run rejects 3,931 comparisons by bounds and visits **205 candidates**:
162 positive intersections and 43 disjoint pairs whose bounds overlap. Core and
Clipper cover all candidates without fallback. Both operands are convex in only
**49 pairs**; the conservative convex adapter uses NTS for the remaining **156**.
Valid holes/multipart inputs use explicit NTS fallback; invalid custom geometry
is rejected rather than silently approximating Solaris's GEOS repair.

## Complete batches

Milliseconds and allocated bytes per complete invocation. Median of three
process medians; bracketed ranges are the three observed medians, not confidence
intervals. Kernel batches reuse prepared inputs; preparation excludes JSON reading
and pair computation. The complete evaluator includes reading JSON through the
warm file cache, validation, fresh preparation, own areas, matching and result
rows. Final JSON serialization/disk write is excluded equally.

| Backend | Kernel, 205 pairs (ms) | Kernel bytes | Preparation (ms) | Preparation bytes | Complete evaluator (ms) | Evaluator bytes |
|---|---:|---:|---:|---:|---:|---:|
| Core | 2.308 [2.287–2.323] | 989,080 | 1.757 [1.749–1.764] | 2,250,808 | 8.185 [8.064–8.326] | 4,529,944 |
| Clipper2 | 2.062 [2.036–2.072] | 1,260,856 | 2.015 [1.987–2.055] | 2,818,056 | 8.222 [8.175–8.322] | 5,379,912 |
| NTS | 17.721 [17.693–18.000] | 14,983,344 | 1.767 [1.728–1.800] | 2,250,808 | 24.958 [24.309–25.045] | 18,524,320 |
| Convex + NTS fallback | 14.599 [14.585–15.067] | 12,069,336 | 1.840 [1.833–1.855] | 2,346,528 | 21.984 [21.571–22.308] | 15,706,032 |

Core's prepared geometry batch is **11.9% slower than Clipper**, with **21.6%
fewer allocated bytes**. Its complete evaluator uses **15.8% fewer bytes**; the
0.5% median time difference is within the observed variation, and their ranking
changes between processes. There is no demonstrated end-to-end speed win over
Clipper. Against the configured NTS backend, Core is **7.68× faster** in the
kernel and **3.05× faster** in the complete evaluator.

The kernel includes the shared NTS `Intersects` predicate when an area is zero,
to distinguish touching from disjoint shapes under the source protocol. This
shared work accounts for Core's 989,080 kernel bytes; the 162 positive pairs
allocate **0 bytes** in warmed Core calls. The whole kernel and evaluator are
not allocation-free. Own areas and validation are shared NTS work for all backends.

## Geometry subsets

Each cell is **batch milliseconds / allocated bytes**. Subsets overlap and their
times must not be added. Vertex bands count both inputs, including supplied closure.
Full precision and process ranges for all 40 rows are retained in raw `summary.csv`
and `summary.md`.

| Subset | Pairs | Core | Clipper2 | NTS | Convex + fallback |
|---|---:|---:|---:|---:|---:|
| Both convex | 49 | 0.406 / 153,528 | 0.284 / 205,152 | 3.416 / 3,067,536 | 0.179 / 153,528 |
| Has concavity | 156 | 1.897 / 835,552 | 1.740 / 1,055,704 | 14.331 / 11,915,808 | 14.210 / 11,915,808 |
| Positive area | 162 | 1.077 / 0 | 0.803 / 233,112 | 14.847 / 12,497,352 | 11.878 / 9,794,848 |
| Zero area | 43 | 1.134 / 989,080 | 1.125 / 1,027,744 | 2.663 / 2,485,992 | 2.471 / 2,274,488 |
| Vertices ≤32 | 43 | 0.283 / 116,464 | 0.236 / 163,816 | 2.667 / 2,324,960 | 1.228 / 1,049,768 |
| Vertices 33–64 | 143 | 1.555 / 692,968 | 1.358 / 885,488 | 12.855 / 10,785,376 | 11.408 / 9,714,112 |
| Vertices >64 | 19 | 0.443 / 179,648 | 0.402 / 211,552 | 2.469 / 1,873,008 | 1.833 / 1,305,456 |

For two certified convex polygons, Sutherland–Hodgman is sufficient and **2.27×
faster than Core**, or **1.59× faster than Clipper**, in this batch. It is not the
best full evaluator here because 156 pairs fall back to NTS. Every prediction
happens to be convex; a separate method exploiting just one convex operand could
have wider coverage. That method was not implemented or measured, so the current
convex fallback rate does not establish a mathematical limit.

## Reproduce and inspect evidence

Measurements ran on 2026-09-25 21:26–21:27 UTC (September 26 locally): Windows x64
build 26200, Intel Core i9-9900K 3.60 GHz, 8 cores/16 logical processors, SDK
10.0.401, runtime .NET 10.0.12. Release; `DOTNET_TieredCompilation=0`; AVX2/FMA
available. Clipper2 2.0.0 uses scale **1e10**; NTS is 2.6.0. Core receives original
doubles, with no quantization to select the integer specialization.

Three sequential fresh processes rotate backend order. Every cell warms for
40 ms, calibrates a power-of-two repetition count to at least 20 ms (cap 65,536),
then records five samples: **600 samples across 40 aggregate rows**. No competing
builds/benchmarks ran. These are warmed repeats of one fixture, not cold-start
measurements. Source and protocol were committed before timing; no source
optimization followed the observations.

Use the [run instructions](README.md#run) for preparation, verification and the
working example, and [measurement commands](README.md#measure) for a new run.
To reproduce the measured source in a separate checkout:

```sh
git worktree add --detach ../polylinekit-solaris-2019112 2019112c6fd7cdede0e921ef7115ad6d280b2297
cd ../polylinekit-solaris-2019112
python examples/PolygonOverlapEvaluation/data/prepare-data.py
dotnet restore examples/PolygonOverlapEvaluation --locked-mode
dotnet run --project examples/PolygonOverlapEvaluation -c Release -- verify-reference
```

Then execute the PowerShell measurement commands from that checkout's README.
`summarize.py` requires Python 3.9+ (measured with 3.12.3). The read-only summarizer
checks source/binary/data identities, the full candidate schedule, all samples,
result checksums and median arithmetic. CI checks correctness and startup without
a timing threshold.

The complete original local evidence is
`artifacts/polygon-overlap-measured-2019112/`, archived as
`artifacts/polygon-overlap-measured-2019112.zip` (590,346 bytes; SHA-256
`c1b5b24b0a632d80df78a9182abc452878ce50f96dc60a14a2e33241f8f5f427`).
It contains `run-1.json` through `run-3.json`, raw ticks/bytes/checksums,
`launch-identity.json`, `process-order.json`, `evidence.json`, summaries, reference
verification, example output, the converted fixture, measured binaries and frozen
summarizer. It is **not uploaded publicly**. The data's redistribution terms are
not separately clear; the repository provides the pinned downloader instead of
the original or converted polygons.

Converted fixture SHA-256:
`883b7d327f1e082a8a34bbe065a40d8fb5a89bddd5d5c1014be5fbcf62fd4b20`.
Protocol SHA-256:
`fb7d87f14bb963839b5610d39cebabba5343e3b934322d6cdeffe6c4b6a0a3ec`.
Raw process SHA-256 hashes:

| File | SHA-256 |
|---|---|
| run-1.json | a2b371904e9e2d09f0cae182662ed95c87fccc5784c920016ce77ed56dce63bd |
| run-2.json | e38a84f2767726b6a11160cb2921b531ddf1784c1afb4a7ddfc9adfcc504393c |
| run-3.json | d7155b71344de155668bece51a1b486eefd251c15e4c90832ffe87cc9d1a4859 |

## Decision

Keep this as a useful C# annotation-evaluation example. It demonstrates full
reference compatibility for the supplied valid single-ring inputs and a clear
time/allocation benefit over the configured NTS overlay. It does **not** justify
replacing Clipper for speed, or claiming universal geometric superiority.

A consumer needing only numeric overlap on validated rings can use Core's small,
dependency-free API. This comparison executable itself depends on NTS and Clipper;
its full dependency footprint must not be described as Core's. Before promoting
it into a production adapter, obtain a concrete C# consumer's inputs, invalid/
holes/multipart policy, throughput needs and acceptable precision, then measure
that workload. The completed bounded experiment does not require another dataset
or another engine rewrite.
