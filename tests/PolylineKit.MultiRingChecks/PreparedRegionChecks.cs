using System.Reflection;

namespace PolylineKit.MultiRingChecks;

/// <summary>Ownership and public-contract checks for prepared aggregate-winding regions.</summary>
internal static class PreparedRegionChecks
{
    internal static object Run()
    {
        var checks = new Checks();
        checks.SnapshotOwnership();
        checks.EmptyAndDegenerate();
        checks.FillSemantics();
        checks.Validation();
        checks.RepeatedGeneratedPairs();
        checks.ConcurrentReads();
        return new
        {
            status = "passed",
            assertions = checks.Assertions,
            generatedPairs = Checks.PairCount,
            generatorSeed = "0x4628e19b",
            concurrentQueries = Checks.ConcurrentQueries,
            scope = "Immutable input ownership, validation, fill semantics and repeated/concurrent queries; no Polygon/MultiPolygon validity claim.",
            parityReference = "Internal raw-ring route, supplemented by analytic areas; not an independent numerical oracle.",
            tolerance = "2e-12 relative to max(1, expected)."
        };
    }

    private sealed class Checks
    {
        internal const int PairCount = 48;
        internal const int ConcurrentQueries = 64;
        private int assertions;
        internal int Assertions => assertions;
        private static readonly PathFillRule[] Rules = [PathFillRule.NonZero, PathFillRule.EvenOdd];

        internal void SnapshotOwnership()
        {
            Point2[] shell = RepeatAndClose(Rectangle(0, 0, 10, 10));
            Point2[] hole = Reverse(Rectangle(2, 2, 8, 8));
            Point2[] component = Rectangle(20, 1, 22, 3);
            var source = new List<IReadOnlyList<Point2>> { shell, hole, component };
            PreparedRegion prepared = PreparedRegion.FromRings(source);
            True("Cleaned ring count", prepared.RingCount == 3);
            True("Cleaned vertex count excludes consecutive and closing duplicates", prepared.VertexCount == 12);
            Bounds("Snapshot bounds", prepared.Bounds, 0, 0, 22, 10);

            // Both levels belong to the caller and can change after successful preparation.
            Array.Fill(shell, new Point2(double.NaN, double.PositiveInfinity));
            hole[0] = new Point2(-1e20, -1e20);
            Array.Clear(component);
            source[1] = Array.Empty<Point2>();
            source.Clear();
            source.Add(null!);
            True("Ring count survives source mutation", prepared.RingCount == 3);
            True("Vertex count survives source mutation", prepared.VertexCount == 12);
            Bounds("Bounds survive source mutation", prepared.Bounds, 0, 0, 22, 10);
            foreach (PathFillRule rule in Rules)
            {
                Near("Filled area survives source mutation", 68, RegionArea.FilledArea(prepared, rule));
                RegionOverlapResult result = RegionArea.Compare(prepared, prepared, rule);
                Areas("Self comparison after source mutation", result, 68, 68, 68, 68, 0, rule);
                Near("Self intersection after source mutation", 68, RegionArea.IntersectionArea(prepared, prepared, rule));
            }

            // Reference-typed public storage would let callers bypass the snapshot's ownership.
            True("No public array fields", !typeof(PreparedRegion).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(field => field.FieldType.IsArray));
            True("No public array properties", !typeof(PreparedRegion).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(property => property.PropertyType.IsArray));
            True("No public array-returning methods", !typeof(PreparedRegion).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(method => method.ReturnType.IsArray));
        }

