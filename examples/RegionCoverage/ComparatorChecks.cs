using PolylineKit;

namespace RegionCoverage;

internal static class ComparatorChecks
{
    internal static int Run()
    {
        int checks = 0;
        Point2[] square = Rect(0, 0, 4, 4);
        Point2[] uShape = [new(0, 0), new(4, 0), new(4, 4), new(3, 4), new(3, 1), new(1, 1), new(1, 4), new(0, 4)];
        Point2[] bottomLeft = [new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 4), new(0, 4)];
        Point2[] topRight = [new(0, 3), new(3, 3), new(3, 0), new(4, 0), new(4, 4), new(0, 4)];
        (string Name, Point2[] First, Point2[] Second, double FirstArea, double SecondArea, double Intersection)[] cases =
        [
            ("identity", square, square, 16, 16, 16),
            ("partial", square, Rect(2, 0, 6, 4), 16, 16, 8),
            ("query inside", square, Rect(1, 1, 2, 2), 16, 1, 1),
            ("zone inside", square, Rect(-1, -1, 5, 5), 16, 36, 16),
            ("separated", square, Rect(5, 1, 6, 2), 16, 1, 0),
            ("shared edge", square, Rect(4, 0, 5, 4), 16, 4, 0),
            ("shared vertex", square, Rect(4, 4, 5, 5), 16, 1, 0),
            // Convex clipping creates two disconnected rectangles of area two each. The S-H
            // output walk's connecting boundary segments cancel in signed area.
            ("disconnected convex clip", uShape, Rect(-1, 2, 5, 5), 10, 18, 4),
            // Both inputs concave, so the specialized row must use its general fallback.
            ("both concave", bottomLeft, topRight, 7, 7, 2),
            ("overlapping envelopes, empty intersection", bottomLeft,
                bottomLeft.Select(p => new Point2(p.X + 1, p.Y + 1)).ToArray(), 7, 7, 0)
        ];
        foreach (CoverageComparator method in Comparators.Create(1e6))
        {
            foreach (var item in cases)
            for (int reversed = 0; reversed < 4; reversed++)
            for (int swapped = 0; swapped < 2; swapped++)
            {
                Point2[] a = (Point2[])item.First.Clone(), b = (Point2[])item.Second.Clone();
                if ((reversed & 1) != 0) Array.Reverse(a);
                if ((reversed & 2) != 0) Array.Reverse(b);
                double areaA = item.FirstArea, areaB = item.SecondArea;
                if (swapped != 0) { (a, b) = (b, a); (areaA, areaB) = (areaB, areaA); }
                var zone = method.Prepare(a, true); var query = method.Prepare(b, false);
                string label = $"{method.Name}/{item.Name}/reverse{reversed}/swap{swapped}";
                CheckNear(zone.Area, areaA, label + "/zone area");
                CheckNear(query.Area, areaB, label + "/query area");
                CoverageValue result = method.Compare(zone, query);
                CheckNear(result.Intersection, item.Intersection, label + "/intersection");
                CheckNear(result.Coverage ?? double.NaN, item.Intersection / areaA, label + "/coverage of zone");
                // Reuse the same engine with another query in between to detect stale pair caches.
                method.Compare(zone, method.Prepare(Rect(-20, -20, 20, 20), false));
                if (result != method.Compare(zone, query)) throw new InvalidOperationException(label + "/repeat changed");
                checks += 5;
            }

            Point2[] closed = [.. square, square[0]];
            var closedZone = method.Prepare(closed, true);
            CheckNear(method.Compare(closedZone, method.Prepare(square, false)).Intersection, 16, method.Name + "/explicit closure");
            Point2[] flat = [new(0, 0), new(1, 0), new(2, 0)];
            CoverageValue zero = method.Compare(method.Prepare(flat, true), method.Prepare(square, false));
            if (zero.Intersection != 0 || zero.Coverage is not null) throw new InvalidOperationException(method.Name + "/zero denominator");
            CoverageValue emptyQuery = method.Compare(method.Prepare(square, true), method.Prepare(flat, false));
            if (emptyQuery.Intersection != 0 || emptyQuery.Coverage != 0) throw new InvalidOperationException(method.Name + "/zero query");
            checks += 3;

            foreach (Point2[] invalid in new Point2[][] { [], [new(0, 0)], [new(0, 0), new(1, 1)],
                [new(0, 0), new(double.NaN, 1), new(0, 1)] })
            {
                bool threw = false;
                try { method.Prepare(invalid, true); } catch (ArgumentException) { threw = true; }
                if (!threw) throw new InvalidOperationException(method.Name + "/invalid input accepted");
                checks++;
            }

            // The pinned integer grid intentionally loses this overlap. Every other adapter is
            // checked against the exact dyadic input, not against the quantized baseline's zero.
            double width = Math.ScaleB(1, -26);
            CoverageValue thin = method.Compare(method.Prepare(Rect(0, 0, 1, 1), true),
                method.Prepare(Rect(1 - width, 0, 2, 1), false));
            double expected = method.Name == "Clipper64-reused-data" ? 0 : width;
            if (thin.Intersection != expected || thin.Coverage != expected)
                throw new InvalidOperationException(method.Name + "/tiny overlap or grid contract changed");
            checks++;
        }

        CoverageComparator convex = Comparators.Create(1e6).Single(x => x.Name == "convex-then-winding");
        if (!Comparators.IsConvex(convex.Prepare(square, true)) || Comparators.IsConvex(convex.Prepare(uShape, true)))
            throw new InvalidOperationException("Convex eligibility ignored validated ring turns.");
        // Repeating the reflex point makes both adjacent triples degenerate. Testing triples
        // without removing consecutive repeats would incorrectly label this L as convex.
        Point2[] repeatedReflex = [new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 1), new(1, 4), new(0, 4)];
        var repeated = convex.Prepare(repeatedReflex, true);
        if (Comparators.IsConvex(repeated)) throw new InvalidOperationException("Repeated reflex vertex hid concavity.");
        CheckNear(convex.Compare(repeated, convex.Prepare(topRight, false)).Intersection, 2, "repeated reflex fallback");
        checks += 3;
        Console.WriteLine($"RegionCoverage adapter controls: {checks} assertions.");
        return checks;
    }

    private static void CheckNear(double value, double expected, string label)
    {
        if (!double.IsFinite(value) || Math.Abs(value - expected) > 2e-13 * Math.Max(1, Math.Abs(expected)))
            throw new InvalidOperationException($"{label}: expected {expected:R}, got {value:R}.");
    }

    private static Point2[] Rect(double x0, double y0, double x1, double y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
}
