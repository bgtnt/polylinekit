# Fresh real-contour benchmark protocol

Freeze this protocol, source data and implementation before timing. This is a deliberately
small real-coordinate preservation check of the public API, not a universal performance claim
or a search for favorable geometry. Do not change the input population or gate after timing.

## Population and operation

Use every ring of every Delaware and Rhode Island county returned by the frozen Census
TIGERweb State_County layer 11 query. The states were selected as two small complete county
populations before any timing. The response contains all 8 counties, 9 rings and 16,668 supplied
vertices (69..5384 per ring). Washington County has two rings. No ring is omitted for size,
shape, simplicity or performance. The source response, layer metadata, exact query, retrieval
time, hashes and source links are under `data/`.

The service projects its geometry to EPSG:5070, NAD83 / Conus Albers, in metres. Read those
returned coordinates as binary64 and leave them unchanged: no local rounding, simplification,
translation, scale change, resampling or adjustment to trigger the integer specialization.
These absolute real coordinates are outside the specialization, so the public method retains
Winding. Each ring is an independent closed walk. **These are ring areas, not county totals**:
the benchmark does not infer or combine outer boundaries, holes or islands. Source boundary
accuracy and projection are not independently surveyed geographic ground truth.

## Independent correctness checks

Keep the supplied coordinates, repeated closure and order for every library call. For the
untimed oracle only, remove consecutive duplicate vertices and optional repeated closure;
these do not change the represented walk. Certify simplicity with exhaustive nonadjacent-edge
intersection checks, exact BigInteger determinants on the original binary64 dyadic coordinates,
and adjacent-retracing checks. Report every non-simple or structurally invalid ring explicitly;
never repair it or silently exclude it. A non-simple ring remains a timed general closed walk,
but receives no simple-polygon shoelace oracle claim.

For each certified simple ring compare translated, compensated shoelace area with exact dyadic
shoelace rounded to binary64 with an explicit nearest-even significand step (relative tolerance
1e-12, floor 1). Focused controls cover ties, exponent carries, subnormals and overflow. Compare both NonZero and EvenOdd
public results to that oracle (relative tolerance 1e-10, floor 1). Require the public result to be
bit-identical to `ClosedPath`'s requested integral, repeatable, finite and nonnegative. Require
unchanged input coordinate hashes and exact equality between both Clipper preparation variants.

Record Clipper's difference from the simple oracle and a diagnostic allowance of
`2 * perimeter / 1e6 + 4 * suppliedVertices / 1e12 + 1e-10 * max(1, oracleArea)`.
This includes grid displacement and a separate floating-point allowance; it is not a universal
quantized-topology error bound. Preserve failed/negative comparator results in evidence.

## Four methods and two granularities

For each ring and each fill measure:

1. `Public`: `PolylineArea.FilledArea(points, rule)`.
2. `ClosedPath`: `WindingArea.ClosedPath(points)` projected to the requested integral. This API
   computes four integrals and diagnostics; the public scalar API currently delegates to it
   for this population.
3. `Clipper-full`: convert all supplied coordinates, clear/add the subject, union and obtain
   the output area. Conversion multiplies by 1e6 and truncates toward zero, matching the
   archived comparator's contract. Clipper also quantizes generated intersections.
4. `Clipper-preloaded`: conversion and subject loading outside timing, execution/output area
   inside timing. Both Clipper instances and their output containers are reused.

Also measure one complete batch per fill: invoke the chosen method on every one of the nine
rings in frozen source order and sum returned areas. This sum consumes outputs; it is not an
asserted county-region union. No data loading or oracle work is timed.

## Timing, allocations and acceptance

Use a Release `.NET 10` build from a committed source revision and freeze its binary directory.
Check the embedded source revision in both harness and leaf library; record SHA-256 of harness,
library, Clipper and source fixture.
Run three fresh processes sequentially with `DOTNET_TieredCompilation=0`; no competing builds,
profilers or timing. The four-method order rotates by offsets 0, 1 and 2 for the three runs.

Each cell warms up for 40 ms. Calibrate a power-of-two invocation count to at least 20 ms, or
the 1,048,576 iteration cap, then take five samples with that count. Individual samples may
be shorter than the calibration interval. Record elapsed time and allocated bytes per
operation; batch B/op means bytes for all nine calls. First calls, buffer growth, retained
memory and nested workspaces are not represented by warm B/op.

There are 80 rows/process and 1200 raw samples. Aggregate as the median of three process
medians and report the observed process range, without claiming confidence intervals.
The primary gate is `Public <= 1.10 * ClosedPath` for **each complete-batch fill**. Report
every per-ring ratio, including all deviations above 10%; a batch pass must not hide these.
There is no predeclared speed-win claim against Clipper and no post-timing tolerance changes.

The summarizer recomputes every output, source hash, validation record, exact row matrix,
binary identity, sample iteration count and median before producing evidence. It also checks
all three launch records for revision/hash identity, zero exit codes, contained result timestamps
and nonoverlapping process intervals, and includes their hash in evidence. Raw results
belong under ignored `artifacts/`, not this source directory. Freeze and run with `run.ps1`.
