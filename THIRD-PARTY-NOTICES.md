# Third-party notices

PolylineKit contains original MIT-licensed code and references one third-party runtime package for general fill-based comparisons. Third-party source is not copied or bundled into our source tree.

The core and experiment reference [Clipper2 2.0.0](https://www.nuget.org/packages/Clipper2/2.0.0), by Angus Johnson, Copyright 2010–2025, under the [Boost Software License 1.0](https://www.boost.org/LICENSE_1_0.txt). NuGet distributes its license with the dependency. Its identity and content hash are pinned in `packages.lock.json`. The released dependency is used without alteration. Graph integration, normalization, affine maps, resampling and similarity fitting do not call Clipper.

LIP/GenLIP formulas and related research are attributed in [baselines.md](docs/baselines.md). No algorithm source was copied from those projects or from the author's unpublished RtTools/MPR001 code.

Recognition experiments implement Yang Li's Protractor (CHI 2010) from the
[official pseudocode](https://depts.washington.edu/acelab/proj/dollar/protractor.pdf)
and a declared constrained dynamic-time-warping recurrence. They are fresh source
implementations, not copied recognizer source. The [$1 project](https://depts.washington.edu/acelab/proj/dollar/)
provides the separately downloaded gesture XML corpus. The original
[UCI Pendigits corpus](https://archive.ics.uci.edu/dataset/81), DOI
[10.24432/C5MG6K](https://doi.org/10.24432/C5MG6K), is attributed to Fevzi Alimoglu
and Ethem Alpaydin and distributed by UCI under CC BY 4.0. Raw coordinates and
archives are not redistributed in this repository. Dataset identifiers, derived
scores, hashes and source metadata support reproduction; see the
[import instructions](scripts/datasets/README.md) and [protocol](docs/recognition-protocol.md).
7-Zip 26.02 is an external development-only `.Z` decoder, not a runtime or bundled dependency.
