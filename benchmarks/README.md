# Benchmarks

For the isolated active-edge experiment on dense integer grids, see the
[bounded-integer scanbeam prototype](PolylineKit.ScanbeamBenchmarks/README.md).
Its exact-integer input contract is separate from the shipping binary64 engine.
The [results](PolylineKit.ScanbeamBenchmarks/RESULTS.md) and
[evidence](scanbeam-evidence.json) include both failed arithmetic baselines,
the final grid gains and the unfavorable controls.
The [expanded results](PolylineKit.ScanbeamBenchmarks/EXPANDED-RESULTS.md) test
real-contour intersections and larger coordinates: both full gates fail, and
the original-input effects of metre-grid rounding are reported separately.
The [guarded double sweep](PolylineKit.ScanbeamBenchmarks/DOUBLE-RESULTS.md)
preserves original coordinates and passes its numerical checks. All four speed
gates fail: roughly 8x slower than Clipper and 10–11x slower than existing Winding
on the frozen real-contour workload. It remains an experiment outside the library.
The subsequent [common-Y and cached-X ablations](PolylineKit.ScanbeamBenchmarks/DOUBLE-ABLATION-RESULTS.md)
cut its full-query time by 2.0–2.1x, but the combined version still fails all four
competitor gates. These gains do not change the shipping implementation.
The subsequent [certified scalar order filter](PolylineKit.ScanbeamBenchmarks/SCALAR-FILTER-RESULTS.md)
saves another 20% against the optimized sweep in the same measured processes,
while all competitor gates still fail. Its error-bound derivation and exact-sign
checks accompany the measurements.
The [prepared-path experiment](PolylineKit.ScanbeamBenchmarks/PREPARED-SWEEP-RESULTS.md)
then saves 47% against the filtered sweep by reusing slopes and sorted endpoints.
Warm tables take 20.3–20.5 ms, but all competitor gates still fail. Preparation,
retained array payload and incoming-query costs are measured separately.

For an area-engine optimization with a predeclared Clipper gate, see the
[intersection-only protocol](../examples/RegionCoverage/INTERSECTION-PROTOCOL.md),
[results](../examples/RegionCoverage/INTERSECTION-RESULTS.md) and
[compact evidence](intersection-evidence.json). It measures complete coverage
queries and fresh sessions, with all four gates passing.

For dynamic segment indexing during vertex removal, see the
[bounded index experiment](PolylineKit.DynamicIndexBenchmarks/README.md).
It compares linear scans and an independent fixed-slot hierarchy with identical
query/update traces; an optional private baseline is local only. Its
[measured results](PolylineKit.DynamicIndexBenchmarks/RESULTS.md) and
[evidence manifest](dynamic-index-evidence.json) preserve both gains and losses.

For specialized simplification identities and conservative threshold filtering,
see the [AreaChange method](../examples/AreaChange/SPECIALIZED.md),
[all measured rows](../examples/AreaChange/SPECIALIZED-RESULTS.md) and
[compact evidence manifest](simplification-evidence.json). These distinguish
supplied geometric guarantees from their validation cost and do not change the
general-engine benchmarks below.

These optional .NET 10 tools measure complete operations, including their ordinary
preparation and allocations. They are separate from the library and correctness
checks. Generated output belongs under the ignored `artifacts/` directory.

## Maintained suites

From the repository root:

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll smoke

