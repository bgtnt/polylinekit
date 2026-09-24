# Filled-area change after contour simplification

Source revision: `a50dade89d17cc89212f7338c13bec724749c83d`. Measured 2026-09-24 UTC. .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel. Three independent processes, five batches each; median of process medians, with min–max process medians. No statistical significance claim.

Four public-domain Natural Earth contours; each source feature has one ring. Longest map-plane bound is 1000. Clipper2 2.0.0 SimplifyPath uses tolerances 1, 4 and 12. The area unit is the squared normalized map-plane unit, not square kilometres.

| Shape | Tolerance | Vertices before → after | Removed | XOR area | Jaccard change | p8 XOR absolute delta | Overlay |
|---|---:|---:|---:|---:|---:|---:|---|
| Bulgaria | 1 | 178 → 153 | 25 | 205.17744 | 0.0474 % | 2.773E-07 | — |
| Bulgaria | 4 | 178 → 88 | 90 | 2610.74563 | 0.6013 % | 5.74E-07 | [view](overlays/BGR-4.svg) |
| Bulgaria | 12 | 178 → 41 | 137 | 9922.26071 | 2.2684 % | 6.214E-07 | — |
| Switzerland | 1 | 186 → 166 | 20 | 188.666918 | 0.0533 % | 3.613E-07 | — |
| Switzerland | 4 | 186 → 99 | 87 | 2892.75205 | 0.8151 % | 1.274E-07 | — |
| Switzerland | 12 | 186 → 55 | 131 | 10332.3856 | 2.8801 % | 3.418E-07 | — |
| Lesotho | 1 | 76 → 64 | 12 | 55.025813 | 0.0096 % | 4.823E-07 | [view](overlays/LSO-1.svg) |
| Lesotho | 4 | 76 → 42 | 34 | 1864.36267 | 0.3257 % | 1.091E-06 | — |
| Lesotho | 12 | 76 → 21 | 55 | 9596.34708 | 1.6663 % | 2.272E-07 | — |
| Nepal | 1 | 201 → 148 | 53 | 334.880209 | 0.1433 % | 4.543E-07 | — |
| Nepal | 4 | 201 → 80 | 121 | 2187.24098 | 0.9327 % | 4.837E-07 | — |
| Nepal | 12 | 201 → 39 | 162 | 7945.32128 | 3.3294 % | 1.037E-06 | [view](overlays/NPL-12.svg) |

Both isolated scorers start from the same two double arrays and request XOR plus union for Jaccard. Direct Clipper64 includes conversion to a 10^-8 grid, two Boolean operations, and area summation without converting output vertices back to doubles. Winding returns its complete overlap result. p7 and p8 are compared in accuracy.json; neither clipping result is claimed to be exact.

## isolated-area

| Pair | Winding µs (min–max) | Clipper64 p8 µs (min–max) | Clipper / Winding | Winding B/op | Clipper B/op |
|---|---:|---:|---:|---:|---:|
| BGR-1 | 75.90 (74.28–79.85) | 118.39 (117.17–120.43) | 1.56 | 0 | 126696 |
| BGR-4 | 82.59 (79.08–83.39) | 119.85 (115.64–121.16) | 1.45 | 0 | 103744 |
| BGR-12 | 54.80 (54.21–55.07) | 101.58 (100.16–102.26) | 1.85 | 0 | 83024 |
| CHE-1 | 89.19 (87.73–91.73) | 135.39 (134.31–140.11) | 1.52 | 0 | 131944 |
| CHE-4 | 90.93 (90.41–92.73) | 136.83 (135.34–145.78) | 1.50 | 0 | 107248 |
| CHE-12 | 70.98 (69.40–71.87) | 122.51 (122.16–132.75) | 1.73 | 0 | 88456 |
| LSO-1 | 27.77 (27.60–28.57) | 29.51 (29.34–29.93) | 1.06 | 0 | 46776 |
| LSO-4 | 28.91 (28.02–31.22) | 31.92 (30.07–35.47) | 1.10 | 0 | 40200 |
| LSO-12 | 21.24 (20.71–21.28) | 28.18 (27.00–28.37) | 1.33 | 0 | 30304 |
| NPL-1 | 78.78 (78.30–81.21) | 139.39 (133.29–141.87) | 1.77 | 0 | 130248 |
| NPL-4 | 71.28 (71.12–73.36) | 134.93 (128.76–139.25) | 1.89 | 0 | 107488 |
| NPL-12 | 56.39 (55.42–58.57) | 109.13 (106.92–110.67) | 1.94 | 0 | 85784 |

## complete-consumer

| Pair | Winding µs (min–max) | Clipper64 p8 µs (min–max) | Clipper / Winding | Winding B/op | Clipper B/op |
|---|---:|---:|---:|---:|---:|
| BGR-1 | 80.83 (79.24–82.78) | 121.81 (120.81–122.81) | 1.51 | 10024 | 136720 |
| BGR-4 | 87.58 (87.45–89.92) | 122.02 (120.79–128.56) | 1.39 | 8984 | 112728 |
| BGR-12 | 63.69 (63.27–64.05) | 107.89 (107.43–108.88) | 1.69 | 8232 | 91256 |
| CHE-1 | 94.42 (93.36–95.10) | 138.80 (137.01–140.66) | 1.47 | 10560 | 142504 |
| CHE-4 | 100.16 (99.27–100.26) | 142.59 (140.10–143.71) | 1.42 | 9488 | 116736 |
| CHE-12 | 78.57 (78.50–81.30) | 134.19 (133.96–136.15) | 1.71 | 8784 | 97240 |
| LSO-1 | 29.84 (29.50–35.97) | 31.74 (29.43–31.87) | 1.06 | 4416 | 51192 |
| LSO-4 | 31.17 (30.95–31.63) | 33.27 (32.30–33.51) | 1.07 | 4064 | 44264 |
| LSO-12 | 23.51 (22.65–23.92) | 29.90 (28.83–30.31) | 1.27 | 3728 | 34032 |
| NPL-1 | 86.78 (86.13–87.37) | 142.19 (139.64–144.45) | 1.64 | 10888 | 141136 |
| NPL-4 | 80.87 (78.95–81.03) | 139.40 (136.54–139.71) | 1.72 | 9800 | 117288 |
| NPL-12 | 64.88 (64.84–67.25) | 115.38 (111.68–117.26) | 1.78 | 9144 | 94928 |

The complete consumer includes conversion for SimplifyPath, simplification, conversion back to Point2[], and the scorer. Frozen-file loading, provenance verification, and SVG/report writing are outside the timed operation. These warm numbers exclude first use and retained workspace. Area change measures changed filled region; it does not bound the largest contour displacement.

Raw process records include assembly hashes, exact values, batch iteration counts, times, and allocated bytes. This small proposed use case demonstrates measurable simplification error; it establishes neither demand from customers nor recognition accuracy.

See [input provenance and reproduction commands](README.md) and the [compact evidence manifest](../../benchmarks/winding-review-evidence.json). All twelve overlays and raw samples are regenerated under ignored `artifacts/`; three representative overlays are retained here.
