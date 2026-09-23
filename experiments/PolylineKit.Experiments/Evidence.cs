using System.Globalization;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

internal static class Evidence
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        List<object> graphs = [];
        foreach (var fixture in Fixtures.HandCases())
        {
            List<Lobe> lobes = [];
            double lip = LipGraphs.Measure(fixture.P, fixture.Q, lobes);
            var polygons = LipPolygons.Regions(fixture.P, fixture.Q);
            graphs.Add(new
            {
                fixture.Name, fixture.P, fixture.Q, fixture.ExpectedArea,
                Area = PolylineArea.BetweenGraphs(fixture.P, fixture.Q), Lip = lip,
                ClipperNonZero = ClipperOracle.Between(fixture.P, fixture.Q),
                Lobes = lobes, IndependentPolygons = polygons,
                GenLipP0 = TryGen(fixture.P, fixture.Q),
                GenLipParallelGood = TryGen(fixture.P, fixture.Q, true),
                GenLipCollinearMerged = TryGen(Geometry.Clean(fixture.P, true), Geometry.Clean(fixture.Q, true))
            });
        }
        WriteJson("geometry.json", graphs);
        WriteJson("contours.json", Fixtures.Contours().Select(pair => new
        {
            Name = pair.Key, Input = pair.Value, Areas = ContourSweep.Measure(pair.Value),
            ClipperNonZero = ClipperOracle.Contour(pair.Value, FillRule.NonZero),
            ClipperEvenOdd = ClipperOracle.Contour(pair.Value, FillRule.EvenOdd)
        }));
        List<object> stability = [];
        foreach (double epsilon in new[] { -1e-2, -1e-4, -1e-6, -1e-8, 0, 1e-8, 1e-6, 1e-4, 1e-2 })
        {
            var f = Fixtures.NearTouch(epsilon);
            Point2[] snapped = f.Q.Select(p => new Point2(p.X, Math.Round(p.Y / 1e-5) * 1e-5)).ToArray();
            stability.Add(new { Epsilon = epsilon, Area = PolylineArea.BetweenGraphs(f.P, f.Q), Lip = LipGraphs.Measure(f.P, f.Q),
                GenLip = TryGen(f.P, f.Q), LipCollinearMerged = LipGraphs.Measure(Geometry.Clean(f.P, true), Geometry.Clean(f.Q, true)),
                LipAfterYSnap1eMinus5 = LipGraphs.Measure(f.P, snapped) });
        }
        // A deadband repairs one neighbourhood but relocates the threshold.
        foreach (double epsilon in new[] { 4.999999e-6, 5.000001e-6 })
        {
            var f = Fixtures.NearTouch(epsilon);
            Point2[] snapped = f.Q.Select(p => new Point2(p.X, Math.Round(p.Y / 1e-5) * 1e-5)).ToArray();
            stability.Add(new { Epsilon = epsilon, Area = PolylineArea.BetweenGraphs(f.P, f.Q), LipAfterYSnap1eMinus5 = LipGraphs.Measure(f.P, snapped) });
        }
        WriteJson("stability.json", stability);
        List<object> parallel = [];
        foreach (int n in new[] { 1, 2, 10, 100 })
        {
            Point2[] p = Enumerable.Range(0, n + 1).Select(i => new Point2(10.0 * i / n, 0)).ToArray();
            Point2[] q = p.Select(a => new Point2(a.X, 1)).ToArray();
            parallel.Add(new { Segments = n, Area = PolylineArea.BetweenGraphs(p, q), StrictGenLip = GenLip.Measure(p, q).Score,
                ParallelGoodCorrection = GenLip.Measure(p, q, true).Score,
                CollinearMerged = GenLip.Measure(Geometry.Clean(p, true), Geometry.Clean(q, true)).Score });
        }
        WriteJson("parallel-subdivision.json", parallel);
        WriteJson("benchmark-inputs.json", from n in new[] { 16, 64, 256, 1024 } from density in new[] { "none", "sparse", "dense" } select Fixtures.Benchmark(n, density));
        Svg("near-touch.svg", Fixtures.NearTouch(-.01), "Near touch: two main lobes and a small opposite lobe (epsilon enlarged)");
        Svg("crossing.svg", Fixtures.HandCases().Single(f => f.Name == "crossing-lobes"), "Opposite lobes add; their signed areas cancel");
        Svg("triangle.svg", Fixtures.HandCases().Single(f => f.Name == "triangle"), "One triangle: unsigned area = 1");
        Console.WriteLine($"Evidence written to {directory}");

        void WriteJson(string name, object value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, JsonOptions) + "\n");
        void Svg(string name, GraphFixture f, string caption)
        {
            double minY = f.P.Concat(f.Q).Min(p => p.Y), maxY = f.P.Concat(f.Q).Max(p => p.Y);
            string Points(IEnumerable<Point2> points) => string.Join(" ", points.Select(p =>
                string.Create(CultureInfo.InvariantCulture, $"{30 + 560 * (p.X - f.P[0].X) / (f.P[^1].X - f.P[0].X):F3},{175 - 130 * (p.Y - minY) / (maxY - minY):F3}")));
            File.WriteAllText(Path.Combine(directory, name), $"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 620 225" role="img" aria-label="{caption}">
                <rect width="620" height="225" fill="#fafaf7"/>
                <polygon points="{Points(f.P.Concat(f.Q.Reverse()))}" fill="#dce9f3" fill-rule="evenodd"/>
                <polyline points="{Points(f.P)}" fill="none" stroke="#2463a6" stroke-width="3"/>
                <polyline points="{Points(f.Q)}" fill="none" stroke="#b74721" stroke-width="3"/>
                <text x="20" y="211" font-family="sans-serif" font-size="12">{caption}</text>
                </svg>
                """);
        }
    }
    private static object TryGen(Point2[] p, Point2[] q, bool parallel = false)
    {
        try { return GenLip.Measure(p, q, parallel); }
        catch (NotSupportedException e) { return new { Unsupported = e.Message }; }
    }
}
