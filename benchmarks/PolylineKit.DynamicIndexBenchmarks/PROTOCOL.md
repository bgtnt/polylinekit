# Dynamic edge-index experiment protocol

Declared on 2026-09-25 before computing this experiment's removal traces or timing
results. This is a bounded index experiment with an illustrative vertex-removal
driver, not a production simplifier, a new public API, or a quality benchmark for
simplification algorithms. Settings must not be tuned after seeing outcomes.

## Fixed workloads

Use all **109 admitted rings** in the frozen
[RegionCoverage dataset](../../examples/RegionCoverage/data/README.md): 98 complete
county rings and 11 complete congressional district rings. Preserve their source
vertex order, IDs and parsed binary64 values, removing no further points. Verify
the existing manifest's two derived-file hashes. As in RegionCoverage, subtract
one center of the combined bounding box of all 109 rings, without scaling.
Keep every workload regardless of index speed or number of accepted removals.

Add exactly six synthetic rings: two families at **128, 512 and 2048 vertices**.
No 8192-vertex extension or tolerance sweep is included in this iteration.

- **Radial:** for `i=0..n-1`, let `theta=2*pi*i/n` and
  `r=1000+150*sin(7*theta)+50*sin(31*theta)`; append
  `(r*cos(theta), r*sin(theta))`. Radius stays positive. The vertex coordinates
  are the actual .NET binary64 results; record their input hash because platform
  math implementations need not reproduce transcendental functions bit for bit.
- **Orthogonal comb:** let `k=(n-4)/4` and `p=1000/(3*k+1)`. Begin with
  `(0,0),(1000,0),(1000,400)`. For `i=k-1..0`, let
  `xr=(3*i+2)*p`, `xl=(3*i+1.5)*p`, `y=80+20*(i%2)`; append
  `(xr,400),(xr,y),(xl,y),(xl,400)`. Finish with `(0,400)` and close implicitly.
  This has exactly `4*k+4=n` vertices and nonoverlapping rectangular notches.

Synthetic coordinates are already near the origin and receive no additional
translation or scaling. Every initial and final ring must pass the same NTS
validity/simple/nonzero-area checks. Failure aborts; do not repair or silently
omit a ring. Initial rings have no holes or multipart components. These checks
do not establish preservation of a GIS layer's topology or relations to other
features. Radial and one-sided comb inputs may have few rejected intersections;
report observed counts rather than adjusting their geometry to balance outcomes.

## Deterministic removal driver

Assign immutable original vertex IDs `0..n-1` and active cyclic predecessor/next
links. Visit original IDs in increasing order for up to **three complete passes**,
skipping inactive vertices. Stop immediately at four active vertices, or after a
complete pass accepting no removal. Input positions never move.

For an active vertex `B` with current neighbours `A,C`, reject coincident `A,C`.
The proposal reaches the index only when point-to-segment distance from `B` to
`AC` is at most `0.01 * max(initialBoundsWidth, initialBoundsHeight)`.
This fixed threshold measures only the currently removed vertex. It is not a
cumulative Hausdorff, area, or original-chain error guarantee.

Query the **inclusive** bounding box of chord `AC`. Every active edge with an
overlapping box is a candidate, including endpoint and zero-extent contacts.
Exclude exactly the two edge IDs being replaced, `AB` and `BC`, from narrow-phase
intersection testing. Use the same NTS `RobustLineIntersector` predicate for all
other candidates:

- Reject any proper crossing, positive-length collinear overlap, or other contact.
- Permit a single-point contact only at `A` with its surviving incoming edge,
  or at `C` with its surviving outgoing edge. Determine adjacency by vertex/edge
  identity, not merely by equal coordinates. Require a single reported intersection
  exactly equal to the permitted endpoint; do not add epsilon-based allowances.

Test **every** nonexcluded candidate without early termination. Candidate order
may differ between indexes, but candidate sets, predicate counts and acceptance
must agree. Sorting is permitted in untimed validation, not added to timed queries.
On acceptance, delete both old edge entries and insert the replacement chord,
reusing the slot associated with its starting vertex `A`; deactivate `B`'s slot.
Deletion receives the original edge ID and old bounds, not the replacement bounds.
No candidate or intersection result may be cached across modifications.

Generate the reference trace once with the linear implementation. Every competing
index must reproduce the candidate IDs, accepted vertex IDs and final active
ring exactly on each workload before timing. Include separate analytic predicate
controls for proper crossings, collinear overlaps, legal adjacent endpoint
contacts, and illegal nonadjacent contacts. NTS predicates and final validity
checks are implementation checks, not an exact-rational oracle.

## Measurements and interpretation

Measure two distinct operations on identical frozen workloads:

1. **Index replay:** replay the reference query bounds and accepted updates;
   perform candidate retrieval and index updates, without narrow-phase geometry,
   distance calculations or acceptance decisions. Consume an order-independent
   candidate-ID digest. This isolates the dynamic index workload.
2. **Complete removal session:** run the same scheduling, distance eligibility,
   bounding-box queries, every required intersection test and accepted updates.
   Consume candidate, predicate, accepted-removal and final-ring identities.

Report fresh construction separately and construction plus the complete replay/
removal session, so initial build cost cannot disappear through amortization.
A destructive replay must begin with the same initial index state each time;
do not replay it repeatedly on its already simplified state. Preparation of a
fresh replay outside a query-only timer must be labeled and its cost shown.
Report time and managed allocation, three sequential process runs and raw sample
medians/ranges under a fixed runtime/build. Managed counters do not measure
retained memory or native allocations. Loading, source validation, trace creation
and final independent validation are outside timers and must be identified.

For every workload report initial/final vertices, eligible proposals, candidates,
narrow-phase calls, blocked proposals (at least one forbidden contact) and accepted
removals; also record source,
input/trace hashes, commands, implementation/binary identity and exclusions.
Aggregate real and synthetic families transparently; do not select only favorable
cases. A static index with rebuilds and a dynamic tree with deletion/reinsertion
solve different maintenance tasks; compare their complete required update costs.
Private RtTools may be a local reference when available, without becoming a public
project dependency or publishing its source. This experiment makes no universal
R-tree ranking and does not replace Winding's internal intersection algorithm.
