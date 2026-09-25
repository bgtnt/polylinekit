# Intersection-only coverage: measured advantage

`WindingArea.IntersectionArea` takes **26-29% less time than the direct reusable
Clipper64 baseline** in all four predeclared whole-coverage gate cells. The
ratios are **1.35-1.41x**, with and without fresh input/index preparation. Warm
traversals allocate **0 managed bytes** versus **625,088 bytes** for Clipper.
The old five-area operation remains available and unchanged.

This gives a specific reason to consider the leaf library: frequent area-only
coverage requests over the measured kind of regions, where output contours are
unnecessary and allocation matters. It establishes a bounded workload benefit,
not a universal replacement for Clipper, a new mathematical area definition,
or evidence of user demand. A consumer needing output contours should use an
operation that produces them. The general dense-crossing losses are not fixed
by this change.

## What changed

```csharp
double intersection = WindingArea.IntersectionArea(zone, query);
double? coverage = zoneArea > 0 ? intersection / zoneArea : null;
```

The method returns one `double` with the same independently applied NonZero or
EvenOdd fill rule, input validation and numerical limits as
`FilledRegions(...).IntersectionArea`. It accepts the same self-intersecting,
retraced and collinear input walks. It reuses crossing discovery and exact
orientation decisions, but accumulates only the intersection chain. Outside
pieces require no area terms. An empty chain returns zero after its topology
and net contributions have been established.

The intersection keeps its own net-bounds origin, exact shared-edge coefficient
cancellation, robust area terms and scaled sub-edge fractions. Collinear pieces
retain their first-occurrence order during netting even when their intersection
coefficient is initially zero. No tolerance, clamp, precision or input contract
was weakened. There is no new runtime dependency or new prepared-edge cache.

## Whole-operation result

The [protocol](INTERSECTION-PROTOCOL.md) was fixed before timing. Every required
ratio had to reach **1.25x**, equivalent to at least 20% less time than Clipper.
All four passed. Times are milliseconds per whole population traversal, with
the median of three process medians and their min-max range.

| Direction / scope | Old full Winding | Intersection only | Direct Clipper | Clipper / new |
|---|---:|---:|---:|---:|
| county-zones / warm-indexed-table | 13.2454 (13.1277-13.5287) | 9.6763 (9.6614-9.7761) | 13.2900 (12.6699-13.3307) | 1.373 |
| county-zones / prepare-plus-one-table | 13.4270 (13.4111-13.4490) | 9.8284 (9.7701-9.9660) | 13.2941 (12.8356-13.7518) | 1.353 |
| district-zones / warm-indexed-table | 12.9006 (12.7840-13.2036) | 9.3343 (9.3252-9.3748) | 12.6577 (12.6015-13.0330) | 1.356 |
| district-zones / prepare-plus-one-table | 13.1485 (12.9454-13.2652) | 9.4412 (9.4325-9.5441) | 13.2726 (12.6483-13.9477) | 1.406 |

The direction changes which region supplies the coverage denominator. Every
table includes all 1,078 potential pairs: 211 bbox candidates undergo geometry,
and 867 are rejected by common bounds checks. It does not allocate an output
matrix. No pair area is cached between traversals. Both methods start from the
same frozen 109 admitted Census rings and common coordinate translation.

Clipper2 2.0.0 uses the existing prepared `ReuseableDataContainer64` inputs and
reused engine, executes one intersection, then sums output areas. Its 1e-6 metre
grid, own-area denominator and preparation policy are unchanged. Winding receives
binary64 coordinates and still validates/copies them and finds crossings per
call. NTS remains an independent agreement check, not an exact oracle.

For preparation plus one table, new managed allocation is **18,496 bytes** with
county zones and **34,480 bytes** with district zones. Clipper uses **1,634,000**
and **1,648,464 bytes** respectively. All 90 raw new-method allocation samples
for warm candidate/linear/indexed scopes are zero. First use, retained workspace,
growth and nested calls are excluded from that warm claim; fresh sessions here
also have already-warmed JIT and Winding workspace, not cold processes.

## All measured scopes

Each cell below is median milliseconds per table/preparation. Eight-table sessions
divide total time and allocation by eight. Ranges, allocations, scope outputs and
all 36 aggregate rows are in the [evidence manifest](../../benchmarks/intersection-evidence.json).

