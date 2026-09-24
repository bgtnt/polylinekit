using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PolylineKit.Experiments;

internal static partial class ClipperBenchmarks
{
    /// <summary>One fresh process per case: first area call, warm calls and retained array capacity.</summary>
    internal static void ProfileStorage(string directory, string key, int run, string revision)
    {
        if (run is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(run));
        var workload = Workloads().Single(w => $"{w.Family}/{w.VerticesPerPath}/{w.Operation}" == key);
        var method = workload.Methods.Single(m => m.Name == "WindingArea");
        var assembly = typeof(WindingArea).Assembly;
        Type workspaceType = assembly.GetType("PolylineKit.WindingEngine+Workspace", throwOnError: true)!;
        var cache = workspaceType.GetField("cached", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (cache.GetValue(null) != null) throw new InvalidOperationException("Profile requires a process with no earlier winding call.");

        // Prepare counters outside the interval. This is a first AREA call (JIT and workspace
        // allocation included), not process launch, input generation or assembly-load latency.
        _ = Stopwatch.GetTimestamp(); _ = Stopwatch.Frequency; _ = GC.GetAllocatedBytesForCurrentThread();
        long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        long bytes = GC.GetAllocatedBytesForCurrentThread(), time = Stopwatch.GetTimestamp();
        Value value = method.Invoke();
        long elapsed = Stopwatch.GetTimestamp() - time;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        long retainedAfter = GC.GetTotalMemory(forceFullCollection: true);
        Validate(value, workload.Operation);

        // Reflection and payload accounting occur only after first-use counters were read.
        var arrays = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        long payload = 0;
        Visit(cache.GetValue(null)!, "Workspace");
        Type predicates = assembly.GetType("PolylineKit.RobustOrientation", throwOnError: true)!;
        foreach (FieldInfo field in predicates.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            if (field.IsDefined(typeof(ThreadStaticAttribute), false) && field.GetValue(null) is Array array)
                AddArray(array, "Predicates." + field.Name);

        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 60) Consume(method.Invoke());
        int iterations = 1;
        while (true)
        {
            watch.Restart();
            for (int i = 0; i < iterations; i++) Consume(method.Invoke());
            if (watch.ElapsedMilliseconds >= 10 || iterations >= 1_048_576) break;
            iterations *= 2;
        }
        var samples = new Sample[9];
        for (int s = 0; s < samples.Length; s++)
        {
            bytes = GC.GetAllocatedBytesForCurrentThread(); time = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) Consume(method.Invoke());
            long warmElapsed = Stopwatch.GetTimestamp() - time;
            long warmBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            samples[s] = new(iterations, warmElapsed * 1e9 / Stopwatch.Frequency / iterations, (double)warmBytes / iterations);
        }
        if (value != method.Invoke()) throw new InvalidOperationException("First-use and warm values differ.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"profile-{key.Replace('/', '-')}-{run}.json"), JsonSerializer.Serialize(new
        {
            Revision = revision, Run = run, Case = key, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CPU = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            WindingSha256 = Hash(File.ReadAllBytes(assembly.Location)),
            HarnessSha256 = Hash(File.ReadAllBytes(typeof(ClipperBenchmarks).Assembly.Location)),
            InputSha256 = Hash(JsonSerializer.SerializeToUtf8Bytes(new { workload.First, workload.Second })),
            Value = value, FirstCallNanoseconds = elapsed * 1e9 / Stopwatch.Frequency, FirstCallAllocatedBytes = allocated,
            RetainedManagedHeapDelta = retainedAfter - retainedBefore,
            RetainedArrayPayloadBytes = payload, RetainedArrays = arrays,
            WarmMedianNanoseconds = samples.Select(s => s.Nanoseconds).Order().ElementAt(4),
            WarmMedianBytes = samples.Select(s => s.Bytes).Order().ElementAt(4), WarmSamples = samples,
            Scope = "Input/setup excluded. First area call includes JIT; heap delta can include runtime caches. Array payload excludes headers, workspace object fields and native/JIT memory. One fresh process per row."
        }, BenchmarkJson.Options) + "\n");
        GC.KeepAlive(workload);
        Console.WriteLine($"First-use/storage profile {key}, run {run}: {allocated} allocated bytes, {payload} retained array payload bytes.");

        void AddArray(Array array, string name)
        {
            if (!seen.Add(array)) return;
            Type element = array.GetType().GetElementType()!;
            if (!element.IsValueType) throw new InvalidOperationException("Unexpected reference-type workspace array; update payload accounting.");
            int elementBytes = (int)typeof(ClipperBenchmarks).GetMethod(nameof(ElementSize), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(element).Invoke(null, null)!;
            long size = (long)elementBytes * array.LongLength;
            payload += size;
            arrays.Add(new { Name = name, Element = element.FullName, array.Length, ElementBytes = elementBytes, PayloadBytes = size });
        }
        void Visit(object instance, string path)
        {
            if (!seen.Add(instance)) return;
            foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                object? item = field.GetValue(instance);
                if (item is Array array) AddArray(array, path + "." + field.Name);
                else if (item != null && !field.FieldType.IsValueType && field.FieldType.Assembly == assembly)
                    Visit(item, path + "." + field.Name);
            }
        }
    }

    private static int ElementSize<T>() => Unsafe.SizeOf<T>();
}
