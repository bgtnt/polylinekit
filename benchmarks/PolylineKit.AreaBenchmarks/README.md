# AreaBenchmarks

A small maintained benchmark of `PolylineArea.FilledArea` on original projected real contours.
It references only the area library and benchmark-only Clipper2 2.0.0; the retired experimental
runners are not build dependencies. See [the fixed protocol](PROTOCOL.md) and
[fixture provenance](data/README.md).

From the repository root:

```powershell
dotnet build benchmarks/PolylineKit.AreaBenchmarks -c Release
dotnet run --project benchmarks/PolylineKit.AreaBenchmarks -c Release --no-build -- check artifacts/area-check
```

Before timing, commit the source, build Release again and copy the complete output directory
to an ignored immutable directory. The launcher checks the embedded commit and binary hashes:

```powershell
./benchmarks/PolylineKit.AreaBenchmarks/run.ps1 `
  -Revision <40-character-commit> `
  -Runner artifacts/area-measured-bin/PolylineKit.AreaBenchmarks.dll `
  -Output artifacts/area-real-contours
```

The script launches three sequential processes, then writes validated `evidence.json` and
`summary.md` alongside the raw per-process files. Do not run builds or other measurements in
parallel. The source is 8 complete county features from two states; all 9 constituent rings
are measured individually and in one complete batch. Areas are in square metres, not county
totals. EPSG:5070 coordinates remain exactly as returned by the service.

The current primary question is whether the public scalar API preserves the performance of
the existing `ClosedPath` fallback on a fresh real-coordinate population. Results for Clipper
provide context; this workload does not establish an integer-specialization speedup. Numerical
checks use an independent BigInteger simplicity check and translated/exact shoelace oracles
for simple rings. Inputs needing repair are reported without changing their geometry.

The original calibration and reusable Clipper adapter structure were adapted from PolylineKit's
MIT-licensed area experiments archived at commit `5ef33e8`; this runner has no source link or
runtime reference to those projects.
