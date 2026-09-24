using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>
/// Arbitrates every degenerate check where Clipper2 disagreed with the winding engine and the slab sweep,
/// using a third method: a scanline integral with a stated error bound.
/// </summary>
internal static class WindingEvidence
{
    public static void Write(string directory)
    {
        WindingAreaChecks.Run();
        var cases = new List<object>();
        foreach (var (name, first, second) in WindingAreaChecks.ClipperDisagreements)
        {
            bool nonZero = name.EndsWith("NonZero", StringComparison.Ordinal);
            PathFillRule rule = nonZero ? PathFillRule.NonZero : PathFillRule.EvenOdd;
            FillRule clipperRule = nonZero ? FillRule.NonZero : FillRule.EvenOdd;
            if (second is null)
            {
                var winding = WindingArea.ClosedPath(first);
                var sweep = ContourSweep.Measure(first);
                double clipper = ClipperOracle.Contour(first, clipperRule);
                double scanline = Scanline([first], [w => nonZero ? w[0] != 0 : (w[0] & 1) != 0], out double bound)[0];
                cases.Add(new
                {
                    Name = name, Kind = "closed walk", FillRule = rule.ToString(), Input = first,
                    Winding = nonZero ? winding.NonZero : winding.EvenOdd, SlabSweep = nonZero ? sweep.NonZero : sweep.EvenOdd,
                    Clipper2Precision8 = clipper, Scanline = scanline, ScanlineErrorBound = bound,
                    WindingWithinScanlineBound = Math.Abs((nonZero ? winding.NonZero : winding.EvenOdd) - scanline) <= bound,
                    ClipperWithinScanlineBound = Math.Abs(clipper - scanline) <= bound
                });
            }
            else
            {
                var winding = WindingArea.FilledRegions(first, second, rule);
                var sweep = RegionSweep.Measure(first, second, nonZero);
                PathsD subject = [new PathD(first.Select(p => new PointD(p.X, p.Y)))], clip = [new PathD(second.Select(p => new PointD(p.X, p.Y)))];
                double Area(PathsD paths) => Math.Abs(Clipper.Area(paths));
                bool Filled(int w) => nonZero ? w != 0 : (w & 1) != 0;
                double[] clipper = [Area(Clipper.Union(subject, new PathsD(), clipperRule, 8)), Area(Clipper.Union(clip, new PathsD(), clipperRule, 8)),
                    Area(Clipper.Intersect(subject, clip, clipperRule, 8)), Area(Clipper.Union(subject, clip, clipperRule, 8)), Area(Clipper.Xor(subject, clip, clipperRule, 8))];
                double[] ours = [winding.FirstArea, winding.SecondArea, winding.IntersectionArea, winding.UnionArea, winding.SymmetricDifferenceArea];
                double[] slabs = [sweep.First, sweep.Second, sweep.Intersection, sweep.Union, sweep.SymmetricDifference];
                double[] scanline = Scanline([first, second],
                [
                    w => Filled(w[0]), w => Filled(w[1]), w => Filled(w[0]) && Filled(w[1]),
                    w => Filled(w[0]) || Filled(w[1]), w => Filled(w[0]) != Filled(w[1])
                ], out double bound);
                cases.Add(new
                {
                    Name = name, Kind = "filled regions", FillRule = rule.ToString(), Input = new[] { first, second },
                    Quantities = new[] { "first", "second", "intersection", "union", "symmetric difference" },
                    Winding = ours, SlabSweep = slabs, Clipper2Precision8 = clipper, Scanline = scanline, ScanlineErrorBound = bound,
                    WindingWithinScanlineBound = ours.Zip(scanline).All(x => Math.Abs(x.First - x.Second) <= bound),
                    ClipperWithinScanlineBound = clipper.Zip(scanline).All(x => Math.Abs(x.First - x.Second) <= bound)
                });
            }
            Console.WriteLine("arbitrated " + name);
        }
        Directory.CreateDirectory(directory);
        var report = new
        {
            Description = "Degenerate integer-grid checks where Clipper2 2.0.0 (precision 8) disagreed with both WindingArea and an independent slab sweep, arbitrated by an independent scanline integral with a stated error bound.",
            Clipper2 = typeof(Clipper).Assembly.GetName().Version?.ToString(), ScanlineRows = Rows,
            ScanlineErrorBound = "(vertices + pairwise crossings + 1) * row height * width: exact interval lengths per row, midpoint rule in y, exact between breakpoint heights",
            Cases = cases
        };
        File.WriteAllText(Path.Combine(directory, "clipper-disagreements.json"), JsonSerializer.Serialize(report, Evidence.JsonOptions) + "\n");
    }

    // Areas where each predicate on the loops' winding numbers holds. Every row integrates exact interval
    // lengths at its mid height (the midpoint rule in y). Between y values of vertices and crossings those
    // lengths are linear in y, so the rule is exact there; a row containing such a breakpoint errs by at most
    // its height times the width. The bound counts all vertices and all pairwise crossings as breakpoints.
    private static double[] Scanline(Point2[][] loops, Func<int[], bool>[] predicates, out double bound)
    {
        var all = loops.SelectMany(l => l).ToArray();
        double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
        double h = (maxY - minY) / Rows;
        var edges = loops.SelectMany((l, id) => Enumerable.Range(0, l.Length).Select(i => (A: l[i], B: l[(i + 1) % l.Length], Loop: id)))
            .Where(e => e.A.Y != e.B.Y).ToArray();
        var areas = new double[predicates.Length];
        var hits = new List<(double X, int Loop, int Delta)>();
        int[] w = new int[loops.Length];
        for (int row = 0; row < Rows; row++)
        {
            double y = minY + (row + .5) * h;
            hits.Clear();
            foreach (var e in edges)
                if ((e.A.Y <= y) != (e.B.Y <= y))
                    hits.Add((e.A.X + (y - e.A.Y) * (e.B.X - e.A.X) / (e.B.Y - e.A.Y), e.Loop, e.B.Y > e.A.Y ? 1 : -1));
            hits.Sort((a, b) => a.X.CompareTo(b.X));
            Array.Clear(w, 0, w.Length);
            // Winding at x = -infinity is zero; crossing an edge from left to right subtracts its upward delta.
            for (int k = 0; k + 1 < hits.Count; k++)
            {
                w[hits[k].Loop] -= hits[k].Delta;
                double length = hits[k + 1].X - hits[k].X;
                for (int p = 0; p < predicates.Length; p++) if (predicates[p](w)) areas[p] += length * h;
            }
        }
        int crossings = 0, vertices = loops.Sum(l => l.Length);
        var segments = loops.SelectMany(l => Enumerable.Range(0, l.Length).Select(i => (A: l[i], B: l[(i + 1) % l.Length]))).ToArray();
        for (int i = 0; i < segments.Length; i++)
        for (int j = i + 1; j < segments.Length; j++)
            if (Geometry.Intersection(segments[i].A, segments[i].B, segments[j].A, segments[j].B, out _, out _)) crossings++;
        bound = (vertices + crossings + 1) * h * (maxX - minX);
        return areas;
    }

    private const int Rows = 1_000_000;
}
