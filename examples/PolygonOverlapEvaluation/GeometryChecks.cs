using NetTopologySuite.Geometries;
using PolylineKit;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PolygonOverlapEvaluation;

internal static class GeometryChecks
{
    private static readonly GeometryFactory Factory = new();

    internal static int Run()
    {
        int checks = 0;
        Polygon square = Polygon(Rect(0, 0, 4, 4));
        Polygon lShape = Polygon([new(0, 0), new(6, 0), new(6, 2), new(2, 2), new(2, 6), new(0, 6)]);
        Polygon holed = Factory.CreatePolygon(Ring(Rect(0, 0, 4, 4)), [Ring(Rect(1, 1, 3, 3))]);
        MultiPolygon multipart = Factory.CreateMultiPolygon([Polygon(Rect(0, 0, 2, 2)), Polygon(Rect(4, 0, 6, 2))]);
        (string Name, NtsGeometry A, NtsGeometry B, double AreaA, double AreaB, double Intersection)[] cases =
        [
            ("identity", square, square, 16, 16, 16),
            ("partial", square, Polygon(Rect(2, 0, 6, 4)), 16, 16, 8),
            ("containment", square, Polygon(Rect(1, 1, 2, 2)), 16, 1, 1),
            ("disjoint", square, Polygon(Rect(5, 1, 6, 2)), 16, 1, 0),
            ("touch-edge", square, Polygon(Rect(4, 0, 5, 4)), 16, 4, 0),
            ("touch-point", square, Polygon(Rect(4, 4, 5, 5)), 16, 1, 0),
            ("triangle-clip", Polygon([new(0, 0), new(4, 0), new(0, 4)]), Polygon(Rect(1, 1, 3, 3)), 8, 4, 2),
            ("L-coverage", lShape, Polygon(Rect(1, 1, 5, 5)), 20, 16, 7),
            ("hole-fallback", holed, Polygon(Rect(0, 0, 2, 2)), 12, 4, 3),
            ("multipart-fallback", multipart, Polygon(Rect(1, -1, 5, 3)), 8, 16, 4)
        ];
        foreach (string name in GeometryBackends.Names)
        {
            IGeometryBackend backend = GeometryBackends.Create(name);
            foreach (var item in cases)
            for (int reverse = 0; reverse < 4; reverse++)
            for (int swap = 0; swap < 2; swap++)
            {
                NtsGeometry a = (reverse & 1) == 0 ? item.A : item.A.Reverse();
                NtsGeometry b = (reverse & 2) == 0 ? item.B : item.B.Reverse();
                double areaA = item.AreaA, areaB = item.AreaB;
                if (swap != 0) { (a, b) = (b, a); (areaA, areaB) = (areaB, areaA); }
                PreparedPolygon p = GeometryPreparation.Prepare(a), t = GeometryPreparation.Prepare(b);
                PreparedOperand prediction = backend.Prepare(p, false), truth = backend.Prepare(t, true);
                string label = $"{name}/{item.Name}/reverse{reverse}/swap{swap}";
                Near(areaA, p.Area, label + "/own A"); Near(areaB, t.Area, label + "/own B");
                IntersectionValue value = backend.IntersectionArea(prediction, truth);
                Near(item.Intersection, value.Area, label + "/intersection");
                Check(value.Area >= 0 && value.Area <= Math.Min(areaA, areaB) + 1e-12, label + "/area bounds");
                bool expectedFallback = name != "nts" && (p.UnsupportedReason is not null || t.UnsupportedReason is not null ||
                    name == "convex" && (!p.IsConvexSimple || !t.IsConvexSimple));
                Check(value.UsedFallback == expectedFallback && (value.FallbackReason is not null) == expectedFallback,
                    label + "/explicit fallback accounting");
                if (item.Name == "L-coverage")
                {
                    Near(29, areaA + areaB - value.Area, label + "/union from cached own areas");
                    Near(7.0 / 29, value.Area / (areaA + areaB - value.Area), label + "/IoU");
                }
                // A different query in between exposes stale Clipper output or convex scratch data.
                _ = backend.IntersectionArea(prediction, backend.Prepare(GeometryPreparation.Prepare(Polygon(Rect(-10, -10, 20, 20))), true));
                Check(value == backend.IntersectionArea(prediction, truth), label + "/reuse");
            }

            PreparedPolygon empty = GeometryPreparation.Prepare(Factory.CreatePolygon());
            PreparedPolygon regular = GeometryPreparation.Prepare(square);
            Check(empty.IsEmpty && empty.Area == 0 && !empty.Bounds.Intersects(regular.Bounds), name + "/empty shared preparation");
            Near(0, backend.IntersectionArea(backend.Prepare(empty, false), backend.Prepare(regular, true)).Area, name + "/empty prediction");
            Near(0, backend.IntersectionArea(backend.Prepare(regular, false), backend.Prepare(empty, true)).Area, name + "/empty truth");

            PreparedPolygon invalid = GeometryPreparation.Prepare(Polygon([new(0, 0), new(2, 2), new(0, 2), new(2, 0)]));
            Check(!invalid.OriginalWasValid && invalid.UnsupportedReason == "invalid-geometry" && invalid.Points is null,
                name + "/invalid geometry identified without repair");
            Throws<NotSupportedException>(() => backend.IntersectionArea(backend.Prepare(invalid, false), backend.Prepare(regular, true)),
                name + "/invalid prediction requires evaluator policy");
            Throws<NotSupportedException>(() => backend.IntersectionArea(backend.Prepare(regular, false), backend.Prepare(invalid, true)),
                name + "/invalid truth requires evaluator policy");

            // This deliberate sub-grid overlap is measured on original coordinates by Core/NTS/convex.
            // Clipper's predeclared pixel grid loses it. It is not a tolerance change or a fill defect.
            double width = Math.ScaleB(1, -35);
            PreparedPolygon narrowA = GeometryPreparation.Prepare(Polygon(Rect(0, 0, 1, 1)));
            PreparedPolygon narrowB = GeometryPreparation.Prepare(Polygon(Rect(1 - width, 0, 2, 1)));
            double expected = name == "clipper" ? 0 : width;
            Near(expected, backend.IntersectionArea(backend.Prepare(narrowA, false), backend.Prepare(narrowB, true)).Area,
                name + "/pixel-grid contract");

            PreparedPolygon far = GeometryPreparation.Prepare(Polygon(Rect(100_000, 100_000, 100_002, 100_002)));
            IntersectionValue farValue = backend.IntersectionArea(backend.Prepare(far, false), backend.Prepare(far, true));
            Near(4, farValue.Area, name + "/range fallback value");
            Check(farValue.UsedFallback == (name == "clipper") && (name != "clipper" || farValue.FallbackReason == "clipper-coordinate-range"),
                name + "/range fallback accounting");

            Throws<ArgumentException>(() => backend.IntersectionArea(backend.Prepare(regular, true), backend.Prepare(regular, false)),
                name + "/roles required");
            IGeometryBackend other = GeometryBackends.Create(name);
            Throws<ArgumentException>(() => backend.IntersectionArea(other.Prepare(regular, false), backend.Prepare(regular, true)),
                name + "/workspace ownership required");
        }

        // Consecutive duplicate reflex vertices must not make a concave shell appear convex.
        PreparedPolygon repeatedReflex = GeometryPreparation.Prepare(Polygon(
            [new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 1), new(1, 4), new(0, 4)]));
        Check(repeatedReflex.OriginalWasValid && !repeatedReflex.IsConvexSimple, "duplicate reflex vertex preserves concavity");
        PreparedPolygon splitSide = GeometryPreparation.Prepare(Polygon(
            [new(0, 0), new(1, 0), new(2, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0)]));
        Check(splitSide.OriginalWasValid && splitSide.IsConvexSimple && splitSide.Points!.Length == 5,
            "closure/duplicates cleaned without removing collinear boundary subdivisions");
        Throws<ArgumentNullException>(() => GeometryPreparation.Prepare(null!), "null geometry rejected");
        Throws<NotSupportedException>(() => GeometryPreparation.Prepare(Factory.CreatePoint(new Coordinate(0, 0))), "nonpolygon rejected");
        Console.WriteLine($"Polygon overlap geometry controls: {checks} assertions.");
        return checks;

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Polygon overlap geometry: " + label);
            checks++;
        }
        void Near(double expected, double actual, string label) =>
            Check(double.IsFinite(actual) && Math.Abs(expected - actual) <= 2e-13 * Math.Max(1, Math.Abs(expected)),
                $"{label}: expected {expected:R}, got {actual:R}");
        void Throws<T>(Action action, string label) where T : Exception
        {
            bool threw = false;
            try { action(); } catch (T) { threw = true; }
            Check(threw, label);
        }
    }

    private static Polygon Polygon(Point2[] points) => Factory.CreatePolygon(Ring(points));
    private static LinearRing Ring(Point2[] points)
    {
        bool closed = points.Length != 0 && GeometryPreparation.Same(points[0], points[^1]);
        var coordinates = points.Select(p => new Coordinate(p.X, p.Y)).ToList();
        if (coordinates.Count != 0 && !closed) coordinates.Add(new Coordinate(points[0].X, points[0].Y));
        return Factory.CreateLinearRing(coordinates.ToArray());
    }
    private static Point2[] Rect(double x0, double y0, double x1, double y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
}
