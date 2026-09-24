using PolylineKit;

namespace PolylineKit.Experiments;

internal sealed record GraphFixture(string Name, Point2[] P, Point2[] Q, double? ExpectedArea = null);

internal static class Fixtures
{
    public static GraphFixture NearTouch(double epsilon) => new($"near-touch-{epsilon:R}",
        [new(0, 0), new(.5, 0), new(1, 0), new(1.5, 0), new(2, 0)],
        [new(0, 0), new(.5, .25), new(1, epsilon), new(1.5, .25), new(2, 0)],
        epsilon >= 0 ? .25 + epsilon / 2 : .25 + epsilon / 2 + 4 * epsilon * epsilon / (1 - 4 * epsilon));

    public static GraphFixture[] HandCases() =>
    [
        new("identity", [new(0,0), new(1,1), new(2,0)], [new(0,0), new(1,1), new(2,0)], 0),
        new("triangle", [new(0,0), new(2,0)], [new(0,0), new(1,1), new(2,0)], 1),
        new("crossing-lobes", [new(0,0), new(2,0)], [new(0,1), new(2,-1)], 1),
        new("parallel", [new(0,0), new(10,0)], [new(0,1), new(10,1)], 10),
        new("unequal-sampling", [new(0,0), new(.1,0), new(1.8,0), new(2,0)], [new(0,0), new(1,1), new(2,0)], 1),
        new("overlap", [new(0,0), new(4,0)], [new(0,0), new(1,0), new(2,1), new(3,0), new(4,0)], 1),
        new("narrow-detour", [new(0,0), new(2,0)], [new(0,0), new(.999999,0), new(1,100), new(1.000001,0), new(2,0)], .0001),
        NearTouch(1e-6), NearTouch(0), NearTouch(-1e-6)
    ];

    public static Point2[] Subdivide(Point2[] p, int parts = 2)
    {
        List<Point2> result = [p[0]];
        for (int i = 1; i < p.Length; i++)
            for (int j = 1; j <= parts; j++) result.Add(Geometry.Lerp(p[i - 1], p[i], (double)j / parts));
        return result.ToArray();
    }

    public static GraphFixture Benchmark(int n, string density)
    {
        Point2[] p = new Point2[n], q = new Point2[n];
        int halfPeriod = density switch { "none" => n, "sparse" => Math.Max(2, n / 8), "dense" => 1, _ => throw new ArgumentException(density) };
        for (int i = 0; i < n; i++)
        {
            // Opposite y values ensure equal corresponding arc lengths. Nonzero slopes and
            // |slope difference| < 2 make all pairs good for the strict published criterion.
            double delta = density == "none" ? .2 + .2 * i / (n - 1) : ((i / halfPeriod) % 2 == 0 ? .2 : -.2) + .00001 * i / n;
            p[i] = new(i, -delta / 2); q[i] = new(i, delta / 2);
        }
        return new($"{density}-{n}", p, q);
    }

    public static GraphFixture RandomGraphs(int seed, int n = 32)
    {
        Random random = new(seed);
        Point2[] Make(int count) => Enumerable.Range(0, count).Select(i => new Point2((double)i / (count - 1), random.NextDouble() * 2 - 1)).ToArray();
        return new($"random-{seed}", Make(n), Make(n + 3));
    }

    public static Dictionary<string, Point2[]> Contours()
    {
        Point2[] square = [new(0,0), new(2,0), new(2,2), new(0,2), new(0,0)];
        return new()
        {
            ["square-once"] = square,
            ["square-twice"] = square.Concat(square.Skip(1)).ToArray(),
            ["square-forward-backward"] = square.Concat(square.Reverse().Skip(1)).ToArray(),
            ["bow-tie"] = [new(0,0), new(2,2), new(0,2), new(2,0)],
            ["overlapping-squares"] = [new(0,0), new(2,0), new(2,2), new(0,2), new(0,0), new(1,0), new(3,0), new(3,2), new(1,2), new(1,0), new(0,0)],
            ["hole-with-retraced-bridge"] = [new(0,0), new(4,0), new(4,4), new(0,4), new(0,0), new(1,1), new(1,3), new(3,3), new(3,1), new(1,1), new(0,0)]
        };
    }
}
