# Guarded double intersection sweep experiment

Declared before timings. This experiment implements the bounded next step in
[DOUBLE-SCANLINE.md](DOUBLE-SCANLINE.md), outside the shipping library.

## Algorithm and numerical contract

Keep the original binary64 coordinates. Sort endpoint events once and maintain
the active edge order through endpoint insertions/removals and crossings. The
first version still orders a copy of the active edges at each endpoint band to
enumerate inversions; it is not a neighbor-event priority queue. It must not
rebuild the active set by scanning all source edges at every level.

Use outward-rounded intervals to certify ordering and area values. Endpoint
identities may be handled exactly; uncertain contacts, overlapping event
intervals, unsupported inputs and exhausted work budgets cause whole-operation
fallback to `WindingArea.IntersectionArea`. Discard provisional area on fallback.
No epsilon comparator, silent grid conversion, or clamped invalid result is
permitted. Diagnostics include the fallback reason and work already incurred.

Initial fast-path limits are 8192 total supplied vertices, coordinate magnitude
<=1e100, 65536 constructed crossings per endpoint band, and 2000000 charged
status/comparison work units per call. Other inputs fall back to the existing
operation; these limits do not redefine the public input contract. Scratch
storage is retained by a mutable, non-thread-safe, non-reentrant instance.
Expected uncertainty currently unwinds via a private exception; that allocation
and unwind cost, when incurred, belong to the reported fallback overhead.

A certified result has an enclosing area interval. Its returned error radius
must be at most **min(0.25, 1e-10 * abs(result))**, in squared input units. Zero is
accepted only when the enclosure is exactly [0,0]. There is no near-zero absolute
error floor. The 0.25 cap supports the existing 1 m² GIS check; this experimental
unit-dependent limit is not a new public-library guarantee. Fallback results
retain Winding's existing numerical contract and have no new interval guarantee.

Analytic and exact-rational checks must exercise actual fast successes, both fill
rules, swaps/reversals, shared boundaries, coincident/multiway events, tiny areas,
large offsets, extreme exponents, repeated vertices, mutation protection and
input rejection parity. Passing by falling back on every example is insufficient.

## Frozen real inputs and accuracy

Use all 98 county and 11 district rings already frozen in
`examples/RegionCoverage/data/{zones,queries}.json`: 109 rings, 10588 supplied
vertices, original EPSG:5070 binary64 metres. Use the previously recorded source
hashes. No rounding, translation, repair, simplification, removed failing pair,
or new download is performed by the harness. Parsing and polygon admission are
outside timing; all per-backend geometry preparation remains charged as below.

Validate all source polygons with NTS `IsValid` and positive area. Evaluate all
1078 county/district pairs in both directions. Use NonZero for the real workload;
these valid simple rings define the same filled set under EvenOdd. Compare all
three methods against original-input NTS OverlayNG: intersection error <=1 m²,
coverage error <=1e-8 absolute fraction. NTS is an independent implementation,
not an exact-area oracle. Also report differences against current Winding.

Methods: current Winding intersection, guarded double sweep including fallback,
and direct reusable-data Clipper64 via the existing RegionCoverage adapter at
scale 1e6. Clipper retains its quantized input/output contract; its error is
measured against the original geometry. All adapters have the same outer
inclusive AABB rejection; assert the frozen candidate masks agree. No indexed
outer search, cached pair result or pair selection is introduced.

Outside timings, record every directional pair's reference/results, candidate
status, fast/fallback decision and reason, error radius when certified, bands,
crossings, active-edge visits and maximum active count. Report fallback fraction
among geometry candidates, separately from AABB rejections and certified zeros.

## Measurement and decision

Three methods, two directions and three scopes give 18 rows per process:

1. `prepare`: all adapter/catalogue construction, bounds, own areas, workspace
   objects and backend-specific input data. Source arrays are borrowed unchanged.
2. `warm-table`: one full table against prepared catalogues; every geometry call
   performs its ordinary preparation, including any failed sweep before fallback.
3. `prepare-plus-one`: fresh preparation plus one full table, in a warm process.

Reuse the established calibrated timing engine: three fresh sequential processes,
five batches per row, >=20ms calibration, rotated method order, Release,
DOTNET_TieredCompilation=0. No simultaneous build or benchmark. This gives 270
samples. Record raw timings, per-thread managed bytes, revision, source and binary
hashes, runtime, CPU, OS and UTC. These allocations are not retained/peak memory.
Record diagnostics in untimed validation; do not add dictionary bookkeeping to
the timed adapter. Consume intersection and directional coverage in each table.

For each direction, in both warm-table and prepare-plus-one, require guarded
time <= **0.8 * Clipper time** and <= **0.9 * current Winding time**, using the
median of three process medians. All four cells and all correctness gates must
pass. Record every failed cell and process range. A passing gate supports further
development, not a universal speed claim or automatic public API integration.
Do not tune thresholds, input selection or numerical bounds after timings.

## Reproduction

From a clean measured revision, restore with `--locked-mode` and build Release.
Run `check-double <directory>`. Set `DOTNET_TieredCompilation=0`, then run
`benchmark-double <directory> <run> <revision>` for runs 1, 2 and 3 in sequential
processes. Finally use `summarize-double <directory>` with the measured binaries.
The DLL is `benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll`.
Keep generated files under ignored `artifacts/`. CI runs checks, never timings.
