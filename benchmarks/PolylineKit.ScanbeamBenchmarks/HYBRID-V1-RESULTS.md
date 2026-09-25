# First adaptive selector: preserved failed result

Measured source: [`bb10c6b72a8a8a48f022740bf564ca79b7588ed4`](https://github.com/bgtnt/polylinekit/commit/bb10c6b72a8a8a48f022740bf564ca79b7588ed4).
See the [fixed first protocol](HYBRID-PROTOCOL.md) and
[complete per-cell evidence](../scanbeam-hybrid-evidence.json).

All 12 dense integer-grid targets pass: the actual hybrid, including selection,
beats Winding and full-input Clipper by at least 20% on each. The overall gate
still **fails** because selection adds roughly 10 us to a 28 us sparse sawtooth,
making the complete operation 35.5–35.8% slower than Winding. That case correctly
selects Winding; the cost of reaching the decision is the problem.

The classifier also rejects repeated contours because they have no sampled
proper crossings. This preserves Winding's roughly 6 ms cost although the forced
integer sweep needs only about 30 us. The hybrid is approximately 194–205 times
slower than that available backend on these rows. A gate against Winding alone
would hide this missed opportunity; the report records best-backend regret.

This is one requested filled area of one implicitly closed walk, not the GIS
two-region intersection operation from earlier experiments. Winding's public
ClosedPath still computes all four integrals; Clipper also constructs contours.
The scalar experimental wrapper does not replace that public API.

The 22 fixtures give 44 input/fill cases, 290 rows/process and 4350 samples in
three sequential processes. The forced double sweep certifies 22 cases and
falls back to Winding on 22, with abandoned work included. The hybrid uses the
integer backend in 12 cases and Winding in 32. Normal and no-intrinsics checks
pass, as do backend bit parity, independent small-area oracle controls, legacy
validation, and the positive plus ten corrupted-evidence controls.

The NonZero `held-near-coincident-512` case yields Winding area
`0.00004029272012038706` and Clipper area `-0.002847855882`, for both Clipper
preparation variants. The negative value is retained and marked invalid, not
clamped. Its cause is not isolated here, and it is not credited as a speed win.

The raw archive is local, not publicly hosted:
`artifacts/scanbeam-hybrid-bb10c6b.zip`, SHA-256
`2dc6bb89a740d25622221b83b02919430131d345c5c00e8ba3ba5684bb81b15d`.
The public manifest records binary/environment, input and sample hashes, each
median/range/allocation, routing decisions, fallback and numeric differences.

A [separately declared second selector experiment](HYBRID-V2-PROTOCOL.md)
addresses cheap early rejection and repeated-edge detection. The first run is
retained as negative evidence and is not reclassified as a success.
