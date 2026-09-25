# Third-party notices

Original PolylineKit code is [MIT licensed](LICENSE). Third-party library source
is not copied or bundled into the maintained source tree. Package versions and
content hashes are pinned in `packages.lock.json` files.

## Runtime dependency

The complete `PolylineKit` project references [Clipper2 2.0.0](https://www.nuget.org/packages/Clipper2/2.0.0),
by Angus Johnson, Copyright 2010–2025, under the
[Boost Software License 1.0](https://www.boost.org/LICENSE_1_0.txt). Its package
distributes the license. The dependency is used without alteration for general
contour-producing fill/Boolean comparisons. Area integration, graph comparison,
normalization, transforms, resampling and alignment do not call Clipper.
The area-only project has no third-party runtime dependency.

## Tooling dependencies

Examples and benchmarks also use Clipper2. The region-coverage example references
[NetTopologySuite 2.6.0](https://www.nuget.org/packages/NetTopologySuite/2.6.0),
Copyright 2006–2025 NetTopologySuite contributors, under
[BSD-3-Clause](https://licenses.nuget.org/BSD-3-Clause). These example/tool
dependencies are not added to the area library's runtime.

## Algorithm references

Boundary predicates use floating-point filtering and expansion arithmetic
following Shewchuk's robust-orientation approach; exact degeneracies use
Simulation of Simplicity (Edelsbrunner and Mücke, 1990). The implementation and
numerical limits are described in [the area-engine reference](docs/winding-area.md).

Test/benchmark LIP and GenLIP references are independent formula implementations
based on Pelekis et al., [TIME 2007](https://kbs.uni-hannover.de/~ntoutsi/papers/07.TIME.pdf)
and [JIIS 2011](https://geoanalytics.net/and/papers/jiis11.pdf). They are not the
authors' source or certified reproductions of their software. Reconstruction
choices remain in the [archived baseline definitions](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/baselines.md).

## Geographic fixtures

The area-change example uses complete single-ring country contours from
[Natural Earth v5.1.2](https://github.com/nvkelso/natural-earth-vector/blob/v5.1.2/geojson/ne_50m_admin_0_countries.geojson).
Made with Natural Earth. Its data are [public domain](https://www.naturalearthdata.com/about/terms-of-use/).
The [example manifest](examples/AreaChange/data/manifest.json) records original
coordinates, source hashes and transformation to a local map plane. Derived
areas are not geodesic land-area measurements.

The region-coverage and real-contour benchmark fixtures come from the U.S.
Census Bureau's TIGERweb services. They retain attribution, original source
responses, queries and hashes. U.S. government geographic data are public domain
in the United States; see the source terms and provenance in the
[coverage fixture](examples/RegionCoverage/data/README.md) and
[area benchmark fixture](benchmarks/PolylineKit.AreaBenchmarks/data/README.md).
The project's MIT license does not replace source-data attribution or terms.

Archived studies retain their original notices alongside their source in the
[historical snapshots](CONTRIBUTING.md#historical-studies).
