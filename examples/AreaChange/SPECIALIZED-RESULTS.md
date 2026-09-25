# Specialized simplification comparisons

The ordinary nested formula is **30.0–39.8× faster** than Winding on the five guaranteed-inner synthetic cases. Ordinary local-pocket sums are **26.4–46.8× faster** on the four guaranteed-disjoint cases. These are floating-point calculations of identities valid under their supplied guarantees; all five outputs match independent exact integer fixture areas. The more expensive product-tail implementation is reported separately.

For the twelve real pairs and three illustrative Jaccard thresholds, prepared bounds resolve **24/36** requests: those calls are **1.17–2.08× faster**, while the **12/36** fallbacks take **1.47–1.94× longer** than direct Winding. All prepared methods allocate zero warm bytes. This demonstrates a conditional filter, not a universal speedup or a production resolution rate.

| Jaccard threshold | Resolved / 12 | Sum of Winding medians, µs | Sum of filter medians, µs | Winding / filter |
|---|---:|---:|---:|---:|
| 0.1% | 9 | 747.52 | 688.22 | 1.086 |
| 1% | 6 | 744.69 | 828.19 | 0.899 |
| 3% | 9 | 744.19 | 635.21 | 1.172 |

These aggregates sum one request per pair; they are not a separately timed workload or a probability-weighted production mix. At 1%, the filter is slower overall. With geometry certification and mapping charged on every call, the tested exact quadratic certifier makes the pipeline **8.07–11.79× slower**, allocating **111,768–399,648 B/call**. A faster certifier may change that comparison; an already guaranteed input does not pay that cost.

Use the specialized formula when its guarantees are available. Use a threshold filter only after measuring its cost and fallback rate for the actual policy. General filled-area comparison remains appropriate for actual XOR/union/Jaccard values and arbitrary accepted walks. No automatic filter was added to the library.

The [method and reproduction commands](SPECIALIZED.md) state the prerequisites and numerical limitations. The [compact evidence manifest](../../benchmarks/simplification-evidence.json) records all ratios, assembly/input identities and local raw-archive hashes. Three processes supply **2,025 raw batches**, all verified by the summarizer; altered-median rejection was also tested. Raw evidence remains local under ignored `artifacts/`, not uploaded. SDK 10.0.401 was used. No NuGet was packed or published.

Measured source `0f689424aba5b80dc9e997f77a3e49215f5a5833`; 2026-09-25 UTC; .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel. Three fresh processes, five calibrated batches each, rotated method order, tiered compilation `0`. Times show median of process medians (minimum–maximum); ratios are descriptive, not significance tests.

The synthetic guarantees follow from construction. Prepared real-pair decisions assume certified simple polygons and retained indices supplied by the caller; mapping and certification are measured separately. Uncertified-input rows charge the exact example certifier on every call. That O(n²) certifier is a correctness-oriented reference, not a claim of optimal validation cost. Timings start with finished input arrays; simplification, file loading and reporting are excluded.

## Bounds and decisions

Accept means Jaccard change ≤ threshold; Reject means greater. Unresolved runs Winding. Bounds conservatively enclose exact binary64-input geometry under the supplied simple/subsequence preconditions; fallback inherits Winding's numerical limits. Winding values below are comparison measurements, not an independent exact oracle.

