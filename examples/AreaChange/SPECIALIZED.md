# Choosing a simplification-area operation

General winding comparison is useful when the consumer needs actual filled-region
change for arbitrary finished contours. A comparison against Clipper alone does not
establish that it is the cheapest applicable method. This experiment also evaluates
two specialized exact-arithmetic identities and a conservative threshold filter.
All new code is internal to this example; the library API and dependencies are unchanged.

The [three-process results](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/AreaChange/SPECIALIZED-RESULTS.md) retain every workload, including
filter fallbacks and the cost of checking geometric prerequisites.

The specialized identities are exact in real arithmetic; their computed outputs remain
floating-point estimates. The ordinary baseline uses translated compensated shoelace
sums, with coordinate validation inside timing and supplied correspondence. A separate
`compensated-products` row retains subtraction/product tails as well; it does more work
and is not presented as the cheapest available formula. It declines extreme products
instead of claiming accuracy there. Only the interval filter makes conservative decisions.

## Preconditions determine the method

| Available guarantee / requested result | Method | Work after the guarantee is supplied |
|---|---|---|
| Simple polygons, one contains the other; overlap areas needed | Compensated shoelace areas, XOR = abs(A − B) | O(n + m); constant time if accurate areas are already maintained |
| Simple polygons with retained-vertex correspondence; replacement pockets are simple, disjoint and each toggles the fill once | Sum absolute areas of removed subchains closed by their replacement chord | O(n + m) |
| Simple polygons with retained-vertex correspondence; only a threshold decision needed | Conservative lower/upper bounds, then Winding if unresolved | O(n + m) filter plus any fallback |
| Arbitrary accepted closed walks; actual filled areas needed | `WindingArea.FilledRegions` | General crossing/overlap processing |

These guarantees are substantive. Simple original/result contours do not imply simple
or disjoint replacement pockets. Shoelace subtraction measures net area change;
outward and inward changes can cancel. A small pocket inside an already filled region
of a self-overlapping NonZero path can change signed multiplicity without changing fill.
Never use the local-pocket identity for that general case.

