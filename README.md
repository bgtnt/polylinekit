# PolylineKit

Small C# tools for area-based polyline comparison, normalization, resampling and
alignment. Each operation has an explicit geometric meaning. The API is experimental;
use a project reference while it is under review. No NuGet release is available yet.

## Measure filled-area change

```csharp
using PolylineKit;

Point2[] original = [new(0, 0), new(2, 0), new(2, 2), new(1, 1), new(0, 2)];
Point2[] processed = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
var change = WindingArea.FilledRegions(original, processed);
double changedArea = change.SymmetricDifferenceArea; // 1 square unit
double? changedFraction = change.JaccardDistance;    // 0.25 of the filled union
```

This directly measures filled-region change, for example after contour
simplification. It does not bound the largest boundary displacement. Reference
only `PolylineKit.Winding` when these area values are sufficient; the broader
library also supplies normalization, alignment and Clipper-generated contours.
The [area-change example](examples/AreaChange/README.md) uses licensed real contours,
records simplification settings, and produces measurements and overlays.

For independent regions, the [coverage example](examples/RegionCoverage/README.md)
measures intersection and the fraction of a prepared zone covered by another
region. It compares complete county/district populations with Clipper2, NTS and
optional WPF, with separate accuracy, preparation and repeated-query costs.
Use `WindingArea.IntersectionArea` when only the intersection is needed. In the
[measured coverage workload](examples/RegionCoverage/INTERSECTION-RESULTS.md),
it takes 26-29% less time than direct reusable Clipper64, including fresh sessions.
This is a specific area-only use case, not a universal clipping speed claim.

## Compare, normalize, align

```csharp
using PolylineKit;

Point2[] reference = [new(0, 0), new(2, 0), new(2, 1), new(1, 2), new(3, 3)];
Point2[] moving = AffineTransform2D.Scaling(2.5)
    .Then(AffineTransform2D.Rotation(0.4))
    .Then(AffineTransform2D.Translation(10, -7)).Apply(reference);

// Fit translation, rotation and uniform scale using arc-length samples.
var fit = PolylineAlignment.FitSimilarity(moving, reference);
var comparison = PolylineComparison.EndpointBridgedArea(reference, fit.AlignedPoints);
double area = comparison.RawArea;           // Approximately zero, within clipping precision.
double rms = fit.RmsError;                  // Sampled residual in reference units.
AffineTransform2D transform = fit.Transform;

// Independent normalization removes position and bounds size, not rotation.
var normalized = PolylineNormalization.ToUnitBounds(moving);
```

The [runnable example](examples/Basic/Program.cs) also compares filled regions.
Alignment minimizes sampled squared distances, not area. Reversed traversal is
opt-in; closed-path fitting searches discrete cyclic sample shifts.

## Choose the operation

| Operation | Meaning |
| --- | --- |
| `PolylineArea.BetweenGraphs(p, q)` | Absolute vertical separation integrated over a shared x interval; strictly increasing-x graphs only. |
| `WindingArea.ClosedPath(p)` / `EndpointBridged(p, q)` | NonZero, EvenOdd, absolute-winding and signed areas; accepts self-intersections and returns areas without constructing contours. |
| `WindingArea.FilledRegions(p, q)` | Intersection, union and XOR areas of two independently filled paths. |
| `WindingArea.IntersectionArea(p, q)` | Only the intersection area of two independently filled paths; skips the unused own/union/XOR area accumulation. |
| `PolylineComparison.EndpointBridgedArea(p, q)` | Fill area of `p + reverse(q)` with straight endpoint connectors; Clipper2 precision and optional resolved contours. |
| `PolylineComparison.FilledRegionDifference(p, q)` / `FilledRegionOverlap(p, q)` | Independent filled-region XOR, or union/XOR and Jaccard distance/IoU, through Clipper2. |
| `PolylineNormalization.ToUnitBounds(p)` / `MatchBounds(p, q)` | Center and scale; returns transformed points and the applied affine map. |
| `PolylineSampling.ResampleByArcLength(p)` | Equidistant samples along an open or closed path. |
| `PolylineAlignment.FitSimilarity(p, q)` | Sampled translation/rotation fit with optional uniform scale. |

