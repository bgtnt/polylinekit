# Winding-area benchmark summary

Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.

Measured source: `becc37d033f0a0c1466610e2130c50121d6465fa`; runtime .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel.

| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |
|:---|---:|:---|---:|---:|---:|
| degenerate-grid | 64 | ClipperEndpointBridged | 324.59 (323.05–326.29) | 249,208 | 0.71 |
| degenerate-grid | 64 | WindingEndpointBridged | 457.89 (453.15–458.62) | 0 |  |
| degenerate-grid | 256 | ClipperEndpointBridged | 5444.20 (5340.20–5473.43) | 2,036,144 | 0.68 |
| degenerate-grid | 256 | WindingEndpointBridged | 7999.57 (7939.10–8011.57) | 0 |  |
| dense-graph | 64 | ClipperEndpointBridged | 28.72 (28.71–28.78) | 66,536 | 2.11 |
| dense-graph | 64 | GraphIntegral | 3.46 (3.39–3.55) | 0 |  |
| dense-graph | 64 | WindingEndpointBridged | 13.58 (13.43–13.64) | 0 |  |
| dense-graph | 256 | ClipperEndpointBridged | 115.46 (115.07–115.50) | 260,168 | 2.05 |
| dense-graph | 256 | GraphIntegral | 13.83 (13.60–14.26) | 0 |  |
| dense-graph | 256 | WindingEndpointBridged | 56.34 (55.97–56.86) | 0 |  |
| dense-graph | 1024 | ClipperEndpointBridged | 477.79 (476.97–486.27) | 1,034,408 | 1.82 |
| dense-graph | 1024 | GraphIntegral | 55.53 (54.66–57.12) | 0 |  |
| dense-graph | 1024 | WindingEndpointBridged | 262.82 (259.53–265.20) | 0 |  |
| filled-regions | 64 | ClipperFilledRegionOverlap | 52.23 (51.73–52.65) | 63,992 | 3.62 |
| filled-regions | 64 | WindingFilledRegions | 14.44 (14.35–14.49) | 0 |  |
| filled-regions | 256 | ClipperFilledRegionOverlap | 190.55 (189.09–196.15) | 214,616 | 2.90 |
| filled-regions | 256 | WindingFilledRegions | 65.78 (65.02–67.08) | 0 |  |
| filled-regions | 1024 | ClipperFilledRegionOverlap | 724.23 (714.20–742.33) | 816,920 | 2.54 |
| filled-regions | 1024 | WindingFilledRegions | 285.57 (283.82–289.96) | 0 |  |
| random-walks | 64 | ClipperEndpointBridged | 54.91 (54.76–54.98) | 69,984 | 2.10 |
| random-walks | 64 | WindingEndpointBridged | 26.18 (26.01–26.39) | 0 |  |
| random-walks | 256 | ClipperEndpointBridged | 318.16 (315.98–321.13) | 239,552 | 1.47 |
| random-walks | 256 | WindingEndpointBridged | 217.12 (215.63–218.41) | 0 |  |
| random-walks | 1024 | ClipperEndpointBridged | 2203.45 (2202.18–2238.25) | 1,019,432 | 1.74 |
| random-walks | 1024 | WindingEndpointBridged | 1268.01 (1264.16–1272.14) | 0 |  |
| similar-strokes | 64 | ClipperEndpointBridged | 27.00 (25.83–27.53) | 38,960 | 2.03 |
| similar-strokes | 64 | WindingEndpointBridged | 13.27 (13.20–13.48) | 0 |  |
| similar-strokes | 256 | ClipperEndpointBridged | 97.36 (97.35–97.91) | 130,920 | 2.13 |
| similar-strokes | 256 | WindingEndpointBridged | 45.70 (44.31–45.72) | 0 |  |
| similar-strokes | 1024 | ClipperEndpointBridged | 364.83 (357.42–394.52) | 464,936 | 1.71 |
| similar-strokes | 1024 | WindingEndpointBridged | 213.33 (212.44–216.45) | 0 |  |
