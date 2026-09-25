using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Structural selection diagnostics, computed from coordinates without timing or fixture identity.</summary>
internal readonly record struct HybridSelection(string Backend, string Reason, int SuppliedVertices,
    int DistinctYLevels, int NonhorizontalEdges, long ActiveBandSpans, int BandCount, int SampleCrossings);

/// <summary>Experimental scalar-area dispatcher between shipping Winding and a bounded integer sweep.</summary>
/// <remarks>
/// This is not a replacement for WindingArea.ClosedPath: it returns only one requested fill, not four
/// integrals or crossing statistics. Selection and backend preparation happen on every call. Coordinates
/// are never rounded, translated or rescaled. Instances retain integer-sweep scratch and are neither
/// thread-safe nor reentrant; callers must not mutate the input during a call.
/// </remarks>
internal sealed class HybridClosedArea
{
    private const int MinimumVertices = 64, MaximumVertices = 1024, MaximumLevels = 16;
    private const int CoordinateLimit = 2048, MinimumAverageActive = 16;
    private const int SampleEdgeCount = 16, MinimumSampleCrossings = 8;
    private readonly IntegerScanbeam integer = new();

    internal HybridSelection LastSelection { get; private set; }

    internal double Measure(Point2[] points, PathFillRule rule = PathFillRule.NonZero)
    {
        LastSelection = default;
        if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(rule));
        LastSelection = Select(points);
        return LastSelection.Backend == "IntegerScanbeam" ? integer.Measure(points, rule) : SelectedWinding(points, rule);
    }

    internal static double SelectedWinding(Point2[] points, PathFillRule rule)
    {
        if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd)
            throw new ArgumentOutOfRangeException(nameof(rule));
        WindingAreaResult result = WindingArea.ClosedPath(points);
        return rule == PathFillRule.NonZero ? result.NonZero : result.EvenOdd;
    }

    /// <summary>Allocation-free O(16n + 16²) predictor; rejection delegates input semantics to Winding.</summary>
    internal static HybridSelection Select(Point2[]? points)
    {
        int count = points?.Length ?? 0;
        if (points is null) return Reject("null", count);
        if (count < MinimumVertices || count > MaximumVertices) return Reject("vertex-count", count);

        Span<int> levels = stackalloc int[MaximumLevels];
        int levelCount = 0, effectiveCount = 0;
        Point2 previous = default;
        for (int i = 0; i < count; i++)
        {
            Point2 p = points[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                p.X < -CoordinateLimit || p.X > CoordinateLimit || p.Y < -CoordinateLimit || p.Y > CoordinateLimit ||
                p.X != Math.Truncate(p.X) || p.Y != Math.Truncate(p.Y))
                return Reject("integer-domain", count, levelCount);
            if (effectiveCount == 0 || p.X != previous.X || p.Y != previous.Y)
            {
                effectiveCount++;
                previous = p;
            }
            int y = (int)p.Y, at = 0;
            while (at < levelCount && levels[at] != y) at++;
            if (at == levelCount)
            {
                if (levelCount == MaximumLevels) return Reject("y-levels", count, levelCount + 1);
                levels[levelCount++] = y;
            }
        }
        if (effectiveCount > 1 && previous.X == points[0].X && previous.Y == points[0].Y) effectiveCount--;
        if (effectiveCount < 3) return Reject("effective-vertices", count, levelCount);
        if (levelCount < 2) return Reject("y-levels", count, levelCount);
        levels[..levelCount].Sort();

        long spans = 0;
        int nonhorizontal = 0;
        for (int i = 0; i < count; i++)
        {
            int y0 = (int)points[i].Y, y1 = (int)points[i + 1 == count ? 0 : i + 1].Y;
            if (y0 == y1) continue;
            nonhorizontal++;
            int from = 0, to = 0;
            while (levels[from] != y0) from++;
            while (levels[to] != y1) to++;
            spans += Math.Abs(to - from);
        }
        int bands = levelCount - 1;
        if (spans < (long)MinimumAverageActive * bands)
            return new("Winding", "sparse-active", count, levelCount, nonhorizontal, spans, bands, 0);

        // Deterministic approximately evenly spaced edge indices, unique because count >= 64.
        // Only strict crossings count: shared endpoints, collinearity and retracing do not imply
        // the proper-crossing workload for which the specialized sweep is being investigated.
        int crossings = 0;
        for (int sampleA = 0; sampleA < SampleEdgeCount; sampleA++)
        {
            int a = sampleA * count / SampleEdgeCount, aNext = a + 1 == count ? 0 : a + 1;
            for (int sampleB = sampleA + 1; sampleB < SampleEdgeCount; sampleB++)
            {
                int b = sampleB * count / SampleEdgeCount, bNext = b + 1 == count ? 0 : b + 1;
                if (aNext == b || bNext == a) continue;
                if (ProperCrossing(points[a], points[aNext], points[b], points[bNext])) crossings++;
            }
        }
        return crossings >= MinimumSampleCrossings
            ? new("IntegerScanbeam", "dense-integer-few-levels", count, levelCount, nonhorizontal, spans, bands, crossings)
            : new("Winding", "sample-crossings", count, levelCount, nonhorizontal, spans, bands, crossings);
    }

    private static HybridSelection Reject(string reason, int count, int levels = 0) =>
        new("Winding", reason, count, levels, 0, 0, Math.Max(0, levels - 1), 0);

    private static bool ProperCrossing(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        long abC = Orientation(a, b, c), abD = Orientation(a, b, d);
        if (!Opposite(abC, abD)) return false;
        return Opposite(Orientation(c, d, a), Orientation(c, d, b));
    }

    private static bool Opposite(long a, long b) => (a < 0 && b > 0) || (a > 0 && b < 0);

    private static long Orientation(Point2 a, Point2 b, Point2 c)
    {
        // Admission proves exact integer coordinates within +/-2048. Differences <=4096 and
        // determinant magnitude <=2*4096² make each Int64 operation exact and overflow-free.
        long abX = (long)b.X - (long)a.X, abY = (long)b.Y - (long)a.Y;
        long acX = (long)c.X - (long)a.X, acY = (long)c.Y - (long)a.Y;
        return abX * acY - abY * acX;
    }
}
