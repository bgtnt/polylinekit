# Selected shapes: one filled-area result

This small suite makes the library's shape-dependent optimizations reproducible.
It complements the [real-contour benchmark](README.md) and
[building-pair evaluation](../../examples/PolygonOverlapEvaluation/RESULTS.md).
It is a deliberate selection of five historical cells, not a random population,
an application benchmark or a universal performance ranking.

See [the recorded results](SYNTHETIC-RESULTS.md) for the measured revision,
per-method timings, allocation counts and process ranges.

## Inputs and operation

| Input | Supplied vertices | Geometry | Fills |
|---|---:|---|---|
| `simple-spiky-star-4096` | 4096 | Increasing polar angle, alternating radii 1 and 0.15; simple, no self-intersections | NonZero |
| `frozen-grid-256` | 512 | A closed walk through 64 distinct integer grid points, with crossings, contacts and repeated edges | NonZero, EvenOdd |
| `repeated-square-512` | 512 | The four corners (0,0), (7,0), (7,7), (0,7), traversed 128 times | NonZero, EvenOdd |

The grid is [frozen locally](data/synthetic-grid.json); no data download or older
checkout is required. The star and square are generated in
[SyntheticCases.cs](SyntheticCases.cs). Each input closes implicitly and is
passed as a `Point2[]`. No geometry is simplified or repaired.

Every cell measures these methods:

- **Public:** `PolylineArea.FilledArea`, including strategy selection and input preparation.
- **ClosedPath:** the general boundary engine returning four winding integrals,
  from which the requested fill is selected. It performs more output work.
- **Clipper-full:** reusable Clipper64, with fresh scale conversion, Clear/Add,
  Union under the chosen fill rule, and area summation inside the timed call.
- **Clipper-preloaded:** conversion and Add outside timing; reusable Clipper64
  performs Union and area summation on every timed call.

Clipper2 C# is pinned to 2.0.0. Coordinates are multiplied by `1e6` and converted
using its `Point64(double, double)` constructor. Grid and square input vertices
remain exact integers; constructed intersections/output still use Clipper's
integer grid. Star vertices are quantized. Numeric differences are retained,
not used to claim equivalent exact geometry. Clipper constructs output contours;
Public returns a number. No C++ comparison is included.

## Reading the comparison

The star exercises the general engine's simplicity certification. If simplicity
is already guaranteed by the application, a direct shoelace sum is the appropriate
cheaper alternative to general fill processing. This suite does not time that
narrower operation.

The grid and square qualify for the bounded integer specialization on .NET 10.
They do not establish the same gains for arbitrary floating-point coordinates,
list representations, portable assemblies or pairwise intersection. A square
traversed 128 times has NonZero area 49 and EvenOdd area 0; this repeated-edge
case must not be described as many distinct proper crossings. An application
that recognizes this repetition may simplify it before clipping; that alternative
is not measured. The headline comparator excludes Clipper input preparation;
it is not a claim about every possible Clipper pipeline.

## Reproduce

From a clean committed checkout at the repository root:

```powershell
dotnet build benchmarks/PolylineKit.AreaBenchmarks -c Release
dotnet run --project benchmarks/PolylineKit.AreaBenchmarks -c Release --no-build -- synthetic-check artifacts/selected-shapes-check
$revision = (git rev-parse HEAD).Trim()
$frozen = "artifacts/selected-shapes-bin-$revision"
Copy-Item benchmarks/PolylineKit.AreaBenchmarks/bin/Release/net10.0 $frozen -Recurse
./benchmarks/PolylineKit.AreaBenchmarks/run.ps1 -Suite Synthetic `
  -Revision $revision -Runner "$frozen/PolylineKit.AreaBenchmarks.dll" `
  -Output "artifacts/selected-shapes-$revision"
```

Use a fresh destination when repeating a measurement. Do not run builds or other
benchmarks concurrently. The launcher runs three processes sequentially with
`DOTNET_TieredCompilation=0`, rotating method order between processes. Each method
warms for 80 ms, calibrates a power-of-two batch to at least 30 ms (up to 1,048,576
iterations), then measures seven batches. Full GC occurs before each batch;
collection during the timed calls remains included. Allocation counters are read
before allocating the sample record. Warm allocations exclude initialization and
retained buffers. Both engines retain their existing per-thread cache policies.

Checks compare Public with ClosedPath within `1e-12 * max(1, abs(area))`, test
the analytic star and square areas, preserve input hashes and repeat results.
The grid agreement is a cross-engine regression check, not an independent exact
oracle. The summarizer checks source/binary identity, sequential launches, all
methods and samples, recomputed areas and sample medians before writing evidence.
There is no timing pass/fail threshold.

`summary.md` and `evidence.json` contain every method, observed ranges and
allocations. `run-1.json` through `run-3.json` retain all 420 samples. Times are
medians of three process medians; observed ranges are not confidence intervals.
Raw output stays under ignored `artifacts/`; source and fixture stay in the
repository. Record measured revisions separately from later documentation commits.
