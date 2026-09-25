# Specialized interval area arithmetic experiment

Declared before timing. The copied prepared sweep at `bc6184c` takes about
19.55 ms per warm table, still slower than Winding and Clipper. Its sampled
profile attributes about one third of inclusive sweep samples to AccumulateGap;
this does not predict the speedup obtainable by changing arithmetic within it.

## Bounded change and numerical contract

Add `optimizeAreaArithmetic`, default false, to the experimental engine. Replace
only the two generic interval multiplications in trapezoid integration with
specialized endpoint products. Preserve operation order, zero/one shortcuts,
outward rounding, interval additions, and the final certificate. A rounded lower
width bound may be negative even though the true width is nonnegative: account
for this explicitly rather than assuming both interval endpoints are positive.
See [the derivation](AREA-ARITHMETIC-NUMERICS.md).

Require bit-identical results, certificate radii, fallbacks and all diagnostics
against the generic operations, including subnormal and extreme controls.
Independently compare arithmetic enclosures and accumulated trapezoids with exact
dyadic rational arithmetic, and run the existing exact geometric area suite.
Test copied and direct prepared snapshots, raw calls, both fill rules, all
ROI/cache/filter combinations in prepared controls, exceptional inputs and
recovery. Run normally and with hardware intrinsics disabled.

No changes to horizontal differences, crossing construction, status ordering,
preparation, budgets, or shipping `src/` APIs. No new dependency or SIMD claim.
Keep direct prepared access disabled in both measured sweep methods.

## Fixed workload and acceptance

Reuse the original 109 Census rings, 10588 vertices, 2156 directional pairs and
422 candidate pairs, original-coordinate NTS reference budgets (1 square metre
and 1e-8 coverage), and Clipper scale 1e6. NTS is a cross-check, not an exact oracle.

Methods A-D:

- A: `Winding-intersection-only`.
- B: `Guarded-prepared`, copied preparation with ROI/cache/scalar filter.
- C: `Guarded-area`, identical flags plus specialized area arithmetic.
- D: `Clipper64-reused-data`.

Both directions use `prepare`, `warm-table`, `warm-zones-fresh-queries` and
`prepare-plus-one`. Prepare each incoming query once per table in the fresh-query
scope. Preparation-plus-one includes fresh comparator/workspace allocation;
Winding's thread-local workspace is warm, so this is not cold-process latency.
Prepared payload excludes object/array headers, shared inputs and engine scratch.

Three fresh sequential Release processes, five calibrated samples per row,
32 rows per process, 480 samples. Disable tiered compilation. Method orders
ABCD, CDAB, BDAC; no simultaneous build, profiler or other benchmark. Report
median of process medians, process ranges, and managed allocations.

All six complete-query competitor gates remain: selected time <=0.8 times
Clipper and <=0.9 times Winding, with correctness required. Publish all twelve
baseline/selected gate cells. Improvement over the experimental baseline alone
does not justify replacing the shipping Winding engine.

## Historical sensitivity and evidence

Shared method changes may alter the baseline's JIT code too. Rerun the frozen
`bc6184ce4b57598ca39999ff50ff279d62db8b2f` binary with its unchanged direct-sweep
matrix, in separate artifacts. Fixed sequence: old1, new1, new2, old2, old3, new3.
Compare the old copied method with both new methods, alongside each series'
Winding and Clipper controls. Method positions B and comparator positions A/D
are unchanged. The historical C remains the direct variant. Do not combine
samples or claim control normalization eliminates time/environment drift.

Summarizers validate the ordered matrices, samples, medians, digests, binary and
input hashes, every recorded pair diagnostic, and prepared metadata before
writing evidence. Test an unchanged positive copy and corrupt row/order/median/
output/binary/diagnostic/fallback/payload controls. Failed controls must leave
submitted JSON unchanged and produce no summary. Preserve raw runs locally;
publish compact evidence, commands, environment and measured commit hashes.

Optional sampled profiles run separately after all timing, using the measured
binary; inclusive samples and inlining are not isolated operation costs.

```text
check-area-arithmetic artifacts/scanbeam-area-check
benchmark-area-arithmetic artifacts/scanbeam-area <run 1..3> <revision>
summarize-area-arithmetic artifacts/scanbeam-area
```

Use `benchmark-direct-sweep`/`summarize-direct-sweep` with the old frozen binary
in `artifacts/scanbeam-area-historical`. CI runs correctness only. No tuning of
numerical bounds or acceptance thresholds after looking at timing results.
