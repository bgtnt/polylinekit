# Area methods

Dependency-free area operations for .NET Standard 2.0 and .NET 10, in the
`PolylineKit` namespace. Reference this project when you need numerical areas
without output contours:

```csharp
double area = PolylineArea.FilledArea(path, PathFillRule.NonZero);
double intersection = PolylineArea.IntersectionArea(first, second);
RegionOverlapResult change = PolylineArea.CompareRegions(first, second);
double graphDifference = PolylineArea.BetweenGraphs(firstGraph, secondGraph);
```

See the [area guide](../../docs/area.md) for input contracts, fill rules and
examples, and [performance and memory](../../docs/performance.md) for costs and
limitations. Each operand is an ordered walk, not a collection of rings. Areas
use squared coordinate units; no input quantization or implicit normalization
is applied. Floating-point results are not exact area guarantees.

The assembly retains its `PolylineKit.Winding` name for binary compatibility.
`WindingArea` remains available for advanced integrals and diagnostics; its
engines are described in the [implementation reference](../../docs/winding-area.md).

For normalization, resampling, alignment or resolved contours, reference the
complete [PolylineKit project](../PolylineKit/PolylineKit.csproj), which includes
these area methods. The [quick start](../../README.md) shows project setup.