pwsh scripts/benchmark.ps1 -Suite Graphs
pwsh scripts/benchmark.ps1 -Suite Transforms
pwsh scripts/benchmark.ps1 -Suite Winding
pwsh scripts/benchmark.ps1 -Suite WindingVsClipper2
```

`smoke` invokes each graph and winding workload method and the transform operations
once, verifies finite outputs, and collects no timings. Correctness tests remain
in [`tests/PolylineKit.Checks`](../tests/PolylineKit.Checks).

| Suite | Operations | Default output |
| --- | --- | --- |
| Graphs | Unsigned graph integral, LIP/GenLIP reconstructions, full and prepared Clipper calls; 16, 64, 256, 1024 vertices, three crossing densities | `artifacts/benchmarks/graphs/` |
| Transforms | Comparison, normalization, similarity fitting and a combined pipeline; 16, 64, 256, 1024 vertices | `artifacts/benchmarks/transforms/` |
| Winding | Winding engine and Clipper comparisons on synthetic paired strokes, dense graphs, random walks, filled regions and degenerate grids | `artifacts/benchmarks/winding/` |
| WindingVsClipper2 | Closed/bridged NonZero area; filled-region full metrics, XOR-only and simple-polygon IoU | `artifacts/benchmarks/windingvsclipper2/` |

The script requires a clean Git tree, records the measured revision, disables
tiered compilation for the process, and runs three independent processes. Each
method has a warmup, iteration calibration and nine batch samples; the winding
summary reports the median of the three process medians and their range. Timing
and per-thread managed allocation counters are unchanged from the archived
runners. `-OutputDirectory <path>` overrides the full output directory.

For one explicitly labelled process, the command-only runner accepts:

```powershell
dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll benchmark-winding artifacts/benchmarks/winding 1 <commit-sha>
dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll summarize-winding artifacts/benchmarks/winding
```

The corresponding commands for the other suites are `benchmark` and
`benchmark-transforms`. The script should normally be preferred because it
records the actual revision and restores the prior environment settings.

LIP, GenLIP and unsigned area use different mathematical definitions. Different
scores do not establish accuracy or superiority. Clipper's fixed precision also
has a different numeric contract from the winding engine. Consult the library
documentation before treating any row as interchangeable with another method.

## Winding versus Clipper2: operation and preparation contracts

The suite pins Clipper2 2.0.0 and records 238 method rows. Its deterministic
generators are in `PolylineKit.Benchmarks/ClipperBenchmarks.cs`; generated inputs,
hashes, assembly hashes, environment, values and nine batches per row accompany
each run. It covers similar strokes, random walks, tangled rings, star regions,
irregular blobs, degenerate integer grids and simple spiky stars. These are
synthetic geometry controls, not recognition benchmarks.

The general families now include 16-vertex inputs. `closed-absolute-winding`
reports four additional Winding-only rows; Clipper fill area is not an equivalent
operation and no speed ratio is assigned to them. The degenerate-grid controls
give both methods exactly the same small integer input geometry (coordinates
0 through 7 are exact in both binary64 and Int64). Intersection/output arithmetic
can still differ. Ordinary double inputs are not silently snapped for Winding;
the conversion-inclusive public wrapper rows show the cost for those callers.

* **WindingArea** measures the entire call, including input preparation. Region
  calls compute all metrics even when the caller consumes only XOR or IoU.
* **Clipper64 static** converts to integer coordinates before timing, then builds
  and executes a fresh engine. Scale is 1e6; Point64's rounding rule is used.
* **Clipper64 reused** retains the engine and output containers, but times
  Clear/Add/Execute. **Clipper64 preloaded** also moves Add outside timing. The
  latter answers repeated queries on unchanged geometry and is not the same
  preparation contract as WindingArea. Clipper's output vertices are still built.
* **ClipperD precision6** starts with PathD inputs. Internal grid conversion is
  timed. Simple own-polygon areas use preconverted integer inputs and are summed
  inside the timed call, so that denominator and intersection share a grid.
* **PolylineKit wrapper** includes conversion and uses the existing broader API.

`overlap-metrics` returns intersection, union, XOR and IoU. Clipper uses two Boolean
operations and obtains XOR from union minus intersection. `xor-only` executes
only Clipper XOR. `simple-iou` executes one intersection and computes the union
from both shoelace areas; it is valid only for these simple region fixtures.
Own shoelace sums are timed. Do not compare different operation rows as equal work.
The summary retains all four value deltas, not just the primary score; Clipper's
grid step is not an area-error guarantee. No claim of a universal fastest method
follows from any of these workloads.

```powershell
dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll smoke-clipper
dotnet benchmarks/PolylineKit.Benchmarks/bin/Release/net10.0/PolylineKit.Benchmarks.dll accuracy-clipper artifacts/benchmarks/windingvsclipper2
```

The smoke verifies repeated execution and a known overlapping-square result for
every method, plus four exact thin triangles; it records no timings. The separate
accuracy command emits 29 quantities: five previously arbitrated integer-grid
disagreements, four thin triangles, three long tilted-strip intersections and
one walk joining two distant unit squares with a retraced bridge.
The tilted strips deliberately retain a known winding precision limitation. Archived
rational expectations and their source are in `fixtures/clipper-accuracy.json`;
all reported implementation values are recomputed. These selected failures do
not establish representative accuracy or general superiority over Clipper.

## First use and retained workspace

```powershell
pwsh scripts/profile-winding.ps1
```

This companion uses the same workload generators and five representative cases:
16/1024-vertex paired strokes, 1024-vertex regions, a 256-vertex degenerate grid,
and a 4096-vertex simple star. Each case runs in three fresh processes, serially.
Input generation, assembly loading and counter setup precede the first measured
area call. That call includes JIT and initial workspace allocation; it does not
measure process startup or input preparation by the caller. Nine later batches
measure warm execution separately. Cold latency is particularly sensitive to
runtime/OS state; the three samples are descriptive, not confidence intervals.

The tool records the retained managed-heap delta after forced collections and an
independent inventory of cached workspace/certificate/predicate arrays. Payload
uses actual managed element sizes, including struct padding, and deduplicates
shared arrays. It excludes array headers, object/delegate fields and native/JIT
memory, so it is not a total-memory or peak-memory estimate. The heap delta may
include runtime caches. Reflection accounting happens after first-call counters
are read, outside every timed interval. Buffer capacities describe one workload
in a fresh process; a long-lived thread can retain larger capacities after growth.

Outputs are under `artifacts/benchmarks/winding-storage/`. The script verifies run,
environment, DLL/input identities, retained payload totals and sample medians
before writing `summary.json`. These are Winding lifecycle measurements, not a
cold-start speed comparison with Clipper.

## Explicit-assembly comparison

`PolylineKit.AssemblyBenchmarks` is an optional developer tool for comparing two
compatible builds of PolylineKit.Winding. It loads the requested DLL before entering the
typed runner; the build-time reference is not copied into its output directory.
A project dependency builds that reference using the selected Debug or Release
configuration on a clean checkout. Supply an
absolute DLL path when invoking it.

The runner uses one fixed input file, [`fixtures/winding.json`](fixtures/winding.json),
plus deterministic generators in `Shared/`. These produce 78 single closed walks
and three independent filled-region pairs for timing. `check` compares the
enabled and disabled simple-path optimization for the 78 walks, with exact-rational
and analytic checks for selected inputs: 424 assertions. The three region pairs
are timed as actual two-input operations, not as a bridged path.

```powershell
$runner = 'benchmarks/PolylineKit.AssemblyBenchmarks/bin/Release/net10.0/PolylineKit.AssemblyBenchmarks.dll'
$currentDll = (Resolve-Path src/PolylineKit.Winding/bin/Release/net10.0/PolylineKit.Winding.dll).Path
dotnet $runner $currentDll benchmarks/fixtures/winding.json check 0 current

