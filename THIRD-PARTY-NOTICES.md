# Third-party notices

PolylineKit contains original MIT-licensed code and references one third-party runtime package for general fill-based comparisons. Third-party source is not copied or bundled into our source tree.

The core and experiment reference [Clipper2 2.0.0](https://www.nuget.org/packages/Clipper2/2.0.0), by Angus Johnson, Copyright 2010–2025, under the [Boost Software License 1.0](https://www.boost.org/LICENSE_1_0.txt). NuGet distributes its license with the dependency. Its identity and content hash are pinned in `packages.lock.json`. The released dependency is used without alteration. Graph integration, normalization, affine maps, resampling and similarity fitting do not call Clipper.

LIP/GenLIP formulas and related research are attributed in [baselines.md](docs/baselines.md). No algorithm source was copied from those projects or from the author's unpublished RtTools/MPR001 code.
