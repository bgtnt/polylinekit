# Borrowed scalar metadata: fixed protocol

Investigate one change identified by a sampled profile of frozen active-pass
commit `bafb1d9ddc9226054bad7503d9467a064ebd1cf3`: borrowing immutable prepared
scalar-order metadata instead of copying it for every candidate pair. Geometry
still uses the contiguous copied edge array with pair-specific loop tags.
The eighth constructor option `borrowPreparedScalars` defaults false. Preserve
all existing scratch arrays and preparation metadata for this comparison.
No shipping code, API, dependency or arithmetic changes are proposed.

The earlier full-direct experiment removed both copies but added geometry
indirection and did not improve timing. This hybrid changes only scalar storage.
Both measured modes enable copied preparation, common Y, endpoint caching,
scalar order filtering, specialized area arithmetic, gap coalescing and active
passes. See [indexing/lifetime invariants](SCALAR-VIEW-NUMERICS.md).

## Correctness and instrumentation

Require exact result/error-bound bits, fallback/exception behavior and every
existing diagnostic to match. Count actual scalar records copied or borrowed;
the prepared first/second split must follow global edge IDs even for unequal
counts. Reset counts at every call. Raw and filter-disabled operations must not
use scalar views. Preserve full-direct mode and all early-return/exception
cleanup, source-mutation isolation and concurrent immutable snapshot use by
separate engines. No borrowed snapshot buffer may outlive a prepared call through engine references.

Before timing, run focused lifecycle controls and the existing prepared and
exact-area suites normally and with hardware intrinsics disabled. Require
422/422 original-coordinate frozen candidate calls certified by both modes,
with bit-identical area/certificate/topology/work/pass/gap diagnostics. Verify
the prior active-pass command's validation JSON stays byte-identical. Test the
summarizer with corrupt rows, order, medians, hashes, output, certificates,
preparation payload and each optional diagnostic group.

## Profiles and timings

Profiles are separate from benchmark timing. Use dotnet-trace sampled thread
time for the old warm loop, both new warm variants, and one preparation loop.
Filter warm stacks to TraverseReal and preparation stacks to PrepareAblation.
Record trace/script/binary hashes and command, with four initial calls included.
JIT inlining and native attribution affect frames; these are sampled thread-time
shares, not measured CPU percentages. Profiles select a candidate; only
unprofiled complete-operation timings establish a benefit.

Keep 109 Census rings, 10,588 vertices, 2,156 directional pairs, 422 candidates,
original double inputs, Clipper scale 1e6 and NTS checks of 1 m² / 1e-8 coverage.
NTS is an independent non-exact check. Each direction measures prepare,
warm-table, warm-zones-fresh-queries and prepare-plus-one. Fresh queries are
prepared once per table; prepare-plus-one grows new sweep scratch, while
Winding retains warm thread-local scratch. Exclude I/O consistently.

Methods A-D: Winding-intersection-only, Guarded-active, Guarded-scalar-view,
Clipper64-reused-data. Three fresh sequential Release processes, five samples
per row, 32 rows/process, DOTNET_TieredCompilation=0, orders ABCD/CDAB/BDAC.
No concurrent builds, profilers or timing workloads. Report medians of process
medians, observed ranges and allocations; ranges are not confidence intervals.

Rerun frozen bafb1d9 using its unchanged active-pass matrix in a separate
directory. Interleave old1/new1/new2/old2/old3/new3. Do not pool the two
480-sample series. Historical Guarded-active is C; current baseline is B.
Report sensitivity without claiming normalization removes JIT/environment drift.

The existing adoption gate stays fixed: each selected full-query cell must
take at most 0.8 times Clipper and 0.9 times Winding. No post-timing tuning.
A failed or regressive result is valid evidence; do not enable the experiment
in shipping code. Publish compact evidence, source/environment/hashes and
commands; keep raw samples and traces local. CI runs correctness only.

```text
check-scalar-view artifacts/scanbeam-scalar-view-check
benchmark-scalar-view artifacts/scanbeam-scalar-view <run 1..3> <revision>
summarize-scalar-view artifacts/scanbeam-scalar-view
profile-double-ablation <method> 20 [warm-table|prepare]
```

Use benchmark-active-passes/summarize-active-passes with the old binary in
artifacts/scanbeam-scalar-view-historical.
