using System.Globalization;
using System.Net;
using System.Text;
using PolylineKit;
using PolylineKit.Recognition;

namespace StrokeTemplates;

internal static class InspectionPage
{
    public static void Write(string path, ReplayInput input, ReplayResult result, bool includeContours)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>PolylineKit stroke replay</title><style>
            :root{color-scheme:light}body{font:16px/1.5 system-ui,sans-serif;color:#18273c;background:#f4f7fa;max-width:1120px;margin:40px auto;padding:0 22px}
            h1{font-size:30px;margin-bottom:5px}h2{font-size:21px}p{max-width:950px}code{overflow-wrap:anywhere;font-size:13px}
            .muted{color:#526276}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:18px}
            article{background:white;border:1px solid #d8e0e9;border-radius:12px;padding:18px;min-width:0}svg{width:100%;height:270px;background:#fafbfd;border-radius:6px}
            table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:6px 0;border-bottom:1px solid #edf0f4;text-align:left}td:last-child{text-align:right;font-variant-numeric:tabular-nums}
            .query{color:#b63442}.template{color:#176f9e}.note{border-left:4px solid #d3a039;padding:10px 14px;background:#fff9e9}
            details{margin-top:15px}summary{cursor:pointer}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px}footer{margin:30px 0;color:#526276;font-size:14px}
            </style></head><body><h1>PolylineKit stroke replay</h1>
            """);
        html.Append("<p class=muted>Query <code>").Append(E(input.Query.SampleId)).Append("</code> · ")
            .Append(E(input.Query.Dataset)).Append(" · selected method <strong>").Append(E(result.Method)).Append("</strong></p>");
        html.Append("<p>Top three distinct labels, using each label's best template. Smaller native scores rank first; exact ties use ordinal template IDs. Scores are not confidence percentages.</p>");
        if (input.IsDemo) html.Append("<p class=note>Synthetic wiring demonstration with newly generated analytic shapes. These results are not a recognition-accuracy evaluation.</p>");
        if (input.IsExternalQuery) html.Append("<p class=note>Standalone imported query. The first frozen bank for its dataset and seed was selected in held-out-writer order; this is a replay, not a held-out evaluation result.</p>");
        bool native = result.Method is "protractor" or "dtw";
        if (native) html.Append("<p class=note>The ranking uses the native baseline score. Overlays and RMS/area values below are separate 64-point diagnostics; they do not depict Protractor's native rotation or DTW's warping correspondence.</p>");
        html.Append("<p><span class=query>Red: query</span> · <span class=template>Blue: template</span>. Solid paths are the actual 64-point RMS/area inputs; faint paths retain the original vertices. Filled orange regions, when requested, show endpoint-bridged NonZero area. Positive Y points down in this view; both paths use the same recorded-axis convention.</p>");
        html.Append("<div class=cards>");
        int rank = 0;
        foreach (RankedTemplate item in result.TopClasses)
        {
            PairScores scores = RecognitionEngine.ScorePair(result.Query, item.Prepared, input.Configuration.TiltDegrees);
            Point2[] querySamples = scores.Transform.Apply(result.Query.Samples64);
            AffineTransform2D rawTransform = result.Query.NormalizationTransform.Then(scores.Transform);
            Point2[] queryFull = rawTransform.Apply(result.Query.Original);
            AreaComparisonResult? diagnostic = includeContours
                ? PolylineComparison.EndpointBridgedArea(item.Prepared.Samples64, querySamples, PathFillRule.NonZero, 6, true) : null;
            if (diagnostic is not null && diagnostic.RawArea != scores.Area)
                throw new InvalidOperationException("Displayed diagnostic contours disagree with the engine area.");
            html.Append("<article><h2>").Append(++rank).Append(". ").Append(E(item.Template.Label)).Append("</h2><p><code>")
                .Append(E(item.Template.SampleId)).Append("</code></p>");
            AppendSvg(html, querySamples, item.Prepared.Samples64, queryFull, item.Prepared.NormalizedFull, diagnostic?.Contours);
            html.Append("<table><tbody>");
            Row(html, "Native " + result.Method + " score", N(item.Score));
            Row(html, native ? "Diagnostic RMS (64 points)" : "RMS (64 points)", N(scores.Rms));
            Row(html, native ? "Diagnostic area (normalized units²)" : "Area (normalized units²)", N(scores.Area));
            Row(html, "RMS-selected tilt", scores.RotationDegrees.ToString(CultureInfo.InvariantCulture) + "°");
            html.Append("</tbody></table><details><summary>Actual query transformation</summary><pre>")
                .Append(E(TransformText(rawTransform))).Append("</pre><p class=muted>Original query coordinates → centered unit bounds → the displayed discrete RMS-selected rotation. No additional fitted scale, reflection or reversal.</p></details></article>");
        }
        html.Append("</div><details><summary>Replay provenance and frozen settings</summary><pre>")
            .Append(E($"Protocol: {RecognitionFiles.Protocol}\nSource revision: {input.SourceRevision}\nEngine: {RecognitionEngine.ImplementationId}\nBank dataset/split: {input.Bank.Dataset}/{input.Bank.Split}\nBank held-out writer: {input.Bank.HeldOutWriter ?? "unspecified"}\nSeed: {input.Bank.Seed}\nTemplates: {input.Templates.Length}\nQuery writer/session: {input.Query.WriterId ?? "unavailable"}/{input.Query.Session ?? "unavailable"}\nKnown query label (display only): {input.Query.Label}\nConfiguration: {input.Configuration}"))
            .Append("</pre></details><footer>Generated locally by the StrokeTemplates consumer using the same recognition engine as the evaluation. No external scripts, fonts or images are loaded. Diagnostic rendering is outside scoring latency.</footer></body></html>");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, html.ToString(), new UTF8Encoding(false));
    }

    private static void AppendSvg(StringBuilder html, IReadOnlyList<Point2> query, IReadOnlyList<Point2> template,
        IReadOnlyList<Point2> queryFull, IReadOnlyList<Point2> templateFull, IReadOnlyList<IReadOnlyList<Point2>>? contours)
    {
        Bounds2D bounds = Bounds2D.FromPoints(query).Union(Bounds2D.FromPoints(template))
            .Union(Bounds2D.FromPoints(queryFull)).Union(Bounds2D.FromPoints(templateFull));
        double extent = Math.Max(bounds.Width, bounds.Height), margin = extent * .12;
        double x = bounds.Center.X - extent / 2 - margin, y = bounds.Center.Y - extent / 2 - margin;
        double size = extent + 2 * margin, line = size / 160;
        html.Append("<svg role=img aria-label=\"Transformed query and template stroke overlay\" viewBox=\"")
            .Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(size)).Append(' ').Append(N(size)).Append("\">");
        if (contours is not null && contours.Count > 0)
        {
            html.Append("<path fill=\"#edb65a\" fill-opacity=\".3\" fill-rule=nonzero d=\"");
            foreach (IReadOnlyList<Point2> contour in contours)
            {
                for (int i = 0; i < contour.Count; i++) html.Append(i == 0 ? 'M' : 'L').Append(N(contour[i].X)).Append(' ').Append(N(contour[i].Y)).Append(' ');
                html.Append("Z ");
            }
            html.Append("\"/>");
        }
        Polyline(html, templateFull, "#176f9e", line * .6, .3);
        Polyline(html, queryFull, "#b63442", line * .6, .3);
        Polyline(html, template, "#176f9e", line, 1);
        Polyline(html, query, "#b63442", line, 1);
        foreach ((IReadOnlyList<Point2> points, string color) in new[] { (query, "#b63442"), (template, "#176f9e") })
        {
            html.Append("<circle cx=\"").Append(N(points[0].X)).Append("\" cy=\"").Append(N(points[0].Y)).Append("\" r=\"").Append(N(line * 1.8)).Append("\" fill=\"").Append(color).Append("\"/>");
            html.Append("<circle cx=\"").Append(N(points[^1].X)).Append("\" cy=\"").Append(N(points[^1].Y)).Append("\" r=\"").Append(N(line * 1.8)).Append("\" fill=white stroke=\"").Append(color).Append("\" stroke-width=\"").Append(N(line * .7)).Append("\"/>");
        }
        html.Append("</svg>");
    }

    private static void Polyline(StringBuilder html, IReadOnlyList<Point2> points, string color, double width, double opacity)
    {
        html.Append("<polyline fill=none stroke=\"").Append(color).Append("\" stroke-linejoin=round stroke-linecap=round stroke-width=\"")
            .Append(N(width)).Append("\" opacity=\"").Append(N(opacity)).Append("\" points=\"");
        foreach (Point2 point in points) html.Append(N(point.X)).Append(',').Append(N(point.Y)).Append(' ');
        html.Append("\"/>");
    }

    private static void Row(StringBuilder html, string label, string value) => html.Append("<tr><td>").Append(E(label)).Append("</td><td>").Append(E(value)).Append("</td></tr>");
    private static string TransformText(AffineTransform2D t) => $"x' = {N(t.M11)} x + {N(t.M12)} y + {N(t.OffsetX)}\ny' = {N(t.M21)} x + {N(t.M22)} y + {N(t.OffsetY)}";
    private static string N(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
