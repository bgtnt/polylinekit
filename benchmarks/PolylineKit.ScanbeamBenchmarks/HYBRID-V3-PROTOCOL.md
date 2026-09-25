# Adaptive closed area: cheaper selection with unchanged decisions

This third experiment is fixed after reviewing the [second-stage result](HYBRID-V2-RESULTS.md)
and before collecting third-stage timings. The second selector passes all 22
target cells but fails preservation on six inexpensive cells: about 4 us of
selection adds 14–30% to the complete call. This experiment reduces the work
needed to make exactly the same decision. Preserve both earlier series, sources,
frozen binaries and evidence. No threshold or routing-policy changes are made.

## Fixed implementation change

Cache each sampled nonzero edge as bounded integer endpoints once, together with
its endpoint bounding box. Validate every sampled endpoint in the original order
before dropping zero-length sample edges. For each nonadjacent sampled pair, an
inclusive bounding-box disjointness test can reject it before endpoint-equality
and orientation work. Pairs whose boxes meet still receive the exact predicates;
box overlap alone never constitutes a crossing or coincidence.

All sampled coordinates are admitted as exact integers within ±2048 before
conversion. Coordinate differences are at most 4096; the absolute determinant
is bounded by `2 * 4096 * 4096 = 33,554,432`, which fits signed Int32. Use exact
bounded integer orientation signs. The selected integer sweep and its Int64
event arithmetic are unchanged. Full-coordinate admission, retained-vertex and
Y-level checks, active-edge threshold, 16 sample positions, crossing/coincidence
thresholds and rejection reasons remain exactly those of stage two. No point is
rounded, translated or scaled.

This change uses scalar safe C#; it adds no SIMD, unsafe code or dependencies.
The bounded sample stack storage grows from 64 to 640 bytes to cache endpoints
and boxes. This trades a small fixed stack cost for less repeated work.

Keep a verbatim second-stage reference class (apart from its class name) in the
same benchmark assembly. `Hybrid-v2` and `Selector-v2` use that reference;
`Hybrid` and `Selector-only` use the optimized selector. Require equal complete
`HybridSelection` records, including counters and reasons, and bit-identical
hybrid outputs. This is a controlled selector implementation comparison, not a
new performance classifier. Both wrappers retain equivalent integer engines.

## Inputs and validation

Keep all 28 existing inputs unchanged: 9 development, 13 originally held-out,
and 6 second-stage confirmation instances. These were already inspected and are
reused controls. Before timing, add 6 `confirmation-v3` inputs: a new 192-point
grid, a repeated parallelogram, an integer star, a sparse few-Y sawtooth, a
subdivided rectangle and a transformed simple comb with broadly overlapping
edge boxes. The last input ensures the box filter still reaches exact predicates
when boxes overlap without proper crossings. All seeds and points are fixed in
source. The new cases remain related shape families, not universal validation.

The matrix contains 34 inputs and 68 input/fill cells. Untimed differential
checks compare optimized and retained selectors beyond these timed fixtures,
including zero edges, inclusive box boundaries, exact coordinate limits,
sampled and unsampled invalid inputs and randomized bounded walks. Preserve the
existing exact-rational oracle, backend parity, mode-switching, failure recovery,
normal/no-intrinsics checks and byte-identical legacy filter-first validation.
For the original 28 inputs, preserve the previous input identities and all
validation fields after removing only the two newly added v2-reference fields
(`SelectionV2` and `HybridV2`). Preserve the prior numeric result bits explicitly.

## Measurement and acceptance

All operation contracts, numeric budgets, forced backends, Clipper preparation
variants, allocation measurement and warm-call timing settings from the first
two protocols remain. Each geometry method returns one requested filled area.
The Winding control still computes all four public integrals plus diagnostics;
that extra work is disclosed and does not prove algorithmic superiority. The
experimental wrapper remains unable to replace that public four-result API.

Each cell times Winding, Hybrid, Hybrid-v2, Double-sweep, Clipper-full-input,
Clipper-preloaded, Selector-only and Selector-v2; also time Integer-sweep wherever
its native input contract permits. Selector methods return numeric sinks, not
areas. Hybrid and Hybrid-v2 include selection plus the backend on every call.
Their same-binary comparison is the primary attribution of the change. Do not
infer instruction-level speedups by comparing elapsed times from earlier runs.

Use three fresh sequential Release processes with tiering disabled, five
calibrated samples per row and method-order rotations 0/2/4. No concurrent
build, profiling or timing. Report medians of process medians and observed
ranges, not confidence intervals. Keep every unfavorable row and allocation.

The existing performance acceptance gates are unchanged: every dense-grid or
retraced cell must have Hybrid ≤0.8 Winding and ≤0.8 full-input Clipper, with
nonnegative Clipper output. Every other cell must have Hybrid ≤1.10 Winding.
There is no small-call exemption. Report Hybrid/v2, Selector/v2, preloaded
Clipper, chosen-backend overhead and fastest-admissible-backend regret per cell.
Retain the negative near-coincident Clipper value without clamping or assuming
its cause. No retuning after the results are inspected; a failed gate is a
reportable outcome.

The frozen-binary summarizer must recompute input identity, outputs, both
selection records, diagnostics, row order, medians and hashes before writing
derived files. Run the ten existing corrupted-evidence controls plus corrupt
retained-v2 output and selection controls. Publish compact evidence with source
and binary hashes; retain the raw archive locally under a new name.

```text
check-hybrid artifacts/scanbeam-hybrid-v3-check
benchmark-hybrid artifacts/scanbeam-hybrid-v3 <run 1..3> <measured-commit>
summarize-hybrid artifacts/scanbeam-hybrid-v3
```

No shipping API, package or numeric-domain expansion is part of this experiment.
Fractional and wide inputs still route to Winding. Passing the bounded gates
would support this selector optimization under the tested conditions, not a
guarantee of faster execution for arbitrary inputs.
