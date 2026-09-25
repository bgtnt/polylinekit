# Frozen Solaris polygon-overlap evaluation protocol

This protocol is fixed before timings or optimization. The fixture is a provided
Solaris/SpaceNet 2 evaluator sample, not a new production population. No training,
registration, normalization, simplification, resampling or Core engine change.

## Input and semantics

Solaris source `5315390942e05e919555088361bd3df42d4f5a18`; the pinned downloader's
manifest identifies each original CSV, reference source and terms document.
Converted JSON SHA-256:
`883b7d327f1e082a8a34bbe065a40d8fb5a89bddd5d5c1014be5fbcf62fd4b20`.
The conversion is an untimed, one-time step, preserving image XY coordinates,
source row order, confidence, explicit closure and empty sentinels. All supplied
Z values must be zero before removing them. Units are pixels and square pixels.

Use all six images, 145 prediction rows, 172 truth rows and all 172 expected
output keys. The two empty sentinels and tiny truth rows remain in the output
inventory. Original-area filters are truth >=20 and prediction >20; 169 truth
and 144 predictions are eligible. Sort confidence descending. Equal-confidence
source-row order is a deterministic extension: pandas' historical default sort
does not specify a stable tie order, but this fixture has no ties within an image.

For each prediction, find the maximum IoU among still-available intersecting
truths; equal-IoU ties choose the first truth source row. Update only that truth's
retained maximum score, including subthreshold scores. Accept/remove it only
when IoU >0.5. Reset all matching state for every complete run. Filtered and
untouched truth output scores are zero. Empty images have zero metrics. IoU
denominator is areaP+areaG-intersection; zero is an error here, as in the source
division (eligible positive-area valid inputs cannot encounter it).

Invalid geometry is explicitly unsupported by this small example. Solaris
repairs invalid predictions with GEOS buffer(0), but not invalid truths. This
adapter must not silently substitute winding fill or assert NTS repair
equivalence. Valid holes/multipart inputs have explicit NTS fallback in the
specialized backends, reported per call. The pinned fixture has none of these
unsupported cases. Full corpus coverage must be confirmed by runtime validation.

## Shared work and backends

One C# harness owns JSON parsing, validation, cached own areas, candidate checks,
matching and result rows. NTS validates source geometry and supplies common own
areas for all four backends. Bounds reject only strict separation; shared edges
and vertices remain candidates. No IoU-bound threshold shortcut is used because
the output also needs retained scores below the match threshold. For an area-zero
bounds candidate, the same NTS intersects predicate distinguishes touching from
disjoint geometry for all backends. No pair IoU or match result is cached.

- Core: `PolylineArea.IntersectionArea`, NonZero, original double coordinates.
- Clipper2 2.0.0: NonZero intersection and signed output area; reusable input data,
  engine and output containers. Scale **1e10**, nearest-away-from-zero conversion,
  scaled coordinates limited to ±1e14. The grid is backend-specific; Core input
  is unchanged. Common cached own areas remain on original geometry for every backend.
- NetTopologySuite 2.6.0: reused validated input geometries, public intersection
  and area. No unnecessary union geometry is built by any backend.
- Convex: Sutherland–Hodgman only when both operands are independently certified
  valid simple convex shells; explicit NTS fallback otherwise. Kernel subset
  results separate eligible work from fallback work.

These are example-only dependencies. The whole evaluator has NTS and Clipper2
dependencies even though Core has no third-party runtime dependencies.

## Correctness before timing

Compare the full key sets, row counts and retained per-building scores to the
immutable expected CSV conversion: absolute difference **<1e-9**, unchanged.
Require identical match decisions and TP/FP/FN across C# backends. Report the
smallest observed distance to the acceptance threshold. The historic Python
environment is not recreated; the implementation follows its pinned sources.

Compare each visited bounds candidate against NTS, including reversal, nonnegative
intersection and intersection <=min(own areas). Area tolerance in square pixels
is **1e-8 + 1e-11*max(areaP,areaG)**; IoU tolerance is **1e-9**. This is a diagnostic
acceptance criterion for this bounded fixture, not a universal error guarantee.
Do not clamp results, change expected values or increase tolerances after a failure.
Analytic/protocol controls cover touching/disjoint/zero area, threshold equality,
asymmetric area filters, removal, retained subthreshold scores, ties and empty images.

## Measurement boundaries

Use Release .NET 10 from a clean committed source. Freeze the complete output
directory; record exact harness/Core/Clipper/NTS DLL hashes and embedded revisions,
fixture hash, SDK/runtime, OS, CPU, hardware flags and environment settings.
Three sequential fresh processes, `DOTNET_TieredCompilation=0`; no competing builds
or benchmarks. Rotate the four-backend order by process index. These are warmed
steady-state measurements, not cold process latency or independent new datasets.

Each cell warms for 40 ms, calibrates a power-of-two count to >=20 ms (cap 65536),
then records five samples of elapsed ticks and thread-allocated bytes. Read the
allocation counter before constructing sample records. Consume numeric outputs.
Report median of three process medians and the process range, not confidence intervals.

1. **Geometry batch:** already-read and prepared inputs; use the exact bounds
   candidate schedule encountered by the reference matching run, recompute all
   intersections and needed arithmetic on every invocation. No matching or JSON
   work. Count candidates, bounds false positives, positive intersections and
   fallback. Report all, both-convex, has-concavity, positive, zero and combined
   vertex-count bands <=32, 33–64, >64. Strata overlap; do not add their times.
2. **Preparation:** the already-deserialized corpus; construct fresh backend,
   geometries, validation, cached areas, backend inputs and per-image order.
   No pair computation. Report separately from warm geometry.
3. **Complete evaluator:** read the same JSON file through the warm OS cache,
   deserialize, validate, prepare fresh backend data, compute own areas, select
   candidates, intersect, match and produce all result rows. Excludes the one-time
   CSV/WKT conversion, final JSON serialization and disk write for all backends.
   No prepared inputs or results survive between these invocations (Core may
   reuse its documented thread-local workspace, as during normal warm calls).

No predeclared speed-win threshold or timing gate in CI. Retain unfavorable
results. CI checks reference/protocol/geometry correctness and benchmark startup,
not shared-runner speed. The summarizer validates file identities, process order,
row/sample matrix, sample counts, checksums and median arithmetic before reporting.
