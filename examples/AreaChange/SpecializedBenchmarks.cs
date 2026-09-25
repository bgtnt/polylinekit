using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PolylineKit;

internal static partial class AreaChange
{
    // These are illustrative application policies, not fitted classification thresholds.
    private static readonly double[] ChangeThresholds = [.001, .01, .03];
    private sealed record SpecialCase(string Key, Point2[] Original, Point2[] Simplified, bool Nested);
    private readonly record struct SpecialValue(double First, double Second, double Intersection, double Union,
        double Xor, bool? Accepted = null, bool Fallback = false);
    private sealed record SpecialMethod(string Key, string Method, string Scope, Func<SpecialValue> Invoke);
    private sealed record SpecialMeasurement(string Key, string Method, string Scope, SpecialValue Value,
        double MedianNanoseconds, double MedianBytes, Sample[] Samples);
    private sealed record BoundEvidence(string Key, SpecializedArea.AreaBounds Bounds, double ActualXor,
        double? ActualJaccard, string[] Decisions);
    private sealed record SpecialRun(int Run, string Revision, DateTimeOffset Utc, string Runtime, string OS,
        string Architecture, string? CPU, string? TieredCompilation, string DataSha256, string InputSha256,
        string WindingSha256, string ClipperSha256, string HarnessSha256,
        BoundEvidence[] Bounds, SpecialMeasurement[] Measurements);

    private static SpecialCase[] SpecialCases()
    {
        var cases = new List<SpecialCase>();
        // Integer points on y=x^2 are strictly convex in this order. Replacing chains by chords
        // gives an inner polygon. This guarantee comes from construction, not the area sums.
        foreach (int n in new[] { 16, 64, 256, 1024, 4096 })
        {
            Point2[] p = Enumerable.Range(0, n).Select(i => new Point2(i, (double)i * i)).ToArray();
            Point2[] q = p.Where((_, i) => i % 4 == 0 || i == n - 1).ToArray();
            cases.Add(new($"nested-{n}", p, q, true));
        }
        // Separated triangles alternate above/below y=10. The simple x-monotone top and
        // bottom y=0 give simple polygons. Pockets have disjoint interiors; neither region
        // contains the other. All vertices and expected half-integer areas are exact doubles.
        foreach (int k in new[] { 4, 16, 64, 256 })
        {
            var p = new List<Point2> { new(0, 0), new(3 * k, 0), new(3 * k, 10) };
            var q = new List<Point2>(p);
            for (int i = k - 1; i >= 0; i--)
            {
                p.Add(new(3 * i + 2, 10)); q.Add(p[^1]);
                p.Add(new(3 * i + 1, 10 + (i % 2 == 0 ? 1 : -1)));
                p.Add(new(3 * i, 10)); q.Add(p[^1]);
            }
            cases.Add(new($"pockets-{k}", p.ToArray(), q.ToArray(), false));
        }
        return cases.ToArray();
    }

    private static SpecialValue RegionValue(Point2[] p, Point2[] q)
    {
        var r = WindingArea.FilledRegions(p, q);
        return new(r.FirstArea, r.SecondArea, r.IntersectionArea, r.UnionArea, r.SymmetricDifferenceArea);
    }

    private static SpecialValue CheapValue(SpecialCase c, int[] indices)
    {
        SpecializedArea.AreaValues r;
        bool valid = c.Nested ? SpecializedArea.TryNestedSimple(c.Original, c.Simplified, out r)
            : SpecializedArea.TryDisjointSimplePockets(c.Original, c.Simplified, indices, out r);
        if (!valid) throw new InvalidDataException($"Specialized input invalid: {c.Key}");
        return new(r.First, r.Second, r.Intersection, r.Union, r.Xor);
    }

