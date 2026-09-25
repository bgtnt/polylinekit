# A double-input scanline experiment

This is the design rationale for the separate experimental `GuardedDoubleSweep`.
Its [fixed protocol](DOUBLE-PROTOCOL.md) defines certification, fallback and
measurement. It is not a shipping backend or a speed claim.
The bounded-integer experiment does not require the eventual scanline algorithm
to quantize user coordinates. ClipperD itself still converts through an integer
grid, but that is a numerical design choice rather than a requirement of sweeping.
See [Clipper's coordinate contract](https://www.angusj.com/clipper2/Docs/Overview.htm)
and [robustness discussion](https://www.angusj.com/clipper2/Docs/Robustness.htm).

## What to borrow

Use incremental active-edge maintenance: sort endpoint events once, insert and
remove affected edges, and update the ordering at intersections. The current
integer prototype instead scans every edge and re-sorts the active set at each
endpoint Y. Its few-level grid advantage does not remove this potentially
quadratic cost for contours with many distinct endpoint levels.

Retain independent loop labels, winding counts, simultaneous crossing groups
and direct integration of filled gaps. For intersection, integrate only where
both loops are filled. Constructing output rings, joining them and tracking hole
ownership is unnecessary for an area-only result. This reduced work is a
plausible advantage; it is not evidence of a measured advantage.

The initial target is two valid simple rings and intersection area, with
unchanged binary64 input and no new public API. Reuse existing workspaces and
compensated sums. Prepared immutable zone data can be evaluated separately after
the core succeeds. Do not start by rewriting the whole clipping library.

## The numerical layer cannot be a type substitution

Use fast double calculations with proven error bounds. Resolve uncertain
decisions exactly or abandon the entire provisional result and call the existing
Winding implementation. An epsilon comparison is not a substitute for consistent
ordering. Keep exact zero meaningful for contacts and overlapping edges.

Existing `RobustOrientation` can certify orientation of original input triples;
it does not certify ordering or equality of two constructed intersections.
Represent events by their source edges and certified location bounds. Comparing
determinant ratios requires additional filtered predicates and, when needed,
expansion or exact dyadic arithmetic. Rounded crossing coordinates cannot serve
as authoritative event keys. The existing `CrossingParameter` returns approximate
parameters and is not such a comparator. Adaptive predicates are an established
technique; see [Shewchuk](https://www.cs.cmu.edu/~quake/robust.html).

Area-value accuracy is a separate obligation. Compute narrow widths and level
differences before rounding; subtracting two already rounded large crossing
coordinates can erase small areas even with correct topology. Ordinary filtered
calculations need certified error bounds for their values as well as signs.

## Implemented first experiment

The experimental guarded double intersection sweep uses conservative whole-call
fallback for every unsupported or uncertified case. It discards partial
accumulation and preserves the existing operation's numerical contract. Reasons
and frequencies are recorded; fallback costs count in full-query timings. The
reusable instance retains its scratch buffers and resets state on the next call.
This limits the first implementation without silently weakening robustness.

Validate the original unquantized Census contours, degeneracies, narrow regions,
large offsets, extreme exponents, swapped/reversed rings and adversarial event
ties. Compare independent exact cases and NTS/Clipper with their respective
construction contracts stated. Measure complete queries, preparation, allocations,
fallback rate and existing regressions, using the existing reproducibility rules.
SIMD or unsafe code can follow a measured hot loop; neither fixes redundant
active-list reconstruction or inconsistent event ordering. No C++ port is needed
to test this architectural hypothesis.

`GuardedDoubleSweep.cs` implements this conservative version. It retains a sorted
copy of the active list per endpoint band to enumerate inversions; incremental
membership does not remove all per-band work. The [measurements](DOUBLE-RESULTS.md)
report its actual cost. It does not yet implement a cheap scalar filter ahead of
every interval operation, nor a neighbor-event priority queue.

## Current certificate

1. Supplied binary64 vertices are treated as exact values. Both complete arrays
   are checked before an early AABB zero; effective vertex-count and unsupported
   input cases delegate to the original public operation.
2. Scalar coordinate differences use TwoDiff. A nonzero tail selects the adjacent
   outward endpoint. The bounded coordinates keep these additions/subtractions
   far from overflow. Products/divisions do not assume exact residuals under
   underflow: their rounded extrema are expanded with BitDecrement/BitIncrement.
   Nonfinite interval endpoints or denominator intervals containing zero cause
   fallback. Algebraically exact zero/identity operations retain point intervals.
3. Active-edge comparisons require disjoint X enclosures or justified exact
   source/support equality. Shared endpoint ordering requires certified slopes.
   Each inversion of the certified bottom/top orders identifies a crossing of
   two affine edges. Its Y is enclosed by
   `bottom + (xRight(bottom)-xLeft(bottom))/(slopeLeft-slopeRight)`.
4. Constructed event intervals must be strictly separated and strictly inside
   their endpoint band. Processing requires adjacent edges, and the resulting
   active order must match the independently constructed top order. Uncertain
   same-level or multiway events cause whole-call fallback.
5. Independent prefix winding counts select intersection gaps. Each gap retains
   its last level until one of its boundaries changes. Between changes its width
   is affine, so `(widthStart+widthEnd)*height/2` gives its exact real integral.
   Every arithmetic operation encloses that expression; dependency between
   interval operands can widen the enclosure but cannot invalidate it.
6. Certified ordering proves widths and height nonnegative, so intersecting their
   enclosures with `[0,+infinity]` is valid. At the provenance-identified crossing
   of the actual boundary pair, width is exactly zero. These operations do not
   replace uncertain topology with an arbitrary clamped value.
7. The returned midpoint lies inside the accumulated area enclosure. The maximum
   distance to either endpoint is rounded upward, then checked against the fixed
   absolute and relative budgets. A failed check discards all provisional area.
   The next call resets state; fallback's error radius is NaN to prevent claiming
   this new certificate for a result computed by the existing Winding engine.

The independent checks use exact rational convex clipping of the actual dyadic
input coordinates and verify the error-radius inequality before rounding the
oracle result. Analytic and existing rational-slab controls cover additional
degeneracies. These checks support the derivation; they are not a general formal
proof of the implementation.

Clipper is a conceptual reference here; no Clipper source was copied. Any later
source reuse must retain its upstream provenance and applicable license notices.
