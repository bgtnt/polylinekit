using Clipper2Lib;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

internal readonly record struct Digest(double IntersectionSum, double CoverageSum, int Candidates);
internal sealed record PreparedOperand(RawFeature Raw, double Area, object Data, Box NativeBounds);
internal sealed record ClipperInput(ReuseableDataContainer64 Data, Paths64 Paths);

/// <summary>Each session owns backend scratch buffers. No pair results are cached.</summary>
internal sealed class Session
{
    internal static readonly string[] Backends = ["Core-alpha.2", "Clipper64-reused-data", "NTS-OverlayNGRobust"];
    private readonly string backend;
    private readonly PathFillRule rule;
    private readonly Clipper64? clipper;
    private readonly Paths64? closed, open;
    internal PreparedOperand[] First { get; }
    internal PreparedOperand[] Second { get; }

    internal Session(RawFeature[] first, RawFeature[] second, string backend, PathFillRule rule)
    {
        Contracts.Require(Backends.Contains(backend, StringComparer.Ordinal), "Unknown backend.");
        this.backend = backend; this.rule = rule;
        if (backend == Backends[1]) { clipper = new(); closed = new(); open = new(); }
        First = first.Select(f => Prepare(f, false)).ToArray();
        Second = second.Select(f => Prepare(f, true)).ToArray();
    }

    private PreparedOperand Prepare(RawFeature raw, bool clip)
    {
        if (backend == Backends[0])
        {
            PreparedRegion region = PrepareCore(raw);
            return new(raw, RegionArea.FilledArea(region, rule), region, raw.Bounds);
        }
        if (backend == Backends[1])
        {
            var paths = new Paths64(raw.Rings.Length);
            foreach (RawPolygon polygon in raw.Polygons)
            {
                paths.Add(QuantizedRing(polygon.Shell, true));
                foreach (Point2[] hole in polygon.Holes) paths.Add(QuantizedRing(hole, false));
            }
            var data = new ReuseableDataContainer64();
            data.AddPaths(paths, clip ? PathType.Clip : PathType.Subject, false);
            // Independently validated simple, disjoint Polygon/MultiPolygon hierarchies permit
            // the ordinary signed shell-minus-hole area. This is not arbitrary-walk filled area.
            double area = Math.Abs(Clipper.Area(paths)) / Contracts.ClipperScale / Contracts.ClipperScale;
            Box nativeBounds = Box.Of(paths.SelectMany(p => p).Select(p => new Point2(p.X / Contracts.ClipperScale,
                p.Y / Contracts.ClipperScale)));
            return new(raw, area, new ClipperInput(data, paths), nativeBounds);
        }
        Geometry geometry = Contracts.Geometry(raw);
        Envelope bounds = geometry.EnvelopeInternal;
        return new(raw, geometry.Area, geometry, new(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY));
    }

    internal static PreparedRegion PrepareCore(RawFeature raw)
    {
        if (raw.Polygons.Length == 1)
        {
            RawPolygon polygon = raw.Polygons[0];
            return PreparedRegion.FromPolygon(polygon.Shell, polygon.Holes.Cast<IReadOnlyList<Point2>>().ToArray());
        }
        return PreparedRegion.FromPolygons(raw.Polygons.Select(p =>
            (Exterior: (IReadOnlyList<Point2>)p.Shell, Holes: (IReadOnlyList<IReadOnlyList<Point2>>?)p.Holes.Cast<IReadOnlyList<Point2>>().ToArray())).ToArray());
    }

    private static Path64 QuantizedRing(Point2[] raw, bool shell)
    {
        var path = new Path64(raw.Length);
        foreach (Point2 point in raw) path.Add(new Point64(Corpus.Quantize(point.X), Corpus.Quantize(point.Y)));
        Coordinate[] coordinates = path.Select(p => new Coordinate(p.X / Contracts.ClipperScale, p.Y / Contracts.ClipperScale)).ToArray();
        if (Orientation.IsCCW(coordinates) != shell) path.Reverse();
        return path;
    }

    internal double Intersection(PreparedOperand first, PreparedOperand second)
    {
        // Every adapter sees the same original-coordinate outer rejection, including edge touching.
        if (!first.Raw.Bounds.Overlaps(second.Raw.Bounds)) return 0;
        if (backend == Backends[0]) return RegionArea.IntersectionArea((PreparedRegion)first.Data, (PreparedRegion)second.Data, rule);
        if (backend == Backends[1]) return Clip(first, second, ClipType.Intersection);
        return OverlayNGRobust.Overlay((Geometry)first.Data, (Geometry)second.Data, SpatialFunction.Intersection).Area;
    }

    internal double SelectedForValidation(PreparedOperand first, PreparedOperand second) =>
        backend == Backends[0] ? RegionArea.IntersectionArea((PreparedRegion)first.Data, (PreparedRegion)second.Data, rule) :
        Intersection(first, second);

    internal MetricValue Full(PreparedOperand first, PreparedOperand second)
    {
        if (backend == Backends[0]) return MetricValue.From(RegionArea.Compare((PreparedRegion)first.Data, (PreparedRegion)second.Data, rule));
        if (backend == Backends[1])
            return MetricValue.Of(first.Area, second.Area, Clip(first, second, ClipType.Intersection),
                Clip(first, second, ClipType.Union), Clip(first, second, ClipType.Xor));
        Geometry a = (Geometry)first.Data, b = (Geometry)second.Data;
        return MetricValue.Of(first.Area, second.Area, OverlayNGRobust.Overlay(a, b, SpatialFunction.Intersection).Area,
            OverlayNGRobust.Overlay(a, b, SpatialFunction.Union).Area,
            OverlayNGRobust.Overlay(a, b, SpatialFunction.SymDifference).Area);
    }

    private double Clip(PreparedOperand first, PreparedOperand second, ClipType type)
    {
        clipper!.Clear();
        clipper.AddReuseableData(((ClipperInput)first.Data).Data);
        clipper.AddReuseableData(((ClipperInput)second.Data).Data);
        if (!clipper.Execute(type, rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd, closed!, open!))
            throw new InvalidOperationException("Clipper operation failed: " + type);
        // Output hole signs are retained; taking absolute area of each contour would overcount holes.
        return Math.Abs(Clipper.Area(closed!)) / Contracts.ClipperScale / Contracts.ClipperScale;
    }

    internal Digest Table()
    {
        double intersectionSum = 0, coverageSum = 0; int candidates = 0;
        foreach (PreparedOperand first in First)
        foreach (PreparedOperand second in Second)
        {
            if (!first.Raw.Bounds.Overlaps(second.Raw.Bounds)) continue;
            candidates++;
            double area = Intersection(first, second);
            if (!double.IsFinite(area) || !double.IsFinite(first.Area) || first.Area <= 0)
                throw new InvalidOperationException("Nonfinite intersection or invalid denominator in the measured table: " + first.Raw.Id + "/" + second.Raw.Id);
            intersectionSum += area; coverageSum += area / first.Area;
        }
        return new(intersectionSum, coverageSum, candidates);
    }

    internal Digest PreparationDigest() => new(First.Sum(f => f.Area), Second.Sum(f => f.Area), First.Length + Second.Length);
}
