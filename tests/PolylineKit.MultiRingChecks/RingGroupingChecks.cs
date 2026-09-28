namespace PolylineKit.MultiRingChecks;

/// <summary>Public region checks with independent oracles for many connected and disconnected rings.</summary>
internal static class RingGroupingChecks
{
    internal static object Run()
    {
        var checks = new Checks();
        checks.SparseGrids();
        checks.LongParallelRectangles();
        checks.HolesAndIslands();
        checks.ContactsAndChains();
        checks.DistantSmallComponents();
        checks.CollinearBounds();
        checks.CoincidentRings();
        checks.RectangleCells();
        checks.ReuseAndConcurrency();
        return new
        {
            status = "passed",
            assertions = checks.Assertions,
            sparseRingCounts = new[] { 17, 64, 257, 1024 },
            longRectangleRingCounts = new[] { 64, 257 },
            rectanglePairs = Checks.RectanglePairCount,
            rectangleSeed = "0xc18e742b",
            concurrentQueries = Checks.ConcurrentQueries,
            oracle = "Analytic rectangle areas and independent signed integer-cell containment; only public PreparedRegion/RegionArea calls.",
            scope = "Sparse grids and long parallel rectangles on both axes, nesting, transitive overlaps, inclusive contacts, zero-width/height bounds, repeated rings and workspace reuse.",
            tolerance = "3e-12 relative to each expected nonzero area; exact zero is required. Tiny components do not use an absolute tolerance floor."
        };
    }

    private sealed class Checks
    {
        internal const int RectanglePairCount = 8;
        internal const int ConcurrentQueries = 12;
        private int assertions;
        internal int Assertions => assertions;
        private static readonly PathFillRule[] Rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];

        internal void SparseGrids()
        {
            // Alternating sizes also exercises workspace growth and subsequent shorter logical prefixes.
            foreach (int count in new[] { 17, 1024, 64, 257, 17 })
                foreach (bool row in new[] { true, false })
                    foreach (bool transformed in new[] { false, true })
                    {
                        PreparedRegion a = Grid(count, row, 0, transformed), b = Grid(count, row, .25, transformed);
                        foreach (PathFillRule rule in Rules)
                            Check($"Sparse {count}/row={row}/transformed={transformed}/{rule}", a, b, rule,
                                count, count, count * 9d / 16, swap: true);
                    }
        }

        internal void LongParallelRectangles()
        {
            const double length = 8192;
            foreach (int count in new[] { 64, 257 })
            {
                var a = new IReadOnlyList<Point2>[count];
                var b = new IReadOnlyList<Point2>[count];
                for (int i = 0; i < count; i++)
                {
                    double y = 4 * i;
                    a[i] = Rectangle(-4096, y, 4096, y + 1);
                    b[i] = Rectangle(-4095.75, y + .25, 4096.25, y + 1.25);
                }
                // Full bounds are much wider than tall, but only the Y centers separate components.
                // The transposed, permuted case exercises the corresponding vertical configuration.
                foreach (bool transformed in new[] { false, true })
                    foreach (PathFillRule rule in Rules)
                        Check($"Long parallel rectangles {count}/{transformed}/{rule}", Prepare(a, transformed), Prepare(b, transformed), rule,
                            count * length, count * length, count * (length - .25) * .75, swap: true);
            }
        }

        internal void HolesAndIslands()
        {
            var a = new List<IReadOnlyList<Point2>>();
            var b = new List<IReadOnlyList<Point2>>();
            for (int i = 0; i < 64; i++)
            {
                double x = 10 * (i % 8), y = 10 * (i / 8);
                a.Add(Rectangle(x, y, x + 6, y + 6));
                a.Add(Reverse(Rectangle(x + 1, y + 1, x + 5, y + 5)));
                a.Add(Rectangle(x + 2, y + 2, x + 4, y + 4));
                b.Add(Rectangle(x + 1, y + 1, x + 5, y + 5));
            }
            foreach (bool transformed in new[] { false, true })
                foreach (PathFillRule rule in Rules)
                    Check($"Disconnected shells/holes/islands/{transformed}/{rule}", Prepare(a, transformed), Prepare(b, transformed), rule,
                        64 * 24, 64 * 16, 64 * 4, swap: true);
        }

