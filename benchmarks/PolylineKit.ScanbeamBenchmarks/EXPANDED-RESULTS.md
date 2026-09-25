# Scanbeam on real contours and wider coordinates

Measured source: [`9a947c769e3859a468fdfe90283ccb543c3abc70`](https://github.com/bgtnt/polylinekit/commit/9a947c769e3859a468fdfe90283ccb543c3abc70).

The integer prototype remains experimental. The metre-grid real-contour experiment
does not satisfy the original binary64 input contract. Its performance and the
separate wider-coordinate controls are reported below without pooling gates.
It is about **6.1 times slower than Clipper** on warm real-contour tables, while
the existing Winding intersection method is faster than both in these measurements.
On the wider-coordinate grids it wins the four EvenOdd gates and loses all four
NonZero gates. Neither experiment passes its full integration/performance gate.

## Correctness and input transformation

All 109 original and rounded Census rings are valid polygons. They contain 10,588
vertices; all vertices and six collapsed consecutive edges were retained. Each
world coordinate is rounded to whole EPSG:5070 metres before one common integer
translation (1423255, 1517079). No repair, selection or filtering occurs.

The initial validator mistakenly combined NTS `IsValid` and `IsSimple`. Macon
County (37113), whose vertices 18 and 19 round to (1129830,1400586), is valid
but fails the latter predicate because of that zero-length edge. This predicate
was corrected and documented before timings. The input was unchanged; a regression
pins this distinction, while a true bowtie and a zero-area ring still fail admission.

All 2156 directional pairs (1078 each way; 211 AABB candidates each)
pass the same-rounded-input backend budgets: 1 m² intersection and 1e-8 absolute
coverage fraction versus NTS. NTS is an independent implementation, not an exact oracle.

| Comparison | Maximum absolute difference |
|---|---:|
| Winding intersection vs rounded NTS, m² | 1.07288360596e-06 |
| Scanbeam intersection vs rounded NTS, m² | 9.53674316406e-07 |
| Clipper intersection vs rounded NTS, m² | 0.0121039152145 |
| Original vs rounded own area, m² | 31603.9680214 |
| Original vs rounded intersection, m² | 15681.3967903 |
| Original vs rounded coverage fraction | 2.90619172312e-05 |
| Original vs rounded vertex displacement, m | 0.701275452161 |

The transformation fails the original-input budgets in **312 of 2156**
directional comparisons. Every failing pair and its deltas is retained in the
[public evidence](../scanbeam-expanded-evidence.json). The small same-input backend
errors do not compensate for these input changes.

## Real-contour measurements

Speed gate: **FAIL**. Original-input equivalence:
**FAIL**. Integration gate: **FAIL**.

One operation is a full population traversal, or preparation as labelled. Times
are medians of three process medians; ranges are the process minimum–maximum.
Allocations are warm managed bytes per operation, not retained or peak memory.

| Direction | Method | Scope | ms | Range ms | B/op |
|---|---|---|---:|---:|---:|
| county-zones | Clipper64-reused-data | prepare | 0.5298 | 0.5239–0.5713 | 1158016 |
| county-zones | Clipper64-reused-data | prepare-plus-one | 12.9592 | 12.9292–13.3978 | 1844320 |
| county-zones | Clipper64-reused-data | warm-table | 12.5776 | 12.5302–12.7128 | 650784 |
| county-zones | IntegerScanbeam-intersection | prepare | 0.2337 | 0.2328–0.2355 | 200392 |
| county-zones | IntegerScanbeam-intersection | prepare-plus-one | 76.3296 | 76.2172–76.6194 | 477704 |
| county-zones | IntegerScanbeam-intersection | warm-table | 76.1716 | 76.0642–76.3430 | 0 |
| county-zones | Winding-intersection-only | prepare | 0.2334 | 0.2331–0.2349 | 200008 |
| county-zones | Winding-intersection-only | prepare-plus-one | 10.5272 | 10.5208–10.5517 | 199944 |
| county-zones | Winding-intersection-only | warm-table | 10.2751 | 10.2731–10.2885 | 0 |
| district-zones | Clipper64-reused-data | prepare | 0.5270 | 0.5265–0.5301 | 1158016 |
| district-zones | Clipper64-reused-data | prepare-plus-one | 12.8497 | 12.8256–12.8694 | 1841912 |
| district-zones | Clipper64-reused-data | warm-table | 12.5061 | 12.3485–12.5581 | 650560 |
| district-zones | IntegerScanbeam-intersection | prepare | 0.2349 | 0.2329–0.2355 | 200392 |
| district-zones | IntegerScanbeam-intersection | prepare-plus-one | 76.3688 | 76.2638–76.6550 | 468416 |
| district-zones | IntegerScanbeam-intersection | warm-table | 76.2101 | 76.1323–76.5624 | 0 |
| district-zones | Winding-intersection-only | prepare | 0.2349 | 0.2332–0.2349 | 200008 |
| district-zones | Winding-intersection-only | prepare-plus-one | 10.2210 | 10.1967–10.2260 | 199944 |
| district-zones | Winding-intersection-only | warm-table | 9.9672 | 9.9439–9.9734 | 0 |

| Direction | Scope | Clipper / prototype | Pass ≥1.25 |
|---|---|---:|---|
| county-zones | prepare-plus-one | 0.1698 | False |
| county-zones | warm-table | 0.1651 | False |
| district-zones | prepare-plus-one | 0.1683 | False |
| district-zones | warm-table | 0.1641 | False |

## Wider-coordinate measurements

Each frozen grid is scaled exactly by 65536, or separately translated by 400000.
These four inputs force Int128. Translation changes no geometry; a future exact
recentring could eliminate that implementation-specific cost. Metamorphic area
checks are separate from independent exact-oracle controls. All original single-loop
output bit patterns on the seven original inputs were preserved.

Eight-cell speed gate: **FAIL**.

| Input | Fill | Method | us | Range us | B/op |
|---|---|---|---:|---:|---:|
| frozen-grid-256-scaled-65536 | EvenOdd | Clipper64-preloaded-p6 | 9189.050 | 9111.250–9339.900 | 2011416 |
| frozen-grid-256-scaled-65536 | EvenOdd | Clipper64-reused-p6 | 9285.150 | 9188.300–9541.550 | 2011504 |
| frozen-grid-256-scaled-65536 | EvenOdd | IntegerScanbeam | 5811.075 | 5792.700–5815.050 | 0 |
| frozen-grid-256-scaled-65536 | EvenOdd | WindingArea | 6968.225 | 6927.350–7471.000 | 0 |
| frozen-grid-256-scaled-65536 | NonZero | Clipper64-preloaded-p6 | 5055.325 | 5047.150–5179.650 | 352536 |
| frozen-grid-256-scaled-65536 | NonZero | Clipper64-reused-p6 | 5109.375 | 5045.900–5110.900 | 352624 |
| frozen-grid-256-scaled-65536 | NonZero | IntegerScanbeam | 6461.800 | 6457.600–6478.450 | 0 |
| frozen-grid-256-scaled-65536 | NonZero | WindingArea | 6928.875 | 6900.500–7528.875 | 0 |
| frozen-grid-256-translated-400000 | EvenOdd | Clipper64-preloaded-p6 | 9073.500 | 9051.100–9332.000 | 2012560 |
| frozen-grid-256-translated-400000 | EvenOdd | Clipper64-reused-p6 | 9417.150 | 9024.900–9644.650 | 2012648 |
| frozen-grid-256-translated-400000 | EvenOdd | IntegerScanbeam | 5695.475 | 5692.925–5853.375 | 0 |
| frozen-grid-256-translated-400000 | EvenOdd | WindingArea | 6657.950 | 6602.050–7147.350 | 0 |
| frozen-grid-256-translated-400000 | NonZero | Clipper64-preloaded-p6 | 5048.587 | 5037.188–5118.175 | 352184 |
| frozen-grid-256-translated-400000 | NonZero | Clipper64-reused-p6 | 5063.113 | 5062.775–5139.250 | 352272 |
| frozen-grid-256-translated-400000 | NonZero | IntegerScanbeam | 6307.325 | 6267.475–6322.150 | 0 |
| frozen-grid-256-translated-400000 | NonZero | WindingArea | 6638.325 | 6604.875–7083.050 | 0 |
| frozen-grid-64-scaled-65536 | EvenOdd | Clipper64-preloaded-p6 | 440.239 | 438.538–443.998 | 135552 |
| frozen-grid-64-scaled-65536 | EvenOdd | Clipper64-reused-p6 | 444.675 | 444.139–447.603 | 135640 |
| frozen-grid-64-scaled-65536 | EvenOdd | IntegerScanbeam | 326.383 | 325.475–328.950 | 0 |
| frozen-grid-64-scaled-65536 | EvenOdd | WindingArea | 400.973 | 400.969–432.889 | 0 |
| frozen-grid-64-scaled-65536 | NonZero | Clipper64-preloaded-p6 | 285.280 | 284.921–286.104 | 45848 |
| frozen-grid-64-scaled-65536 | NonZero | Clipper64-reused-p6 | 311.590 | 310.555–313.069 | 45936 |
| frozen-grid-64-scaled-65536 | NonZero | IntegerScanbeam | 377.534 | 377.414–377.645 | 0 |
| frozen-grid-64-scaled-65536 | NonZero | WindingArea | 401.698 | 400.247–432.683 | 0 |
| frozen-grid-64-translated-400000 | EvenOdd | Clipper64-preloaded-p6 | 448.481 | 442.261–448.789 | 135136 |
| frozen-grid-64-translated-400000 | EvenOdd | Clipper64-reused-p6 | 445.986 | 445.378–449.567 | 135224 |
| frozen-grid-64-translated-400000 | EvenOdd | IntegerScanbeam | 313.859 | 312.973–314.667 | 0 |
| frozen-grid-64-translated-400000 | EvenOdd | WindingArea | 420.436 | 389.323–447.048 | 0 |
| frozen-grid-64-translated-400000 | NonZero | Clipper64-preloaded-p6 | 286.016 | 285.691–286.471 | 45936 |
| frozen-grid-64-translated-400000 | NonZero | Clipper64-reused-p6 | 287.291 | 286.965–288.202 | 46024 |
| frozen-grid-64-translated-400000 | NonZero | IntegerScanbeam | 362.789 | 362.528–363.536 | 0 |
| frozen-grid-64-translated-400000 | NonZero | WindingArea | 388.066 | 387.384–423.675 | 0 |

| Input | Fill | Clipper / prototype | Pass ≥1.25 |
|---|---|---:|---|
| frozen-grid-256-scaled-65536 | EvenOdd | 1.5813 | True |
| frozen-grid-256-scaled-65536 | NonZero | 0.7823 | False |
| frozen-grid-256-translated-400000 | EvenOdd | 1.5931 | True |
| frozen-grid-256-translated-400000 | NonZero | 0.8004 | False |
| frozen-grid-64-scaled-65536 | EvenOdd | 1.3488 | True |
| frozen-grid-64-scaled-65536 | NonZero | 0.7556 | False |
| frozen-grid-64-translated-400000 | EvenOdd | 1.4210 | True |
| frozen-grid-64-translated-400000 | NonZero | 0.7884 | False |

## Reproduction and decision

.NET 10.0.12; Microsoft Windows 10.0.26200; X64;
Intel64 Family 6 Model 158 Stepping 12, GenuineIntel; 16 logical processors; tiering disabled.
Three fresh sequential processes per experiment, five calibrated batches per row:
270 real-contour and 480 wider-coordinate samples. No build or second benchmark
ran concurrently. Ordinary OS activity was not controlled; ranges are descriptive,
not confidence intervals. All three processes used identical measured binaries.

Build the measured commit in Release with locked restore. See the
[real protocol](REAL-PROTOCOL.md), [wide protocol](WIDE-PROTOCOL.md) and
[runner commands](README.md). Use `benchmark-real`/`summarize-real` for
`artifacts/scanbeam-real`, and `benchmark-wide`/`summarize-wide` for
`artifacts/scanbeam-wide`. The summarizers validate inputs, binary identities,
all rows and batches, medians and freshly recomputed geometric outputs.

Validation: Release solution build; five scalar/SIMD/target verification modes;
4572 scanbeam assertions plus 56 original and 32 wide method/fill checks;
109 real polygon admission/own-area checks and all 2156 directional comparisons.
The public manifest records revisions, binaries, raw-file hashes, every measured
aggregate and every failed original-equivalence pair.

Ten deliberately corrupted evidence controls (missing rows, altered medians,
outputs, binary identities and validation records) were rejected without changing
the recorded files. An earlier timing attempt at `d0fbfed` was rejected by the
summarizer: the read-only Point2 origin did not deserialize correctly. Commit
`9a947c7` replaced that metadata field with a constructor-bound record, then all
timings were rerun. No geometry or timing algorithm changed in that correction.
The rejected attempt is retained locally in
`artifacts/scanbeam-real-d0fbfed-summary-rejected`; it is not used in the tables.

Local audit archive: `artifacts/scanbeam-expanded-9a947c7.zip` (65710 bytes),
SHA-256 `804c7c44c6bbec19c35f03ee95f77cd2979a215fdb983a619955cd86abbeb39d`. Its members were verified against their source files.
This archive is local, not a public download; frozen inputs and reproduction code
are public. Raw generated runs stay outside the source tree.

Do not integrate this integer prototype as a replacement for the binary64 API.
Its small-grid gain remains tied to the historical measured commit and workload.
The next architectural experiment is [an incremental double-input scanline with
certified decisions and whole-call fallback](DOUBLE-SCANLINE.md), using the original
unquantized contours. That proposal is not an implemented backend or a speed claim.