| Pair | XOR lower | Winding XOR | XOR upper | Jaccard lower–upper | 0.1% | 1% | 3% |
|---|---:|---:|---:|---:|---|---|---|
| BGR-1 | 19.7237758 | 205.17744 | 218.554334 | 0.0046 %–0.0505 % | Accept | Accept | Accept |
| BGR-4 | 150.140722 | 2610.74563 | 3329.30406 | 0.0347 %–0.7662 % | Unresolved | Accept | Accept |
| BGR-12 | 1002.65138 | 9922.26071 | 17235.9904 | 0.2316 %–3.9078 % | Reject | Unresolved | Unresolved |
| CHE-1 | 40.0993328 | 188.666918 | 210.616446 | 0.0113 %–0.0595 % | Accept | Accept | Accept |
| CHE-4 | 405.33611 | 2892.75205 | 3654.57628 | 0.1146 %–1.0287 % | Reject | Unresolved | Accept |
| CHE-12 | 112.892874 | 10332.3856 | 13898.468 | 0.0319 %–3.8549 % | Unresolved | Unresolved | Unresolved |
| LSO-1 | 36.3413469 | 55.025813 | 58.272969 | 0.0064 %–0.0102 % | Accept | Accept | Accept |
| LSO-4 | 675.061635 | 1864.36267 | 2257.66737 | 0.1181 %–0.3943 % | Reject | Accept | Accept |
| LSO-12 | 1434.17563 | 9596.34708 | 12931.4515 | 0.2508 %–2.2389 % | Reject | Unresolved | Accept |
| NPL-1 | 44.1872703 | 334.880209 | 403.551729 | 0.0189 %–0.1726 % | Unresolved | Accept | Accept |
| NPL-4 | 317.218996 | 2187.24098 | 3045.05444 | 0.1358 %–1.2962 % | Reject | Unresolved | Accept |
| NPL-12 | 2209.56803 | 7945.32128 | 13649.1547 | 0.9372 %–5.6520 % | Reject | Unresolved | Unresolved |

## five-areas-guarantee-supplied

| Case | Method | µs (min–max) | B/op | Fallback |
|---|---|---:|---:|---|
| nested-16 | winding | 4.873 (4.673–4.945) | 0 | False |
| nested-16 | nested-shoelace | 0.123 (0.122–0.124) | 0 | False |
| nested-16 | compensated-products | 0.513 (0.511–0.521) | 0 | False |
| nested-64 | winding | 16.259 (16.015–17.292) | 0 | False |
| nested-64 | nested-shoelace | 0.495 (0.489–0.497) | 0 | False |
| nested-64 | compensated-products | 2.213 (2.183–2.252) | 0 | False |
| nested-256 | winding | 59.304 (58.653–60.959) | 0 | False |
| nested-256 | nested-shoelace | 1.957 (1.953–1.965) | 0 | False |
| nested-256 | compensated-products | 8.924 (8.908–8.980) | 0 | False |
| nested-1024 | winding | 232.057 (229.829–237.109) | 0 | False |
| nested-1024 | nested-shoelace | 7.727 (7.719–7.739) | 0 | False |
| nested-1024 | compensated-products | 35.840 (35.754–36.113) | 0 | False |
| nested-4096 | winding | 926.550 (914.341–929.697) | 0 | False |
| nested-4096 | nested-shoelace | 30.818 (30.766–31.226) | 0 | False |
| nested-4096 | compensated-products | 144.005 (143.800–145.287) | 0 | False |
| pockets-4 | winding | 5.947 (5.808–6.251) | 0 | False |
| pockets-4 | local-pockets | 0.218 (0.215–0.222) | 0 | False |
| pockets-4 | compensated-products | 0.858 (0.852–0.899) | 0 | False |
| pockets-16 | winding | 19.882 (19.278–19.992) | 0 | False |
| pockets-16 | local-pockets | 0.753 (0.742–0.767) | 0 | False |
| pockets-16 | compensated-products | 3.064 (3.022–3.098) | 0 | False |
| pockets-64 | winding | 85.117 (82.479–86.223) | 0 | False |
| pockets-64 | local-pockets | 2.857 (2.844–2.857) | 0 | False |
| pockets-64 | compensated-products | 11.990 (11.957–12.028) | 0 | False |
| pockets-256 | winding | 521.644 (512.112–534.534) | 0 | False |
| pockets-256 | local-pockets | 11.153 (11.150–11.179) | 0 | False |
| pockets-256 | compensated-products | 46.826 (46.288–46.938) | 0 | False |

