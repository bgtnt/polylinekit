# Multiple rings: design boundary

This is a design note, not an implemented API. `FilledRegions` currently accepts
one ordered closed path per operand. Self-intersections are allowed, but separate
outer boundaries and holes cannot be passed as a collection of rings. Callers
must not join rings with invented segments merely to fit that signature.

A future region representation could be an ordered collection of closed rings.
Each ring would be validated and implicitly closed independently. No edges would
join successive rings. A region's winding at a point would be the signed sum of
all of its rings, followed by a single NonZero or EvenOdd fill decision. For
NonZero, opposite orientation removes a hole only where the combined winding
becomes zero; two equally oriented nested rings stay filled. EvenOdd ignores
orientation and alternates filling with crossing parity. Overlapping outer rings
would follow the same explicit fill rule.

For two regions, intersection/union/XOR would classify membership independently
for each operand. Returning separate metrics for each ring and adding them would
be incorrect when rings overlap or form holes. Existing chain accumulation would
need operand labels independent of ring boundaries, plus bounds and origin
handling that remains accurate for far-separated components. The current
single-path optimization would require an independently justified collection
certificate; simple rings alone do not prove a simple region.

Before adding a public overload, tests must cover disjoint islands, nested holes,
overlapping rings, shared edges, repeated rings, reversed orientation, tangency,
zero-area rings and far-separated small components. Compare analytic cases and
an independent rational oracle; use Clipper only with matching fill semantics.
Benchmarks should include construction and repeated-call costs separately.
Streaming ring input, empty regions and ownership/copying policy remain open
choices. There is no promise to implement this extension until a concrete
consumer justifies it.
