using System.Text.Json;

namespace PolylineKit.Experiments;

internal static class BenchmarkJson
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
