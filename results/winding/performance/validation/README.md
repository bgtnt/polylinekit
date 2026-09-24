# Validation evidence

`build.txt`, `implementation-checks.txt` and `recognition-checks.txt` are the
final Release build and repository check logs at `6fc1c35`. Reproduce with the
commands in [the assessment](../../../../docs/winding-performance.md#reproduction).

The following are supplemental independent-review snapshots, not new public APIs
or test projects included in the solution:

- `EngineProbe.cs.txt` and `independent-engine.jsonl`: comparison of `97d1a97`
  against `9fff4d2`, slab oracles, analytic extremes and nested input access.
  The probe uses a separate load context for the baseline. To run its source in
  a .NET 10 console project, reference the candidate assembly, link `Geometry.cs`,
  `ContourSweep.cs` and `RegionSweep.cs` from `experiments/PolylineKit.Experiments`,
  and set the baseline DLL path near the start of the source to your checkout.
- `PredicateProbe.cs.txt`, `BaselineRobustOrientation.cs.txt` and
  `predicate-{modern,no-intrinsics,portable}.txt`: independent BigInteger signs,
  baseline expansion values and crossing parameters for 860,000 cases per mode.
  The renamed baseline class is from `9fff4d2`. Compile both source files in a
  .NET 10 console project referencing the candidate assembly. The modern run
  used the FMA experiment; the other runs exercised the scalar arithmetic retained
  in the final implementation. Disable intrinsics with `DOTNET_EnableHWIntrinsic=0`;
  for the portable run substitute the library's netstandard2.0 assembly in the
  probe output. The printed hardware header alone does not identify the loaded
  library target.

Archived `.cs.txt` files must be copied/renamed to `.cs` to compile them. These
supplemental probes are snapshots from review, while the committed
`WindingAreaChecks` are the maintained regression suite. Local paths in the logs
identify the isolated checkouts used for measurement.
