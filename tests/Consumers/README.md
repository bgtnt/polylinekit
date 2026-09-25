# Consumer and binary compatibility checks

Run `pwsh scripts/verify-consumers.ps1` from the repository root. The script builds
the current consumers, reconstructs commit
`969f8acc1c0f77c52654e81ace951367bf9b0743` with `git archive`, and builds its
monolithic `PolylineKit.dll`. Git history must contain that commit (`fetch-depth: 0`
in CI). An existing monolithic build can instead be supplied with
`-LegacyAssembly <absolute-path-to-PolylineKit.dll>`.

* `LeafOnly` is a .NET 10 executable referencing only `PolylineKit.Winding`.
  It checks analytic areas and rejects unexpected runtime references.
* `PortableOnly` compiles a .NET Standard 2.0 class library referencing only
  `PolylineKit.Winding`. It demonstrates the portable compile-time surface; it
  is not a claim that CI ran .NET Framework or another .NET Standard runtime.
* `LegacyApiConsumer` compiles **once against the old monolithic DLL**, using all
  six extracted public types and passing its old `Point2[]` to the forwarded
  `PolylineArea.BetweenGraphs` method.
* `CompatibilityHost` supplies the new dependency graph, then loads and executes
  that unchanged legacy component against the modern and portable parent/leaf
  DLL pairs. The script verifies the component's SHA256 remains unchanged.

The binary compatibility claim is for the precompiled consumer component. An
existing executable's deployment manifest must also include the newly required
`PolylineKit.Winding.dll`; copying only a replacement `PolylineKit.dll` beside an
old `.deps.json` is not what this test validates. No binary fixture is checked in.

The ordinary solution includes `LeafOnly`, `PortableOnly` and `CompatibilityHost`.
`LegacyApiConsumer` is built by the script because its explicit old-assembly path
is intentionally unavailable during an ordinary solution build. It has no
project reference to the current libraries.

Build outputs, reconstructed source, SHA256 evidence and per-target runtime
outputs stay in a uniquely named, ignored `artifacts/consumer-verification-*`
directory. These checks use project references and explicit assembly references;
they do not pack, publish or consume a NuGet package.
