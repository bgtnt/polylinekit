# Optional WPF coverage comparison

This Windows-only .NET 10 host links the RegionCoverage experiment sources and frozen data.
It adds three WPF comparators to the same workload, preparation measurements, accuracy gate,
and timing harness. It is deliberately outside the cross-platform solution. Clipper2 and
NetTopologySuite are experiment dependencies; no dependency is added to the Winding library.

Build with `dotnet build benchmarks/PolylineKit.WpfBenchmarks -c Release`. The executable
accepts the same commands as `examples/RegionCoverage`.

Run the durable controls and frozen-data accuracy check without timings:

```powershell
dotnet run --project benchmarks/PolylineKit.WpfBenchmarks -c Release -- check
```

`WpfComparatorChecks.cs` supplies 69 analytic/reuse/coverage controls: alternating queries,
all original/reversed ring combinations, swapped zone/query coverage denominators, touching
and nested squares, bounding-box rejection, and null coverage for a zero-area zone. The
unit-sized analytic controls allow `2e-8` numerical error; this allowance does not change the
shared real-data accuracy gate. The command also prints the observed result for a `2^-26`
overlap without asserting that it must remain inaccurate in future WPF versions.

## Compared operations

| Name | Computation |
|---|---|
| `wpf-evenodd-area` | Area scanner over both input figures using EvenOdd gives XOR; `I=(A+B-XOR)/2`. |
| `wpf-nonzero-area` | Area scanner over equally oriented figures using Nonzero gives union; `I=A+B-union`. |
| `wpf-combine-explicit` | `Geometry.Combine(Intersect, 1e-6, Absolute)` constructs the intersection; `GetArea(1e-6, Absolute)` measures it. |

The first two identities require the experiment's certified simple, single-ring polygons.
Nonzero orientation is normalized once during preparation. These identities are not general
substitutes for independently filled arbitrary self-intersecting paths.

Independent inputs, their own WPF areas, and their frozen figures/geometries are prepared once.
Each comparison replaces the two input figures in a reusable `PathGeometry`; it does not reuse
a cached intersection or union. A comparator instance is used from one thread. The common
runner performs the same bounding-box rejection and uses `I / zone.Area` for coverage.
Cached own areas are WPF `GetArea` results, including that engine's numerical error.

This two-figure container is the public equivalent of concatenating the children of a
`GeometryGroup`. The [group implementation](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/GeometryGroup.cs)
creates a temporary path of its children's figures. Reusing that path directly avoids the
temporary group conversion. `PathGeometry` still serializes the figures for every native
call; preparation is not a cached native spatial index.
[Path serialization](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/PathGeometry.cs)

The first two rows reach the native area scanner without producing Boolean output contours.
The [CArea implementation](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/geometry/area.cpp)
classifies boundary pieces during scanning and accumulates their signed area contributions.
The third row intentionally measures the public contour-producing alternative.

`CombinedGeometry.GetArea(tolerance, Absolute)` is not used for the latter row: its hidden
Combine call uses the default tolerance, before the supplied tolerance reaches `GetArea`.
It also obtains path representations of both operands. Explicit `Geometry.Combine` accepts
the frozen `StreamGeometry` buffers directly and sets the tolerance for the overlay itself.
[CombinedGeometry source](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/CombinedGeometry.cs)

## Numerical contract

WPF is a graphics geometry engine. A requested `1e-6` flattening tolerance does **not** promise
that linear polygon coordinates or intersection areas are accurate to that amount. Its
[scanner workspace](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/geometry/scanner.cpp)
centers and rescales the geometry extent using `LARGESTINTEGER26`, then operates on rounded
integer-valued coordinates. Float bounds and contour output introduce additional precision
limits. The tolerance primarily controls curve approximation; this experiment has line
segments only. The [public tolerance documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.geometry.getarea?view=windowsdesktop-10.0)
also specifies a minimum tolerance of `1e-6`.

Own-area and combined-area scans can use different workspace grids. Subtraction can amplify
their inconsistent rounding near a zero intersection. The experiment preserves negative
estimates and zeroed tiny overlaps: there is no clamping, hidden repair, or fallback.

During development, a separate isolated .NET 10.0.12 probe checked 26 cases at tolerances `0.25`, `1e-6`, and
`1e-9`. Direct two-figure paths and GeometryGroups returned identical area values in all 78
checks. Identical, touching, nested and ordinary overlapping squares were included, together
with scale changes and the proposal's Census pair. Selected observations:

- A unit-square overlap of width `2^-26` became zero in all three WPF approaches; reducing
  the requested tolerance did not restore it.
- Without the common runner's prior bounding-box rejection, disjoint unit squares separated
  by `2^-10` produced an EvenOdd-derived intersection of `-2.3283064365386963e-10`.
- For the centered Census pair, intersection errors against the proposal's GEOS value were
  approximately `+1.37 m²` (EvenOdd), `-7.80 m²` (Nonzero), and `+14.64 m²` (explicit Combine).
  That GEOS value is a comparison reference, not an exact-rational oracle. Raw coordinates
  gave larger errors. These are calibration observations, not a representative GIS dataset.

That broader isolated probe is not part of the committed harness. The calibration numbers
above describe the inspected runtime; they are not assertions executed by `check`. The
committed controls reproduce the ordinary-case contracts and print the tiny-overlap
observation. Full accuracy results are generated independently on the frozen dataset.

The frozen workload uses one common coordinate translation for every engine. No WPF-only
per-pair normalization is applied. The shared accuracy gate is fixed before timing; a WPF
row that misses it remains visible as a graphics-precision comparison and cannot support a
same-quality speed claim.

Warm allocation measurements count managed bytes on the calling thread. WPF also allocates
inside native code; these bytes are **not** included in that counter. A low managed byte count
must not be described as total or native allocation-free operation.
