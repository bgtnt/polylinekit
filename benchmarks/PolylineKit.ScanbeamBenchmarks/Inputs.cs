using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

internal sealed record Input(string Name, Point2[] Points, bool Gate)
{
    internal string Hash => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Points)));
}

internal static class Inputs
{
    internal static Input[] Create()
    {
        var result = new List<Input>();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "winding.json")));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("Name").GetString() != "degenerate-grid") continue;
            static Point2[] Read(JsonElement e) => e.EnumerateArray().Select(p =>
                new Point2(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();
            var a = Read(item.GetProperty("First"));
            var b = Read(item.GetProperty("Second"));
            result.Add(new($"frozen-grid-{a.Length}", [.. a, .. b.Reverse()], true));
        }
        foreach (int n in new[] { 128, 512 })
            result.Add(new($"fresh-grid-{n}", Grid(n, 0x51ab23u + (uint)n), false));
        Point2[] repeated = Enumerable.Range(0, 128).SelectMany(_ => new Point2[]
            { new(0, 0), new(7, 0), new(7, 7), new(0, 7) }).ToArray();
        result.Add(new("repeated-square-512", repeated, false));
        result.Add(new("integer-star-512", Enumerable.Range(0, 512).Select(i =>
        {
            double a = i * (2 * Math.PI / 512), r = i % 2 == 0 ? 2000 : 1000;
            return new Point2(Math.Round(r * Math.Cos(a)), Math.Round(r * Math.Sin(a)));
        }).ToArray(), false));
        uint state = 0x81d2ab31;
        result.Add(new("many-levels-128", Enumerable.Range(0, 128).Select(_ =>
            new Point2(Next(ref state) % 4097 - 2048.0, Next(ref state) % 4097 - 2048.0)).ToArray(), false));
        return result.ToArray();
    }

    internal static Point2[] Grid(int n, uint seed)
    {
        uint state = seed;
        return Enumerable.Range(0, n).Select(_ => new Point2(Next(ref state) % 8, Next(ref state) % 8)).ToArray();
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return state;
    }
}
