# Assembly comparison runner

This optional standalone experiment loads one chosen PolylineKit assembly per
process, then uses strongly typed calls. It covers the official Winding fixtures
and thin subdivided rectangles in both orientations. It is not part of the library
or solution. No reflection or input construction occurs in the timed loops.

Build the library and this project in Release, then run:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet bin/Release/net10.0/PolylineKit.WindingPerf.dll `
    C:/absolute/path/to/PolylineKit.dll `
    C:/absolute/path/to/results.json `
    ../../results/winding/benchmarks/inputs.json
```

Use three fresh processes for each assembly, alternate their order, and keep
other CPU work out of the measurement window. The result identifies the loaded
assembly path, runtime and environment. Each workload has a 25 ms warmup, batch
calibration to at least 20 ms, then five samples. Raw samples and warm allocations
are retained. Timing is reported per complete Winding API call.

The official nine-sample comparison with Clipper remains `scripts/benchmark.ps1
-Suite Winding`. This smaller runner is for ablations and orientation cases.
