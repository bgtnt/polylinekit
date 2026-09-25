# Guarded double sweep: common-Y and cached-X ablations

Declared before timings. This follows the failed first
[double sweep](DOUBLE-RESULTS.md). The shipping library is unchanged.

## Fixed contracts

Keep all original-input, admission, certificate, fallback, pair-population,
accuracy and complete-operation contracts from [DOUBLE-PROTOCOL.md](DOUBLE-PROTOCOL.md).
No coordinate rounding, new data, pair removal, changed tolerance or approximate
ordering is permitted. Use all 2156 directional pairs, including the same 422
geometry candidates. Report any change in certification/fallback frequency.

Two independently switchable changes are evaluated:

1. Restrict bands to the overlapping Y bounds. Validate both complete paths and
   prepare edges first. Initialize every edge spanning the lower bound, certify
   its order immediately above the bound and establish both winding prefixes.
   Retain X-side winding contributions. Stop at the upper bound. Charge initial
   status construction and all preparation inside every ordinary geometry call.
2. Lazily cache the exact `XAt` interval for an original endpoint level, edge ID
   and call generation. Preserve its complete interval bits and comparison logic.
   Do not evaluate intervals that the uncached method never requests. Invalidate
   across calls and storage growth. Do not change relative-coordinate widths in
   `HorizontalDifference`, area arithmetic or acceptance budgets.

Constructor flags default off; previous commands retain the original algorithm.
Measure a contemporary baseline in the same binary because flags and diagnostic
counters can perturb code generation relative to the earlier frozen binary.

Cache-only must preserve result/error-radius bits, fallback/reason, bands,
crossings, status visits and charged work of its corresponding uncached method.
Require this for every frozen pair and independent exact controls. Common-Y may
change the integration sequence or avoid irrelevant uncertain events; certified
values must still satisfy the original exact-oracle enclosure checks. Verify
lower/upper boundary crossings, spanning edges, outside-range degeneracies,
validation outside the retained range and stale caches on reused inputs/edge IDs.

## Profiling

Collect a separate managed sampled-thread-time profile of repeated complete warm
county tables, with tiering disabled. Preparation/JIT samples outside `TraverseReal`
are excluded. Sampling and JIT inlining limit attribution; inclusive frame shares
overlap, and sampled thread time is not an exact CPU percentage. Profiled elapsed
time is never used as benchmark evidence. Record tool version, source/binary/trace
hashes and command. No profiling runs concurrently with benchmark measurements.
The previous frozen binary can be driven through a separate reflection harness;
the new `profile-double-ablation <method> <seconds>` command reproduces this
workload directly for the current variants (1–60 seconds).
For example, collect with `dotnet-trace collect --profile dotnet-sampled-thread-time
--format Speedscope -- dotnet <runner.dll> profile-double-ablation Guarded-baseline 20`.
Use `profile-summary.py <trace.speedscope.json> <summary.json>` for the documented
frame shares. The tool is an optional development tool, not a project dependency.
See [Microsoft's tracing documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).

## Measurement and decisions

Methods, in declaration order A–F:

- A: current `Winding-intersection-only`.
- B: `Guarded-baseline`, both flags off.
- C: `Guarded-common-y`, Y restriction only.
- D: `Guarded-cache`, X cache only.
- E: `Guarded-combined`, both flags on.
- F: `Clipper64-reused-data`, the existing scale-1e6 adapter.

Use the same two directions and three scopes (`prepare`, `warm-table`,
`prepare-plus-one`), all six methods in each. Three fresh sequential processes,
five calibrated batches/row, Release, `DOTNET_TieredCompilation=0`: 36 rows per
process, 540 samples in total. Fixed method orders are `ABCDEF`, `DEFABC`,
`CFBEAD` across processes1–3. This diversifies placement without choosing order
after timings. Preparation-only rows and work-count reductions are descriptive.
Managed allocations include ordinary initialization and fallback, as previously.

Report all four variants relative to the contemporary baseline, Winding and
Clipper. Preserve the original gate independently for each variant: each of the
four direction/full-query cells must take <=0.8 times Clipper and <=0.9 times
Winding, using the median of three process medians. Correctness and actual
certified successes are required too. There are16 performance cells; the
combined variant's four cells determine its gate. A baseline improvement alone
does not justify library integration. Report every failed cell and process range.

The summarizer verifies the ordered complete matrix, all output values, sample
medians, source/binary identities and exact recorded diagnostic file before writing
evidence. Negative controls must reject changed rows/order/medians/output/hash or
cache diagnostics without replacing submitted validation. Keep raw files ignored.

## Commands

From the clean recorded revision, restore locked and build Release. With the DLL
at `benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll`:

```text
check-double-ablation artifacts/double-ablation-check
benchmark-double-ablation artifacts/double-ablation 1 <revision>
benchmark-double-ablation artifacts/double-ablation 2 <revision>
benchmark-double-ablation artifacts/double-ablation 3 <revision>
summarize-double-ablation artifacts/double-ablation
```

Disable tiering for all timing processes; run them sequentially with no build,
profiling or second benchmark. Also run the exact controls with hardware intrinsics
disabled. CI runs untimed validation only. No new runtime dependency is added.
