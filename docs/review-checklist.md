# Independent review

This is a review candidate, not an independently reviewed release. No NuGet.org publication is authorized by this checklist.

1. Start at the source revision and evidence links in `report.md`. Restore in locked mode, build, run the explicit `check` command and independently inspect the JSON inputs and outputs. `dotnet test` alone does not run this project's console checks.
2. Verify the near-touch limits from the polygon coordinates and whole-route length weights. In particular, verify that all segment pairs are good under the strict criterion, so the key result does not rely on our unresolved general GenLIP cases.
3. Review `GenLip.cs` against the paper, especially exhausted-side handling, reused segments, strict parallel classification, connector contacts and global denominators. The unsupported cases in `baselines.md` are intentional limits, not successful comparisons.
4. Check whether the graph-only scope is useful for the intended developer use case. No closed/backtracking route should be accepted without a separately specified mathematical operation.
5. Inspect signed-hole handling in the Clipper oracle, the square-twice/forward-backward fixtures and the distinction between net winding multiplicity and traced regions.
6. Re-run the timing processes on independent hardware. Include the transposed Clipper control; do not advertise the original orientation's large slowdown as a universal speed ratio.
7. Verify the package with `scripts/verify-package.ps1`: isolated local-feed installation, no shipped dependencies, repository commit, MIT metadata, README and XML documentation. The alpha API is intentionally unfrozen.

Suggested decision after review: retain the small graph-area utility if it meets a concrete consumer need; otherwise keep the repository as reproducible research. A general polyline matcher, correspondence strategy or alignment optimizer requires a new scope decision.
