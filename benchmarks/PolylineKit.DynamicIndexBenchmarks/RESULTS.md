# Dynamic segment indexing: measured decision

An independent fixed-slot hierarchy is useful for the measured larger-ring
removal workloads. It is not a universal replacement for linear scanning.
Against scanning only active edges, complete sessions are **1.74x and 2.54x
faster at 2048 vertices**, but **7.9% slower for the county batch**. The district
batch improves by 1.15x. Keep the custom implementation as a small, independently
usable experiment; do not add a general R-tree dependency or replace Winding's
internal sweep on this evidence.

The specialization follows the operation: AB and BC become AC, one original edge
ID survives and the other becomes inactive. A contiguous binary hierarchy refits
those two ancestor paths. It needs no insertion policy, rotations, condensation
or new nodes during removal. It is a bounding-box hierarchy, not a general R-tree.
Construction is O(n), an update is O(log n), and queries remain O(n) in the worst
case. Edge order determines grouping; heavily overlapping bounds can reduce its
pruning benefit. The new code uses ordinary safe C# and double coordinates, without
SIMD intrinsics or unsafe code. The measured change is algorithmic specialization.

## Frozen workload and correctness

All 109 admitted single rings from the frozen Census sample participate (98
counties and 11 districts), plus six declared synthetic rings. See the
[protocol](PROTOCOL.md) and [source admission rules](../../examples/RegionCoverage/data/README.md).
No workload was dropped based on speed or outcome. Local removal tolerance is
1% of initial maximum bounds extent; the driver visits original IDs for at most
three passes. It is an illustrative simplifier, not an error-bounded public API.

| Group | Cases | Initial vertices | Final | Eligible queries | Blocked proposals | Candidate IDs | Predicates |
|---|---:|---:|---:|---:|---:|---:|---:|
| census-county | 98 | 6660 | 2802 | 3859 | 1 | 16440 | 8722 |
| census-district | 11 | 3928 | 441 | 3501 | 14 | 17598 | 10596 |
| radial-128 | 1 | 128 | 68 | 60 | 0 | 240 | 120 |
| orthogonal-comb-128 | 1 | 128 | 97 | 31 | 0 | 155 | 93 |
| radial-512 | 1 | 512 | 15 | 497 | 0 | 2007 | 1013 |
| orthogonal-comb-512 | 1 | 512 | 257 | 255 | 0 | 1211 | 701 |
| radial-2048 | 1 | 2048 | 4 | 2045 | 1 | 39389 | 35299 |
| orthogonal-comb-2048 | 1 | 2048 | 1025 | 1023 | 0 | 4859 | 2813 |

Totals: **15,964 to 4,709 vertices**, 11,255 accepted removals, 11,271 eligible
queries, 16 blocked proposals, 81,899 candidate IDs and 59,357 predicate calls.
Only 0.14% of eligible proposals are blocked: this is a high-removal workload,
not a stress test of repeated rejected shortcuts. Separate analytic cases cover
crossings, forbidden contacts, overlaps and a blocked shortcut through a finger.

All four methods reproduce exact candidate sets, decisions, accepted edge changes
and final contours on all 115 inputs. There are 42,120 public index assertions and
504 shortcut assertions. The private adapter also passes 10,516 differential
candidate checks across 2,620 updates. Initial/final rings pass NTS validity checks.
The shared NTS predicate and validity checks are not an exact-rational oracle.

## Complete sessions

Times are **milliseconds per whole group**, including fresh index construction,
mutable simulation state, scheduling, local eligibility tests, every candidate
predicate and accepted updates. Counties mean all 98 rings; districts mean all
11 rings; each synthetic group means one ring. Entries are the median of three
process medians, followed by their min-max range. Ranges are descriptive, not
confidence intervals. Each process has five calibrated batches per row.

`RtTools*` is an optional **private local baseline with adapter costs**, not a
publicly reproducible third-party ranking. The public source has no RtTools
dependency and publishes none of its source or binaries. The adapter uses real
two-edge deletion and insertion, including condensation/reinsertion; a closed-box
wrapper preserves contacts. Its cached callback fills retained scratch storage
which is copied to the common output span. That overhead is included.

