# Adaptive closed area: second selector experiment

This is an explicitly subsequent experiment, fixed after inspecting the first
three-process run at `bb10c6b72a8a8a48f022740bf564ca79b7588ed4` and before
collecting any second-stage timings. Preserve that failed run, its source,
frozen binary, manifest and raw archive. Do not pool either series or describe
the first 22 fixtures as unseen validation of this revised selector.

The first policy passes all 12 dense-grid gates but fails preservation on the
sparse few-Y sawtooth: roughly 10 microseconds of selection adds 36% to Winding.
It also misses a much faster integer route on repeated contours (about 30 us
versus 6 ms). Those are classifier and classification-cost failures, not area
algorithm regressions. Correct both through the following new fixed policy.

## Policy change

After the 64–1024 supplied-count check, sample the same 16 deterministic edge
positions first. Check sampled endpoint coordinates for exact integer ±2048
admissibility before bounded Int64 orientation arithmetic. Count strict proper
crossings and identical undirected nonzero edge pairs, excluding adjacencies.
Proceed if either count is at least 8; otherwise immediately choose Winding.
Only a positive sample performs full-coordinate validation, retained-vertex
counting, ≤16 distinct Y levels and mean active-edge count ≥16. Every coordinate
must still satisfy the original fast integer domain before integer execution.
Fractional, wide or invalid unsampled points cannot bypass that final validation.

The geometric sample and its thresholds are fixed now; no label/hash/time-based
selection, point rounding, recentering or scaling. Coincident sample segments
represent a second form of expensive input, not evidence of proper crossings.
Sampling remains sensitive to cyclic starting point, reversal and subdivision;
this is a heuristic performance policy, not a shape-invariant classifier.

Add six new confirmation inputs before timing: a new transformed/reversed/cyclic
grid, repeated diamond, opposite repeated traversal, shifted/reversed sparse
sawtooth, binary fractional grid and widely translated grid. Seeds and points
are fixed in source. Keep the original 22 inputs untouched: 9 development and
13 originally held-out instances, now reused. The six confirmation instances
are the only newly introduced cases; they still belong to related families.

## Measurement and acceptance

All arithmetic, input contracts, validation budgets, forced backends, Clipper
variants and warm-call measurement policies from [stage one](HYBRID-PROTOCOL.md)
remain. Timed Hybrid includes the new selector and backend. The forced double
mode and existing integer engine are unchanged. All raw values, including the
observed negative Clipper result, stay visible without clamping or an assumed
diagnosis. `ClipperRelativeError` records absolute error when Winding is zero.

There are now 28 inputs and 56 input/fill cells. Dense-grid and retraced families
are explicit targets: require Hybrid ≤0.8 Winding and ≤0.8 full-input Clipper
on each, with nonnegative Clipper output. Every other cell must remain ≤1.10
Winding. The retraced gates are additional requirements, not exemptions from
stage one's failed preservation rule. Report preloaded Clipper and the fastest
forced backend on every row, including any false-negative selection.

Three fresh sequential processes, five samples per row, method rotations
0/2/4, Release and tiering disabled. No concurrent build/profiling/timing, no
post-timing tuning. The same-binary controls are primary; differences from the
first series are descriptive and not isolated instruction-level speedups.
The expanded matrix prevents treating the two run totals as identical workloads.

Run normal/no-intrinsics checks, exact-oracle controls, backend output parity,
and legacy filter-first validation. Use the frozen-binary summarizer and ten
corrupted evidence controls as before. Raw directories are distinct:

```text
check-hybrid artifacts/scanbeam-hybrid-v2-check
benchmark-hybrid artifacts/scanbeam-hybrid-v2 <run 1..3> <measured-commit>
summarize-hybrid artifacts/scanbeam-hybrid-v2
```

No shipping API or package change follows from a bounded positive outcome.
Integrating one filled area into an API returning four integrals and diagnostics
requires a separate API/performance decision. Arbitrary double-coordinate
inputs remain a limitation even if every declared gate passes.
