# Integrated sweep measurements

Microseconds per complete call; medians of three process medians. Ratio > 1 favors integration.
Outcome: 0 = no certificate attempted; 1 = rejected and resumed; 2 = certified simple.

| Group | Workload | Vertices | Outcome | Baseline us | Integrated us | Ratio | Integrated range us |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| development | similar-strokes | 128 | 0 | 10.231 | 10.750 | 0.95x | 10.644-11.561 |
| development | dense-graph | 128 | 0 | 12.959 | 13.282 | 0.98x | 13.221-13.465 |
| development | random-walks | 128 | 0 | 17.403 | 17.208 | 1.01x | 17.128-19.461 |
| development | similar-strokes | 512 | 0 | 33.964 | 36.467 | 0.93x | 35.895-36.702 |
| development | dense-graph | 512 | 0 | 47.069 | 48.272 | 0.98x | 48.032-48.580 |
| development | random-walks | 512 | 0 | 143.658 | 147.566 | 0.97x | 145.693-150.048 |
| development | similar-strokes | 2048 | 0 | 140.902 | 147.730 | 0.95x | 145.331-159.595 |
| development | dense-graph | 2048 | 0 | 187.759 | 191.657 | 0.98x | 190.936-195.833 |
| development | random-walks | 2048 | 0 | 750.316 | 726.534 | 1.03x | 722.581-727.803 |
| development | degenerate-grid | 128 | 0 | 412.236 | 406.873 | 1.01x | 402.894-408.127 |
| development | degenerate-grid | 512 | 0 | 7049.650 | 7269.025 | 0.97x | 7031.625-7716.800 |
| development | ring | 64 | 0 | 3.948 | 4.186 | 0.94x | 4.157-4.195 |
| development | radial-star | 64 | 0 | 7.037 | 7.190 | 0.98x | 7.185-7.237 |
| development | stacked-bars | 64 | 0 | 12.900 | 12.919 | 1.00x | 12.792-12.932 |
| development | diagonal-bars | 64 | 0 | 21.858 | 21.884 | 1.00x | 21.588-32.035 |
| development | simple-comb | 66 | 0 | 5.036 | 4.937 | 1.02x | 4.901-7.937 |
| development | simple-diagonal-comb | 66 | 0 | 13.640 | 13.509 | 1.01x | 13.360-15.533 |
| development | vertical-band | 64 | 0 | 4.718 | 4.627 | 1.02x | 4.606-4.687 |
| development | ring | 256 | 0 | 14.261 | 14.956 | 0.95x | 14.947-15.954 |
| development | radial-star | 256 | 2 | 85.203 | 108.020 | 0.79x | 106.731-108.241 |
| development | stacked-bars | 256 | 0 | 84.115 | 85.186 | 0.99x | 82.233-86.448 |
| development | diagonal-bars | 256 | 0 | 211.696 | 212.023 | 1.00x | 211.975-214.215 |
| development | simple-comb | 258 | 0 | 29.879 | 29.983 | 1.00x | 29.666-30.357 |
| development | simple-diagonal-comb | 258 | 2 | 157.486 | 88.699 | 1.78x | 86.363-88.723 |
| development | vertical-band | 256 | 0 | 16.320 | 18.262 | 0.89x | 17.248-20.021 |
| development | ring | 1024 | 0 | 54.734 | 59.368 | 0.92x | 58.753-59.935 |
| development | radial-star | 1024 | 2 | 1242.741 | 481.733 | 2.58x | 476.870-489.736 |
| development | stacked-bars | 1024 | 0 | 789.581 | 781.375 | 1.01x | 776.278-791.019 |
| development | diagonal-bars | 1024 | 0 | 2759.875 | 2850.350 | 0.97x | 2795.438-4030.488 |
| development | simple-comb | 1026 | 0 | 265.291 | 265.708 | 1.00x | 262.823-265.953 |
| development | simple-diagonal-comb | 1026 | 2 | 2238.875 | 401.398 | 5.58x | 389.520-402.970 |
| development | vertical-band | 1024 | 0 | 63.075 | 67.140 | 0.94x | 66.508-67.872 |
| development | ring | 4096 | 0 | 218.213 | 235.278 | 0.93x | 234.743-243.901 |
| development | radial-star | 4096 | 2 | 19202.200 | 2082.206 | 9.22x | 2045.819-2107.206 |
| development | stacked-bars | 4096 | 0 | 4777.812 | 4623.738 | 1.03x | 4617.938-5180.575 |
| development | diagonal-bars | 4096 | 0 | 36196.500 | 37005.400 | 0.98x | 36635.800-40708.200 |
| development | simple-comb | 4098 | 0 | 3229.975 | 3177.550 | 1.02x | 3165.738-3178.938 |
| development | simple-diagonal-comb | 4098 | 2 | 35211.800 | 1763.406 | 19.97x | 1753.544-1802.031 |
| development | vertical-band | 4096 | 0 | 251.195 | 265.938 | 0.94x | 263.815-266.870 |
| development | late-crossing | 6 | 0 | 0.491 | 0.496 | 0.99x | 0.493-0.496 |
| development | late-crossing-comb | 1026 | 1 | 2251.269 | 2486.725 | 0.91x | 2482.150-2861.175 |
| development | late-crossing-comb | 4098 | 1 | 35229.600 | 36421.600 | 0.97x | 36153.400-36421.900 |
| fresh | fresh-perturbed-outline | 64 | 0 | 3.897 | 4.081 | 0.95x | 4.071-4.260 |
| fresh | fresh-thin-ribbon | 64 | 0 | 4.728 | 4.959 | 0.95x | 4.917-5.031 |
| fresh | fresh-sheared-canyon | 64 | 0 | 12.765 | 13.147 | 0.97x | 13.030-13.427 |
| fresh | fresh-subdivided-parallelogram | 64 | 0 | 6.026 | 6.149 | 0.98x | 6.063-6.236 |
| fresh | fresh-retraced-outline | 64 | 0 | 8.916 | 8.995 | 0.99x | 8.941-9.067 |
| fresh | fresh-touching-rectangles | 64 | 0 | 4.808 | 4.963 | 0.97x | 4.943-4.972 |
| fresh | fresh-late-crossing | 64 | 0 | 4.422 | 4.708 | 0.94x | 4.579-5.252 |
| fresh | fresh-random-walk | 64 | 0 | 7.037 | 7.017 | 1.00x | 7.005-7.218 |
| fresh | fresh-rotated-two-lobes | 64 | 0 | 3.913 | 4.098 | 0.95x | 4.090-4.125 |
| fresh | fresh-perturbed-outline | 257 | 0 | 15.694 | 16.246 | 0.97x | 16.215-16.701 |
| fresh | fresh-thin-ribbon | 257 | 0 | 16.357 | 17.522 | 0.93x | 17.461-17.612 |
| fresh | fresh-sheared-canyon | 257 | 2 | 174.323 | 95.443 | 1.83x | 94.809-98.180 |
| fresh | fresh-subdivided-parallelogram | 257 | 0 | 21.325 | 21.943 | 0.97x | 21.872-22.085 |
| fresh | fresh-retraced-outline | 257 | 0 | 38.479 | 38.518 | 1.00x | 37.996-39.367 |
| fresh | fresh-touching-rectangles | 257 | 0 | 18.603 | 19.082 | 0.97x | 19.047-19.139 |
| fresh | fresh-late-crossing | 257 | 0 | 14.241 | 15.234 | 0.93x | 15.217-15.282 |
| fresh | fresh-random-walk | 257 | 0 | 49.743 | 49.373 | 1.01x | 49.314-49.381 |
| fresh | fresh-rotated-two-lobes | 257 | 0 | 14.581 | 15.626 | 0.93x | 15.579-15.754 |
| fresh | fresh-perturbed-outline | 1024 | 0 | 90.510 | 94.339 | 0.96x | 93.767-95.705 |
| fresh | fresh-thin-ribbon | 1024 | 0 | 62.154 | 66.599 | 0.93x | 66.571-66.948 |
| fresh | fresh-sheared-canyon | 1024 | 2 | 2717.863 | 482.984 | 5.63x | 480.220-489.802 |
| fresh | fresh-subdivided-parallelogram | 1024 | 0 | 89.409 | 92.069 | 0.97x | 91.401-93.541 |
| fresh | fresh-retraced-outline | 1024 | 0 | 211.510 | 214.199 | 0.99x | 212.515-219.052 |
| fresh | fresh-touching-rectangles | 1024 | 0 | 87.253 | 91.341 | 0.96x | 90.939-91.631 |
| fresh | fresh-late-crossing | 1024 | 0 | 54.597 | 58.559 | 0.93x | 58.068-61.358 |
| fresh | fresh-random-walk | 1024 | 0 | 338.302 | 339.312 | 1.00x | 338.562-354.036 |
| fresh | fresh-rotated-two-lobes | 1024 | 0 | 60.725 | 65.241 | 0.93x | 65.043-67.170 |
| fresh | fresh-perturbed-outline | 2048 | 0 | 221.124 | 236.098 | 0.94x | 235.207-242.408 |
| fresh | fresh-thin-ribbon | 2048 | 0 | 123.283 | 131.463 | 0.94x | 130.992-132.351 |
| fresh | fresh-sheared-canyon | 2048 | 2 | 10951.800 | 1020.459 | 10.73x | 1018.647-1042.434 |
| fresh | fresh-subdivided-parallelogram | 2048 | 0 | 177.746 | 182.731 | 0.97x | 182.666-182.866 |
| fresh | fresh-retraced-outline | 2048 | 0 | 517.669 | 479.723 | 1.08x | 478.663-481.152 |
| fresh | fresh-touching-rectangles | 2048 | 0 | 211.823 | 218.088 | 0.97x | 217.530-218.469 |
| fresh | fresh-late-crossing | 2048 | 0 | 107.688 | 115.747 | 0.93x | 115.746-122.704 |
| fresh | fresh-random-walk | 2048 | 0 | 750.575 | 731.994 | 1.03x | 729.900-746.875 |
| fresh | fresh-rotated-two-lobes | 2048 | 0 | 164.105 | 172.214 | 0.95x | 172.027-172.903 |
| control-regions | filled-regions | 128 | 0 | 14.146 | 14.419 | 0.98x | 14.410-14.752 |
| control-regions | filled-regions | 512 | 0 | 57.372 | 58.922 | 0.97x | 58.777-59.333 |
| control-regions | filled-regions | 2048 | 0 | 233.819 | 237.101 | 0.99x | 236.381-238.103 |

Allocation samples: 2430; maximum 0 B/call.

development: 42 cases, 6 certified, 2 rejected attempts.
fresh: 36 cases, 3 certified, 0 rejected attempts.
control-regions: 3 cases, 0 certified, 0 rejected attempts.
