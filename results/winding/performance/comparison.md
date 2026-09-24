# Managed winding comparison

Median of three process medians, microseconds per operation.

| Workload | Vertices per path | Baseline us | Optimized us | Baseline / optimized | Optimized bytes/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| degenerate-grid | 64 | 459.98 | 409.60 | 1.12 | 0 |
| degenerate-grid | 256 | 8256.38 | 7539.10 | 1.10 | 0 |
| dense-graph | 64 | 13.93 | 12.36 | 1.13 | 0 |
| dense-graph | 256 | 57.36 | 52.22 | 1.10 | 0 |
| dense-graph | 1024 | 269.13 | 246.34 | 1.09 | 0 |
| filled-regions | 64 | 14.85 | 14.16 | 1.05 | 0 |
| filled-regions | 256 | 66.31 | 56.66 | 1.17 | 0 |
| filled-regions | 1024 | 289.75 | 267.87 | 1.08 | 0 |
| random-walks | 64 | 26.13 | 17.65 | 1.48 | 0 |
| random-walks | 256 | 217.34 | 157.73 | 1.38 | 0 |
| random-walks | 1024 | 1279.82 | 964.10 | 1.33 | 0 |
| similar-strokes | 64 | 13.82 | 9.98 | 1.39 | 0 |
| similar-strokes | 256 | 46.51 | 35.32 | 1.32 | 0 |
| similar-strokes | 1024 | 216.15 | 175.42 | 1.23 | 0 |
