using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

internal static class Checks
{
    private static int passed;
    public static int Run()
    {
        passed = 0;
        foreach (var fixture in Fixtures.HandCases()) CheckGraph(fixture);
        foreach (int seed in Enumerable.Range(0, 100)) CheckGraph(Fixtures.RandomGraphs(seed));

        var above = Fixtures.NearTouch(1e-6); var below = Fixtures.NearTouch(-1e-6);
        double lipAbove = LipGraphs.Measure(above.P, above.Q), lipBelow = LipGraphs.Measure(below.P, below.Q);
        True("near-touch LIP jump", lipAbove - lipBelow > .12499);
        True("near-touch area continuity", Math.Abs(PolylineArea.BetweenGraphs(above.P, above.Q) - PolylineArea.BetweenGraphs(below.P, below.Q)) < 1.01e-6);
        foreach (var f in new[] { above, below })
        {
            var gen = GenLip.Measure(f.P, f.Q);
            Near("GenLIP all-good reduces to LIP", LipGraphs.Measure(f.P, f.Q), gen.Score);
            True("all-good certificate", gen.GoodGroups == 1 && gen.BadPairs == 0);
            Near("GenLIP swap", gen.Score, GenLip.Measure(f.Q, f.P).Score);
            Near("collinear merge preserves LIP", LipGraphs.Measure(f.P, f.Q), LipGraphs.Measure(Geometry.Clean(f.P, true), Geometry.Clean(f.Q, true)));
            Near("collinear merge preserves GenLIP near-touch", gen.Score, GenLip.Measure(Geometry.Clean(f.P, true), Geometry.Clean(f.Q, true)).Score);
        }
        foreach (int n in new[] { 1, 2, 10 })
        {
            Point2[] p = Enumerable.Range(0, n + 1).Select(i => new Point2(10.0 * i / n, 0)).ToArray();
            Point2[] q = p.Select(a => new Point2(a.X, 1)).ToArray();
            Near("strict GenLIP parallel subdivision", 10.0 / n, GenLip.Measure(p, q).Score);
            Near("parallel-is-good correction", 10, GenLip.Measure(p, q, parallelIsGood: true).Score);
            Near("collinear merge repairs parallel GenLIP", 10, GenLip.Measure(Geometry.Clean(p, true), Geometry.Clean(q, true)).Score);
        }
        Near("opposite directions bad pair", 4, GenLip.BadPair(new(0,0), new(2,0), new(0,0), new(-2,0), 4, 1e-6));
        Near("orthogonal directions bad pair", 2, GenLip.BadPair(new(0,0), new(2,0), new(0,0), new(0,2), 4, 1e-6));
        Near("collinear translation fallback", 3e-6, GenLip.BadPair(new(0,0), new(2,0), new(3,0), new(5,0), 4, 1e-6));
        Near("identical segment bad pair", 0, GenLip.BadPair(new(0,0), new(2,0), new(0,0), new(2,0), 4, 1e-6));
        Point2[] mixedP = [new(0,0),new(1,0),new(2,0),new(3,0)];
        Point2[] mixedQ = [new(0,0),new(1,1),new(2,1),new(3,0)];
        var mixed = GenLip.Measure(mixedP, mixedQ);
        Near("GenLIP mixed groups global denominator", (3 + Math.Sqrt(2)) / (4 + 2 * Math.Sqrt(2)), mixed.Score);
        True("GenLIP mixed partition", mixed.GoodGroups == 2 && mixed.BadPairs == 1);
        Near("GenLIP mixed symmetry", mixed.Score, GenLip.Measure(mixedQ, mixedP).Score);
        Near("GenLIP d sensitivity", 3e-3, GenLip.BadPair(new(0,0), new(2,0), new(3,0), new(5,0), 4, 1e-3));
        Reject<NotSupportedException>("GenLIP ambiguous unpaired bad tail", () => GenLip.Measure([new(0,0),new(2,0)], [new(0,1),new(1,1),new(2,1)]));

        // Test a non-monotone, single acute-angle pair through the polygon reconstruction.
        Point2[] angledP = [new(0,0),new(0,1)], angledQ = [new(0,0),new(1,1)];
        Near("GenLIP vertical good pair", .5, GenLip.Measure(angledP, angledQ).Score);

        var expectedContours = new Dictionary<string, (double Nz, double Eo, double Aw)>
        {
            ["square-once"] = (4,4,4), ["square-twice"] = (4,0,8), ["square-forward-backward"] = (0,0,0),
            ["bow-tie"] = (2,2,2), ["overlapping-squares"] = (6,4,8), ["hole-with-retraced-bridge"] = (12,12,12)
        };
        foreach (var (name, contour) in Fixtures.Contours())
        {
            var areas = ContourSweep.Measure(contour); var expected = expectedContours[name];
            Near(name + " nonzero", expected.Nz, areas.NonZero);
            Near(name + " parity", expected.Eo, areas.EvenOdd);
            Near(name + " multiplicity", expected.Aw, areas.AbsoluteWinding);
            Near(name + " Clipper nonzero", areas.NonZero, ClipperOracle.Contour(contour, FillRule.NonZero));
            Near(name + " Clipper parity", areas.EvenOdd, ClipperOracle.Contour(contour, FillRule.EvenOdd));
            Near(name + " signed integral", Geometry.SignedArea(contour), areas.Signed);
        }
        foreach (int n in new[] { 16, 64, 256, 1024 })
        foreach (string density in new[] { "none", "sparse", "dense" })
        {
            var f = Fixtures.Benchmark(n, density);
            var gen = GenLip.Measure(f.P, f.Q);
            Near(f.Name + " GenLIP certificate", LipGraphs.Measure(f.P, f.Q), gen.Score, 1e-9);
            True(f.Name + " good groups", gen.GoodGroups == 1 && gen.BadPairs == 0);
            Near(f.Name + " Clipper", PolylineArea.BetweenGraphs(f.P, f.Q), ClipperOracle.Between(f.P, f.Q), 2e-5);
        }
        Point2[] axis = [new(0,0), new(2,0)];
        Near("duplicates", 1, PolylineArea.BetweenGraphs([new(0,0), new(0,0), new(2,0)], [new(0,0), new(1,1), new(1,1), new(2,0)]));
        Reject<ArgumentException>("empty", () => PolylineArea.BetweenGraphs([], axis));
        Reject<ArgumentException>("single point", () => PolylineArea.BetweenGraphs([new(0,0)], axis));
        Reject<ArgumentException>("all duplicates", () => PolylineArea.BetweenGraphs([new(0,0),new(0,0)], axis));
        Reject<ArgumentException>("vertical", () => PolylineArea.BetweenGraphs([new(0,0),new(0,1),new(2,0)], axis));
        Reject<ArgumentException>("backtracking", () => PolylineArea.BetweenGraphs([new(0,0),new(1,1),new(.5,0),new(2,0)], axis));
        Reject<ArgumentException>("closed", () => PolylineArea.BetweenGraphs([new(0,0),new(2,1),new(0,0)], axis));
        Reject<ArgumentException>("reversed", () => PolylineArea.BetweenGraphs(axis.Reverse().ToArray(), axis));
        Reject<ArgumentException>("mismatched domain", () => PolylineArea.BetweenGraphs([new(0,0),new(1,0)], axis));
        Reject<ArgumentException>("NaN", () => PolylineArea.BetweenGraphs([new(0,double.NaN),new(2,0)], axis));
        Reject<ArgumentException>("infinity", () => PolylineArea.BetweenGraphs([new(0,0),new(double.PositiveInfinity,0)], axis));
        Reject<ArgumentException>("range", () => PolylineArea.BetweenGraphs([new(0,1e200),new(2,0)], axis));
        Reject<ArgumentNullException>("null", () => PolylineArea.BetweenGraphs(null!, axis));
        Point2[] tilted = [new(0,1),new(1,2),new(2,1)];
        Near("common translation", PolylineArea.BetweenGraphs(axis, tilted), PolylineArea.BetweenGraphs(axis.Select(p => new Point2(p.X + 20,p.Y - 50)).ToArray(), tilted.Select(p => new Point2(p.X + 20,p.Y - 50)).ToArray()));
        Near("uniform scale squared", 9 * PolylineArea.BetweenGraphs(axis, tilted), PolylineArea.BetweenGraphs(axis.Select(p => new Point2(p.X * 3,p.Y * 3)).ToArray(), tilted.Select(p => new Point2(p.X * 3,p.Y * 3)).ToArray()));
        Near("reflection with increasing reordering", PolylineArea.BetweenGraphs(axis, tilted), PolylineArea.BetweenGraphs(axis.Reverse().Select(p => new Point2(-p.X,p.Y)).ToArray(), tilted.Reverse().Select(p => new Point2(-p.X,p.Y)).ToArray()));
        Console.WriteLine($"PASS: {passed} geometric, formula, metamorphic and rejection checks.");
        return passed;
    }

