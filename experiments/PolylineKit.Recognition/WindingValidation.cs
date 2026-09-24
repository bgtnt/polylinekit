using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.Recognition;

/// <summary>
/// Compares WindingArea with the Clipper2-based endpoint-bridged area on every query/template pair of the
/// frozen evaluation, using the evaluation's own preparation and transforms. Descriptive only: it changes no
/// frozen method, configuration or prediction.
/// </summary>
public static class WindingValidation
{
    private sealed record PairResult(string Query, string Template, double Clipper6, double Clipper8, double NonZero,
        double Absolute, int Exact, int Symbolic);

    public static void Run(string data, string frozenPath, string output, string revision)
    {
        var frozen = EvaluationRunner.LoadFrozen(data, frozenPath);
        Directory.CreateDirectory(output);
        var datasets = new List<object>();
        foreach (var config in frozen.Configurations)
        {
            var rows = RecognitionFiles.Load(data, config.Dataset);
            var records = rows.ToDictionary(r => r.SampleId, StringComparer.Ordinal);
            var prepared = new Dictionary<string, PreparedStroke>(StringComparer.Ordinal);
            foreach (var row in rows.Where(r => r.Supported))
                try { prepared[row.SampleId] = RecognitionEngine.Prepare(row.GetPoints()); }
                catch (ArgumentException) { }
            var banks = frozen.Banks.Where(b => b.Dataset == config.Dataset).ToArray();
            var unique = banks.SelectMany(b => b.QueryIds.Where(prepared.ContainsKey).SelectMany(q => b.TemplateIds.Select(t => (q, t))))
                .Distinct().ToArray();
            var results = new ConcurrentDictionary<(string, string), PairResult>();
            var watch = Stopwatch.StartNew();
            Parallel.ForEach(unique, pair =>
            {
                var (q, t) = pair;
                AffineTransform2D transform = config.TiltDegrees == 0 ? AffineTransform2D.Identity
                    : RecognitionEngine.Align(prepared[q], prepared[t], config.TiltDegrees).Transform;
                Point2[] template = prepared[t].SampleArray, query = transform.Apply(prepared[q].SampleArray);
                var winding = WindingArea.EndpointBridged(template, query);
                results[pair] = new PairResult(q, t,
                    PolylineComparison.EndpointBridgedArea(template, query, PathFillRule.NonZero, 6).RawArea,
                    PolylineComparison.EndpointBridgedArea(template, query, PathFillRule.NonZero, 8).RawArea,
                    winding.NonZero, winding.AbsoluteWinding, winding.ExactPredicateCount, winding.SymbolicTieBreakCount);
            });
            Console.WriteLine($"{config.Dataset}: {unique.Length} unique pairs in {watch.Elapsed.TotalSeconds:F1} s");

            // Disagreements beyond Clipper's grid error are arbitrated by an independent horizontal-slab integral.
            var disagreements = new List<object>();
            foreach (var r in results.Values.Where(r => Math.Abs(r.NonZero - r.Clipper8) > 1e-6).OrderBy(r => r.Query, StringComparer.Ordinal).ThenBy(r => r.Template, StringComparer.Ordinal))
            {
                Point2[] walk = prepared[r.Template].SampleArray.Concat(prepared[r.Query].SampleArray.Reverse()).ToArray();
                double slab = SlabIntegral(walk);
                disagreements.Add(new
                {
                    r.Query, r.Template, WindingNonZero = r.NonZero, Clipper2Precision8 = r.Clipper8, Clipper2Precision6 = r.Clipper6,
                    SlabIntegral = slab, WindingMinusSlab = r.NonZero - slab, Clipper8MinusSlab = r.Clipper8 - slab, r.Exact, r.Symbolic
                });
            }

            // Would the frozen area ranking change if the Clipper area were replaced by the winding area?
            int rankings = 0, changedAreaWinner = 0, changedAbsoluteWinner = 0;
            foreach (var bank in banks)
            foreach (string q in bank.QueryIds.Where(prepared.ContainsKey))
            {
                rankings++;
                string Winner(Func<PairResult, double> score) => bank.TemplateIds.Select(t => results[(q, t)])
                    .OrderBy(score).ThenBy(p => p.Template, StringComparer.Ordinal).First().Template;
                string clipperWinner = Winner(p => p.Clipper6);
                if (Winner(p => p.NonZero) != clipperWinner) changedAreaWinner++;
                if (Winner(p => p.Absolute) != clipperWinner) changedAbsoluteWinner++;
            }

            var values = results.Values.ToArray();
            double[] excess = values.Where(v => v.NonZero > 0).Select(v => v.Absolute / v.NonZero - 1).Order().ToArray();
            datasets.Add(new
            {
                Dataset = config.Dataset, UniquePairs = values.Length, Rankings = rankings,
                PairsWithExactPredicates = values.Count(v => v.Exact > 0), PairsWithSymbolicTieBreaks = values.Count(v => v.Symbolic > 0),
                MaxAbsoluteDifferenceToClipper8 = values.Max(v => Math.Abs(v.NonZero - v.Clipper8)),
                MaxAbsoluteDifferenceToClipper6 = values.Max(v => Math.Abs(v.NonZero - v.Clipper6)),
                MaxAbsoluteDifferenceToClipper8ExcludingArbitrated = values.Where(v => Math.Abs(v.NonZero - v.Clipper8) <= 1e-6).Max(v => Math.Abs(v.NonZero - v.Clipper8)),
                DisagreementsBeyond1e6 = disagreements.Count, Disagreements = disagreements,
                RankingsWhoseAreaWinnerChangesWithWindingNonZero = changedAreaWinner,
                RankingsWhoseAreaWinnerChangesWithAbsoluteWinding = changedAbsoluteWinner,
                PairsWhereAbsoluteWindingExceedsNonZero = excess.Count(x => x > 1e-12),
                AbsoluteOverNonZeroExcessQuantiles = new { P50 = Quantile(excess, .5), P90 = Quantile(excess, .9), P99 = Quantile(excess, .99), Max = excess[^1] }
            });
        }
        var report = new
        {
            Description = "WindingArea.EndpointBridged against PolylineComparison.EndpointBridgedArea on every frozen query/template pair. Descriptive; no frozen result changes.",
            Revision = revision, frozen.Protocol, frozen.InputHashes, Datasets = datasets
        };
        File.WriteAllText(Path.Combine(output, "winding-validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(RecognitionFiles.Json) { WriteIndented = true }) + "\n");
    }

    private static double Quantile(double[] sorted, double q) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1)];