The nested contract exists in established tooling:
[NetTopologySuite PolygonHullSimplifier](https://nettopologysuite.github.io/NetTopologySuite/api/NetTopologySuite.Simplify.PolygonHullSimplifier.html)
produces inner/outer topology-preserving hulls with containment guarantees. We do not
add an NTS dependency or benchmark its simplifier here. The example's Clipper simplifier
does not supply that guarantee. Likewise, triangle importance in
[Visvalingam–Whyatt simplification](https://postgis.net/docs/ST_SimplifyVW.html)
is not the final symmetric difference, and its general output may lose validity.

## Conservative threshold filter

For simple polygons P and Q with Q an exact cyclic subsequence of P's vertices, let
A and B be their unsigned areas and D their symmetric-difference area. In exact arithmetic:

```text
L = abs(A - B)
U = sum over replacement chains of sum abs(fan-triangle area)
L <= D <= U
Jaccard(D) = D / union = 2D / (A + B + D)
```

Each fan closes a removed original chain with the new chord, including the wraparound
chain. The winding difference decomposes into these triangles. Triangle inequality
bounds its absolute integral, which in turn bounds the change in filled membership.
The lower bound is the difference in filled areas. Fan triangles need not be disjoint
for the **upper bound**; pocket disjointness is required only for the exact local identity.

`SpecializedArea` propagates intervals through coordinate subtraction, multiplication,
addition, absolute values and division, rounding endpoints outward with
`Math.BitDecrement` / `Math.BitIncrement`. It does not treat compensated summation as a
certified bound. Threshold acceptance uses the upper Jaccard bound; rejection uses the
lower bound, and everything else is unresolved. These enclosures concern the exact
geometry of the supplied binary64 coordinates, under the stated preconditions and
ordinary IEEE binary64 evaluation with gradual underflow. Invalid data or unavailable
bounds cannot yield a certified decision. Fallback results retain Winding's documented
floating-point limitations; a threshold extremely close to the exact answer is not
magically certified by calling the general engine.

The zero-union case has no Jaccard value, matching the library contract. A threshold
decision does not return the exact XOR or Jaccard value. Consumers needing those values
must still evaluate the relevant area method even when a filter would decide a threshold.

## Workloads and measured scopes

The nested synthetic contours contain integer points `(i, i*i)` closed by their final
chord, at 16, 64, 256, 1024 and 4096 original vertices. The inner contour retains every
fourth vertex and the final vertex. Strict convexity and containment follow from the
parabola construction. This differs from the review's regular-circle benchmark; ratios
must not be compared as if the inputs or machine were the same.

The local-change contours alternate disjoint unit-area triangular notches and bumps
on the top of a rectangle. There are 4, 16, 64 or 256 triangles. Original counts are
`3*k + 3`, retained counts `2*k + 3`; both areas equal `30*k`, while XOR is `k`, union
`30.5*k` and intersection `29.5*k`. Thus ordinary area subtraction incorrectly gives zero.
The five requested area fields are consumed for both specialized and general calls.
Synthetic construction and supplied correspondence are outside timing.

The threshold matrix uses the existing twelve frozen Natural Earth simplification
pairs at Jaccard thresholds 0.1%, 1% and 3%. These are illustrative policy choices,
not learned settings or evidence of production acceptance rates. Every prepared pair
is checked with exact dyadic integer simplicity predicates and an exact cyclic index
mapping before timing. Prepared calls pay for bounds on every request; bounds are not
cached. Certification and mapping are separately measured. At 1%, an additional
uncertified-input comparison charges them on **every** call. Direct Winding needs no
simplicity certificate and receives no artificial validation overhead.

The certifier deliberately uses O(n²) pair enumeration and BigInteger predicates;
it prioritizes transparent correctness for small examples. Its timing does not establish
the cheapest possible certification method. It rejects contacts/retracing rather than
extending the proof to ambiguous topology. No general containment or pocket-disjointness
detector is introduced: those two benchmark guarantees come from synthetic construction.

All scopes start with finished contour arrays. They exclude simplification and I/O,
so they are **not** additional measurements of the complete simplifier consumer in
[RESULTS.md](https://github.com/bgtnt/polylinekit/blob/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/AreaChange/RESULTS.md). Warm allocation counts also exclude retained workspace,
input construction, first calls and prepared certificates.

## Reproduce

From the repository root, on an otherwise idle machine:

```powershell
dotnet build PolylineKit.slnx -c Release
dotnet run --project examples/AreaChange -c Release --no-build -- check
$env:DOTNET_TieredCompilation = '0'
1..3 | ForEach-Object {
    dotnet run --project examples/AreaChange -c Release --no-build -- specialized-benchmark examples/AreaChange/artifacts/specialized $_ (git rev-parse HEAD)
}
dotnet run --project examples/AreaChange -c Release --no-build -- specialized-summarize examples/AreaChange/artifacts/specialized
```

Three fresh sequential processes rotate method order and retain five calibrated batches
per method, input/assembly hashes, all numerical outputs and allocations. The summarizer
validates samples, recomputes medians and checks the measured binaries and inputs before
writing a report. Label uncommitted source explicitly if measuring it. CI runs numerical
and geometric checks only, without timing assertions.

## Related area definition

Kronenfeld and Deng's 2019 paper
[Between the Lines: Measuring Areal Displacement in Line Simplification](https://ica-adv.copernicus.org/articles/1/9/2019/)
defines shift displacement through the integral of the absolute winding-number
difference. Its algorithm inserts crossings into a difference walk and tracks winding
while accumulating modified shoelace terms. This is directly related to
`AbsoluteWinding` of the correctly constructed difference walk. The accumulation is
linear after crossing processing; this does not make the entire algorithm linear.

For open lines the paper assumes shared endpoints. Polygon rings need explicit closure
when constructing an endpoint-bridged difference walk. This quantity is not generally
the XOR of independently filled self-overlapping paths, or of opposite-oriented rings.
`FilledRegions` remains the relevant API for this example. We claim no mathematical
novelty for these area definitions.
