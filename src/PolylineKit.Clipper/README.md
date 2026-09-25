# PolylineKit.Clipper

Optional Clipper2 integration for quantized area comparisons and operations
that also need resulting contours. Targets .NET Standard 2.0 and .NET 10.
References `PolylineKit.Core` at the same alpha version and Clipper2 2.0.0.

Version `0.1.0-alpha.1` is prepared for evaluation. Until publication on NuGet.org,
use the local feed and NuGet.org source configuration in the
[package verification guide](https://github.com/bgtnt/polylinekit/blob/main/docs/releasing.md).
Then add the package:

```sh
dotnet add MyApp/MyApp.csproj package PolylineKit.Clipper --version 0.1.0-alpha.1
```

```csharp
using PolylineKit;

Point2[] first = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] second = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
var difference = PolylineComparison.FilledRegionDifference(
    first, second, PathFillRule.NonZero,
    decimalPrecision: 6, includeContours: true);
// RawArea = 4; Contours contains the two resolved difference boundaries.
```

Every `PolylineComparison` call uses Clipper2, including `includeContours: false`.
Coordinates are quantized; features near or below the selected grid can collapse.
Each operand is one ordered walk, not a collection of rings. See the
[adapter guide](https://github.com/bgtnt/polylinekit/blob/main/docs/clipper.md)
for fill rules, precision limits and compatibility.

If you only need numeric areas, normalization or alignment, reference
[PolylineKit.Core](https://github.com/bgtnt/polylinekit/blob/main/src/PolylineKit.Core/README.md)
instead. It has no dependency on this adapter or Clipper2.

The assembly retains its `PolylineKit` identity for previously compiled consumers
and forwards moved public types into the core. PolylineKit code is MIT licensed;
[Clipper2](https://github.com/AngusJohnson/Clipper2) is by Angus Johnson under the
[Boost Software License 1.0](https://github.com/AngusJohnson/Clipper2/blob/main/LICENSE).