    // Independent NonZero area of a closed walk by horizontal slabs: split y at every vertex and every pairwise
    // crossing height; inside a slab the covered length is linear in y, so its mid-height length times the slab
    // height is exact. Only floating-point rounding (crossing heights, interval ends, summation) remains.
    private static double SlabIntegral(Point2[] walk)
    {
        var cuts = new SortedSet<double>(walk.Select(p => p.Y));
        for (int i = 0; i < walk.Length; i++)
        for (int j = i + 1; j < walk.Length; j++)
        {
            Point2 a = walk[i], b = walk[(i + 1) % walk.Length], c = walk[j], d = walk[(j + 1) % walk.Length];
            double den = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
            if (den == 0) continue;
            double t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / den;
            double u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / den;
            if (t > 0 && t < 1 && u > 0 && u < 1) cuts.Add(a.Y + t * (b.Y - a.Y));
        }
        double[] ys = cuts.ToArray();
        double area = 0;
        var hits = new List<(double X, int Delta)>();
        for (int k = 1; k < ys.Length; k++)
        {
            double y = (ys[k - 1] + ys[k]) / 2, height = ys[k] - ys[k - 1];
            hits.Clear();
            for (int e = 0; e < walk.Length; e++)
            {
                Point2 a = walk[e], b = walk[(e + 1) % walk.Length];
                if ((a.Y <= y) != (b.Y <= y)) hits.Add((a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y), b.Y > a.Y ? 1 : -1));
            }
            hits.Sort((p, q) => p.X.CompareTo(q.X));
            int w = 0;
            for (int i = 0; i + 1 < hits.Count; i++) { w -= hits[i].Delta; if (w != 0) area += (hits[i + 1].X - hits[i].X) * height; }
        }
        return area;
    }
}