        internal void ContactsAndChains()
        {
            const int count = 257;
            var edgeA = new List<IReadOnlyList<Point2>>();
            var edgeB = new List<IReadOnlyList<Point2>>();
            var pointA = new List<IReadOnlyList<Point2>>();
            var pointB = new List<IReadOnlyList<Point2>>();
            var overlaps = new List<IReadOnlyList<Point2>>();
            for (int i = 0; i < count; i++)
            {
                edgeA.Add(Rectangle(4 * i, 0, 4 * i + 1, 1));
                pointA.Add(Rectangle(2 * i, 2 * i, 2 * i + 1, 2 * i + 1));
                overlaps.Add(Rectangle(i, 0, i + 2, 1));
                if (i + 1 < count)
                {
                    edgeB.Add(Rectangle(4 * i + 1, 0, 4 * i + 4, 1));
                    pointB.Add(Rectangle(2 * i + 1, 2 * i + 1, 2 * i + 2, 2 * i + 2));
                }
            }
            PreparedRegion cover = PreparedRegion.FromRings(new[] { Rectangle(0, 0, count + 1, 1) });
            foreach (bool transformed in new[] { false, true })
                foreach (PathFillRule rule in Rules)
                {
                    Check($"Shared-edge chain/{transformed}/{rule}", Prepare(edgeA, transformed), Prepare(edgeB, transformed), rule,
                        count, 3 * (count - 1), 0, swap: true);
                    Check($"Point-contact chain/{transformed}/{rule}", Prepare(pointA, transformed), Prepare(pointB, transformed), rule,
                        count, count - 1, 0, swap: true);
                    // Neighbours overlap; distant members connect only transitively. Interior winding is 2.
                    double filled = rule == PathFillRule.NonZero ? count + 1 : 2;
                    PreparedRegion probe = transformed ? Prepare([Rectangle(0, 0, count + 1, 1)], true) : cover;
                    Check($"Transitive positive-overlap chain/{transformed}/{rule}", Prepare(overlaps, transformed), probe, rule,
                        filled, count + 1, filled, swap: true);
                }
        }

        internal void DistantSmallComponents()
        {
            foreach ((double distance, double side) in new[] { (1e12, .001), (1e14, .1) })
            {
                var rings = new List<IReadOnlyList<Point2>>();
                double expected = 0;
                // All 64 tiny components need local numerical origins despite a very large overall extent.
                for (int i = 0; i < 64; i++)
                {
                    double x = (i % 32) * 4 + (i >= 32 ? distance : 0), y = (i & 1) * distance;
                    double actualWidth = x + side - x, actualHeight = y + side - y;
                    rings.Add(Rectangle(x, y, x + side, y + side));
                    expected += actualWidth * actualHeight;
                }
                PreparedRegion probe = PreparedRegion.FromRings(new[] { Rectangle(0, 0, side, side) });
                foreach (bool transformed in new[] { false, true })
                    foreach (PathFillRule rule in Rules)
                    {
                        PreparedRegion a = Prepare(rings, transformed);
                        Check($"Distant tiny components/{distance:R}/{transformed}/{rule}", a, a, rule, expected, expected, expected);
                        Check($"One tiny component/{distance:R}/{transformed}/{rule}", a, probe, rule, expected, side * side, side * side);
                    }
            }
        }

        internal void CollinearBounds()
        {
            var horizontal = new List<IReadOnlyList<Point2>>();
            var vertical = new List<IReadOnlyList<Point2>>();
            for (int i = 0; i < 257; i++)
            {
                horizontal.Add(new Point2[] { new(4 * i, 0), new(4 * i + 1, 0), new(4 * i + 2, 0) });
                vertical.Add(new Point2[] { new(0, 4 * i), new(0, 4 * i + 1), new(0, 4 * i + 2) });
            }
            PreparedRegion h = Prepare(horizontal, false), v = Prepare(vertical, false);
            PreparedRegion square = PreparedRegion.FromRings(new[] { Rectangle(-1, -1, 1100, 1100) });
            foreach (PathFillRule rule in Rules)
            {
                Check("Zero-height and zero-width regions " + rule, h, v, rule, 0, 0, 0, swap: true);
                Check("Zero-area components inside a filled shell " + rule, h, square, rule, 0, 1101d * 1101, 0, swap: true);
            }
        }