        internal void EmptyAndDegenerate()
        {
            PreparedRegion empty = PreparedRegion.FromRings(Array.Empty<IReadOnlyList<Point2>>());
            PreparedRegion collinear = Prepare((Point2[])[new(1, 3), new(2, 3), new(4, 3)]);
            PreparedRegion square = Prepare(Rectangle(0, 0, 2, 2));
            True("Empty region metadata", empty.RingCount == 0 && empty.VertexCount == 0 && empty.Bounds is null);
            True("Collinear region is retained", collinear.RingCount == 1 && collinear.VertexCount == 3);
            Bounds("Zero-height bounds", collinear.Bounds, 1, 3, 4, 3);
            foreach (PathFillRule rule in Rules)
            {
                Near("Empty filled area", 0, RegionArea.FilledArea(empty, rule));
                Near("Collinear filled area", 0, RegionArea.FilledArea(collinear, rule));
                Near("Empty intersection", 0, RegionArea.IntersectionArea(empty, square, rule));
                Near("Collinear intersection", 0, RegionArea.IntersectionArea(square, collinear, rule));
                Areas("Empty first operand", RegionArea.Compare(empty, square, rule), 0, 4, 0, 4, 4, rule);
                Areas("Empty second operand", RegionArea.Compare(square, empty, rule), 4, 0, 0, 4, 4, rule);
                RegionOverlapResult zero = RegionArea.Compare(empty, collinear, rule);
                Areas("Both operands have zero area", zero, 0, 0, 0, 0, 0, rule);
                True("Zero union has no IoU or Jaccard ratio", zero.IntersectionOverUnion is null && zero.JaccardDistance is null);
            }
        }

        internal void FillSemantics()
        {
            Point2[] shell = Rectangle(0, 0, 10, 10), hole = Rectangle(2, 2, 8, 8);
            PreparedRegion nested = Prepare(shell, hole);
            PreparedRegion holeOnly = Prepare(hole);
            PreparedRegion withHoleAndComponents = Prepare(shell, Reverse(hole), Rectangle(4, 4, 6, 6), Rectangle(20, 0, 22, 2));
            foreach (PathFillRule rule in Rules)
            {
                Near("Hole, island and disconnected component", 72, RegionArea.FilledArea(withHoleAndComponents, rule));
                Areas("Probe inside hole and island", RegionArea.Compare(withHoleAndComponents, holeOnly, rule), 72, 36, 4, 104, 100, rule);
                Near("Selected probe intersection", 4, RegionArea.IntersectionArea(withHoleAndComponents, holeOnly, rule));
                double expected = rule == PathFillRule.NonZero ? 100 : 64;
                Near("Same-orientation nesting is fill-dependent", expected, RegionArea.FilledArea(nested, rule));
                PreparedRegion repeated = Prepare(shell, shell);
                Near("Repeated ring is fill-dependent", rule == PathFillRule.NonZero ? 100 : 0, RegionArea.FilledArea(repeated, rule));
                PreparedRegion cancelled = Prepare(shell, Reverse(shell));
                Near("Opposite rings cancel", 0, RegionArea.FilledArea(cancelled, rule));
                PreparedRegion crossing = Prepare((Point2[])[new(0, 0), new(2, 2), new(0, 2), new(2, 0)]);
                Near("Self intersections remain supported", 2, RegionArea.FilledArea(crossing, rule));
            }
            Near("Default fill rule is NonZero", 100, RegionArea.FilledArea(nested));
            Near("Default intersection rule is NonZero", 36, RegionArea.IntersectionArea(nested, holeOnly));
            True("Default comparison reports NonZero", RegionArea.Compare(nested, holeOnly).FillRule == PathFillRule.NonZero);
        }

        internal void Validation()
        {
            Throws<ArgumentNullException>("Null ring collection", () => PreparedRegion.FromRings(null!));
            Throws<ArgumentException>("Null ring", () => PreparedRegion.FromRings(new IReadOnlyList<Point2>[] { null! }));
            Throws<ArgumentException>("Empty supplied ring", () => Prepare(Array.Empty<Point2>()));
            Throws<ArgumentException>("Point", () => Prepare((Point2[])[new(0, 0)]));
            Throws<ArgumentException>("Segment", () => Prepare((Point2[])[new(0, 0), new(1, 1)]));
            Throws<ArgumentException>("Closed segment", () => Prepare((Point2[])[new(0, 0), new(1, 1), new(0, 0)]));
            Throws<ArgumentException>("Collapsed duplicates", () => Prepare((Point2[])[new(0, 0), new(0, 0), new(0, 0)]));
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.01e100, -1.01e100 })
            {
                Throws<ArgumentException>("Invalid x coordinate", () => Prepare((Point2[])[new(0, 0), new(1, 0), new(value, 1)]));
                Throws<ArgumentException>("Invalid y coordinate", () => Prepare((Point2[])[new(0, 0), new(1, 0), new(1, value)]));
            }
            Throws<ArgumentException>("Later invalid ring is validated", () => Prepare(Rectangle(0, 0, 2, 2), [new(100, 100)]));

