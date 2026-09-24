# Winding-area benchmark summary

Median of three process medians (nine batch samples each), microseconds per operation, with the process-median range. Bytes are per-thread managed allocations per operation. Vertices are per path. Values describe this machine and these deterministic fixtures only.

Measured source: `a0b32953f0178e94b9df4910bcca65157589bfb2`; runtime .NET 10.0.12; Microsoft Windows 10.0.26200; Intel64 Family 6 Model 158 Stepping 12, GenuineIntel.

| Workload | Vertices | Method | Median µs (range) | Bytes/op | Clipper time / winding time |
|:---|---:|:---|---:|---:|---:|
| degenerate-grid | 64 | ClipperEndpointBridged | 330.43 (323.92–331.52) | 249,209 | 1.04 |
| degenerate-grid | 64 | WindingEndpointBridged | 316.34 (315.94–318.72) | 1 |  |
| degenerate-grid | 256 | ClipperEndpointBridged | 5579.52 (5179.70–5629.60) | 2,036,154 | 1.00 |
| degenerate-grid | 256 | WindingEndpointBridged | 5591.48 (5559.55–5611.00) | 10 |  |
| dense-graph | 64 | ClipperEndpointBridged | 29.36 (28.96–29.80) | 66,536 | 2.87 |
| dense-graph | 64 | GraphIntegral | 3.54 (3.48–3.77) | 0 |  |
| dense-graph | 64 | WindingEndpointBridged | 10.24 (10.11–10.31) | 0 |  |
| dense-graph | 256 | ClipperEndpointBridged | 116.38 (115.78–118.00) | 260,168 | 2.72 |
| dense-graph | 256 | GraphIntegral | 14.04 (13.86–14.54) | 0 |  |
| dense-graph | 256 | WindingEndpointBridged | 42.84 (42.61–43.46) | 0 |  |
| dense-graph | 1024 | ClipperEndpointBridged | 480.53 (479.84–481.78) | 1,034,409 | 2.31 |
| dense-graph | 1024 | GraphIntegral | 56.03 (55.51–58.86) | 0 |  |
| dense-graph | 1024 | WindingEndpointBridged | 207.87 (207.66–211.00) | 0 |  |
| filled-regions | 64 | ClipperFilledRegionOverlap | 51.29 (50.87–52.00) | 63,992 | 6.27 |
| filled-regions | 64 | WindingFilledRegions | 8.18 (8.08–8.23) | 0 |  |
| filled-regions | 256 | ClipperFilledRegionOverlap | 188.27 (187.56–189.43) | 214,616 | 5.05 |
| filled-regions | 256 | WindingFilledRegions | 37.25 (35.43–37.34) | 0 |  |
| filled-regions | 1024 | ClipperFilledRegionOverlap | 726.48 (711.56–738.92) | 816,921 | 4.07 |
| filled-regions | 1024 | WindingFilledRegions | 178.69 (178.38–181.51) | 0 |  |
| random-walks | 64 | ClipperEndpointBridged | 55.24 (54.79–56.58) | 69,984 | 2.56 |
| random-walks | 64 | WindingEndpointBridged | 21.54 (21.18–21.90) | 0 |  |
| random-walks | 256 | ClipperEndpointBridged | 320.86 (316.10–337.58) | 239,553 | 1.91 |
| random-walks | 256 | WindingEndpointBridged | 167.77 (167.36–169.11) | 0 |  |
| random-walks | 1024 | ClipperEndpointBridged | 2235.88 (2194.48–2258.74) | 1,019,434 | 2.13 |
| random-walks | 1024 | WindingEndpointBridged | 1048.43 (1046.01–1050.48) | 1 |  |
| similar-strokes | 64 | ClipperEndpointBridged | 26.44 (25.37–26.83) | 38,960 | 2.27 |
| similar-strokes | 64 | WindingEndpointBridged | 11.63 (11.41–11.67) | 0 |  |
| similar-strokes | 256 | ClipperEndpointBridged | 98.27 (97.83–99.15) | 130,920 | 2.54 |
| similar-strokes | 256 | WindingEndpointBridged | 38.72 (38.33–38.81) | 0 |  |
| similar-strokes | 1024 | ClipperEndpointBridged | 361.63 (354.63–363.15) | 464,937 | 2.05 |
| similar-strokes | 1024 | WindingEndpointBridged | 176.23 (174.74–178.20) | 0 |  |