    // The ordinary translated, compensated shoelace baseline has only two products
    // per triangle. It does not provide a rigorous error bound or retain product tails.
    // The exact integer fixtures below keep its intermediate arithmetic representable.
    private static double OrdinaryArea(Point2[] path, int start, int steps)
    {
        Point2 origin = path[start];
        double sum = 0, correction = 0;
        for (int k = 1; k < steps; k++)
        {
            Point2 a = path[(start + k) % path.Length], b = path[(start + k + 1) % path.Length];
            double term = (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
            double adjusted = term - correction, total = sum + adjusted;
            correction = (total - sum) - adjusted;
            sum = total;
        }
        return Math.Abs(sum * .5);
    }

    private static SpecialValue OrdinaryValue(SpecialCase c, int[] indices)
    {
        ValidateOrdinary(c.Original); ValidateOrdinary(c.Simplified);
        double a = OrdinaryArea(c.Original, 0, c.Original.Length - 1);
        double b = OrdinaryArea(c.Simplified, 0, c.Simplified.Length - 1);
        if (c.Nested) return new(a, b, Math.Min(a, b), Math.Max(a, b), Math.Abs(a - b));
        double d = 0, correction = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            int start = indices[i], end = indices[(i + 1) % indices.Length];
            int steps = end >= start ? end - start : c.Original.Length - start + end;
            double adjusted = OrdinaryArea(c.Original, start, steps) - correction, total = d + adjusted;
            correction = (total - d) - adjusted;
            d = total;
        }
        return new(a, b, (a + b - d) * .5, (a + b + d) * .5, d);
    }

