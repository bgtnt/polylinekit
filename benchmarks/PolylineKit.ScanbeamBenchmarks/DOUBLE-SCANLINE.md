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

## Bounded next step

Implement an experimental guarded double intersection sweep, with conservative
whole-call fallback for every unsupported or uncertified case. A fallback must
discard partial accumulation, release its workspace and preserve the existing
operation's numerical contract. Instrument reasons and frequencies; count their
cost in full-query timings. This limits the first implementation without silently
weakening robustness. Extend exact predicates only where evidence warrants it.

Validate the original unquantized Census contours, degeneracies, narrow regions,
large offsets, extreme exponents, swapped/reversed rings and adversarial event
ties. Compare independent exact cases and NTS/Clipper with their respective
construction contracts stated. Measure complete queries, preparation, allocations,
fallback rate and existing regressions, using the existing reproducibility rules.
SIMD or unsafe code can follow a measured hot loop; neither fixes redundant
active-list reconstruction or inconsistent event ordering. No C++ port is needed
to test this architectural hypothesis.

Clipper is a conceptual reference here; no Clipper source was copied. Any later
source reuse must retain its upstream provenance and applicable license notices.
