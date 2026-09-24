# Third-party notices

PolylineKit contains original MIT-licensed code and references one third-party runtime package for general fill-based comparisons. Third-party source is not copied or bundled into the maintained source tree.

The core, tests and benchmarks reference [Clipper2 2.0.0](https://www.nuget.org/packages/Clipper2/2.0.0), by Angus Johnson, Copyright 2010–2025, under the [Boost Software License 1.0](https://www.boost.org/LICENSE_1_0.txt). NuGet distributes its license with the dependency. Its identity and content hash are pinned in `packages.lock.json`. The released dependency is used without alteration. Graph integration, winding integration, normalization, affine maps, resampling and similarity fitting do not call Clipper.

The test/benchmark LIP and GenLIP references are independent formula implementations based on Pelekis et al., [TIME 2007](https://kbs.uni-hannover.de/~ntoutsi/papers/07.TIME.pdf) and [JIIS 2011](https://geoanalytics.net/and/papers/jiis11.pdf). Their reconstruction choices and limits are retained in the [archived baseline definitions](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/baselines.md). They are neither the authors' source nor certified reproductions of their software. No algorithm source was copied from those projects or from the author's unpublished RtTools/MPR001 code.

Winding predicates use floating-point filtering and expansion arithmetic following Shewchuk's robust-orientation approach; exact degeneracies use Simulation of Simplicity (Edelsbrunner and Mücke, 1990). The implementation and supported numerical contract are described in [winding-area.md](docs/winding-area.md).

## Area-change example data

The four complete single-ring contours in `examples/AreaChange/data` come from
[Natural Earth v5.1.2, 1:50m country boundaries](https://github.com/nvkelso/natural-earth-vector/blob/v5.1.2/geojson/ne_50m_admin_0_countries.geojson).
Natural Earth distributes these data in the [public domain](https://www.naturalearthdata.com/about/terms-of-use/).
The example records source and per-file SHA256 values, the original longitude/latitude
coordinates and the transformation to a local map plane. Simplified outlines and
overlays are derived from those data; they are not geodesic area measurements.
The pinned Clipper2 simplifier is used only in the example/tooling layer.

## Archived recognition research

Recognition code and derived evaluation artifacts are preserved in the [versioned research archive](research/README.md), outside the maintained library and benchmark surface. Its implementations of Yang Li's Protractor (CHI 2010) follow the [official pseudocode](https://depts.washington.edu/acelab/proj/dollar/protractor.pdf); its dynamic-time-warping recurrence is explicitly declared. They are fresh implementations, not copied recognizer source.

The [$1 project](https://depts.washington.edu/acelab/proj/dollar/) provides the separately downloaded gesture XML corpus. The original [UCI Pendigits corpus](https://archive.ics.uci.edu/dataset/81), DOI [10.24432/C5MG6K](https://doi.org/10.24432/C5MG6K), is attributed to Fevzi Alimoglu and Ethem Alpaydin and distributed by UCI under CC BY 4.0. Raw coordinates and source archives are not redistributed in this repository. Dataset identifiers, derived scores, hashes and source metadata support reproduction; see the frozen [import instructions](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/scripts/datasets/README.md) and [evaluation protocol](https://github.com/bgtnt/polylinekit/blob/00f96248cc404e2d662d9e51c457d811701fa889/docs/recognition-protocol.md).

7-Zip 26.02 was an external development-only `.Z` decoder for that research, not a runtime or bundled dependency.