| Direction | Scope | Old full Winding | Intersection only | Direct Clipper |
|---|---|---:|---:|---:|
| county-zones | prepare-catalogues-and-index | 0.1460 | 0.1457 | 0.4121 |
| county-zones | warm-candidate-geometry | 13.2614 | 9.6130 | 12.8696 |
| county-zones | warm-linear-table | 13.0998 | 9.6346 | 12.8174 |
| county-zones | warm-indexed-table | 13.2454 | 9.6763 | 13.2900 |
| county-zones | prepare-plus-one-table | 13.4270 | 9.8284 | 13.2941 |
| county-zones | prepare-plus-eight-tables | 13.3581 | 9.7351 | 12.8345 |
| district-zones | prepare-catalogues-and-index | 0.1706 | 0.1705 | 0.4301 |
| district-zones | warm-candidate-geometry | 12.9666 | 9.3053 | 12.9031 |
| district-zones | warm-linear-table | 12.7831 | 9.3079 | 13.0228 |
| district-zones | warm-indexed-table | 12.9006 | 9.3343 | 12.6577 |
| district-zones | prepare-plus-one-table | 13.1485 | 9.4412 | 13.2726 |
| district-zones | prepare-plus-eight-tables | 12.8393 | 9.3336 | 12.4497 |

The improvement over old Winding in the same build attributes the gain to unused
area work rather than a different outer index, data selection, input preparation
or Clipper wrapper. Some process variation remains. Ranges are descriptive, not
confidence intervals; no CPU affinity/frequency lock or significance claim.
This workload was already observed before optimization, not reserved test data.

## Correctness and validation

- New and full Winding return **bit-identical intersection and coverage values
  on all 2,156 directional pairs**. All numerical budgets remain satisfied.
- Winding's maximum intersection disagreement with NTS is 1.431e-6 square metres;
  Clipper's is about 0.0262 square metres. Both satisfy the declared 1 square metre
  and 1e-8 coverage budgets. This is agreement, not a universal accuracy bound.
- The five implementation modes pass 181,272 checks on the portable target and
  181,184 each on modern/default, forced scalar, no-AVX and no-intrinsics modes.
  This includes 349 new direct API checks, existing overlap parity assertions,
  independent exact-area and extreme sub-edge regressions. No tolerance changed.
- Existing operation outputs remain bit-identical in a before/after dump of 120
  frozen operation records. Standalone, portable and precompiled consumers pass.
- Release solution build: zero warnings/errors. Coverage checks: 2,859 adapter
  and 2,840 bounds-index assertions, plus all pair and index-result checks.
- The summarizer validates every sample, median, matrix cell, assembly/data
  identity and regenerated scope result. Five corruption controls are rejected;
  a synthetic unfavorable timing control correctly emits a failed decision gate.
  The latter is a validator test, not a benchmark observation.

## Reproduce

Measured source: [`f93cc25d589a471e405af9ed53d094dd311aac61`](https://github.com/bgtnt/polylinekit/commit/f93cc25d589a471e405af9ed53d094dd311aac61).
2026-09-25 UTC; SDK 10.0.401 / .NET 10.0.12; Microsoft Windows 10.0.26200;
x64 Intel Core i9-9900K; `DOTNET_TieredCompilation=0`. Three fresh sequential
processes, 36 rows each and five batches per row: **540 timing samples**.
The [protocol commands](INTERSECTION-PROTOCOL.md#commands) reproduce the run.
No concurrent builds or benchmark jobs ran during measurement.

The [manifest](../../benchmarks/intersection-evidence.json) records actual binaries,
input identity, complete aggregate results, accuracy summaries, gate cells,
validation counts and raw-file hashes. Raw pair values, timings, first-use records,
old/new frozen-output dumps and generated tables remain locally in
`intersection-evidence-f93cc25.zip` (471,261 bytes), SHA-256
`10bf61bee6bceeacf3967a4ad016c7370ad82c13c191d2ca2f91c129fa811fec`. This is a retained local archive, **not a public
download link**. Public inputs and runners are sufficient to repeat the experiment.
Keep the measured assemblies to re-summarize these exact raw files after later builds.

## Decision and next architecture question

Use the single-output method for intersection/coverage requests. Keep
`FilledRegions` when union/XOR or all region metrics are required. This measured
gain justifies a narrow performance claim; parity alone would not have passed.
The dynamic simplification index's separate gains are not counted as area-engine
speedups.

Clipper's operation-specific contribution selection supports the same principle:
compute the requested operation. Its active-edge scanbeams, inversion-based
intersection list and separate horizontal handling offer a concrete direction
for the still-slower dense-crossing cases. These mechanisms are visible in the
[official release-era engine](https://github.com/AngusJohnson/Clipper2/blob/4d363dcb51c193c2f1883440962024b044f11953/CSharp/Clipper2Lib/Clipper.Engine.cs).
This is a source-based hypothesis, not a measured explanation of every loss.

No Clipper source was copied into this change. An active-sweep prototype needs
its own equivalence and performance experiment: Clipper's integer rounding and
grid-scale join rules cannot silently replace the existing binary64 contract.
Any future source adaptation must retain its
[Boost license notices](https://github.com/AngusJohnson/Clipper2/blob/4d363dcb51c193c2f1883440962024b044f11953/LICENSE).
