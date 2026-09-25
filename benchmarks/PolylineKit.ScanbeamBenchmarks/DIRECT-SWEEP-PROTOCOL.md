# Direct prepared-edge access experiment

Declared before timings. The prepared sweep at `57c519c` spends 20.3–20.5 ms
per warm table, still slower than Winding and Clipper. Its profile attributes a
substantial inclusive sampled share to copying prepared edge records. This
experiment tests direct reads from the two immutable per-path arrays instead.
It changes no shipping-library API or runtime dependency.

## Bounded implementation and correctness contract

Add an optional `directPreparedEdges` constructor flag, default off. Prepared
queries may borrow each snapshot's original Edge/scalar-bound arrays. Map the
existing global edge ID using the first path's nonhorizontal edge count, and
derive its first/second winding role without rewriting the immutable record.
Read via safe ref-readonly access; do not mutate or expose the source arrays.

Keep prepared snapshot construction, endpoint merging/order, active status,
crossing construction, fill rules, scalar filter, area expressions, all work
and error budgets, and original-coordinate fallback unchanged. Retain the same
workspace allocation strategy, including the now-unused copied geometry buffers,
to isolate direct access from a scratch-allocation redesign. Keep the copied
prepared mode and ordinary raw-input overload available on the same engine.
Borrowed references must be cleared on every exit, including fallback exceptions;
subsequent queries must not retain or read the previous pair's borrowed arrays.

Reuse prepared controls across all eight ROI/cache/filter combinations, both
fill rules, swapped roles, repeated/same snapshots and independent engines.
Add alternating raw/prepared queries, unequal nonhorizontal edge/vertex counts,
workspace growth, and fallback/exception recovery to expose stale bindings and
offset errors. Compare value/radius bits and every existing diagnostic with the
copied path. Run the exact-rational/analytic area suite in direct mode, normally
and with hardware intrinsics disabled. Preserve historical command outputs.

The prepared geometry and its payload must remain identical to `57c519c`.
This removes per-pair Edge/scalar array copying, not every copy in the algorithm:
endpoints and active status still use query-local scratch.

## Workload, costs and acceptance

Use the same frozen inputs and four scopes as
[PREPARED-SWEEP-PROTOCOL.md](PREPARED-SWEEP-PROTOCOL.md): 109 original Census rings,
10588 vertices, 2156 directional pairs and 422 geometry candidates. Preserve the
outer candidate masks, original NTS budgets (1 m² intersection, 1e-8 coverage),
Clipper's scale-1e6 quantization and unrounded double inputs. All direct/copied
results, coverage, certificate radii, fallback states and diagnostics must match.

Methods A-D:

- A: `Winding-intersection-only`.
- B: `Guarded-prepared`, copied geometry with ROI/cache/scalar filter enabled.
- C: `Guarded-direct`, the same flags with direct prepared geometry access.
- D: `Clipper64-reused-data`.

Both directions use `prepare`, `warm-table`, `warm-zones-fresh-queries`, and
`prepare-plus-one`. Fresh-query scope prepares each incoming query once per
complete table, within timing. Preparation-plus-one constructs fresh catalogues
and comparator, charging its Guarded/Clipper workspace growth. Winding's
thread-local workspace remains warmed; this is not cold-process latency.

Three fresh processes, five calibrated samples per row, 32 rows per process:
480 samples. Use Release and `DOTNET_TieredCompilation=0`. Fixed method orders
`ABCD`, `CDAB`, `BDAC` retain the preceding ordering scheme. No simultaneous
build, profiler or second benchmark. Report all allocation samples, medians of
process medians and process ranges. The retained array-element payload excludes
headers, shared source catalogue and engine scratch; do not call it total memory.

Keep each of the six full-query competitor gates: direct time <=0.8 times
Clipper and <=0.9 times Winding, with correctness required. Report all twelve
copied/direct cells. A gain against the copied prototype alone does not justify
replacing Winding. Do not tune bounds or thresholds after seeing timings.

## Historical-binary sensitivity

Shared accessors can change the copied mode's machine code too. Separately rerun
the frozen `57c519c` binary with its unchanged `benchmark-prepared-sweep` matrix.
Use three fresh processes and its own existing orders/scopes. Predeclared process
sequence: **old1, new1, new2, old2, old3, new3**. No processes overlap.

Summarize each series with its own measured binary; never combine their samples.
The historical matrix has filtered/copied modes, while the new matrix has
copied/direct modes. Their method positions therefore differ, with the original
fixed orders retained in each. Report the copied-old/copied-new time ratio and
separate ratios normalized by each series' Winding and Clipper times. These are
sensitivity observations, not proof that normalization removes drift or isolates
an accessor cost. Also compare direct-new against copied-old so a worsened
contemporary baseline cannot alone support an optimization claim.

## Evidence and reproduction

The summarizer validates ordered matrices, finite samples, medians, recomputed
digests, exact pair diagnostics, prepared payload and source/binary hashes before
writing evidence. An unchanged positive copy must pass. Corrupt row/order/median/
output/hash/diagnostic/fallback/payload controls must fail without rewriting their
submitted JSON or creating a summary. Raw samples and traces stay ignored;
publish compact evidence, commands, environment and both measured commit hashes.

Optional profiles run separately after timings on the new measured binary.
They include warmup TraverseReal calls and exclude catalogue preparation outside
that traversal. Sampled thread-time/inlining limitations still apply.

From each recorded revision, restore locked and build Release. The runner is
`benchmarks/PolylineKit.ScanbeamBenchmarks/bin/Release/net10.0/PolylineKit.ScanbeamBenchmarks.dll`.

```text
check-direct-sweep artifacts/direct-sweep-check
benchmark-direct-sweep artifacts/direct-sweep <run 1..3> <new-revision>
summarize-direct-sweep artifacts/direct-sweep
```

For the old binary use `benchmark-prepared-sweep` and `summarize-prepared-sweep`
with a separate `artifacts/direct-sweep-historical` directory and revision
`57c519cba31c039ef3dd7f254665d8c26a8716e2`. Follow the process sequence above with
tiering disabled. CI runs correctness only; no new C++ or SIMD claim is made.
