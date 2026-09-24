# Cost of the numerical fixes

The history supports investigating the fixes rather than assuming the original
slowdown came from exact predicates. Most of the filled-region regression came
from how exact boundary chains were constructed and accumulated.

| Revision/change | Correctness requirement | Cost and subsequent improvement |
| --- | --- | --- |
| `7dee036` initial engine | Incomplete numeric/reentrancy handling | Aggregates winding fractions per edge, with few area evaluations; some published failure inputs are wrong. |
| `a0b3295` local origins, projection and workspace lease | Distant regions, tiny coordinates and nested calls | Largely preserves the initial organization and speed. These fixes are not the main measured regression. |
| `533e9ec` exact boundary chains | Tiny XOR cannot be recovered by subtracting large rounded own/intersection totals | Five chains and local bounds replace three totals plus inclusion–exclusion; initially materializes many `Piece` values. |
| `1adefca` and `1094dca` | Preserve those chains | Two walks remove ordinary-piece storage; common-origin groups reuse edge terms; hashing replaces sorting of shared pieces. These already remove substantial overhead from the first repair. |
| `becc37d` / `9fff4d2` area terms and event order | Translated/subnormal triangles, narrow regions and reversed near-coincident events | Compensated relative midpoint terms retain four difference tails. Whole-edge shares preserve original-edge areas. Keyed event sorting also removes a former quadratic tie-repair step. |

The earlier reports put the filled-region increase after exact chains at roughly
47–59% at `474db55`, then 60–77% after the later arithmetic repair. These are
historical measurements under the then-current harness, not fresh controlled
speedups from this follow-up. The [numerical review history](winding-area.md#independent-reviews)
records why reverting the old formula would be incorrect.

## A bounded alternative: do not subdivide an own area unnecessarily

For a loop with no self-crossing event and no collinear-overlap edge, its own
winding weight is constant. Crossings with the other input subdivide its boundary
but do not change its independently filled area. Its OwnA/OwnB chain can therefore
use each original edge once. A-only, B-only and Both still require the complete
sub-edge walk, exact shared-piece netting and separate local origins.

The prototype records a conservative per-loop eligibility mask during the
existing crossing search; it adds no detection pass or index. Overlap with either
input disables the specialization for that loop. It retains `Cross`, difference
tails, compensated sums, winding decisions and exclusive-region accumulation.
Origins for eligible own chains come from original endpoints. Rounded crossing
points can have enlarged the old bounds slightly, so last bits can change;
the new result is checked against standalone `ClosedPath`.

The first prototype applies this whenever eligible. Three-process measurements
show it is about **5% slower on the ordinary filled-region fixtures**, despite
doing fewer own-area additions. It adds a mode branch in the shared inner walk,
and most ordinary edges have few subdivisions.

Two additional fixtures isolate the intended benefit: perpendicular simple combs,
offset by a quarter unit to avoid collinear overlap. Their vertices and own-area
formulas are recorded in the evidence. At 130/514 vertices per path the first
prototype improves the complete operation by **1.09×/1.19×**, respectively.
These intentionally dense fixtures do not establish a general polygon speedup.

The final experimental variant chooses the specialization only when total
crossings exceed total edges, and uses value-type generic specialization for the
two own-area policies. This leaves a choice at the four walk calls rather than
at every sub-edge. The density condition is a workload heuristic: self-crossings
in the other loop can also contribute to the total. It is not a correctness
condition; the separate eligibility mask supplies that condition.

Three more fresh-process runs of that density-gated variant give:

| Input | Vertices per path | Combined baseline, µs | Adaptive own-area, µs | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Ordinary filled regions | 64 | 13.72 | 14.60 | 0.94× |
| Ordinary filled regions | 256 | 56.86 | 64.99 | 0.87× |
| Ordinary filled regions | 1024 | 228.58 | 238.99 | 0.96× |
| Perpendicular combs | 130 | 495.00 | 438.94 | 1.13× |
| Perpendicular combs | 514 | 8668.95 | 7997.77 | 1.08× |

**Neither own-area prototype is retained in the library.** The sparse-case
regressions remain after specialization; the smaller arithmetic count does not
justify the additional common-path cost. The exact cause of the remaining JIT/
code-layout cost was not isolated, so it should not be attributed solely to a
particular branch or instruction. The generic implementation was a hypothesis,
not a guaranteed way to recover the baseline's machine code.

All timings include the full API call and all preparation. Each variant/input
suite has three independent processes, five samples per workload, alternating
baseline/prototype order; there were no simultaneous builds or other benchmarks.
The complete [tables and raw runs](../results/winding/fix-cost/summary.md) include
the original workloads and additional control shapes. All warm samples allocate
zero bytes. The unconditional prototype passed the existing 31,851 checks in
five modes; the adaptive prototype also passed 78 new own-area-independence
assertions, for 31,929 checks per mode. Its tests and code are archived in the
patch rather than added to the maintained implementation.

Reproduction: the baseline is combined implementation `3bfe544`. Apply exactly
one of [always.patch](../results/winding/fix-cost/always.patch) or
[adaptive.patch](../results/winding/fix-cost/adaptive.patch) in an isolated
checkout, build and use the current assembly-comparison runner. Adaptive source
is also captured by experimental commit `dbaa50b`. For the comb suite pass
[combs.json](../results/winding/fix-cost/combs.json) as the fixture argument;
the runner adds its standard control shapes. The JSON contains every coordinate
and the analytic own area; its adjacent Python generator regenerates it. Run
`python scripts/summarize-winding-fix-cost.py` to validate and summarize the
checked-in results.

## What this establishes

There is avoidable work in the organization of a correct repair. However,
performing fewer arithmetic operations does not by itself make a new control
flow faster. Keep the numerical regression suite as the acceptance boundary,
and compare ordinary as well as deliberately favorable inputs.

A more ambitious rewrite would separate input topology from pairwise comparison,
or replace candidate enumeration with an active-edge sweep. Those are discussed
in [the architecture assessment](winding-search.md#architectural-alternatives).
Neither changing language nor removing compensated arithmetic substitutes for
that design work.
