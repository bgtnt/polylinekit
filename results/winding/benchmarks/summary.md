# Winding-area benchmark summary

Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.

Measured source: `1094dcaa486e146fd5341e01dd11bf0ef76f1deb`; runtime .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel.

| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |
|:---|---:|:---|---:|---:|---:|
| degenerate-grid | 64 | ClipperEndpointBridged | 323.53 (321.72–327.70) | 249,208 | 0.73 |
| degenerate-grid | 64 | WindingEndpointBridged | 440.92 (438.51–442.64) | 0 |  |
| degenerate-grid | 256 | ClipperEndpointBridged | 5295.05 (5283.30–5350.52) | 2,036,144 | 0.66 |
| degenerate-grid | 256 | WindingEndpointBridged | 7966.20 (7891.90–7994.32) | 0 |  |
| dense-graph | 64 | ClipperEndpointBridged | 28.57 (28.39–28.66) | 66,536 | 2.12 |
| dense-graph | 64 | GraphIntegral | 3.46 (3.40–3.62) | 0 |  |
| dense-graph | 64 | WindingEndpointBridged | 13.50 (13.47–13.59) | 0 |  |
| dense-graph | 256 | ClipperEndpointBridged | 116.13 (114.55–116.77) | 260,168 | 2.05 |
| dense-graph | 256 | GraphIntegral | 13.96 (13.65–14.46) | 0 |  |
| dense-graph | 256 | WindingEndpointBridged | 56.65 (55.50–56.65) | 0 |  |
| dense-graph | 1024 | ClipperEndpointBridged | 479.32 (474.90–482.81) | 1,034,408 | 1.83 |
| dense-graph | 1024 | GraphIntegral | 56.05 (54.70–58.16) | 0 |  |
| dense-graph | 1024 | WindingEndpointBridged | 262.03 (260.94–264.14) | 0 |  |
| filled-regions | 64 | ClipperFilledRegionOverlap | 50.92 (49.68–51.97) | 63,992 | 3.91 |
| filled-regions | 64 | WindingFilledRegions | 13.03 (12.91–13.18) | 0 |  |
| filled-regions | 256 | ClipperFilledRegionOverlap | 189.04 (188.00–189.67) | 214,616 | 3.14 |
| filled-regions | 256 | WindingFilledRegions | 60.27 (59.73–60.92) | 0 |  |
| filled-regions | 1024 | ClipperFilledRegionOverlap | 709.38 (703.42–709.96) | 816,920 | 2.70 |
| filled-regions | 1024 | WindingFilledRegions | 262.88 (262.15–263.65) | 0 |  |
| random-walks | 64 | ClipperEndpointBridged | 54.66 (54.63–55.54) | 69,984 | 2.07 |
| random-walks | 64 | WindingEndpointBridged | 26.37 (25.89–26.39) | 0 |  |
| random-walks | 256 | ClipperEndpointBridged | 317.67 (314.75–319.06) | 239,552 | 1.53 |
| random-walks | 256 | WindingEndpointBridged | 207.17 (206.65–208.51) | 0 |  |
| random-walks | 1024 | ClipperEndpointBridged | 2190.01 (2179.03–2198.30) | 1,019,432 | 1.74 |
| random-walks | 1024 | WindingEndpointBridged | 1260.83 (1257.77–1260.86) | 0 |  |
| similar-strokes | 64 | ClipperEndpointBridged | 25.51 (25.42–25.69) | 38,960 | 1.91 |
| similar-strokes | 64 | WindingEndpointBridged | 13.32 (13.22–13.35) | 0 |  |
| similar-strokes | 256 | ClipperEndpointBridged | 96.30 (96.12–96.64) | 130,920 | 2.14 |
| similar-strokes | 256 | WindingEndpointBridged | 45.08 (44.05–45.18) | 0 |  |
| similar-strokes | 1024 | ClipperEndpointBridged | 357.97 (357.55–362.67) | 464,936 | 1.73 |
| similar-strokes | 1024 | WindingEndpointBridged | 207.06 (207.01–209.19) | 0 |  |