        internal void CoincidentRings()
        {
            PreparedRegion b = PreparedRegion.FromRings(new[] { Rectangle(1, 0, 3, 2) });
            foreach (int count in new[] { 64, 65 })
                foreach (bool alternate in new[] { false, true })
                {
                    var rings = new IReadOnlyList<Point2>[count];
                    for (int i = 0; i < count; i++)
                        rings[i] = alternate && (i & 1) != 0 ? Reverse(Rectangle(0, 0, 2, 2)) : Rectangle(0, 0, 2, 2);
                    PreparedRegion a = Prepare(rings, false);
                    foreach (PathFillRule rule in Rules)
                    {
                        bool filled = rule == PathFillRule.EvenOdd || alternate ? (count & 1) != 0 : true;
                        Check($"Coincident {count}/alternate={alternate}/{rule}", a, b, rule, filled ? 4 : 0, 4, filled ? 2 : 0, swap: true);
                    }
                }
        }

        internal void RectangleCells()
        {
            var random = new FixedRandom(0xc18e742b);
            for (int pair = 0; pair < RectanglePairCount; pair++)
            {
                int count = 32 * (1 + pair % 4);
                Rect[] a = Draw(ref random, count), b = Draw(ref random, count);
                PreparedRegion first = PreparedRegion.FromRings(a.Select(r => r.Points()).ToArray());
                PreparedRegion second = PreparedRegion.FromRings(b.Select(r => r.Points()).ToArray());
                foreach (PathFillRule rule in Rules)
                {
                    (double areaA, double areaB, double intersection) = CellOracle(a, b, rule);
                    Check($"Integer cells pair {pair}/{count}/{rule}", first, second, rule, areaA, areaB, intersection, swap: true);
                }
            }
        }

