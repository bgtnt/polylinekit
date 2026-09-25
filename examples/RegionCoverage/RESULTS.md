# Region coverage: measured result

The experiment supports an **allocation-sensitive intersection/coverage use case** for
Winding, not a claim that it is the universally fastest geometry engine. Direct
reusable Clipper64 is a close throughput competitor on this population. NTS
OverlayNG is a materially stronger baseline than its legacy overlay. Prepared
predicates help in one operand direction and cost more in the other.

Measured source [`60e3d3b`](https://github.com/bgtnt/polylinekit/commit/60e3d3b042f654759422d67ffb90b35a772eb77f).
Windows build 26200, Intel Core i9-9900K, SDK 10.0.401 / .NET 10.0.12, x64,
`DOTNET_TieredCompilation=0`. Three fresh sequential processes, five calibrated
batches per row: **3,780 timing samples**. Adapter order rotates. Medians below
are medians of process medians; ranges and all 252 rows are in the
[machine-readable evidence](../../benchmarks/coverage-evidence.json). No CPU
affinity/frequency lock or statistical significance claim.

The frozen population has 98 complete county rings and 11 complete district
rings, 10,588 vertices, 1,078 pairs in each direction, 211 bounding-box
candidates, 12 nested pairs and **zero convex-eligible candidates**. All admitted
rings passed validation. See the [protocol](data/PROTOCOL.md) for the five
whole-feature exclusions and the common EPSG:5070 coordinate translation.

## Prepared full-population traversal

NTS STRtree is the common outer index in this table. Values are milliseconds
for the complete pair population; all 211 candidate results are computed and
consumed as intersection/coverage digests, without constructing an output table.

| Method | County zones, ms | District zones, ms | Managed bytes, county / district |
|---|---:|---:|---:|
| Winding | 13.410 | 13.185 | 0 / 0 |
| Clipper64-reused-data | 12.585 | 12.366 | 625,088 / 625,088 |
| NTS-legacy | 68.278 | 68.665 | 39,159,320 / 39,159,320 |
| NTS-OverlayNG | 35.164 | 34.520 | 26,647,184 / 26,647,184 |
| NTS-prepared-OverlayNG | 43.666 | 28.701 | 29,361,968 / 19,228,144 |
| convex-then-winding | 13.507 | 13.121 | 0 / 0 |
| wpf-evenodd-area | 25.394 | 25.189 | 3,999,800 / 3,999,800 |
| wpf-nonzero-area | 25.377 | 24.840 | 3,999,800 / 3,999,800 |
| wpf-combine-explicit | 24.156 | 23.466 | 1,535,104 / 1,535,104 |

Winding takes about **6.6% more time than Clipper64** in both directions here,
while avoiding Clipper's roughly 625 kB of managed allocation per traversal.
That is the concrete tradeoff; this population does not show a throughput win
over the direct reusable Clipper baseline.

Winding's zero is **additional managed allocation in a warmed call**. Its
thread-local workspace retains memory; first use, growth and nested calls are
not allocation-free. WPF additionally allocates native memory that this counter
does not measure. The convex row always falls back to Winding here.

## Preparation and reuse

Milliseconds including preparation of both catalogues and the selected STRtree.
Eight-traversal sessions amortize one preparation; their figures are per traversal.
Loading, common translation and independent validity certification are outside
all timers. JIT and the Winding thread workspace have already been warmed by
correctness checks; this is a fresh prepared session, not a cold-process start.

| Method | County: prepare + one | County: prepare + eight, each | District: prepare + one | District: prepare + eight, each |
|---|---:|---:|---:|---:|
| Winding | 13.598 | 13.295 | 13.288 | 13.094 |
| Clipper64-reused-data | 12.893 | 12.354 | 13.062 | 12.232 |
| NTS-legacy | 69.876 | 69.081 | 69.328 | 68.316 |
| NTS-OverlayNG | 35.508 | 34.151 | 35.026 | 34.228 |
| NTS-prepared-OverlayNG | 47.956 | 44.336 | 30.940 | 28.324 |
| convex-then-winding | 13.678 | 13.413 | 13.248 | 12.975 |
| wpf-evenodd-area | 29.282 | 25.599 | 28.712 | 25.585 |
| wpf-nonzero-area | 29.234 | 25.593 | 29.304 | 25.339 |
| wpf-combine-explicit | 26.782 | 24.185 | 26.584 | 23.573 |

The raw JSON also preserves preparation-only, candidate-geometry and
first-traversal-after-preparation records. First-use records use STRtree only;
lazy prepared-object indexes are charged there and in fresh sessions.

## Outer R-tree experiment

The packed32 prototype applies RtTools design ideas without copying its
unpublished code: contiguous storage, reusable query buffers and bulk emission
of fully covered subtrees. NTS HPRtree supplies the published Hilbert-packed
alternative found during the web review. Every candidate ID was checked
against a linear scan, including touching and zero-extent bounds.

| Winding traversal | County zones, ms (range) | District zones, ms (range) |
|---|---:|---:|
| Linear boxes | 13.300 (13.265–13.581) | 13.098 (13.019–13.154) |
| STRtree10 | 13.410 (13.283–13.411) | 13.185 (13.119–13.232) |
| Packed32 | 13.474 (13.429–13.890) | 13.123 (13.030–13.178) |
| HPRtree16 | 13.359 (13.304–13.406) | 13.127 (12.950–13.153) |

These measurements include geometry, not just index traversal. There are
only 11 or 98 indexed rectangles; different indexes also change candidate
order. Small differences do not establish an isolated or universal R-tree
ranking. Preparation includes the selected index only, and all alternative
fresh-session costs remain in the evidence. This does not test a segment
index inside Winding or establish that replacing its sweep would be faster.

## Numerical agreement and WPF limitations

The predeclared gate requires both intersection disagreement ≤1 m² and absolute
coverage disagreement ≤1e-8 against NTS legacy for **every** pair. NTS is an
independent comparison, not an exact oracle. No failed pair is omitted and no
negative WPF value is clamped.

| Method | Max area disagreement, m² | Max coverage disagreement | Failed pairs county / district |
|---|---:|---:|---:|
| Winding | 1.4305115e-06 | 1.4432899e-15 | 0 / 0 |
| Clipper64-reused-data | 0.026165724 | 2.2365776e-11 | 0 / 0 |
| NTS-legacy | 0 | 0 | 0 / 0 |
| NTS-OverlayNG | 1.9073486e-06 | 1.6653345e-15 | 0 / 0 |
| NTS-prepared-OverlayNG | 1.9073486e-06 | 1.6653345e-15 | 0 / 0 |
| convex-then-winding | 1.4305115e-06 | 1.4432899e-15 | 0 / 0 |
| wpf-evenodd-area | 304.95096 | 3.7289303e-07 | 204 / 202 |
| wpf-nonzero-area | 321.93985 | 4.0613353e-07 | 205 / 205 |
| wpf-combine-explicit | 303.95204 | 3.0667416e-07 | 142 / 141 |

Each failure count is out of 1,078 pairs, of which 211 are bbox candidates.
Both WPF area-subtraction variants produce 35 negative intersections per
direction (the same geometric pairs); explicit Combine produces none. The
worst negative estimate is about −322 m². WPF's maximum coverage disagreement
is nevertheless only about 4.1e-7 (0.000041 percentage points) on this large-area
population; a coarser application budget could accept it. That does not fix
tiny-overlap loss or meet this experiment's stricter contract. Reducing WPF
flattening tolerance does not remove the native scanner's coordinate grid.

## Decision

- Keep intersection/coverage as a concrete consumer example for the existing
  Winding API. The demonstrated benefit is low warmed allocation with competitive
  throughput and no caller-selected decimal grid, under the documented numerical limits.
- Use direct Clipper64 and current NTS OverlayNG as primary performance baselines;
  comparing only with legacy NTS would overstate the practical benefit.
- Keep WPF as a documented graphics-engine comparison, not an equal-accuracy
  speed result. ArcGIS/QGIS would need separate whole-workflow tests with
  equivalent projected-area, grouping and output-storage contracts.
- Do not replace the area engine or choose a general R-tree from these small
  outer-index measurements. A subsequent optimization experiment should isolate
  prepared segment searches on larger, varied contours and preserve exact
  crossing/tie semantics. Static packed/Hilbert layouts are candidates, not proven winners.
- Successive simplification is a distinct **dynamic** workload. RtTools' real
  delete/condense/reinsert machinery is a valid local baseline there; this static
  experiment does not rank it. Compare complete proposal/update sequences against
  a focused dynamic bounds tree or a packed-index/delta/rebuild strategy, preserving
  edge identities, old bounds and contact semantics. See the
  [dynamic-index discussion](COMPETITORS.md#dynamic-simplification).

This bounded, related Census population does not represent arbitrary polygons,
holes, multipart GIS features, dynamic indexes, or surveyed area accuracy.
No new runtime dependency, public API, package publication or recognition work
was added to the library. [Commands and contracts](README.md) reproduce the
experiment; the evidence manifest records raw-file and binary identities.
