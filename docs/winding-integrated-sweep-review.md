# Integrated sweep review

Reviewed runtime changes at `5614828` and their evidence at `d141806` on
2026-09-24. Two separate agent reviews covered the certificate and its engine
integration; the coordinating review independently recalculated the benchmark
table and added the regressions below. This is a code and test review, not a
formal proof or an external human review.

**Conclusion: no blocking correctness defect found in this integration.** No
runtime change was required. The measured tradeoffs in the
[implementation report](winding-integrated-sweep.md) remain the basis for deciding
whether to merge the optimization; this review does not establish universal
speedup or repair inherited area-arithmetic limitations.

## What was checked

- Lexicographic event order, vertical edges, exact zeros, adjacent collinear
  continuation and retracing, and full-segment checks for newly adjacent edges.
- Treap rotations and links, rejection propagation, bounded traversal, and
  reinitialization after partially completed insertions or removals.
- Selection cancellation after crossings or overlaps, including coincident edges
  that emit no split event; clearing offsets on acceptance; continuing the same
  candidate pass on rejection.
- Workspace leasing, nested calls and borrowed vertex lifetime. The certificate
  modifies only its own scratch and exact-predicate diagnostics on rejection.
- Single-loop scope, unchanged four-chain accumulation, and exclusion of the
  two-independent-loop operation from certification.
- All 81 benchmark rows: independently recomputed process medians and ratios,
  input identities and all recorded numerical outputs across six processes.
  All 2,430 recorded allocation samples are zero. No timing reruns or selection
  threshold changes were made during review.

## Validation gaps closed

The previous small-grid all-pairs oracle used the production `ExactSign` method.
It now uses a separate Int64 determinant; coordinates are small integers and its
products cannot overflow. It is applied both to the existing 10,000 randomized
trials and to all **125,628 cycles of 3 through 9 distinct vertices on a 3x3 grid**,
modulo cyclic shifts. Both traversal directions are retained. Of these cycles,
**1,950 are simple**; every certificate result agrees with the independent oracle.
This specifically exercises collinearity, vertex-on-edge contact, vertical
events, and varied treap/index orderings.

Previously, the rejection tests exercised geometry but did not show actual
traversal-budget exhaustion. A deterministic 4,096-vertex fixture now arranges
horizontal edge pairs using the treap's priority order. Its left-side events
exhaust the real traversal budget before the right-side crossings are reached.
The test checks rejection, a negative remaining budget, release of the borrowed
input, and successful reuse of the same certificate for a triangle. Reflection
distinguishes budget rejection from geometric rejection without introducing a
runtime test switch or public API.

This adversarial fixture is not simple. It tests certificate exhaustion and
recovery directly; it does not claim to exercise the public engine's selector
or its full general fallback on this particular input. Existing late-crossing
tests cover continuation of the general engine after geometric rejection.

## Results and limits

Release build: zero warnings and errors. **167,646 assertions pass in each of five
modes**: portable, modern, forced scalar, no AVX, and no hardware intrinsics.
The suite includes 135,795 prepared-sweep checks and 164,062 winding checks.
[Build, test and evidence audit logs](../results/winding/integrated-sweep/review)
are retained separately from the original benchmark validation logs.

Runtime source is unchanged from measured commit `5614828`:

```powershell
git diff --exit-code 5614828 -- src/PolylineKit
dotnet build PolylineKit.slnx -c Release --no-restore
./scripts/verify-implementations.ps1
```

The review build's DLL byte hash differs because assembly informational-version
metadata embeds `d141806` instead of `5614828`. The original measured DLL hashes
and raw timing files remain unchanged; source equivalence is not represented as
binary identity.

Exhaustive small-grid agreement is not proof for every binary64 input. Existing
extreme-scale tests remain relevant. The inherited skinny-triangle area error,
small-star slowdown, smooth-input overhead, and late-rejection costs remain
documented limitations. This step leaves the work local and ready for a pull
request; it performs no push or merge.
