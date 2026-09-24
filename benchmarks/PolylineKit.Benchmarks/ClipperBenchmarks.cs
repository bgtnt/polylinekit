using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;

namespace PolylineKit.Experiments;

/// <summary>Explicit operation and preparation contracts for comparisons with pinned Clipper2.</summary>
internal static partial class ClipperBenchmarks
{
    private const double Scale = 1e6;
    private static double sink;
    private readonly record struct Value(double Area, double Union = 0, double Xor = 0, double IoU = 0);
    private sealed record Method(string Name, string Preparation, Func<Value> Invoke);
    private sealed record Workload(string Family, int VerticesPerPath, string Operation, Point2[] First, Point2[] Second, Method[] Methods);
    private sealed record Sample(int Iterations, double Nanoseconds, double Bytes);
    private sealed record Row(string Family, int VerticesPerPath, string Operation, string Method, string Preparation,
        string InputSha256, Value Value, double MedianNanoseconds, double MedianBytes, Sample[] Samples);

    internal static void Run(string directory, int run, string revision)
    {
        if (run is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(run));
        Directory.CreateDirectory(directory);
        var rows = new List<Row>();
        foreach (var w in Workloads())
        {
            string hash = Hash(JsonSerializer.SerializeToUtf8Bytes(new { w.First, w.Second }));
            int offset = (run - 1) % w.Methods.Length;
            foreach (var method in w.Methods.Skip(offset).Concat(w.Methods.Take(offset)))
            {
                Value value = method.Invoke(); Validate(value, w.Operation);
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 60) Consume(method.Invoke());
                int iterations = 1;
                while (true)
                {
                    watch.Restart();
                    for (int i = 0; i < iterations; i++) Consume(method.Invoke());
                    if (watch.ElapsedMilliseconds >= 20 || iterations >= 1_048_576) break;
                    iterations *= 2;
                }
                var samples = new Sample[9];
                for (int s = 0; s < samples.Length; s++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), time = Stopwatch.GetTimestamp();
                    for (int i = 0; i < iterations; i++) Consume(method.Invoke());
                    long elapsed = Stopwatch.GetTimestamp() - time;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    samples[s] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)allocated / iterations);
                }
                if (value != method.Invoke()) throw new InvalidOperationException("Repeated operation changed its result.");
                rows.Add(new(w.Family, w.VerticesPerPath, w.Operation, method.Name, method.Preparation, hash,
                    value, samples.Select(x => x.Nanoseconds).Order().ElementAt(4), samples.Select(x => x.Bytes).Order().ElementAt(4), samples));
            }
            Console.WriteLine($"clipper run {run}: {w.Family}/{w.VerticesPerPath}/{w.Operation}");
        }
        var metadata = new
        {
            Revision = revision, Run = run, Utc = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CPU = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), LogicalProcessors = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            LibrarySha256 = Hash(File.ReadAllBytes(typeof(PolylineComparison).Assembly.Location)),
            WindingSha256 = Hash(File.ReadAllBytes(typeof(WindingArea).Assembly.Location)),
            HarnessSha256 = Hash(File.ReadAllBytes(typeof(ClipperBenchmarks).Assembly.Location)),
            ClipperVersion = typeof(Clipper64).Assembly.GetName().Version!.ToString(),
            ClipperSha256 = Hash(File.ReadAllBytes(typeof(Clipper64).Assembly.Location)),
            CoordinateScale = Scale, Measurements = rows
        };
        File.WriteAllText(Path.Combine(directory, $"run-{run}.json"), JsonSerializer.Serialize(metadata, BenchmarkJson.Options) + "\n");
        File.WriteAllText(Path.Combine(directory, "inputs.json"), JsonSerializer.Serialize(Workloads().Select(w => new
            { w.Family, w.VerticesPerPath, w.Operation, w.First, w.Second }), BenchmarkJson.Options) + "\n");
        GC.KeepAlive(sink);
    }

    internal static void Smoke()
    {
        int calls = 0;
        foreach (var w in Workloads()) foreach (var m in w.Methods)
        {
            var value = m.Invoke(); Validate(value, w.Operation);
            if (value != m.Invoke()) throw new InvalidOperationException($"Unstable smoke result: {w.Family}/{m.Name}");
            calls++;
        }
        // Analytic expectations are for the supplied binary64 coordinates, independent of clipping.
        Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        Point2[] shifted = square.Select(p => new Point2(p.X + 1, p.Y)).ToArray();
        var overlap = WindingArea.FilledRegions(square, shifted);
        if (overlap.IntersectionArea != 2 || overlap.UnionArea != 6 || overlap.SymmetricDifferenceArea != 4)
            throw new InvalidOperationException("Analytic overlap smoke failed.");
        foreach (string operation in new[] { "overlap-metrics", "xor-only", "simple-iou" })
        foreach (Method method in Regions(square, shifted, operation))
        {
            Value actual = method.Invoke(); Validate(actual, operation);
            Value expected = operation switch { "xor-only" => new(4), "simple-iou" => new(1.0 / 3), _ => new(2, 6, 4, 1.0 / 3) };
            if (Math.Abs(actual.Area - expected.Area) > 2e-15 || Math.Abs(actual.Union - expected.Union) > 2e-15
                || Math.Abs(actual.Xor - expected.Xor) > 2e-15 || Math.Abs(actual.IoU - expected.IoU) > 2e-15)
                throw new InvalidOperationException($"Analytic method smoke failed: {operation}/{method.Name}");
        }
        foreach (double length in new[] { 1e4, 1e8, 1e12, 1e16 })
        {
            Point2[] t = [new(0,0), new(length,length), new(2*length, Math.BitIncrement(2*length))];
            double exact = length * (Math.BitIncrement(2*length) - 2*length) / 2;
            if (WindingArea.ClosedPath(t).NonZero != exact) throw new InvalidOperationException("Thin triangle accuracy smoke failed.");
        }
        Console.WriteLine($"Clipper comparison smoke: {calls} method calls, analytic overlap and four exact thin triangles; no timing.");
    }

    internal static void Summarize(string directory)
    {
        var runs = Enumerable.Range(1, 3).Select(i => JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"run-{i}.json"))).RootElement).ToArray();
        string[] identity = ["Revision", "Runtime", "OS", "Architecture", "CPU", "LogicalProcessors", "TieredCompilation",
            "LibrarySha256", "WindingSha256", "HarnessSha256", "ClipperVersion", "ClipperSha256", "CoordinateScale"];
        for (int i = 0; i < 3; i++)
        {
            if (runs[i].GetProperty("Run").GetInt32() != i + 1) throw new InvalidDataException("Run order differs.");
            foreach (string key in identity) if (runs[i].GetProperty(key).GetRawText() != runs[0].GetProperty(key).GetRawText())
                throw new InvalidDataException($"Measurement identity differs: {key}");
        }
        var maps = runs.Select(r => r.GetProperty("Measurements").EnumerateArray().ToDictionary(Key, x => x)).ToArray();
        if (maps[0].Count == 0 || maps.Any(m => !m.Keys.Order().SequenceEqual(maps[0].Keys.Order())))
            throw new InvalidDataException("Incomplete method matrix.");
        var summary = new List<object>();
        var md = new StringBuilder("# Winding versus Clipper2\n\nMedian of three independent process medians, nine batches each. Time includes the declared operation; preparation differs by method. Repeated/preloaded rows are a separate amortized-input scenario. Coordinate grid size is not an area-error bound. All values are machine/workload specific.\n\n");
        md.AppendLine($"Source: `{runs[0].GetProperty("Revision").GetString()}`; {runs[0].GetProperty("Runtime").GetString()}; {runs[0].GetProperty("OS").GetString()}; {runs[0].GetProperty("CPU").GetString()}.\n");
        md.AppendLine("| Family | n per path | Operation | Method | us (min–max) | B/op | Method / winding | Absolute deltas: primary / union / XOR / IoU |");
        md.AppendLine("|---|---:|---|---|---:|---:|---:|---:|");
        foreach (string key in maps[0].Keys.Order(StringComparer.Ordinal))
        {
            var row = maps[0][key];
            foreach (var map in maps)
            {
                var item = map[key];
                foreach (string field in new[] { "InputSha256", "Preparation", "Value" })
                    if (item.GetProperty(field).GetRawText() != row.GetProperty(field).GetRawText()) throw new InvalidDataException($"Input or value differs: {key}");
                var samples = item.GetProperty("Samples").EnumerateArray().ToArray();
                if (samples.Length != 9 || samples.Select(x => x.GetProperty("Nanoseconds").GetDouble()).Order().ElementAt(4) != item.GetProperty("MedianNanoseconds").GetDouble()
                    || samples.Select(x => x.GetProperty("Bytes").GetDouble()).Order().ElementAt(4) != item.GetProperty("MedianBytes").GetDouble())
                    throw new InvalidDataException("Sample medians differ.");
            }
            double[] times = maps.Select(m => m[key].GetProperty("MedianNanoseconds").GetDouble() / 1000).Order().ToArray();
            double bytes = maps.Select(m => m[key].GetProperty("MedianBytes").GetDouble()).Order().ElementAt(1);
            string windingKey = key[..key.LastIndexOf('|')] + "|WindingArea";
            double windingTime = maps.Select(m => m[windingKey].GetProperty("MedianNanoseconds").GetDouble() / 1000).Order().ElementAt(1);
            double[] deltas = new[] { "Area", "Union", "Xor", "IoU" }.Select(field => Math.Abs(row.GetProperty("Value").GetProperty(field).GetDouble() - maps[0][windingKey].GetProperty("Value").GetProperty(field).GetDouble())).ToArray();
            double? ratio = row.GetProperty("Method").GetString() == "WindingArea" ? null : times[1] / windingTime;
            summary.Add(new { Key = key, MedianUs = times[1], MinUs = times[0], MaxUs = times[2], Bytes = bytes, Ratio = ratio, AbsoluteMetricDeltas = deltas });
            string ratioText = ratio?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
            md.AppendLine(FormattableString.Invariant($"| {row.GetProperty("Family").GetString()} | {row.GetProperty("VerticesPerPath").GetInt32()} | {row.GetProperty("Operation").GetString()} | {row.GetProperty("Method").GetString()} | {times[1]:F2} ({times[0]:F2}–{times[2]:F2}) | {bytes:F0} | {ratioText} | {deltas[0]:G3}/{deltas[1]:G3}/{deltas[2]:G3}/{deltas[3]:G3} |"));
        }
        File.WriteAllText(Path.Combine(directory, "summary.md"), md.ToString());
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, BenchmarkJson.Options) + "\n");
        static string Key(JsonElement e) => $"{e.GetProperty("Family").GetString()}|{e.GetProperty("VerticesPerPath").GetInt32()}|{e.GetProperty("Operation").GetString()}|{e.GetProperty("Method").GetString()}";
    }

    private static void Validate(Value v, string operation)
    { if (!double.IsFinite(v.Area) || !double.IsFinite(v.Union) || !double.IsFinite(v.Xor) || !double.IsFinite(v.IoU)) throw new InvalidOperationException("Nonfinite comparison result.");
        if (v.Area < 0 || v.Union < 0 || v.Xor < 0 || v.IoU < 0 || v.IoU > 1 || (operation == "simple-iou" && v.Area > 1)) throw new InvalidOperationException("Invalid area/IoU range."); }
    private static void Consume(Value v) => sink = v.Area + v.Union + v.Xor + v.IoU;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static Paths64 Ints(Point2[] p) => new() { new Path64(p.Select(q => new Point64(q.X * Scale, q.Y * Scale))) };
    private static PathsD Doubles(Point2[] p) => new() { new PathD(p.Select(q => new PointD(q.X, q.Y))) };
    private static double Execute(Clipper64 engine, ClipType operation, Paths64 solution, Paths64 open)
    { if (!engine.Execute(operation, FillRule.NonZero, solution, open)) throw new InvalidOperationException("Clipper execution failed."); return Clipper.Area(solution) / (Scale * Scale); }
    private static Value Metrics(double intersection, double union) => new(intersection, union, union - intersection, intersection / union);

    private static Method[] Closed(Point2[] walk, Func<Value> winding, Func<Value>? wrapper)
    {
        var ints = Ints(walk); var doubles = Doubles(walk);
        var reused = new Clipper64(); var prepared = new Clipper64(); prepared.AddSubject(ints);
        var solution = new Paths64(); var preparedSolution = new Paths64();
        var open = new Paths64(); var preparedOpen = new Paths64();
        var list = new List<Method>
        {
            new("WindingArea", "input points only; all engine preparation timed", winding),
            new("Clipper64 static", "integer conversion outside; engine preparation timed", () => new(Clipper.Area(Clipper.Union(ints, FillRule.NonZero))/(Scale*Scale))),
            new("Clipper64 reused", "integer conversion outside; Clear/Add/Execute timed", () => { reused.Clear(); reused.AddSubject(ints); return new(Execute(reused, ClipType.Union, solution, open)); }),
            new("Clipper64 preloaded", "integer conversion and Add outside; repeated Execute timed", () => new(Execute(prepared, ClipType.Union, preparedSolution, preparedOpen))),
            new("ClipperD precision6", "PathD conversion outside; grid conversion/engine timed", () => new(Clipper.Area(Clipper.Union(doubles, new PathsD(), FillRule.NonZero, 6))))
        };
        if (wrapper != null) list.Add(new("PolylineKit wrapper", "input points only; conversion and engine timed", wrapper));
        return list.ToArray();
    }

    private static Method[] Regions(Point2[] a, Point2[] b, string operation)
    {
        var ia = Ints(a); var ib = Ints(b); var da = Doubles(a); var db = Doubles(b);
        var reused = new Clipper64(); var prepared = new Clipper64(); prepared.AddSubject(ia); prepared.AddClip(ib);
        var solution = new Paths64(); var preparedSolution = new Paths64();
        var open = new Paths64(); var preparedOpen = new Paths64();
        Value Region64(Clipper64 engine, Paths64 output, Paths64 openOutput)
        {
            if (operation == "xor-only") return new(Execute(engine, ClipType.Xor, output, openOutput));
            double i = Execute(engine, ClipType.Intersection, output, openOutput);
            if (operation == "simple-iou") return new(i / ((Math.Abs(Clipper.Area(ia)) + Math.Abs(Clipper.Area(ib))) / (Scale * Scale) - i));
            return Metrics(i, Execute(engine, ClipType.Union, output, openOutput));
        }
        Value Winding()
        {
            var r = WindingArea.FilledRegions(a,b);
            return operation switch { "xor-only" => new(r.SymmetricDifferenceArea), "simple-iou" => new(r.IntersectionOverUnion!.Value), _ => new(r.IntersectionArea, r.UnionArea, r.SymmetricDifferenceArea, r.IntersectionOverUnion!.Value) };
        }
        Value Static64()
        {
            if (operation == "xor-only") return new(Clipper.Area(Clipper.Xor(ia,ib,FillRule.NonZero))/(Scale*Scale));
            double i = Clipper.Area(Clipper.Intersect(ia,ib,FillRule.NonZero))/(Scale*Scale);
            return operation == "simple-iou" ? new(i / ((Math.Abs(Clipper.Area(ia))+Math.Abs(Clipper.Area(ib)))/(Scale*Scale)-i))
                : Metrics(i,Clipper.Area(Clipper.Union(ia,ib,FillRule.NonZero))/(Scale*Scale));
        }
        Value DoubleClip()
        {
            if (operation == "xor-only") return new(Clipper.Area(Clipper.Xor(da,db,FillRule.NonZero,6)));
            double i = Clipper.Area(Clipper.Intersect(da,db,FillRule.NonZero,6));
            // Simple own areas use the same quantized geometry as the intersection.
            return operation == "simple-iou" ? new(i / ((Math.Abs(Clipper.Area(ia))+Math.Abs(Clipper.Area(ib)))/(Scale*Scale)-i))
                : Metrics(i,Clipper.Area(Clipper.Union(da,db,FillRule.NonZero,6)));
        }
        return [new("WindingArea", "all engine preparation/metrics timed", Winding),
            new("Clipper64 static", "integer conversion outside; simple own areas timed", Static64),
            new("Clipper64 reused", "conversion outside; Clear/Add/Execute and simple own areas timed", () => { reused.Clear(); reused.AddSubject(ia); reused.AddClip(ib); return Region64(reused,solution,open); }),
            new("Clipper64 preloaded", "conversion/Add outside; Execute and simple own areas timed", () => Region64(prepared,preparedSolution,preparedOpen)),
            new("ClipperD precision6", "PathD/own-area integer conversion outside; operations/own areas timed", DoubleClip),
            new("PolylineKit wrapper", "input points only; all preparation timed", () => {
                if (operation == "xor-only") return new(PolylineComparison.FilledRegionDifference(a,b).RawArea);
                var r = PolylineComparison.FilledRegionOverlap(a,b);
                return operation == "simple-iou" ? new(r.IntersectionOverUnion!.Value) : new(r.UnionArea-r.SymmetricDifferenceArea,r.UnionArea,r.SymmetricDifferenceArea,r.IntersectionOverUnion!.Value);
            })];
    }

    private static IEnumerable<Workload> Workloads()
    {
        foreach (int n in new[] {16,64,256,1024})
        {
            Point2[] a = Enumerable.Range(0,n).Select(i => { double t=(double)i/(n-1); return new Point2(3*t+.5*Math.Sin(9*t),Math.Sin(5*t)+t); }).ToArray();
            Point2[] b = Enumerable.Range(0,n).Select(i => { double t=(double)i/(n-1); return new Point2(3*t+.5*Math.Sin(9*t)+.05*Math.Sin(31*t),Math.Sin(5*t)+t+.08*Math.Cos(23*t)); }).ToArray();
            yield return Bridged("similar-strokes",n,a,b);
            Random random = new(n);
            Point2[] Walk() { double x=0,y=0; return Enumerable.Range(0,n).Select(_=>new Point2(x+=random.NextDouble()*2-1,y+=random.NextDouble()*2-1)).ToArray(); }
            yield return Bridged("random-walks",n,Walk(),Walk());
            Point2[] ring=Enumerable.Range(0,n).Select(i=>{double t=14*Math.PI*i/n,r=1+.35*Math.Sin(5.5*t);return new Point2(r*Math.Cos(t),r*Math.Sin(t));}).ToArray();
            yield return new("tangled-ring",n,"closed-nonzero",ring,[],Closed(ring,()=>new(WindingArea.ClosedPath(ring).NonZero),null));
            yield return new("tangled-ring", n, "closed-absolute-winding", ring, [],
                [new("WindingArea", "all preparation timed; no equivalent Clipper fill-area operation",
                    () => new(WindingArea.ClosedPath(ring).AbsoluteWinding))]);
            Point2[] Star(double phase,double dx)=>Enumerable.Range(0,n).Select(i=>{double t=2*Math.PI*i/n,r=1+.3*Math.Sin(5*t+phase);return new Point2(r*Math.Cos(t)+dx,r*Math.Sin(t));}).ToArray();
            var noise=new Random(7*n);
            Point2[] Blob(double cx,double cy) {double phase=noise.NextDouble()*6;return Enumerable.Range(0,n).Select(i=>{double t=2*Math.PI*i/n,r=1+.08*Math.Sin(3*t+phase)+.02*(noise.NextDouble()-.5);return new Point2(cx+r*Math.Cos(t),cy+r*Math.Sin(t));}).ToArray();}
            foreach(var pair in new[]{("star-regions",Star(0,0),Star(.7,.25)),("blobs",Blob(0,0),Blob(.2,.1))})
                foreach(string operation in new[]{"overlap-metrics","xor-only","simple-iou"})
                    yield return new(pair.Item1,n,operation,pair.Item2,pair.Item3,Regions(pair.Item2,pair.Item3,operation));
        }
        foreach(int n in new[]{64,256})
        {
            Random random=new(1000+n);
            Point2[] Grid()=>Enumerable.Range(0,n).Select(_=>new Point2(random.Next(0,8),random.Next(0,8))).ToArray();
            yield return Bridged("degenerate-grid",n,Grid(),Grid());
        }
        foreach(int n in new[]{1024,4096})
        {
            Point2[] star=Enumerable.Range(0,n).Select(i=>{double t=2*Math.PI*i/n,r=i%2==0?1:.15;return new Point2(r*Math.Cos(t),r*Math.Sin(t));}).ToArray();
            yield return new("simple-spiky-star",n,"closed-nonzero",star,[],Closed(star,()=>new(WindingArea.ClosedPath(star).NonZero),null));
        }
    }
    private static Workload Bridged(string family,int n,Point2[] a,Point2[] b) => new(family,n,"bridged-nonzero",a,b,
        Closed([..a,..b.Reverse()],()=>new(WindingArea.EndpointBridged(a,b).NonZero),()=>new(PolylineComparison.EndpointBridgedArea(a,b).RawArea)));
}
