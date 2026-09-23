# Scalar/accelerated prediction audit

Exact full-row agreement: **True**.
Matched rows: 110070; changed rows: 0; changed predictions: 0; changed statuses: 0.
Missing from right: 0; missing from left: 0.

| Numeric field | Comparisons | Bitwise equal | Maximum absolute difference |
|---|---:|---:|---:|
| area | 98760 | 98760 | 0 |
| margin | 98760 | 98760 | 0 |
| rms | 98760 | 98760 | 0 |
| rotationDegrees | 98760 | 98760 | 0 |
| score | 98760 | 98760 | 0 |
| seed | 110070 | 110070 | 0 |

No epsilon clamping is used. JSON contains every differing row and per-method ties/margins. This checks exported winning-score diagnostics, not unexported pair scores.
