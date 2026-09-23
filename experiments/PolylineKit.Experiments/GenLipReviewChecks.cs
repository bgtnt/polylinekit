using PolylineKit;

namespace PolylineKit.Experiments;

internal static class GenLipReviewChecks
{
    public static int Run()
    {
        int passed = 0;
        void True(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException("GenLIP review: " + name);
            passed++;
        }
        void Reject(string name, Action action)
        {
            try { action(); }
            catch (NotSupportedException e) when (e.Message.Contains("simple component routes")) { passed++; return; }
            throw new InvalidOperationException("GenLIP admitted nonsimple input: " + name);
        }
        void Near(string name, double expected, double actual) =>
            True(name, double.IsFinite(actual) && Math.Abs(expected - actual) <= 1e-12);

        // Independent review of 33e17fe: previously returned 7.125 in one good group.
        Point2[] reportedP = [new(0, 0), new(2, 0), new(0, 0), new(3, 0)];
        Point2[] reportedQ = [new(0, 2), new(2, 2.5), new(0, 2), new(3, 2.75)];
        Reject("reported retracing pair", () => GenLip.Measure(reportedP, reportedQ));
        Reject("reported pair swapped", () => GenLip.Measure(reportedQ, reportedP));
        var initialOverlap = GenLip.Measure([new(0, 0), new(4, 0)], [new(0, -1), new(2, 0)]);
        True("first-pair connector overlap is bad", initialOverlap.GoodGroups == 0 && initialOverlap.BadPairs == 1);
        Near("first-pair overlap uses existing bad formula", 2 * Math.Sqrt(5), initialOverlap.Score);

        Func<Point2, Point2>[] orientations = [p => p, p => new(-p.Y, p.X), p => new(p.X, p.X + p.Y)];
        foreach (var orient in orientations)
        {
            Point2[] Map(params Point2[] points) => points.Select(orient).ToArray();
            Point2[] adjacent = Map(new(0, 0), new(3, 0), new(1, 0));
            Point2[] nonadjacent = Map(new(0, 0), new(4, 0), new(5, 1), new(6, 0), new(2, 0));
            Reject("adjacent horizontal/vertical/diagonal overlap", () => GenLip.RequireSimple(adjacent));
            Reject("nonadjacent horizontal/vertical/diagonal overlap", () => GenLip.RequireSimple(nonadjacent));
            Reject("reverse nonadjacent overlap", () => GenLip.RequireSimple(nonadjacent.Reverse().ToArray()));
            Point2[] shiftedRetrace = Map(new(0, 2), new(3, 2), new(1, 2));
            Reject("bad-only partition cannot bypass global admission", () => GenLip.Measure(adjacent, shiftedRetrace));
            Point2[] valid = Map(new(0, 0), new(1, 0), new(3, 0));
            Point2[] shiftedValid = Map(new(0, 2), new(1, 2), new(3, 2));
            GenLip.RequireSimple(valid); passed++;
            Near("shared endpoint subdivisions remain valid", 6,
                GenLip.Measure(valid, shiftedValid, parallelIsGood: true).Score);
            Near("subdivision retains strict bad-pair formula", 10.0 / 3,
                GenLip.Measure(valid, shiftedValid).Score);

            Point2 a = orient(new(0, 0)), b = orient(new(4, 0));
            foreach (bool reverse in new[] { false, true })
            {
                Point2 c = orient(new(2, 0)), d = orient(new(6, 0));
                var overlap = Geometry.Contact(a, b, reverse ? d : c, reverse ? c : d, out double lo, out double hi);
                True("contact classifies positive overlap", overlap == SegmentContact.Overlap);
                Near("overlap begins halfway", .5, lo); Near("overlap ends at endpoint", 1, hi);
            }
            True("shared collinear endpoint is a point", Geometry.Contact(a, b, b, orient(new(6, 0)), out _, out _) == SegmentContact.Point);
            True("separated collinear segments", Geometry.Contact(a, b, orient(new(5, 0)), orient(new(6, 0)), out _, out _) == SegmentContact.None);
            True("parallel distinct segments", Geometry.Contact(a, b, orient(new(0, 1)), orient(new(4, 1)), out _, out _) == SegmentContact.None);

            // Endpoints form the connector (0,0)->(4,0). Ordinary incidence is
            // allowed; either a positive overlap or interior point contact blocks it.
            Point2[] left = Map(new(-2, 0), new(0, 0));
            Point2[] right = Map(new(6, 0), new(4, 0));
            True("connector accepts collinear endpoint incidence", GenLip.ConnectorClear(left, right, 0, 1, 0, 1));
            Point2[] overlapping = Map(new(2, 0), new(0, 0));
            True("connector rejects overlap with first path", !GenLip.ConnectorClear(overlapping, right, 0, 1, 0, 1));
            True("connector rejects overlap with second path", !GenLip.ConnectorClear(right, overlapping, 0, 1, 0, 1));
            Point2[] interiorVertex = Map(new(2, 0), new(2, 1), new(0, 0));
            True("connector rejects interior endpoint touch", !GenLip.ConnectorClear(interiorVertex, right, 0, 2, 0, 1));
            Point2[] crossing = Map(new(2, -1), new(2, 1), new(0, 0));
            True("connector rejects transverse crossing", !GenLip.ConnectorClear(crossing, right, 0, 2, 0, 1));
            Point2[] sameEndpoint = Map(new(0, 1), new(0, 0));
            True("zero-length connector has no interior", GenLip.ConnectorClear(left, sameEndpoint, 0, 1, 0, 1));
        }
        Reject("nonadjacent endpoint revisit", () => GenLip.RequireSimple([new(0, 0), new(2, 0), new(2, 2), new(0, 0)]));
        Reject("transverse self-crossing", () => GenLip.RequireSimple([new(0, 0), new(2, 2), new(0, 2), new(2, 0)]));

        // The published benchmark family remains entirely on the graph fast path.
        foreach (int n in new[] { 16, 64, 256, 1024 })
        foreach (string density in new[] { "none", "sparse", "dense" })
        {
            var f = Fixtures.Benchmark(n, density);
            var actual = GenLip.Measure(f.P, f.Q);
            True("graph fixture still one good group", actual.GoodGroups == 1 && actual.BadPairs == 0);
            Near("graph fixture keeps LIP formula", LipGraphs.Measure(f.P, f.Q), actual.Score);
        }
        Console.WriteLine($"PASS: {passed} GenLIP review checks.");
        return passed;
    }
}
