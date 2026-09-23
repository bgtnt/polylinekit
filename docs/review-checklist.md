# Independent review

This is a review candidate, not an independently reviewed release. No NuGet.org publication is authorized by this checklist.

1. Start at the source revision and evidence links in `report.md`. Restore in locked mode, build, run the explicit `check` command and independently inspect the JSON inputs and outputs. `dotnet test` alone does not run this project's console checks.
2. Verify the near-touch limits from the polygon coordinates and whole-route length weights. In particular, verify that all segment pairs are good under the strict criterion, so the key result does not rely on our unresolved general GenLIP cases.
3. Review `GenLip.cs` against the paper, especially exhausted-side handling, reused segments, strict parallel classification, connector contacts and global denominators. The unsupported cases in `baselines.md` are intentional limits, not successful comparisons.
4. Read `comparison-api.md` and verify that each closed/backtracking use selects the intended operation: endpoint-bridged walk fill versus independent filled-region XOR. Check normalization transform provenance, collapsed axes, numeric rejection, sample correspondence, reversal and closed phase metadata.
5. Inspect signed-hole handling in the Clipper oracle, the square-twice/forward-backward fixtures and the distinction between net winding multiplicity and traced regions.
6. Re-run the timing processes on independent hardware. Include the transposed Clipper control; do not advertise the original orientation's large slowdown as a universal speed ratio.
7. Run the example and the transformation benchmark suite. Check the runtime Clipper2 dependency and pinned restore, but do not package or publish to NuGet at this stage. The API is intentionally unfrozen.

Suggested decision after review: assess concrete consumer inputs with the named comparison and preparation methods. General correspondence, affine/projective optimization and scientific novelty remain unestablished; different scores alone are not evidence of improvement.
