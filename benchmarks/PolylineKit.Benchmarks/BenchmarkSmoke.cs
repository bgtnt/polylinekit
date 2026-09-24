using Clipper2Lib;

namespace PolylineKit.Experiments;

/// <summary>Invokes benchmark operations once without generating timing claims or output files.</summary>
internal static class BenchmarkSmoke
{
    internal static void Run()
    {
        int calls = 0;
        foreach (int n in new[] { 16, 64, 256, 1024 })
        foreach (string density in new[] { "none", "sparse", "dense" })
        {
            var f = Fixtures.Benchmark(n, density);
            PathsD prepared = new() { new PathD(f.P.Concat(f.Q.Reverse()).Select(p => new PointD(p.X, p.Y))) };
            PathsD transposed = new() { new PathD(f.P.Concat(f.Q.Reverse()).Select(p => new PointD(p.Y, p.X))) };
            Finite(PolylineArea.BetweenGraphs(f.P, f.Q));
            Finite(LipGraphs.Measure(f.P, f.Q));
            Finite(GenLip.Measure(f.P, f.Q).Score);
            Finite(ClipperOracle.Between(f.P, f.Q));
            Finite(Math.Abs(Clipper.Area(Clipper.Union(prepared, new PathsD(), FillRule.NonZero, ClipperOracle.Precision))));
            Finite(Math.Abs(Clipper.Area(Clipper.Union(transposed, new PathsD(), FillRule.NonZero, ClipperOracle.Precision))));
        }

        Point2[] reference = [new(0, 0), new(1, 2), new(3, 1), new(4, 0)];
        Point2[] moving = AffineTransform2D.Scaling(2).Then(AffineTransform2D.Rotation(.37))
            .Then(AffineTransform2D.Translation(7, -4)).Apply(reference);
        Finite(PolylineComparison.EndpointBridgedArea(reference, moving).RawArea);
        Finite(PolylineNormalization.ToUnitBounds(moving).Bounds.Width);
        Finite(PolylineComparison.CompareNormalized(reference, moving, AreaComparisonKind.EndpointBridged).Comparison.RawArea);
        Finite(PolylineAlignment.FitSimilarity(moving, reference).RmsError);
        var p = PolylineNormalization.ToUnitBounds(reference);
        var q = PolylineNormalization.ToUnitBounds(moving);
        var fit = PolylineAlignment.FitSimilarity(q.Points, p.Points);
        Finite(PolylineComparison.EndpointBridgedArea(p.Points, fit.AlignedPoints).RawArea);
        calls += WindingBenchmarks.Smoke();
        Console.WriteLine($"Benchmark smoke passed: {calls} operation calls; no timings collected.");

        void Finite(double value)
        {
            if (!double.IsFinite(value)) throw new InvalidOperationException("Benchmark smoke produced a nonfinite value.");
            calls++;
        }
    }
}
