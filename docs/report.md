# Evaluation and continuation decision

**Continue with a small graph-area utility for independent review. Do not expand it into a universal polyline matcher on this evidence.** The measurable advantage is continuity under bounded geometric perturbations, compared with the published LIP regional weighting. A simpler, allocation-free implementation is also useful on this domain. This is an established area integral, not a newly discovered algorithm.

Repository: **https://github.com/bgtnt/polylinekit**.

Measured source revision: [`49fe47b9bd8c77d43b832351896958d779c140a5`](https://github.com/bgtnt/polylinekit/commit/49fe47b9bd8c77d43b832351896958d779c140a5). Subsequent report/packaging changes do not alter the measured methods. The final delivery commit is reported with the handoff and in the local package's repository metadata; it cannot be embedded in a file inside that same commit without changing its hash.

## Near-touch reproduction

Use already positioned paths:

```text
P = [(0,0), (.5,0), (1,0), (1.5,0), (2,0)]
Q = [(0,0), (.5,.25), (1,epsilon), (1.5,.25), (2,0)]
```

Only one y coordinate changes. Moving it from `+1e-6` to `-1e-6` changes the entire graph by at most `2e-6` vertically.

| epsilon | Unweighted area | LIP | GenLIP p=0 | Nonzero lobes |
|---:|---:|---:|---:|---:|
| +0.000001 | 0.25000049999999996 | 0.25000049999999996 | 0.25000049999999996 | 1 |
| 0 | 0.25 | 0.125 | 0.125 | 2 |
| −0.000001 | 0.249999500004 | 0.12499950000244722 | 0.12499950000244722 | 3 |

For the negative case, the two main lobes each have area approximately `0.124999750001` and weight `0.499999000003789`. The tiny middle lobe has area approximately `1.999992e-12` and weight `1.999992e-6`. Positive epsilon produces one area with weight 1. These independently reconstructed polygons and lengths are saved in [geometry.json](../results/geometry/geometry.json).

As epsilon approaches zero from above, LIP tends to `1/4`; from below it tends to `1/8`. The unweighted sum tends to `1/4` from both sides. Ignoring the exact-tangency input cannot repair the unequal one-sided limits. This establishes an actual discontinuity, not merely different numeric scales or a bug in a downloaded implementation.

Analytically, the unweighted area is `1/4 + epsilon/2` for nonnegative epsilon. For `epsilon=-delta<0`, it is `1/4-delta/2+4*delta²/(1+4*delta)`. The implementation, separate intersection-polygon reference and Clipper agree within the declared tolerances. The tiny middle triangle can be below Clipper's effective integer resolution; the analytic and polygon references check it without depending on that quantization.

All four segment pairs pass GenLIP's strict goodness criterion, forming one good group. The result consequently does not depend on lookahead, the bad-pair fallback `d`, or ambiguous bad-segment ownership. This is the most defensible shared domain for the comparison.

## Does the advantage survive reasonable repairs?

- Removing exact collinear vertices leaves the LIP result unchanged, including the jump. The GenLIP reconstruction also retains it after merging, subject to its documented tail boundary choice.
- Allowing parallel segments to be good does not affect these nonparallel segment pairs.
- Snapping coordinates can suppress the jump near zero, but relocates it. At a `1e-5` y grid, epsilon values `4.999999e-6` and `5.000001e-6` differ by `2e-12`, yet the snapped LIP scores are `0.125` and `0.250005`. See [stability.json](../results/geometry/stability.json).
- General smoothing may be appropriate when supplied with an application noise scale, but was not proved ineffective here. Replacing intersection-dependent weights with fixed/continuous weights can fix the structural defect too; that defines another objective. This experiment does not claim unique superiority over such redesigns.

A separate parallel-subdivision effect **fails** the fair-repair test: strict GenLIP for two length-10 lines one unit apart returns `10/n` for `n` equal segments. Both collinear merging and the parallel-good correction recover 10. We record it in [parallel-subdivision.json](../results/geometry/parallel-subdivision.json), but do not use it as our reason to continue.

## Scope and tradeoffs

The continuation criterion is met for existing-coordinate, increasing-x graphs where accumulated area and continuity are desired. It is **not** met for an all-purpose route similarity library. The candidate rejects vertical/backtracking/closed paths; GenLIP's broader intended domain is not matched by this API. Its experimental reconstruction is `p=0` only and rejects underspecified ownership/tail cases; see [the explicit audit](baselines.md).

Unsigned area is not a maximum-deviation detector: the checked narrow-detour fixture reaches height **100** while its area is only **0.0001**. Widespread noise accumulates. LIP's weighting may be useful where many small lobes should be suppressed. No labeled retrieval dataset or downstream developer workload was tested, so no better classification/ranking accuracy is claimed.

For complex walks, Clipper NonZero, EvenOdd and absolute net winding give distinct results. A twice-traversed area-4 square yields **4 / 0 / 8**. A square traced forward and backward yields **0 / 0 / 0**, while counting its bounded geometric face once would yield 4. These are different definitions, not competing numerical accuracy scores. See [contour inputs and results](../results/geometry/contours.json).

## Benchmark methodology

The complete [time/allocation table](../results/benchmarks/summary.md) is generated from three independent process outputs: [run 1](../results/benchmarks/run-1.json), [run 2](../results/benchmarks/run-2.json), [run 3](../results/benchmarks/run-3.json). CSV versions and all nine batch samples per method/fixture/process are included. There are 4 sizes × 3 densities × 6 methods × 3 processes = **216 measurements**, containing **1944 batch samples**. Inputs and their SHA-256 hashes are recorded.

Each path has 16, 64, 256 or 1024 vertices. `none` has no interior crossings; `sparse` has seven sign changes; `dense` changes sign in every interval, producing 16/64/256/1024 nonzero lobes. The two paths have equal corresponding arc lengths and acute nonzero angles, keeping GenLIP in its good-case regime. These are synthetic graph fixtures, not arbitrary GPS trajectories; they intentionally isolate comparison and intersection density. Normalization, alignment and fixture construction are outside timing.

Each method is warmed for at least 60 ms, then calibrated to a batch lasting at least 20 ms (or a cap of 1,048,576 calls). Nine samples follow; GC is collected outside each sample. The recorded allocation metric is `GC.GetAllocatedBytesForCurrentThread`, not peak/native memory. Sample medians and the range of three process medians are reported. Method order rotates between processes. `DOTNET_TieredCompilation=0` gives steady optimized JIT code without tier promotion during samples. Run 1 was repeated after a local package build to remove concurrent compiler interference; only that replacement is in the final table. Initial five-method pilot runs are not used in the table.

The timed LIP baseline is an `O(n+m)` specialized graph sweep too, not the slower all-pairs polygon oracle. Both it and the candidate validate inputs and allocate zero managed bytes in these measurements. GenLIP includes duplicate cleanup, goodness processing, group copies and its result object. ClipperFull includes point conversion and union; ClipperPrepared excludes conversion. ClipperPreparedTransposed also exchanges x/y before timing, preserving area and exposing sweep-orientation effects. It is an intentionally favorable preprocessing control for Clipper.

Environment: Windows 11, OS build **10.0.26200**, Intel **Core i9-9900K @ 3.60GHz**, 8 cores / 16 logical processors, x64; .NET SDK **10.0.401**, runtime **10.0.12**, MSBuild **18.9.11**. Measurements ran on a shared workstation without fixed CPU affinity, locked frequency or a dedicated performance lab. They support local cost comparisons, not portable speed guarantees.

The dense 1024-vertex fixture exposes a substantial Clipper orientation effect: swapping x/y reduces its cost from tens of milliseconds to below one millisecond. Any claim based only on the original orientation would exaggerate the advantage. The graph-only sweep remains cheaper on these fixtures even against that control; Clipper provides a much broader operation.

Representative results at 1024 vertices per path (microseconds, median of three process medians):

| Method | No crossings | Dense crossings | Managed bytes/op, dense |
|---|---:|---:|---:|
| Unsigned graph area | 53.10 | 55.50 | 0 |
| Specialized LIP sweep | 68.42 | 74.25 | 0 |
| GenLIP p=0 reconstruction | 131.71 | 138.27 | 131632 |
| Clipper prepared, transposed | 191.35 | 450.79 | 968552 |

The candidate takes about 22–25% less time than the specialized LIP sweep in these two cases. The operations deliberately compute different objective functions; a timing difference alone does not imply better similarity quality. Baseline implementation costs are not lower bounds on LIP, GenLIP or Clipper implementations.

Reproduce on PowerShell with `scripts/benchmark.ps1`; it launches three processes. A shell-independent manual equivalent is to set `DOTNET_TieredCompilation=0`, build Release, and invoke the following three times with run numbers 1, 2 and 3:

```text
dotnet experiments/PolylineKit.Experiments/bin/Release/net10.0/PolylineKit.Experiments.dll benchmark artifacts/reproduced-benchmarks RUN COMMIT_SHA
```

Generate the summary with `pwsh -File scripts/summarize-benchmarks.ps1 -Directory artifacts/reproduced-benchmarks`. Geometry evidence is regenerated separately with the `evidence` command in the README.

## Validation and delivery

The Release solution build passed with no warnings or errors. The deterministic console harness passed **1351** analytic, independent-geometry, formula, metamorphic and rejection checks. It includes identity, crossing lobes, collinear subdivision, unequal sampling, bounded noise, overlap, holes, loops/retracing, a narrow detour, mixed GenLIP good/bad groups and invalid input contracts. The console example returned area 1 and mean separation 0.5.

The local `0.1.0-alpha.1` package was packed, its MIT/README/XML/repository metadata and empty runtime dependency graph inspected, and installed into a fresh .NET 10 consumer using only a local feed and an isolated cache. It has **not** been published to NuGet.org. CI builds, checks, runs the example and packs on Windows and Linux; the handoff reports its observed final status.

Proceed to the [independent review checklist](review-checklist.md). A reviewer should first validate the mathematical contract and baseline fidelity, then decide whether this narrowly scoped utility meets a real consumer need. The public API remains an alpha. Further generalization is a separate decision, not a promise made by these results.
