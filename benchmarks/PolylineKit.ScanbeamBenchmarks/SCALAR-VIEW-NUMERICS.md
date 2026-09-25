# Prepared scalar views: parity and lifetime contract

`GuardedDoubleSweep`'s optional eighth constructor flag,
`borrowPreparedScalars = false`, selects a benchmark-only storage experiment.
It removes the prepared scalar-filter array copy while retaining contiguous
copied geometry. No interval operation, numerical guard, comparator, event order,
work charge, public API or dependency changes.

## Storage mapping

A prepared snapshot owns private immutable arrays of coordinates, nonhorizontal
edges, scalar filter records and sorted endpoints. Its scalar record at local
index `i` was prepared from its edge at the same index. For a pair `(A, B)`,
global edge IDs enumerate all A edges followed by all B edges. The split is
`A.EdgeCount`, not its vertex count.

The copied baseline stores the same scalar records at those global IDs in the
engine's scratch array. The experiment instead reads A's record at `id` when
`id < A.EdgeCount`, otherwise B's record at `id - A.EdgeCount`, using the existing
`ScalarEdgeAt` ref-readonly accessor. Thus every lookup returns identical field
bits. No filter computation, preparation, rounding or arithmetic is repeated.

Geometry still follows `CopyEdgesTo`: A's copied edges receive `Loop = 0`, and
B's receive `Loop = 1`. Neither geometry-array reference is borrowed by this
mode. Consequently `EdgeAt` stays on contiguous scratch, and `IsFirstLoop`
continues using the copied Loop value. A nonzero scalar split cannot change
winding roles, because the role accessor tests the geometry reference itself.

The existing full-direct mode takes precedence when enabled and keeps borrowing
both geometry and scalar arrays. Its behavior is unchanged apart from the new
record-count diagnostic. When scalar filtering is disabled, the new flag has no
effect. Raw input calls continue building their ordinary scalar scratch records.

## Lifetime and exceptional exits

Binding occurs only after both inputs pass the existing contract, bounds and
deferred slope-preparation gates. AABB-zero and early fallback paths do not bind
records. A prepared call's existing `finally` clears both scalar references,
both geometry references and the split, whether it succeeds, falls back or throws.
`ResetState` also clears them before every raw or prepared query. Mixed calls,
growth, role reversal and reuse of one snapshot in both roles therefore cannot
reuse another query's binding.

The arrays never escape their owner or a ref-readonly engine lookup and are not
mutated. A prepared snapshot remains shareable by separate engines. The engine
itself remains neither thread-safe nor reentrant. The `finally` releases query
references; diagnostics retain counts only.

## Diagnostics and exact parity

Two counters reset to zero at the beginning of every query:

- `PreparedScalarRecordsCopied`: scalar records successfully copied from
  snapshots into filter scratch. Each completed array copy adds its length.
- `PreparedScalarRecordsBorrowed`: scalar records bound for filtering without
  copying, equal to `A.EdgeCount + B.EdgeCount` after binding in either hybrid or
  full-direct mode. It counts available records, not filter calls or distinct
  records actually read.

Both counters are zero for raw calls, disabled filtering, or exits before
binding/copying. A later geometric fallback can retain a nonzero count of work
already done, while its borrowed references are still cleared. These diagnostic
increments add no work-budget charges, matching the unchanged copy policy.

Every scalar access resolves to the same immutable value as the copied mode.
Geometry, endpoint merge, crossing and integration order remain unchanged, so
results, error bounds, fallback decisions and all existing numerical, topology,
gap, active-pass, filter, cache and work counters must match exactly. Only the
new storage counters distinguish this mode from its copied control.

The experiment allocates no new arrays and deliberately retains the previous
scratch allocation policy, including scalar scratch unused by a prepared hybrid
call. Immutable preparation and its payload are unchanged. Any timing benefit
must be measured against the extra split-array read cost; eliminating copied
records alone is not evidence of a CPU-time or retained-memory improvement.
