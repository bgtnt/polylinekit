using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using DynamicIndexBenchmarks;
using NetTopologySuite.Geometries;
using PolylineKit;

return Experiment.Run(args);

internal static class Experiment
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] Scopes = ["build", "build-and-index-replay", "complete-simplification"];
    private static ulong sink;
    private sealed record Group(string Name, Scenario[] Cases);
    private sealed record CaseEvidence(string Id, string Family, int Vertices, int FinalVertices, int BlockedProposals, Counters Value);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Row(string Group, string Method, string Scope, Counters Value, double MedianNanoseconds, double MedianBytes, Sample[] Samples);
    private sealed record RunData(int Run, string Revision, DateTimeOffset Utc, string Runtime, string OS, string Architecture,
        string? Cpu, string? TieredCompilation, string InputHash, string TraceHash, Dictionary<string,string> Assemblies,
        string[] Methods, CaseEvidence[] Cases, Row[] Rows);

    internal static int Run(string[] args)
    {
        var factories = new List<IEdgeIndexFactory> { new LinearIndexFactory(), new DenseLinearIndexFactory(), new FixedSlotIndexFactory() };
        var arguments = args.ToList();
        int plugin = arguments.IndexOf("--plugin");
        if (plugin >= 0)
        {
            Simulation.Require(plugin + 1 < arguments.Count, "Missing plugin path.");
            LoadPlugin(arguments[plugin + 1], factories); arguments.RemoveRange(plugin, 2);
        }
        if (arguments.Count == 0) arguments.Add("check");
        Simulation.Require(factories.Select(f => f.Name).Distinct().Count() == factories.Count, "Duplicate factory name.");
        var scenarios = Workloads.Load().Select(Simulation.Prepare).ToArray();
        var groups = scenarios.GroupBy(s => s.Input.Family.StartsWith("census-", StringComparison.Ordinal) ? s.Input.Family : s.Input.Id)
            .Select(g => new Group(g.Key, g.ToArray())).ToArray();
        switch (arguments[0])
        {
            case "check" when arguments.Count == 1:
                Check(scenarios, factories); return 0;
            case "run" when arguments.Count == 2:
                Check(scenarios, factories); Directory.CreateDirectory(arguments[1]);
                File.WriteAllText(Path.Combine(arguments[1], "traces.json"), JsonSerializer.Serialize(scenarios.Select(s => new
                    { s.Input.Id, s.Input.Family, s.Input.Points, s.InitialBounds, s.Steps, s.Expected, s.Simplified }), Json) + "\n");
                return 0;
            case "benchmark" when arguments.Count == 4:
                Check(scenarios, factories); Benchmark(groups, factories, arguments[1], int.Parse(arguments[2]), arguments[3]); return 0;
            case "summarize" when arguments.Count == 2:
                Check(scenarios, factories); Summarize(groups, factories, arguments[1]); return 0;
            default:
                Console.Error.WriteLine("check | run <dir> | benchmark <dir> <run:1..3> <revision> | summarize <dir> [--plugin <local DLL>]");
                return 2;
        }
    }

    private static void LoadPlugin(string path, List<IEdgeIndexFactory> factories)
    {
        path = Path.GetFullPath(path);
        var resolver = new AssemblyDependencyResolver(path);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string? resolved = resolver.ResolveAssemblyToPath(name);
            string fallback = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
            if (resolved is null && File.Exists(fallback)) resolved = fallback;
            return resolved is null ? null : context.LoadFromAssemblyPath(resolved);
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var types = assembly.GetExportedTypes().Where(t => typeof(IEdgeIndexFactory).IsAssignableFrom(t) && !t.IsAbstract).ToArray();
        Simulation.Require(types.Length == 1, "A local plugin must export exactly one factory.");
        factories.Add((IEdgeIndexFactory)Activator.CreateInstance(types[0])!);
    }

    private static void Check(Scenario[] scenarios, List<IEdgeIndexFactory> factories)
    {
        Console.WriteLine($"Fixed-slot contracts: {FixedSlotIndexChecks.Run()} assertions.");
        Console.WriteLine($"Shortcut predicates: {SimulationChecks.Run()} assertions.");
        foreach (var factory in factories)
        {
            foreach (var s in scenarios)
            {
                Simulation.Replay(s, factory, true);
                var actual = Simulation.Capture(s, factory);
                Simulation.Require(actual.Value == s.Expected && actual.Points.SequenceEqual(s.Simplified) && actual.Steps.Length == s.Steps.Length,
                    factory.Name + ": changed simplification result in " + s.Input.Id);
                for (int i = 0; i < s.Steps.Length; i++)
                {
                    var a = actual.Steps[i]; var b = s.Steps[i];
                    Simulation.Require(a.Candidates.SequenceEqual(b.Candidates) &&
                        (a with { Candidates = b.Candidates }) == b, "Different proposal/accepted-removal trace.");
                }
                Workloads.Validate(s.Input.Id, actual.Points);
            }
            Console.WriteLine($"{factory.Name}: exact candidates, decisions, counters and final rings match for {scenarios.Length} workloads.");
        }
        foreach (var g in scenarios.GroupBy(s => s.Input.Family))
            Console.WriteLine($"{g.Key}: {g.Count()} cases, {g.Sum(s => s.Input.Points.Length)} initial, " +
                $"{g.Sum(s => s.Expected.Remaining)} final vertices, {g.Sum(s => s.Expected.Queries - s.Expected.Accepted)} blocked / {g.Sum(s => s.Expected.Queries)} eligible proposals.");
    }

    private static Counters Invoke(Group group, IEdgeIndexFactory factory, string scope)
    {
        Counters total = default;
        foreach (var scenario in group.Cases)
            total = total.Add(scope switch
            {
                "build" => Simulation.Build(scenario, factory),
                "build-and-index-replay" => Simulation.Replay(scenario, factory),
                "complete-simplification" => Simulation.Run(scenario, factory),
                _ => throw new ArgumentException("Unknown scope.")
            });
        return total;
    }

    private static Row Measure(Group group, IEdgeIndexFactory factory, string scope)
    {
        Counters value = Invoke(group, factory, scope); var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 40) Keep(Invoke(group, factory, scope));
        int iterations = 1;
        while (true)
        {
            watch.Restart(); for (int i = 0; i < iterations; i++) Keep(Invoke(group, factory, scope));
            if (watch.ElapsedMilliseconds >= 20 || iterations >= 16384) break;
            iterations *= 2;
        }
        var samples = new Sample[5];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) Keep(Invoke(group, factory, scope));
            long elapsed = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)bytes / iterations);
        }
        Simulation.Require(value == Invoke(group, factory, scope), "Nondeterministic timed output.");
        return new(group.Name, factory.Name, scope, value, samples.Select(s => s.Nanoseconds).Order().ElementAt(2),
            samples.Select(s => s.Bytes).Order().ElementAt(2), samples);
    }

    private static (string Input, string Trace) Hashes(Group[] groups)
    {
        var scenarios = groups.SelectMany(g => g.Cases).ToArray();
        return (Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scenarios.Select(s => s.Input)))),
            Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scenarios.Select(s => new { s.Input.Id, s.Steps, s.Expected, s.Simplified })))));
    }
    private static Dictionary<string,string> Assemblies(List<IEdgeIndexFactory> factories)
    {
        var assemblies = new[] { typeof(Experiment).Assembly, typeof(Point2).Assembly, typeof(Polygon).Assembly }
            .Concat(factories.Select(f => f.GetType().Assembly))
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("RtTools", StringComparison.Ordinal)))
            .Distinct().OrderBy(a => a.GetName().Name, StringComparer.Ordinal);
        return assemblies.ToDictionary(a => a.GetName().Name!, a => Hash(File.ReadAllBytes(a.Location)));
    }
    private static CaseEvidence[] Evidence(Group[] groups) => groups.SelectMany(g => g.Cases)
        .Select(s => new CaseEvidence(s.Input.Id, s.Input.Family, s.Input.Points.Length, s.Simplified.Length,
            s.Steps.Count(p => !p.Accepted), s.Expected)).ToArray();

    private static void Benchmark(Group[] groups, List<IEdgeIndexFactory> factories, string directory, int run, string revision)
    {
        Simulation.Require(run is >= 1 and <= 3, "Run must be 1..3.");
        var rows = new List<Row>();
        int groupOrdinal = 0;
        foreach (Group group in groups)
        {
            int rotation = (groupOrdinal++ + run - 1) % factories.Count;
            foreach (var factory in factories.Skip(rotation).Concat(factories.Take(rotation)))
            foreach (string scope in Scopes) rows.Add(Measure(group, factory, scope));
            Console.WriteLine($"Run {run}: {group.Name} complete.");
        }
        var hashes = Hashes(groups);
        var record = new RunData(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            hashes.Input, hashes.Trace, Assemblies(factories), factories.Select(f => f.Name).ToArray(), Evidence(groups), rows.ToArray());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(record, Json) + "\n");
        GC.KeepAlive(sink);
    }

    private static void Summarize(Group[] groups, List<IEdgeIndexFactory> factories, string directory)
    {
        var runs = Enumerable.Range(1, 3).Select(i => JsonSerializer.Deserialize<RunData>(File.ReadAllText(Path.Combine(directory, $"run-{i}.json")))!).ToArray();
        var hashes = Hashes(groups);
        string Identity(RunData r) => JsonSerializer.Serialize(new { r.Revision, r.Runtime, r.OS, r.Architecture, r.Cpu, r.TieredCompilation,
            r.InputHash, r.TraceHash, r.Assemblies, r.Methods, r.Cases });
        Simulation.Require(runs[0].InputHash == hashes.Input && runs[0].TraceHash == hashes.Trace &&
            JsonSerializer.Serialize(runs[0].Assemblies) == JsonSerializer.Serialize(Assemblies(factories)) &&
            runs[0].Methods.SequenceEqual(factories.Select(f => f.Name)) &&
            JsonSerializer.Serialize(runs[0].Cases) == JsonSerializer.Serialize(Evidence(groups)), "Current build/input/trace differs from measured evidence.");
        var expected = new Dictionary<(string Group, string Method, string Scope), Counters>();
        foreach (var group in groups) foreach (var factory in factories) foreach (string scope in Scopes)
            expected.Add((group.Name, factory.Name, scope), Invoke(group, factory, scope));
        var maps = runs.Select(r => r.Rows.ToDictionary(row => (row.Group, row.Method, row.Scope))).ToArray();
        for (int i = 0; i < runs.Length; i++)
        {
            Simulation.Require(runs[i].Run == i + 1 && Identity(runs[i]) == Identity(runs[0]) && maps[i].Keys.ToHashSet().SetEquals(expected.Keys),
                "Mismatched independent run or matrix.");
            foreach (var row in runs[i].Rows)
                Simulation.Require(row.Value == expected[(row.Group,row.Method,row.Scope)] && row.Samples.Length == 5 &&
                    row.Samples.All(s => s.Iterations > 0 && double.IsFinite(s.Nanoseconds) && s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0) &&
                    row.MedianNanoseconds == row.Samples.Select(s => s.Nanoseconds).Order().ElementAt(2) &&
                    row.MedianBytes == row.Samples.Select(s => s.Bytes).Order().ElementAt(2), "Altered output digest or invalid timed sample/median.");
        }
        var report = new StringBuilder("# Dynamic simplification index results\n\n");
        report.AppendLine($"Source `{runs[0].Revision}`; {runs[0].Utc:yyyy-MM-dd} UTC; {runs[0].Runtime}; {runs[0].OS}; {runs[0].Cpu}; tiering `{runs[0].TieredCompilation}`. Three sequential processes, five batches each.\n");
        report.AppendLine("| Workload | Vertices | Final | Eligible queries | Blocked | Candidate IDs | Predicate calls |\n|---|---:|---:|---:|---:|---:|---:|");
        foreach (var c in runs[0].Cases)
            report.AppendLine($"| {c.Id} | {c.Vertices} | {c.FinalVertices} | {c.Value.Queries} | {c.BlockedProposals} | {c.Value.Candidates} | {c.Value.Predicates} |");
        foreach (string scope in Scopes)
        {
            report.AppendLine($"\n## {scope}\n\n| Group | Method | ms per complete group (min–max) | Managed bytes per group |\n|---|---|---:|---:|");
            foreach (var group in groups) foreach (var factory in factories)
            {
                var values = maps.Select(m => m[(group.Name,factory.Name,scope)]).ToArray();
                report.AppendLine(FormattableString.Invariant($"| {group.Name} | {factory.Name} | {values.Select(v=>v.MedianNanoseconds).Order().ElementAt(1)/1e6:F4} ({values.Min(v=>v.MedianNanoseconds)/1e6:F4}–{values.Max(v=>v.MedianNanoseconds)/1e6:F4}) | {values.Max(v=>v.MedianBytes):F0} |"));
            }
        }
        report.AppendLine("\nFresh index construction is included in both replay and complete-session times; the build row contains only index construction. Replay executes bounds queries and the same recorded accepted updates, with no narrow-phase geometry. Complete sessions also allocate simulation state and execute the scheduler, local distance test and every nonexcluded candidate predicate. No output contours, trace recording, final validity checks or sorting are timed. All methods reproduce exact candidate/decision/final-ring sequences before timing.\n");
        report.AppendLine("All inputs are simple single rings. Local removal distance is not a bound on cumulative error or an area-change guarantee. Every candidate is enumerated; early-exit callback designs were not measured. Private plugin rows include their adapter costs and are locally reproducible only; no private source or binary is distributed. Ranges are descriptive process medians, not confidence intervals. This index experiment does not replace Winding's internal sweep.");
        File.WriteAllText(Path.Combine(directory, "summary.md"), report.ToString());
        Console.WriteLine($"Validated {runs.Sum(r=>r.Rows.Sum(row=>row.Samples.Length))} timing samples with regenerated inputs, traces and scope outputs.");
    }

    private static void Keep(Counters value) => sink = value.Digest ^ (ulong)value.Candidates ^ (ulong)value.Accepted;
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
