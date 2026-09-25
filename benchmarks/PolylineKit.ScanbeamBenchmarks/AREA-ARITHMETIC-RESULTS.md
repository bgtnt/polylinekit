# Specialized interval area arithmetic results

Measured source: [`5bc86fb238be2ea751aa3429f6eb37045cb2651b`](https://github.com/bgtnt/polylinekit/commit/5bc86fb238be2ea751aa3429f6eb37045cb2651b).
Historical control: [`bc6184ce4b57598ca39999ff50ff279d62db8b2f`](https://github.com/bgtnt/polylinekit/commit/bc6184ce4b57598ca39999ff50ff279d62db8b2f).

Specialized trapezoid arithmetic reduces median complete-query time by
**2.33–3.40%** against the copied prepared control in the same binary.
Warm tables take **19.02–19.18 ms**, versus **19.68–19.74 ms** for that control.
All six same-binary process-median ranges are disjoint in favor of the area
variant. They are descriptive observed ranges, not confidence intervals.
The gain against a freshly rerun historical binary is smaller.
**All six selected competitor gates still fail**: this is a small improvement
within the prototype, not evidence for replacing the shipping Winding engine.

## Bounded change

Both measured sweep methods use copied prepared geometry with ROI, endpoint
cache and scalar-order filtering enabled. Direct prepared access is disabled.
The optional fifth flag `optimizeAreaArithmetic` specializes only two interval
multiplications in trapezoid integration. Additions, operation order, zero/one
identities, outward rounding and final certificates remain unchanged.

The halved interval's lower endpoint can be negative after outward rounding,
even when the true width is nonnegative; the specialization accounts for this.
See the [derivation](AREA-ARITHMETIC-NUMERICS.md) and
[implementation](GuardedDoubleSweep.Area.cs). Eight ordinary non-identity
source-level endpoint-product expressions become four. A JIT may already
eliminate duplicate products: this is not a measured machine-instruction count
or an instruction-level speedup claim. No profiles were collected in this stage.

## Complete-query and preparation costs

The fixed workload has 109 Census rings, 10588 vertices, 2156 directional pairs
and 422 geometry candidates. Each table visits 1078 pairs, of which 211 reach
geometry. The same candidate masks and unrounded coordinates are retained;
Clipper keeps its scale-1e6 quantization. Parsing and I/O are excluded equally.

| Direction | Method | Warm table, ms | Fresh queries, ms | Prepare + one, ms |
|---|---|---:|---:|---:|
| county-zones | Guarded-prepared | 19.7353 | 20.7441 | 22.0998 |
| county-zones | Guarded-area | 19.1826 | 20.2095 | 21.5576 |
| county-zones | Clipper64-reused-data | 12.5085 | 12.8895 | 12.8302 |
| county-zones | Winding-intersection-only | 9.6265 | 9.5620 | 9.7380 |
| district-zones | Guarded-prepared | 19.6844 | 21.0587 | 21.7881 |
| district-zones | Guarded-area | 19.0157 | 20.3903 | 21.2797 |
| district-zones | Clipper64-reused-data | 12.4122 | 12.9176 | 12.6962 |
| district-zones | Winding-intersection-only | 9.0474 | 9.1500 | 9.2039 |

Fresh-query scope prepares every incoming query once per complete table.
Preparation-plus-one charges fresh catalogues/comparator and Guarded/Clipper
workspace growth. Winding's thread-local workspace is warm, so this is not
cold-process latency. Warm area time remains about **1.53x Clipper** and
**1.99–2.10x Winding**.

Every full-query cell requires selected time <=0.8 times Clipper and <=0.9 times
Winding. Both sweep methods fail all six cells, giving twelve failed gate cells.
The [public evidence](../scanbeam-area-evidence.json) contains all 32 measurement
rows, process ranges, allocation medians and twelve gate ratios.

| Method | County preparation, ms | District preparation, ms | Preparation B/op, either direction |
|---|---:|---:|---:|
| Guarded-prepared | 2.2058 | 2.2000 | 1557408 |
| Guarded-area | 2.2269 | 2.1952 | 1557408 |
| Clipper64-reused-data | 0.4051 | 0.4073 | 968888 |
| Winding-intersection-only | 0.1261 | 0.1260 | 10544 |

Preparation is algorithmically unchanged. Both sweep variants retain the same
**1524672 array-element bytes**, excluding headers, shared source inputs,
mutable engine scratch and transient/peak allocation. Both allocate **0 B/op
in every warm-table sample**. Their allocations also match for fresh queries
(568952 / 986024 B) and preparation plus one table (2107600 / 2089144 B).
The change demonstrates no memory saving.

## Historical sensitivity

The frozen historical binary was rerun using its original direct-sweep matrix.
Each series has three fresh processes and **480 samples**, with the fixed
sequence **old1, new1, new2, old2, old3, new3**. No processes overlap and samples
are never pooled. Prepared and competitor method positions stay the same;
the old third method is direct access, while the new third method is area
arithmetic. Each series is summarized with its own measured DLL.

The old copied warm medians are **19.31–19.49 ms**. Thus the new same-binary
control is slower than the historical control, and the area variant's apparent
benefit is smaller against that older binary. For competitor C, normalized
old/area means `(old prepared / old C) / (new area / new C)`; larger than one
means lower relative cost for the area variant.

| Direction | Scope | Old prepared/new prepared | Old prepared/area | Winding-normalized old/area | Clipper-normalized old/area |
|---|---|---:|---:|---:|---:|
| county-zones | warm-table | 0.988 | 1.016 | 1.011 | 1.012 |
| county-zones | warm-zones-fresh-queries | 0.983 | 1.009 | 0.996 | 1.007 |
| county-zones | prepare-plus-one | 0.985 | 1.010 | 1.003 | 1.003 |
| district-zones | warm-table | 0.981 | 1.016 | 1.010 | 1.017 |
| district-zones | warm-zones-fresh-queries | 0.985 | 1.017 | 1.010 | 1.025 |
| district-zones | prepare-plus-one | 0.992 | 1.016 | 1.009 | 1.017 |

The raw old/area ratios show only about **0.9–1.7% less time**. Normalization
can reduce or increase that observed difference; the Winding-normalized county
fresh-query ratio is below one. Historical and area process ranges overlap for
county fresh-query tables; the other five query cells have disjoint ranges.
These sensitivity observations do not isolate
an arithmetic/JIT cost or remove environmental drift. Shipping-library source
is unchanged, but separately built Winding DLL hashes differ and are recorded
in the manifest. No byte-identical competitor-binary claim is made.

## Correctness and evidence validation

All **422 geometry calls are certified**, with no fallback, in both measured
sweep variants. All **2156 pair areas, coverage values, certificate radii and
existing diagnostics match**, including value/radius bits. Historical validation
and prepared metadata are unchanged; normal and no-intrinsics outputs agree.
The area variant retains 25580 interval-X evaluations and 527975 accepted scalar
orders out of 540765 attempts. No topology or certificate tolerance is relaxed.

The test runs passed normally and with hardware intrinsics disabled:

- **6101 exact dyadic arithmetic controls**: 5283 finite enclosures and 818
  rejected operations; generic/specialized endpoints and rejection behavior match.
- **2977 copied-prepared** and **4057 direct-prepared** snapshot/reuse/input and
  all-diagnostic parity controls, including raw queries and exception recovery.
- **51422 exact-rational/analytic geometric assertions** across the four
  ROI/cache combinations.

Arithmetic controls cover signed zero, exact identities, subnormal halves and
products, negative outward lower endpoints, overflow boundaries and repeated
sums. Original-input NTS cross-check budgets remain 1 m² intersection and 1e-8
coverage; all methods satisfy them. NTS is independent, not the exact oracle.

The unchanged positive evidence copy passed. Nine controlled corruptions were
rejected: row, order, median, output, binary hash, filter diagnostic, fallback,
prepared payload and certificate. Rejected submissions retained identical JSON
bytes and produced neither evidence nor summary files.

## Reproduction, limits and decision

.NET 10.0.12; Microsoft Windows 10.0.26200; X64; Intel64 Family 6 Model 158
Stepping 12, GenuineIntel; 16 logical processors. Release build,
`DOTNET_TieredCompilation=0`, fixed orders ABCD/CDAB/BDAC, five calibrated samples
per row and 32 rows per process. Reported values are medians of process medians;
process ranges in the manifest are not confidence intervals. No simultaneous
build, profiler or other benchmark ran. Ordinary OS activity is uncontrolled.

See the [fixed protocol](AREA-ARITHMETIC-PROTOCOL.md) and
[compact evidence](../scanbeam-area-evidence.json) for both independent series,
actual binary/source hashes, sensitivity, compatibility and memory definitions.
From the new measured revision, restore locked and build Release, then run:

```text
dotnet <measured-dll> check-area-arithmetic artifacts/scanbeam-area-check
dotnet <measured-dll> benchmark-area-arithmetic artifacts/scanbeam-area <run 1..3> <revision>
dotnet <measured-dll> summarize-area-arithmetic artifacts/scanbeam-area
```

Use `benchmark-direct-sweep` and `summarize-direct-sweep` with the historical
DLL in a separate directory, retaining the process sequence above. Raw runs
stay local under ignored artifacts. No source or numerical threshold was tuned
after inspecting timings.

Keep the arithmetic variant available for further controlled research, with its
flag **off by default**. Shipping code is unchanged. The observed small gain is
specific to this dataset, hardware and runtime, and remains below the competing
engines' performance. It does not establish a general geometry-library, SIMD
or C++ advantage.

Local archive: `artifacts/scanbeam-area-5bc86fb.zip` (222405 bytes, 28 members).
SHA-256 `b4b2d58668bdc5f0b234a4e2c59031029451bfc8ba5cfbb666181dbefe5c1ceb`.
The archive is local, not publicly hosted.
