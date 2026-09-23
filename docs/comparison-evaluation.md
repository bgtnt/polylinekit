# Comparison and transformation evaluation

The current implementation adds usable, explicitly named preparation and comparison methods. It does not claim a universal distance or new superiority over LIP/GenLIP for arbitrary paths. The [original experiment](report.md) remains the source of the graph-domain stability result.

## Correctness

Release build and example pass with zero compiler warnings/errors. The console harness executes **1751 checks**: 1351 original geometric/formula checks, 329 comparison/normalization checks, and 71 alignment/sampling checks.

The new checks cover analytical endpoint-bridged areas and filled-region XOR, graph-oracle agreement, holes, repeated loops and fill parity, overlaps, collinear subdivisions, traversal direction, optional closing points, immutable snapshots, precision rejection and undefined rectangle normalization. They explicitly reproduce the parallel-segment normalization limitation.

Transformation checks use known maps and residuals: translation, rotation, scale, rigid fitting, bounds fitting/stretching, arc-length sampling with uneven discretization, optional reversal, closed sample phase, tiny/large coordinates, covariance degeneracy, and actual returned-transform RMS. Independent review found and fixed underflow in the bounds midpoint and material loss of centering from ill-conditioned affine normalization. Regressions retain both examples.

The runnable example recovers scale **0.4** and angle **-0.4 radians** from an input scaled by 2.5, rotated by 0.4 and translated. Sample RMS is approximately **8.1e-16**, and endpoint-bridged area after clipping is zero. Two overlapping 2-by-2 squares shifted by one unit have XOR area **4** and normalized ratio **2/3**. Exact inputs are in `examples/Basic/Program.cs`.

## Timing protocol

The transform suite measures five different operations: endpoint-bridged area, normalization of one path, independent normalization and comparison, 64-sample similarity fitting, and normalization followed by fitting and area comparison. These stages are different workloads, not competing algorithms.

Inputs have 16, 64, 256 and 1024 vertices. Reference points use `t=i/(n-1)`, `x=3t+0.5*sin(9t)`, `y=sin(5t)+t`; moving points are scaled by 2, rotated by 0.37 radians and translated by `(7,-4)`. Exact generated coordinates are saved in `inputs.json`. This is one deterministic open-path family, not coverage of all intersection densities or closed phase-search costs. The earlier graph suite separately covers intersection-density cases.

Each method receives 60 ms warmup and batches calibrated to at least 20 ms (unless the iteration cap is reached). Nine timed batches follow, with explicit garbage collection outside the timed region. Timing uses `Stopwatch`; allocations use `GC.GetAllocatedBytesForCurrentThread`. Input generation and optional contour diagnostics are excluded, but validation and all internal result allocations are included. Method order rotates across three fresh processes. Tiered compilation is disabled.

Reproduce from committed source:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
pwsh -File scripts/benchmark.ps1 -Suite Transforms -OutputDirectory artifacts/transform-benchmarks
```

The output records source revision, runtime, OS, CPU, operation values and every batch sample. Performance data is added in the subsequent evidence commit so the measured source revision can be recorded without a self-referential hash.

## Practical limits

- Endpoint-bridged fill can cancel winding and is sensitive to endpoint correspondence; filled-region XOR intentionally ignores stroke order.
- A union-rectangle ratio is not a calibrated similarity percentage. Zero area is not universal stroke identity.
- Clipper quantizes coordinates; tiny features can disappear. Normalization can reject poorly conditioned affine maps.
- Similarity fitting minimizes sampled residuals, not final area. Closed phase search is discrete, reversal optional, and narrow features can be missed between samples.
- The implementation supports similarity fitting and explicit affine application, not automatic general affine/projective or nonlinear registration.

The next useful evaluation is a set of concrete consumer strokes/contours with explicit transformation invariances and expected rankings. Package publication remains deferred.
