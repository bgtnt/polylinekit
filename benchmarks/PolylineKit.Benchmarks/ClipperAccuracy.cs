using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;

namespace PolylineKit.Experiments;

/// <summary>Small accuracy controls with analytic or archived exact-rational expectations.</summary>
internal static class ClipperAccuracy
{
    private sealed record Row(string Name, string Quantity, string Exact, double Expected,
        double Winding, double Clipper, int Precision, double WindingError, double ClipperError);

    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var rows = new List<Row>();
        string fixture = Path.Combine(AppContext.BaseDirectory, "clipper-accuracy.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixture));
        foreach (var item in document.RootElement.GetProperty("Cases").EnumerateArray())
        {
            string name = item.GetProperty("Name").GetString()!;
            var rule = Enum.Parse<PathFillRule>(item.GetProperty("FillRule").GetString()!);
            var clipRule = rule == PathFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;
            if (item.GetProperty("Kind").GetString() == "closed walk")
            {
                var a = Points(item.GetProperty("Input"));
                var r = WindingArea.ClosedPath(a);
                Add(name, "area", item.GetProperty("Exact").GetString()!, item.GetProperty("ExactValue").GetDouble(),
                    rule == PathFillRule.NonZero ? r.NonZero : r.EvenOdd,
                    Clipper.Area(Clipper.Union(Paths(a), new PathsD(), clipRule, 8)), 8);
            }
            else
            {
                var a = Points(item.GetProperty("Input")[0]); var b = Points(item.GetProperty("Input")[1]);
                var r = WindingArea.FilledRegions(a, b, rule);
                double[] values = [r.FirstArea, r.SecondArea, r.IntersectionArea, r.UnionArea, r.SymmetricDifferenceArea];
                var ca = Paths(a); var cb = Paths(b);
                double[] clipped = [Clipper.Area(Clipper.Union(ca, new PathsD(), clipRule, 8)),
                    Clipper.Area(Clipper.Union(cb, new PathsD(), clipRule, 8)),
                    Clipper.Area(Clipper.Intersect(ca, cb, clipRule, 8)),
                    Clipper.Area(Clipper.Union(ca, cb, clipRule, 8)),
                    Clipper.Area(Clipper.Xor(ca, cb, clipRule, 8))];
                for (int i = 0; i < values.Length; i++)
                    Add(name, item.GetProperty("Quantities")[i].GetString()!, item.GetProperty("Exact")[i].GetString()!,
                        item.GetProperty("ExactValue")[i].GetDouble(), values[i], clipped[i], 8);
            }
        }
        foreach (double length in new[] { 1e4, 1e8, 1e12, 1e16 })
        {
            Point2[] triangle = [new(0, 0), new(length, length), new(2 * length, Math.BitIncrement(2 * length))];
            double exact = length * (Math.BitIncrement(2 * length) - 2 * length) / 2;
            // Keep the scaled coordinates below Clipper's integer range; each precision is recorded.
            int precision = Math.Min(8, (int)Math.Floor(Math.Log10(1e18 / (2 * length))));
            Add($"thin triangle L={length:G}", "area", "L*(next(2L)-2L)/2", exact,
                WindingArea.ClosedPath(triangle).NonZero,
                Clipper.Area(Clipper.Union(Paths(triangle), new PathsD(), FillRule.NonZero, precision)), precision);
        }
        foreach (double length in new[] { 1e8, 1e12, 1e16 })
        {
            double m = length / 1000;
            Point2[] a = [new(-length, -m - 1), new(length, m - 1), new(length, m + 1), new(-length, -m + 1)];
            Point2[] b = [new(m - 1, -length), new(m + 1, -length), new(-m + 1, length), new(-m - 1, length)];
            int precision = Math.Min(8, (int)Math.Floor(Math.Log10(1e18 / length)));
            Add($"tilted strips L={length:G}", "intersection", "4000000/1000001", 4000000.0 / 1000001,
                WindingArea.FilledRegions(a, b).IntersectionArea,
                Clipper.Area(Clipper.Intersect(Paths(a), Paths(b), FillRule.NonZero, precision)), precision);
        }
        const double distance = 1e8;
        Point2[] distant = [new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0, 0),
            new(distance, distance), new(distance + 1, distance), new(distance + 1, distance + 1),
            new(distance, distance + 1), new(distance, distance)];
        Add("unit squares 1e8 apart with retraced bridge", "area", "1+1", 2,
            WindingArea.ClosedPath(distant).NonZero,
            Clipper.Area(Clipper.Union(Paths(distant), new PathsD(), FillRule.NonZero, 8)), 8);
        var result = new
        {
            FixtureSource = document.RootElement.GetProperty("Source").GetString(),
            FixtureSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).ToLowerInvariant(),
            WindingSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(WindingArea).Assembly.Location))).ToLowerInvariant(),
            ClipperVersion = typeof(Clipper64).Assembly.GetName().Version!.ToString(),
            Rows = rows
        };
        File.WriteAllText(Path.Combine(directory, "accuracy.json"), JsonSerializer.Serialize(result, BenchmarkJson.Options) + "\n");
        var md = new StringBuilder("# Accuracy controls\n\nErrors are signed differences from rounded exact expectations for the supplied binary64 input. ClipperD quantizes to the recorded decimal precision; the grid step is not an area-error bound. Selected historical disagreement cases are regression controls, not a representative accuracy survey. The original rational derivation is linked by the fixture's Source.\n\n| Case | Quantity | Expected | Winding error | ClipperD error | Precision |\n|---|---|---:|---:|---:|---:|\n");
        foreach (var r in rows)
            md.AppendLine(FormattableString.Invariant($"| {r.Name} | {r.Quantity} | {r.Expected:G17} | {r.WindingError:G5} | {r.ClipperError:G5} | {r.Precision} |"));
        File.WriteAllText(Path.Combine(directory, "accuracy.md"), md.ToString());
        Console.WriteLine($"Accuracy controls: {rows.Count} quantities written to {directory}.");

        void Add(string name, string quantity, string formula, double expected, double winding, double clipper, int precision)
        {
            if (!double.IsFinite(winding) || !double.IsFinite(clipper)) throw new InvalidOperationException("Nonfinite accuracy result.");
            rows.Add(new(name, quantity, formula, expected, winding, clipper, precision, winding - expected, clipper - expected));
        }
    }

    private static Point2[] Points(JsonElement value) => value.EnumerateArray()
        .Select(p => new Point2(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();
    private static PathsD Paths(Point2[] points) => new() { new PathD(points.Select(p => new PointD(p.X, p.Y))) };
}
