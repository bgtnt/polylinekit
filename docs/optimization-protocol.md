# Optimization measurement protocol v1

This protocol is fixed before optimization measurements. The baseline core is
`e6b4978522b3221b36f054024be338b8e3618577`. The harness calls public APIs present in
that revision; it must be identical for the baseline and every candidate. The
experiment measures computational cost, not whether area ranks shapes better than
RMS. No NuGet publication belongs to this experiment.

## Frozen cases

There are 48 cases: the following 12 stages at 16, 64, 256 and 1024 input vertices.
All coordinates are `double`. Fixture generation is scalar code in the harness,
independent of the tested core assembly. Exact coordinates are saved as
`inputs.json`; its SHA-256 must match across all runs and variants. No random
generator, external data or interpolation by a different implementation is used.

| Family | Stage | Role |
| --- | --- | --- |
| Open, exact transformed shape | Normalize both → fit → endpoint-bridged area | Primary |
| Open, perturbed transformed shape | Normalize both → fit → endpoint-bridged area | Primary |
| Increasing-x graphs | Unsigned graph integral | Control |
| Open, exact transformed shape | Normalize moving path | Control |
| Open, exact transformed shape | Apply affine transform | Control |
| Open, exact transformed shape | Similarity fit, 64 samples | Control |
| Open, perturbed transformed shape | Similarity fit, 64 samples | Control |
| Asymmetric closed contours | Similarity fit with phase search, 64 samples | Control |
| Asymmetric closed contours | Similarity fit with phase search, 256 samples | Control |
| Open, exact transformed shape | Raw endpoint-bridged area | Control |
| Open, perturbed transformed shape | Raw endpoint-bridged area | Control |
| Asymmetric closed contours | Filled-region symmetric difference | Control |

For `t=i/(n-1)`, the open reference is
`(3t + 0.5 sin(9t), sin(5t) + t)`, matching the previous transformation benchmark.
The exact moving shape applies scale 2, rotation 0.37 radians, translation (7,-4).
The perturbed source first adds `(0.025 sin(17t), 0.04 sin(13t) + 0.015t)` and then
uses that same transform. This second primary family exercises nonzero residuals
and area after alignment.

Closed references use `theta=2 pi i/n`,
`r=1 + 0.2 cos(3theta) + 0.08 sin(5theta)`, and coordinates
`(1.3r cos(theta) + 0.12 sin(2theta), 0.8r sin(theta))`. The moving contour starts
`n/8` vertices later, then applies scale 1.7, rotation -0.43, translation (3,-2).
There is no duplicated closing point. Phase search examines all sample offsets;
reversal is disabled. The graph control compares `sin(5t)` with
`sin(5t) + 0.05 sin(17t) + 0.01` on the same increasing-x grid.

Public calls use default uniform normalization, positive similarity scale,
64 open samples, NonZero fill and decimal precision 6; diagnostic contours are
disabled. Normalization precision validation and all existing input validation
remain enabled. Primary calls include both normalizations, resampling and fit,
the transformed output snapshot and the final Clipper area. Input construction,
delegate construction, timing infrastructure and serialization are outside timing.
No pre-normalized or cached result substitutes for a primary workflow.

## Variants and execution

Four separately built variants run on the same .NET 10 runtime and hardware:

1. Original `netstandard2.0` core at the baseline revision.
2. Portable cleanup with redundant allocation/copy removal, array specialization and equivalent cyclic-index iteration (same reduction order).
3. The modern `net10.0` scalar core, with explicit SIMD disabled.
4. The same modern core with supported SIMD enabled.

Record actual core target, source SHA, harness SHA and SHA-256 of the loaded core
DLL. Record runtime, OS, processor, process architecture, ISA support, clock
frequency and relevant environment settings. The harness preflight command
records all 48 scalar observations before timing. Compare these with the original
observations, and run the full semantic test suite, before accepting a variant.
The recorded scalar observations supplement tests; they do not prove full output
equivalence. In particular RMS alone cannot prove equality of fitted transforms.

Use `DOTNET_TieredCompilation=0` for every process. Run sequentially, with no
concurrent build, test or benchmark workload. Start a fresh process for each
variant in each of three rounds. Order is deliberately rotated:

| Round | Variant order |
| --- | --- |
| 1 | Original, Cleanup, Modern scalar, SIMD |
| 2 | Cleanup, Modern scalar, SIMD, Original |
| 3 | Modern scalar, SIMD, Original, Cleanup |

The stage order also rotates by one between rounds, identically across variants.
Each case warms up for at least 60 ms, then doubles batch iterations until a
calibration batch lasts at least 20 ms (bounded at 1,048,576 iterations). Measure
nine batches using that count. Full collection and finalizer waits occur outside
each timed batch. `Stopwatch` measures elapsed time;
`GC.GetAllocatedBytesForCurrentThread` measures allocated bytes, not peak memory
or retained memory. Workflows are synchronous. Every result is consumed; each
case's observation is checked before and after measurement for finite,
bit-identical repeatability within that variant/process.

Publish all batches and per-process medians. Compare paired process medians;
do not pool the 27 batches as 27 independent runs. Report the median of three
paired candidate/original ratios, and the range of those three ratios. Also
report cleanup/original, modern-scalar/cleanup and SIMD/modern-scalar ratios so
runtime targeting and SIMD are not credited for cleanup work.

## Decision fixed in advance

For **each** primary family at **both** 256 and 1024 input vertices, accept the
candidate only if the median paired ratio shows at least 20% lower time **or**
at least 30% lower bytes/op versus the remeasured original. Keep the 16/64 results
visible even though they are not primary threshold gates.

A control has a confirmed time regression when all three paired process ratios
are above 1.05. Such a regression fails acceptance, even if the primary gate
passes. If a primary threshold is crossed by some rounds but not others, or a
control median is above 1.05 but not every round is, one additional complete
three-round set is allowed. That set is a confirmation, not a replacement for
unfavorable data. Report both sets. On confirmation, the combined median must
pass the primary thresholds; a control regression is confirmed if each set's
median is above 1.05. If uncertainty remains, do not claim the candidate passed.
Do not keep collecting until a favorable outcome appears.

Correctness is mandatory: existing 1851 checks plus scalar/SIMD equivalence,
degenerate inputs and forced scalar fallback, and Windows/Linux CI. The public
contracts, normalization precision guard, `double` coordinates, Clipper2 and
portable target remain. SIMD must be established with actual packed arithmetic
in JIT disassembly on the available hardware; ISA availability alone is not
evidence of its use. Changes to reduction order need explicit numerical tests.

Only verified improvements may be selected for publication. A bounded negative
result is valid: retain the portable implementation and publish the evidence
instead of expanding the search or weakening the workload and thresholds.