# Supply an independently built compatible baseline DLL, preserving that build's revision.
$baselineDll = (Resolve-Path '<baseline-build>/PolylineKit.Winding.dll').Path
$oldTiered = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    for ($run = 1; $run -le 3; $run++) {
        $order = if ($run -eq 2) { @('integrated', 'baseline') } else { @('baseline', 'integrated') }
        foreach ($label in $order) {
            $dll = if ($label -eq 'baseline') { $baselineDll } else { $currentDll }
            dotnet $runner $dll benchmarks/fixtures/winding.json "artifacts/assembly-benchmarks/$label-$run.json" $run $label
            if ($LASTEXITCODE) { throw "Assembly benchmark failed: $label / $run" }
        }
    }
} finally {
    $env:DOTNET_TieredCompilation = $oldTiered
}
python benchmarks/summarize-assembly.py artifacts/assembly-benchmarks
```

Each assembly run records the DLL SHA-256, input hashes, numerical results, runtime,
OS, CPU, allocation samples and five timing batches. Run these processes serially
on an otherwise quiet machine. The standard-library Python summarizer validates
all six files, matching recorded environments, numerical equality, input identity
(including the boundary between two region inputs) and per-process medians before
writing `summary.md` and `summary.json`. Its default directory is
`artifacts/assembly-benchmarks`. It deliberately rejects comparisons whose
numerical outputs differ; such a change needs a separate correctness assessment.
`baseline` and `integrated` are fixed file labels for the reference and candidate,
not a claim that every later comparison evaluates the historical sweep integration.
Do not use Python's `-O` option: the summarizer rejects it to keep validation enabled.
Matching metadata cannot detect other workload interference; control the machine
and build options when attributing a difference to code.

The runner reads an internal workspace diagnostic to identify bypassed, rejected
and accepted certification attempts. That makes it appropriate for compatible
engine builds, not a general benchmark for arbitrary library versions. No
profiling or recognition experiment tools are needed by either maintained runner.

For a mechanical refactor, `dump:<absolute-or-relative-output.json>` in the output
argument writes 120 deterministic operation records without timing: 78 closed
walks and 14 pairs as bridges and both region fill rules. All public result
properties are recorded; doubles use their exact binary64 bit patterns. Compare
these dumps within the same target/runtime to test numerical identity. An older
monolithic DLL needs a runner compiled against that older assembly identity;
the current leaf-bound runner cannot load it as a substitute leaf. The extraction
comparison used the P2-corrected pre-split DLL, not the inaccurate earlier build.

## Fixture provenance and existing evidence

The current [review evidence manifest](winding-review-evidence.json) identifies
the sub-edge correction, 238-row external comparison, first-use/storage profile,
and [real-contour consumer](../examples/AreaChange/RESULTS.md). The earlier
[extraction evidence](winding-evidence.json) remains a historical measurement,
with its original revisions and archive checksum. Neither manifest is a new
measurement merely because documentation changes afterward.

The fixed fixture is the exact Git blob `72f77aff31f1a9eb3288c07b113d429ee38af164`
from commit `00f96248cc404e2d662d9e51c457d811701fa889`, preserved at public tag
[`archive/research-2026-09-24`](https://github.com/bgtnt/polylinekit/tree/archive/research-2026-09-24).
It contains synthetic geometry, not downloaded recognition data. Its 876,853
bytes have SHA-256
`ef253bbdc499a4533beed1a6b638b3a76760c4f00e97b8454cf03b984e6a4f84`.
The machine-readable provenance is in [`fixtures/manifest.json`](fixtures/manifest.json).
Generators remain deterministic; do not retune an optimization against a set
already used as held-out evaluation evidence.

The latest pre-layout [integrated sweep report](https://github.com/bgtnt/polylinekit/blob/archive/research-2026-09-24/docs/winding-integrated-sweep.md)
records complete reproduction details and raw-data links. On that recorded machine,
the new 2048-vertex canyon fixture improved by 10.73x and the large diagonal comb
by 19.97x; the 256-vertex radial star became about 27% slower. All sampled warm
allocations were zero. Those measurements compare a specific baseline and integrated
engine, not this directory relocation, all C# implementations, or all geometries.
The [full archived measurements](https://github.com/bgtnt/polylinekit/tree/archive/research-2026-09-24/results/winding/integrated-sweep)
preserve unfavorable cases and process variation. No new performance claim is
made merely because the runners now live under `benchmarks/`.