    private static void ValidateOrdinary(Point2[] path)
    {
        // Bounds validation remains inside the call, as in the review baseline.
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length < 3) throw new ArgumentException("At least three vertices required.");
        foreach (Point2 p in path)
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1e100 || Math.Abs(p.Y) > 1e100)
                throw new ArgumentException("Invalid coordinate.");
    }

    private static SpecialValue DirectDecision(Pair pair, double threshold)
    {
        double? value = Winding(pair.Shape.Points, pair.Simplified).Jaccard;
        return new(0, 0, 0, 0, 0, value.HasValue ? value <= threshold : null);
    }

    private static bool Certify(Pair pair, out int[] indices)
    {
        indices = [];
        return SimplificationCertificate.IsSimple(pair.Shape.Points) &&
            SimplificationCertificate.IsSimple(pair.Simplified) &&
            SpecializedArea.TryMapRetained(pair.Shape.Points, pair.Simplified, out indices);
    }

    private static SpecialValue FilteredDecision(Pair pair, int[] preparedIndices, double threshold, bool certify)
    {
        int[] indices = preparedIndices;
        bool usable = !certify || Certify(pair, out indices);
        if (usable && SpecializedArea.TryBounds(pair.Shape.Points, pair.Simplified, indices, out var bounds))
        {
            var decision = SpecializedArea.Decide(bounds, threshold);
            if (decision != SpecializedArea.Decision.Unresolved)
                return new(0, 0, 0, 0, 0, decision == SpecializedArea.Decision.Accept);
        }
        return DirectDecision(pair, threshold) with { Fallback = true };
    }

    private static void CheckSpecialized(Pair[] pairs)
    {
        int checks = SimplificationCertificate.CheckControls() + SpecializedAreaChecks.Run();
        foreach (Point2[] original in new Point2[][]
        {
            [new(0, 0), new(2, 2), new(0, 2), new(2, 0)],
            [new(0, 0), new(1, 0), new(2, 0)]
        })
        {
            var pair = new Pair(new Shape("fallback", "Rejected certificate", original), 0, original);
            var result = FilteredDecision(pair, [], .01, true);
            Require(result.Fallback && result.Accepted == DirectDecision(pair, .01).Accepted,
                "Uncertified or degenerate input must fall back, retaining null for zero union.");
            checks++;
        }
        foreach (var c in SpecialCases())
        {
            Require(SpecializedArea.TryMapRetained(c.Original, c.Simplified, out var indices), "Synthetic map failed.");
            SpecialValue cheap = CheapValue(c, indices), full = RegionValue(c.Original, c.Simplified);
            Require(OrdinaryValue(c, indices) == cheap, "Ordinary fixture estimate differs.");
            Require(Math.Abs(cheap.Xor - full.Xor) <= 1e-8 * Math.Max(1, cheap.Xor) &&
                Math.Abs(cheap.Union - full.Union) <= 1e-12 * Math.Max(1, cheap.Union) &&
                Math.Abs(cheap.First - full.First) <= 1e-12 * Math.Max(1, cheap.First) &&
                Math.Abs(cheap.Second - full.Second) <= 1e-12 * Math.Max(1, cheap.Second) &&
                Math.Abs(cheap.Intersection - full.Intersection) <= 1e-12 * Math.Max(1, cheap.Intersection), "Specialized area differs.");
            Require(SpecializedArea.TryBounds(c.Original, c.Simplified, indices, out var bounds) &&
                bounds.Xor.Lower <= cheap.Xor && cheap.Xor <= bounds.Xor.Upper, "Synthetic bounds exclude area.");
            if (c.Nested)
            {
                double n = c.Original.Length, last = n - 1;
                double area = last * (last * last - 1) / 6, difference = 2.5 * n - 6;
                var expected = new SpecialValue(area, area - difference, area - difference, area, difference);
                Require(cheap == expected && full == expected, "Integer parabola oracle differs.");
            }
            else
            {
                int k = (c.Original.Length - 3) / 3;
                var expected = new SpecialValue(30 * k, 30 * k, 29.5 * k, 30.5 * k, k);
                Require(cheap == expected && full == expected, "Disjoint triangle oracle differs.");
            }
            checks += 4;
        }
        foreach (var pair in pairs)
        {
            Require(Certify(pair, out int[] indices), $"Simple/subsequence certificate failed: {pair.Key}");
            var full = Winding(pair.Shape.Points, pair.Simplified);
            Require(SpecializedArea.TryBounds(pair.Shape.Points, pair.Simplified, indices, out var bounds) &&
                bounds.Xor.Lower <= full.Xor && full.Xor <= bounds.Xor.Upper &&
                bounds.Jaccard.Lower <= full.Jaccard && full.Jaccard <= bounds.Jaccard.Upper,
                $"Bound check differs from Winding: {pair.Key}");
            foreach (double t in ChangeThresholds)
            {
                Require(FilteredDecision(pair, indices, t, false).Accepted == DirectDecision(pair, t).Accepted &&
                    FilteredDecision(pair, indices, t, true).Accepted == DirectDecision(pair, t).Accepted,
                    $"Filter decision differs: {pair.Key}/{t:R}");
                checks++;
            }
        }
        Console.WriteLine($"Specialized area: {checks} construction/certificate/decision checks passed.");
    }

    private static List<SpecialMethod> SpecialMethods(Pair[] pairs)
    {
        var methods = new List<SpecialMethod>();
        foreach (var c in SpecialCases())
        {
            Require(SpecializedArea.TryMapRetained(c.Original, c.Simplified, out int[] indices), "Synthetic map failed.");
            methods.Add(new(c.Key, "winding", "five-areas-guarantee-supplied", () => RegionValue(c.Original, c.Simplified)));
            methods.Add(new(c.Key, c.Nested ? "nested-shoelace" : "local-pockets", "five-areas-guarantee-supplied", () => OrdinaryValue(c, indices)));
            methods.Add(new(c.Key, "compensated-products", "five-areas-guarantee-supplied", () => CheapValue(c, indices)));
        }
        foreach (var pair in pairs)
        {
            Require(Certify(pair, out int[] indices), "Frozen pair certificate failed.");
            methods.Add(new(pair.Key, "certify-simple-and-map", "preparation", () => new(0, 0, 0, 0, 0, Certify(pair, out _))));
            foreach (double threshold in ChangeThresholds)
            {
                string key = pair.Key + "/" + threshold.ToString("R", CultureInfo.InvariantCulture);
                methods.Add(new(key, "winding", "decision-guarantee-supplied", () => DirectDecision(pair, threshold)));
                methods.Add(new(key, "bounds-then-winding", "decision-guarantee-supplied", () => FilteredDecision(pair, indices, threshold, false)));
                // This mode charges certification and mapping on every input. Winding itself
                // accepts arbitrary closed walks and does not require these extra predicates.
                if (threshold == .01)
                {
                    methods.Add(new(key, "winding", "decision-uncertified-input", () => DirectDecision(pair, threshold)));
                    methods.Add(new(key, "certify-bounds-then-winding", "decision-uncertified-input", () => FilteredDecision(pair, [], threshold, true)));
                }
            }
        }
        return methods;
    }

    private static BoundEvidence[] BoundsEvidence(Pair[] pairs) => pairs.Select(pair =>
    {
        Require(Certify(pair, out var map) && SpecializedArea.TryBounds(pair.Shape.Points, pair.Simplified, map, out _), "Missing bounds.");
        SpecializedArea.TryBounds(pair.Shape.Points, pair.Simplified, map, out var bounds);
        var actual = Winding(pair.Shape.Points, pair.Simplified);
        return new BoundEvidence(pair.Key, bounds, actual.Xor, actual.Jaccard,
            ChangeThresholds.Select(t => SpecializedArea.Decide(bounds, t).ToString()).ToArray());
    }).ToArray();

    private static string SpecialInputHash(Pair[] pairs) => Hash(Encoding.UTF8.GetBytes(PairJson(pairs) +
        JsonSerializer.Serialize(SpecialCases()) + JsonSerializer.Serialize(ChangeThresholds)));

    private static void KeepSpecial(SpecialValue v) => sink = v.First + v.Second + v.Intersection + v.Union + v.Xor +
        (v.Union > 0 ? v.Xor / v.Union : 0) + (v.Accepted == true ? 1 : 0) + (v.Fallback ? 1 : 0);

    private static void BenchmarkSpecialized(string directory, int run, string revision)
    {
        if (run is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(run));
        var pairs = LoadPairs(); CheckControls(); CheckSpecialized(pairs);
        var rows = new List<SpecialMeasurement>();
        // Same warmup, calibrated Stopwatch batches and per-thread allocation accounting
        // as the existing AreaChange runner. No timing is performed by the CI checks.
        foreach (var group in SpecialMethods(pairs).GroupBy(m => (m.Key, m.Scope)))
        foreach (var method in group.Skip((run - 1) % group.Count()).Concat(group.Take((run - 1) % group.Count())))
        {
            var value = method.Invoke(); var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 40) KeepSpecial(method.Invoke());
            int iterations = 1;
            while (true)
            {
                watch.Restart();
                for (int i = 0; i < iterations; i++) KeepSpecial(method.Invoke());
                if (watch.ElapsedMilliseconds >= 15 || iterations >= 131072) break;
                iterations *= 2;
            }
            var samples = new Sample[5];
            for (int batch = 0; batch < samples.Length; batch++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) KeepSpecial(method.Invoke());
                long elapsed = Stopwatch.GetTimestamp() - start;
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)bytes / iterations);
            }
            Require(value == method.Invoke(), "Unstable specialized value.");
            rows.Add(new(method.Key, method.Method, method.Scope, value,
                samples.Select(s => s.Nanoseconds).Order().ElementAt(2), samples.Select(s => s.Bytes).Order().ElementAt(2), samples));
        }
        var record = new SpecialRun(run, revision, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "manifest.json"))), SpecialInputHash(pairs),
            Hash(File.ReadAllBytes(typeof(WindingArea).Assembly.Location)), Hash(File.ReadAllBytes(typeof(Clipper2Lib.Clipper).Assembly.Location)),
            Hash(File.ReadAllBytes(typeof(AreaChange).Assembly.Location)), BoundsEvidence(pairs), rows.ToArray());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"specialized-{run}.json"), JsonSerializer.Serialize(record, Json) + "\n");
        Console.WriteLine($"Specialized process {run}: {rows.Count} measurements written.");
        GC.KeepAlive(sink);
    }

    private static void SummarizeSpecialized(string directory)
    {
        var runs = Enumerable.Range(1, 3).Select(i => JsonSerializer.Deserialize<SpecialRun>(
            File.ReadAllText(Path.Combine(directory, $"specialized-{i}.json")))!).ToArray();
        string Identity(SpecialRun r) => JsonSerializer.Serialize(new { r.Revision, r.Runtime, r.OS, r.Architecture,
            r.CPU, r.TieredCompilation, r.DataSha256, r.InputSha256, r.WindingSha256, r.ClipperSha256, r.HarnessSha256, r.Bounds });
        var pairs = LoadPairs(); var methods = SpecialMethods(pairs);
        var current = methods.ToDictionary(m => (m.Key, m.Method, m.Scope), m => m.Invoke());
        Require(runs[0].InputSha256 == SpecialInputHash(pairs) &&
            runs[0].DataSha256 == Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "manifest.json"))) &&
            runs[0].WindingSha256 == Hash(File.ReadAllBytes(typeof(WindingArea).Assembly.Location)) &&
            runs[0].ClipperSha256 == Hash(File.ReadAllBytes(typeof(Clipper2Lib.Clipper).Assembly.Location)) &&
            runs[0].HarnessSha256 == Hash(File.ReadAllBytes(typeof(AreaChange).Assembly.Location)) &&
            JsonSerializer.Serialize(runs[0].Bounds) == JsonSerializer.Serialize(BoundsEvidence(pairs)), "Measured assemblies/inputs differ.");
        for (int i = 0; i < runs.Length; i++)
        {
            Require(runs[i].Run == i + 1 && Identity(runs[i]) == Identity(runs[0]), "Mismatched process identities.");
            var map = runs[i].Measurements.ToDictionary(m => (m.Key, m.Method, m.Scope));
            Require(map.Count == current.Count && map.Keys.ToHashSet().SetEquals(current.Keys), "Incomplete workload matrix.");
            foreach (var (key, row) in map)
            {
                Require(row.Value == current[key] && row.Samples.Length == 5 && row.Samples.All(s =>
                    s.Iterations > 0 && double.IsFinite(s.Nanoseconds) && s.Nanoseconds > 0 && double.IsFinite(s.Bytes) && s.Bytes >= 0), "Invalid raw sample/value.");
                Require(row.MedianNanoseconds == row.Samples.Select(s => s.Nanoseconds).Order().ElementAt(2) &&
                    row.MedianBytes == row.Samples.Select(s => s.Bytes).Order().ElementAt(2), "Median differs from raw samples.");
            }
        }
        var md = new StringBuilder("# Specialized simplification comparisons\n\n");
        md.AppendLine($"Measured source `{runs[0].Revision}`; {runs[0].Utc:yyyy-MM-dd} UTC; {runs[0].Runtime}; {runs[0].OS}; {runs[0].CPU}. Three fresh processes, five calibrated batches each, rotated method order, tiered compilation `{runs[0].TieredCompilation}`. Times show median of process medians (minimum–maximum); ratios are descriptive, not significance tests.\n");
        md.AppendLine("The synthetic guarantees follow from construction. Prepared real-pair decisions assume certified simple polygons and retained indices supplied by the caller; mapping and certification are measured separately. Uncertified-input rows charge the exact example certifier on every call. That O(n²) certifier is a correctness-oriented reference, not a claim of optimal validation cost. Timings start with finished input arrays; simplification, file loading and reporting are excluded.\n");
        md.AppendLine("## Bounds and decisions\n\nAccept means Jaccard change ≤ threshold; Reject means greater. Unresolved runs Winding. Bounds conservatively enclose exact binary64-input geometry under the supplied simple/subsequence preconditions; fallback inherits Winding's numerical limits. Winding values below are comparison measurements, not an independent exact oracle.\n");
        md.AppendLine("| Pair | XOR lower | Winding XOR | XOR upper | Jaccard lower–upper | 0.1% | 1% | 3% |\n|---|---:|---:|---:|---:|---|---|---|");
        foreach (var r in runs[0].Bounds)
            md.AppendLine(FormattableString.Invariant($"| {r.Key} | {r.Bounds.Xor.Lower:G9} | {r.ActualXor:G9} | {r.Bounds.Xor.Upper:G9} | {r.Bounds.Jaccard.Lower:P4}–{r.Bounds.Jaccard.Upper:P4} | {string.Join(" | ", r.Decisions)} |"));
        foreach (var scope in methods.Select(m => m.Scope).Distinct())
        {
            md.AppendLine($"\n## {scope}\n\n| Case | Method | µs (min–max) | B/op | Fallback |\n|---|---|---:|---:|---|");
            foreach (var method in methods.Where(m => m.Scope == scope))
            {
                var rows = runs.Select(r => r.Measurements.Single(m => m.Key == method.Key && m.Method == method.Method && m.Scope == scope)).ToArray();
                md.AppendLine(FormattableString.Invariant($"| {method.Key} | {method.Method} | {rows.Select(r => r.MedianNanoseconds).Order().ElementAt(1) / 1000:F3} ({rows.Min(r => r.MedianNanoseconds) / 1000:F3}–{rows.Max(r => r.MedianNanoseconds) / 1000:F3}) | {rows.Max(r => r.MedianBytes):F0} | {rows[0].Value.Fallback} |"));
            }
        }
        md.AppendLine("\nWarm allocation figures exclude startup, retained workspace, fixture construction and supplied certificates. A threshold decision is not an exact-area result. This small matrix establishes neither production acceptance rates nor universal performance superiority. Read SPECIALIZED.md for prerequisites and source references.");
        File.WriteAllText(Path.Combine(directory, "specialized-summary.md"), md.ToString());
        Console.WriteLine($"Validated {runs.Sum(r => r.Measurements.Sum(m => m.Samples.Length))} raw samples; wrote specialized-summary.md.");
    }
}