## preparation

| Case | Method | µs (min–max) | B/op | Fallback |
|---|---|---:|---:|---|
| BGR-1 | certify-simple-and-map | 735.375 (732.125–760.844) | 378040 | False |
| BGR-4 | certify-simple-and-map | 573.725 (564.653–613.191) | 304976 | False |
| BGR-12 | certify-simple-and-map | 471.527 (470.053–493.009) | 252152 | False |
| CHE-1 | certify-simple-and-map | 776.234 (769.319–819.078) | 399648 | False |
| CHE-4 | certify-simple-and-map | 634.172 (620.663–663.025) | 326120 | False |
| CHE-12 | certify-simple-and-map | 530.350 (522.525–564.566) | 276808 | False |
| LSO-1 | certify-simple-and-map | 290.697 (273.089–302.947) | 159808 | False |
| LSO-4 | certify-simple-and-map | 230.218 (225.435–248.422) | 135080 | False |
| LSO-12 | certify-simple-and-map | 190.987 (189.241–194.441) | 111768 | False |
| NPL-1 | certify-simple-and-map | 761.531 (759.909–809.009) | 399560 | False |
| NPL-4 | certify-simple-and-map | 621.191 (609.284–645.359) | 323160 | False |
| NPL-12 | certify-simple-and-map | 520.881 (517.188–567.931) | 277368 | False |

## decision-guarantee-supplied

