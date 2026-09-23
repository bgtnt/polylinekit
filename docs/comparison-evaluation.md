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

The output records source revision, runtime, OS, CPU, operation values and every batch sample. Measured source: [`6d12758319ea38f1dede8aa3f2836b12162eabd0`](https://github.com/bgtnt/polylinekit/commit/6d12758319ea38f1dede8aa3f2836b12162eabd0). This evidence is committed afterwards so that the measured revision is unambiguous.

Environment: .NET SDK 10.0.401, runtime .NET 10.0.12, Windows build 26200 (runtime OS string `Microsoft Windows 10.0.26200`), Intel64 Family 6 Model 158 Stepping 12. Three separate processes produced **60 measurements and 540 timed batch samples**. Every pipeline output was zero area on this exact transformed-copy family; direct-fit RMS ranged from approximately `6.6e-16` to `3.6e-15`.

Median of the three per-process medians, in **microseconds per operation**:

| Operation | 16 vertices | 64 | 256 | 1024 | Allocated bytes/op at 1024 (rounded) |
| --- | ---: | ---: | ---: | ---: | ---: |
| EndpointBridgedArea | 7.23 | 23.23 | 86.61 | 342.02 | 536,057 |
| NormalizeOnePath | 0.82 | 2.81 | 10.38 | 40.69 | 16,576 |
| NormalizeAndCompare | 10.21 | 32.44 | 121.82 | 455.46 | 593,673 |
| SimilarityFit64 | 5.76 | 10.31 | 27.76 | 97.42 | 153,288 |
| NormalizeAlignCompare | 15.60 | 39.04 | 134.57 | 511.07 | 905,553 |

At 1024 vertices the complete pipeline's process medians range from **507.24 to 516.96 microseconds**. Its allocations are substantial (about 0.91 MB per call); this implementation favors inspectable immutable results and correctness over pooling. These timings do not justify a universal speed claim, and they do not measure closed phase search. Raw area before alignment and after normalization alone remain nonzero, as expected because normalization does not undo rotation.

Evidence: [exact inputs](../results/transforms/inputs.json), [run 1](../results/transforms/run-1.json), [run 2](../results/transforms/run-2.json), [run 3](../results/transforms/run-3.json), [summary including ranges and allocations](../results/transforms/summary.json). The summary rounds timings to 0.001 microseconds; raw samples retain full precision.

## Practical limits

- Endpoint-bridged fill can cancel winding and is sensitive to endpoint correspondence; filled-region XOR intentionally ignores stroke order.
- A union-rectangle ratio is not a calibrated similarity percentage. Zero area is not universal stroke identity.
- Clipper quantizes coordinates; tiny features can disappear. Normalization can reject poorly conditioned affine maps.
- Similarity fitting minimizes sampled residuals, not final area. Closed phase search is discrete, reversal optional, and narrow features can be missed between samples.
- The implementation supports similarity fitting and explicit affine application, not automatic general affine/projective or nonlinear registration.

The next useful evaluation is a set of concrete consumer strokes/contours with explicit transformation invariances and expected rankings. Package publication remains deferred.
