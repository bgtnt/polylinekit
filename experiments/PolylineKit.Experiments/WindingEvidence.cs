using System.Numerics;
using System.Text.Json;
using Clipper2Lib;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>
/// Arbitrates every degenerate check where Clipper2 disagreed with the winding engine and the slab sweep,
/// using a third method in exact rational arithmetic: vertical slabs split at every vertex and crossing.
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
            bool Filled(int w) => nonZero ? w != 0 : (w & 1) != 0;
            if (second is null)
            {
                var winding = WindingArea.ClosedPath(first);
                Fraction exact = ExactAreas([first], [w => Filled(w[0])])[0];
                double ours = nonZero ? winding.NonZero : winding.EvenOdd, clipper = ClipperOracle.Contour(first, clipperRule);
                cases.Add(new
                {
                    Name = name, Kind = "closed walk", FillRule = rule.ToString(), Input = first,
                    Exact = exact.ToString(), ExactValue = exact.ToDouble(), Winding = ours, Clipper2Precision8 = clipper,
                    WindingError = ours - exact.ToDouble(), Clipper2Error = clipper - exact.ToDouble()
                });
            }
            else
            {
                var winding = WindingArea.FilledRegions(first, second, rule);
                PathsD subject = [new PathD(first.Select(p => new PointD(p.X, p.Y)))], clip = [new PathD(second.Select(p => new PointD(p.X, p.Y)))];
                double Area(PathsD paths) => Math.Abs(Clipper.Area(paths));
                double[] clipper = [Area(Clipper.Union(subject, new PathsD(), clipperRule, 8)), Area(Clipper.Union(clip, new PathsD(), clipperRule, 8)),
                    Area(Clipper.Intersect(subject, clip, clipperRule, 8)), Area(Clipper.Union(subject, clip, clipperRule, 8)), Area(Clipper.Xor(subject, clip, clipperRule, 8))];
                double[] ours = [winding.FirstArea, winding.SecondArea, winding.IntersectionArea, winding.UnionArea, winding.SymmetricDifferenceArea];
                Fraction[] exact = ExactAreas([first, second],
                [
                    w => Filled(w[0]), w => Filled(w[1]), w => Filled(w[0]) && Filled(w[1]),
                    w => Filled(w[0]) || Filled(w[1]), w => Filled(w[0]) != Filled(w[1])
                ]);
                cases.Add(new
                {
                    Name = name, Kind = "filled regions", FillRule = rule.ToString(), Input = new[] { first, second },
                    Quantities = new[] { "first", "second", "intersection", "union", "symmetric difference" },
                    Exact = exact.Select(e => e.ToString()).ToArray(), ExactValue = exact.Select(e => e.ToDouble()).ToArray(),
                    Winding = ours, Clipper2Precision8 = clipper,
                    WindingError = ours.Zip(exact).Select(x => x.First - x.Second.ToDouble()).ToArray(),
                    Clipper2Error = clipper.Zip(exact).Select(x => x.First - x.Second.ToDouble()).ToArray()
                });
            }
            Console.WriteLine("arbitrated " + name);
        }
        Directory.CreateDirectory(directory);
        var report = new
        {
            Description = "Degenerate integer-grid checks where Clipper2 2.0.0 (precision 8) disagreed with both WindingArea and an independent slab sweep, arbitrated by exact rational slab integration.",
            Clipper2 = typeof(Clipper).Assembly.GetName().Version?.ToString(),
            Method = "Integer inputs; vertical slabs split at every vertex x and every pairwise segment crossing x; inside a slab edge heights are linear, so rational mid-slab heights give the exact area. No floating-point arithmetic is involved.",
            Cases = cases
        };
        File.WriteAllText(Path.Combine(directory, "clipper-disagreements.json"), JsonSerializer.Serialize(report, Evidence.JsonOptions) + "\n");
    }

    // Exact areas where each predicate on the loops' winding numbers holds, for integer-coordinate loops.
    private static Fraction[] ExactAreas(Point2[][] loops, Func<int[], bool>[] predicates)
    {
        if (loops.SelectMany(l => l).Any(p => p.X != Math.Floor(p.X) || p.Y != Math.Floor(p.Y) || Math.Abs(p.X) > 1e9 || Math.Abs(p.Y) > 1e9))
            throw new ArgumentException("Exact arbitration expects moderate integer coordinates.");
        var edges = loops.SelectMany((l, id) => Enumerable.Range(0, l.Length)
                .Select(i => (A: ((long)l[i].X, (long)l[i].Y), B: ((long)l[(i + 1) % l.Length].X, (long)l[(i + 1) % l.Length].Y), Loop: id)))
            .Where(e => e.A != e.B).ToArray();
        var cuts = new SortedSet<Fraction>(edges.Select(e => new Fraction(e.A.Item1)));
        foreach (var e in edges)
        foreach (var f in edges)
        {
            long d1x = e.B.Item1 - e.A.Item1, d1y = e.B.Item2 - e.A.Item2, d2x = f.B.Item1 - f.A.Item1, d2y = f.B.Item2 - f.A.Item2;
            long den = d1x * d2y - d1y * d2x;
            if (den == 0) continue;
            var t = new Fraction((f.A.Item1 - e.A.Item1) * d2y - (f.A.Item2 - e.A.Item2) * d2x, den);
            var u = new Fraction((f.A.Item1 - e.A.Item1) * d1y - (f.A.Item2 - e.A.Item2) * d1x, den);
            if (t.Sign < 0 || t > Fraction.One || u.Sign < 0 || u > Fraction.One) continue;
            cuts.Add(new Fraction(e.A.Item1) + t * new Fraction(d1x));
        }
        var areas = Enumerable.Repeat(Fraction.Zero, predicates.Length).ToArray();
        Fraction[] xs = cuts.ToArray();
        int[] w = new int[loops.Length];
        for (int k = 1; k < xs.Length; k++)
        {
            Fraction mid = (xs[k - 1] + xs[k]) * new Fraction(1, 2), width = xs[k] - xs[k - 1];
            var active = edges.Where(e => new Fraction(Math.Min(e.A.Item1, e.B.Item1)) < mid && mid < new Fraction(Math.Max(e.A.Item1, e.B.Item1)))
                .Select(e => (Y: new Fraction(e.A.Item2) + new Fraction(e.B.Item2 - e.A.Item2) * (mid - new Fraction(e.A.Item1)) / new Fraction(e.B.Item1 - e.A.Item1),
                              e.Loop, Delta: e.B.Item1 > e.A.Item1 ? 1 : -1))
                .OrderBy(e => e.Y).ToArray();
            Array.Clear(w, 0, w.Length);
            for (int i = 0; i + 1 < active.Length; i++)
            {
                w[active[i].Loop] += active[i].Delta;
                Fraction piece = (active[i + 1].Y - active[i].Y) * width;
                for (int p = 0; p < predicates.Length; p++) if (predicates[p](w)) areas[p] += piece;
            }
        }
        return areas;
    }

    private readonly struct Fraction : IComparable<Fraction>
    {
        private readonly BigInteger n, d;
        public Fraction(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            BigInteger g = BigInteger.GreatestCommonDivisor(numerator, denominator);
            if (!g.IsZero && !g.IsOne) { numerator /= g; denominator /= g; }
            n = numerator; d = denominator.IsZero ? BigInteger.One : denominator;
        }
        public Fraction(long value) : this(value, 1) { }
        public static readonly Fraction Zero = new(0), One = new(1);
        public int Sign => n.Sign;
        public static Fraction operator +(Fraction a, Fraction b) => new(a.n * b.d + b.n * a.d, a.d * b.d);
        public static Fraction operator -(Fraction a, Fraction b) => new(a.n * b.d - b.n * a.d, a.d * b.d);
        public static Fraction operator *(Fraction a, Fraction b) => new(a.n * b.n, a.d * b.d);
        public static Fraction operator /(Fraction a, Fraction b) => new(a.n * b.d, a.d * b.n);
        public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;
        public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;
        public int CompareTo(Fraction other) => (n * other.d).CompareTo(other.n * d);
        public double ToDouble() => (double)n / (double)d;
        public override string ToString() => d.IsOne ? n.ToString() : $"{n}/{d}";
    }
}
