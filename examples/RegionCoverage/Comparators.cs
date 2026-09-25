using Clipper2Lib;
using NetTopologySuite;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using PolylineKit;

namespace RegionCoverage;

internal readonly record struct RegionBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    // Strict separation only: shared edges/vertices still reach the geometry engine.
    internal bool Disjoint(RegionBounds other) => MaxX < other.MinX || other.MaxX < MinX ||
        MaxY < other.MinY || other.MaxY < MinY;
    internal bool Covers(RegionBounds other) => MinX <= other.MinX && MinY <= other.MinY &&
        MaxX >= other.MaxX && MaxY >= other.MaxY;
}

internal class PreparedRegion(Point2[] points, RegionBounds bounds, double area)
{
    internal Point2[] Points { get; } = points;
    internal RegionBounds Bounds { get; } = bounds;
    internal double Area { get; } = area;
}

internal readonly record struct CoverageValue(double Intersection, double? Coverage);

/// <summary>Example-only adapters for already validated simple rings without holes.</summary>
/// <remarks>
/// Input arrays are owned by the caller and must remain unchanged after preparation. Preparation caches
/// own areas, envelopes and engine-specific inputs, never pair results. Instances are not thread-safe.
/// Coverage means the fraction of the first (zone) area covered by the second (query) region.
/// </remarks>
internal abstract class CoverageComparator
{
    internal abstract string Name { get; }
    internal abstract PreparedRegion Prepare(Point2[] points, bool zone);

    internal CoverageValue Compare(PreparedRegion zone, PreparedRegion query)
    {
        double intersection = zone.Area == 0 || query.Area == 0 || zone.Bounds.Disjoint(query.Bounds)
            ? 0 : Intersection(zone, query);
        return new(intersection, zone.Area > 0 ? intersection / zone.Area : null);
    }

    protected abstract double Intersection(PreparedRegion zone, PreparedRegion query);
}

internal static class Comparators
{
    // A new call creates independent mutable workspaces, including cold NTS prepared indexes.
    internal static CoverageComparator[] Create(double scale) =>
    [
        new WindingComparator(), new ClipperComparator(scale), new NtsComparator(false, false),
        new NtsComparator(true, false), new NtsComparator(true, true), new ConvexComparator()
    ];

    internal static CoverageComparator? Create(string name, double scale) => name switch
    {
        "Winding" => new WindingComparator(),
        "Clipper64-reused-data" => new ClipperComparator(scale),
        "NTS-legacy" => new NtsComparator(false, false),
        "NTS-OverlayNG" => new NtsComparator(true, false),
        "NTS-prepared-OverlayNG" => new NtsComparator(true, true),
        "convex-then-winding" => new ConvexComparator(),
        _ => null
    };

    internal static bool IsConvex(PreparedRegion region) => region is ConvexRegion { IsConvex: true };

    private sealed class WindingComparator : CoverageComparator
    {
        internal override string Name => "Winding";
        internal override PreparedRegion Prepare(Point2[] points, bool zone)
        {
            var bounds = RegionPreparation.Bounds(points);
            // The workload guarantees simple rings; the same translated shoelace denominator is used
            // by the convex adapter. Winding's full own-area computation remains inside FilledRegions.
            return new(points, bounds, Math.Abs(RegionPreparation.SignedArea(points)));
        }
        protected override double Intersection(PreparedRegion zone, PreparedRegion query) =>
            WindingArea.FilledRegions(zone.Points, query.Points, PathFillRule.NonZero).IntersectionArea;
    }

    private sealed class ClipperRegion(Point2[] points, RegionBounds bounds, double area,
        ReuseableDataContainer64 data, bool zone) : PreparedRegion(points, bounds, area)
    {
        internal ReuseableDataContainer64 Data { get; } = data;
        internal bool Zone { get; } = zone;
    }

    private sealed class ClipperComparator : CoverageComparator
    {
        private readonly double scale;
        private readonly Clipper64 engine = new();
        private readonly Paths64 closed = new(), open = new();

