# Dynamic indexes during vertex removal

This bounded experiment compares index maintenance while an illustrative closed
polyline simplifier replaces AB and BC with AC. The requested operation is to
find **all active segment boxes** that overlap the shortcut box, followed by the
same robust intersection checks. It tests whether specializing index storage for
this update pattern is worthwhile. It is not a production simplifier or an area
engine rewrite.

The [protocol](PROTOCOL.md) was fixed before computing removal outcomes or timing.
All 109 previously frozen Census rings and six synthetic rings participate.
Eligibility is a fixed local distance test, with three passes in original vertex
order. Rejected proposals leave the index unchanged. Identical candidate sets,
accepted removals, predicate counts and final rings are required for every method.
The final simple-ring checks do not imply a cumulative shape-error guarantee.

## Implementations

| Method | State and updates |
|---|---|
| `linear` | Original edge slots and active flags; scans the original slot range. |
| `dense-linear` | Scans only a dense vector of active IDs; removes by swapping with the last ID. This tests an inexpensive alternative as the ring shrinks. |
| `fixed-slots` | Independently written binary hierarchy over the original outgoing-edge slots; refits one leaf, deactivates another and updates their ancestor paths. No insertion, node allocation, rotations or balancing during updates. |
| Optional local plugin | Implements the same benchmark-only contract. The private RtTools baseline uses real dynamic deletion, condensation/reinsertion and insertion; its source is not part of this repository. |

The custom tree's grouping follows the original edge order. It is a hypothesis
about spatial coherence along a polyline, not a generally optimal R-tree layout.
Every query preserves boundary contacts and zero-extent boxes. Stable edge IDs
and exact old-bound snapshots prevent mutation from leaving stale entries.

The optional RtTools adapter uses a generic closed-rectangle wrapper, its callback
query, a retained scratch buffer and a copy into the common output span. Those
adapter costs are included. A private plugin result is a **local comparison**, not
a publicly reproducible third-party ranking. No private source or DLL is shipped.
The library projects gain no dependency; this executable uses the already pinned
NTS 2.6.0 for shared predicates and independent validity checks.

## Run and reproduce

```powershell
dotnet restore PolylineKit.slnx --locked-mode
dotnet build benchmarks/PolylineKit.DynamicIndexBenchmarks -c Release --no-restore
$runner = 'benchmarks/PolylineKit.DynamicIndexBenchmarks/bin/Release/net10.0/PolylineKit.DynamicIndexBenchmarks.dll'
dotnet $runner check
dotnet $runner run artifacts/dynamic-traces
$env:DOTNET_TieredCompilation = '0'
$revision = git rev-parse HEAD
foreach ($run in 1..3) {
    dotnet $runner benchmark artifacts/dynamic-index $run $revision
    if ($LASTEXITCODE) { throw "Dynamic benchmark failed: $run" }
}
dotnet $runner summarize artifacts/dynamic-index
```

With an authorized local adapter, append `--plugin <absolute-adapter-DLL>` to each
command. The DLL must export one `IEdgeIndexFactory`; its dependencies resolve
from its dependency manifest or directory. Only load trusted local code.
No plugin is used by public CI. Built-in tests include empty indexes, contacts,
stale updates, inactive slots, non-power-of-two trees and randomized refits.

`run` preserves actual points, candidate IDs, accepted changes and final contours.
`check` compares exact traces, not just timing digests. The summarizer demands
matching assemblies and data, regenerates the expected outputs for every scope,
and verifies raw samples, medians and the complete measurement matrix. Preserve
the measured binaries to re-summarize a run; build identity can change at a later
commit. All workload and trace hashes use actual binary64 inputs, including the
runtime's synthetic sine/cosine results.

## Measurement contract

- `build`: construct the index from already prepared edge bounds.
- `build-and-index-replay`: construct a fresh index, then perform the complete
  recorded query/update sequence, without narrow-phase geometry or decisions.
- `complete-simplification`: allocate fresh simulation/index state and execute
  scheduling, distance tests, all candidate predicates and accepted updates.

Census groups report one whole batch (98 counties or 11 districts); each synthetic
row is one complete ring. Construction is included in replay and complete sessions;
the original index is never reused after its destructive sequence. Workload
loading, certification, trace generation, final contour allocation/validation and
candidate sorting for checks are outside timing. Every timed candidate ID and
accepted change contributes to a digest, so no cached query result replaces work.

Three fresh sequential processes rotate method order across groups and runs, with
five calibrated batches per row. Managed allocation is measured before creating
sample records. Warm query/update zero allocation does not mean construction is
free or retained memory is zero. All candidates are enumerated and tested; an
early-exit visitor simplifier would be a different measured contract. Descriptive
ranges are process medians, not confidence intervals or a universal index ranking.
