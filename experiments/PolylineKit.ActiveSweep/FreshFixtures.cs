namespace PolylineKit.ActiveSweep;

/// <summary>
/// A held-out protocol chosen before integrated-sweep timings: nine families at
/// 64, 257, 1024 and 2048 vertices (36 paths). Seeds and transforms are fixed;
/// no case is selected using candidate counts, sweep acceptance or timings.
/// SplitMix64 makes the pseudo-random stream independent of System.Random.
/// </summary>
/// <remarks>
/// Perturbed outlines, two-lobe outlines, ribbons, canyon profiles and
/// parallelograms are simple by construction. The ribbon's ideal area is 1/8;
/// the parallelogram's is 2625/32; touching rectangles have nonzero/even-odd
/// area 30. The retraced outline has both filled areas zero. Coordinates and
/// affine transforms are rounded to binary64, so nonzero analytic areas allow
/// coordinate-rounding error. Late crossings contain one small bow tie near
/// the end of the upper chain; random walks have unrestricted intersections.
/// An odd-size retrace contains an extra repeated point, deliberately testing
/// zero-length-edge handling. Touching rectangles repeat the shared vertex.
/// </remarks>
internal static class FreshFixtures
{
    internal static List<Fixture> Create()
    {
        var fixtures = new List<Fixture>(36);
        foreach (int n in new[] { 64, 257, 1024, 2048 })
        {
            ulong seed = 0xC25E7B193A64D08FUL ^ (ulong)n;
            fixtures.Add(new("fresh-perturbed-outline", Outline(n, seed, false)));
            fixtures.Add(new("fresh-thin-ribbon", Ribbon(n)));
            fixtures.Add(new("fresh-sheared-canyon", Canyon(n, seed ^ 0x921FCB36UL)));
            fixtures.Add(new("fresh-subdivided-parallelogram", Parallelogram(n)));
            fixtures.Add(new("fresh-retraced-outline", Retrace(n, seed)));
            fixtures.Add(new("fresh-touching-rectangles", TouchingRectangles(n)));
            fixtures.Add(new("fresh-late-crossing", LateCrossing(n)));
            fixtures.Add(new("fresh-random-walk", RandomWalk(n, seed ^ 0xBE350982UL)));
            fixtures.Add(new("fresh-rotated-two-lobes", Outline(n, seed ^ 0xF64A271DUL, true)));
        }
        return fixtures;
    }

    private static Point2[] Outline(int n, ulong seed, bool lobes)
    {
        var random = new FixedRandom(seed);
        var path = new Point2[n];
        for (int i = 0; i < n; i++)
        {
            double angle = (i + 0.173) * (2 * Math.PI / n);
            // Positive radii in strictly increasing angular order yield a
            // simple polygon, even when the independent perturbations are large.
            double radius = lobes
                ? 25 + 17 * Math.Cos(2 * angle) + 0.12 * random.Unit()
                : 38 + 3.5 * Math.Sin(7 * angle) + 0.7 * (random.Unit() - 0.5);
            double x = radius * Math.Cos(angle), y = radius * Math.Sin(angle);
            path[i] = lobes
                ? new Point2(0.8 * x - 0.6 * y + 17, 0.6 * x + 0.8 * y - 9)
                : new Point2(x + 0.1875 * y - 3, 0.25 * x + y + 11);
        }
        return path;
    }

    private static Point2[] Ribbon(int n)
    {
        int half = n / 2;
        var path = new List<Point2>(n);
        for (int i = 0; i < half; i++) path.Add(RibbonPoint(i, half, 1.0 / 2048));
        for (int i = half - 1; i >= 0; i--) path.Add(RibbonPoint(i, half, -1.0 / 2048));
        if (path.Count != n) InsertMidpoint(path, half / 2);
        return path.Select(p => new Point2(p.X + 0.125 * p.Y, 0.5 * p.X + 1.0625 * p.Y)).ToArray();
    }

    private static Point2 RibbonPoint(int i, int count, double offset)
    {
        double t = (double)i / (count - 1);
        return new Point2(128 * t, 3 * Math.Sin(5 * Math.PI * t) + 0.4 * Math.Sin(19 * Math.PI * t) + offset);
    }