        internal ClipperComparator(double scale)
        {
            if (!double.IsFinite(scale) || scale <= 0 || !double.IsFinite(scale * scale))
                throw new ArgumentOutOfRangeException(nameof(scale));
            this.scale = scale;
        }
        internal override string Name => "Clipper64-reused-data";
        internal override PreparedRegion Prepare(Point2[] points, bool zone)
        {
            _ = RegionPreparation.Bounds(points);
            var integer = new Path64(points.Length);
            foreach (Point2 p in points)
            {
                double x = p.X * scale, y = p.Y * scale;
                // Keep scaled inputs inside 2^52, where every integer is represented by binary64,
                // and well inside Clipper's documented coordinate range. No silent integer overflow.
                if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 4_503_599_627_370_496 ||
                    Math.Abs(y) > 4_503_599_627_370_496) throw new ArgumentOutOfRangeException(nameof(points));
                integer.Add(new Point64(checked((long)Math.Round(x, MidpointRounding.AwayFromZero)),
                    checked((long)Math.Round(y, MidpointRounding.AwayFromZero))));
            }
            var data = new ReuseableDataContainer64();
            data.AddPaths(new Paths64 { integer }, zone ? PathType.Clip : PathType.Subject, false);
            // Both the broad phase and denominator must describe this adapter's quantized input.
            long minX = integer[0].X, minY = integer[0].Y, maxX = minX, maxY = minY;
            foreach (Point64 p in integer)
            { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
            double area = Math.Abs(Clipper.Area(integer)) / scale / scale;
            return new ClipperRegion(points, new(minX / scale, minY / scale, maxX / scale, maxY / scale), area, data, zone);
        }
        protected override double Intersection(PreparedRegion zone, PreparedRegion query)
        {
            var z = (ClipperRegion)zone; var q = (ClipperRegion)query;
            if (!z.Zone || q.Zone) throw new ArgumentException("Prepare zone and query with their respective roles.");
            engine.Clear();
            // Vertices/local extrema are prepared once; copying their references and sorting minima
            // still happens for every pair. The pinned API has no selective query-only Clear.
            engine.AddReuseableData(z.Data); engine.AddReuseableData(q.Data);
            if (!engine.Execute(ClipType.Intersection, FillRule.NonZero, closed, open))
                throw new InvalidOperationException("Clipper intersection failed.");
            return Math.Abs(Clipper.Area(closed)) / scale / scale;
        }
    }

    private sealed class NtsRegion(Point2[] points, RegionBounds bounds, Polygon polygon,
        IPreparedGeometry? prepared) : PreparedRegion(points, bounds, polygon.Area)
    {
        internal Polygon Polygon { get; } = polygon;
        internal IPreparedGeometry? Prepared { get; } = prepared;
    }

    private sealed class NtsComparator(bool ng, bool prepared) : CoverageComparator
    {
        // An explicit Legacy factory prevents changes to the process-wide NTS service singleton
        // from silently changing the ordinary public-API baseline.
        private readonly GeometryFactory factory = new NtsGeometryServices(GeometryOverlay.Legacy).CreateGeometryFactory();
        internal override string Name => prepared ? "NTS-prepared-OverlayNG" : ng ? "NTS-OverlayNG" : "NTS-legacy";
        internal override PreparedRegion Prepare(Point2[] points, bool zone)
        {
            var bounds = RegionPreparation.Bounds(points);
            Polygon polygon = RegionPreparation.Polygon(factory, points);
            // PreparedPolygon creates expensive indexes lazily. They are intentionally not warmed
            // here: the first use belongs to the first-query/cold workload, never a hidden setup loop.
            return new NtsRegion(points, bounds, polygon, prepared && zone ? PreparedGeometryFactory.Prepare(polygon) : null);
        }
        protected override double Intersection(PreparedRegion zone, PreparedRegion query)
        {
            var z = (NtsRegion)zone; var q = (NtsRegion)query;
            if (prepared)
            {
                IPreparedGeometry p = z.Prepared ?? throw new ArgumentException("The first region must be prepared as a zone.");
                if (p.Covers(q.Polygon)) return q.Area;
                if (q.Bounds.Covers(z.Bounds) && q.Polygon.Covers(z.Polygon)) return z.Area;
                if (!p.Intersects(q.Polygon)) return 0;
            }
            return ng ? OverlayNGRobust.Overlay(z.Polygon, q.Polygon, SpatialFunction.Intersection).Area
                : z.Polygon.Intersection(q.Polygon).Area;
        }
    }

    private sealed class ConvexRegion(Point2[] points, RegionBounds bounds, double area, bool convex)
        : PreparedRegion(points, bounds, area)
    {
        internal bool IsConvex { get; } = convex;
    }