| Group | Linear slots | Active-only scan | Custom fixed slots | RtTools* |
|---|---:|---:|---:|---:|
| census-county | 3.5495 (3.5217-3.7002) | 3.5707 (3.5663-3.5928) | 3.8512 (3.8395-3.9795) | 12.2914 (12.2067-14.0162) |
| census-district | 4.5319 (4.4636-4.5765) | 3.6418 (3.6339-3.7280) | 3.1675 (3.1585-3.2773) | 9.2131 (9.1969-9.2701) |
| radial-128 | 0.0609 (0.0605-0.0627) | 0.0581 (0.0569-0.0582) | 0.0573 (0.0570-0.0600) | 0.1866 (0.1863-0.1883) |
| orthogonal-comb-128 | 0.0471 (0.0467-0.0488) | 0.0462 (0.0461-0.0464) | 0.0485 (0.0477-0.0491) | 0.1312 (0.1306-0.1331) |
| radial-512 | 0.5697 (0.5692-0.5759) | 0.4453 (0.4424-0.4489) | 0.3876 (0.3829-0.3888) | 1.1627 (1.1552-1.1819) |
| orthogonal-comb-512 | 0.3805 (0.3773-0.3849) | 0.3374 (0.3326-0.3437) | 0.2671 (0.2655-0.2885) | 1.0277 (1.0160-1.0392) |
| radial-2048 | 7.0062 (6.9330-7.0277) | 5.2976 (5.2208-5.3259) | 3.0492 (3.0059-3.0543) | 6.3020 (6.2907-6.3185) |
| orthogonal-comb-2048 | 3.6558 (3.6383-3.6786) | 3.1006 (3.0899-3.1092) | 1.2195 (1.1952-1.2357) | 5.1025 (5.0182-5.1356) |

The small radial-128 ranges overlap; its 1.014x median ratio is not a persuasive
gain. The custom index is 5.1% slower on comb-128. At 512 vertices it improves
complete sessions by 1.15x (radial) and 1.26x (comb). At 2048 vertices the gains
are 1.74x and 2.54x over active-only scanning. Its advantage over the private
RtTools adapter is 2.07x-4.18x across these complete-session rows, with no claim
about RtTools under other contracts or a separately optimized adapter.

## Index replay and construction

Replay includes fresh construction, the recorded bounds queries and identical
accepted updates, but no scheduler, geometric predicates or acceptance decisions.
All candidate IDs contribute to the output digest. It reveals the part of the
work that an index can improve; its speedup must not be called the session speedup.

| Group | Linear slots | Active-only scan | Custom fixed slots | RtTools* |
|---|---:|---:|---:|---:|
| census-county | 0.9929 (0.9876-1.0233) | 0.8381 (0.8353-0.9088) | 1.2822 (1.2680-1.2903) | 8.7131 (8.7057-8.8110) |
| census-district | 2.6878 (2.6807-2.8111) | 1.8376 (1.8368-1.8930) | 1.3823 (1.3566-1.3889) | 7.3132 (7.3044-7.3830) |
| radial-128 | 0.0155 (0.0153-0.0165) | 0.0139 (0.0136-0.0139) | 0.0141 (0.0134-0.0142) | 0.1276 (0.1255-0.1278) |
| orthogonal-comb-128 | 0.0083 (0.0080-0.0085) | 0.0073 (0.0072-0.0074) | 0.0096 (0.0096-0.0097) | 0.0903 (0.0889-0.0926) |
| radial-512 | 0.3598 (0.3589-0.3695) | 0.2320 (0.2296-0.2379) | 0.1727 (0.1721-0.1749) | 0.9421 (0.9327-0.9468) |
| orthogonal-comb-512 | 0.1935 (0.1934-0.1936) | 0.1533 (0.1508-0.1656) | 0.0847 (0.0812-0.0871) | 0.8244 (0.8197-0.8959) |
| radial-2048 | 5.2309 (5.2304-5.2521) | 3.3915 (3.3907-3.4012) | 1.1237 (1.1211-1.1361) | 4.3237 (4.3214-4.3393) |
| orthogonal-comb-2048 | 2.9068 (2.9058-2.9666) | 2.3088 (2.2841-2.3341) | 0.4121 (0.4055-0.4173) | 4.2606 (4.2279-4.2790) |

At 2048 vertices, index replay improves by 3.02x (radial) and 5.60x (comb) over
active-only scanning. The smaller complete-session gains above reflect the shared
geometry/simulation work. For counties, replay is 53% slower than active-only
scanning: an index is unnecessary overhead for much of this small-ring batch.

Fresh index construction alone, also in milliseconds per whole group:

| Group | Linear slots | Active-only scan | Custom fixed slots | RtTools* |
|---|---:|---:|---:|---:|
| census-county | 0.0355 (0.0354-0.0361) | 0.0471 (0.0470-0.0476) | 0.0899 (0.0894-0.0962) | 2.9764 (2.9591-2.9946) |
| census-district | 0.0155 (0.0154-0.0157) | 0.0205 (0.0198-0.0208) | 0.0511 (0.0507-0.0522) | 2.8222 (2.8051-2.8485) |
| radial-128 | 0.0006 (0.0006-0.0006) | 0.0007 (0.0007-0.0008) | 0.0017 (0.0016-0.0017) | 0.0698 (0.0689-0.0704) |
| orthogonal-comb-128 | 0.0006 (0.0006-0.0006) | 0.0007 (0.0007-0.0007) | 0.0017 (0.0016-0.0017) | 0.0519 (0.0517-0.0552) |
| radial-512 | 0.0020 (0.0020-0.0020) | 0.0026 (0.0026-0.0027) | 0.0066 (0.0065-0.0067) | 0.3774 (0.3769-0.3808) |
| orthogonal-comb-512 | 0.0020 (0.0020-0.0020) | 0.0026 (0.0026-0.0027) | 0.0066 (0.0065-0.0067) | 0.3169 (0.3149-0.3178) |
| radial-2048 | 0.0078 (0.0078-0.0078) | 0.0103 (0.0102-0.0107) | 0.0354 (0.0354-0.0355) | 1.7215 (1.6971-1.7342) |
| orthogonal-comb-2048 | 0.0078 (0.0077-0.0079) | 0.0108 (0.0106-0.0110) | 0.0357 (0.0346-0.0372) | 1.5347 (1.5197-1.5423) |

