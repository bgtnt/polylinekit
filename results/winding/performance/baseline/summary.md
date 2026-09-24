# Winding-area benchmark summary

Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.

Measured source: `9fff4d2e7ea453c1c6a6c504485642164cf4065b`; runtime .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel.

| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |
|:---|---:|:---|---:|---:|---:|
| degenerate-grid | 64 | ClipperEndpointBridged | 331.89 (331.10–336.81) | 249,208 | 0.72 |
| degenerate-grid | 64 | WindingEndpointBridged | 459.98 (457.74–507.23) | 0 |  |
| degenerate-grid | 256 | ClipperEndpointBridged | 5517.82 (5489.27–5576.68) | 2,036,144 | 0.67 |
| degenerate-grid | 256 | WindingEndpointBridged | 8256.38 (8188.57–8462.17) | 0 |  |
| dense-graph | 64 | ClipperEndpointBridged | 30.31 (29.58–31.70) | 66,536 | 2.18 |
| dense-graph | 64 | GraphIntegral | 3.57 (3.44–3.64) | 0 |  |
| dense-graph | 64 | WindingEndpointBridged | 13.93 (13.89–14.05) | 0 |  |
| dense-graph | 256 | ClipperEndpointBridged | 118.87 (117.62–120.14) | 260,168 | 2.07 |
| dense-graph | 256 | GraphIntegral | 14.22 (13.74–14.59) | 0 |  |
| dense-graph | 256 | WindingEndpointBridged | 57.36 (56.55–59.26) | 0 |  |
| dense-graph | 1024 | ClipperEndpointBridged | 499.21 (498.52–501.16) | 1,034,408 | 1.85 |
| dense-graph | 1024 | GraphIntegral | 57.18 (55.25–59.00) | 0 |  |
| dense-graph | 1024 | WindingEndpointBridged | 269.13 (268.28–269.18) | 0 |  |
| filled-regions | 64 | ClipperFilledRegionOverlap | 52.62 (51.89–54.03) | 63,992 | 3.54 |
| filled-regions | 64 | WindingFilledRegions | 14.85 (14.71–15.28) | 0 |  |
| filled-regions | 256 | ClipperFilledRegionOverlap | 193.47 (192.38–200.26) | 214,616 | 2.92 |
| filled-regions | 256 | WindingFilledRegions | 66.31 (65.45–70.11) | 0 |  |
| filled-regions | 1024 | ClipperFilledRegionOverlap | 733.23 (729.23–876.86) | 816,920 | 2.53 |
| filled-regions | 1024 | WindingFilledRegions | 289.75 (289.67–292.88) | 0 |  |
| random-walks | 64 | ClipperEndpointBridged | 57.45 (56.77–58.08) | 69,984 | 2.20 |
| random-walks | 64 | WindingEndpointBridged | 26.13 (25.60–26.77) | 0 |  |
| random-walks | 256 | ClipperEndpointBridged | 322.52 (322.29–344.27) | 239,552 | 1.48 |
| random-walks | 256 | WindingEndpointBridged | 217.34 (217.29–223.12) | 0 |  |
| random-walks | 1024 | ClipperEndpointBridged | 2255.29 (2239.51–2387.13) | 1,019,432 | 1.76 |
| random-walks | 1024 | WindingEndpointBridged | 1279.82 (1275.44–1298.78) | 0 |  |
| similar-strokes | 64 | ClipperEndpointBridged | 26.95 (26.25–28.11) | 38,960 | 1.95 |
| similar-strokes | 64 | WindingEndpointBridged | 13.82 (13.80–14.41) | 0 |  |
| similar-strokes | 256 | ClipperEndpointBridged | 99.29 (99.26–102.54) | 130,920 | 2.13 |
| similar-strokes | 256 | WindingEndpointBridged | 46.51 (46.15–47.16) | 0 |  |
| similar-strokes | 1024 | ClipperEndpointBridged | 379.98 (365.92–382.98) | 464,936 | 1.76 |
| similar-strokes | 1024 | WindingEndpointBridged | 216.15 (213.99–219.44) | 0 |  |