    private static Point2[] Canyon(int n, ulong seed)
    {
        var random = new FixedRandom(seed);
        var path = new Point2[n];
        for (int i = 0; i < n - 2; i++)
        {
            double x = 100.0 * i / (n - 3);
            double y = (i % 3 == 1 ? 2 : 27) + random.Unit();
            path[i] = new Point2(x, y);
        }
        path[n - 2] = new Point2(100, 0);
        path[n - 1] = new Point2(0, 0);
        // Determinant one: preserve the concavity and area while changing both
        // sweep projections. Neither axis is tied to the original monotone axis.
        return path.Select(p => new Point2(p.X + 2 * p.Y, 3 * p.X + 7 * p.Y)).ToArray();
    }

    private static Point2[] Parallelogram(int n)
    {
        Point2[] corners = [new(0, 0), new(25, 0), new(25, 3), new(0, 3)];
        return Subdivide(corners, n)
            .Select(p => new Point2(p.X + 0.375 * p.Y + 8, -0.25 * p.X + p.Y - 16)).ToArray();
    }

    private static Point2[] Retrace(int n, ulong seed)
    {
        Point2[] forward = Outline(n / 2, seed ^ 0x743FE286UL, false);
        var path = new List<Point2>(n);
        path.AddRange(forward);
        path.AddRange(forward.Reverse());
        if (path.Count != n) path.Add(forward[0]);
        return path.ToArray();
    }

    private static Point2[] TouchingRectangles(int n)
    {
        // Explicitly close the first lobe before starting the second. Both
        // rectangle traversals are counterclockwise and meet only at the origin.
        Point2[] first = [new(0, 0), new(6, 0), new(6, 3), new(0, 3)];
        Point2[] second = [new(0, 0), new(-4, 0), new(-4, -3), new(0, -3)];
        int firstCount = (n - 1) / 2;
        return Subdivide(first, firstCount).Append(first[0])
            .Concat(Subdivide(second, n - firstCount - 1)).ToArray();
    }

    private static Point2[] LateCrossing(int n)
    {
        int topCount = (n + 1) / 2, bottomCount = n - topCount;
        var path = new Point2[n];
        for (int i = 0; i < topCount; i++)
        {
            double t = (double)i / (topCount - 1);
            path[i] = new Point2(100 * t, 20 * t * t);
        }
        for (int i = 0; i < bottomCount; i++)
        {
            double t = 1 - (double)i / (bottomCount - 1);
            path[topCount + i] = new Point2(100 * t, 20 * t * t - 0.75);
        }
        // Exchanging two interior points of the convex parabolic chain makes
        // the surrounding diagonals cross. The other edges remain unchanged.
        (path[topCount - 3], path[topCount - 2]) = (path[topCount - 2], path[topCount - 3]);
        return path.Select(p => new Point2(p.X + 0.03125 * p.Y, p.Y - 0.125 * p.X)).ToArray();
    }

    private static Point2[] RandomWalk(int n, ulong seed)
    {
        var random = new FixedRandom(seed);
        var path = new Point2[n];
        double x = 0, y = 0;
        for (int i = 0; i < n; i++)
        {
            // Local steps produce a moderate number of crossings at large n,
            // unlike a fully shuffled point cloud with quadratic intersections.
            x += 2 * random.Unit() - 1;
            y += 2 * random.Unit() - 1;
            path[i] = new Point2(x, y);
        }
        return path;
    }

    private static Point2[] Subdivide(Point2[] corners, int count)
    {
        var path = new Point2[count];
        int at = 0;
        for (int side = 0; side < corners.Length; side++)
        {
            int points = count / corners.Length + (side < count % corners.Length ? 1 : 0);
            Point2 a = corners[side], b = corners[(side + 1) % corners.Length];
            for (int i = 0; i < points; i++)
            {
                double t = (double)i / points;
                path[at++] = new Point2(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
            }
        }
        return path;
    }

    private static void InsertMidpoint(List<Point2> path, int index)
    {
        Point2 a = path[index], b = path[index + 1];
        path.Insert(index + 1, new Point2((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
    }

    private struct FixedRandom(ulong state)
    {
        internal double Unit()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return ((z ^ (z >> 31)) >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
