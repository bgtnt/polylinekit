# Baselines, provenance and reconstruction limits

## Source definitions

[Pelekis et al., TIME 2007, §3](https://kbs.uni-hannover.de/~ntoutsi/papers/07.TIME.pdf) supplies the original LIP area/length weighting. The expanded [Pelekis et al., JIIS 2011, §3.1, Definitions 2–8 and Figures 7–8](https://geoanalytics.net/and/papers/jiis11.pdf) supplies the GenLIP baseline here:

```text
LIPgood = sum_i Ai * wi
wi = (length_Pi + length_Qi) / (length_P + length_Q)
DIR = (1 - cos(phi)) / 2
LIPbad = (max(TA(P,Qstart), TA(Q,Pstart)) + length_Pi*length_Qi*DIR) * wi
TA = |cross(translation, segment)|,
     or (|translation| + |length_Pi-length_Qi|)*d when the cross product is zero.
GenLIP = sum over good groups and bad segment pairs.
```

The denominator uses the **whole original routes**. Goodness requires `0 < phi < 90°` and a clear connector between the examined endpoints. Parallel pairs are therefore bad. A good group can contain mutual crossings; its component paths must be simple. GenLIP traverses according to accumulated lengths, with a lookahead parameter `p`. We reconstruct `p=0`. These are independent formula implementations, **not authors' source or certified reproductions of their software**.

## Explicit reconstruction choices

The paper is not an executable specification. The following choices are visible in `GenLip.cs` and are part of the experimental baseline label:

| Issue | Implementation |
|---|---|
| Figure 8 termination/last segments and recursive removal | Consume each completed bad pair once; flush the final good group once; stop after both last segments. |
| `next` inequalities | Use the printed `>=` inequalities, evaluated simultaneously. At an exhausted side, advance the remaining side to satisfy the described traversal to both ends. |
| Held segment becomes bad | Reject with `NotSupportedException`; ownership/rollback is ambiguous. No double counting. |
| One side ends immediately after a bad pair | Reject the unpaired tail. No invented zero-length segment. |
| Incident endpoint contact with connector | Ordinary incidence is allowed; interior connector crossings fail goodness. |
| `d` | Explicit positive parameter, default `1e-6` in coordinate units; analytic checks include another value. It matters in degenerate bad pairs. |
| Parallel classification | Default follows the strict inequality. `parallelIsGood` is a separately labelled correction, never silently enabled. |
| Zero-length segments | Remove exact consecutive duplicates before GenLIP processing. |
| General arrangements | Reject crossing orders incompatible with the polygon construction; these are not silently interpreted through a Clipper fill rule. |
| Lookahead | `p>0` is not implemented. Its effect on cases involving bad pairs is unassessed. |

The decisive near-touch pair and every timing fixture pass as **one good group with zero bad pairs** using the original equal-grid inputs. Thus they exercise the published LIPgood formula without `d`, bad-pair ownership or lookahead choices. A lookahead that only runs after a failed goodness test cannot change these all-good partitions. Collinear-merged near-touch paths additionally exercise our documented exhausted-side boundary rule; the direct LIP proof does not depend on that rule.

The tests also exercise mixed good/bad groups with a hand-computed whole-route denominator, opposite and orthogonal directions, collinear translation and a non-monotone vertical segment pair. This does **not** establish comprehensive conformance for arbitrary trajectories. The package ships neither reference baseline.

## Fair repairs and rejected shortcuts

Collinear merging restores the strict GenLIP score of two parallel length-10 lines separated by 1 from `10/n` to 10. Allowing parallel pairs to remain in a good group also restores 10. Therefore that subdivision effect is **not** our continuation criterion. Neither repair removes the near-touch change in LIPgood.

Snapping y to a `1e-5` grid stabilizes the particular `±1e-6` example, but moves the discontinuity to a half-grid threshold; `stability.json` includes two inputs separated by `2e-12` across that threshold. This is a counterexample to snapping as a universal cure, not a claim that application-specific tolerances are useless. Ignoring exact tangencies alone cannot remove the unequal limits on the positive and negative sides.

Removing intersection-dependent weights, or replacing them with fixed spatial/continuous weights, can also remove the defect. Those are legitimate alternative objective functions, not demonstrations of a novel PolylineKit invention. This experiment establishes a useful tradeoff against the published regional weighting, not dominance over all reasonable redesigned variants.

The publicly accessible [trajectory-distance-benchmark](https://github.com/douglasapeixoto/trajectory-distance-benchmark/tree/e5b2f1d16f81b6c3a093765061ebb458f076d679) was inspected as another implementation lead. Its embedded Java `LIPDistanceCalculator` builds areas when intersections are encountered and computes lengths from interior sampled points; no-intersection/tail and partial-segment behavior does not reproduce our formula oracle. This is a source-inspection finding, not an executed benchmark. It was neither copied nor used as evidence of superiority. No root license was found during inspection.

## Other primary references inspected

- [Clipper2 overview](https://www.angusj.com/clipper2/Docs/Overview.htm), [fill rules](https://www.angusj.com/clipper2/Docs/Units/Clipper/Types/FillRule.htm), [area prerequisites](https://www.angusj.com/clipper2/Docs/Units/Clipper/Functions/Area.htm), [robustness](https://www.angusj.com/clipper2/Docs/Robustness.htm), [engine source](https://github.com/AngusJohnson/Clipper2/blob/main/CSharp/Clipper2Lib/Clipper.Engine.cs). Active-edge ordering and explicit fill rules inform the experiment design; the tested artifact is the pinned 2.0.0 NuGet package, not mutable `main`.
- [Jekel et al. implementation](https://github.com/cjekel/similarity_measures/blob/master/similaritymeasures/similaritymeasures.py), `area_between_two_curves`: quadrilateral accumulation after equalizing point counts. This is related prior art, not an equivalent oracle for unequal graph sampling.
- [Chambers and Wang, homotopy area](https://jocg.org/index.php/jocg/article/view/3070): a different curve deformation optimization problem. We do not claim to implement it.

The candidate graph integral is elementary area integration. No novelty or priority claim is made. Clipper already supplies the requisite general polygon operation when the intended semantics are its fill rules.
