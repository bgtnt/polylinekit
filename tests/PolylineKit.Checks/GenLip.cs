using PolylineKit;

namespace PolylineKit.Experiments;

internal sealed record GenLipResult(double Score, int GoodGroups, int BadPairs);

/// <summary>
/// Experimental reconstruction of Definitions 2–8 and Figure 8, p=0, in the 2011 paper.
/// Explicitly rejects an ambiguous reused bad segment instead of silently double-counting it.
/// </summary>
internal static class GenLip
{
    public static GenLipResult Measure(Point2[] p, Point2[] q, bool parallelIsGood = false, double d = 1e-6)
    {
        if (!(d > 0) || !double.IsFinite(d)) throw new ArgumentOutOfRangeException(nameof(d));
        p = Geometry.Clean(p); q = Geometry.Clean(q);
        bool graphP = Geometry.IncreasingX(p), graphQ = Geometry.IncreasingX(q);
        // Conservative admission rule of this reconstruction: require the entire
        // component route to be simple, including portions classified as bad.
        // Increasing-x graphs are already simple, retaining the linear fast path.
        if (!graphP) RequireSimple(p);
        if (!graphQ) RequireSimple(q);
        double denominator = Geometry.Length(p) + Geometry.Length(q), score = 0;
        int goodGroups = 0, badPairs = 0, i = 1, j = 1, startP = 0, startQ = 0, certifiedP = 0, certifiedQ = 0;
        double lengthP = 0, lengthQ = 0;
        bool graphs = graphP && graphQ;
        while (true)
        {
            Point2 dp = Geometry.Sub(p[i], p[i - 1]), dq = Geometry.Sub(q[j], q[j - 1]);
            bool goodDirection = Geometry.Dot(dp, dq) > 0 && (parallelIsGood || Geometry.Cross(dp, dq) != 0);
            // A first pair needs the same connector test as an extended group.
            // Equal-x endpoints on increasing-x paths admit only endpoint contact.
            bool good = goodDirection && ((graphs && p[i].X == q[j].X) ||
                ConnectorClear(p, q, startP, i, startQ, j));
            if (good)
            {
                if (i > certifiedP) lengthP += Geometry.Length(p[i - 1], p[i]);
                if (j > certifiedQ) lengthQ += Geometry.Length(q[j - 1], q[j]);
                certifiedP = i; certifiedQ = j;
                if (i == p.Length - 1 && j == q.Length - 1) { FlushGood(); break; }
                // Both inequalities are evaluated against the same pre-update lengths (paper p.18).
                // At an exhausted side, advance the remaining side: the paper says to reach both
                // ends but omits this boundary rule. This reconstruction choice is documented.
                bool nextP = i < p.Length - 1 && (j == q.Length - 1 || lengthP + Geometry.Length(p[i], p[i + 1]) >= lengthQ);
                bool nextQ = j < q.Length - 1 && (i == p.Length - 1 || lengthQ + Geometry.Length(q[j], q[j + 1]) >= lengthP);
                if (!nextP && !nextQ)
                    throw new NotSupportedException("Published next inequalities cannot advance this unequal-length tail.");
                if (nextP) i++;
                if (nextQ) j++;
            }
            else
            {
                if (i <= certifiedP || j <= certifiedQ)
                    throw new NotSupportedException("A previously certified segment became bad when reused; Figure 8 does not specify rollback ownership.");
                FlushGood();
                score += BadPair(p[i - 1], p[i], q[j - 1], q[j], denominator, d);
                badPairs++;
                startP = certifiedP = i; startQ = certifiedQ = j;
                lengthP = lengthQ = 0;
                bool endP = i == p.Length - 1, endQ = j == q.Length - 1;
                if (endP && endQ) break;
                if (endP || endQ)
                    throw new NotSupportedException("Unpaired tail after a bad pair: no zero-length-segment rule is specified in the paper.");
                i++; j++;
            }
        }
        return new(score, goodGroups, badPairs);

        void FlushGood()
        {
            if (certifiedP == startP && certifiedQ == startQ) return;
            if (certifiedP == startP || certifiedQ == startQ) throw new NotSupportedException("One-sided good group.");
            Point2[] a = p[startP..(certifiedP + 1)], b = q[startQ..(certifiedQ + 1)];
            if (graphs && a[0].X == b[0].X && a[^1].X == b[^1].X)
                score += LipGraphs.Measure(a, b, wholeLength: denominator);
            else
            {
                score += LipPolygons.Regions(a, b, denominator).Sum(r => r.Area * r.Weight);
            }
            goodGroups++;
        }
    }

    public static double BadPair(Point2 p0, Point2 p1, Point2 q0, Point2 q1, double wholeLength, double d)
    {
        Point2 u = Geometry.Sub(p1, p0), v = Geometry.Sub(q1, q0), translation = Geometry.Sub(q0, p0);
        double lp = Geometry.Length(p0, p1), lq = Geometry.Length(q0, q1);
        double fallback = (Geometry.Length(p0, q0) + Math.Abs(lp - lq)) * d;
        double ta = Math.Abs(Geometry.Cross(translation, u)), tb = Math.Abs(Geometry.Cross(translation, v));
        if (ta == 0) ta = fallback;
        if (tb == 0) tb = fallback;
        double cosine = Math.Clamp(Geometry.Dot(u, v) / (lp * lq), -1, 1);
        return (Math.Max(ta, tb) + lp * lq * (1 - cosine) / 2) * ((lp + lq) / wholeLength);
    }

    internal static bool ConnectorClear(Point2[] p, Point2[] q, int pi, int pe, int qi, int qe)
    {
        Point2 a = p[pe], b = q[qe];
        return Clear(p, pi, pe) && Clear(q, qi, qe);
        bool Clear(Point2[] path, int first, int last)
        {
            for (int k = first + 1; k <= last; k++)
            {
                SegmentContact contact = Geometry.Contact(a, b, path[k - 1], path[k], out double t, out _);
                if (contact == SegmentContact.Overlap || (contact == SegmentContact.Point && t > 0 && t < 1))
                    return false;
            }
            return true;
        }
    }
    internal static void RequireSimple(Point2[] path)
    {
        for (int i = 1; i < path.Length; i++)
        for (int j = i + 1; j < path.Length; j++)
        {
            SegmentContact contact = Geometry.Contact(path[i - 1], path[i], path[j - 1], path[j], out _, out _);
            // Adjacent segments may share their common vertex, but may not retrace.
            if (contact == SegmentContact.Overlap || (j > i + 1 && contact == SegmentContact.Point))
                throw new NotSupportedException("This reconstruction requires simple component routes; self-contact or retracing is unsupported.");
        }
    }
}
