using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using PolylineKit;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("WindingPerf <absolute PolylineKit.dll path> <results.json> <fixture inputs.json>"); return 2; }
        AppContext.SetSwitch("PolylineKit.DisableSimd", Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        return Runner.Run(args);
    }
}
internal static class Runner
{
    private static double sink;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args)
    {
        string inputFile = args[2];
        var input = JsonDocument.Parse(File.ReadAllText(inputFile));
        var cases = new List<(string Name, int N, Point2[] A, Point2[] B)>();
        foreach (var item in input.RootElement.EnumerateArray())
            cases.Add((item.GetProperty("Name").GetString()!, item.GetProperty("Vertices").GetInt32(), Read(item.GetProperty("First")), Read(item.GetProperty("Second"))));
        foreach (int n in new[] { 256, 1024, 4096 })
        {
            var a = Enumerable.Range(0, n / 2).Select(i => new Point2(-1, i)).Concat(Enumerable.Range(0, n / 2).Reverse().Select(i => new Point2(1, i))).ToArray();
            cases.Add(("vertical-bands", n, a, []));
            cases.Add(("horizontal-bands", n, a.Select(p => new Point2(p.Y, p.X)).ToArray(), []));
        }
        var rows = new List<object>();
        foreach (int n in new[] { 256, 1024, 4096 })
        {
            var ring = Enumerable.Range(0, n).Select(i => { double t = i * (2 * Math.PI / n); return new Point2(100 * Math.Cos(t), 100 * Math.Sin(t)); }).ToArray();
            cases.Add(("ring", n, ring, []));
            // Long alternating horizontal bars, separated along Y; X remains the wider bounds axis.
            var bars = Enumerable.Range(0, n).Select(i => new Point2((i % 4 == 0 || i % 4 == 3) ? 0 : n * 2, i / 2)).ToArray();
            cases.Add(("stacked-bars", n, bars, []));
        }
        foreach (int n in new[] { 64, 256 })
        {
            // Overlapping AABBs of parallel diagonal bars: an index cannot eliminate these false positives.
            var diagonal = Enumerable.Range(0, n).Select(i => { double x = (i % 4 == 0 || i % 4 == 3) ? 0 : n * 2; return new Point2(x, x + i / 2); }).ToArray();
            cases.Add(("diagonal-bars", n, diagonal, []));
            var star = Enumerable.Range(0, n).Select(i => { double t = i * (2 * Math.PI / n), r = (i & 1) == 0 ? 100 : 1; return new Point2(r * Math.Cos(t), r * Math.Sin(t)); }).ToArray();
            cases.Add(("radial-star", n, star, []));
        }
        foreach (var item in cases)
        {
            // This strongly typed delegate binds once. Reflection is used only to load the chosen assembly.
            double Invoke() => item.Name == "filled-regions" ? WindingArea.FilledRegions(item.A, item.B).SymmetricDifferenceArea : item.B.Length == 0 ? WindingArea.ClosedPath(item.A).NonZero : WindingArea.EndpointBridged(item.A, item.B).NonZero;
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 25) sink = Invoke();
            int iterations = 1;
            while (true)
            {
                timer.Restart();
                for (int k = 0; k < iterations; k++) sink = Invoke();
                if (timer.ElapsedMilliseconds >= 20 || iterations >= 1048576) break;
                iterations *= 2;
            }
            var us = new double[5];
            var bytes = new double[5];
            for (int sample = 0; sample < 5; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int k = 0; k < iterations; k++) sink = Invoke();
                long elapsed = Stopwatch.GetTimestamp() - start;
                long endAllocated = GC.GetAllocatedBytesForCurrentThread();
                us[sample] = elapsed * 1e6 / Stopwatch.Frequency / iterations;
                bytes[sample] = (double)(endAllocated - allocated) / iterations;
            }
            var row = new { item.Name, item.N, Iterations = iterations, Value = sink, MedianUs = us.Order().ElementAt(2), MedianBytes = bytes.Order().ElementAt(2), SamplesUs = us, SamplesBytes = bytes };
            rows.Add(row);
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        string path = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Kernel = Path.GetFullPath(args[0]), Runtime = RuntimeInformation.FrameworkDescription, Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), Vector256 = System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated, ForcedScalar = AppContext.TryGetSwitch("PolylineKit.DisableSimd", out bool disabled) && disabled, Utc = DateTimeOffset.UtcNow, Measurements = rows }, new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(sink);
        return 0;
    }
    private static Point2[] Read(JsonElement e) => e.EnumerateArray().Select(p => new Point2(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();
}
