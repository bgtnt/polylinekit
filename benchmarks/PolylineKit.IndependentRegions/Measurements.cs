using System.Diagnostics;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

internal sealed record Sample(int Iterations, double NanosecondsPerOperation, double BytesPerOperation);
internal sealed record Row(string Direction, string Backend, string Scope, Digest Digest,
    double MedianNanoseconds, double MedianBytes, Sample[] Samples);

internal static class Measurements
{
    internal const int SamplesPerCell = 5;
    internal const int TargetMilliseconds = 10;
    internal const int MaximumIterations = 16384;
    private static double sink;
    internal static Row[] Run(FrozenCorpus corpus, int run)
    {
        Contracts.Require(run is >= 1 and <= 3, "Run identity must be 1, 2 or 3.");
        Contracts.Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0" &&
            Environment.GetEnvironmentVariable("DOTNET_TieredPGO") == "0", "Set DOTNET_TieredCompilation=0 and DOTNET_TieredPGO=0 before launching.");
        Contracts.Require(!corpus.InvalidInputs.Any(i => i.Stage == "quantized-NTS"),
            "The predeclared quantization invalidates source topology; record check evidence before timing.");
        var rows = new List<Row>();
        foreach (bool reverse in new[] { false, true })
        {
            RawFeature[] first = reverse ? corpus.Queries.Eligible : corpus.Zones.Eligible;
            RawFeature[] second = reverse ? corpus.Zones.Eligible : corpus.Queries.Eligible;
            string direction = reverse ? "queries-to-zones" : "zones-to-queries";
            foreach (string backend in Session.Backends.Skip(run - 1).Concat(Session.Backends.Take(run - 1)))
            {
                var session = new Session(first, second, backend, PathFillRule.NonZero);
                foreach (string scope in new[] { "prepare-only", "warm-table", "prepare-plus-one-table" })
                {
                    Func<Digest> invoke = scope switch
                    {
                        "prepare-only" => () => new Session(first, second, backend, PathFillRule.NonZero).PreparationDigest(),
                        "warm-table" => session.Table,
                        "prepare-plus-one-table" => () => new Session(first, second, backend, PathFillRule.NonZero).Table(),
                        _ => throw new InvalidOperationException()
                    };
                    Row row = Measure(direction, backend, scope, invoke);
                    if (scope != "prepare-only") Contracts.Require(row.Digest.Candidates == corpus.EligibleCandidates, "Timed candidate inventory differs.");
                    rows.Add(row);
                    Console.WriteLine($"Independent-region run {run}: {direction}/{backend}/{scope}: {row.MedianNanoseconds / 1e6:F4} ms, {row.MedianBytes:F0} B/op.");
                }
                Row warm = rows.Single(r => r.Direction == direction && r.Backend == backend && r.Scope == "warm-table");
                Row fresh = rows.Single(r => r.Direction == direction && r.Backend == backend && r.Scope == "prepare-plus-one-table");
                Contracts.Require(warm.Digest == fresh.Digest, "Warm/fresh result digest differs.");
            }
        }
        Contracts.Require(rows.Count == 18, "Incomplete timing matrix.");
        Corpus.VerifyUnchanged(corpus);
        GC.KeepAlive(sink);
        return rows.ToArray();
    }

    private static Row Measure(string direction, string backend, string scope, Func<Digest> invoke)
    {
        Digest value = invoke();
        var watch = Stopwatch.StartNew(); bool stable = true;
        do { Digest actual = invoke(); Keep(actual); stable &= actual == value; } while (watch.ElapsedMilliseconds < TargetMilliseconds);
        Contracts.Require(stable, "Unstable warmup digest.");
        int iterations = 1;
        while (true)
        {
            watch.Restart(); stable = true;
            for (int i = 0; i < iterations; i++) { Digest actual = invoke(); Keep(actual); stable &= actual == value; }
            Contracts.Require(stable, "Unstable calibration digest.");
            if (watch.ElapsedMilliseconds >= TargetMilliseconds || iterations >= MaximumIterations) break;
            iterations *= 2;
        }
        var samples = new Sample[SamplesPerCell];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); stable = true;
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                Digest actual = invoke(); Keep(actual); stable &= actual == value;
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            // Assertion messages, sample objects and serialization occur after both counters.
            Contracts.Require(stable, "Unstable measured digest.");
            samples[batch] = new(iterations, elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)bytes / iterations);
        }
        Contracts.Require(value == invoke(), "Final result digest differs.");
        return new(direction, backend, scope, value, samples.Select(s => s.NanosecondsPerOperation).Order().ElementAt(2),
            samples.Select(s => s.BytesPerOperation).Order().ElementAt(2), samples);
    }

    private static void Keep(Digest value) => Volatile.Write(ref sink, value.IntersectionSum + value.CoverageSum + value.Candidates);
}
