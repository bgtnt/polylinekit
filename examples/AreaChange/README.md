# Measure change after contour simplification

Compare the original and simplified filled regions to measure the area added
or removed. This does not bound the maximum local boundary displacement.

```csharp
var change = PolylineArea.CompareRegions(original, simplified);
Console.WriteLine($"Changed area: {change.SymmetricDifferenceArea}");
Console.WriteLine($"Changed fraction of union: {change.JaccardDistance}");
```

## Run the example

```sh
dotnet run --project examples/AreaChange -c Release -- check
dotnet run --project examples/AreaChange -c Release -- run artifacts/area-change
```

`check` verifies analytic controls and the supplied contours. `run` writes
numerical results and SVG overlays for twelve original/simplified pairs. The
example uses Clipper2's simplifier at three tolerances; the area library itself
does not depend on Clipper. No new simplification algorithm is provided here.

## Data and units

The four complete single-ring features are Bulgaria, Switzerland, Lesotho and
Nepal from Natural Earth v5.1.2, 1:50m country boundaries. No islands or holes
were discarded. A repeated closing point is removed for implicit closure.

Coordinates are transformed to a local equirectangular plane and uniformly
scaled so the longest bound is 1000. Areas are **squared normalized map-plane
units**, not land areas in square kilometres. The supplied manifest records
source and projected-coordinate hashes; ordinary runs are offline.

Made with Natural Earth. Its data are [public domain](https://www.naturalearthdata.com/about/terms-of-use/).
See the [pinned source](https://github.com/nvkelso/natural-earth-vector/blob/v5.1.2/geojson/ne_50m_admin_0_countries.geojson),
[manifest](data/manifest.json) and [reproduction script](data/freeze.py).

## Further use

The simplifier may change topology; `CompareRegions` measures the resulting
filled change rather than certifying topology preservation. If your application
has stronger guarantees, [specialized area identities](SPECIALIZED.md) explain
when less work is sufficient and how prerequisites are checked.

The executable also exposes benchmark commands for maintainers; see its command
usage and the [benchmark guide](../../benchmarks/README.md). Prior comparative
results are retained in the [development snapshot](https://github.com/bgtnt/polylinekit/tree/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3/examples/AreaChange).
