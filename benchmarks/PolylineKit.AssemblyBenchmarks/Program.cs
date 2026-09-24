using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using PolylineKit;
using PolylineKit.ActiveSweep;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine("PolylineKit.AssemblyBenchmarks <absolute PolylineKit.Winding.dll> <inputs.json> <output.json|check|dump:path> <run-number> <label>");
            return 2;
        }
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        return Runner.Run(args);
    }
}

internal static class Runner
{
    private static double sink;
    private static readonly Type Workspace = typeof(WindingArea).Assembly.GetType("PolylineKit.WindingEngine+Workspace")!;
    private static readonly FieldInfo Cached = Workspace.GetField("cached", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo? OutcomeField = Workspace.GetField("SimpleSweepOutcome", BindingFlags.NonPublic | BindingFlags.Instance);
    private static int Outcome() => OutcomeField is null ? -1 : (int)OutcomeField.GetValue(Cached.GetValue(null))!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string[] args)
    {
        var development = Fixtures.Performance(args[1]);
        foreach (int n in new[] { 1024, 4096 })
        {
            Point2[] late = Fixtures.Comb(n, true);
            (late[^7], late[^5]) = (late[^5], late[^7]);
            development.Add(new("late-crossing-comb", late));
        }
        var fixtures = development.Select(f => (Group: "development", Fixture: f))
            .Concat(FreshFixtures.Create().Select(f => (Group: "fresh", Fixture: f))).ToArray();
        if (args[2] == "check") return Check(fixtures);
        if (args[2].StartsWith("dump:", StringComparison.Ordinal))
            return Dump(args[1], fixtures, args[2][5..]);
        var measurements = new List<object>();
        foreach (var (group, fixture) in fixtures)
        {
            var value = WindingArea.ClosedPath(fixture.Path);
            int outcome = Outcome(); // Reflection occurs only outside measured intervals.
            Sample sample = Measure(() => WindingArea.ClosedPath(fixture.Path).NonZero);
            measurements.Add(new { Group = group, Operation = "ClosedPath", fixture.Name, Vertices = fixture.Path.Length,
                InputSha256 = fixture.Hash(), Outcome = outcome,
                Value = new { value.NonZero, value.EvenOdd, value.AbsoluteWinding, value.Signed, value.CrossingCount },
                Sample = sample });
            Console.WriteLine($"{group}/{fixture.Name}/{fixture.Path.Length}: outcome={outcome}, {sample.MedianUs:F3} us, {sample.MedianBytes} B");
        }
        MeasureRegions(args[1], measurements);
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Label = args[4], Run = int.Parse(args[3]), Utc = DateTimeOffset.UtcNow,
            KernelSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[0]))),
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), LogicalProcessors = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Measurements = measurements
        }, new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(sink);
        return 0;
    }

    private static int Dump(string inputPath, (string Group, Fixture Fixture)[] fixtures, string outputPath)
    {
        var records = new List<object>();
        foreach (var (group, fixture) in fixtures)
            records.Add(new { Operation = "ClosedPath", Name = group + "/" + fixture.Name,
                InputSha256 = fixture.Hash(), Values = NumericBits(WindingArea.ClosedPath(fixture.Path)) });
        using var input = JsonDocument.Parse(File.ReadAllText(inputPath));
        foreach (var item in input.RootElement.EnumerateArray())
        {
            Point2[] a = Read(item.GetProperty("First")), b = Read(item.GetProperty("Second"));
            string name = item.GetProperty("Name").GetString()! + "/" + a.Length;
            string hash = new Fixture(name, a.Concat(b).ToArray()).Hash();
            records.Add(new { Operation = "EndpointBridged", Name = name, InputSha256 = hash,
                Values = NumericBits(WindingArea.EndpointBridged(a, b)) });
            foreach (var rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
                records.Add(new { Operation = "FilledRegions/" + rule, Name = name, FirstVertices = a.Length,
                    InputSha256 = hash, Values = NumericBits(WindingArea.FilledRegions(a, b, rule)) });
        }
        string full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Exact output dump: {records.Count} operation records -> {full}");
        return 0;
    }

    private static SortedDictionary<string, object?> NumericBits(object value)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? v = property.GetValue(value);
            result.Add(property.Name, v is double number ? BitConverter.DoubleToInt64Bits(number).ToString("x16") : v);
        }
        return result;
    }

    private static void MeasureRegions(string inputPath, List<object> measurements)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(inputPath));
        foreach (var item in input.RootElement.EnumerateArray())
        {
            if (item.GetProperty("Name").GetString() != "filled-regions") continue;
            Point2[] a = Read(item.GetProperty("First")), b = Read(item.GetProperty("Second"));
            var value = WindingArea.FilledRegions(a, b);
            int outcome = Outcome();
            Sample sample = Measure(() => WindingArea.FilledRegions(a, b).SymmetricDifferenceArea);
            var fixture = new Fixture("filled-regions", a.Concat(b).ToArray());
            measurements.Add(new { Group = "control-regions", Operation = "FilledRegions", fixture.Name,
                Vertices = a.Length + b.Length, FirstVertices = a.Length, InputSha256 = fixture.Hash(), Outcome = outcome,
                Value = new { value.FirstArea, value.SecondArea, value.IntersectionArea, value.UnionArea,
                    value.SymmetricDifferenceArea, value.CrossingCount }, Sample = sample });
            Console.WriteLine($"control-regions/{a.Length + b.Length}: {sample.MedianUs:F3} us, {sample.MedianBytes} B");
        }
    }

    private static Point2[] Read(JsonElement points) => points.EnumerateArray()
        .Select(p => new Point2(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();

    private static int Check((string Group, Fixture Fixture)[] fixtures)
    {
        int assertions = 0, attempted = 0, certified = 0;
        AppContext.TryGetSwitch("PolylineKit.DisableSimpleSweep", out bool previous);
        try
        {
            foreach (var (group, fixture) in fixtures)
            {
                AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", true);
                var baseline = WindingArea.ClosedPath(fixture.Path);
                AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", false);
                var actual = WindingArea.ClosedPath(fixture.Path);
                int outcome = Outcome();
                if (outcome != 0) attempted++;
                if (outcome == 2) certified++;
                Require(baseline.NonZero == actual.NonZero && baseline.EvenOdd == actual.EvenOdd &&
                    baseline.AbsoluteWinding == actual.AbsoluteWinding && baseline.Signed == actual.Signed &&
                    baseline.CrossingCount == actual.CrossingCount, $"Changed result: {fixture.Name}/{fixture.Path.Length}");
                assertions += 5;
                if (group == "fresh" && fixture.Path.Length == 64)
                {
                    var exact = ExactAreaOracle.Measure(fixture.Path);
                    Near(exact.NonZero, actual.NonZero, fixture.Name + " exact nonzero");
                    Near(exact.EvenOdd, actual.EvenOdd, fixture.Name + " exact evenodd");
                    assertions += 2;
                }
                double? analytic = fixture.Name switch
                {
                    "fresh-thin-ribbon" => .125,
                    "fresh-subdivided-parallelogram" => 2625.0 / 32,
                    "fresh-touching-rectangles" => 30,
                    "fresh-retraced-outline" => 0,
                    _ => null
                };
                if (analytic is double expected) { Near(expected, actual.NonZero, fixture.Name + " analytic"); assertions++; }
                Console.WriteLine($"{group}/{fixture.Name}/{fixture.Path.Length}: {outcome}");
            }
        }
        finally { AppContext.SetSwitch("PolylineKit.DisableSimpleSweep", previous); }
        Console.WriteLine($"Integrated sweep fixture checks: {assertions}; {fixtures.Length} paths; {attempted} attempts, {certified} certified.");
        return 0;
    }

    private static void Near(double expected, double actual, string name) => Require(
        double.IsFinite(actual) && Math.Abs(expected - actual) <= Math.Abs(expected) * 2e-9, $"{name}: {actual:R} != {expected:R}");
    private static void Require(bool success, string name)
    {
        if (!success) throw new InvalidOperationException(name);
    }

    private static Sample Measure(Func<double> invoke)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 30) sink = invoke();
        int iterations = 1;
        while (true)
        {
            timer.Restart();
            for (int i = 0; i < iterations; i++) sink = invoke();
            if (timer.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
            iterations *= 2;
        }
        var us = new double[5];
        var bytes = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = invoke();
            long elapsed = Stopwatch.GetTimestamp() - start, endAllocated = GC.GetAllocatedBytesForCurrentThread();
            us[sample] = elapsed * 1e6 / Stopwatch.Frequency / iterations;
            bytes[sample] = (double)(endAllocated - allocated) / iterations;
        }
        return new(iterations, us.Order().ElementAt(2), bytes.Order().ElementAt(2), us, bytes);
    }

    private sealed record Sample(int Iterations, double MedianUs, double MedianBytes, double[] SamplesUs, double[] SamplesBytes);
}
