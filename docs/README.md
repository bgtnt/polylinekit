# Documentation

| Task | Guide |
|---|---|
| Start using the library | [Quick start](../README.md) |
| Measure areas, intersection, XOR, IoU or coverage | [Area methods](area.md) |
| Normalize, resample or align | [Transformations](comparison-api.md) |
| Generate contours or use quantized comparisons | [Optional Clipper adapter](clipper.md) |
| Integrate vertical separation between graphs | [Graph-area formula](design.md) |
| Understand speed, allocations and memory | [Performance](performance.md) |
| Evaluate predicted polygons against reference annotations | [Polygon overlap example](../examples/PolygonOverlapEvaluation/README.md) |
| Build or contribute a change | [Contributor guide](../CONTRIBUTING.md) |
| Inspect the alpha candidate and evidence | [Release notes](releases/0.1.0-alpha.1.md) |
| Verify packages or prepare publication | [Package verification and release guide](releasing.md) |

## Implementation reference

- [Boundary integration, fill rules and integer sweep](winding-area.md)
- [Numerical derivation and difficult inputs](winding-numerics.md)
- [Dependency and algorithm attribution](../THIRD-PARTY-NOTICES.md)

These details explain how the public methods work; they are not alternative
APIs an application must select. Development studies remain in the
[historical snapshots](../CONTRIBUTING.md#historical-studies).
