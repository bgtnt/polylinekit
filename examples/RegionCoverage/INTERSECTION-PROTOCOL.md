# Intersection-only coverage experiment

Declared on **2026-09-25 before any timings of this ablation**. This tests one
specific hypothesis: returning only intersection area can remove enough unused
area-accumulation work to provide a material advantage over the existing direct
Clipper intersection baseline. An unrelated index improvement or parity with
Clipper is not success under this protocol.

The data and earlier coverage results are already known. This is a local
engineering experiment on an observed workload, **not a held-out evaluation**,
a statistical proof, or evidence of general market value. Freeze implementation,
protocol and source revision before timing; preserve unfavorable results.

## Fixed operation and methods

Every request returns intersection area and coverage of its zone:
`I = area(zone intersect query)` and `coverage = I / area(zone)`.
The denominator is prepared once using the existing adapter contract. All
methods consume both quantities; none caches pair results.

Measure exactly these three adapters:

| Method | Purpose |
|---|---|
| `Winding` | Unchanged `FilledRegions(...).IntersectionArea`, computing its full area result internally; the within-build ablation baseline. |
| `Winding-intersection-only` | The new single-output operation, preserving the existing input, fill-rule and numerical contracts. Preparation is identical to `Winding`. |
| `Clipper64-reused-data` | Existing direct intersection with cached integer/local-minimum data, a reused engine, and output-contour area summation. |

Clipper keeps the existing `10^-6` metre coordinate grid, quantized bounds and
denominator, `Clear`/`AddReuseableData` behavior and one intersection operation.
Winding keeps the supplied translated binary64 coordinates. Do not add output
conversions to Clipper, change precision, weaken a check, or substitute the less
competitive wrapper. The old full-metric Winding operation remains available
and unchanged. This experiment does not include new prepared-edge caches,
simple-input assumptions, alternative outer indexes, SIMD tuning or C++ code.

## Frozen population and accuracy

Use the complete existing [dataset](data/README.md) and its
[admission protocol](data/PROTOCOL.md): **98 county rings and 11 congressional
district rings**, with source hashes verified. Preserve coordinates and source
selection. Subtract the same combined-bounds center as the existing runner;
do not normalize or translate contours independently.

Run both directions: county zones against district queries, and district zones
against county queries. Each direction contains **1,078 pairs**, **211 inclusive
bounding-box candidates** and **867 bounding-box rejects**. Keep contacts,
containment, shared boundaries and every difficult candidate. Initial geometry
validation failure aborts; do not repair or remove inputs after seeing results.

Retain raw numerical results for every pair and all three methods in both
directions, including zero pairs. Each result must meet both existing agreement
budgets against the NTS reference: absolute intersection-area disagreement at
most **1 square metre** and absolute coverage disagreement at most **1e-8**.
NTS agreement is a cross-implementation check, not an exact arithmetic oracle.
Report old/new Winding differences as well; keep all analytical and numerical
regression tolerances unchanged. Passing this dataset cannot excuse a failure
of the general operation's self-intersection, fill-rule, overlap, extreme-scale,
input-validation or reentrancy contracts. Do not clamp invalid values.

## Six measurement scopes

Use only the existing outer **STRtree with default fanout 10**. Build the same
catalogues and selected index for every method. No HPRtree or custom packed-index
rows participate in this ablation.

| Scope | Timed work and unit |
|---|---|
| `prepare-catalogues-and-index` | Both prepared input catalogues, own areas, adapter state and the STRtree. |
| `warm-candidate-geometry` | The entire 211-candidate batch; candidate-list preparation is outside this microbenchmark's timer. |
| `warm-linear-table` | One complete pair population, with common bounds rejection and geometry calls for candidates. |
| `warm-indexed-table` | One complete population traversal using the STRtree, including candidate retrieval and geometry. |
| `prepare-plus-one-table` | Fresh preparation plus one indexed table. |
| `prepare-plus-eight-tables` | Fresh preparation plus eight indexed tables; time and bytes divided by eight. |

A table consumes both area and coverage in the existing digest, without
allocating an output matrix. The two warm traversal scopes do not prepare an
eager pair list. No area result is reused between tables. Loading, source/hash
checks, initial geometry validation, accuracy evaluation and report generation
are outside timers. Preparation is included explicitly in fresh-session rows.
Correctness checks run before measurement, so JIT and Winding's thread-local
workspace are warm; fresh sessions are **not cold-process measurements**.
Retain the existing separate first-table-after-preparation records.

Run **three fresh processes sequentially**, with no concurrent builds or other
benchmark jobs, `DOTNET_TieredCompilation=0`, and the same Release binaries.
Rotate method order across processes. Each row has **five calibrated batches**;
read allocation counters before creating sample records. The fixed matrix is
**2 directions x 3 methods x 6 scopes = 36 rows per process**, hence **540 timed
batch samples** across three processes. First-use records are additional
observations, not part of that sample count.

Report the median of the three process medians and their minimum/maximum for
time; preserve every raw sample and allocation measurement. Managed allocation
is not retained, peak, native or process-total memory. Record source revision,
input and assembly hashes, runtime, OS, architecture, CPU, environment settings,
UTC run times and exact commands. Summarization must regenerate input/results,
verify the complete matrix and identities, and recalculate sample medians.

## Predeclared decision gate

For each direction and each of these two scopes:

- `warm-indexed-table`;
- `prepare-plus-one-table`;

compute `Clipper64-reused-data time / Winding-intersection-only time`, using
the median-of-process-medians time defined above. **All four ratios must be at
least 1.25**, equivalent to the new operation taking at least **20% less time**
than Clipper, and all correctness requirements must pass. Report the four ratios
individually. Do not replace a failing cell with an aggregate, a favorable
direction, candidate-only timing, or the eight-table amortization row.

This is a deliberately local engineering acceptance threshold, not a significance
test or a universal performance promise. Show process ranges even when the gate
passes. Improvement over old Winding helps attribute the change but does not
replace the Clipper gate. If any required ratio or correctness check fails,
record that this candidate did not establish the requested advantage on this
protocol; a changed implementation or prepared-geometry design requires a
separately identified experiment, without rewriting these results.

## Commands

From the repository root, after freezing a clean measured revision:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build examples/RegionCoverage -c Release --no-restore
$runner = 'examples/RegionCoverage/bin/Release/net10.0/RegionCoverage.dll'
dotnet $runner check
$env:DOTNET_TieredCompilation = '0'
$revision = git rev-parse HEAD
foreach ($run in 1..3) {
    dotnet $runner benchmark-intersection artifacts/coverage-intersection $run $revision
    if ($LASTEXITCODE) { throw "Intersection benchmark failed: $run" }
}
dotnet $runner summarize-intersection artifacts/coverage-intersection
```

The dedicated commands select exactly the three methods and six STR scopes
above. Keep the previous coverage evidence intact and use a separate output
directory for this ablation.
