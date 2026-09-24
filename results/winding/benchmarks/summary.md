# Winding-area benchmark summary

Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.

Measured source: `2bf4182dce165e6cd15668d7b7bf72447fd49b84`; runtime .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel.

| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |
|:---|---:|:---|---:|---:|---:|
| degenerate-grid | 64 | ClipperEndpointBridged | 330.56 (327.88–344.20) | 249,209 | 1.01 |
| degenerate-grid | 64 | WindingEndpointBridged | 327.54 (324.80–330.01) | 1 |  |
| degenerate-grid | 256 | ClipperEndpointBridged | 5588.80 (5240.27–5666.70) | 2,036,154 | 0.97 |
| degenerate-grid | 256 | WindingEndpointBridged | 5791.12 (5640.43–5889.65) | 10 |  |
| dense-graph | 64 | ClipperEndpointBridged | 29.15 (29.08–30.67) | 66,536 | 2.73 |
| dense-graph | 64 | GraphIntegral | 3.47 (3.45–3.74) | 0 |  |
| dense-graph | 64 | WindingEndpointBridged | 10.66 (10.63–11.79) | 0 |  |
| dense-graph | 256 | ClipperEndpointBridged | 118.23 (112.25–119.29) | 260,168 | 2.68 |
| dense-graph | 256 | GraphIntegral | 13.74 (13.71–14.34) | 0 |  |
| dense-graph | 256 | WindingEndpointBridged | 44.20 (43.92–51.41) | 0 |  |
| dense-graph | 1024 | ClipperEndpointBridged | 495.70 (485.05–507.47) | 1,034,409 | 2.31 |
| dense-graph | 1024 | GraphIntegral | 54.92 (54.87–56.05) | 0 |  |
| dense-graph | 1024 | WindingEndpointBridged | 214.23 (213.05–214.24) | 0 |  |
| filled-regions | 64 | ClipperFilledRegionOverlap | 53.46 (51.16–53.91) | 63,992 | 6.13 |
| filled-regions | 64 | WindingFilledRegions | 8.72 (8.67–8.72) | 0 |  |
| filled-regions | 256 | ClipperFilledRegionOverlap | 190.72 (188.02–200.11) | 214,616 | 4.91 |
| filled-regions | 256 | WindingFilledRegions | 38.83 (37.52–39.00) | 0 |  |
| filled-regions | 1024 | ClipperFilledRegionOverlap | 726.37 (716.58–738.42) | 816,921 | 3.78 |
| filled-regions | 1024 | WindingFilledRegions | 192.18 (187.91–197.89) | 0 |  |
| random-walks | 64 | ClipperEndpointBridged | 55.79 (55.67–58.94) | 69,984 | 2.52 |
| random-walks | 64 | WindingEndpointBridged | 22.10 (21.95–22.15) | 0 |  |
| random-walks | 256 | ClipperEndpointBridged | 322.50 (319.49–324.44) | 239,553 | 1.86 |
| random-walks | 256 | WindingEndpointBridged | 173.05 (172.76–174.69) | 0 |  |
| random-walks | 1024 | ClipperEndpointBridged | 2287.31 (2220.66–2459.56) | 1,019,434 | 2.15 |
| random-walks | 1024 | WindingEndpointBridged | 1065.37 (1056.75–1066.40) | 1 |  |
| similar-strokes | 64 | ClipperEndpointBridged | 25.71 (25.71–26.73) | 38,960 | 2.28 |
| similar-strokes | 64 | WindingEndpointBridged | 11.25 (11.24–11.47) | 0 |  |
| similar-strokes | 256 | ClipperEndpointBridged | 97.20 (96.89–105.91) | 130,920 | 2.50 |
| similar-strokes | 256 | WindingEndpointBridged | 38.81 (37.71–40.15) | 0 |  |
| similar-strokes | 1024 | ClipperEndpointBridged | 370.07 (361.41–370.75) | 464,937 | 2.08 |
| similar-strokes | 1024 | WindingEndpointBridged | 178.03 (176.96–178.14) | 0 |  |
