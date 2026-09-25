# Performance tools

Benchmark tools are optional; application projects reference only `src/`.
Build Release and run timings on an otherwise idle machine. Raw output belongs
under ignored `artifacts/`. Record source revision, target, input and binary
hashes; compare equivalent input/output preparation costs.

## Public area methods on real contours

[AreaBenchmarks](PolylineKit.AreaBenchmarks/README.md) measures
`PolylineArea.FilledArea` on original projected coordinates from complete,
attributed county populations. It includes per-contour and full-batch calls,
an independent area oracle, Clipper conversion-inclusive/preloaded variants,
three sequential processes and allocation counts.

```sh
dotnet run --project benchmarks/PolylineKit.AreaBenchmarks -c Release -- check artifacts/area-check
```

Follow its [protocol and commands](PolylineKit.AreaBenchmarks/README.md) for timing.

## Graphs, transforms and engine costs

The general runner provides graph, transformation, winding and Clipper suites:

```powershell
./scripts/benchmark.ps1 -Suite Transforms
./scripts/benchmark.ps1 -Suite WindingVsClipper2
```

The script builds a clean committed checkout, runs three processes and retains
raw data in `artifacts/benchmarks`. Other supported suites are `Graphs` and
`Winding`. `scripts/summarize-benchmarks.ps1` summarizes graph/transform runs;
winding and Clipper summaries are produced by the runner. The graph suite also
contains independent LIP/GenLIP reference comparisons for maintainers.

`scripts/profile-winding.ps1` records first-call and warm latency/allocations
and retained workspace inventory. These measurements are distinct from
complete-query competitor timings.

## Compare compatible assemblies

`PolylineKit.AssemblyBenchmarks` loads an explicit area assembly and checks
frozen numerical fixtures. Supply an absolute DLL path:

```powershell
$runner = 'benchmarks/PolylineKit.AssemblyBenchmarks/bin/Release/net10.0/PolylineKit.AssemblyBenchmarks.dll'
$library = (Resolve-Path src/PolylineKit.Winding/bin/Release/net10.0/PolylineKit.Winding.dll).Path
dotnet $runner $library benchmarks/fixtures/winding.json check 0 current
```

For timing, replace `check` with an output JSON path and use runs 1–3 for each
compatible build. `summarize-assembly.py` validates the six files named
`baseline-1.json` through `integrated-3.json` in the supplied directory.
For refactors, `dump:<output.json>` writes exact result-bit records without
timing. [Fixture provenance](fixtures/manifest.json) is versioned alongside the
inputs; `Shared/` holds deterministic generators and independent oracles.

Current [performance guidance](../docs/performance.md) is separate from the
[archived development studies](../CONTRIBUTING.md#historical-studies). Retired
prototype runners are available in those snapshots, not required by this build.
