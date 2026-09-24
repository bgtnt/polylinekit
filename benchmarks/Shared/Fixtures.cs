using System.Security.Cryptography;
using System.Text.Json;

namespace PolylineKit.ActiveSweep;

internal sealed record Fixture(string Name, Point2[] Path)
{
    internal string Hash()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var point in Path) { writer.Write(point.X); writer.Write(point.Y); }
        writer.Flush();
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
}

internal static class Fixtures
{
    internal static List<Fixture> Performance(string inputPath)
    {
        var result = new List<Fixture>();
        using var input = JsonDocument.Parse(File.ReadAllText(inputPath));
        foreach (var row in input.RootElement.EnumerateArray())
        {
            string name = row.GetProperty("Name").GetString()!;
            // Two independently filled paths are a different operation. This first
            // prototype only certifies one closed walk, including a prebuilt bridge.
            if (name == "filled-regions") continue;
            Point2[] a = Read(row.GetProperty("First")), b = Read(row.GetProperty("Second"));
            result.Add(new(name, a.Concat(b.Reverse()).ToArray()));
        }
        foreach (int n in new[] { 64, 256, 1024, 4096 })
        {
            result.Add(new("ring", Radial(n, false)));
            result.Add(new("radial-star", Radial(n, true)));
            result.Add(new("stacked-bars", Bars(n, false)));
            result.Add(new("diagonal-bars", Bars(n, true)));
            result.Add(new("simple-comb", Comb(n, false)));
            result.Add(new("simple-diagonal-comb", Comb(n, true)));
            Point2[] band = Enumerable.Range(0, n / 2).Select(i => new Point2(-1, i))
                .Concat(Enumerable.Range(0, n / 2).Reverse().Select(i => new Point2(1, i))).ToArray();
            result.Add(new("vertical-band", band));
        }
        result.Add(new("late-crossing", [new(0, 0), new(1000, 0), new(1000, 1000),
            new(999, 1000), new(999, -1), new(0, -1)]));
        return result;
    }

    internal static Point2[] Bars(int n, bool diagonal) => Enumerable.Range(0, n).Select(i =>
    {
        double x = i % 4 == 0 || i % 4 == 3 ? 0 : n * 2;
        return new Point2(x, (diagonal ? x : 0) + i / 2);
    }).ToArray();

    // Bars' closing edge touches/retraces their left endpoints. An outside closing
    // lane makes a separate, genuinely simple fixture; both families remain measured.
    internal static Point2[] Comb(int n, bool diagonal) => Bars(n, false)
        .Concat(new Point2[] { new(-1, n / 2 - 1), new(-1, 0) })
        .Select(p => diagonal ? new Point2(p.X, p.Y + p.X) : p).ToArray();

    internal static Point2[] Radial(int n, bool star) => Enumerable.Range(0, n).Select(i =>
    {
        double t = i * (2 * Math.PI / n), r = star && (i & 1) != 0 ? 1 : 100;
        return new Point2(r * Math.Cos(t), r * Math.Sin(t));
    }).ToArray();

    private static Point2[] Read(JsonElement e) => e.EnumerateArray()
        .Select(p => new Point2(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();
}
