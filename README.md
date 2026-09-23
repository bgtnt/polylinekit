# PolylineKit

Experimental comparison of unweighted polyline area with LIP and GenLIP. Evaluation in progress.
The small candidate package supports piecewise linear graphs over a common x interval only.
No general route-similarity, alignment or scientific novelty claim.

```csharp
using PolylineKit;
Point2[] p = [new(0, 0), new(2, 0)];
Point2[] q = [new(0, 0), new(1, 1), new(2, 0)];
double area = PolylineArea.BetweenGraphs(p, q); // 1 square coordinate unit
```

Not published to NuGet.org. API is experimental.
