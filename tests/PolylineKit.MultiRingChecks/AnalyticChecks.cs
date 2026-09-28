using System.Collections;

namespace PolylineKit.MultiRingChecks;

/// <summary>Independent small-coordinate area oracles for the internal multi-ring feasibility check.</summary>
internal static class AnalyticChecks
{
    internal static object Run()
    {
        var checks = new Checks();
        checks.Analytic();
        checks.RectangleFuzz();
        checks.SingleRingCompatibility();
        checks.Validation();
        checks.Reentrancy();
        return new
        {
            status = "passed",
            assertions = checks.Assertions,
            areaCases = checks.AreaCases,
            rectangleCollections = Checks.RectangleCaseCount,
            rectangleSeed = "0x614e5d09",
            oracle = "Exact integer cell subdivision: sum signed rectangle containment per operand, then apply fill independently.",
            tolerance = "2e-12 relative to max(1, expected); geometry and expected rectangle areas use small exact integers."
        };
    }

    private sealed class Checks
    {
        internal const int RectangleCaseCount = 160;
        internal int Assertions, AreaCases;
        private static readonly IReadOnlyList<IReadOnlyList<Point2>> Empty = Array.Empty<IReadOnlyList<Point2>>();
        private static readonly PathFillRule[] Rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];

        internal void Analytic()
        {
            Point2[] outer = Rectangle(0, 0, 10, 10), inner = Rectangle(2, 2, 8, 8);
            Point2[] island = Rectangle(4, 4, 6, 6), far = Rectangle(12, 0, 14, 2);
            Point2[] unit = Rectangle(0, 0, 1, 1);
            foreach (PathFillRule rule in Rules)
            {
                Case("both empty", Empty, Empty, rule, new(0, 0, 0), true);
                Case("first empty", Empty, Rings(outer), rule, new(0, 100, 0), true);
                Case("second empty", Rings(outer), Empty, rule, new(100, 0, 0), true);
                Case("disjoint opposite orientation", Rings(outer, Reverse(far)), Rings(unit), rule, new(104, 1, 1), true);
                double nested = rule == PathFillRule.NonZero ? 100 : 64;
                Case("nested same direction", Rings(outer, inner), Rings(inner), rule, new(nested, 36, rule == PathFillRule.NonZero ? 36 : 0), true);
                Case("hole", Rings(outer, Reverse(inner)), Rings(inner), rule, new(64, 36, 0), true);
                Case("island in hole", Rings(outer, Reverse(inner), island), Rings(inner), rule, new(68, 36, 4), true);
                double repeated = rule == PathFillRule.NonZero ? 100 : 0;
                Case("identical rings", Rings(outer, outer), Rings(outer), rule, new(repeated, 100, repeated), true);
                Case("opposite identical rings", Rings(outer, Reverse(outer)), Rings(outer), rule, new(0, 100, 0), true);

                Point2[] left = Rectangle(0, 0, 4, 4), right = Rectangle(2, 0, 6, 4);
                double overlapped = rule == PathFillRule.NonZero ? 24 : 16;
                Case("overlapping same operand", Rings(left, right), Rings(Rectangle(2, 0, 4, 4)), rule,
                    new(overlapped, 8, rule == PathFillRule.NonZero ? 8 : 0), true);
                Case("overlapping opposite winding", Rings(left, Reverse(right)), Rings(Rectangle(2, 0, 4, 4)), rule,
                    new(16, 8, 0), true);
                Case("shared edge within operand", Rings(unit, Rectangle(1, 0, 2, 1)), Rings(Rectangle(0, 0, 2, 1)), rule, new(2, 2, 2), true);
                Case("point contact within operand", Rings(unit, Rectangle(1, 1, 2, 2)), Rings(Rectangle(0, 0, 2, 2)), rule, new(2, 4, 2), true);
                Case("shared edge across operands", Rings(unit), Rings(Rectangle(1, 0, 2, 1)), rule, new(1, 1, 0), true);
                Case("point contact across operands", Rings(unit), Rings(Rectangle(1, 1, 2, 2)), rule, new(1, 1, 0), true);

                Point2[] bowTie = [new(0, 0), new(2, 2), new(0, 2), new(2, 0)];
                Case("self crossing plus separate island", Rings(bowTie, Rectangle(3, 0, 4, 1)), Rings(Rectangle(0, 0, 1, 2)), rule, new(3, 2, 1), true);
                Point2[] diamond = [new(0, 2), new(2, 0), new(4, 2), new(2, 4)];
                Case("proper crossings with hole", Rings(Rectangle(0, 0, 4, 4), Reverse(Rectangle(1, 1, 3, 3))), Rings(diamond), rule, new(12, 8, 4), true);
                Point2[] zero = [new(0, 0), new(1, 0), new(2, 0)];
                Case("collinear zero ring", Rings(zero), Rings(unit), rule, new(0, 1, 0), true);
                Case("zero ring alongside valid ring", Rings(zero, unit), Rings(unit), rule, new(1, 1, 1), true);
                Case("duplicate and explicit closure cleanup", Rings(RepeatAndClose(outer), RepeatAndClose(Reverse(inner))), Rings(RepeatAndClose(island)), rule,
                    new(64, 4, 0), true);

                // Every coordinate, including the unit side at 2^52, is exactly representable.
                // A distant component must not erase a small disconnected region in a shared chain.
                foreach (double distance in new[] { 1e12, 4503599627370496d })
                {
                    IReadOnlyList<IReadOnlyList<Point2>> distant = Rings(unit, Rectangle(distance, distance, distance + 1, distance + 1));
                    Case($"disconnected unit squares at {distance:R}", distant, Empty, rule, new(2, 0, 0), true);
                    Case($"identical disconnected unit squares at {distance:R}", distant, distant, rule, new(2, 2, 2), true);
                    Case($"select one disconnected unit square at {distance:R}", distant, Rings(unit), rule, new(2, 1, 1), true);
                }
                foreach ((double distance, double size) in new[] { (1e12, .001), (1e14, .1) })
                {
                    double farSide = (distance + size) - distance;
                    double expected = size * size + farSide * farSide;
                    IReadOnlyList<IReadOnlyList<Point2>> distant = Rings(Rectangle(0, 0, size, size), Rectangle(distance, distance, distance + size, distance + size));
                    // The oracle measures the supplied binary64 corners: the far square's rounded side
                    // need not equal size. These products/addition are the only oracle rounding.
                    Case($"disconnected rounded squares at {distance:R} size {size:R}", distant, Empty, rule, new(expected, 0, 0), true);
                    Case($"identical disconnected rounded squares at {distance:R} size {size:R}", distant, distant, rule, new(expected, expected, expected), true);
                }
            }

            // Reversing only one ring is deliberately not a NonZero invariant.
            Near("NonZero same-direction nested fill", 100, MultiRingArea.FilledArea(Rings(outer, inner), PathFillRule.NonZero));
            Near("NonZero reversed hole", 64, MultiRingArea.FilledArea(Rings(outer, Reverse(inner)), PathFillRule.NonZero));
        }