| Case | Method | µs (min–max) | B/op | Fallback |
|---|---|---:|---:|---|
| BGR-1/0.001 | winding | 76.407 (75.537–79.344) | 0 | False |
| BGR-1/0.001 | bounds-then-winding | 41.449 (41.395–41.705) | 0 | False |
| BGR-1/0.01 | winding | 78.675 (78.211–79.804) | 0 | False |
| BGR-1/0.01 | bounds-then-winding | 41.681 (41.302–41.774) | 0 | False |
| BGR-1/0.03 | winding | 76.967 (76.937–78.727) | 0 | False |
| BGR-1/0.03 | bounds-then-winding | 41.112 (40.875–42.402) | 0 | False |
| BGR-4/0.001 | winding | 79.586 (79.492–80.404) | 0 | False |
| BGR-4/0.001 | bounds-then-winding | 124.297 (124.102–125.460) | 0 | True |
| BGR-4/0.01 | winding | 78.752 (78.165–81.437) | 0 | False |
| BGR-4/0.01 | bounds-then-winding | 41.873 (41.805–42.131) | 0 | False |
| BGR-4/0.03 | winding | 82.166 (79.495–83.419) | 0 | False |
| BGR-4/0.03 | bounds-then-winding | 41.812 (41.536–42.378) | 0 | False |
| BGR-12/0.001 | winding | 56.850 (54.166–58.641) | 0 | False |
| BGR-12/0.001 | bounds-then-winding | 41.430 (41.363–41.979) | 0 | False |
| BGR-12/0.01 | winding | 55.509 (55.454–55.805) | 0 | False |
| BGR-12/0.01 | bounds-then-winding | 100.828 (100.631–101.992) | 0 | True |
| BGR-12/0.03 | winding | 55.256 (54.918–56.712) | 0 | False |
| BGR-12/0.03 | bounds-then-winding | 100.421 (98.570–100.609) | 0 | True |
| CHE-1/0.001 | winding | 89.386 (86.019–89.611) | 0 | False |
| CHE-1/0.001 | bounds-then-winding | 43.477 (42.414–44.226) | 0 | False |
| CHE-1/0.01 | winding | 86.509 (85.883–87.308) | 0 | False |
| CHE-1/0.01 | bounds-then-winding | 43.040 (42.560–43.146) | 0 | False |
| CHE-1/0.03 | winding | 87.409 (86.043–88.954) | 0 | False |
| CHE-1/0.03 | bounds-then-winding | 42.829 (42.691–44.821) | 0 | False |
| CHE-4/0.001 | winding | 91.527 (89.100–92.315) | 0 | False |
| CHE-4/0.001 | bounds-then-winding | 43.932 (43.720–44.160) | 0 | False |
| CHE-4/0.01 | winding | 92.713 (91.339–93.203) | 0 | False |
| CHE-4/0.01 | bounds-then-winding | 136.691 (136.160–139.380) | 0 | True |
| CHE-4/0.03 | winding | 89.709 (89.528–90.838) | 0 | False |
| CHE-4/0.03 | bounds-then-winding | 43.987 (43.278–44.191) | 0 | False |
| CHE-12/0.001 | winding | 71.072 (68.727–71.226) | 0 | False |
| CHE-12/0.001 | bounds-then-winding | 117.002 (116.107–117.185) | 0 | True |
| CHE-12/0.01 | winding | 69.649 (69.364–70.247) | 0 | False |
| CHE-12/0.01 | bounds-then-winding | 116.824 (114.645–116.988) | 0 | True |
| CHE-12/0.03 | winding | 71.972 (70.761–74.505) | 0 | False |
| CHE-12/0.03 | bounds-then-winding | 115.150 (114.925–118.585) | 0 | True |
| LSO-1/0.001 | winding | 28.222 (27.441–29.244) | 0 | False |
| LSO-1/0.001 | bounds-then-winding | 17.426 (17.412–17.730) | 0 | False |
| LSO-1/0.01 | winding | 28.325 (28.105–28.845) | 0 | False |
| LSO-1/0.01 | bounds-then-winding | 17.529 (17.354–17.692) | 0 | False |
| LSO-1/0.03 | winding | 28.068 (27.844–29.450) | 0 | False |
| LSO-1/0.03 | bounds-then-winding | 17.535 (17.446–18.628) | 0 | False |
| LSO-4/0.001 | winding | 29.286 (28.259–31.453) | 0 | False |
| LSO-4/0.001 | bounds-then-winding | 17.480 (17.360–18.250) | 0 | False |
| LSO-4/0.01 | winding | 28.705 (28.456–30.179) | 0 | False |
| LSO-4/0.01 | bounds-then-winding | 17.533 (17.518–17.661) | 0 | False |
| LSO-4/0.03 | winding | 28.348 (27.955–28.968) | 0 | False |
| LSO-4/0.03 | bounds-then-winding | 17.708 (17.486–19.583) | 0 | False |
| LSO-12/0.001 | winding | 21.125 (21.062–21.815) | 0 | False |
| LSO-12/0.001 | bounds-then-winding | 17.626 (17.565–17.839) | 0 | False |
| LSO-12/0.01 | winding | 21.810 (20.769–22.306) | 0 | False |
| LSO-12/0.01 | bounds-then-winding | 39.590 (38.707–41.130) | 0 | True |
| LSO-12/0.03 | winding | 20.725 (20.475–22.891) | 0 | False |
| LSO-12/0.03 | bounds-then-winding | 17.546 (17.394–17.689) | 0 | False |
| NPL-1/0.001 | winding | 78.838 (78.631–80.209) | 0 | False |
| NPL-1/0.001 | bounds-then-winding | 129.863 (127.863–132.126) | 0 | True |
| NPL-1/0.01 | winding | 78.657 (77.932–78.735) | 0 | False |
| NPL-1/0.01 | bounds-then-winding | 46.690 (46.497–48.891) | 0 | False |
| NPL-1/0.03 | winding | 79.032 (78.685–79.495) | 0 | False |
| NPL-1/0.03 | bounds-then-winding | 46.346 (46.192–47.439) | 0 | False |
| NPL-4/0.001 | winding | 70.100 (69.894–70.677) | 0 | False |
| NPL-4/0.001 | bounds-then-winding | 46.967 (46.142–47.151) | 0 | False |
| NPL-4/0.01 | winding | 71.011 (68.712–71.057) | 0 | False |
| NPL-4/0.01 | bounds-then-winding | 120.603 (118.661–122.161) | 0 | True |
| NPL-4/0.03 | winding | 69.855 (69.743–69.909) | 0 | False |
| NPL-4/0.03 | bounds-then-winding | 46.581 (46.235–46.856) | 0 | False |
| NPL-12/0.001 | winding | 55.125 (54.392–55.850) | 0 | False |
| NPL-12/0.001 | bounds-then-winding | 47.275 (46.980–48.201) | 0 | False |
| NPL-12/0.01 | winding | 54.376 (53.544–54.855) | 0 | False |
| NPL-12/0.01 | bounds-then-winding | 105.306 (104.120–105.513) | 0 | True |
| NPL-12/0.03 | winding | 54.684 (54.184–56.927) | 0 | False |
| NPL-12/0.03 | bounds-then-winding | 104.180 (103.445–105.499) | 0 | True |

