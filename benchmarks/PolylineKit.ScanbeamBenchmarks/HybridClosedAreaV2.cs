// Preserved verbatim from 87858da7cf41e44ad208b262efe8e57b39307b1d except the class name and
// shared HybridSelection declaration, for same-binary timing and selector equivalence checks.
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Experimental scalar-area dispatcher between shipping Winding and a bounded integer sweep.</summary>
/// <remarks>
/// This is not a replacement for WindingArea.ClosedPath: it returns only one requested fill, not four
/// integrals or crossing statistics. Selection and backend preparation happen on every call. Coordinates
/// are never rounded, translated or rescaled. Instances retain integer-sweep scratch and are neither
/// thread-safe nor reentrant; callers must not mutate the input during a call.
/// </remarks>
internal sealed class HybridClosedAreaV2
{
    private const int MinimumVertices = 64, MaximumVertices = 1024, MaximumLevels = 16;
    private const int CoordinateLimit = 2048, MinimumAverageActive = 16;
    private const int SampleEdgeCount = 16, MinimumSampleCrossings = 8, MinimumSampleCoincidences = 8;
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

        // Reject sparse samples before scanning the complete walk. The 16 approximately evenly spaced
        // indices are unique because count >= 64. Sample coordinates must pass exact integer admission
        // before any Int64 predicate; acceptance later validates every coordinate independently.
        Span<int> sampleEdges = stackalloc int[SampleEdgeCount];
        for (int sample = 0; sample < SampleEdgeCount; sample++)
        {
            int edge = sample * count / SampleEdgeCount;
            sampleEdges[sample] = edge;
            if (!InIntegerDomain(points[edge]) || !InIntegerDomain(points[edge + 1 == count ? 0 : edge + 1]))
                return Reject("integer-domain", count);
        }
        int crossings = 0, coincidences = 0;
        for (int sampleA = 0; sampleA < SampleEdgeCount; sampleA++)
        {
            int a = sampleEdges[sampleA], aNext = a + 1 == count ? 0 : a + 1;
            Point2 a0 = points[a], a1 = points[aNext];
            if (Same(a0, a1)) continue;
            for (int sampleB = sampleA + 1; sampleB < SampleEdgeCount; sampleB++)
            {
                int b = sampleEdges[sampleB], bNext = b + 1 == count ? 0 : b + 1;
                if (aNext == b || bNext == a) continue;
                Point2 b0 = points[b], b1 = points[bNext];
                if (Same(b0, b1)) continue;
                if ((Same(a0, b0) && Same(a1, b1)) || (Same(a0, b1) && Same(a1, b0))) coincidences++;
                else if (ProperCrossing(a0, a1, b0, b1)) crossings++;
            }
        }
        if (crossings < MinimumSampleCrossings && coincidences < MinimumSampleCoincidences)
            return Reject("sample-structure", count, crossings: crossings, coincidences: coincidences);

        Span<int> levels = stackalloc int[MaximumLevels];
        int levelCount = 0, effectiveCount = 0;
        Point2 previous = default;
        for (int i = 0; i < count; i++)
        {
            Point2 p = points[i];
            if (!InIntegerDomain(p))
                return Reject("integer-domain", count, levelCount, crossings, coincidences);
            if (effectiveCount == 0 || p.X != previous.X || p.Y != previous.Y)
            {
                effectiveCount++;
                previous = p;
            }
            int y = (int)p.Y, at = 0;
            while (at < levelCount && levels[at] != y) at++;
            if (at == levelCount)
            {
                if (levelCount == MaximumLevels) return Reject("y-levels", count, levelCount + 1, crossings, coincidences);
                levels[levelCount++] = y;
            }
        }
        if (effectiveCount > 1 && previous.X == points[0].X && previous.Y == points[0].Y) effectiveCount--;
        if (effectiveCount < 3) return Reject("effective-vertices", count, levelCount, crossings, coincidences);
        if (levelCount < 2) return Reject("y-levels", count, levelCount, crossings, coincidences);
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
            return new("Winding", "sparse-active", count, levelCount, nonhorizontal, spans, bands, crossings, coincidences);
        return new("IntegerScanbeam", "dense-integer-few-levels", count, levelCount, nonhorizontal, spans, bands,
            crossings, coincidences);
    }

    private static HybridSelection Reject(string reason, int count, int levels = 0, int crossings = 0, int coincidences = 0) =>
        new("Winding", reason, count, levels, 0, 0, Math.Max(0, levels - 1), crossings, coincidences);

    private static bool InIntegerDomain(Point2 p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        p.X >= -CoordinateLimit && p.X <= CoordinateLimit && p.Y >= -CoordinateLimit && p.Y <= CoordinateLimit &&
        p.X == Math.Truncate(p.X) && p.Y == Math.Truncate(p.Y);

    private static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;

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