These are different geometric definitions. Area does not bound the worst local
deviation or distinguish every traversal. `BoundsAreaRatio` divides by the joint
bounding rectangle, so it is not a calibrated similarity percentage; it is null
for zero-area bounds. `NormalizedArea` remains a compatibility alias. Jaccard
distance and IoU instead use the filled union and are null when that union is zero.

Read the [comparison and transformation contracts](docs/comparison-api.md),
[graph-area definition](docs/design.md), and [winding-area contract and numerical
limits](docs/winding-area.md) before choosing a method. Exact orientation decisions
do not make floating-point intersection coordinates or accumulated areas exact.

## Build and verify

The library targets **.NET Standard 2.0** and **.NET 10**. Modern builds use optional
packed-double SIMD for selected operations and have scalar fallbacks.
[`PolylineKit.Winding`](src/PolylineKit.Winding/README.md) is an independent leaf
assembly with no external runtime dependencies: reference it directly for winding
areas and region overlap. The broader `PolylineKit` project references that leaf
and **Clipper2 2.0.0** for general polygon fill and Boolean operations. It also
provides the graph integral, transforms, normalization, resampling and alignment.
Both projects use the `PolylineKit` namespace.

The parent assembly forwards the five extracted public types. Existing compiled
components can keep their old type references, but deployment must include the
new leaf DLL and an updated dependency manifest. See the
[consumer checks](tests/Consumers/README.md). No package publication, trimming or
NativeAOT compatibility claim is part of this extraction.

Install the .NET 10 SDK and run:

```sh
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
pwsh -File scripts/verify-implementations.ps1
pwsh -File scripts/verify-consumers.ps1
dotnet run --project examples/Basic -c Release --no-build
```

The deterministic console checks fail with a nonzero exit code; they are not a
`dotnet test` project. The verification script checks the portable target, modern
target, forced scalar dispatch, no-AVX mode and no-intrinsics fallback. CI runs on
Windows and Linux, including benchmark smoke checks without collecting timings.

## Repository guide

| Directory | Purpose |
| --- | --- |
| [`src/PolylineKit.Winding`](src/PolylineKit.Winding) | Independent winding and overlap areas; framework-only runtime dependencies. |
| [`src/PolylineKit`](src/PolylineKit) | Broader comparison, normalization, sampling and alignment API. |
| [`examples/Basic`](examples/Basic) | Small runnable consumer. |
| [`docs`](docs) | Contracts, limitations and a concise performance assessment. |
| [`tests/PolylineKit.Checks`](tests/PolylineKit.Checks) | Deterministic geometry, numeric, transformation and dispatch checks. |
| [`tests/Consumers`](tests/Consumers) | Standalone, portable and precompiled binary consumer checks. |
| [`benchmarks`](benchmarks/README.md) | Maintained runners, deterministic fixtures and compact measurement evidence. |
| [`scripts`](scripts) | Build verification and benchmark orchestration. |
| [`research`](research/README.md) | Index and checksums for the archived experiments and raw evidence. |

New benchmark output goes under ignored `artifacts/`. The
[benchmark guide](benchmarks/README.md) describes three-process timing and
allocation measurements. The [performance assessment](docs/performance.md)
summarizes measured gains, regressions and hardware-specific limits; there is no
universal speed guarantee.
The latest [double sweep ablations](benchmarks/PolylineKit.ScanbeamBenchmarks/DOUBLE-ABLATION-RESULTS.md)
halve that prototype's full-query time while preserving the tested certificates.
It is still substantially slower than current Winding and Clipper on the measured
contours and remains outside the library.

Historical recognition experiments did not justify developing area-based
recognition further. Their code, protocols and negative results remain available
in the [versioned research archive](research/README.md), alongside the original
LIP/GenLIP and optimization experiments. They are not needed to build or use the library.

## License and provenance

Original code is [MIT-licensed](LICENSE). RtTools.Geometry was inspected for ideas;
an optional private RtTools adapter was also measured in the dynamic-index experiment.
Its code and binaries are not distributed or required. MPR001 is not included or
used as a test oracle or benchmark.
See [third-party notices](THIRD-PARTY-NOTICES.md) for dependency and algorithm attribution.
