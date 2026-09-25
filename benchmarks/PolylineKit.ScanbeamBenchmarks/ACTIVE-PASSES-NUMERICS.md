# Active-pass experiment: order and arithmetic contract

`GuardedDoubleSweep`'s optional seventh constructor argument,
`optimizeActivePasses = false`, selects a benchmark-only reduction of active-status
passes. The default path retains its original visits and operation order. The
experiment adds no arrays and changes no shipping API, interval operation,
tolerance, geometric predicate, or fallback engine.

## Incremental top-order construction

At the start of a band, `active` is the certified order just above its bottom
endpoint. The original code copies all IDs to `topOrder` before insertion sort.
The experiment copies its first ID, then takes the next unsorted ID directly from
`active[i]`. Insertion sort only reads its sorted prefix and the current ID.
Inductively, both versions therefore compare the same IDs, in the same order, and
write the same sorted prefix. They construct the same crossings, with the same
IDs and interval levels. `active` is unchanged throughout enumeration.

If enumeration fails, the whole query still falls back. Prefix initialization
contains integer winding updates and buffer writes only; moving it after
enumeration introduces no geometric arithmetic or validation.

## Bands without crossings

A completed enumeration with zero crossings performed no insertion-sort shifts.
Its `topOrder` is exactly `active`, and there are no crossing events that can
change that status. The original final equality check is consequently redundant
in this branch.

The experiment walks `active` once, starting both winding counts at zero and
applying each left edge's signed delta before examining its right-hand gap. The
last edge has no right-hand gap and its delta is not needed. This is
the same prefix plus left-edge delta used by `AccumulateGap`. It evaluates the
same fill predicate for each gap, in the same order. Each accepted gap has the
same edge IDs and `Level.At(bottom)` / `Level.At(top)` bounds as before.

Only filled gaps call `AccumulateFilledGap`. In particular, height is **not**
computed for an unfilled gap. The shared helper preserves height subtraction,
nonnegative projection, zero-height suppression, contribution counting and
integration/coalescing order. This preserves the emitted contribution stream,
including full crossing-level provenance in other bands. Pending coalescer
state and final flush order are unchanged.

## Bands with crossings

One pass initializes `prefixA`, `prefixB` and `gapStarts`. Its resulting buffers
equal those of the original two initialization passes. The code then retains
the original crossing sort, strict event separation check, adjacent-event
updates, local prefix updates, final status verification, and final gap walk.
Verification and accumulation remain separate: an invalid final order must
abandon the query before any final-band gap is emitted.

## Numerical and budget contract

For a query that finishes the same certified sweep in both modes, the interval
operations and contribution order are unchanged. Area and `LastErrorBound`
therefore retain their bit patterns; this experiment makes no claim of improved
accuracy. Event, band, peak-active, filter, X-evaluation, gap and horizontal-width
counters also retain their values.

`Visit()` still charges one unit for every actual counted active-entry visit.
The numerical budgets and the 2,000,000 work limit are unchanged. Removed visits
reduce work, so the experiment can finish a query abandoned by the original at
the work limit; fallback timing and partial counters need not match in that
case. All other uncertainty conditions still cause whole-query fallback.

For a successfully processed band of `n > 0` active edges:

| Work | Original | Optimized |
|---|---:|---:|
| Initial copy/prefix visits, no crossings | `2n` | `0` |
| Verification visits, no crossings | `n` | `0` |
| Final gap/stream visits, no crossings | `n - 1` | `n - 1` |
| Initial copy/prefix visits, crossings | `2n` | `n` |
| Top-order writes, excluding inversion shifts | `2n - 1` | `n` |

Thus a no-crossing band removes `3n` visits; a crossing band removes `n`.
All insertion comparisons, inversion visits, event handling, endpoint status
maintenance, and coalescer work charges remain unchanged. These are source-level
work counts, not a prediction of CPU time. On matching certified queries, the
difference in `ActiveEdgeVisits` (and `WorkCount`) equals the sum of the
differences in initialization and verification visits.

## Diagnostics and lifecycle

Both modes reset these counters on every query:

- `NoCrossingBandCount` and `CrossingBandCount` classify bands after complete
  inversion enumeration. Their sum equals `BandCount` on certified queries.
- `BandInitializationVisits` counts actual initial copy/gap-start/prefix visits,
  excluding local prefix updates after crossings and the no-crossing stream.
- `TopOrderWrites` counts all assignments to that buffer, including shifts.
- `TopOrderVerificationVisits` counts visits in the final equality-check loop.

Counters can be partial after fallback. Existing scratch arrays are retained,
including the arrays unused by a no-crossing band. Every crossing band fully
initializes the live prefix and gap-start range before reading it, so stale
values from a previous band or query cannot affect the result. The engine
remains neither thread-safe nor reentrant; prepared snapshots remain immutable.