            PreparedRegion empty = PreparedRegion.FromRings(Array.Empty<IReadOnlyList<Point2>>());
            PreparedRegion square = Prepare(Rectangle(0, 0, 2, 2));
            foreach (PreparedRegion other in new[] { empty, square })
                foreach (PathFillRule rule in Rules)
                {
                    Throws<ArgumentNullException>("Null first comparison operand", () => RegionArea.Compare(null!, other, rule));
                    Throws<ArgumentNullException>("Null second comparison operand", () => RegionArea.Compare(other, null!, rule));
                    Throws<ArgumentNullException>("Null first intersection operand", () => RegionArea.IntersectionArea(null!, other, rule));
                    Throws<ArgumentNullException>("Null second intersection operand", () => RegionArea.IntersectionArea(other, null!, rule));
                    Throws<ArgumentNullException>("Null area operand", () => RegionArea.FilledArea(null!, rule));
                }
            foreach (PathFillRule invalid in new[] { (PathFillRule)(-1), (PathFillRule)123 })
            {
                Throws<ArgumentOutOfRangeException>("Area fill rule precedes null", () => RegionArea.FilledArea(null!, invalid));
                Throws<ArgumentOutOfRangeException>("Comparison fill rule precedes null", () => RegionArea.Compare(null!, null!, invalid));
                Throws<ArgumentOutOfRangeException>("Intersection fill rule precedes null", () => RegionArea.IntersectionArea(null!, null!, invalid));
                Throws<ArgumentOutOfRangeException>("Area fill rule checked for empty region", () => RegionArea.FilledArea(empty, invalid));
                Throws<ArgumentOutOfRangeException>("Comparison fill rule checked for empty regions", () => RegionArea.Compare(empty, empty, invalid));
                Throws<ArgumentOutOfRangeException>("Intersection fill rule checked for empty regions", () => RegionArea.IntersectionArea(empty, empty, invalid));
            }
        }

        internal void RepeatedGeneratedPairs()
        {
            var random = new FixedRandom(0x4628e19b);
            for (int pair = 0; pair < PairCount; pair++)
            {
                IReadOnlyList<IReadOnlyList<Point2>> first = Draw(ref random), second = Draw(ref random);
                PreparedRegion a = PreparedRegion.FromRings(first), b = PreparedRegion.FromRings(second);
                foreach (PathFillRule rule in Rules)
                {
                    WindingOverlapResult reference = MultiRingArea.Compare(first, second, rule);
                    // Reuse the same two immutable snapshots for both fills and multiple operations.
                    for (int repeat = 0; repeat < 2; repeat++)
                    {
                        string name = $"Generated pair {pair}/{rule}/repeat {repeat}";
                        Areas(name, RegionArea.Compare(a, b, rule), reference.FirstArea, reference.SecondArea,
                            reference.IntersectionArea, reference.UnionArea, reference.SymmetricDifferenceArea, rule);
                        Near(name + " selected first", MultiRingArea.FilledArea(first, rule), RegionArea.FilledArea(a, rule));
                        Near(name + " selected second", MultiRingArea.FilledArea(second, rule), RegionArea.FilledArea(b, rule));
                        Near(name + " selected intersection", MultiRingArea.Intersection(first, second, rule), RegionArea.IntersectionArea(a, b, rule));
                        Areas(name + " swapped", RegionArea.Compare(b, a, rule), reference.SecondArea, reference.FirstArea,
                            reference.IntersectionArea, reference.UnionArea, reference.SymmetricDifferenceArea, rule);
                    }
                }
            }
        }

        internal void ConcurrentReads()
        {
            Point2[] original = Rectangle(0, 0, 10, 10);
            var source = new List<IReadOnlyList<Point2>> { original, Reverse(Rectangle(2, 2, 8, 8)), Rectangle(20, 0, 22, 2) };
            PreparedRegion shared = PreparedRegion.FromRings(source);
            PreparedRegion probe = Prepare(Rectangle(0, 0, 5, 10));
            // These mutations are complete before any query. The prepared instances alone are shared.
            source.Clear();
            Array.Fill(original, new Point2(double.NaN, double.NaN));
            Parallel.For(0, ConcurrentQueries, i =>
            {
                PathFillRule rule = Rules[i & 1];
                Near("Concurrent own area", 68, RegionArea.FilledArea(shared, rule));
                Near("Concurrent intersection", 32, RegionArea.IntersectionArea(shared, probe, rule));
                Areas("Concurrent comparison", RegionArea.Compare(shared, probe, rule), 68, 50, 32, 86, 54, rule);
            });
            Bounds("Concurrent calls preserve metadata", shared.Bounds, 0, 0, 22, 10);
        }

        private void Areas(string name, RegionOverlapResult result, double a, double b, double intersection, double union, double xor, PathFillRule rule)
        {
            Near(name + " first", a, result.FirstArea);
            Near(name + " second", b, result.SecondArea);
            Near(name + " intersection", intersection, result.IntersectionArea);
            Near(name + " union", union, result.UnionArea);
            Near(name + " xor", xor, result.SymmetricDifferenceArea);
            True(name + " fill rule", result.FillRule == rule);
            if (union > 0)
            {
                True(name + " has IoU", result.IntersectionOverUnion.HasValue);
                True(name + " has Jaccard distance", result.JaccardDistance.HasValue);
                Near(name + " IoU", intersection / union, result.IntersectionOverUnion!.Value);
                Near(name + " Jaccard distance", xor / union, result.JaccardDistance!.Value);
            }
        }

        private void Bounds(string name, Bounds2D? value, double minX, double minY, double maxX, double maxY)
        {
            True(name + " exists", value.HasValue);
            True(name + " coordinate extrema", value!.Value.MinX == minX && value.Value.MinY == minY && value.Value.MaxX == maxX && value.Value.MaxY == maxY);
        }

        private void Near(string name, double expected, double actual) =>
            True($"{name}: expected {expected:R}, actual {actual:R}", double.IsFinite(actual) && Math.Abs(actual - expected) <= 2e-12 * Math.Max(1, Math.Abs(expected)));

        private void True(string name, bool value)
        {
            if (!value) throw new InvalidOperationException(name);
            Interlocked.Increment(ref assertions);
        }

        private void Throws<T>(string name, Action action) where T : Exception
        {
            try { action(); }
            catch (Exception exception) when (exception.GetType() == typeof(T))
            {
                Interlocked.Increment(ref assertions);
                return;
            }
            throw new InvalidOperationException(name + ": expected exactly " + typeof(T).Name);
        }
    }

    private static IReadOnlyList<IReadOnlyList<Point2>> Draw(ref FixedRandom random)
    {
        var rings = new IReadOnlyList<Point2>[random.Next(5)];
        for (int i = 0; i < rings.Length; i++)
        {
            int x = random.Next(13) - 6, y = random.Next(13) - 6;
            Point2[] ring = Rectangle(x, y, x + 1 + random.Next(6), y + 1 + random.Next(6));
            if (i > 0 && random.Next(4) == 0) ring = rings[random.Next(i)].ToArray();
            if (random.Next(2) == 0) Array.Reverse(ring);
            rings[i] = ring;
        }
        return rings;
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

    private static PreparedRegion Prepare(params Point2[][] rings) => PreparedRegion.FromRings(rings);
    private static Point2[] Rectangle(double x0, double y0, double x1, double y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
    private static Point2[] Reverse(Point2[] ring) => ring.Reverse().ToArray();
    private static Point2[] RepeatAndClose(Point2[] ring) => ring.SelectMany(point => new[] { point, point }).Concat(new[] { ring[0], ring[0] }).ToArray();
}
