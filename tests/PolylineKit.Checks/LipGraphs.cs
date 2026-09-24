using PolylineKit;

namespace PolylineKit.Experiments;

internal sealed record Lobe(double StartX, double EndX, double Area, double BoundaryLength, double Weight);

/// <summary>Independent sweep of graph lobes with the published LIP area/length weights.</summary>
internal static class LipGraphs
{
    public static double Measure(IReadOnlyList<Point2> p, IReadOnlyList<Point2> q, List<Lobe>? lobes = null,
        double? wholeLength = null)
    {
        if (!Geometry.IncreasingX(p) || !Geometry.IncreasingX(q) || p[0].X != q[0].X || p[^1].X != q[^1].X)
            throw new ArgumentException("LIP graph sweep requires strictly increasing x and equal domains; clean duplicates first.");
        double denominator = wholeLength ?? (Geometry.Length(p) + Geometry.Length(q));
        double x = p[0].X, start = x, faceArea = 0, faceLength = 0, result = 0;
        int i = 1, j = 1;
        while (i < p.Count && j < q.Count)
        {
            double right = Math.Min(p[i].X, q[j].X);
            double pa = Geometry.At(p[i - 1], p[i], x), pb = Geometry.At(p[i - 1], p[i], right);
            double qa = Geometry.At(q[j - 1], q[j], x), qb = Geometry.At(q[j - 1], q[j], right);
            double a = pa - qa, b = pb - qb, width = right - x;
            double length = Geometry.Length(new(x, pa), new(right, pb)) + Geometry.Length(new(x, qa), new(right, qb));
            if (a == 0) Finish(x);
            if ((a < 0 && b > 0) || (a > 0 && b < 0))
            {
                double fraction = -a / (b - a), crossX = x + fraction * width;
                faceArea += Math.Abs(a) * width * fraction / 2;
                faceLength += length * fraction;
                Finish(crossX);
                faceArea = Math.Abs(b) * width * (1 - fraction) / 2;
                faceLength = length * (1 - fraction);
            }
            else
            {
                faceArea += Math.Abs((a + b) * width / 2);
                faceLength += length;
            }
            if (b == 0) Finish(right);
            x = right;
            if (right == p[i].X) i++;
            if (right == q[j].X) j++;
        }
        Finish(x);
        return result;

        void Finish(double end)
        {
            if (faceLength > 0)
            {
                double weight = faceLength / denominator;
                result += faceArea * weight;
                lobes?.Add(new(start, end, faceArea, faceLength, weight));
            }
            faceArea = faceLength = 0;
            start = end;
        }
    }
}
