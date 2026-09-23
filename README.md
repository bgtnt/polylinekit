# PolylineKit

Small experimental C# tools for the **unsigned area between piecewise linear graphs over the same x interval**, with reproducible comparisons against LIP, a documented GenLIP reconstruction and Clipper2.

The experiment finds a real stability advantage over LIP's intersection-dependent area weights in a near-touch case. It does **not** establish a universal trajectory similarity method, new mathematics, or a replacement for Clipper's general polygon engine. Read the [decision and measurements](docs/report.md), [mathematical contract](docs/design.md) and [baseline limitations](docs/baselines.md) before relying on a score.

## Use the candidate API

```csharp
using PolylineKit;
Point2[] p = [new(0, 0), new(2, 0)];
Point2[] q = [new(0, 0), new(1, 1), new(2, 0)];
double area = PolylineArea.BetweenGraphs(p, q); // 1 square coordinate unit
```

The result is in squared coordinate units; smaller means less accumulated vertical separation. Divide by the common x span for mean absolute vertical separation. This is not a percentage, alignment result or maximum-deviation bound.

Requirements: both paths increase in x and have exactly the same domain endpoints. Unequal sampling, collinear subdivisions, mutual crossings and consecutive identical points are supported. Vertical segments, backtracking and closed paths are rejected. The comparison uses existing coordinates; normalization and alignment are separate and currently unimplemented. See the full [numeric contract](docs/design.md).

`PolylineKit.0.1.0-alpha.1` is a **local prerelease for independent review**. It targets `netstandard2.0` and has no external runtime packages. It has **not** been published to NuGet.org, and the API can change. The experiment targets .NET 10; its only third-party package is Clipper2 2.0.0.

## Three small examples

One triangle gives area 1:

![Triangle](https://raw.githubusercontent.com/bgtnt/polylinekit/main/results/geometry/triangle.svg)

Opposite lobes add rather than cancel:

![Crossing lobes](https://raw.githubusercontent.com/bgtnt/polylinekit/main/results/geometry/crossing.svg)

Near-touch geometry changes little while LIP's region weights change substantially:

![Near touch](https://raw.githubusercontent.com/bgtnt/polylinekit/main/results/geometry/near-touch.svg)

The near-touch drawing enlarges epsilon; the exact measured coordinates and per-region areas/weights are in [geometry.json](results/geometry/geometry.json).

## Reproduce

Install the .NET 10 SDK, then run from this repository:

```sh
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
dotnet run --project experiments/PolylineKit.Experiments -c Release --no-build -- check
dotnet run --project examples/Basic -c Release --no-build
dotnet run --project experiments/PolylineKit.Experiments -c Release --no-build -- evidence artifacts/reproduced-geometry
```

Checks are a deterministic console harness, **not a `dotnet test` project**. A failed check throws and exits nonzero. CI runs this same command on Windows and Linux.

Three independent timing processes, with allocations and all individual samples:

```powershell
pwsh -File scripts/benchmark.ps1 -OutputDirectory artifacts/reproduced-benchmarks
```

The script requires committed tracked changes and sets `DOTNET_TieredCompilation=0`. Fixture generation is outside timing. See [benchmark methodology](docs/report.md#benchmark-methodology) for interpretation and the manual cross-platform equivalent.

Pack, inspect metadata/dependencies and install from a local feed into an isolated fresh consumer:

```powershell
pwsh -File scripts/verify-package.ps1
```

The package is written to `artifacts/packages/PolylineKit.0.1.0-alpha.1.nupkg`. The script uses a fresh package cache and restores the consumer from that local feed only.

## Scope and provenance

Original code is MIT-licensed. All implementation was written afresh; the author's unpublished RtTools.Geometry was inspected for ideas only. Neither it nor MPR001 is included, linked or used as a test oracle or benchmark. No third-party algorithm source is copied. [Sources and reconstruction choices](docs/baselines.md) distinguish published definitions from implementation policies.

General contours remain experimental: NonZero, EvenOdd and net winding multiplicity give different answers for repeated loops. Those operations are not exposed as a generic path-distance API. [Complex-contour evidence](results/geometry/contours.json) makes this distinction explicit.
