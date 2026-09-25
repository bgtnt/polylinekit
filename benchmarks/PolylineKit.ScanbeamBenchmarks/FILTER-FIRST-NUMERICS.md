# Scalar order before common support: parity contract

The benchmark-only ninth constructor flag, `filterBeforeSupport = false`,
reorders two checks in `CompareAtEndpoint`. The existing work charge and
`left == right` shortcut remain first. When scalar filtering and the new flag
are enabled, the existing strict scalar-order filter runs before `SameSupport`.
An accepted order returns immediately. An inconclusive probe runs the original
support check, then the original interval/tie path if support does not match.
No comparison probes the scalar filter twice.

The default branch retains support-before-filter ordering. Disabling scalar
filtering makes the new flag ineffective. `SameSupport` within
`HorizontalDifference` and all geometric arithmetic remain unchanged. The
experiment introduces no arrays, dependencies, public APIs or new tolerances.

## Why strict order excludes common support

All compared edges are nonhorizontal, ordered by lower/upper Y, and the endpoint
Y lies in both closed vertical ranges. The scalar filter's existing contract
therefore applies: a successful probe returns the exact strict sign of the two
edges' horizontal separation at that Y. Unavailable filter metadata returns
inconclusive without accepting an order. Its rounding/error certificate is not
changed by moving the call.

`SameSupport` has two sufficient conditions:

1. Both lower endpoints and both upper endpoints have equal coordinates. Their
   exact affine horizontal positions agree throughout the common Y interval.
2. Both edges are vertical and their constant X coordinates agree. Their
   horizontal positions agree even when their Y extents differ.

In either case the exact separation is zero, so the strict filter cannot accept
either sign. This also covers numeric equality of signed zero coordinates;
bitwise equality is not required. The existing certified radii enclose each
exact position, so overlapping exact positions cannot be strictly separated by
the filter. A supported pair consequently takes an inconclusive scalar probe,
then the same edge-ID tie-break as before. Conversely, strict acceptance proves
that the old support check would fail, permitting its removal for that call.

## Operation and fallback parity

For a nonsupported pair, the filter reads the same prepared records at the same
Y and returns the same outcome. Strict acceptance returns the same order. An
inconclusive result executes the unchanged interval-X evaluation and tie logic.
For a supported pair, the additional probe is pure, performs no interval work,
updates no cache and cannot throw an uncertainty signal. Its result is ignored
after the unchanged support shortcut succeeds.

Thus comparison results, sort/event order, gap contribution order, area and
certificate bits remain identical. No existing work charge is removed or added,
so work-budget fallback and other uncertainty decisions retain their order.
Only actual support/probe operation totals differ; instrumentation is observational
and does not change the budget policy. The existing engine/reset lifetime and prepared
snapshot ownership rules are unchanged.

## Logical versus actual diagnostic counts

The existing `FilterAttemptCount`, `FilterAcceptedCount` and
`FilterIntervalCount` count logical filter decisions after excluding common
support. The new branch preserves that definition:

- Strict acceptance increments attempt and accepted counts.
- Inconclusive probing followed by matching support increments neither old
  filter count, matching the old shortcut.
- Inconclusive probing with different support increments attempt and interval
  counts before running the original interval path.

Three new diagnostics reset before every raw or prepared query:

- `EndpointSupportTestCount`: actual `SameSupport` calls inside the endpoint
  comparer, excluding the `left == right` shortcut and gap arithmetic.
- `EndpointSupportMatchCount`: successful tests among those calls.
- `ScalarProbeCount`: actual scalar filter calls, including new inconclusive
  probes that precede a matching-support shortcut.

Only two new increments execute: one after each nonsame-edge comparison's work
charge, and one after a support match. Both variants execute exactly the same
increment streams. Support tests are the comparison count minus early strict
acceptances (zero subtraction for the baseline). Probe totals are the comparison
count for filter-first, the logical attempt count for the baseline, or zero when
filtering is disabled. These are derived operation counts, not independent
instrumentation of each call. This avoids measuring skipped counter increments
as a benefit. Both variants still pay common instrumentation overhead relative
to the historical binary; historical timing sensitivity remains necessary.

With scalar filtering enabled and matching comparison streams:

```text
old support tests - new support tests = FilterAcceptedCount
new scalar probes - old scalar probes = EndpointSupportMatchCount
old scalar probes = FilterAttemptCount
new scalar probes = FilterAttemptCount + EndpointSupportMatchCount
```

The support-match count and all old diagnostics remain equal. With filtering
disabled, both versions have zero probes and identical support counts. These
identities also apply to matching partial streams before fallback; counters can
be partial after an exception. Copies, reads and numeric work outside the
endpoint comparer are unaffected. Fewer support tests are not a prediction of
CPU-time improvement: extra probes on supported pairs and changed generated
code still require measurement against the unchanged default control.
