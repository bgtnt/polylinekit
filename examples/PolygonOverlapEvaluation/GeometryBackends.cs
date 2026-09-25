using Clipper2Lib;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using PolylineKit;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PolygonOverlapEvaluation;

internal readonly record struct PolygonBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    internal bool IsEmpty => MinX > MaxX || MinY > MaxY;
    // Include edge/vertex touching. Only strict separation is an early rejection.
    internal bool Intersects(PolygonBounds other) => !IsEmpty && !other.IsEmpty &&
        MaxX >= other.MinX && other.MaxX >= MinX && MaxY >= other.MinY && other.MaxY >= MinY;
    internal bool Disjoint(PolygonBounds other) => !Intersects(other);
}

/// <summary>Shared original-coordinate geometry, own area and bounds; never caches pair results.</summary>
/// <remarks>The example owns the NTS geometry and must not mutate it after preparation.</remarks>
internal sealed class PreparedPolygon(NtsGeometry geometry, Point2[]? points, PolygonBounds bounds,
    double area, bool originalWasValid, bool isMultipart, bool hasHoles, bool convex, string? unsupportedReason)
{
    internal NtsGeometry Geometry { get; } = geometry;
    internal Point2[]? Points { get; } = points;
    internal PolygonBounds Bounds { get; } = bounds;
    internal double Area { get; } = area;
    internal bool OriginalWasValid { get; } = originalWasValid;
    internal bool IsEmpty => Geometry.IsEmpty;
    internal bool IsMultipart { get; } = isMultipart;
    internal bool HasHoles { get; } = hasHoles;
    internal bool IsConvexSimple { get; } = convex;
    internal string? UnsupportedReason { get; } = unsupportedReason;
}

internal class PreparedOperand(PreparedPolygon polygon, bool isTruth, object owner, string? fallbackReason = null)
{
    internal PreparedPolygon Polygon { get; } = polygon;
    internal bool IsTruth { get; } = isTruth;
    internal object Owner { get; } = owner;
    internal string? FallbackReason { get; } = fallbackReason;
}

internal readonly record struct IntersectionValue(double Area, bool UsedFallback = false, string? FallbackReason = null);

/// <summary>Example-only geometry adapters. Each instance owns scratch storage and is not thread-safe.</summary>
internal interface IGeometryBackend
{
    string Name { get; }
    PreparedOperand Prepare(PreparedPolygon polygon, bool isTruth);
    IntersectionValue IntersectionArea(PreparedOperand prediction, PreparedOperand truth);
}

internal static class GeometryPreparation
{
    /// <summary>Inspects geometry without repair, coordinate rounding or a change of traversal.</summary>
    /// <remarks>
    /// The evaluator must filter by original area before any explicitly selected repair policy.
    /// Invalid geometry is retained as an identified unsupported input, never passed to Core as
    /// though fill-rule semantics reproduced Solaris/GEOS buffer(0). Valid holes/multipart inputs
    /// have an explicit NTS fallback in the narrower adapters.
    /// </remarks>
    internal static PreparedPolygon Prepare(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry is not Polygon && geometry is not MultiPolygon)
            throw new NotSupportedException("Only Polygon and MultiPolygon inputs are accepted by this example.");
        foreach (Coordinate p in geometry.Coordinates)
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1e100 || Math.Abs(p.Y) > 1e100 ||
                !double.IsNaN(p.Z) && p.Z != 0)
                throw new ArgumentException("Coordinates must be finite XY of magnitude at most 1e100, with absent or zero Z.", nameof(geometry));

        bool valid = geometry.IsValid;
        bool multipart = geometry is MultiPolygon;
        bool holes = Enumerable.Range(0, geometry.NumGeometries)
            .Any(i => ((Polygon)geometry.GetGeometryN(i)).NumInteriorRings != 0);
        double area = geometry.Area;
        if (!double.IsFinite(area) || area < 0)
            throw new ArgumentException("Own area must be finite and nonnegative.", nameof(geometry));
        Envelope envelope = geometry.EnvelopeInternal;
        var bounds = geometry.IsEmpty
            ? new PolygonBounds(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity)
            : new PolygonBounds(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
        Point2[]? points = null;
        bool convex = false;
        string? unsupported = !valid ? "invalid-geometry" : geometry.IsEmpty ? "empty-geometry" :
            multipart ? "multipart" : holes ? "holes" : null;
        if (unsupported is null)
        {
            var polygon = (Polygon)geometry;
            // NTS polygon validity has already established shell simplicity independently.
            // Under that precondition, robust consistent turns certify convexity; computing
            // another hull/overlay would duplicate preparation work for every backend.
            var cleaned = new List<Point2>(polygon.ExteriorRing.NumPoints);
            foreach (Coordinate p in polygon.ExteriorRing.Coordinates)
            {
                Point2 point = new(p.X, p.Y);
                if (cleaned.Count == 0 || !Same(cleaned[^1], point)) cleaned.Add(point);
            }
            if (cleaned.Count > 1 && Same(cleaned[0], cleaned[^1])) cleaned.RemoveAt(cleaned.Count - 1);
            if (cleaned.Count < 3) unsupported = "too-few-shell-vertices";
            else
            {
                points = cleaned.ToArray();
                convex = IsConvex(points);
            }
        }
        return new(geometry, points, bounds, area, valid, multipart, holes, convex, unsupported);
    }

    internal static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;

    internal static int Orientation(Point2 a, Point2 b, Point2 c) =>
        CGAlgorithmsDD.OrientationIndex(a.X, a.Y, b.X, b.Y, c.X, c.Y);

    private static bool IsConvex(Point2[] points)
    {
        int sign = 0;
        for (int i = 0; i < points.Length; i++)
        {
            int turn = Orientation(points[i], points[(i + 1) % points.Length], points[(i + 2) % points.Length]);
            if (turn == 0) continue;
            if (sign != 0 && turn != sign) return false;
            sign = turn;
        }
        return sign != 0;
    }

    internal static double SignedArea(IReadOnlyList<Point2> points)
    {
        if (points.Count < 3) return 0;
        Point2 origin = points[0];
        double sum = 0, correction = 0;
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
}

