# Scalar filter before common support: fixed protocol

Test one benchmark-only change: run the existing certified scalar-order filter
before the endpoint comparer tests `SameSupport`. Strict acceptance rules out
common support. An inconclusive probe retains the support shortcut and original
interval/tie path without a second filter probe. The ninth constructor option
`filterBeforeSupport` defaults false. Both measured sweep variants retain copied
geometry and scalar metadata, common Y, endpoint caching, specialized area
arithmetic, gap coalescing and active passes. The previous borrowed-scalar
experiment was slower and is not the new baseline.

No shipping code, arithmetic, tolerances, arrays, dependencies or API changes.
See [the parity argument](FILTER-FIRST-NUMERICS.md). A source-level reduction in
support tests is a hypothesis about cost; additional probes on common support
and changed generated code can outweigh it.

## Correctness and instrumentation

Require bit-identical area, coverage and error-bound results, identical fallback
and exception behavior, work charges and all existing diagnostics. Exercise raw,
prepared copied, direct and borrowed-scalar access; filtering on/off; both fill
rules; identical/reversed/retraced segments; unequal vertical extents; crossings,
near ties, unavailable scalar metadata, reuse and early-return cleanup.

Keep the old logical filter counters, which exclude common-support shortcuts.
Add actual endpoint support-test, support-match and scalar-probe counters. For
matching comparison streams with filtering enabled, require:

```text
baseline support tests - selected support tests = accepted filter decisions
selected scalar probes - baseline scalar probes = support matches
baseline scalar probes = logical filter attempts
selected support tests = support matches + logical interval decisions
```

Both support-match counts must agree; all counters reset each call. The support
counter excludes `HorizontalDifference`, which is unchanged. These measurements
must expose the extra probes as well as the saved support tests.
Derive actual totals from a comparison count, the support-match count and old
logical filter counts. Only comparisons and support matches add increments;
their streams are identical in both variants. Do not separately increment a
support-test counter on the path the candidate skips. Historical comparison
also exposes the effect of this new common instrumentation and generated code.

Before timing, run exact-sign, focused parity, prepared lifecycle and existing
geometry suites normally and with hardware intrinsics disabled. Both variants
must certify all 422 frozen original-coordinate candidate calls with identical
results, certificates and previous diagnostics. The legacy `check-scalar-view`
validation JSON must remain byte-identical. After timing, test the summarizer
against unchanged evidence and 13 corruptions: missing row, method order,
median, output, binary hash, filter diagnostic, fallback record, preparation
payload, certificate, gap, active-pass, scalar-storage and new ordering counters.
Rejection must leave submitted inputs unchanged and write no derived evidence.

## Timings and historical sensitivity

Keep the frozen 109 Census rings, 10,588 vertices, 2,156 directional pairs and
422 geometry candidates, original double inputs, Clipper scale 1e6 and NTS
checks of 1 m² / 1e-8 coverage. NTS is an independent non-exact check. Each
direction measures preparation, warm-table, warm-zones-fresh-queries and
prepare-plus-one. Fresh queries are prepared once per table; prepare-plus-one
grows new sweep scratch while Winding retains warm thread-local scratch.
Exclude I/O consistently. Do not run profiles during this experiment.

Methods A-D: Winding-intersection-only, Guarded-active, Guarded-filter-first,
Clipper64-reused-data. Three fresh sequential Release processes, five samples
per row, 32 rows/process, DOTNET_TieredCompilation=0, orders ABCD/CDAB/BDAC.
No concurrent builds, profilers or timing workloads. Report medians of process
medians, observed ranges and allocations; ranges are not confidence intervals.

Rerun frozen `e4ffc1092d14bc5a1c11f8122386475c13e972bb` with its unchanged
scalar-view matrix in a separate directory. Interleave old1/new1/new2/old2/old3/new3.
Both matrices have the copied-data Guarded-active baseline at B. Historical C
is the failed borrowed-scalar experiment, not a baseline for the new change.
Do not pool the two 480-sample series. Report competitor drift and historical
sensitivity without claiming normalization removes JIT/environment effects.

The adoption gate remains fixed: each selected full-query cell must take at
most 0.8 times Clipper and 0.9 times Winding. No post-timing tuning. A small,
negative or inconsistent result is valid evidence. Publish compact evidence,
source/environment/hashes and commands; retain raw measurements locally. CI
runs correctness only. Do not enable a shipping backend based on this experiment.

```text
check-filter-first artifacts/scanbeam-filter-first-check
benchmark-filter-first artifacts/scanbeam-filter-first <run 1..3> <revision>
summarize-filter-first artifacts/scanbeam-filter-first
```

Use `benchmark-scalar-view` / `summarize-scalar-view` with the frozen historical
binary in `artifacts/scanbeam-filter-first-historical`. Preserve its original
binary and embedded source revision; freeze the new binary after committing all
measured code and this protocol.
