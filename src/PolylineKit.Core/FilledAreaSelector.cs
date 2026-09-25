#if NET10_0_OR_GREATER
namespace PolylineKit;

// The measured be96dea selector policy, without experimental reporting fields. Admission is an
// implementation detail, not a restriction on the public FilledArea input contract. Rejected paths
// retain the general boundary engine. The caller must keep the array stable throughout the call.
internal static class FilledAreaSelector
{
    private const int MinimumVertices = 64, MaximumVertices = 1024, MaximumLevels = 16;
    private const int CoordinateLimit = 2048, MinimumAverageActive = 16;
    private const int SampleEdgeCount = 16, MinimumSampleCrossings = 8, MinimumSampleCoincidences = 8;

    internal static bool ShouldUseIntegerSweep(Point2[]? points)
    {
        int count = points?.Length ?? 0;
        if (points is null || count < MinimumVertices || count > MaximumVertices) return false;

        // Validate all sampled endpoints before any integer predicate. Compaction removes only
        // zero edges. It preserves the order and the crossing/coincidence totals of the reference.
        Span<SampleEdge> sampleEdges = stackalloc SampleEdge[SampleEdgeCount];
        int sampleCount = 0;
        for (int sample = 0; sample < SampleEdgeCount; sample++)
        {
            int edge = sample * count / SampleEdgeCount;
            Point2 a = points[edge], b = points[edge + 1 == count ? 0 : edge + 1];
            if (!InIntegerDomain(a) || !InIntegerDomain(b)) return false;
            if (a.X != b.X || a.Y != b.Y)
                sampleEdges[sampleCount++] = new((int)a.X, (int)a.Y, (int)b.X, (int)b.Y);
        }
        int crossings = 0, coincidences = 0;
        for (int sampleA = 0; sampleA < sampleCount; sampleA++)
        {
            ref readonly SampleEdge a = ref sampleEdges[sampleA];
            for (int sampleB = sampleA + 1; sampleB < sampleCount; sampleB++)
            {
                ref readonly SampleEdge b = ref sampleEdges[sampleB];
                // Sample gaps, including closure, are >= floor(count/16) >= 4: no adjacent pairs.
                // Strict separation is essential; vertical/horizontal crossings have zero-width boxes.
                if (a.MaxX < b.MinX || b.MaxX < a.MinX || a.MaxY < b.MinY || b.MaxY < a.MinY) continue;
                if ((a.X0 == b.X0 && a.Y0 == b.Y0 && a.X1 == b.X1 && a.Y1 == b.Y1) ||
                    (a.X0 == b.X1 && a.Y0 == b.Y1 && a.X1 == b.X0 && a.Y1 == b.Y0)) coincidences++;
                else if (ProperCrossing(in a, in b)) crossings++;
            }
        }
        if (crossings < MinimumSampleCrossings && coincidences < MinimumSampleCoincidences) return false;

        // A favorable sample never substitutes for full-coordinate admission. Invalid, fractional or
        // out-of-range unsampled points must be handled by the general public validation path.
        Span<int> levels = stackalloc int[MaximumLevels];
        int levelCount = 0, effectiveCount = 0;
        Point2 previous = default;
        for (int i = 0; i < count; i++)
        {
            Point2 p = points[i];
            if (!InIntegerDomain(p)) return false;
            if (effectiveCount == 0 || p.X != previous.X || p.Y != previous.Y)
            {
                effectiveCount++;
                previous = p;
            }
            int y = (int)p.Y, at = 0;
            while (at < levelCount && levels[at] != y) at++;
            if (at == levelCount)
            {
                if (levelCount == MaximumLevels) return false;
                levels[levelCount++] = y;
            }
        }
        if (effectiveCount > 1 && previous.X == points[0].X && previous.Y == points[0].Y) effectiveCount--;
        if (effectiveCount < 3 || levelCount < 2) return false;
        levels[..levelCount].Sort();

        long spans = 0;
        for (int i = 0; i < count; i++)
        {
            int y0 = (int)points[i].Y, y1 = (int)points[i + 1 == count ? 0 : i + 1].Y;
            if (y0 == y1) continue;
            int from = 0, to = 0;
            while (levels[from] != y0) from++;
            while (levels[to] != y1) to++;
            spans += Math.Abs(to - from);
        }
        return spans >= (long)MinimumAverageActive * (levelCount - 1);
    }

    private static bool InIntegerDomain(Point2 p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        p.X >= -CoordinateLimit && p.X <= CoordinateLimit && p.Y >= -CoordinateLimit && p.Y <= CoordinateLimit &&
        p.X == Math.Truncate(p.X) && p.Y == Math.Truncate(p.Y);

    private static bool ProperCrossing(in SampleEdge a, in SampleEdge b)
    {
        // Differences <=4096, products <=16,777,216, determinants <=33,554,432. Every signed Int32
        // operation is exact. Comparing signs avoids multiplying determinants beyond this bound.
        int abC = a.Dx * (b.Y0 - a.Y0) - a.Dy * (b.X0 - a.X0);
        int abD = a.Dx * (b.Y1 - a.Y0) - a.Dy * (b.X1 - a.X0);
        if (!Opposite(abC, abD)) return false;
        int cdA = b.Dx * (a.Y0 - b.Y0) - b.Dy * (a.X0 - b.X0);
        int cdB = b.Dx * (a.Y1 - b.Y0) - b.Dy * (a.X1 - b.X0);
        return Opposite(cdA, cdB);
    }

    private static bool Opposite(int a, int b) => (a < 0 && b > 0) || (a > 0 && b < 0);

    private readonly struct SampleEdge
    {
        internal readonly int X0, Y0, X1, Y1, Dx, Dy, MinX, MaxX, MinY, MaxY;

        internal SampleEdge(int x0, int y0, int x1, int y1)
        {
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
            Dx = x1 - x0; Dy = y1 - y0;
            MinX = Math.Min(x0, x1); MaxX = Math.Max(x0, x1);
            MinY = Math.Min(y0, y1); MaxY = Math.Max(y0, y1);
        }
    }
}
#endif
