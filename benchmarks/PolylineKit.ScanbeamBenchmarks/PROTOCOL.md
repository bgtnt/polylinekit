# Bounded-integer scanbeam experiment

This protocol precedes the first timing run. The experiment asks whether an
area-only active-edge sweep can reduce the established degenerate-grid loss.
It is outside the shipping library and does not change its binary64 contract.

## Algorithm and scope

The prototype accepts an implicitly closed walk with exact integer coordinates
in [-32768, 32768], up to 8192 points, and NonZero or EvenOdd fill. It rejects
fractional/out-of-range coordinates rather than rounding. Int128 rational
comparisons determine event topology; compensated binary64 integration computes
area. Exact ordering does not make the final area exact.

Endpoint levels define scanbeams. Active edges are ordered at the bottom and top;
order inversions generate interior crossing events. Events at one exact point
are handled together; filled gaps between adjacent active edges are integrated
as nonnegative trapezoids. Their endpoint widths and event-height differences
are formed as exact rational differences before binary64 conversion. This avoids
subtracting large, separately rounded boundary-area contributions.
Horizontal edges have zero-height contribution. Repeated and collinear edges
retain signed winding multiplicity. This is a focused scanbeam prototype:
rebuilding active edges at endpoint levels can be expensive when there are many
distinct levels; it is not an O((n+k) log n) general sweep.
It buffers all inversion events in the current band, O(k_band) working event
storage. It avoids the general engine's global per-edge incidence lists and
shared-subedge hashing, not event storage altogether. Setup costs O(n) edge
scanning plus O(a log a) ordering per band; inversion enumeration costs O(a+k)
and event ordering O(k log k) for that band, where a is its active count.

Clipper's scanbeam/AEL/SEL organization and intersection inversions are the
conceptual reference ([release-era C# source](https://github.com/AngusJohnson/Clipper2/blob/4d363dcb51c193c2f1883440962024b044f11953/CSharp/Clipper2Lib/Clipper.Engine.cs)).
No Clipper implementation is copied. The existing pinned Clipper2 2.0.0 package
is a benchmark-only dependency, already used by the repository.

## Correctness before timing

Independent analytic shapes and the existing exact-rational area oracle check
both fills, overlaps, repeated and reversed traversals, multiway crossings,
endpoint contacts and seeded small walks. Prototype/oracle tolerance is
1e-10 * max(1, abs(expected)); this is an experiment acceptance budget, not an
API error guarantee. Frozen and larger generated inputs must agree with the
unchanged WindingArea within the same budget. Tests do not treat Clipper output
as exact: even integer input can have noninteger intersections.

## Inputs and methods

Seven inputs, both fills, four timed methods give 56 rows per process:

* The two frozen degenerate-grid pairs (64 and 256 points per path), joined as
  first followed by reversed second. The complete walk is prepared for every
  method before timing; no per-query result is cached.
* Fresh deterministic 128- and 512-point 8x8 walks; a 512-point repeated square;
  a rounded integer star of 512 points; a 128-point walk with many endpoint
  levels. The latter is an explicit unfavorable control for band rebuilding.
* Current WindingArea.ClosedPath, consuming the requested fill but computing
  all four existing integrals; no claim of equal output work.
* IntegerScanbeam, validating and preparing from Point2 input on every call.
* Direct Clipper64 reused: preconverted input, Clear/Add/Execute/area timed.
* Direct Clipper64 preloaded: conversion and Add outside, Execute/area timed.

Both Clipper methods use scale 1e6, as the previous comparison did. Input
integers scale exactly. Crossing coordinates remain quantized to that grid,
so their area errors are reported separately. Unscaled Clipper64 is an
untimed diagnostic that shows why integer input does not imply integer output.

Three fresh processes run sequentially with DOTNET_TieredCompilation=0, five
calibrated batches per row, >=20 ms target per batch, warmup and rotated method
order. This is warm throughput and managed allocation measurement, not first
use, retained or peak memory. Record all samples, input/output values, machine,
runtime, commit and assembly hashes. Report medians of process medians and
their ranges; no significance or general hardware claim.

## Decision fixed before results

For each frozen size and fill, the faster of the two Clipper medians divided by
the prototype median must be >=1.25 (at least 20% less time). All four cells
must pass, and correctness must pass, to justify developing this bounded
backend further. Fresh and unfavorable controls remain visible regardless of
their results. A valid negative result completes this experiment. Passing is
not permission to silently dispatch general binary64 inputs to this prototype.

## Bounded arithmetic ablation

The first complete three-process run at `4457184` failed all four gates. Those
raw files and measured binaries are retained separately. Before further timing,
one bounded follow-up is declared: remove checked-arithmetic overflow handling
only where the accepted coordinate/vertex limits prove the intermediates fit.
The input contract, algorithm, numerical formulas, guards, tolerance, fixtures,
method matrix and four-cell gate remain unchanged. Run three new processes and
preserve both versions' results; require bit-identical output values between
versions. This is a post-baseline implementation ablation, not an unseen test
set or a new performance threshold. Algorithm counts collected outside timing
are descriptive and do not constitute a stage-by-stage CPU profile.

The bounded-arithmetic run at `94f5ecd` improves grid throughput by 3.2–3.7x
but still fails both NonZero gates; both EvenOdd gates pass. One final arithmetic
ablation is declared before its timings: when all supplied coordinate magnitudes
are <=2048, use Int64 for the same exact expressions, otherwise retain Int128.
The largest intermediate is bounded by 128*M^5 = 2^62 at M=2048, below signed
Int64 capacity. Coordinate validation is still against 32768; this is a proven
dispatch predicate, not a smaller accepted domain or a quantization step.
The same matrix, gate and bit-identity requirement apply. Keep both earlier
versions, add checks on either side of the dispatch boundary, and do not infer
universal or unseen-input performance from this development sequence.