    private static void CheckGraph(GraphFixture f)
    {
        double area = PolylineArea.BetweenGraphs(f.P, f.Q);
        if (f.ExpectedArea is double expected) Near(f.Name + " analytic", expected, area, 2e-9);
        Near(f.Name + " symmetry", area, PolylineArea.BetweenGraphs(f.Q, f.P));
        Near(f.Name + " Clipper", area, ClipperOracle.Between(f.P, f.Q), 2e-7);
        Near(f.Name + " Clipper parity", area, ClipperOracle.Between(f.P, f.Q, FillRule.EvenOdd), 2e-7);
        Near(f.Name + " subdivision", area, PolylineArea.BetweenGraphs(Fixtures.Subdivide(f.P), Fixtures.Subdivide(f.Q, 3)), 2e-11);
        List<Lobe> lobes = [];
        double lip = LipGraphs.Measure(f.P, f.Q, lobes);
        Near(f.Name + " unweighted lobes", area, lobes.Sum(l => l.Area));
        Near(f.Name + " weights partition lengths", 1, lobes.Sum(l => l.Weight));
        var polygons = LipPolygons.Regions(f.P, f.Q);
        Near(f.Name + " independent polygon areas", area, polygons.Sum(r => r.Area), 2e-10);
        Near(f.Name + " independent polygon weights", lip, polygons.Sum(r => r.Area * r.Weight), 2e-10);
        Near(f.Name + " LIP subdivision", lip, LipGraphs.Measure(Fixtures.Subdivide(f.P), Fixtures.Subdivide(f.Q, 3)), 2e-10);
        // A bounded perturbation has a provable area bound: |A(P,Q)-A(P,Q')| <= width * sup|Q-Q'|.
        Point2[] noisy = f.Q.Select((p, i) => new Point2(p.X, p.Y + (i % 2 == 0 ? 1e-7 : -1e-7))).ToArray();
        True(f.Name + " perturbation bound", Math.Abs(area - PolylineArea.BetweenGraphs(f.P, noisy)) <= (f.P[^1].X - f.P[0].X) * 1e-7 + 1e-12);
        Near(f.Name + " identity", 0, PolylineArea.BetweenGraphs(f.P, f.P));
    }

    internal static void Near(string name, double expected, double actual, double tolerance = 2e-12)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance * Math.Max(1, Math.Abs(expected)))
            throw new Exception($"{name}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}");
        passed++;
    }
    internal static void True(string name, bool condition)
    {
        if (!condition) throw new Exception(name);
        passed++;
    }
    private static void Reject<T>(string name, Action action) where T : Exception
    {
        try { action(); } catch (T) { passed++; return; }
        throw new Exception(name + " should have been rejected.");
    }
}
