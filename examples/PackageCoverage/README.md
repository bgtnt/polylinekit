# Coverage with the NuGet package

This executable uses `PackageReference`, with no source-project references. It is outside
the main solution because the alpha packages must first exist in a feed.

From the repository root, produce and verify a local feed in a new directory:

```powershell
pwsh -File scripts/verify-packages.ps1 -OutputDirectory artifacts/alpha-0.1.0-alpha.1
dotnet restore examples/PackageCoverage --source ./artifacts/alpha-0.1.0-alpha.1 --source https://api.nuget.org/v3/index.json
dotnet run --project examples/PackageCoverage -c Release --no-restore
```

The verifier requires .NET 10 SDK and .NET 8/10 runtimes. It runs isolated copies outside
the repository, maps `PolylineKit.*` exclusively to its newly produced local feed, and
checks actual package assets and runtime assemblies. Its `net8.0` consumers select the
packages' `netstandard2.0` assemblies; this is not a .NET Framework runtime test.

Expected values: zone area **20 m²**, footprint **16 m²**, intersection **7 m²**, zone
coverage **35%**, union **29 m²**, and IoU **7/29**. The example calculates the fixed
zone's area once and treats coverage as undefined when that area is zero.

To also exercise contour output, restore and run with the same adapter property:

```powershell
dotnet restore examples/PackageCoverage -p:UseClipper=true --source ./artifacts/alpha-0.1.0-alpha.1 --source https://api.nuget.org/v3/index.json
dotnet run --project examples/PackageCoverage -c Release --no-restore -p:UseClipper=true
```

The default project uses only Core. `UseClipper=true` references the optional adapter,
which brings Core and Clipper2. For another prepared alpha version, pass
`-p:PolylineKitVersion=<version>` to both restore and run.

Coordinates must already be planar; latitude/longitude is not projected. The API accepts
one closed walk per operand, not a general collection of rings or multipolygons. Fill
rules determine self-intersection behavior. Adding coverage from overlapping footprints
can count the same area more than once. This small example checks semantics and package
usability; it is not a performance benchmark.
