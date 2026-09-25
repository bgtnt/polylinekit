# Coalescing filled sweep gaps

Declared before timing. The preceding interval-arithmetic experiment reaches
about 19 ms per warm table but remains slower than Winding and Clipper. This
experiment reduces the number of geometric area integrations by combining
adjacent filled intervals between the same two immutable edges.

## Bounded architecture and numerical contract

Keep endpoint handling, status ordering, crossing construction, winding prefixes
and per-band gap starts. After the existing filled/zero-height checks, emit a
term with ordered left/right edge IDs and its original start/finish Levels.
Keep at most one pending term per left edge. Extend it only when the ordered
pair is unchanged and its finish Level exactly matches the new start Level,
including binary64 bound bits and crossing provenance. Otherwise integrate the
old term and replace it. Integrate remaining terms before certifying the result.

Additionally require point-valued Y enclosures at both ends of both pieces
before merging. Keep pieces adjacent to an uncertain crossing separate. The
pre-timing unrestricted prototype failed the original relative certificate on
the frozen county 37027/district 3714 pair: radius 1.1280e-7 versus budget 9.09e-8,
while the baseline radius was 2.0335e-8. This guard limits interval-dependency
amplification; it does not change either error threshold or promise bit parity.

An unfilled positive-height interruption cannot join: the next emitted start
is later than the pending finish. Reusing immutable edge IDs allows delayed
integration after an edge leaves active status. The width of the gap is affine
on a fixed pair, so the exact integral over consecutive filled intervals equals
the integral over their union. No topology test is skipped. See the
[numerical contract](GAP-COALESCING-NUMERICS.md).

Coalescing changes rounding and accumulation order. Do not require historical
area/radius bits or the same numerical fallback reason. Every successful fast
result must still enclose the exact area within the original absolute 0.25 and
conservative relative 1e-10 budgets. Fallback uses original inputs and returns
the shipping result without claiming a new certificate. Reset pending validity
on every query, including invalid/early/fallback paths; retain bounded scratch.
Charge additional loops against the existing work budget.

Add an optional sixth constructor flag `coalesceGaps`, default false. Both
measured sweep methods use copied prepared geometry with ROI/cache/scalar filter
and specialized area arithmetic enabled. No shipping API, dependency or default
backend changes. Preserve the historical commands and JSON shape outside this
experiment; attach new optional gap diagnostics only in its validation records.

## Checks before timing

Use independent exact analytic/dyadic and rational geometric controls: persistent
affine gaps with many unrelated endpoint levels; fill interruption while the
same pair stays adjacent; split/rejoined adjacency; crossing boundaries; holes,
retracing, both fill rules, swapped/reversed paths; representable shifts and
scales; degenerate and invalid inputs; fallback/exception recovery and mixed
raw/prepared calls. Ordinary controls must certify, use merging and reduce
actual integrations. Run the existing rational certificate suite and prepared
snapshot checks normally and with hardware intrinsics disabled.

For every completed call, contributions = integrations + merges. The unmerged
baseline has integrations = contributions and zero merges. Record actual
HorizontalDifference evaluations and the additional retained coalescer array
element bytes, separately from immutable prepared metadata and total memory.
On frozen real inputs require 422/422 certified geometry calls for both modes,
unchanged topology diagnostics and candidate masks, consistent area certificates,
and the existing independent NTS budgets. NTS is not an exact oracle.

## Workload, costs and decision

Keep 109 original Census rings, 10588 vertices, 2156 directional pairs and 422
geometry candidates. Clipper uses scale 1e6; inputs for the double engines remain
unrounded. Methods A-D are Winding-intersection-only, Guarded-area, Guarded-gaps
and Clipper64-reused-data. Both directions retain the scopes prepare, warm-table,
warm-zones-fresh-queries and prepare-plus-one. Fresh queries are prepared once
per table. Preparation-plus-one charges new comparator/workspace growth;
Winding's thread-local workspace is warm, not cold-process startup.

Three fresh sequential Release processes, five calibrated samples per row,
32 rows/process and 480 samples. Set DOTNET_TieredCompilation=0. Keep method orders
ABCD/CDAB/BDAC. No concurrent builds, profilers or other benchmarks. Publish
median of process medians, process ranges and allocations; descriptive ranges
are not confidence intervals. Do not tune budgets after timing.

Each of the six full-query cells must beat 0.8 times Clipper and 0.9 times Winding,
with correctness required. Publish both sweep methods' twelve gate cells.
Reducing prototype work alone is not a reason to replace shipping Winding.

Rerun frozen 5bc86fb238be2ea751aa3429f6eb37045cb2651b with its unchanged
area-arithmetic matrix in a separate directory. Process sequence:
old1, new1, new2, old2, old3, new3. Never pool the series. Historical Guarded-area is
method C, contemporary baseline Guarded-area is B: method positions differ.
Report old/new ratios and sensitivity normalized by each series' own Winding
and Clipper times, without claiming that normalization eliminates drift.

Summarizers validate matrices, samples, medians, source/DLL hashes, recomputed
outputs, all recorded pair certificates and diagnostics, and prepared metadata
before publishing. Test unchanged positive evidence and deliberate corruptions,
including new gap counters and certificate records. Keep raw artifacts local;
publish compact evidence, commands, environment and measured commit hashes.

```text
check-gap-coalescing artifacts/scanbeam-gaps-check
benchmark-gap-coalescing artifacts/scanbeam-gaps <run 1..3> <revision>
summarize-gap-coalescing artifacts/scanbeam-gaps
```

Use benchmark-area-arithmetic/summarize-area-arithmetic with the old binary in
artifacts/scanbeam-gaps-historical. CI checks correctness without timings.
