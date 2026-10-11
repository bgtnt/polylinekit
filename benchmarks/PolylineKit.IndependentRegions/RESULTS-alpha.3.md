# Published alpha.3 on the frozen Helsinki corpus

**The actual NuGet.org alpha.3 passed Core/NTS agreement under the unchanged
budgets. The three-way gate still failed with 27 comparator failures, so no
timings were run.** This closes the release check for alpha.3; it is a repeat
on the same independent corpus, not a new dataset or a performance result.

The clean assessment runner is
[`cbcc3113de85fa7c7136585ab29768a059483347`](https://github.com/bgtnt/polylinekit/commit/cbcc3113de85fa7c7136585ab29768a059483347).
The delivered `PolylineKit.Core/0.1.0-alpha.3` is from
[`23c0b3280424569356d64362266c519c839b4790`](https://github.com/bgtnt/polylinekit/commit/23c0b3280424569356d64362266c519c839b4790).
It was restored from NuGet.org into an initially empty cache outside the checkout;
no library ProjectReference, local build, private DLL or additional package feed
was used. The loaded .NET 10 DLL, signed package and content hashes match the
published release identity. All changed runner inputs were committed before
building the consumer.

## What was checked

The [replay supplement](REPLAY-alpha.3.md) changes only the selected Core release.
The [original protocol](PROTOCOL.md), data, comparators, grid, exclusions and
budgets remain frozen. The [alpha.2 results](RESULTS.md) and
[assessment](ASSESSMENT.json) remain unchanged.

- 34 districts and 1,095 planning-area features, including 836 original holes.
- The same 31 NTS-invalid originals are explicitly excluded from the common
  comparison, leaving 34 × 1,064 features and 36,176 pairs per direction.
- Both fill rules and traversal directions; 1,090,560 numerical assertions
  across Core, NTS 2.6.0 and Clipper2 C# 2.0.0.
- **363,520 Core assertions**, with no failures against the NTS reference.
- 144,704 actual Core selected-intersection calls and 29,880 full comparisons
  across the three backends, rules and directions.
- The fresh consumer passed 36 analytical backend assertions and 30 integer-area
  regressions. All 36 wrapper evidence controls passed.

The largest observed Core/NTS differences match the alpha.2 record:

| Metric | Maximum absolute difference |
|---|---:|
| Own area | 2.5332e-7 m² |
| Selected / full intersection | 1.5087e-6 m² |
| Union | 2.7716e-6 m² |
| Symmetric difference | 3.7402e-6 m² |
| IoU | 7.8298e-14 |
| First-operand coverage | 6.3956e-12 |
| Selected versus full intersection within Core | 0 |

These are observed differences on these inputs, not absolute accuracy bounds or
proof of equivalence on arbitrary geometry. The fixed acceptance budgets remain
1 m² for areas and 1e-8 for fractions.

## Retained comparator limits

The original full check exits **1**, with **26 coverage differences** on the
fixed Clipper grid and **one quantized topology change** at planning area `78`.
No numerical disagreement was removed from the population. The narrower
Core/NTS wrapper succeeds without changing the failed three-way status or
allowing timing. The [alpha.2 explanation](RESULTS.md#why-the-three-way-gate-failed)
still applies; the replay does not establish a Clipper engine defect.

## Evidence and reproduction

[ASSESSMENT-alpha.3.json](ASSESSMENT-alpha.3.json) preserves the package and
runner identities, complete exclusion/failure lists, metric maxima and worst
inputs, source hashes and hashes of the retained full reports. Bulk output stays
under ignored `artifacts/`. Reproduce this release repeat with the commands in
[REPLAY-alpha.3.md](REPLAY-alpha.3.md); the default historical commands and CI
continue to select alpha.2.

Environment: Release, .NET 10.0.12, Windows x64 build 26300, Intel Core i9-9900K,
16 logical processors. **Zero timing processes** were run. This validates the
published alpha.3, including its polygon-factory optimization, on the fixed
Helsinki inputs. It does not measure the optimization's speed or demonstrate
adoption by an independent developer.
