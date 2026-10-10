using System.Security.Cryptography;
using System.Text.Json;

namespace ContourSimplificationDemo;

internal static class Presets
{
    private const string ManifestHash = "b2f0a57e59ca5728be16ebd464fb67bf821961dde033d26cb3be143e9a2832e5";
    private sealed record FileEntry(string File, string Sha256, int Vertices);
    private sealed record Manifest(string SourceVersion, string License, FileEntry[] Contours);
    private sealed record Shape(string Code, string Name, double[][] Points);

    internal static Preset[] Load()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "data");
        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(directory, "manifest.json"));
        if (Hash(manifestBytes) != ManifestHash) throw new InvalidDataException("The frozen AreaChange manifest checksum differs.");
        Manifest manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes) ?? throw new InvalidDataException("Missing AreaChange manifest.");
        var presets = new List<Preset>();
        foreach (FileEntry entry in manifest.Contours)
        {
            if (Path.GetFileName(entry.File) != entry.File) throw new InvalidDataException("Unexpected preset filename.");
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, entry.File));
            if (Hash(bytes) != entry.Sha256) throw new InvalidDataException("The frozen contour checksum differs: " + entry.File);
            Shape shape = JsonSerializer.Deserialize<Shape>(bytes) ?? throw new InvalidDataException("Missing contour: " + entry.File);
            var clean = CandidateGenerator.Validate(shape.Points);
            if (clean.Length != entry.Vertices || shape.Points.Length != entry.Vertices)
                throw new InvalidDataException("The frozen contour vertex count differs: " + entry.File);
            presets.Add(new(shape.Code, shape.Name,
                manifest.SourceVersion + "; " + manifest.License + ". Local map plane scaled to a longest bound of 1000; not square kilometres.",
                CandidateGenerator.Coordinates(clean)));
        }
        if (presets.Count != 4 || !presets.Select(p => p.Id).SequenceEqual(new[] { "BGR", "CHE", "LSO", "NPL" }))
            throw new InvalidDataException("The complete AreaChange preset inventory differs.");
        presets.Add(new("MIXED", "Added and removed area",
            "Analytic control input: a half-unit triangular notch and bump have equal area. Try a 5% budget; equal total area can hide changed fill.",
            MixedPoints()));
        return presets.ToArray();
    }

    internal static double[][] MixedPoints() =>
    [
        [0, 0], [8, 0], [8, 4], [7, 4], [6, 4.5], [5, 4],
        [3, 4], [2, 3.5], [1, 4], [0, 4]
    ];
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
