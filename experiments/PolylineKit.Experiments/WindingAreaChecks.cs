using System.Collections;
using System.Numerics;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Checks for boundary winding integrals, their exact predicates and symbolic tie-breaking.</summary>
internal static class WindingAreaChecks
{
    private static int passed;

    public static int Run()
    {
        passed = 0; checkedClipper = 0; ClipperDisagreements.Clear();
        CheckAnalytic();
        CheckSymbolicPerturbation();
        CheckExactSign();
        CheckGraphAgreement();
        CheckRandomAgainstOracles();
        CheckDegenerateGridAgainstOracles();
        CheckMetamorphic();
        CheckFilledRegions();
        CheckReviewedNearDegeneracy();
        CheckReviewRegressions();
        CheckAllocations();
        CheckInvalidInputs();
        Console.WriteLine($"Clipper2 disagreements on degenerate grid inputs: {ClipperDisagreements.Count} of {checkedClipper} checked; winding matched the slab sweep in every case.");
        Console.WriteLine($"PASS: {passed} winding-area, exact-predicate and symbolic tie-break checks.");
        return passed;
    }

    private static void CheckAnalytic()
    {
        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        Areas("counterclockwise square", WindingArea.ClosedPath(square), 4, 4, 4, 4);
        Areas("clockwise square", WindingArea.ClosedPath(square.Reverse().ToArray()), 4, 4, 4, -4);
        Areas("bow tie", WindingArea.ClosedPath([new(0, 0), new(2, 2), new(2, 0), new(0, 2)]), 2, 2, 2, 0);
        // Same-direction nested loops joined by a retraced bridge: winding 2 inside the inner square.
        Point2[] nested = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0), new(1, 1), new(3, 1), new(3, 3), new(1, 3), new(1, 1)];
        Areas("nested same-direction loops", WindingArea.ClosedPath(nested), 16, 12, 20, 20);

        var expected = new Dictionary<string, (double NonZero, double EvenOdd, double Absolute)>
        {
            ["square-once"] = (4, 4, 4), ["square-twice"] = (4, 0, 8), ["square-forward-backward"] = (0, 0, 0),
            ["bow-tie"] = (2, 2, 2), ["overlapping-squares"] = (6, 4, 8), ["hole-with-retraced-bridge"] = (12, 12, 12)
        };
        foreach (var (name, contour) in Fixtures.Contours())
        {
            var result = WindingArea.ClosedPath(contour);
            var sweep = ContourSweep.Measure(contour);
            Areas(name + " fixture", result, expected[name].NonZero, expected[name].EvenOdd, expected[name].Absolute, sweep.Signed);
            Near(name + " absolute agrees with independent slab sweep", sweep.AbsoluteWinding, result.AbsoluteWinding);
            True(name + " degenerate fixture reports symbolic decisions", name is "bow-tie" or "square-once" || result.SymbolicTieBreakCount > 0);
        }
        True("generic square needs no exact predicate", WindingArea.ClosedPath(square).ExactPredicateCount == 0);
        True("bow tie has one crossing", WindingArea.ClosedPath([new(0, 0), new(2, 2), new(2, 0), new(0, 2)]).CrossingCount == 1);

        Point2[] bottom = [new(0, 0), new(2, 0)], top = [new(0, 2), new(2, 2)];
        Areas("bridged rectangle", WindingArea.EndpointBridged(bottom, top), 4, 4, 4, 4);
        Areas("bridged crossing strokes", WindingArea.EndpointBridged([new(0, 0), new(1, 1)], [new(0, 1), new(1, 0)]), .5, .5, .5, 0);
        Areas("identical strokes", WindingArea.EndpointBridged(bottom, bottom), 0, 0, 0, 0);
        Areas("closed reversed stroke doubles winding", WindingArea.EndpointBridged(square.Append(square[0]).ToArray(),
            square.Append(square[0]).Reverse().ToArray()), 4, 0, 8, 8);
        Areas("closed same-order stroke cancels", WindingArea.EndpointBridged(square.Append(square[0]).ToArray(),
            square.Append(square[0]).ToArray()), 0, 0, 0, 0);
    }

    // RobustOrientation's symbolic sign must equal the sign of an explicitly perturbed determinant,
    // evaluated exactly with y_p += e^(2^(2p)), x_p += e^(2^(2p+1)), e = 2^-16 and indices 0..5.
    private static void CheckSymbolicPerturbation()
    {
        Random random = new(4242);
        const int bits = 16, maxIndex = 5, scaleExponent = bits * (1 << (2 * maxIndex + 1));
        BigInteger Coordinate(double value, int perturbationExponent) =>
            (new BigInteger(value) << scaleExponent) + (BigInteger.One << (scaleExponent - bits * perturbationExponent));
        int degenerate = 0;
        for (int trial = 0; trial < 3000; trial++)
        {
            var points = new Point2[maxIndex + 1];
            for (int i = 0; i <= maxIndex; i++) points[i] = new(random.Next(-2, 3), random.Next(-2, 3));
            int[] indices = Enumerable.Range(0, maxIndex + 1).OrderBy(_ => random.Next()).Take(3).ToArray();
            int a = indices[0], b = indices[1], c = indices[2];
            var statistics = new WindingStatistics();
            int actual = RobustOrientation.Sign(points, a, b, c, ref statistics);
            if (statistics.SymbolicTieBreaks > 0) degenerate++;
            BigInteger X(int p) => Coordinate(points[p].X, 1 << (2 * p + 1));
            BigInteger Y(int p) => Coordinate(points[p].Y, 1 << (2 * p));
            int expected = ((X(b) - X(a)) * (Y(c) - Y(a)) - (Y(b) - Y(a)) * (X(c) - X(a))).Sign;
            True($"symbolic orientation {a},{b},{c} trial {trial}", actual == expected && actual != 0);
            var reversed = new WindingStatistics();
            True($"symbolic orientation antisymmetry trial {trial}", RobustOrientation.Sign(points, b, a, c, ref reversed) == -actual);
        }
        True("symbolic trials include many exact ties", degenerate > 500);
    }

    // Floating-point expansions against independent exact rational arithmetic on nearly collinear triples.
    private static void CheckExactSign()
    {
        Random random = new(99);
        int nonzero = 0, zero = 0;
        for (int trial = 0; trial < 4000; trial++)
        {
            double slope = random.NextDouble() * 3 - 1.5, offset = random.NextDouble() - .5;
            double scale = Math.Pow(2, random.Next(-60, 60));
            Point2 On(double x) => new(x * scale, (slope * x + offset) * scale);
            Point2 a = On(random.NextDouble()), b = On(random.NextDouble() + 1), c = On(random.NextDouble() * 5 - 2);
            if (trial % 3 == 0) c = new(c.X, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(c.Y) + random.Next(-2, 3)));
            if (trial % 7 == 0) { a = new(Math.Round(a.X), 0); b = new(a.X + 3, 0); c = new(a.X + 7, 0); }
            int expected = RationalSign(a, b, c), actual = RobustOrientation.ExactSign(a, b, c);
            True("exact orientation sign trial " + trial, actual == expected);
            if (expected == 0) zero++; else nonzero++;
        }
        foreach (int exponent in new[] { -700, -500, -420, 300 })
        {
            double s = Math.Pow(2, exponent);
            Point2 a = new(s, s), b = new(3 * s, 2 * s), c = new(5 * s, 3 * s);
            True("extreme-exponent collinear triple " + exponent, RobustOrientation.ExactSign(a, b, c) == RationalSign(a, b, c));
            c = new(5 * s, 3 * s + s * Math.Pow(2, -40));
            True("extreme-exponent separated triple " + exponent, RobustOrientation.ExactSign(a, b, c) == RationalSign(a, b, c));
        }
        True("exact sign trials include true zeros and nonzeros", zero > 200 && nonzero > 1000);

        // Components below 2^-400 take the integer path. The crossing parameter 1 / (1 + 2^m) rounds to 2^-m;
        // an absolute 2^-62 grid would return zero here.
        double tinyScale = Math.Pow(2, -450);
        foreach (int m in new[] { 70, 100, 300, 500 })
        {
            bool crosses = RobustOrientation.CrossingParameter(new(-tinyScale, 0), new(tinyScale, 0), new(0, -tinyScale * Math.Pow(2, -m)), new(0, tinyScale), out double t, out double u, out _);
            True($"integer-path crossing parameter 2^-{m} is relative", crosses && NearlyRelative(Math.Pow(2, -m), t, 1e-15));
            True($"integer-path crossing parameter 2^-{m} from the other end", NearlyRelative(1 - Math.Pow(2, -m), u, 1e-15));
        }
    }

    private static void CheckGraphAgreement()
    {
        foreach (var fixture in Fixtures.HandCases().Concat(Enumerable.Range(0, 12).Select(seed => Fixtures.RandomGraphs(seed))))
        {
            double expected = fixture.ExpectedArea ?? PolylineArea.BetweenGraphs(fixture.P, fixture.Q);
            var result = WindingArea.EndpointBridged(fixture.P, fixture.Q);
            Near(fixture.Name + " winding/graph agreement", expected, result.NonZero, 1e-12);
            Near(fixture.Name + " graph lobes have unit winding", result.NonZero, result.AbsoluteWinding, 1e-12);
            Near(fixture.Name + " winding subdivision agreement", result.NonZero,
                WindingArea.EndpointBridged(Fixtures.Subdivide(fixture.P), Fixtures.Subdivide(fixture.Q, 3)).NonZero, 1e-12);
        }
        foreach (double epsilon in new[] { 1e-6, -1e-6 })
        {
            var f = Fixtures.NearTouch(epsilon);
            Near("near-touch graph integral equality " + epsilon, PolylineArea.BetweenGraphs(f.P, f.Q), WindingArea.EndpointBridged(f.P, f.Q).NonZero, 1e-15);
        }
    }

    private static void CheckRandomAgainstOracles()
    {
        Random random = new(2026);
        foreach (int n in new[] { 5, 12, 40, 150 })
        for (int trial = 0; trial < (n > 100 ? 15 : 60); trial++)
        {
            Point2[] walk = RandomWalk(random, n, 1.0, false);
            if (ClosedClean(walk).Length < 3) continue;
            var result = WindingArea.ClosedPath(walk);
            var sweep = ContourSweep.Measure(walk);
            string name = $"random walk n={n} #{trial}";
            Near(name + " nonzero vs slab sweep", sweep.NonZero, result.NonZero, 1e-9);
            Near(name + " evenodd vs slab sweep", sweep.EvenOdd, result.EvenOdd, 1e-9);
            Near(name + " absolute vs slab sweep", sweep.AbsoluteWinding, result.AbsoluteWinding, 1e-9);
            Near(name + " signed vs slab sweep", sweep.Signed, result.Signed, 1e-9);
            Near(name + " nonzero vs Clipper", ClipperOracle.Contour(walk, FillRule.NonZero), result.NonZero, 1e-7);
            Near(name + " evenodd vs Clipper", ClipperOracle.Contour(walk, FillRule.EvenOdd), result.EvenOdd, 1e-7);
            True(name + " integrals are ordered", result.EvenOdd <= result.NonZero + 1e-9 && result.NonZero <= result.AbsoluteWinding + 1e-9
                && Math.Abs(result.Signed) <= result.AbsoluteWinding + 1e-9);
        }
    }

    // Small integer grids make shared vertices, T-junctions, collinear overlap and retracing common.
    private static void CheckDegenerateGridAgainstOracles()
    {
        Random random = new(7);
        int symbolic = 0;
        foreach (int n in new[] { 4, 8, 16, 40 })
        for (int trial = 0; trial < 150; trial++)
        {
            Point2[] walk = RandomWalk(random, n, 1, true);
            if (ClosedClean(walk).Length < 3) continue;
            var result = WindingArea.ClosedPath(walk);
            var sweep = ContourSweep.Measure(walk);
            string name = $"grid walk n={n} #{trial}";
            Near(name + " nonzero vs slab sweep", sweep.NonZero, result.NonZero, 1e-9);
            Near(name + " evenodd vs slab sweep", sweep.EvenOdd, result.EvenOdd, 1e-9);
            Near(name + " absolute vs slab sweep", sweep.AbsoluteWinding, result.AbsoluteWinding, 1e-9);
            Near(name + " signed vs slab sweep", sweep.Signed, result.Signed, 1e-9);
            // Clipper2 is not an authority on exactly degenerate input: disagreements are counted and
            // arbitrated separately (see ClipperDisagreements), while the slab sweep must agree.
            foreach (FillRule rule in new[] { FillRule.NonZero, FillRule.EvenOdd })
            {
                double clipper = ClipperOracle.Contour(walk, rule), expected = rule == FillRule.NonZero ? sweep.NonZero : sweep.EvenOdd;
                checkedClipper++;
                if (Math.Abs(clipper - expected) > 1e-7 * Math.Max(1, expected)) ClipperDisagreements.Add((name + " " + rule, walk, null));
            }
            symbolic += result.SymbolicTieBreakCount;
        }
        True("grid walks exercise symbolic tie-breaking", symbolic > 1000);
    }

    internal static readonly List<(string Name, Point2[] First, Point2[]? Second)> ClipperDisagreements = [];
    private static int checkedClipper;

    private static void CheckMetamorphic()
    {
        Random random = new(314);
        for (int trial = 0; trial < 200; trial++)
        {
            Point2[] walk = RandomWalk(random, 6 + trial % 30, 1, trial % 2 == 0);
            walk = ClosedClean(walk);
            if (walk.Length < 3) continue;
            var reference = WindingArea.ClosedPath(walk);
            double scale = Math.Max(1, reference.AbsoluteWinding);
            string name = "metamorphic #" + trial;
            int shift = 1 + trial % (walk.Length - 1);
            Same(name + " cyclic start", reference, WindingArea.ClosedPath(walk.Skip(shift).Concat(walk.Take(shift)).ToArray()), 1, scale);
            Same(name + " reversal", reference, WindingArea.ClosedPath(walk.Reverse().ToArray()), -1, scale);
            Same(name + " axis exchange", reference, WindingArea.ClosedPath(walk.Select(p => new Point2(p.Y, p.X)).ToArray()), -1, scale);
            Same(name + " translation", reference, WindingArea.ClosedPath(AffineTransform2D.Translation(1000.5, -700.25).Apply(walk)), 1, scale * 1e3);
            var scaled = WindingArea.ClosedPath(AffineTransform2D.Scaling(3).Apply(walk));
            Near(name + " scaling absolute", 9 * reference.AbsoluteWinding, scaled.AbsoluteWinding, 1e-12);
            Point2[] repeated = walk.SelectMany((p, i) => i % 3 == 0 ? new[] { p, p } : new[] { p }).Append(walk[0]).ToArray();
            Same(name + " duplicates and closing point", reference, WindingArea.ClosedPath(repeated), 1, scale);
        }
    }

    private static void CheckFilledRegions()
    {
        Point2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Point2[] offset = AffineTransform2D.Translation(.5, .5).Apply(square);
        Point2[] far = AffineTransform2D.Translation(100, 100).Apply(square);
        foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
        {
            Overlap("identity " + rule, WindingArea.FilledRegions(square, square, rule), 1, 1, 1, 1, 0);
            Overlap("reversed identity " + rule, WindingArea.FilledRegions(square, square.Reverse().ToArray(), rule), 1, 1, 1, 1, 0);
            Overlap("cyclic identity " + rule, WindingArea.FilledRegions(square, [square[2], square[3], square[0], square[1]], rule), 1, 1, 1, 1, 0);
            var diagonal = WindingArea.FilledRegions(square, offset, rule);
            Overlap("diagonal offset " + rule, diagonal, 1, 1, .25, 1.75, 1.5);
            Near("diagonal Jaccard " + rule, 6.0 / 7, diagonal.JaccardDistance!.Value);
            Near("diagonal IoU " + rule, 1.0 / 7, diagonal.IntersectionOverUnion!.Value);
            var apart = WindingArea.FilledRegions(square, far, rule);
            Overlap("far apart " + rule, apart, 1, 1, 0, 2, 2);
            Near("far apart Jaccard " + rule, 1, apart.JaccardDistance!.Value);
            True("metadata " + rule, diagonal.FillRule == rule);
        }
        Point2[] hole = Fixtures.Contours()["hole-with-retraced-bridge"];
        Point2[] inner = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        Overlap("hole and its filling", WindingArea.FilledRegions(hole, inner), 12, 4, 0, 16, 16);
        True("collinear region has undefined Jaccard", WindingArea.FilledRegions([new(0, 0), new(1, 0), new(2, 0)], [new(0, 0), new(1, 0), new(3, 0)]).JaccardDistance is null);

        Random random = new(55);
        foreach (bool grid in new[] { false, true })
        for (int trial = 0; trial < 150; trial++)
        {
            Point2[] a = RandomWalk(random, 5 + trial % 25, 1, grid), b = RandomWalk(random, 5 + (trial * 7) % 25, 1, grid);
            if (ClosedClean(a).Length < 3 || ClosedClean(b).Length < 3) continue;
            foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
            {
                FillRule clipperRule = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
                PathsD subject = [new PathD(a.Select(p => new PointD(p.X, p.Y)))], clip = [new PathD(b.Select(p => new PointD(p.X, p.Y)))];
                double Area(PathsD paths) => Math.Abs(Clipper.Area(paths));
                var result = WindingArea.FilledRegions(a, b, rule);
                var sweep = RegionSweep.Measure(ClosedClean(a), ClosedClean(b), rule == PathFillRule.NonZero);
                string name = $"{(grid ? "grid" : "random")} regions #{trial} {rule}";
                Near(name + " first vs slab sweep", sweep.First, result.FirstArea, 1e-9);
                Near(name + " second vs slab sweep", sweep.Second, result.SecondArea, 1e-9);
                Near(name + " intersection vs slab sweep", sweep.Intersection, result.IntersectionArea, 1e-9);
                Near(name + " union vs slab sweep", sweep.Union, result.UnionArea, 1e-9);
                Near(name + " symmetric difference vs slab sweep", sweep.SymmetricDifference, result.SymmetricDifferenceArea, 1e-9);
                double[] clipper = [Area(Clipper.Union(subject, new PathsD(), clipperRule, 8)), Area(Clipper.Union(clip, new PathsD(), clipperRule, 8)),
                    Area(Clipper.Intersect(subject, clip, clipperRule, 8)), Area(Clipper.Union(subject, clip, clipperRule, 8)), Area(Clipper.Xor(subject, clip, clipperRule, 8))];
                double[] ours = [result.FirstArea, result.SecondArea, result.IntersectionArea, result.UnionArea, result.SymmetricDifferenceArea];
                bool agrees = clipper.Zip(ours).All(x => Math.Abs(x.First - x.Second) <= 1e-7 * Math.Max(1, x.First));
                if (grid) { checkedClipper++; if (!agrees) ClipperDisagreements.Add((name, ClosedClean(a), ClosedClean(b))); }
                else True(name + " generic regions agree with Clipper", agrees);
                var swapped = WindingArea.FilledRegions(b, a, rule);
                Near(name + " exchange symmetry", result.IntersectionArea, swapped.IntersectionArea, 1e-12);
                Near(name + " inclusion-exclusion", result.FirstArea + result.SecondArea - result.IntersectionArea, result.UnionArea, 1e-12);
            }
        }
    }

    // A stroke vertex 2^-54 from a vertical edge on the normalized bound x = -0.5: the configuration that
    // made unfiltered floating-point crossing decisions inconsistent in real gesture pairs.
    private static void CheckReviewedNearDegeneracy()
    {
        double nudge = -0.5 + Math.Pow(2, -54);
        Point2[] q = [new(-.2, .5), new(-.5, .5), new(-.5, -.2), new(-.5, -.5), new(.3, -.5)];
        foreach (double x in new[] { nudge, -.5, -.5 - Math.Pow(2, -53) })
        {
            Point2[] p = [new(x, 0), new(-.3, .2), new(0, 0), new(.3, -.3)];
            var result = WindingArea.EndpointBridged(p, q);
            double clipper = ClipperOracle.Between(p, q);
            Near($"near-degenerate bridge x={x:R} vs Clipper", clipper, result.NonZero, 1e-7);
            Near($"near-degenerate bridge x={x:R} vs slab sweep", ContourSweep.Measure(p.Concat(q.Reverse()).ToArray()).NonZero, result.NonZero, 1e-12);
            if (x == -.5) True("exactly degenerate bridge used symbolic predicates", result.SymbolicTieBreakCount > 0);
        }
    }

    // Regressions for the independent review of 7dee036.
    private static void CheckReviewRegressions()
    {
        static Point2[] Square(double x, double y, double size = 1) => [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];
        PathFillRule[] rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];

        // Separated small regions: a shared distant origin used to cancel their whole area.
        foreach (double distance in new[] { 1e6, 1e8, 1e10, 1e12, 1e14 })
        foreach (PathFillRule rule in rules)
        {
            var apart = WindingArea.FilledRegions(Square(0, 0), Square(distance, distance), rule);
            Overlap($"unit squares {distance:G} apart {rule}", apart, 1, 1, 0, 2, 2);
            True($"unit squares {distance:G} apart Jaccard {rule}", apart.JaccardDistance == 1 && apart.IntersectionOverUnion == 0);
            Overlap($"overlapping unit squares at {distance:G} {rule}",
                WindingArea.FilledRegions(Square(distance, distance), Square(distance + .5, distance + .5), rule), 1, 1, .25, 1.75, 1.5);
            Areas($"unit square at {distance:G} {rule}", WindingArea.ClosedPath(Square(distance, -distance)), 1, 1, 1, 1);
        }
        var inside = WindingArea.FilledRegions(Square(0, 0, 1e8), Square(1e8 - 3, 1e8 - 3));
        Near("small square in a distant corner of a large one: second", 1, inside.SecondArea, 0);
        Near("small square in a distant corner of a large one: intersection", 1, inside.IntersectionArea, 1e-12);
        Near("small square in a distant corner of a large one: union", 1e16, inside.UnionArea, 1e-15);
        foreach (double distance in new[] { 1e4, 1e8 })
        {
            // One closed path: two unit squares joined by a retraced bridge. Rounding grows with the distance
            // of edges from the path's own center, so this bound is looser than for separate paths.
            Point2[] walk = [.. Square(0, 0), new(0, 0), .. Square(distance, distance), new(distance, distance)];
            var result = WindingArea.ClosedPath(walk);
            double tolerance = 1e-15 * distance * 16;
            Near($"retraced bridge {distance:G} nonzero", 2, result.NonZero, tolerance);
            Near($"retraced bridge {distance:G} absolute", 2, result.AbsoluteWinding, tolerance);
            Near($"retraced bridge {distance:G} signed", 2, result.Signed, tolerance);
        }

        // Follow-up review of 3f365f2: a small symmetric difference between large regions must survive.
        // Inclusion-exclusion from rounded totals returned zero for all of these.
        double cutSize = Math.Pow(2, -27);
        Point2[] unitSquare = Square(0, 0);
        Point2[] cornerCut = [new(cutSize, 0), new(1, 0), new(1, 1), new(0, 1), new(0, cutSize)];
        Point2[] withHole = [.. unitSquare, new(0, 0), new(.25, .25), new(.25, .25 + cutSize), new(.25 + cutSize, .25 + cutSize), new(.25 + cutSize, .25), new(.25, .25)];
        foreach (PathFillRule rule in rules)
        foreach (var (label, first, second, expected) in new[]
        {
            ("corner cut", unitSquare, cornerCut, cutSize * cutSize / 2), ("hole", unitSquare, withHole, cutSize * cutSize)
        })
        foreach (var (variant, a, b) in new[]
        {
            ("normal", first, second), ("swap", second, first),
            ("reverse first", first.Reverse().ToArray(), second), ("reverse second", first, second.Reverse().ToArray())
        })
        {
            var result = WindingArea.FilledRegions(a, b, rule);
            string name = $"{label} of side 2^-27 {variant} {rule}";
            True(name + " symmetric difference", NearlyRelative(expected, result.SymmetricDifferenceArea, 1e-9));
            True(name + " Jaccard distance", result.JaccardDistance is double jd && NearlyRelative(expected, jd, 1e-9));
            Near(name + " union", 1, result.UnionArea, 1e-15);
        }
        foreach (double length in new[] { 1e4, 1e6, 1e8 })
        {
            // A unit clockwise hole joined to the outer boundary by a retraced bridge, integer coordinates.
            Point2[] large = Square(0, 0, length);
            Point2[] holed = [.. large, new(0, 0), new(2, 2), new(2, 3), new(3, 3), new(3, 2), new(2, 2)];
            foreach (PathFillRule rule in rules)
            foreach (var (variant, a, b) in new[] { ("normal", large, holed), ("swap", holed, large), ("reverse", large.Reverse().ToArray(), holed.Reverse().ToArray()) })
            {
                var result = WindingArea.FilledRegions(a, b, rule);
                string name = $"unit hole in square {length:G} {variant} {rule}";
                True(name + " symmetric difference", NearlyRelative(1, result.SymmetricDifferenceArea, 1e-12));
                True(name + " union", NearlyRelative(length * length, result.UnionArea, 1e-15));
                True(name + " intersection", NearlyRelative(length * length - 1, result.IntersectionArea, 1e-15));
            }
        }

        // Crossings next to the far end of a long edge: 1 - t cannot represent them, so both ends are kept.
        // The expected areas use the actual rounded coordinates of the shifted squares.
        foreach (double length in new[] { 1e4, 1e6, 1e8, 1e12 })
        foreach (double shift in new[] { 1e-8, 3e-7 })
        {
            Point2[] square = Square(0, 0, length), moved = Square(shift, 0, length), lifted = Square(0, shift, length);
            double expected = length * shift + (length + shift - length) * length;
            foreach (var (variant, a, b) in new[] { ("x", square, moved), ("x swapped", moved, square), ("y", square, lifted), ("x reversed", square.Reverse().ToArray(), moved) })
            {
                var result = WindingArea.FilledRegions(a, b);
                True($"square {length:G} shifted {shift:G} in {variant}: symmetric difference", NearlyRelative(expected, result.SymmetricDifferenceArea, 1e-9));
            }
        }

        // Perpendicular unit-wide strips of half-length 1e6 to 1e16: the crossings sit mid-edge, closer than the
        // parameter resolution of the longest edges, and are ordered and placed by their points.
        foreach (double length in new[] { 1e6, 1e8, 1e10, 1e12, 1e14, 1e16 })
        {
            Point2[] horizontal = [new(-length, -.5), new(length, -.5), new(length, .5), new(-length, .5)];
            Point2[] vertical = [new(-.5, -length), new(.5, -length), new(.5, length), new(-.5, length)];
            var strips = WindingArea.FilledRegions(horizontal, vertical);
            True($"crossed strips {length:G} intersection", NearlyRelative(1, strips.IntersectionArea, 1e-12));
            True($"crossed strips {length:G} symmetric difference", NearlyRelative(4 * length - 2, strips.SymmetricDifferenceArea, 1e-15));
        }

        // Follow-up review of 474db55: a midpoint formed in world coordinates rounded d + 1/2 to d, and halving a
        // subnormal coordinate underflowed. Both triangles have exactly representable areas.
        double big = Math.ScaleB(1, 52), smallest = double.Epsilon, tall = Math.ScaleB(1, 300);
        foreach (var (label, triangle, area) in new[]
        {
            ("triangle at 2^52", new Point2[] { new(big, big), new(big + 1, big), new(big, big + 1) }, .5),
            ("subnormal triangle", new Point2[] { new(0, 0), new(smallest, 0), new(0, tall) }, Math.ScaleB(1, -775))
        })
        {
            var closed = WindingArea.ClosedPath(triangle);
            True(label + " closed path", closed.NonZero == area && closed.EvenOdd == area && closed.AbsoluteWinding == area && closed.Signed == area);
            var self = WindingArea.FilledRegions(triangle, triangle);
            True(label + " filled with itself", self.FirstArea == area && self.SecondArea == area && self.IntersectionArea == area
                && self.UnionArea == area && self.SymmetricDifferenceArea == 0 && self.IntersectionOverUnion == 1);
        }

        // Unit-wide strips of half-length l tilted by m / l cross near the origin. Parameters of the crossings on
        // the long edges were ordered opposite to their shared points, which reversed part of the intersection
        // boundary (negative area at l = 1e16). The exact intersection of the binary64 input is 4000000/1000001
        // (rational clipping). Points are rounded relative to the coordinates of the edges that form them, about
        // m times the strip width, so the bound scales with m; exactly scaling by 2^-54 changes nothing.
        foreach (double l in new[] { 1e8, 1e12, 1e16 })
        foreach (double scale in new[] { 1, Math.ScaleB(1, -54) })
        {
            double m = l / 1000;
            Point2[] a = [new(-l * scale, (-m - 1) * scale), new(l * scale, (m - 1) * scale), new(l * scale, (m + 1) * scale), new(-l * scale, (-m + 1) * scale)];
            Point2[] b = [new((m - 1) * scale, -l * scale), new((m + 1) * scale, -l * scale), new((-m + 1) * scale, l * scale), new((-m - 1) * scale, l * scale)];
            double exact = 4000000.0 / 1000001 * scale * scale, bound = 8 * m * Math.ScaleB(1, -53);
            foreach (PathFillRule rule in rules)
            foreach (var (variant, first, second) in new[]
            {
                ("normal", a, b), ("swap", b, a), ("reverse both", a.Reverse().ToArray(), b.Reverse().ToArray()), ("reverse first", a.Reverse().ToArray(), b)
            })
            {
                var result = WindingArea.FilledRegions(first, second, rule);
                string name = $"tilted strips {l:G} scaled {scale:G3} {variant} {rule}";
                True(name + " intersection", result.IntersectionArea > 0 && NearlyRelative(exact, result.IntersectionArea, bound));
                True(name + " symmetric difference", NearlyRelative(8 * l * scale * scale - 2 * exact, result.SymmetricDifferenceArea, 1e-15));
            }
        }

        // A zigzag across a strip 2^101 long: every crossing on the strip's long edges has t = u = 1/2 in binary64,
        // so only the points order them. Each zigzag segment covers 1/2 of the strip.
        foreach (int count in new[] { 32, 256 })
        {
            double half = Math.ScaleB(1, 100);
            Point2[] strip = [new(-half, 0), new(half, 0), new(half, 1), new(-half, 1)];
            Point2[] zigzag = [.. Enumerable.Range(1, count).Select(k => new Point2(k, k % 2 == 1 ? -1 : 2)), new(count, -3), new(1, -3)];
            var result = WindingArea.FilledRegions(strip, zigzag);
            True($"zigzag of {count} across a 2^101 strip", NearlyRelative((count - 1) / 2.0, result.IntersectionArea, 1e-14));
        }

        // Tiny coordinates: a squared edge length underflowed and a guessed midpoint halved the area.
        double tiny = Math.Pow(2, -600);
        var flat = WindingArea.ClosedPath([new(0, 0), new(3 * tiny, 0), new(-2 * tiny, 0), new(0, -1), new(3 * tiny, 0)]);
        foreach (var (name, value) in new[] { ("nonzero", flat.NonZero), ("evenodd", flat.EvenOdd), ("absolute", flat.AbsoluteWinding), ("signed", flat.Signed) })
            True("tiny retraced triangle " + name, NearlyRelative(2.5 * tiny, value, 1e-12));
        True("tiny retraced triangle keeps |w| >= |signed|", flat.AbsoluteWinding >= Math.Abs(flat.Signed) * (1 - 1e-12));

        // Anisotropic and tiny scales on degenerate integer walks and region pairs, against unscaled oracles.
        Random random = new(734221);
        foreach (var (sx, sy) in new[] { (Math.Pow(2, -600), Math.Pow(2, 300)), (Math.Pow(2, 300), Math.Pow(2, -600)), (Math.Pow(2, -500), Math.Pow(2, -500)), (1e-3, 1e5) })
        for (int trial = 0; trial < 250; trial++)
        {
            Point2[] points = ClosedClean(Enumerable.Range(0, random.Next(4, 16)).Select(_ => new Point2(random.Next(-3, 4), random.Next(-3, 4))).ToArray());
            if (points.Length < 3) continue;
            Point2[] scaled = points.Select(p => new Point2(p.X * sx, p.Y * sy)).ToArray();
            var expected = ContourSweep.Measure(points);
            var actual = WindingArea.ClosedPath(scaled);
            double areaUnit = sx * sy;
            string name = $"anisotropic ({sx:G3},{sy:G3}) #{trial}";
            True(name + " nonzero", NearlyScaled(expected.NonZero, actual.NonZero, areaUnit));
            True(name + " evenodd", NearlyScaled(expected.EvenOdd, actual.EvenOdd, areaUnit));
            True(name + " absolute", NearlyScaled(expected.AbsoluteWinding, actual.AbsoluteWinding, areaUnit));
            True(name + " signed", NearlyScaled(expected.Signed, actual.Signed, areaUnit));
            if (trial % 5 != 0) continue;
            Point2[] other = ClosedClean(Enumerable.Range(0, random.Next(4, 12)).Select(_ => new Point2(random.Next(-3, 4), random.Next(-3, 4))).ToArray());
            if (other.Length < 3) continue;
            var regions = RegionSweep.Measure(points, other, true);
            var overlap = WindingArea.FilledRegions(scaled, other.Select(p => new Point2(p.X * sx, p.Y * sy)).ToArray());
            True(name + " regions intersection", NearlyScaled(regions.Intersection, overlap.IntersectionArea, areaUnit));
            True(name + " regions union", NearlyScaled(regions.Union, overlap.UnionArea, areaUnit));
            True(name + " regions difference", NearlyScaled(regions.SymmetricDifference, overlap.SymmetricDifferenceArea, areaUnit));
        }

        // Large translations keep results close to the untranslated ones; the input itself is rounded.
        for (int trial = 0; trial < 100; trial++)
        {
            Point2[] walk = ClosedClean(RandomWalk(random, 6 + trial % 20, 1, false));
            if (walk.Length < 3) continue;
            var reference = WindingArea.ClosedPath(walk);
            var moved = WindingArea.ClosedPath(AffineTransform2D.Translation(1e8, -1e8).Apply(walk));
            double tolerance = 1e-5 * Math.Max(1, reference.AbsoluteWinding);
            Near("translated by 1e8 nonzero #" + trial, reference.NonZero, moved.NonZero, tolerance);
            Near("translated by 1e8 absolute #" + trial, reference.AbsoluteWinding, moved.AbsoluteWinding, tolerance);
        }

        // Calls nested inside a list indexer on the same thread must not share working storage.
        Point2[] unit = Square(0, 0), shifted = Square(.5, .5);
        Point2[] open = [new(0, 0), new(1, 1), new(2, 0)], openOther = [new(0, 1), new(1, 0), new(2, 1)];
        var plainClosed = WindingArea.ClosedPath(unit);
        var plainBridged = WindingArea.EndpointBridged(open, openOther);
        var plainRegions = WindingArea.FilledRegions(unit, shifted);
        Action[] nested =
        [
            () => WindingArea.ClosedPath(Square(10, 10, 10)),
            () => WindingArea.EndpointBridged([new(5, 5), new(9, 1), new(7, 8)], [new(5, 9), new(9, 5)]),
            () => WindingArea.FilledRegions(Square(20, 20, 3), Square(21, 21, 3)),
            () => WindingArea.ClosedPath(new NestedList(Square(40, 40, 7), () => WindingArea.ClosedPath(Square(-9, -9, 4)))),
            () => { try { WindingArea.ClosedPath([new(0, 0), new(double.NaN, 0), new(0, 1)]); } catch (ArgumentException) { } }
        ];
        for (int k = 0; k < nested.Length; k++)
        {
            string name = "nested call " + k;
            Identical(name + " inside ClosedPath", plainClosed, WindingArea.ClosedPath(new NestedList(unit, nested[k])));
            Identical(name + " inside EndpointBridged first", plainBridged, WindingArea.EndpointBridged(new NestedList(open, nested[k]), openOther));
            Identical(name + " inside EndpointBridged second", plainBridged, WindingArea.EndpointBridged(open, new NestedList(openOther, nested[k])));
            Identical(name + " inside FilledRegions first", plainRegions, WindingArea.FilledRegions(new NestedList(unit, nested[k]), shifted));
            Identical(name + " inside FilledRegions second", plainRegions, WindingArea.FilledRegions(unit, new NestedList(shifted, nested[k])));
        }
        // An outer call that fails after a nested call still releases its storage.
        Reject<InvalidOperationException>("indexer failure after a nested call", () => WindingArea.ClosedPath(new NestedList(unit, () =>
        {
            WindingArea.ClosedPath(Square(3, 3, 2));
            throw new InvalidOperationException("indexer failure");
        })));
        Identical("call after a failed nested call", plainClosed, WindingArea.ClosedPath(unit));
        Identical("regions after a failed nested call", plainRegions, WindingArea.FilledRegions(unit, shifted));
    }

    /// <summary>A valid list whose indexer runs other geometry when index 1 is read.</summary>
    private sealed class NestedList(Point2[] data, Action onSecond) : IReadOnlyList<Point2>
    {
        public int Count => data.Length;
        public Point2 this[int index] { get { if (index == 1) onSecond(); return data[index]; } }
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)data).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static bool NearlyRelative(double expected, double actual, double relative) =>
        double.IsFinite(actual) && Math.Abs(expected - actual) <= relative * Math.Abs(expected);

    // Scaled integer-grid results: compare in units of the area scale, which may be far from 1.
    private static bool NearlyScaled(double unscaled, double actual, double unit) =>
        double.IsFinite(actual) && Math.Abs(actual / unit - unscaled) <= 1e-9 * Math.Max(1, Math.Abs(unscaled));

    private static void Identical(string name, WindingAreaResult expected, WindingAreaResult actual) =>
        True(name, expected.NonZero == actual.NonZero && expected.EvenOdd == actual.EvenOdd &&
            expected.AbsoluteWinding == actual.AbsoluteWinding && expected.Signed == actual.Signed && expected.CrossingCount == actual.CrossingCount);

    private static void Identical(string name, WindingOverlapResult expected, WindingOverlapResult actual) =>
        True(name, expected.FirstArea == actual.FirstArea && expected.SecondArea == actual.SecondArea &&
            expected.IntersectionArea == actual.IntersectionArea && expected.UnionArea == actual.UnionArea &&
            expected.SymmetricDifferenceArea == actual.SymmetricDifferenceArea);

    private static void CheckAllocations()
    {
        Point2[] p = Enumerable.Range(0, 64).Select(i => new Point2(i / 63.0, Math.Sin(i / 6.0) * .3)).ToArray();
        Point2[] q = Enumerable.Range(0, 64).Select(i => new Point2(i / 63.0, Math.Cos(i / 5.0) * .3)).ToArray();
        Point2[] closedA = Enumerable.Range(0, 64).Select(i => new Point2(Math.Cos(i / 10.0), Math.Sin(i / 10.0) * (1 + .3 * Math.Sin(i)))).ToArray();
        Point2[] closedB = AffineTransform2D.Translation(.2, .1).Apply(closedA);
        double sink = 0;
        for (int i = 0; i < 20; i++) sink += WindingArea.EndpointBridged(p, q).NonZero + WindingArea.FilledRegions(closedA, closedB).UnionArea;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) sink += WindingArea.EndpointBridged(p, q).NonZero + WindingArea.FilledRegions(closedA, closedB).UnionArea;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        True($"steady-state winding calls allocate nothing ({bytes} bytes)", bytes == 0 && sink > 0);
    }

    private static void CheckInvalidInputs()
    {
        Point2[] ok = [new(0, 0), new(1, 0), new(0, 1)];
        Reject<ArgumentNullException>("null closed path", () => WindingArea.ClosedPath(null!));
        Reject<ArgumentException>("empty closed path", () => WindingArea.ClosedPath([]));
        Reject<ArgumentException>("closed path too short", () => WindingArea.ClosedPath([new(0, 0), new(1, 0), new(1, 0), new(0, 0)]));
        Reject<ArgumentException>("nonfinite closed path", () => WindingArea.ClosedPath([new(0, 0), new(double.NaN, 0), new(0, 1)]));
        Reject<ArgumentException>("too large coordinate", () => WindingArea.ClosedPath([new(0, 0), new(1e101, 0), new(0, 1)]));
        Reject<ArgumentNullException>("null bridged path", () => WindingArea.EndpointBridged(ok, null!));
        Reject<ArgumentException>("bridged single point", () => WindingArea.EndpointBridged(ok, [new(1, 1), new(1, 1)]));
        Reject<ArgumentException>("bridged infinite", () => WindingArea.EndpointBridged(ok, [new(1, 1), new(double.PositiveInfinity, 1)]));
        Reject<ArgumentNullException>("null region", () => WindingArea.FilledRegions(null!, ok));
        Reject<ArgumentException>("region too short", () => WindingArea.FilledRegions(ok, [new(0, 0), new(1, 1), new(0, 0)]));
        Reject<ArgumentOutOfRangeException>("undefined fill rule", () => WindingArea.FilledRegions(ok, ok, (PathFillRule)7));
        Areas("bridged collapse to a segment", WindingArea.EndpointBridged([new(0, 0), new(1, 0)], [new(0, 0), new(1, 0)]), 0, 0, 0, 0);
    }

    private static Point2[] ClosedClean(Point2[] walk)
    {
        Point2[] clean = Geometry.Clean(walk);
        int count = clean.Length;
        while (count > 1 && Geometry.Same(clean[0], clean[count - 1])) count--;
        return clean[..count];
    }

    private static Point2[] RandomWalk(Random random, int n, double step, bool grid)
    {
        var walk = new Point2[n];
        double x = 0, y = 0;
        for (int i = 0; i < n; i++)
        {
            if (grid) { x = random.Next(0, 4); y = random.Next(0, 4); }
            else { x += (random.NextDouble() * 2 - 1) * step; y += (random.NextDouble() * 2 - 1) * step; }
            walk[i] = new(x, y);
        }
        return walk;
    }

    private static int RationalSign(Point2 a, Point2 b, Point2 c)
    {
        var (ax, ay, bx, by, cx, cy) = (Exact(a.X), Exact(a.Y), Exact(b.X), Exact(b.Y), Exact(c.X), Exact(c.Y));
        // (b - a) x (c - a) with each term a fraction m / 2^k; bring everything to 2^-1074 units.
        return ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)).Sign;
    }

    private static BigInteger Exact(double value)
    {
        if (value == 0) return BigInteger.Zero;
        long bits = BitConverter.DoubleToInt64Bits(value);
        int biased = (int)((bits >> 52) & 0x7FF);
        long mantissa = biased == 0 ? bits & 0xFFFFFFFFFFFFFL : (bits & 0xFFFFFFFFFFFFFL) | (1L << 52);
        int shift = (biased == 0 ? 1 : biased) - 1;
        BigInteger result = new BigInteger(mantissa) << shift; // value * 2^1074
        return bits < 0 ? -result : result;
    }

    private static void Areas(string name, WindingAreaResult actual, double nonZero, double evenOdd, double absolute, double signed)
    {
        Near(name + " nonzero", nonZero, actual.NonZero);
        Near(name + " evenodd", evenOdd, actual.EvenOdd);
        Near(name + " absolute", absolute, actual.AbsoluteWinding);
        Near(name + " signed", signed, actual.Signed);
    }

    private static void Overlap(string name, WindingOverlapResult actual, double first, double second, double intersection, double union, double difference)
    {
        Near(name + " first area", first, actual.FirstArea);
        Near(name + " second area", second, actual.SecondArea);
        Near(name + " intersection", intersection, actual.IntersectionArea);
        Near(name + " union", union, actual.UnionArea);
        Near(name + " symmetric difference", difference, actual.SymmetricDifferenceArea);
    }

    private static void Same(string name, WindingAreaResult expected, WindingAreaResult actual, int signedFactor, double scale)
    {
        double tolerance = 1e-12 * scale;
        Near(name + " nonzero", expected.NonZero, actual.NonZero, tolerance);
        Near(name + " evenodd", expected.EvenOdd, actual.EvenOdd, tolerance);
        Near(name + " absolute", expected.AbsoluteWinding, actual.AbsoluteWinding, tolerance);
        Near(name + " signed", signedFactor * expected.Signed, actual.Signed, tolerance);
    }

    private static void Near(string name, double expected, double actual, double tolerance = 2e-12)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance * Math.Max(1, Math.Abs(expected)))
            throw new Exception($"{name}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}");
        passed++;
    }

    private static void True(string name, bool condition)
    {
        if (!condition) throw new Exception(name);
        passed++;
    }

    private static void Reject<T>(string name, Action action) where T : Exception
    {
        try { action(); }
        catch (T) { passed++; return; }
        throw new Exception(name + ": expected " + typeof(T).Name);
    }
}
