# Managed ablations

Three fresh processes per variant; five samples per workload in each.

`scalar` retains all managed improvements but has no FMA product. `predicates-only` retains the old sweep/input processing with optimized predicates and FMA. `checkout` is the experimental FMA version at 97d1a97. The final implementation at 6fc1c35 keeps the scalar version.

| Workload | N | Baseline us | Scalar us | Predicates only us | All with FMA us | Scalar / FMA | Baseline / scalar |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| degenerate-grid | 64 | 455.38 | 407.13 | 419.63 | 401.78 | 1.013 | 1.12 |
| degenerate-grid | 256 | 8140.23 | 7280.73 | 7383.65 | 7351.23 | 0.990 | 1.12 |
| dense-graph | 64 | 13.70 | 12.10 | 13.56 | 12.07 | 1.003 | 1.13 |
| dense-graph | 256 | 55.81 | 51.05 | 55.84 | 50.29 | 1.015 | 1.09 |
| dense-graph | 1024 | 262.03 | 243.52 | 260.71 | 242.72 | 1.003 | 1.08 |
| filled-regions | 64 | 14.59 | 13.71 | 14.41 | 13.84 | 0.991 | 1.06 |
| filled-regions | 256 | 67.75 | 58.05 | 67.26 | 55.67 | 1.043 | 1.17 |
| filled-regions | 1024 | 289.00 | 259.91 | 291.48 | 262.67 | 0.990 | 1.11 |
| horizontal-bands | 256 | 25.75 | 18.44 | 18.52 | 18.42 | 1.001 | 1.40 |
| horizontal-bands | 1024 | 109.54 | 78.79 | 80.81 | 80.88 | 0.974 | 1.39 |
| horizontal-bands | 4096 | 550.84 | 425.45 | 437.06 | 427.76 | 0.995 | 1.29 |
| random-walks | 64 | 24.91 | 16.71 | 23.86 | 16.51 | 1.012 | 1.49 |
| random-walks | 256 | 218.97 | 155.69 | 213.35 | 154.44 | 1.008 | 1.41 |
| random-walks | 1024 | 1294.75 | 943.50 | 1287.79 | 935.20 | 1.009 | 1.37 |
| similar-strokes | 64 | 13.29 | 9.96 | 11.72 | 9.61 | 1.037 | 1.33 |
| similar-strokes | 256 | 45.38 | 33.95 | 45.22 | 33.80 | 1.004 | 1.34 |
| similar-strokes | 1024 | 218.34 | 172.67 | 217.94 | 172.54 | 1.001 | 1.26 |
| vertical-bands | 256 | 94.18 | 18.52 | 86.12 | 18.50 | 1.001 | 5.09 |
| vertical-bands | 1024 | 1224.15 | 80.28 | 1185.35 | 79.21 | 1.014 | 15.25 |
| vertical-bands | 4096 | 17838.20 | 428.23 | 17624.90 | 429.05 | 0.998 | 41.66 |