        internal void ReuseAndConcurrency()
        {
            PreparedRegion[] a = [Grid(17, true, 0, false), Grid(1024, false, 0, true)];
            PreparedRegion[] b = [Grid(17, true, .25, false), Grid(1024, false, .25, true)];
            // The same immutable prepared instances are shared; each worker alternates workspace sizes.
            Parallel.For(0, ConcurrentQueries, i =>
            {
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    int index = (i + repeat) & 1;
                    int count = index == 0 ? 17 : 1024;
                    Check($"Parallel reuse {i}/{repeat}", a[index], b[index], Rules[(i >> 1) & 1], count, count, count * 9d / 16);
                }
            });
        }

        private void Check(string name, PreparedRegion a, PreparedRegion b, PathFillRule rule,
            double areaA, double areaB, double intersection, bool swap = false)
        {
            Compare(name, RegionArea.Compare(a, b, rule), rule, areaA, areaB, intersection);
            Near(name + " selected first", areaA, RegionArea.FilledArea(a, rule));
            Near(name + " selected second", areaB, RegionArea.FilledArea(b, rule));
            Near(name + " selected intersection", intersection, RegionArea.IntersectionArea(a, b, rule));
            if (swap) Compare(name + " swapped", RegionArea.Compare(b, a, rule), rule, areaB, areaA, intersection);
        }

        private void Compare(string name, RegionOverlapResult result, PathFillRule rule, double a, double b, double intersection)
        {
            double union = a + b - intersection, xor = a + b - 2 * intersection;
            Near(name + " first", a, result.FirstArea);
            Near(name + " second", b, result.SecondArea);
            Near(name + " intersection", intersection, result.IntersectionArea);
            Near(name + " union", union, result.UnionArea);
            Near(name + " xor", xor, result.SymmetricDifferenceArea);
            True(name + " reported rule", result.FillRule == rule);
            if (union > 0)
            {
                True(name + " defined ratios", result.IntersectionOverUnion.HasValue && result.JaccardDistance.HasValue);
                Near(name + " IoU", intersection / union, result.IntersectionOverUnion!.Value);
                Near(name + " Jaccard", xor / union, result.JaccardDistance!.Value);
            }
            else True(name + " undefined zero-union ratios", result.IntersectionOverUnion is null && result.JaccardDistance is null);
        }

        private void Near(string name, double expected, double actual) => True($"{name}: expected {expected:R}, actual {actual:R}",
            double.IsFinite(actual) && Math.Abs(actual - expected) <= 3e-12 * Math.Abs(expected));

        private void True(string name, bool value)
        {
            if (!value) throw new InvalidOperationException(name);
            Interlocked.Increment(ref assertions);
        }
    }

    private static PreparedRegion Grid(int count, bool row, double shift, bool transformed)
    {
        var rings = new IReadOnlyList<Point2>[count];
        int width = row ? count : (int)Math.Ceiling(Math.Sqrt(count));
        for (int i = 0; i < count; i++)
        {
            double x = 4 * (i % width) + shift, y = 4 * (i / width) + shift;
            rings[i] = Rectangle(x, y, x + 1, y + 1);
        }
        return Prepare(rings, transformed);
    }

    private static PreparedRegion Prepare(IReadOnlyList<IReadOnlyList<Point2>> rings, bool transformed)
    {
        if (!transformed) return PreparedRegion.FromRings(rings);
        // Transposition, complete reversal, changed starting vertices and deterministic ring permutation.
        // Complete reversal preserves both fills, while transposition exercises the other spatial axis.
        IReadOnlyList<Point2>[] changed = rings.Select((ring, index) => (IReadOnlyList<Point2>)Enumerable.Range(0, ring.Count)
            .Select(j => ring[(index + ring.Count - j) % ring.Count]).Select(p => new Point2(p.Y, p.X)).ToArray()).ToArray();
        var random = new FixedRandom(0x11cd82f3);
        for (int i = changed.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (changed[i], changed[j]) = (changed[j], changed[i]);
        }
        return PreparedRegion.FromRings(changed);
    }

    private readonly record struct Rect(int X0, int Y0, int X1, int Y1, int Sign)
    {
        internal Point2[] Points() => Sign > 0 ? Rectangle(X0, Y0, X1, Y1) : Reverse(Rectangle(X0, Y0, X1, Y1));
        internal int Contribution(int x, int y) => x >= X0 && x < X1 && y >= Y0 && y < Y1 ? Sign : 0;
    }

    private static Rect[] Draw(ref FixedRandom random, int count)
    {
        var result = new Rect[count];
        for (int i = 0; i < count; i++)
        {
            int x = random.Next(25) - 12, y = random.Next(25) - 12;
            result[i] = new Rect(x, y, x + 1 + random.Next(6), y + 1 + random.Next(6), random.Next(2) == 0 ? -1 : 1);
            if (i > 0 && random.Next(4) == 0)
            {
                Rect earlier = result[random.Next(i)];
                result[i] = random.Next(2) == 0 ? earlier : earlier with { Sign = -earlier.Sign };
            }
        }
        return result;
    }

    private static (double A, double B, double Intersection) CellOracle(Rect[] a, Rect[] b, PathFillRule rule)
    {
        int areaA = 0, areaB = 0, intersection = 0;
        // Integer rectangle boundaries partition this fixed grid exactly; every included cell has unit area.
        for (int y = -12; y < 18; y++)
            for (int x = -12; x < 18; x++)
            {
                int wa = 0, wb = 0;
                foreach (Rect r in a) wa += r.Contribution(x, y);
                foreach (Rect r in b) wb += r.Contribution(x, y);
                bool fa = rule == PathFillRule.NonZero ? wa != 0 : (wa & 1) != 0;
                bool fb = rule == PathFillRule.NonZero ? wb != 0 : (wb & 1) != 0;
                if (fa) areaA++;
                if (fb) areaB++;
                if (fa && fb) intersection++;
            }
        return (areaA, areaB, intersection);
    }

    private struct FixedRandom(uint initial)
    {
        private uint state = initial;
        internal int Next(int maximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)maximum);
        }
    }

    private static Point2[] Rectangle(double x0, double y0, double x1, double y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
    private static Point2[] Reverse(Point2[] ring) => ring.Reverse().ToArray();
}
