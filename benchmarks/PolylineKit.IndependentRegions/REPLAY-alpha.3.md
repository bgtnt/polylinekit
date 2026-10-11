# Frozen Helsinki protocol replay with published alpha.3

This supplement declares a separate release repeat. The original
[PROTOCOL.md](PROTOCOL.md) selected published alpha.2; it has not been rewritten
to imply that it originally selected alpha.3. The original [assessment](ASSESSMENT.json),
[results](RESULTS.md), alpha.2 lock and all frozen data remain unchanged.

The repeat changes only the declared Core release identity:

| Identity | Alpha.3 repeat |
| --- | --- |
| Package | Actual NuGet.org `PolylineKit.Core/0.1.0-alpha.3` |
| Package source revision | `23c0b3280424569356d64362266c519c839b4790` |
| Core net10.0 DLL SHA256 | `bcc2bb413e5bf0087d3621b49d138f4b9ca627ac300386f55973ea49565ddc72` |
| Backend report name | `Core-alpha.3` |
| Separate dependency lock | [packages-alpha.3.lock.json](packages-alpha.3.lock.json) |

[release-profiles.json](release-profiles.json) pins both identities. Alpha.2 is
still the default for the project and verification commands. For alpha.3,
`verify-package.ps1 -Release alpha.3` changes only the Core PackageReference in
its fresh outside-checkout project and copies the separate lock. Both profiles
use NuGet.org alone and an initially empty cache; no library project or private
DLL is linked. Clipper2 C# 2.0.0 and NTS 2.6.0 remain assessment dependencies.

The geometry, EPSG:3067 coordinates, hierarchy, admission/exclusion policy,
grid, original/quantized validity checks, call inventories, area/fraction
budgets and comparator implementations are the same frozen protocol. The
replay continues to distinguish narrower Core/NTS agreement from the original
three-way gate. All comparator failures remain in its full check. A passing
Core/NTS wrapper never authorizes timings after a failed three-way check.
No timing processes are part of this repeat.

Reports record the actual runner source revision, copied-source/profile hashes,
and `clean` or `development` runner provenance. A run while assessment code or
configuration is uncommitted is development evidence. Final alpha.3 results
must come from a new fresh consumer built after the implementation is committed;
they must not attribute changed runner code to an earlier commit.

From the repository root, choose a fresh output directory after committing the
runner implementation:

```powershell
$tool = 'benchmarks/PolylineKit.IndependentRegions'
$data = (Resolve-Path "$tool/data").Path
$revision = (git rev-parse HEAD).Trim()
$evidence = [IO.Path]::GetFullPath("artifacts/independent-helsinki-alpha3-$revision")
if (Test-Path -LiteralPath $evidence) { throw 'Choose a fresh evidence directory.' }
& "$tool/verify-package.ps1" -Release alpha.3 -OutputDirectory "$evidence/package" -KeepWorkspace
$proof = Get-Content "$evidence/package/verification.json" -Raw | ConvertFrom-Json
if (!$proof.Complete -or $proof.AssessmentSourceState -ne 'clean') { throw 'A clean completed package proof is required.' }
$runner = Join-Path $proof.Workspace 'bin/Release/net10.0/PolylineKit.IndependentRegions.dll'
& "$tool/verify-corpus.ps1" -Release alpha.3 -RunnerPath $runner -DataRoot $data -PackageProof "$evidence/package/verification.json" -Output "$evidence/check.json"
& "$tool/check-verify-corpus.ps1" -Release alpha.3 -ReportPath "$evidence/check.json" -RunnerPath $runner -DataRoot $data -PackageProof "$evidence/package/verification.json"
```

The package probe checks 36 analytic backend metrics and the unchanged integer-area
regressions. The corpus wrapper executes the actual full corpus once, retains
its exit code and failures, and checks its narrower Core/NTS contract. Keep all
outputs, including a failed three-way report; do not call the timing commands.
The completed [alpha.3 results](RESULTS-alpha.3.md) and
[machine-readable assessment](ASSESSMENT-alpha.3.json) record the clean runner
`cbcc3113de85fa7c7136585ab29768a059483347`. Core/NTS agreement passed; the full
three-way check retained exit 1 and all 27 comparator failures. No timings were
run. The 36 wrapper evidence controls also passed.