        internal void RectangleFuzz()
        {
            var random = new FixedRandom(0x614e5d09);
            for (int i = 0; i < RectangleCaseCount; i++)
            {
                Rect[] a = Draw(ref random), b = Draw(ref random);
                IReadOnlyList<IReadOnlyList<Point2>> first = a.Select(r => r.Points()).ToArray();
                IReadOnlyList<IReadOnlyList<Point2>> second = b.Select(r => r.Points()).ToArray();
                foreach (PathFillRule rule in Rules)
                    Case($"rectangle cells {i}", first, second, rule, CellOracle(a, b, rule), true);
            }
        }

        internal void SingleRingCompatibility()
        {
            Point2[][] paths =
            [
                Rectangle(0, 0, 4, 3),
                [new(0, 0), new(4, 3), new(0, 3), new(4, 0)],
                [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0), new(1, 1), new(1, 3), new(3, 3), new(3, 1), new(1, 1)],
                [new(0, 0), new(2, 0), new(4, 0)]
            ];
            foreach (Point2[] a in paths)
                foreach (Point2[] b in paths)
                    foreach (PathFillRule rule in Rules)
                    {
                        WindingOverlapResult reference = WindingArea.FilledRegions(a, b, rule);
                        Case("single-ring compatibility", Rings(a), Rings(b), rule,
                            new(reference.FirstArea, reference.SecondArea, reference.IntersectionArea, reference.UnionArea, reference.SymmetricDifferenceArea), false);
                        Near("single-ring selected area", WindingArea.FilledArea(a, rule), MultiRingArea.FilledArea(Rings(a), rule));
                        Near("single-ring intersection", WindingArea.IntersectionArea(a, b, rule), MultiRingArea.Intersection(Rings(a), Rings(b), rule));
                    }
        }

        internal void Validation()
        {
            IReadOnlyList<IReadOnlyList<Point2>> valid = Rings(Rectangle(0, 0, 1, 1));
            foreach (PathFillRule rule in Rules)
            {
                Invalid<ArgumentNullException>("null collection", null!, valid, rule);
                Invalid<ArgumentException>("null ring", new IReadOnlyList<Point2>[] { null! }, valid, rule);
                Invalid<ArgumentException>("empty ring", Rings(Array.Empty<Point2>()), valid, rule);
                Invalid<ArgumentException>("point", Rings((Point2[])[new(0, 0)]), valid, rule);
                Invalid<ArgumentException>("segment", Rings((Point2[])[new(0, 0), new(1, 1)]), valid, rule);
                Invalid<ArgumentException>("collapsed duplicates", Rings((Point2[])[new(0, 0), new(0, 0), new(0, 0)]), valid, rule);
                Invalid<ArgumentException>("closed segment", Rings((Point2[])[new(0, 0), new(1, 1), new(0, 0)]), valid, rule);
                foreach (double coordinate in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.01e100, -1.01e100 })
                    Invalid<ArgumentException>($"invalid coordinate {coordinate}", Rings((Point2[])[new(0, 0), new(1, 0), new(coordinate, 1)]), valid, rule);
                // Bounds rejection and empty operands must never hide malformed other operands.
                Invalid<ArgumentException>("invalid later ring", Rings(Rectangle(1e10, 1e10, 1e10 + 1, 1e10 + 1), [new(0, 0)]), valid, rule);
            }
            Throws<ArgumentOutOfRangeException>("invalid rule on empty Compare", () => MultiRingArea.Compare(Empty, Empty, (PathFillRule)123));
            Throws<ArgumentOutOfRangeException>("invalid rule on empty Intersection", () => MultiRingArea.Intersection(Empty, Empty, (PathFillRule)123));
            Throws<ArgumentOutOfRangeException>("invalid rule on empty FilledArea", () => MultiRingArea.FilledArea(Empty, (PathFillRule)123));
        }

        private void Invalid<T>(string name, IReadOnlyList<IReadOnlyList<Point2>> bad, IReadOnlyList<IReadOnlyList<Point2>> valid, PathFillRule rule) where T : Exception
        {
            Throws<T>(name + " fill", () => MultiRingArea.FilledArea(bad, rule));
            foreach (IReadOnlyList<IReadOnlyList<Point2>> other in new[] { valid, Empty })
            {
                Throws<T>(name + " compare first", () => MultiRingArea.Compare(bad, other, rule));
                Throws<T>(name + " compare second", () => MultiRingArea.Compare(other, bad, rule));
                Throws<T>(name + " intersection first", () => MultiRingArea.Intersection(bad, other, rule));
                Throws<T>(name + " intersection second", () => MultiRingArea.Intersection(other, bad, rule));
            }
        }

        internal void Reentrancy()
        {
            Point2[] square = Rectangle(0, 0, 2, 2);
            IReadOnlyList<IReadOnlyList<Point2>> plain = Rings(square);
            int nested = 0;
            Action callback = () =>
            {
                nested++;
                WindingOverlapResult result = MultiRingArea.Compare(plain, plain, PathFillRule.NonZero);
                Near("nested compare", 4, result.IntersectionArea);
                Near("nested intersection", 4, MultiRingArea.Intersection(plain, plain, PathFillRule.EvenOdd));
                Near("nested filled area", 4, MultiRingArea.FilledArea(plain, PathFillRule.NonZero));
            };
            foreach (PathFillRule rule in Rules)
            {
                IReadOnlyList<IReadOnlyList<Point2>> wrappedRing = Rings(new CallbackList<Point2>(square, callback));
                Case("reentrant vertex indexer", wrappedRing, plain, rule, new(4, 4, 4), false);
                IReadOnlyList<IReadOnlyList<Point2>> wrappedCollection = new CallbackList<IReadOnlyList<Point2>>(plain, callback);
                Case("reentrant ring indexer", plain, wrappedCollection, rule, new(4, 4, 4), false);

                Action fail = () => { callback(); throw new IndexerFailure(); };
                IReadOnlyList<IReadOnlyList<Point2>> throwing = Rings(new CallbackList<Point2>(square, fail));
                Throws<IndexerFailure>("nested then throwing Compare", () => MultiRingArea.Compare(throwing, plain, rule));
                Case("recovery after Compare indexer failure", plain, plain, rule, new(4, 4, 4), false);
                Throws<IndexerFailure>("nested then throwing Intersection", () => MultiRingArea.Intersection(plain, throwing, rule));
                Case("recovery after Intersection indexer failure", plain, plain, rule, new(4, 4, 4), false);
                Throws<IndexerFailure>("nested then throwing FilledArea", () => MultiRingArea.FilledArea(throwing, rule));
                Case("recovery after FilledArea indexer failure", plain, plain, rule, new(4, 4, 4), false);
            }
            True("nested indexers executed", nested > 0);
        }

        private void Case(string name, IReadOnlyList<IReadOnlyList<Point2>> a, IReadOnlyList<IReadOnlyList<Point2>> b,
            PathFillRule rule, Expected expected, bool variants)
        {
            CheckAreas(name, a, b, rule, expected);
            if (!variants) return;
            CheckAreas(name + " swap", b, a, rule, new(expected.B, expected.A, expected.Intersection, expected.Union, expected.Xor));
            CheckAreas(name + " ring order and start", Transform(a, false, false), Transform(b, false, false), rule, expected);
            CheckAreas(name + " reverse all rings", Transform(a, true, false), Transform(b, true, false), rule, expected);
            if (rule == PathFillRule.EvenOdd)
                CheckAreas(name + " reverse alternate rings", Transform(a, false, true), Transform(b, false, true), rule, expected);
        }

        private void CheckAreas(string name, IReadOnlyList<IReadOnlyList<Point2>> a, IReadOnlyList<IReadOnlyList<Point2>> b,
            PathFillRule rule, Expected expected)
        {
            string label = name + "/" + rule;
            WindingOverlapResult actual = MultiRingArea.Compare(a, b, rule);
            Near(label + " first", expected.A, actual.FirstArea);
            Near(label + " second", expected.B, actual.SecondArea);
            Near(label + " intersection", expected.Intersection, actual.IntersectionArea);
            Near(label + " union", expected.Union, actual.UnionArea);
            Near(label + " xor", expected.Xor, actual.SymmetricDifferenceArea);
            Near(label + " intersection-only", expected.Intersection, MultiRingArea.Intersection(a, b, rule));
            Near(label + " area-only first", expected.A, MultiRingArea.FilledArea(a, rule));
            Near(label + " area-only second", expected.B, MultiRingArea.FilledArea(b, rule));
            True(label + " fill rule", actual.FillRule == rule);
            AreaCases++;
        }

        private void Near(string name, double expected, double actual)
        {
            double tolerance = 2e-12 * Math.Max(1, Math.Abs(expected));
            True($"{name}: expected {expected:R}, actual {actual:R}", double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance);
        }

        private void True(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException(name);
            Assertions++;
        }

        private void Throws<T>(string name, Action action) where T : Exception
        {
            try { action(); }
            catch (T) { Assertions++; return; }
            throw new InvalidOperationException(name + ": expected " + typeof(T).Name);
        }

        private static Rect[] Draw(ref FixedRandom random)
        {
            var result = new Rect[random.Next(6)];
            for (int i = 0; i < result.Length; i++)
            {
                // Copying an earlier rectangle deliberately exercises exact coincidence/reversal.
                if (i > 0 && random.Next(4) == 0)
                {
                    Rect old = result[random.Next(i)];
                    result[i] = old with { Direction = random.Next(2) == 0 ? 1 : -1 };
                }
                else
                {
                    int x = random.Next(17) - 8, y = random.Next(17) - 8;
                    result[i] = new Rect(x, y, x + 1 + random.Next(7), y + 1 + random.Next(7), random.Next(2) == 0 ? 1 : -1);
                }
            }
            return result;
        }

        private static Expected CellOracle(Rect[] a, Rect[] b, PathFillRule rule)
        {
            Rect[] all = a.Concat(b).ToArray();
            int[] xs = all.SelectMany(r => new[] { r.X0, r.X1 }).Distinct().Order().ToArray();
            int[] ys = all.SelectMany(r => new[] { r.Y0, r.Y1 }).Distinct().Order().ToArray();
            long ownA = 0, ownB = 0, intersection = 0, union = 0, xor = 0;
            for (int x = 1; x < xs.Length; x++)
                for (int y = 1; y < ys.Length; y++)
                {
                    // Twice each midpoint is an integer; no geometric predicate from Core is reused.
                    int mx = xs[x - 1] + xs[x], my = ys[y - 1] + ys[y];
                    int wa = Winding(a, mx, my), wb = Winding(b, mx, my);
                    bool fa = Filled(wa, rule), fb = Filled(wb, rule);
                    long area = (long)(xs[x] - xs[x - 1]) * (ys[y] - ys[y - 1]);
                    if (fa) ownA += area;
                    if (fb) ownB += area;
                    if (fa && fb) intersection += area;
                    if (fa || fb) union += area;
                    if (fa != fb) xor += area;
                }
            return new(ownA, ownB, intersection, union, xor);
        }

        private static int Winding(Rect[] rectangles, int twiceX, int twiceY)
        {
            int winding = 0;
            foreach (Rect rectangle in rectangles)
                if (2 * rectangle.X0 < twiceX && twiceX < 2 * rectangle.X1 && 2 * rectangle.Y0 < twiceY && twiceY < 2 * rectangle.Y1)
                    winding += rectangle.Direction;
            return winding;
        }

        private static bool Filled(int winding, PathFillRule rule) => rule == PathFillRule.NonZero ? winding != 0 : (winding & 1) != 0;
    }

    private readonly record struct Expected(double A, double B, double Intersection, double Union, double Xor)
    {
        internal Expected(double a, double b, double intersection) : this(a, b, intersection, a + b - intersection, a + b - 2 * intersection) { }
    }

    private readonly record struct Rect(int X0, int Y0, int X1, int Y1, int Direction)
    {
        internal Point2[] Points()
        {
            Point2[] points = Rectangle(X0, Y0, X1, Y1);
            return Direction > 0 ? points : Reverse(points);
        }
    }

    private struct FixedRandom(uint state)
    {
        private uint state = state;
        internal int Next(int maximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)maximum);
        }
    }

    private static Point2[] Rectangle(double x0, double y0, double x1, double y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
    private static Point2[] Reverse(IReadOnlyList<Point2> points) => points.Reverse().ToArray();
    private static IReadOnlyList<IReadOnlyList<Point2>> Rings(params IReadOnlyList<Point2>[] rings) => rings;
    private static Point2[] RepeatAndClose(Point2[] ring) => ring.SelectMany(p => new[] { p, p }).Concat(new[] { ring[0], ring[0] }).ToArray();

    private static IReadOnlyList<IReadOnlyList<Point2>> Transform(IReadOnlyList<IReadOnlyList<Point2>> rings, bool reverseAll, bool reverseAlternate)
    {
        var transformed = new IReadOnlyList<Point2>[rings.Count];
        for (int r = 0; r < rings.Count; r++)
        {
            IReadOnlyList<Point2> ring = rings[r];
            var points = new Point2[ring.Count];
            for (int i = 0; i < points.Length; i++) points[i] = ring[(i + r + 1) % ring.Count];
            if (reverseAll || (reverseAlternate && (r & 1) == 0)) Array.Reverse(points);
            transformed[rings.Count - 1 - r] = points;
        }
        return transformed;
    }

    private sealed class IndexerFailure : Exception { }

    private sealed class CallbackList<T>(IReadOnlyList<T> source, Action callback) : IReadOnlyList<T>
    {
        public int Count => source.Count;
        public T this[int index]
        {
            get { callback(); return source[index]; }
        }
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