## decision-uncertified-input

| Case | Method | µs (min–max) | B/op | Fallback |
|---|---|---:|---:|---|
| BGR-1/0.01 | winding | 74.872 (74.768–76.089) | 0 | False |
| BGR-1/0.01 | certify-bounds-then-winding | 799.728 (785.975–803.513) | 378040 | False |
| BGR-4/0.01 | winding | 80.265 (79.882–81.840) | 0 | False |
| BGR-4/0.01 | certify-bounds-then-winding | 647.691 (619.119–654.116) | 304976 | False |
| BGR-12/0.01 | winding | 55.166 (54.210–61.254) | 0 | False |
| BGR-12/0.01 | certify-bounds-then-winding | 584.378 (575.519–598.659) | 252152 | True |
| CHE-1/0.01 | winding | 88.113 (84.987–88.811) | 0 | False |
| CHE-1/0.01 | certify-bounds-then-winding | 855.503 (837.219–860.597) | 399648 | False |
| CHE-4/0.01 | winding | 89.738 (89.002–90.444) | 0 | False |
| CHE-4/0.01 | certify-bounds-then-winding | 800.272 (787.619–814.947) | 326120 | True |
| CHE-12/0.01 | winding | 69.973 (69.732–72.447) | 0 | False |
| CHE-12/0.01 | certify-bounds-then-winding | 692.775 (675.094–694.425) | 276808 | True |
| LSO-1/0.01 | winding | 27.900 (27.655–30.330) | 0 | False |
| LSO-1/0.01 | certify-bounds-then-winding | 303.003 (293.217–307.719) | 159808 | False |
| LSO-4/0.01 | winding | 28.729 (27.922–28.734) | 0 | False |
| LSO-4/0.01 | certify-bounds-then-winding | 273.645 (265.369–277.855) | 135080 | False |
| LSO-12/0.01 | winding | 21.379 (21.016–22.068) | 0 | False |
| LSO-12/0.01 | certify-bounds-then-winding | 246.609 (242.211–267.575) | 111768 | True |
| NPL-1/0.01 | winding | 78.890 (77.997–79.099) | 0 | False |
| NPL-1/0.01 | certify-bounds-then-winding | 839.475 (812.228–841.497) | 399560 | False |
| NPL-4/0.01 | winding | 71.143 (70.012–72.467) | 0 | False |
| NPL-4/0.01 | certify-bounds-then-winding | 756.644 (741.644–765.378) | 323160 | True |
| NPL-12/0.01 | winding | 56.152 (55.201–57.942) | 0 | False |
| NPL-12/0.01 | certify-bounds-then-winding | 661.856 (646.172–669.263) | 277368 | True |

Warm allocation figures exclude startup, retained workspace, fixture construction and supplied certificates. A threshold decision is not an exact-area result. This small matrix establishes neither production acceptance rates nor universal performance superiority. Read SPECIALIZED.md for prerequisites and source references.