internal static class GeometryBackends
{
    // Fixed before comparing outputs: 1e-10 pixel grid, no shift or scaling of any other backend.
    // A conservative 1e14 scaled-coordinate bound is enforced before checked Int64 conversion.
    internal const double ClipperScale = 1e10;
    internal static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "core", "clipper", "nts", "convex" });

    internal static IGeometryBackend Create(string name) => name switch
    {
        "core" => new CoreBackend(),
        "clipper" => new ClipperBackend(),
        "nts" => new NtsBackend(),
        "convex" => new ConvexBackend(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown geometry backend.")
    };

    private abstract class Backend : IGeometryBackend
    {
        public abstract string Name { get; }
        public virtual PreparedOperand Prepare(PreparedPolygon polygon, bool isTruth)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            return new(polygon, isTruth, this, polygon.UnsupportedReason);
        }

        public IntersectionValue IntersectionArea(PreparedOperand prediction, PreparedOperand truth)
        {
            ArgumentNullException.ThrowIfNull(prediction); ArgumentNullException.ThrowIfNull(truth);
            if (!ReferenceEquals(prediction.Owner, this) || !ReferenceEquals(truth.Owner, this) || prediction.IsTruth || !truth.IsTruth)
                throw new ArgumentException("Prepare prediction and truth with this backend and their respective roles.");
            if (!prediction.Polygon.OriginalWasValid || !truth.Polygon.OriginalWasValid)
                throw new NotSupportedException("Invalid geometry requires explicit evaluator-level Solaris repair/rejection handling.");
            if (prediction.Polygon.IsEmpty || truth.Polygon.IsEmpty || prediction.Polygon.Area == 0 || truth.Polygon.Area == 0)
                return new(0);
            return Measure(prediction, truth);
        }

        protected abstract IntersectionValue Measure(PreparedOperand prediction, PreparedOperand truth);
        protected static IntersectionValue Fallback(PreparedOperand prediction, PreparedOperand truth, string defaultReason) =>
            new(prediction.Polygon.Geometry.Intersection(truth.Polygon.Geometry).Area, true,
                prediction.FallbackReason ?? truth.FallbackReason ?? defaultReason);
    }

    private sealed class CoreBackend : Backend
    {
        public override string Name => "core";
        protected override IntersectionValue Measure(PreparedOperand prediction, PreparedOperand truth) =>
            prediction.FallbackReason is null && truth.FallbackReason is null
                ? new(PolylineArea.IntersectionArea(prediction.Polygon.Points!, truth.Polygon.Points!, PathFillRule.NonZero))
                : Fallback(prediction, truth, "unsupported-core-input");
    }

    private sealed class NtsBackend : Backend
    {
        public override string Name => "nts";
        protected override IntersectionValue Measure(PreparedOperand prediction, PreparedOperand truth) =>
            new(prediction.Polygon.Geometry.Intersection(truth.Polygon.Geometry).Area);
    }

    private sealed class ClipperOperand(PreparedPolygon polygon, bool isTruth, object owner, ReuseableDataContainer64 data)
        : PreparedOperand(polygon, isTruth, owner)
    {
        internal ReuseableDataContainer64 Data { get; } = data;
    }

    private sealed class ClipperBackend : Backend
    {
        private readonly Clipper64 engine = new();
        private readonly Paths64 closed = new(), open = new();
        public override string Name => "clipper";

        public override PreparedOperand Prepare(PreparedPolygon polygon, bool isTruth)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            if (polygon.UnsupportedReason is not null) return base.Prepare(polygon, isTruth);
            var integer = new Path64(polygon.Points!.Length);
            foreach (Point2 point in polygon.Points)
            {
                double x = point.X * ClipperScale, y = point.Y * ClipperScale;
                if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e14 || Math.Abs(y) > 1e14)
                    return new(polygon, isTruth, this, "clipper-coordinate-range");
                integer.Add(new Point64(checked((long)Math.Round(x, MidpointRounding.AwayFromZero)),
                    checked((long)Math.Round(y, MidpointRounding.AwayFromZero))));
            }
            var data = new ReuseableDataContainer64();
            data.AddPaths(new Paths64 { integer }, isTruth ? PathType.Clip : PathType.Subject, false);
            return new ClipperOperand(polygon, isTruth, this, data);
        }

        protected override IntersectionValue Measure(PreparedOperand prediction, PreparedOperand truth)
        {
            if (prediction is not ClipperOperand p || truth is not ClipperOperand t)
                return Fallback(prediction, truth, "unsupported-clipper-input");
            engine.Clear();
            // Reuse parsed vertices/local minima and output containers. Pair-specific edge setup,
            // sorting, intersection and output contours remain real work on every invocation.
            engine.AddReuseableData(p.Data); engine.AddReuseableData(t.Data);
            if (!engine.Execute(ClipType.Intersection, FillRule.NonZero, closed, open))
                throw new InvalidOperationException("Clipper intersection failed.");
            // Signed contours subtract holes; never sum their absolute areas independently.
            return new(Math.Abs(Clipper.Area(closed)) / ClipperScale / ClipperScale);
        }
    }

    private sealed class ConvexOperand(PreparedPolygon polygon, bool isTruth, object owner, Point2[] ccw)
        : PreparedOperand(polygon, isTruth, owner)
    {
        internal Point2[] Ccw { get; } = ccw;
    }

    private sealed class ConvexBackend : Backend
    {
        private List<Point2> first = new(), second = new();
        public override string Name => "convex";

        public override PreparedOperand Prepare(PreparedPolygon polygon, bool isTruth)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            if (!polygon.IsConvexSimple)
                return new(polygon, isTruth, this, polygon.UnsupportedReason ?? "non-convex-pair");
            Point2[] oriented = (Point2[])polygon.Points!.Clone();
            if (GeometryPreparation.SignedArea(oriented) < 0) Array.Reverse(oriented);
            // The intersection of two convex polygons has no more than n+m vertices.
            int capacity = checked(2 * oriented.Length);
            first.EnsureCapacity(capacity); second.EnsureCapacity(capacity);
            return new ConvexOperand(polygon, isTruth, this, oriented);
        }

        protected override IntersectionValue Measure(PreparedOperand prediction, PreparedOperand truth)
        {
            if (prediction is not ConvexOperand p || truth is not ConvexOperand t)
                return Fallback(prediction, truth, "non-convex-pair");
            Point2[] clip = p.Ccw.Length <= t.Ccw.Length ? p.Ccw : t.Ccw;
            Point2[] subject = ReferenceEquals(clip, p.Ccw) ? t.Ccw : p.Ccw;
            first.Clear(); first.AddRange(subject);
            for (int c = 0; c < clip.Length && first.Count != 0; c++)
            {
                Point2 a = clip[c], b = clip[(c + 1) % clip.Length];
                second.Clear();
                Point2 s = first[^1]; int sideS = GeometryPreparation.Orientation(a, b, s);
                foreach (Point2 e in first)
                {
                    int sideE = GeometryPreparation.Orientation(a, b, e);
                    if (sideS < 0 && sideE >= 0 || sideS >= 0 && sideE < 0)
                        Append(second, sideS == 0 ? s : sideE == 0 ? e : Crossing(a, b, s, e));
                    if (sideE >= 0) Append(second, e);
                    s = e; sideS = sideE;
                }
                if (second.Count > 1 && GeometryPreparation.Same(second[0], second[^1])) second.RemoveAt(second.Count - 1);
                (first, second) = (second, first);
            }
            return new(Math.Abs(GeometryPreparation.SignedArea(first)));
        }

        private static void Append(List<Point2> points, Point2 point)
        {
            if (points.Count == 0 || !GeometryPreparation.Same(points[^1], point)) points.Add(point);
        }

        private static Point2 Crossing(Point2 a, Point2 b, Point2 s, Point2 e)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double ds = dx * (s.Y - a.Y) - dy * (s.X - a.X);
            double de = dx * (e.Y - a.Y) - dy * (e.X - a.X);
            double fraction = ds / (ds - de);
            if (double.IsFinite(fraction) && fraction > 0 && fraction < 1 && ds != 0 && de != 0 && Math.Sign(ds) != Math.Sign(de))
                return new(s.X + fraction * (e.X - s.X), s.Y + fraction * (e.Y - s.Y));
            Coordinate point = CGAlgorithmsDD.Intersection(new(a.X, a.Y), new(b.X, b.Y), new(s.X, s.Y), new(e.X, e.Y));
            if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidOperationException("Convex clipping produced a nonfinite line intersection.");
            return new(point.X, point.Y);
        }
    }
}
