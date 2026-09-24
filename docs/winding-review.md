# Winding review disposition, 2026-09-24

The supplied review targeted `969f8ac`. The starting branch for this follow-up
was `e4444cc`, which already contained the separate edge-product correction,
dependency-free winding assembly and explicit Clipper comparison contracts.
The new work addresses the distinct sub-edge fraction failure and completes
the bounded comparison and area-native example. Recognition remains archived.

## Sub-edge underflow: fixed

Commit `cd23a11` preserves the ordinary divide/multiply path. A subnormal or
zero fraction with a nonzero numerator is scaled by `2^512` before division;
the contribution is scaled back after multiplication. Both single-loop and
region accumulators share the repair. The coordinate cap makes this exceptional
calculation safe from intermediate overflow; see the
[arithmetic derivation](winding-numerics.md#sub-edge-ratio-underflow).

The reported `1e-250` and `1e-300` intersections are exact now. The 3,584 new
checks use dyadic-oracle areas and relative/ULP-sensitive assertions; 192 fail
against the saved pre-fix assembly. The five-mode gate passes 177,627 portable
and 177,539 assertions in each modern mode. Both targets run on .NET 10; this
does not assert compatibility testing on every .NET Standard host. The fixed
fixture checker passes 424 assertions per target, and the 120 retained public
operation outputs per target remain bit-identical to the previous corrected
engine. These ordinary fixtures supplement the targeted regressions; they
cannot establish accuracy at every scale.

The six-process, identical-harness comparison replaces only the leaf assembly.
Changes range from -7.9% to +4.9%, mostly within 3%, with zero warm allocations.
This is a bounded cost check, not an optimization claim or confidence interval.

## Current comparison and storage: measured

At `2410a6b`, three fresh processes measure 238 method rows each, including
16-vertex inputs and separate AbsoluteWinding rows without a Clipper ratio.
Clipper64 static, reused and preloaded contracts, ClipperD precision six and
the conversion-inclusive public wrapper remain distinct. Input/output
quantization and numerical deltas are retained. At `a50dade`, 15 fresh
processes measure first area calls and retained workspace separately from
warm calls. No winding runtime code changed between these two revisions.
All tables, unfavorable cases and memory limitations are in
[performance.md](performance.md).

## Area-native consumer: implemented

[AreaChange](../examples/AreaChange/README.md) freezes four complete, licensed
single-ring Natural Earth features, then uses the existing Clipper2 simplifier
at three tolerances. The example reports XOR, union-normalized change and
removed vertices, with three retained overlays. Analytic controls and a p8
clipping comparison pass; p7 provides a separate sensitivity check. Both
complete consumers perform identical simplification and consume the Jaccard
division. All twelve measured cases, process ranges and allocations appear in
[the results](../examples/AreaChange/RESULTS.md). The observed whole-consumer
ratios are 1.06–1.78 in favor of winding on this machine, with the smallest
differences requiring caution. This is evidence for a bounded geometry use
case, not a claim of universal superiority or customer demand.

## Packaging boundary

The README and Basic example now lead with a winding-area operation. The
standalone `PolylineKit.Winding` assembly and its parent-assembly forwarders
were already implemented and checked. NuGet creation/publication remains
deferred according to the user's current scope; no package verification or
release is claimed here. No public API or runtime dependency was added by this
follow-up. Clipper2 remains an example-only simplifier/comparison dependency
for AreaChange and is absent from the leaf runtime.

The [compact manifest](../benchmarks/winding-review-evidence.json) records source,
assembly and input identities, summaries and a checksum archive of local raw
evidence. Historical archive tags and recognition evidence are unchanged.
