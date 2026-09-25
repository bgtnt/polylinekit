# Contributing

## Build and check

Install the .NET 10 SDK and PowerShell, then run from the repository root:

```sh
dotnet restore PolylineKit.slnx --locked-mode
dotnet build PolylineKit.slnx -c Release --no-restore
pwsh -File scripts/verify-implementations.ps1
pwsh -File scripts/verify-consumers.ps1
dotnet run --project examples/Basic -c Release --no-build
```

The correctness suite is a deterministic console executable, not a `dotnet test`
project. Verification checks the portable target, modern target, forced scalar,
no AVX and no hardware intrinsics. CI runs on Windows and Linux. Consumer checks
exercise an unchanged component built against a historical assembly; keep full
Git history available for that check.

## Repository layout

| Directory | Contents |
|---|---|
| `src/PolylineKit.Winding` | Dependency-free area methods, coordinate/fill types and internal engines |
| `src/PolylineKit` | Transformation, alignment, sampling and contour-producing methods |
| `examples` | Runnable consumers and attributed data |
| `tests` | Correctness, numerical and compatibility checks |
| `benchmarks` | Maintained performance runners and fixtures |
| `docs` | Usage contracts and implementation documentation |
| `scripts` | Verification and measurement commands |

The area assembly retains its `PolylineKit.Winding` identity for binary
compatibility. `PolylineArea` is the developer-facing area API. The complete
assembly forwards moved types. Deploy using project/dependency resolution;
replacing one old DLL without its dependencies is insufficient.

## Changes and performance

Document input validation, units, fill rules, transformations and precision
limits. Preserve existing APIs and previously compiled consumers. New dependencies
need a concrete benefit; tooling dependencies must stay out of runtime projects.
Package publication is a separate release action.

Use analytic geometry or independent oracles for numeric changes. Measure the
same input and requested output, including preparation when the application must
pay for it. Warm allocation counts exclude first use, growth and retained memory.
Put raw output under ignored `artifacts/`. See [benchmark commands](benchmarks/README.md).

## Historical studies

Earlier investigations remain reproducible in immutable public snapshots, with
their original paths and unfavorable results intact:

- [Development snapshot at `5ef33e8`](https://github.com/bgtnt/polylinekit/tree/5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3): sweep/index alternatives, filled-area integration, protocols and evidence.
- [Research snapshot at `00f9624`](https://github.com/bgtnt/polylinekit/tree/00f96248cc404e2d662d9e51c457d811701fa889): LIP/GenLIP, recognition, native probes and earlier optimizations.

To reproduce a study in a separate checkout:

```sh
git clone --branch archive/development-2026-09-25 https://github.com/bgtnt/polylinekit.git polylinekit-development
```

That tag points to `5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3`. Follow the report
there; historical timings are not claims about every later build. Some raw
archives were local-only, as marked in their evidence. Archiving changes current
browsing/build scope, not historical Git object size.
