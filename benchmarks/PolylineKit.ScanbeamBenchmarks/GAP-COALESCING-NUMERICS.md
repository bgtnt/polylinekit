# Coalescing contiguous filled gap contributions

This experiment reduces repeated geometric integration over unrelated endpoint
levels. It leaves the sweep's endpoint handling, active status, winding prefixes,
crossing discovery/order, `gapStarts` updates and nonnegative projections intact.
The optional sixth constructor flag `coalesceGaps` is off by default. The measured
baseline is copied preparation with ROI, endpoint cache, scalar order filtering
and specialized area arithmetic enabled; direct prepared access is disabled.

## Contribution stream

The original `AccumulateGap` first updates its positional start level, checks
the current intersection-filled predicate and evaluates the interval height.
Only after those unchanged checks establish a filled, nonzero-height interval
does it emit `(left edge ID, right edge ID, start Level, finish Level)`.

The new mode stores at most one pending contribution per left edge. It extends
that contribution only when the right edge ID matches, all pending/incoming
start and finish Y intervals are point-valued, and its previous finish is
identical to the new start. Level identity compares both Y interval endpoint
bits and both crossing edge IDs A/B; an approximate or overlapping Y is not enough.
Otherwise it integrates the previous contribution and replaces it. A successful
end of topology processing flushes the remaining contributions in left-ID order.

Only filled pieces enter this stream. An unfilled positive-height piece still
advances the original positional start level, so a later filled piece starts
after the preceding pending finish and cannot bridge that hole. Consequently
matching adjacency alone is never used to infer persistent filling. Changes at
a single zero-height boundary do not add area.

Captured edge IDs refer to immutable geometry for the entire call, including
after edges leave the active status. Neither deferred integration nor final
flush consults current active positions or winding prefixes. Direct prepared
queries retain their borrowed geometry until integration finishes, then their
existing `finally` clears it.

## Exact integral preserved

For a fixed ordered pair of straight nonhorizontal edges, horizontal separation
`w(y) = x_right(y) - x_left(y)` is affine throughout their common vertical range.
The existing sweep certifies that each emitted piece lies in that range, is
filled and has nonnegative true separation. If pieces share the same ordered
pair and exact consecutive event level, their union is a contiguous filled
interval. Therefore

```text
sum over pieces of integral(w(y), start_i, finish_i)
    = integral(w(y), first_start, last_finish)
    = (w(first_start) + w(last_finish)) * (last_finish - first_start) / 2.
```

This is an identity of exact real integrals; it does not assume equality of the
separately rounded trapezoids. Full Level identity preserves event meaning as
well as enclosure bits. Endpoint levels represent the same supplied Y exactly;
crossing levels retain the same A/B pair, whose certified differing slopes
define one unique crossing. Retaining that provenance also preserves the
`HorizontalDifference` shortcut for an exact zero width at the crossing.

The deferred computation uses the original interval height, horizontal-difference
and nonnegative-projection methods, then the selected original/specialized
trapezoid expression. Its endpoint width and height intervals contain the true
values for the whole contiguous span. Their interval expression therefore
encloses the same exact integral, even though variables are correlated. Original
interval addition encloses the sum of all emitted, nonoverlapping filled pieces.

Coalescing changes integration boundaries and the order of area accumulation.
Result and radius bits may differ from the unmerged mode. The existing absolute
and relative certificate budgets are unchanged; a result is returned only if
the final enclosure passes them. Numerical uncertainty still falls back to the
original-coordinate Winding operation. Delayed arithmetic may change which
numerical fallback occurs first; it must not be represented as bitwise parity
with the unmerged path. No topology decision or tolerance is relaxed.

## Point-valued endpoint restriction

Merging is mathematically valid for uncertain crossing levels too, but a longer
trapezoid can magnify the width/height interval dependency enough to fail the
unchanged area certificate. The first uncommitted unrestricted prototype exposed
this on frozen county/query pair `37027`/`3714`: 14 original integrations became
9 (5 merges), and horizontal-difference calls fell from 34 to 24. The original
area enclosure was `[908.9087708255547, 908.908770866224]`, with radius about
`2.0335e-8`; the merged enclosure was `[908.9087707330834, 908.9087709586903]`,
with radius about `1.1280e-7`, exceeding the roughly `9.09e-8` relative budget.
Several boundary levels have Y-enclosure width `4.656612873077393e-10`.
This is geometric interval widening, not a reason to relax the certificate.

The bounded variant therefore leaves any contribution adjacent to a non-point
Y interval unmerged. An incoming exact-level contribution can begin a fresh
chain after that separate piece is flushed. The condition depends on interval
kind, not a tuned numeric threshold, and full event provenance is still checked.
It limits crossing-height amplification without promising the old certificate
for every possible input: other interval dependencies and addition order still
change. The exact-area checks and all-candidate acceptance gate remain required.

## State, budgets and diagnostics

Pending state is a reusable value-type array, allocated with existing workspace
growth only when coalescing is enabled. Call generations invalidate all entries
before any early return or fallback; generation wrap clears the array. Growth
creates zero-initialized entries. A failed topology run does not flush pending
area work. Raw/copied/direct alternation uses the same reset rules.

Every emitted contribution incurs one additional `Charge()`, and the final
scan incurs one per edge. Existing Visit/topology charges are retained. Thus a
completed coalescing call has the baseline work count plus contribution count
plus nonhorizontal edge count, while the original work/event/vertex limits stay
fixed. The conservative extra accounting can cause an earlier work-budget
fallback. It is not permission to process an unbounded pending queue.

For a successfully completed non-fallback query:

```text
GapContributionCount = GapIntegrationCount + GapMergedCount
unmerged GapIntegrationCount = GapContributionCount; GapMergedCount = 0
```

`GapIntegrationCount` counts actual integration attempts, including an attempt
that throws. `HorizontalDifferenceEvaluationCount` counts real function calls,
including crossings and zero-width shortcuts; it does not count skipped merged
integrations. `GapWorkspacePayloadBytes` counts only retained new array element
bytes, excluding headers and all previous engine buffers. Warm reuse performs
no per-contribution allocation.

Validation must use independent exact area/enclosure controls rather than assume
unmerged result/radius bits are the answer. Include persistent affine gaps,
unfilled holes with unchanged adjacency, NonZero/EvenOdd changes, crossings,
endpoint replacements, subnormals/large translations and failure/reuse paths.
Topology and filter diagnostics remain identical on completed certified calls;
new geometric-work counters quantify the saving before any speed claim.
