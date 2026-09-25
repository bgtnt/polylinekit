# Scalar order filter experiment

Declared before timings. This follows the common-Y/cached-X experiment at
`ab1c6ba`, whose optimized sweep still lost to both competitors. It tests one
additional change: a certified scalar filter before interval endpoint ordering.
There is no shipping-library change, endpoint-sort rewrite, new geometry or
prepared-pair cache in this experiment.

## Numerical obligation

Keep the original binary64 coordinates, area certificate, all numerical/work
budgets, crossing construction, width formula and area accumulation. The filter
may return only a mathematically certified strict order for the actual dyadic
input coordinates. Uncertain signs, exact ties and excluded arithmetic regimes
continue through the existing interval comparator and its tie handling. The
filter itself must not turn ordinary uncertainty into a new whole-call fallback.
See [the derivation](SCALAR-FILTER-NUMERICS.md) for the exact bound and domain.

Filter-specific metadata is separate from the ordinary Edge arrays and is
prepared only when enabled. Its preparation and allocation costs belong to the
measured call. Do not silently add scalar caches or use approximate event keys.
Same-support handling stays ahead of the filter. Count filter attempts, accepted
strict comparisons and interval continuations; attempts must equal the other two.
Report remaining XAt evaluations/cache hits and whole-operation fallbacks.

Independent rational controls must compare every accepted direct filter order
with the exact interpolated X values, and exercise ordinary accepted signs,
uncertain equality, endpoint contacts, one-ULP differences, cancellation, large
offsets, signed zero, subnormals and excluded arithmetic. Run the complete exact
area suite with the filter enabled in all four combinations of the earlier
flags; retain paired cache/no-cache parity with matching filter settings. Where
both filtered and unfiltered runs certify, unchanged area expressions must yield
the same result and radius bits. Changed fallback frequencies must be visible.

The filter is off by default. Old commands retain their algorithm. Compare the
unfiltered variant in the same measured binary; added flags/counters may affect
code generation even when disabled. Check historical baseline outputs and logical
diagnostics against the frozen preceding evidence before interpreting speedups.

## Frozen inputs and measurements

Retain the input/admission contracts from [DOUBLE-PROTOCOL.md](DOUBLE-PROTOCOL.md):
109 valid original Census rings, 10588 vertices, all 2156 directional pairs and
422 geometry candidates. Every method uses the same outer candidate masks and
original source arrays. Require <=1 m² intersection and <=1e-8 absolute coverage
error against original NTS; NTS is not an exact oracle. Clipper keeps scale-1e6.
For every pair already certified by the unfiltered sweep, require no new fallback,
same result/radius bits, and unchanged logical bands/events/status/work. This
frozen-set parity supplements, rather than replaces, independent exact checks.

Four methods A–D:

- A: `Winding-intersection-only`.
- B: `Guarded-combined`: common-Y restriction and interval-X caching, filter off.
- C: `Guarded-filtered`: same operations, scalar filter on.
- D: `Clipper64-reused-data`.

Use both directions and all three existing scopes: preparation, warm full-table
traversal and fresh preparation plus one table. Three fresh sequential processes,
five calibrated batches per row, Release and `DOTNET_TieredCompilation=0`: 24 rows
per process and 360 samples. Fixed orders `ABCD`, `CDAB`, `BDAC` diversify method
positions across the three processes; no order is chosen after timings.
No profiling, build or second benchmark runs during measurement. Allocation is
managed bytes on the measured thread, not retained/peak memory. Report ranges.

Preserve the competitor gate: for each of the four direction/full-query cells,
filtered time must be <=0.8 times Clipper and <=0.9 times current Winding. Use
medians of three process medians; correctness and actual filter acceptance are
required. A gain against the unfiltered sweep alone does not justify integration.
Record all eight baseline/filtered competitor cells, including failures. Do not
tune thresholds or numerical bounds after timings.

The shared runner validates ordered complete matrices, sample medians, output
digests, source/binary identities and complete diagnostic records before writing
evidence. Negative controls must reject changed method/order/median/output/hash
or filter counters without replacing submitted records. Raw output stays ignored.
An optional separate profile may explain remaining cost, with the same thread-time
and inlining caveats as the preceding experiment; it is not benchmark evidence.

## Reproduction

From the clean recorded revision, restore locked and build Release. The runner is
`benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll`.

```text
check-scalar-filter artifacts/scalar-filter-check
benchmark-scalar-filter artifacts/scalar-filter 1 <revision>
benchmark-scalar-filter artifacts/scalar-filter 2 <revision>
benchmark-scalar-filter artifacts/scalar-filter 3 <revision>
summarize-scalar-filter artifacts/scalar-filter
```

Run timing commands with tiering disabled, sequentially. Repeat correctness with
hardware intrinsics disabled. Use measured binaries to summarize. CI checks
correctness only; no new runtime dependency or C++ measurement is introduced.