    private sealed class ConvexComparator : CoverageComparator
    {
        private List<Point2> first = new(), second = new();
        internal override string Name => "convex-then-winding";
        internal override PreparedRegion Prepare(Point2[] points, bool zone)
        {
            var bounds = RegionPreparation.Bounds(points);
            Point2[] cleaned = RegionPreparation.OpenDistinctConsecutive(points);
            double signed = RegionPreparation.SignedArea(cleaned);
            bool convex = RegionPreparation.IsConvexSimple(cleaned);
            // Half planes use a counterclockwise clip ring. Copying/reversal is preparation work.
            Point2[] oriented = cleaned;
            if (signed < 0) { oriented = (Point2[])cleaned.Clone(); Array.Reverse(oriented); }
            return new ConvexRegion(oriented, bounds, Math.Abs(signed), convex);
        }
        protected override double Intersection(PreparedRegion zone, PreparedRegion query)
        {
            var z = (ConvexRegion)zone; var q = (ConvexRegion)query;
            if (!z.IsConvex && !q.IsConvex)
                return WindingArea.FilledRegions(z.Points, q.Points, PathFillRule.NonZero).IntersectionArea;
            // Either input may clip. If both are convex, use the shorter ring as the clip.
            Point2[] clip = z.IsConvex && (!q.IsConvex || z.Points.Length <= q.Points.Length) ? z.Points : q.Points;
            Point2[] subject = ReferenceEquals(clip, z.Points) ? q.Points : z.Points;
            first.Clear(); first.AddRange(subject);
            for (int c = 0; c < clip.Length && first.Count != 0; c++)
            {
                Point2 a = clip[c], b = clip[(c + 1) % clip.Length];
                if (RegionPreparation.Same(a, b)) continue;
                second.Clear();
                Point2 s = first[^1]; int sideS = RegionPreparation.Orientation(a, b, s);
                foreach (Point2 e in first)
                {
                    int sideE = RegionPreparation.Orientation(a, b, e);
                    if (sideS < 0 && sideE >= 0 || sideS >= 0 && sideE < 0)
                        second.Add(sideS == 0 ? s : sideE == 0 ? e : Crossing(a, b, s, e));
                    if (sideE >= 0) second.Add(e);
                    s = e; sideS = sideE;
                }
                (first, second) = (second, first);
            }
            // A concave subject may yield disconnected pieces joined by cancelling clip-boundary
            // segments. This signed walk is valid for area, not necessarily as one simple polygon.
            return Math.Abs(RegionPreparation.SignedArea(first));
        }
        private static Point2 Crossing(Point2 a, Point2 b, Point2 s, Point2 e)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double ds = dx * (s.Y - a.Y) - dy * (s.X - a.X);
            double de = dx * (e.Y - a.Y) - dy * (e.X - a.X);
            double t = ds / (ds - de);
            if (double.IsFinite(t) && t > 0 && t < 1 && ds != 0 && de != 0 && Math.Sign(ds) != Math.Sign(de))
                return new(s.X + t * (e.X - s.X), s.Y + t * (e.Y - s.Y));
            // Rare uncertain value path: use NTS double-double line intersection. Ordinary cases
            // keep the allocation-free scalar interpolation used by the specialized baseline.
            Coordinate p = CGAlgorithmsDD.Intersection(new(a.X, a.Y), new(b.X, b.Y), new(s.X, s.Y), new(e.X, e.Y));
            if (p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y))
                throw new InvalidOperationException("Convex clip line intersection is not finite.");
            return new(p.X, p.Y);
        }
    }
}

internal static class RegionPreparation
{
    internal static RegionBounds Bounds(Point2[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 3) throw new ArgumentException("A ring needs at least three points.", nameof(points));
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        foreach (Point2 p in points)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1e100 || Math.Abs(p.Y) > 1e100)
                throw new ArgumentOutOfRangeException(nameof(points));
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return new(minX, minY, maxX, maxY);
    }

    internal static Polygon Polygon(GeometryFactory factory, Point2[] points)
    {
        bool closed = Same(points[0], points[^1]);
        var coordinates = new Coordinate[points.Length + (closed ? 0 : 1)];
        for (int i = 0; i < points.Length; i++) coordinates[i] = new(points[i].X, points[i].Y);
        if (!closed) coordinates[^1] = new(points[0].X, points[0].Y);
        return factory.CreatePolygon(coordinates);
    }

    internal static double SignedArea(IReadOnlyList<Point2> points)
    {
        if (points.Count < 3) return 0;
        Point2 origin = points[0]; double sum = 0, correction = 0;
        for (int i = 1; i + 1 < points.Count; i++)
        {
            Point2 a = points[i], b = points[i + 1];
            double term = (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
            double next = sum + term;
            correction += Math.Abs(sum) >= Math.Abs(term) ? (sum - next) + term : (term - next) + sum;
            sum = next;
        }
        return (sum + correction) / 2;
    }

    internal static int Orientation(Point2 a, Point2 b, Point2 c) =>
        CGAlgorithmsDD.OrientationIndex(a.X, a.Y, b.X, b.Y, c.X, c.Y);

    internal static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;

    internal static Point2[] OpenDistinctConsecutive(Point2[] points)
    {
        int count = 1;
        for (int i = 1; i < points.Length; i++) if (!Same(points[i - 1], points[i])) count++;
        bool closed = count > 1 && Same(points[0], points[^1]);
        if (closed) count--;
        if (count == points.Length) return points;
        var result = new Point2[count]; int j = 1;
        result[0] = points[0];
        for (int i = 1; i < points.Length && j < count; i++)
            if (!Same(points[i - 1], points[i])) result[j++] = points[i];
        return result;
    }

    // A same-sign turn test certifies convexity only under the workload's separately validated
    // simplicity precondition. It is not a replacement for checking arbitrary ring validity.
    internal static bool IsConvexSimple(Point2[] points)
    {
        int n = points.Length;
        if (n > 1 && Same(points[0], points[^1])) n--;
        int direction = 0;
        for (int i = 0; i < n; i++)
        {
            int turn = Orientation(points[i], points[(i + 1) % n], points[(i + 2) % n]);
            if (turn == 0) continue;
            if (direction != 0 && turn != direction) return false;
            direction = turn;
        }
        return direction != 0;
    }
}
