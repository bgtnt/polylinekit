# Published alpha.2 on independent Helsinki regions

**Core agreed with the configured NTS reference on this new corpus. The
three-way comparison did not meet the frozen acceptance conditions, so no
performance measurements were taken.** This is evidence for the published
package's numerical integration, not a new speed claim or external adoption.

Assessment runner:
[`1dcdfe8f1e7010c7c3bc6ca80db8ffa8e839fab2`](https://github.com/bgtnt/polylinekit/commit/1dcdfe8f1e7010c7c3bc6ca80db8ffa8e839fab2).
Actual NuGet.org `PolylineKit.Core` **0.1.0-alpha.2**, source
[`5e9dcc1`](https://github.com/bgtnt/polylinekit/commit/5e9dcc1a5bcb34b5b2d8ae60105d328aec66a80c),
was restored into an initially empty cache in an outside-checkout consumer.
There was no library ProjectReference, private DLL, local feed or geometry
repair. The package does **not** contain the later source factory optimization.
No Core implementation, public API or NuGet version changed in this assessment.

The compact [machine-readable assessment](ASSESSMENT.json) contains the complete
exclusion list, remaining failures, maximum errors with actual/reference values,
input and binary identities, and hashes of the retained local reports.
The [protocol](PROTOCOL.md), [source manifest](data/manifest.json) and
[reproduction commands](README.md) are versioned with the runner.

## Inputs and operation

The question is **how much of each Helsinki planning-area feature lies within
each basic district?** Both layers are complete official WFS snapshots, requested
in projected EPSG:3067 metres. Preserve source Polygon/MultiPolygon hierarchy,
shells, holes, coordinates, directions and closing duplicates. Preparation uses
the published `FromPolygon` / `FromPolygons` factories directly.

| Population | Districts | Planning areas |
|---|---:|---:|
| Original features | 34 | 1,095 |
| Original components | 34 | 1,296 |
| Original rings / holes | 34 / 0 | 2,132 / 836 |
| Original supplied vertices, including closure | 9,165 | 207,665 |
| Features with valid original NTS topology | 34 | 1,064 |
| Admitted components | 34 | 1,213 |
| Admitted rings / holes | 34 / 0 | 1,675 / 462 |
| Admitted supplied vertices | 9,165 | 159,601 |

The **31 original NTS-invalid features** are reported, not repaired or silently
discarded. They are excluded only from the common reference population. All 62
Core fill observations on these originals (two rules each) returned finite areas
without exceptions; this is not an accuracy comparison with invalid NTS inputs.

There are 37,230 source pairs per direction. The comparable population contains
**36,176 pairs**, including **2,426 original-bounds candidates**. No pair was
excluded because its numerical result disagreed. Source planning categories may
overlap, so pairwise coverage must not be summed as a union of current parks.
Data are attributed to the City of Helsinki under **CC BY 4.0**, separately from
the repository's MIT code license; see [source attribution](data/README.md).

## Numerical checks

Both fill rules and both traversal directions were checked. The fixed budgets,
declared before outputs, are **1 m² for areas** and **1e-8 for fractions**.
NTS 2.6.0 `OverlayNGRobust` is the floating-point reference. Clipper2 C# 2.0.0
uses a fixed 1e-6 m grid, reusable inputs and separately executed intersection,
union and XOR operations. Neither comparator is an exact oracle.

- 1,090,560 numerical assertions across the three backends.
- **363,520 Core assertions**, with no failure against NTS.
- 144,704 actual Core selected-intersection API calls, including disjoint bounds.
- 29,880 full comparisons across backends/rules/directions: all 2,426 candidates
  plus the fixed first 64 disjoint pairs in lexicographic source-ID order.
- Core and NTS each found 1,206 positive and 34,970 zero selected intersections
  per direction and rule. Clipper found 1,205 positive and 34,971 zero results.
- Quantization changed no outer bounds-candidate memberships. The maximum
  observed coordinate displacement was 5.0012e-7 m, including binary64 rounding.

These are the largest observed **Core versus NTS** differences across both rules
and directions. Pair IDs are `district/planning-area`; own-area IDs are features.

| Metric | Maximum absolute difference | Worst input |
|---|---:|---|
| Own area | 2.5332e-7 m² | planning area `5` |
| Selected / full intersection | 1.5087e-6 m² | `112826/6` |
| Union | 2.7716e-6 m² | `112826/5` |
| Symmetric difference | 3.7402e-6 m² | `112826/5` |
| IoU | 7.8298e-14 | `112828/651` |
| Coverage of the first operand | 6.3956e-12 | `112831/813` |
| Selected versus full intersection within Core | 0 | all checked full pairs |

Small floating-point excursions outside [0,1] are retained without clamping and
checked against the declared rounding budget. The table is observed agreement
on this population; it is not a bound for arbitrary coordinates or topology.

## Why the three-way gate failed

The first run at `d75c2fc` reported 43 failures. Investigation separated
**area-sum cancellation** from **grid-induced differences**. We corrected the
assessment adapter's signed area sum, rather than counting a weak comparator
implementation as an advantage for Core.

At planning area `788`, Clipper input and output have the same exact twice-area
of 2,996,748 grid². Their area is 1.498374e-6 m² and coverage is exactly 1 on
the grid. Ordinary `Clipper.Area` summation returned different input/output
areas, producing coverage 0.9999893218293842. Similar cancellation occurred for
`848`, `994` and `995`. The corrected adapter uses checked **Int128** signed
shoelace sums with a full **BigInteger** restart on overflow, preserves hole
signs and converts the final total to double once. It changes neither Clipper's
clipping engine nor the input grid. Thirty analytical integer-area regressions
cover translation, tiny determinants, holes, cancellation and actual overflow.

After the correction, **27 failures remain**:

1. **26 coverage differences on 13 pairs**, counted once per fill rule, exceed
   1e-8 in the planning-area-to-district direction. The maximum is
   **2.9049912253e-8** at `112828/1065`: Clipper gives 0.6859554835712601, NTS on
   original coordinates gives 0.6859555126211724. All area differences stay below
   the 1 m² budget; the largest Clipper/NTS union difference is 0.0077088 m².
2. **Planning area `78` becomes topologically invalid after quantization.** It
   remains in the admitted original population and the failure is retained.
   Independent exact orientation confirms an incident-ray order reversal at a
   shared point, so this is not merely an NTS validity false alarm.

For that local orientation, the supplied points are:

```text
S = (380484.63456713, 6679939.83687981)
A = (380497.60472582, 6679973.73918080)
B = (380497.58528927, 6679973.68837607)
```

The exact determinant `cross(A-S, B-S)` of the original binary64 values is
negative, approximately **-1.6352702677e-6 m²**. After grid conversion it is
exactly **+10,096,542 grid²**, or **+1.0096542e-5 m²**. This illustrates that even
a fine grid can change near-degenerate topology. It does not establish a
Clipper engine defect. Grid choice depends on the application's coordinate
and error contract.

Inputs, grid, budgets and exclusions were **not changed to obtain a pass**.
The original CLI full check still exits 1, retains every failure and reports
`Passed=false`. **Zero benchmark processes were run.** The planned 18-cell,
three-process timing matrix is not a result, and the reducer refuses to summarize
timings without a successful full check bound to the same package consumer.

## Reproduction and continuous checks

Follow [README.md](README.md) for the offline freezer, fresh NuGet consumer and
full `check` command. On this corpus the full check is expected to retain the
comparator failures and block the subsequent timing commands.

`verify-corpus.ps1` runs that same full check and separately validates **Core/NTS
agreement**, binary/source/input identities and complete call inventories. Its
success never changes the original three-way status or permits timings. CI runs
this narrower check on Windows and Linux and retains the original report and
failures as artifacts. `check-verify-corpus.ps1` tests missing evidence, changed
identities, omitted calls and unexpected failures; it does not recompute geometry.

Recorded local environment: **.NET 10.0.12**, Release, Windows x64 build 26300,
Intel Core i9-9900K, 8 cores / 16 logical processors. There are no timing windows,
allocation measurements or process medians to report. The consumer passed the
existing 36 analytical backend checks plus 30 integer-area regressions.

| Retained evidence | SHA256 |
|---|---|
| Corrected full check | `4f5333b61344099ff00a38a6dfab51e7b21ccb440113c0a323051302f06bfbb1` |
| Corrected outside-consumer proof | `7f64640404115748f48b1651cf3d928df9ac0fe6244216b345a1e1f931f97e82` |
| Initial uncorrected full check | `c7deabe26447010b06c3ea9769d496b055419be3ee89d3ac133c6dd06ea2e39d` |
| Frozen benchmark protocol | `0d684e4fbeaa4b39ed120022627c5ccda99369a93dc0a9c629e776caf5f1112a` |
| Frozen source manifest | `f182a14e2640e5a73eecdfa18072bf9bf9c42ab827b1d27b692030145a0a4d14` |

Bulk local outputs remain under ignored `artifacts/`; the public compact
assessment preserves their numerical findings and identities. The loaded Core
net10.0 DLL SHA256 is
`bf7646066a73a9229b40a5bcc8fbfd541ccc6b7d92be8ebacf2e940354c17aca`.
The runner and comparator identities are in [ASSESSMENT.json](ASSESSMENT.json).

## Practical conclusion

The published role-aware factories and area calls worked on a substantially
different, hole-rich corpus without a Core change. That supports trying the
package for repeated numerical region evaluation when output contours are not
needed. It does **not** prove a performance advantage here, exact correctness,
general GIS validity repair, or production adoption by someone else.
The most useful next evidence is a developer's own workload and coordinate/error
contract, rather than tuning this implementation or grid to the observed cases.
