# Active sweep passes: frozen protocol

Compare the current coalesced-gap sweep with an optional seventh constructor
flag, `optimizeActivePasses`, default false. Both use copied prepared geometry,
common-Y restriction, endpoint caching, scalar order filtering, specialized
area arithmetic and gap coalescing. Shipping code and public APIs are unchanged.

Build the top-order copy incrementally during the same insertion sort. Bands
without crossings stream winding values and contributions in one pass without
prefix/gap-start initialization or a redundant top-order verification. Crossing
bands initialize prefixes and starts together before the original event playback
and retain full verification before final gap emission. Do not change comparator,
crossing construction or numerical integration order. See the accompanying
[invariants](ACTIVE-PASSES-NUMERICS.md).

Calls certified by both modes must retain exact result/error-bound bits and geometry
diagnostics. Work/active-visit counters intentionally count fewer visits; the
unchanged work budget can therefore admit formerly budget-limited inputs. No
numerical threshold, topology guard or fallback contract is relaxed. New pass
counters distinguish crossing bands, initialization, top-order writes and
verification. They are instrumentation costs shared by both measured variants.

Before timing, run independent exact-area controls, state-reuse/fallback cases,
raw/copied/direct preparation, both fills and orientation variants. Run existing
rational and prepared suites normally and with hardware intrinsics disabled.
Require all 422 original-coordinate frozen candidates to certify in both modes,
with bit-identical outputs/certificates and unchanged gap/filter/cache counters.
Also compare the baseline with the prior frozen binary and require legacy
validation JSON to remain unchanged. Test summarizer rejection of corrupted
rows, hashes, outputs, certificates and the new pass counters.

## Measurements and decision

Keep 109 Census rings, 10,588 vertices, 2,156 directional pairs and 422 candidate
calls. Clipper uses scale 1e6. NTS checks retain 1 m²/1e-8 coverage tolerances;
NTS is not an exact oracle. Both directions use prepare, warm-table,
warm-zones-fresh-queries and prepare-plus-one scopes. Fresh queries are prepared
once per table. Prepare-plus-one creates a comparator and grows scratch;
Winding's thread-local scratch is warm, not a cold process.

Methods A-D are Winding-intersection-only, Guarded-gaps, Guarded-active and
Clipper64-reused-data. Use three fresh sequential Release processes, five
calibrated samples per row, 32 rows per process, DOTNET_TieredCompilation=0,
and orders ABCD/CDAB/BDAC. No concurrent builds, profilers or timing workloads.
Report median of process medians, observed process ranges and allocations;
ranges are descriptive, not confidence intervals. No post-timing tuning.

Rerun frozen commit `4b8a131117264ec950a373bfae5462edd216b2c5` with its unchanged
gap-coalescing matrix. Sequence old1/new1/new2/old2/old3/new3. Keep these two
480-sample series separate: historical Guarded-gaps is method C and the current
baseline is B. Report historical sensitivity without claiming it removes
environment or code-generation drift.

The original competitor gate remains: every selected complete-query cell must
take at most 0.8 times Clipper and 0.9 times Winding. A prototype improvement
alone does not justify replacing shipping Winding. Publish measured commits,
commands, hashes, environment, counters and compact evidence; raw files remain
local. There are no new arrays; account for engine scalar fields separately
from existing retained scratch and immutable preparation.

```text
check-active-passes artifacts/scanbeam-active-check
benchmark-active-passes artifacts/scanbeam-active <run 1..3> <revision>
summarize-active-passes artifacts/scanbeam-active
```

Use benchmark-gap-coalescing/summarize-gap-coalescing for the old binary in
artifacts/scanbeam-active-historical. CI runs correctness checks without timings.
