# Public FilledArea integration protocol

Freeze this protocol, the implementation and all 34 existing `HybridInputs` before timing.
This is a public API integration check of the measured hybrid v3, not an unseen workload
study. The 9 development, 13 originally held-out, 6 confirmation and 6 confirmation-v3
inputs have all been inspected in prior stages. Their coordinates and 68 input/fill cells
remain unchanged. Do not tune the selector, fixtures or gates in response to these timings.

## Operation and implementations

Each call measures one implicitly closed walk's NonZero or EvenOdd filled area. All methods
receive the same immutable coordinates. Input copying, validation and dispatch performed by
the called API belong to its measured cost. Warm instances/buffers are allowed throughout.
The six methods, in this base order, are:

1. `Public-array`: `WindingArea.FilledArea(points, rule)` on the supplied array.
2. `Public-list`: the same public call with a precreated `Array.AsReadOnly(points)` wrapper.
   The wrapper is created outside timing; this interface input uses general Winding.
3. `Hybrid-v3`: retained experimental `HybridClosedArea`, in the same measured binary.
4. `Winding`: `WindingArea.ClosedPath(points)` projected to the requested fill. This existing
   API computes four integrals and diagnostics; the specialized sweep computes only one
   area, so this is an API/workload comparison rather than equal internal work.
5. `Clipper-full-input`: the established adapter with coordinate conversion, `Clear`,
   `AddSubject`, union execution and output area all included.
6. `Clipper-preloaded`: conversion and `AddSubject` outside timing; execution/output area
   inside. This stronger secondary comparator is reported, without a primary speed gate.

The public array specialization uses the unchanged v3 coordinate-based selector and a
bounded single-loop Int64 extraction. It accepts exact integer coordinates in ±2048,
64..1024 supplied vertices, with the frozen few-level/density/sample heuristics. No input
rounding, translation, scaling or name/family-based dispatch is introduced. The public list
path and unsupported array shapes retain Winding. `.NET Standard 2.0` uses Winding only;
these measurements concern the `.NET 10` build.

Clipper uses scale 1e6 with the existing conversion's truncation toward zero; generated
crossings are quantized. Its numeric contract therefore differs. Preserve the known
negative Clipper filled-area result without clamping; it cannot satisfy a target gate.
The root cause of that result is not established by this experiment.

## Correctness before timing

Validate all 68 cells with immutable input hashes and repeated calls:

- Public array result is bit-identical to retained hybrid v3.
- Public list result is bit-identical to projected Winding.
- The public selector agrees with v3 on all fixtures and the existing 1315 selector controls.
- All own results are finite and nonnegative; differences from Winding stay within the
  established `1e-10 * max(1, abs(Winding))` comparison tolerance.
- Both Clipper preparation variants return identical bits; retain their numerical errors.

Run the public correctness checks normally and with `DOTNET_EnableHWIntrinsic=0`.
Public API boundary/reentrancy tests and unchanged legacy geometry checks are separate
correctness checks, not additional timed workloads.

## Measurement and gates

Build Release from a committed source revision and freeze its binaries. Check the embedded
commit and SHA-256 of the harness, Winding library and Clipper assembly. Launch three fresh,
sequential processes with `DOTNET_TieredCompilation=0`, without competing builds or timing.
Reuse the established harness: 40 ms warmup, five samples using an iteration count calibrated
to at least 20 ms (or the 1,048,576 iteration cap),
time and thread allocation counters, and a consumed result. Rotate each cell's method order
by offsets 0, 2 and 4 in runs 1, 2 and 3. This yields 408 rows/process and 6120 raw samples.

Aggregate with the median of three process medians; report all observed process ranges and
warm B/op. Ranges are not confidence intervals. Gates apply independently to every cell:

- Dense-grid and retraced families: Public-array ≤ 0.8 × Winding and ≤ 0.8 × full-input
  Clipper, with a nonnegative Clipper result.
- Every other family: Public-array ≤ 1.10 × Winding. No tiny-call exception.
- Every cell: Public-array ≤ 1.10 × retained Hybrid-v3 (integration overhead gate).

Overall success requires all gates. Report Public-array/Hybrid-v3 on every cell even when
the preservation/target gate passes. Public-list is measured for context and correctness;
there is no speed claim or target gate for arbitrary interface implementations.
First-call allocation, retained workspace memory, nested calls and buffer growth are not
represented by warm B/op and must remain documented separately.

## Audit and reproducibility

Commands from the repository root, using the frozen measured binary:

```powershell
dotnet <frozen>/PolylineKit.ScanbeamBenchmarks.dll check-filled-area artifacts/filled-area-check
$env:DOTNET_EnableHWIntrinsic = '0'
dotnet <frozen>/PolylineKit.ScanbeamBenchmarks.dll check-filled-area artifacts/filled-area-nohw-check
Remove-Item Env:DOTNET_EnableHWIntrinsic
./artifacts/run-filled-area.ps1 -Revision <full-measured-commit>
python artifacts/check-filled-area-evidence.py --runner <frozen>/PolylineKit.ScanbeamBenchmarks.dll
python artifacts/publish-filled-area.py
```

The summarizer recomputes every method output and validates exact inputs, route, binary/source
identity, complete row order, all samples and medians before writing derived evidence. Negative
controls cover missing/reordered rows, modified medians, iteration counts and outputs (including Public-list),
binary/source hashes, input coordinates, route and Clipper validity. An unchanged control must
pass. Rejections must neither mutate submitted JSON nor produce derived output.

An independent script audits all raw rows, aggregates, gates, input coordinate hashes and
sequential launch times. Publish `benchmarks/filled-area-evidence.json`; keep the full raw
archive at `artifacts/filled-area-<commit>.zip`, recording its hash and explicitly marking it
local unless separately hosted. Preserve all prior hybrid evidence.