Construction is included in both preceding tables, never amortized away. RtTools
uses repeated insertion in this adapter; no bulk builder is measured. The custom
hierarchy builds bottom-up from already prepared boxes. These are the actual
construction strategies of the measured implementations.

## Allocation and limits

Maximum of the three process-median **managed KiB per complete group**:

| Group | Linear slots | Active-only scan | Custom fixed slots | RtTools* |
|---|---:|---:|---:|---:|
| census-county | 3017.3 | 3066.7 | 3278.3 | 4100.8 |
| census-district | 1834.3 | 1861.7 | 1987.9 | 2472.1 |
| radial-128 | 53.5 | 54.5 | 58.6 | 66.4 |
| orthogonal-comb-128 | 51.1 | 52.0 | 56.1 | 60.7 |
| radial-512 | 234.4 | 237.9 | 254.4 | 314.4 |
| orthogonal-comb-512 | 213.6 | 217.1 | 233.6 | 281.6 |
| radial-2048 | 939.9 | 953.9 | 1019.8 | 1232.1 |
| orthogonal-comb-2048 | 852.6 | 866.7 | 932.7 | 1178.2 |

Warm query/update paths for the three public indexes pass zero-allocation checks,
but fresh indexes and full sessions allocate. The tree uses roughly 1.8x the
construction allocation of active-only scanning at 2048 vertices, and about 7%
more allocation for those complete sessions. The complete-session total includes
shared NTS predicates and simulation state. It is not retained/peak memory, nor
a measurement of native allocations. Exact build/replay allocation rows are in
the evidence manifest. No zero-allocation full-simplifier claim follows.

All candidates are enumerated and tested. A visitor that exits on the first
forbidden contact is a different contract, especially on rejection-heavy inputs.
Tests establish simple final rings, not cumulative Hausdorff error, bounded area
change, hole preservation or topology between neighboring GIS features. Bounds
overlap and source edge order can change performance; no universal vertex-count
threshold for switching indexes is established.

## Reproduction and evidence

Measured public source: [`882ef72e360166f7f591d80a3eeaaf7dabf2a4d5`](https://github.com/bgtnt/polylinekit/commit/882ef72e360166f7f591d80a3eeaaf7dabf2a4d5).
SDK 10.0.401, .NET 10.0.12, Microsoft Windows 10.0.26200, X64,
Intel Core i9-9900K (8 cores / 16 logical processors); `DOTNET_TieredCompilation=0`.
Three sequential processes on 2026-09-25, 96 rows each, five batches per row:
**1,440 validated timing samples**. Settings and workloads were declared before
timing. Release solution build completed with zero warnings and errors.

The [commands and measurement exclusions](README.md) reproduce the three public
methods without the private plugin. Loading, input certification, trace creation,
final contour materialization/validation and check-time sorting are outside timers.
To reproduce this precise source, use the commit above. Re-summarizing retained
runs requires matching measured assemblies; a later build can change their hashes.

The [compact evidence manifest](../dynamic-index-evidence.json) records all 96
aggregated rows, all 115 workload counters, input/trace hashes, actual public and
private DLL hashes, raw-file checksums and environment. The summarizer regenerated
scope outputs and verified every matrix row, sample and median. Six deliberate
corruptions (missing row, changed output, median, input hash, binary hash, invalid
sample) were rejected. An independent review confirmed the totals and ratios.

Raw JSON, complete actual traces, validation metadata and generated tables are
retained locally in `dynamic-index-evidence-882ef72.zip` (1,348,138 bytes), SHA-256
`3a5b891b3a6642a5b5399731c6318a0bd37759700a9d7941dfffef911dd52f53`. This checksum is **not a public download link**.
The archive contains no private source or binaries. Public inputs, generators and
implementations can be rerun independently; the private comparison cannot.

## Decision

Keep the independently written hierarchy and the linear alternatives available in
this bounded example. There is measured reason to use the custom design for larger
removal workloads, but no reason to impose a generic R-tree on every contour.
Do not choose a switching threshold from these eight groups alone. A next
production-simplifier task would first need an explicit cumulative error contract
and independent workload validation, including rejected shortcuts and holes.
This experiment supplies no evidence for moving Winding to C++.
