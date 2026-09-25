# Wider-coordinate scanbeam controls

This protocol is fixed before these timing runs. It tests the prototype's
Int128 path using exact transformations of the two frozen degenerate-grid
walks. It does not select new shapes using measured performance and does not
change the shipping library or the original seven-input experiment.

## Inputs and correctness

Take each frozen 64- and 256-point pair, joined as first followed by reversed
second, exactly as in PROTOCOL.md. The complete walks contain 128 and 512 points.
Make two separate transformations of each walk:

* Multiply both coordinates by 65536 (2^16). The area must be multiplied by
  4294967296 (2^32); the largest coordinate is 458752.
* Add 400000 to both coordinates. The area must remain unchanged; the largest
  coordinate is 400007.

All operations are exact for these binary64 integer inputs. No rounding,
repair, recentering or geometry-dependent selection is performed. These four
inputs fit the expanded [-524288, 524288] prototype domain and force its current
Int128 dispatch. In particular, translation does not require wider arithmetic
in principle: this control exposes the current implementation's choice to use
absolute supplied coordinates. Scaling also increases geometric extent.

Before timing, both NonZero and EvenOdd outputs must obey these metamorphic
relations within 1e-10 * max(1, abs(expected)). The base area comes from the same
prototype on the untransformed walk, whose correctness has already been tested
against independent analytic/exact-oracle controls and WindingArea. Therefore
the transformation test is not an independent exact-area oracle for these full
walks. WindingArea agreement is required separately at the same budget. Record
base/transformed input hashes, outputs, expected areas, differences and exact
binary64 equality in wide-validation.json. Summarization regenerates these
records using the measured binaries and checks their identity.

## Measurement and decision

Use the existing measurement engine and four unchanged methods: WindingArea,
IntegerScanbeam, direct Clipper64 with reused data, and direct Clipper64 with
preloaded data. Their distinct preparation costs and output work remain as
described in PROTOCOL.md. Clipper uses scale 1e6; integer input conversion is
exact here, while crossing coordinates still use a different, quantized grid.
Unscaled Clipper64 remains an untimed diagnostic, not the area oracle.

Four inputs times two fills times four methods give 32 rows per process.
Three fresh sequential processes, with DOTNET_TieredCompilation=0 and five
calibrated batches per row, give 480 samples. Report every row, process range,
managed allocation result and output difference. Preparation of the input
transformations is outside timing. Every prototype call still validates and
prepares its supplied Point2 array; no measured result is cached.

For each of the eight input/fill cells, the faster Clipper median divided by
the prototype median must be at least 1.25 (at least 20% less time). All eight
cells and the correctness checks must pass to pass this local engineering gate.
Preserve failed cells and complete raw files; no statistical, unseen-input or
general replacement claim follows from a pass. Compare these results with the
original small-coordinate controls, not by pooling them into a favorable mean.

## Commands

Using the Release DLL built from the recorded source revision:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll check-wide
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll benchmark-wide artifacts/scanbeam-wide 1 <revision>
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll benchmark-wide artifacts/scanbeam-wide 2 <revision>
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll benchmark-wide artifacts/scanbeam-wide 3 <revision>
dotnet benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll summarize-wide artifacts/scanbeam-wide
```

The original check/benchmark/summarize commands and their output format remain
unchanged. The wider-coordinate results use a separate artifact directory.
