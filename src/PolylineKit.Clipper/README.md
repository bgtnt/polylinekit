# PolylineKit.Clipper

Optional adapter for resolved output contours and quantized
`PolylineComparison` operations. It references `PolylineKit.Core` and
Clipper2 2.0.0. Every `PolylineComparison` call uses Clipper2, including
`includeContours: false`.

See [the adapter guide](../../docs/clipper.md) for setup, precision, examples and
compatibility. If you only need areas, normalization or alignment, reference
[PolylineKit.Core](../PolylineKit.Core/README.md) instead.

The assembly retains its `PolylineKit` identity for previously compiled
consumers and forwards moved public types into the core. Source projects must
use the new project path. No package has been published.
