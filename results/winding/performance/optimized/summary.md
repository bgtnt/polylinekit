# Winding-area benchmark summary

Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.

Measured source: `6fc1c35fc61ff4d116c65938daddf2255d0e6be2`; runtime .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel.

| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |
|:---|---:|:---|---:|---:|---:|
| degenerate-grid | 64 | ClipperEndpointBridged | 334.45 (334.25–342.93) | 249,208 | 0.82 |
| degenerate-grid | 64 | WindingEndpointBridged | 409.60 (408.31–410.23) | 0 |  |
| degenerate-grid | 256 | ClipperEndpointBridged | 5538.93 (5425.38–5709.98) | 2,036,144 | 0.73 |
| degenerate-grid | 256 | WindingEndpointBridged | 7539.10 (7430.85–7673.48) | 0 |  |
| dense-graph | 64 | ClipperEndpointBridged | 30.08 (29.71–32.01) | 66,536 | 2.43 |
| dense-graph | 64 | GraphIntegral | 3.61 (3.54–3.77) | 0 |  |
| dense-graph | 64 | WindingEndpointBridged | 12.36 (12.25–13.06) | 0 |  |
| dense-graph | 256 | ClipperEndpointBridged | 120.29 (119.81–123.86) | 260,168 | 2.30 |
| dense-graph | 256 | GraphIntegral | 14.16 (14.08–15.75) | 0 |  |
| dense-graph | 256 | WindingEndpointBridged | 52.22 (51.52–53.08) | 0 |  |
| dense-graph | 1024 | ClipperEndpointBridged | 507.02 (502.25–536.69) | 1,034,408 | 2.06 |
| dense-graph | 1024 | GraphIntegral | 56.58 (56.53–59.37) | 0 |  |
| dense-graph | 1024 | WindingEndpointBridged | 246.34 (245.86–246.82) | 0 |  |
| filled-regions | 64 | ClipperFilledRegionOverlap | 53.12 (53.05–54.86) | 63,992 | 3.75 |
| filled-regions | 64 | WindingFilledRegions | 14.16 (13.95–14.23) | 0 |  |
| filled-regions | 256 | ClipperFilledRegionOverlap | 194.09 (190.74–198.24) | 214,616 | 3.43 |
| filled-regions | 256 | WindingFilledRegions | 56.66 (56.31–57.65) | 0 |  |
| filled-regions | 1024 | ClipperFilledRegionOverlap | 780.08 (733.65–790.35) | 816,920 | 2.91 |
| filled-regions | 1024 | WindingFilledRegions | 267.87 (264.99–267.88) | 0 |  |
| random-walks | 64 | ClipperEndpointBridged | 59.69 (57.31–60.49) | 69,984 | 3.38 |
| random-walks | 64 | WindingEndpointBridged | 17.65 (17.23–18.72) | 0 |  |
| random-walks | 256 | ClipperEndpointBridged | 334.82 (329.45–338.62) | 239,552 | 2.12 |
| random-walks | 256 | WindingEndpointBridged | 157.73 (157.35–160.40) | 0 |  |
| random-walks | 1024 | ClipperEndpointBridged | 2314.61 (2275.05–2425.44) | 1,019,432 | 2.40 |
| random-walks | 1024 | WindingEndpointBridged | 964.10 (944.94–967.02) | 0 |  |
| similar-strokes | 64 | ClipperEndpointBridged | 26.56 (26.37–28.31) | 38,960 | 2.66 |
| similar-strokes | 64 | WindingEndpointBridged | 9.98 (9.93–10.29) | 0 |  |
| similar-strokes | 256 | ClipperEndpointBridged | 98.63 (98.34–101.00) | 130,920 | 2.79 |
| similar-strokes | 256 | WindingEndpointBridged | 35.32 (35.07–35.62) | 0 |  |
| similar-strokes | 1024 | ClipperEndpointBridged | 371.28 (369.04–372.82) | 464,936 | 2.12 |
| similar-strokes | 1024 | WindingEndpointBridged | 175.42 (175.14–179.13) | 0 |  |
