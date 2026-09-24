using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

[StructLayout(LayoutKind.Sequential)] readonly record struct Point(double X, double Y);
[StructLayout(LayoutKind.Sequential)] readonly record struct Edge(Point A, Point B, Point Origin);
[StructLayout(LayoutKind.Sequential)] readonly record struct Box(double MinX, double MaxX, double MinY, double MaxY);

static unsafe class Program
{
    [DllImport("native-probe", EntryPoint = "edge_sum", CallingConvention = CallingConvention.Cdecl)]
    private static extern double NativeEdges(Edge* edges, int n);
    [DllImport("native-probe", EntryPoint = "box_pairs", CallingConvention = CallingConvention.Cdecl)]
    private static extern long NativeBoxes(Box* boxes, int n);
    private static double sink;

    static void Main(string[] args)
    {
        bool measure = args.Contains("measure");
        Console.WriteLine(JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            process = RuntimeInformation.ProcessArchitecture.ToString(), avx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported,
            tiered = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), measure }));
        if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported) throw new PlatformNotSupportedException("Native probe is compiled for AVX2.");
        foreach (int n in new[] { 0, 16, 64, 256, 1024, 16384 })
        {
            var random = new Random(7533 + n);
            var edges = new Edge[n];
            for (int i = 0; i < n; i++) {
                double x = (random.NextDouble() - .5) * 1e8, y = (random.NextDouble() - .5) * 1e8;
                edges[i] = new(new(x, y), new(x + random.NextDouble(), y + random.NextDouble()), new(x / 2, y / 2));
            }
            fixed (Edge* p = edges) {
                double managed = ManagedEdges(edges), pointer = PointerEdges(p, n), native = NativeEdges(p, n);
                if (BitConverter.DoubleToInt64Bits(managed) != BitConverter.DoubleToInt64Bits(native) || managed != pointer)
                    throw new Exception($"Edge mismatch n={n}: {managed:R} / {pointer:R} / {native:R}");
                Console.WriteLine(JsonSerializer.Serialize(new { check = "edge_sum", n, value = managed }));
                if (measure) {
                    nint address = (nint)p;
                    Compare("edge_sum", n, new[] { "cs_array", "cs_pointer", "cpp_pinvoke" },
                        new Func<double>[] { () => ManagedEdges(edges), () => PointerEdges((Edge*)address, n), () => NativeEdges((Edge*)address, n) });
                }
            }
        }
        foreach (bool dense in new[] { false, true })
        foreach (int n in new[] { 16, 64, 256, 1024 })
        {
            var random = new Random(9634 + n);
            var boxes = new Box[n];
            for (int i = 0; i < n; i++) {
                double x = i * .1, y = random.NextDouble() * 10;
                boxes[i] = new(x, x + (dense ? n * .1 : .25), y, y + .1 + random.NextDouble());
            }
            fixed (Box* p = boxes) {
                long managed = ManagedBoxes(boxes), pointer = PointerBoxes(p, n), native = NativeBoxes(p, n);
                if (managed != native || managed != pointer) throw new Exception("Box mismatch");
                string name = dense ? "box_pairs_dense" : "box_pairs_sparse";
                Console.WriteLine(JsonSerializer.Serialize(new { check = name, n, value = managed }));
                if (measure) {
                    nint address = (nint)p;
                    Compare(name, n, new[] { "cs_array", "cs_pointer", "cpp_pinvoke" },
                        new Func<double>[] { () => ManagedBoxes(boxes), () => PointerBoxes((Box*)address, n), () => NativeBoxes((Box*)address, n) });
                }
            }
        }
        GC.KeepAlive(sink);
    }

    // Round-robin order changes each sample to reduce phase/thermal bias. Native transition is included.
    static void Compare(string kernel, int n, string[] names, Func<double>[] kernels)
    {
        int iterations = 1;
        for (; iterations < (1 << 22); iterations *= 2) {
            long begin = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = kernels[0]();
            if (Stopwatch.GetElapsedTime(begin).TotalMilliseconds >= 10) break;
        }
        foreach (var kernelCall in kernels) for (int i = 0; i < 100; i++) sink = kernelCall();
        for (int sample = 0; sample < 9; sample++)
            for (int offset = 0; offset < kernels.Length; offset++) {
                int method = (sample + offset) % kernels.Length;
                var run = kernels[method];
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                double value = 0;
                for (int i = 0; i < iterations; i++) value = run();
                long stop = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                sink = value;
                Console.WriteLine(JsonSerializer.Serialize(new { kernel, n, method = names[method], sample, iterations,
                    ns = (stop - start) * (1e9 / Stopwatch.Frequency) / iterations, bytes = (double)allocated / iterations }));
            }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] static double ManagedEdges(Edge[] edges) {
        double sum = 0, error = 0;
        for (int i = 0; i < edges.Length; i++) {
            double term = Cross(edges[i]), next = sum + term;
            error += Math.Abs(sum) >= Math.Abs(term) ? (sum - next) + term : (term - next) + sum;
            sum = next;
        }
        return sum + error;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static double PointerEdges(Edge* edges, int n) {
        double sum = 0, error = 0;
        for (int i = 0; i < n; i++) {
            double term = Cross(edges[i]), next = sum + term;
            error += Math.Abs(sum) >= Math.Abs(term) ? (sum - next) + term : (term - next) + sum;
            sum = next;
        }
        return sum + error;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static double Cross(Edge e) {
        double ax = e.A.X - e.Origin.X, bx = e.B.X - e.Origin.X, ay = e.A.Y - e.Origin.Y, by = e.B.Y - e.Origin.Y;
        double dx = (ax + bx) + (Tail(e.A.X, e.Origin.X, ax) + Tail(e.B.X, e.Origin.X, bx));
        double dy = (ay + by) + (Tail(e.A.Y, e.Origin.Y, ay) + Tail(e.B.Y, e.Origin.Y, by));
        return dx * (e.B.Y - e.A.Y) - dy * (e.B.X - e.A.X);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static double Tail(double x, double y, double difference) {
        double yv = x - difference, xv = difference + yv;
        return (x - xv) + (yv - y);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static long ManagedBoxes(Box[] boxes) {
        long pairs = 0;
        for (int i = 0; i < boxes.Length; i++) {
            Box a = boxes[i];
            for (int j = i + 1; j < boxes.Length && boxes[j].MinX <= a.MaxX; j++) {
                if (boxes[j].MaxY < a.MinY || boxes[j].MinY > a.MaxY) continue;
                pairs++;
            }
        }
        return pairs;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static long PointerBoxes(Box* boxes, int n) {
        long pairs = 0;
        for (int i = 0; i < n; i++) {
            Box a = boxes[i];
            for (int j = i + 1; j < n && boxes[j].MinX <= a.MaxX; j++) {
                if (boxes[j].MaxY < a.MinY || boxes[j].MinY > a.MaxY) continue;
                pairs++;
            }
        }
        return pairs;
    }
}
