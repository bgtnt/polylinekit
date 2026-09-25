using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Reporting metadata only; the dispatcher receives Points, never this record.</summary>
internal sealed record HybridInput(Input Data, string Family, string Split)
{
    internal string Name => Data.Name;
    internal Point2[] Points => Data.Points;
    internal string Hash => Data.Hash;
}

/// <summary>
/// Frozen before timing: established development inputs and separate generated hold-out inputs.
/// Each input is one implicitly closed walk, evaluated under both fill rules.
/// </summary>
internal static class HybridInputs
{
    internal static HybridInput[] Create()
    {
        var result = new List<HybridInput>();
        foreach (Input input in Inputs.Create())
        {
            string family = input.Name.Contains("grid", StringComparison.Ordinal) ? "dense-grid"
                : input.Name.StartsWith("repeated", StringComparison.Ordinal) ? "retraced"
                : input.Name.StartsWith("integer-star", StringComparison.Ordinal) ? "star"
                : "many-y-levels";
            result.Add(new(input, family, "development"));
        }
        Add("dev-simple-128", "simple", "development", SubdividedRectangle(128, 64, 48));
        Add("dev-near-coincident-128", "near-coincident", "development",
            NearRetrace(128, 0x94fa631u));

        foreach (int count in new[] { 128, 512 })
        {
            Point2[] grid = Inputs.Grid(count, 0x6b792da1u + (uint)count);
            Add($"held-grid-{count}", "dense-grid", "held-out", grid);
            Add($"held-binary-grid-{count}", "fractional-binary-grid", "held-out",
                Map(grid, p => new Point2(p.X / 8 + 0.0625, p.Y / 8 - 0.03125)));
            uint state = 0xf81341u + (uint)count;
            Add($"held-perturbed-grid-{count}", "perturbed-grid", "held-out",
                Map(grid, p => new Point2(p.X + (Next(ref state) % 997 + 1) / 100003.0,
                    p.Y + (Next(ref state) % 991 + 1) / 100019.0)));
        }

        Add("held-retraced-512", "retraced", "held-out",
            Enumerable.Range(0, 127).SelectMany(_ => new Point2[]
                { new(-3, 2), new(8, 2), new(8, 9), new(-3, 9) })
                .Concat(Enumerable.Repeat(new Point2(-3, 2), 4)).ToArray());
        Add("held-star-128", "star", "held-out", Enumerable.Range(0, 128).Select(i =>
        {
            double angle = (i + 0.25) * (2 * Math.PI / 128);
            double radius = i % 2 == 0 ? 1536 : 640;
            return new Point2(Math.Round(radius * Math.Cos(angle)), Math.Round(radius * Math.Sin(angle)));
        }).ToArray());
        uint manyYState = 0x3781b1a9u;
        Add("held-many-levels-128", "many-y-levels", "held-out",
            Enumerable.Range(0, 128).Select(_ => new Point2(
                Next(ref manyYState) % 3073 - 1536.0,
                Next(ref manyYState) % 3073 - 1536.0)).ToArray());
        Add("held-simple-512", "simple", "held-out", SubdividedRectangle(512, 128, 96));
        // Few endpoint levels alone do not imply many crossings: this is a simple x-monotone comb.
        Add("held-sparse-few-levels-512", "sparse-few-y-levels", "held-out",
            [.. Enumerable.Range(0, 510).Select(i => new Point2(i, i % 2)), new(509, -1), new(0, -1)]);
        Add("held-near-coincident-512", "near-coincident", "held-out",
            NearRetrace(512, 0x33f8ab11u));
        Add("held-wide-grid-128", "wide-grid", "held-out",
            Map(Inputs.Grid(128, 0x728111d3u), p => new Point2(1e9 + p.X * 64, -1e9 + p.Y * 64)));

        // Added before the second selector's timings. These six cases were not inspected in stage one;
        // the original development and held-out coordinates above stay unchanged.
        Point2[] confirmationGrid = Inputs.Grid(256, 0x2ea9d613u).Reverse().ToArray();
        Add("confirm-grid-256", "dense-grid", "confirmation", Enumerable.Range(0, 256).Select(i =>
        {
            Point2 p = confirmationGrid[(i + 37) % confirmationGrid.Length];
            return new Point2(p.Y, p.X);
        }).ToArray());
        Add("confirm-diamond-256", "retraced", "confirmation",
            Enumerable.Range(0, 64).SelectMany(_ => new Point2[]
                { new(0, -8), new(12, 0), new(0, 8), new(-12, 0) }).ToArray());
        Add("confirm-opposite-retrace-256", "retraced", "confirmation",
            Enumerable.Range(0, 32).SelectMany(_ => new Point2[]
                { new(-4, -3), new(9, -3), new(9, 5), new(-4, 5),
                  new(-4, -3), new(-4, 5), new(9, 5), new(9, -3) }).ToArray());
        Point2[] confirmationSparse = [.. Enumerable.Range(0, 254).Select(i => new Point2(i, i % 2)), new(253, -1), new(0, -1)];
        Add("confirm-sparse-few-levels-256", "sparse-few-y-levels", "confirmation",
            confirmationSparse.Reverse().Select(p => new Point2(p.X - 128, p.Y + 9)).ToArray());
        Add("confirm-binary-grid-256", "fractional-binary-grid", "confirmation",
            Map(Inputs.Grid(256, 0x7e61811u), p => new Point2(p.X / 16 + .03125, p.Y / 16 - .015625)));
        Add("confirm-wide-grid-256", "wide-grid", "confirmation",
            Map(Inputs.Grid(256, 0x395a2807u), p => new Point2(1e9 + p.X * 64, -1e9 + p.Y * 64)));
        return result.ToArray();

        void Add(string name, string family, string split, Point2[] points) =>
            result.Add(new(new Input(name, points, false), family, split));
    }

    private static Point2[] Map(Point2[] points, Func<Point2, Point2> map) => points.Select(map).ToArray();

    private static Point2[] NearRetrace(int count, uint seed)
    {
        Point2[] forward = Inputs.Grid(count / 2, seed);
        const double offset = 1.0 / 16777216; // Exact 2^-24; no decimal-to-grid rounding.
        return [.. forward, .. forward.Reverse().Select(p => new Point2(p.X + offset, p.Y - offset))];
    }

    private static Point2[] SubdividedRectangle(int count, double width, double height)
    {
        int perSide = count / 4;
        return Enumerable.Range(0, count).Select(i =>
        {
            double t = (double)(i % perSide) / perSide;
            return (i / perSide) switch
            {
                0 => new Point2(t * width, 0),
                1 => new Point2(width, t * height),
                2 => new Point2((1 - t) * width, height),
                _ => new Point2(0, (1 - t) * height)
            };
        }).ToArray();
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return state;
    }
}
