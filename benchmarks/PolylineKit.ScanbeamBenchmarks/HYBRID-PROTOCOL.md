# Adaptive closed filled area: fixed experiment

The objective is an actual choice between complementary algorithms on the
original dense-crossing losses, rather than further optimizing a general sweep
on GIS intersections where shipping Winding already wins. This protocol and
the selector are fixed before any new timing. A negative result is acceptable;
do not tune on held-out timings or silently broaden a numeric domain.

## One operation and explicit contracts

Every timed geometry method returns one requested NonZero or EvenOdd filled
area of the same implicitly closed walk. This is not two-region intersection,
nor a similarity score. The Winding control calls `ClosedPath` and selects one
result; its current public API also computes three other integrals and diagnostic
counts. This extra work is disclosed, not credited as algorithmic superiority.
Clipper produces contours before reducing them to area. The experimental scalar
wrapper cannot replace the existing four-result `ClosedPath` API.

The new double candidate has a dedicated closed-path mode. It uses original
edges, not intersection with an artificial bounding rectangle. Uncertainty or a
work-limit failure invokes the requested scalar result of shipping `ClosedPath`;
failed sweep and full fallback are both timed. Existing raw/prepared two-region
intersection semantics and all their previous checks must remain unchanged.

Hybrid default is Winding. Choose the integer scanbeam only if all conditions
hold: 64–1024 supplied vertices; at least three retained vertices after Winding's
consecutive-duplicate/closure cleanup; exact integer coordinates within ±2048;
at most 16 distinct endpoint Y levels; mean active nonhorizontal edge count at
least 16 across bands; at least 8 strict proper crossings among 16 deterministically
spaced sampled edges, excluding adjacent pairs. Use exact bounded Int64 signs
for that sample. The selector receives only coordinates, not fixture names,
families, split labels, hashes, measured times or past decisions. It runs on
every call, with bounded stack storage and no new heap allocation. Its counters
describe observed work; early rejections have only prefix statistics.

The coordinate test preserves original inputs without rounding, translation or
scaling. Fractional and wide inputs go to Winding; they remain visible in the
benchmark. The selected integer path uses the previously proven Int64 domain,
exact rational event ordering and approximate binary64 area accumulation. The
general guarded-double candidate is forced for measurement, not automatically
selected before it shows a relevant advantage. Reuse is single-threaded and
non-reentrant. No shipping API, dependency or implementation changes.

## Correctness first

Preserve invalid-input rejection, implicit closure, consecutive duplicates,
fill-rule checks, caller immutability and reuse after failures. On every fixture,
hybrid output must be bit-identical to the selected forced backend. Integer and
double results must agree with Winding within `1e-10 * max(1, abs(Winding))`.
This is an experimental comparison budget, not an arbitrary-input error proof.
Independently check small paths with the exact-rational slab oracle, including
crossings, coincident edges, retracing and both fill rules. Exercise selector
boundaries and mode changes between closed area and both existing intersection
overloads, normally and without hardware intrinsics. Legacy filter-first
validation JSON must stay byte-identical.

Clipper scale is 1e6. The existing converter truncates scaled input coordinates
toward zero; generated intersections are also quantized. Record absolute and
relative output error against Winding rather than treating Clipper as an exact
oracle or claiming identical numeric contracts. Near-coincident paths deliberately
expose precision loss. Clipper preparation variants must produce identical bits.
The near-coincident validation exposed a negative Clipper result before timing.
Retain that raw value and mark it explicitly; do not clamp it or remove its row.
A negative reference area cannot satisfy a targeted Clipper performance gate.

## Inputs, methods and timing

`HybridInputs` freezes 22 inputs: 9 development inputs (the 7 historical controls
plus a simple contour and near retrace) and 13 held-out instances. The held-out
set contains new 128/512-vertex grid seeds, exact-binary fractional grids,
perturbed grids, repeated traversal, star, many-Y walk, simple contour, a sparse
few-Y sawtooth, near retrace and a widely translated grid. Related held-out
instances are not independent evidence across all shape families. Freeze input
hashes and selector thresholds before timings; neither changes after inspection.

Each input uses both fills. Time Winding, Hybrid, forced double sweep,
Clipper-full-input, Clipper-preloaded, and Selector-only; additionally time the
integer sweep wherever its native exact integer domain permits it (±524288,
3–8192 supplied vertices), including cases the selector deliberately rejects.
The selector-only row returns a numeric sink and is not an area method.

Primary timings are complete raw-input calls with warm reusable engines. Hybrid
includes selection plus the chosen call. Full-input Clipper includes coordinate
conversion, Clear/Add, execution and area reduction; preloaded Clipper excludes
conversion/Add and is a separate stronger comparison. Winding computes all its
normal outputs. No result caching. Record backend choice, selector cost, forced
backend cost, guarded fallback/reason/work and allocations. Warm timings do not
describe first use or retained memory.

Use three fresh sequential Release processes, `DOTNET_TieredCompilation=0`,
five calibrated samples per row, >=20 ms target per sample, and 40 ms warmup.
Rotate method order by 0, 2 and 4 places. No concurrent builds, profilers or
other timing workloads. Report medians of process medians and observed ranges,
not confidence intervals. Keep all failed and unfavorable rows.

## Decision criteria

For each of the six dense integer-grid inputs (both historical grids, two prior
fresh grids and two held-out grids), under each fill, hybrid must take at most
0.8 times Winding and 0.8 times full-input Clipper. Every other cell must take
at most 1.10 times Winding. There is no post-hoc exception for small calls.
Report the stronger preloaded-Clipper comparison and worst regret against both
Winding and the fastest admissible forced backend on every input, even if the
primary gate passes. Correct output and all gates are necessary for a positive
bounded conclusion; no universal speed claim or shipping dispatch follows.

Before publishing, the frozen-binary summarizer must reject corrupt matrix,
order, medians, outputs, hashes, validation decisions and input manifests without
overwriting submitted evidence. Publish compact per-cell evidence and source,
runtime, binary and raw-file hashes. Retain raw samples locally, outside the
public source tree. CI checks correctness without collecting timings.

```text
check-hybrid artifacts/scanbeam-hybrid-check
benchmark-hybrid artifacts/scanbeam-hybrid <run 1..3> <measured-commit>
summarize-hybrid artifacts/scanbeam-hybrid
```
