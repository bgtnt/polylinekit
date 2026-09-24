# Native microkernel results

Median of the three fresh-process medians; 9 samples in each. `exclusive-run*.jsonl` only; preliminary run files are excluded.

| Kernel | N | C# array ns | C# pointer ns | C++ with P/Invoke ns | array/native | pointer/native |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| box_pairs_dense | 16 | 107.34 | 136.48 | 113.06 | 0.95x | 1.21x |
| box_pairs_dense | 64 | 1752.86 | 2345.92 | 1692.63 | 1.04x | 1.39x |
| box_pairs_dense | 256 | 78130.47 | 93667.97 | 47110.16 | 1.66x | 1.99x |
| box_pairs_dense | 1024 | 1584812.50 | 1802037.50 | 1148125.00 | 1.38x | 1.57x |
| box_pairs_sparse | 16 | 44.39 | 44.80 | 38.89 | 1.14x | 1.15x |
| box_pairs_sparse | 64 | 190.83 | 191.50 | 145.23 | 1.31x | 1.32x |
| box_pairs_sparse | 256 | 776.49 | 744.34 | 582.22 | 1.33x | 1.28x |
| box_pairs_sparse | 1024 | 3351.34 | 4229.22 | 2413.28 | 1.39x | 1.75x |
| edge_sum | 0 | 3.09 | 2.72 | 8.42 | 0.37x | 0.32x |
| edge_sum | 16 | 77.99 | 91.71 | 85.90 | 0.91x | 1.07x |
| edge_sum | 64 | 307.79 | 363.86 | 333.48 | 0.92x | 1.09x |
| edge_sum | 256 | 1230.68 | 1467.62 | 1310.51 | 0.94x | 1.12x |
| edge_sum | 1024 | 4943.75 | 6064.89 | 5217.29 | 0.95x | 1.16x |
| edge_sum | 16384 | 80599.22 | 96096.09 | 86039.06 | 0.94x | 1.12x |

Maximum measured allocation: 0 bytes per operation.

This measures only two small kernels. It does not measure the complete geometry operation, native input adaptation, sorting, exact predicates, boundary chains, or native workspace ownership. No full-engine C++ speedup is established.
