# Follow-up to the independent review of 33e17fe

The owner supplied an independent review of [`33e17fe404b9090eb33aefffc1c2acc7ea72018b`](https://github.com/bgtnt/polylinekit/commit/33e17fe404b9090eb33aefffc1c2acc7ea72018b). Its concrete findings were checked against the later implementation, rather than treating its proposed continuation as a replacement specification.

| Finding | Disposition |
| --- | --- |
| GenLIP admits collinear self-overlap/retracing | Correct the experimental admission checks and add focused contact/overlap regressions. |
| Zero/subdivision claims overstate floating-point behavior | Qualify XML/design text and retain the exact counterexample as a numerical-limit regression. |
| Normalization and a compiling workflow are missing | Already delivered in `6d12758`/`2a19c11`: explicit bounds normalization, transforms, comparison helpers, sampled similarity alignment and runnable examples. |
| Legacy provenance lacks concrete decisions | Add the inspection-to-design decision table in [design.md](design.md#decisions-from-the-legacy-inspection), without copying private source or fixtures. |
| Benchmark cleanliness ignores untracked build inputs | Reject tracked changes and non-ignored untracked files before building/stamping HEAD; fail closed if Git inspection fails. |

## GenLIP guard correction

The review's exact input was:

```text
P = [(0,0), (2,0), (0,0), (3,0)]
Q = [(0,2), (2,2.5), (0,2), (3,2.75)]
```

The previous baseline returned score `7.125`, one good group and no bad pairs, despite retraced component paths. The corrected experimental reconstruction rejects such self-overlapping input. Shared adjacent endpoints and forward collinear subdivisions remain valid; positive-length overlap is different from a single point of incidence. Connector checks use the same distinction, including the first examined pair.

Whole-route simplicity admission is a **conservative limit of this reconstruction**, including routes that could otherwise be partitioned into bad pairs. It is not a claim that the publication forbids every globally nonsimple route. Strict increasing-x paths are simple by construction and retain the linear graph path; other paths require quadratic pairwise validation. The existing nonparallel intersection routine used by the polygon oracle is not silently redefined. Predicates still use ordinary double arithmetic, without a robust exact-predicate guarantee.

The decisive near-touch inputs and existing benchmark fixtures are increasing-x graphs. The geometric formulas and their recorded source revisions remain unchanged. Historical timing records are not relabeled as measurements of this revision.

## Numerical limit retained explicitly

For `m=2^53`, paths `[(-m,1),(m,-1)]` and `[(-m,1),(1,-1/m),(m,-1)]` describe the same exact line using exactly representable input values. The current graph integral returns **1** instead of mathematical zero. This is intermediate interpolation error accumulated over a wide interval, not input quantization or a geometry difference.

`NumericReviewChecks` retains the case, prints its observed result and checks the case-specific absolute error ceiling of 1, symmetry and same-list identity. An improvement below that ceiling is welcome. This is not a universal numerical error guarantee. No output clamp or unexplained epsilon was introduced.

Nearest-endpoint interpolation fixes this particular example but can make the exact collinear input `[(0,100),(33,1)]` with an inserted `(24,28)` nonzero. Midpoint interpolation has similar tradeoffs. We therefore keep the measured arithmetic for this bounded correction and document its limits, rather than claiming a universally more accurate replacement.

## Scope and reproduction

The current core targets .NET Standard 2.0; the harness/examples target .NET 10. Clipper2 is now the one declared runtime dependency for the separately named general fill comparisons. The review's zero-runtime-dependency statement applies to the earlier graph-only revision. Existing user authorization for general methods remains in force; the review does not remove those methods or require a new geometry engine.

```sh
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
dotnet run --project experiments/PolylineKit.Experiments -c Release --no-build -- check
dotnet run --project examples/Basic -c Release --no-build
```

The benchmark guard covers Git-visible tracked and non-ignored untracked changes. It is not a hermetic-build guarantee for ignored custom inputs or external MSBuild imports. Reproduce published timings from a clean checkout; place scratch output under ignored `artifacts/`.

Validation for this iteration: Release build with zero warnings/errors and **1851 passing console checks** (the previous 1751 plus 96 GenLIP admission/contact checks and 4 numeric-limit checks). The example also remains executable. Four isolated Git-fixture scenarios exercised the actual benchmark script: clean source reaches the build step, an untracked C# file is rejected, a tracked modification is rejected, and ignored output is permitted. A sentinel replaced `dotnet` in that guard probe; it did not execute or fabricate performance measurements.

NuGet packaging and publication remain deferred at the owner's request. No new package-consumption claim is made for this iteration.
