using PolylineKit;

namespace PolylineKit.Experiments;

internal static class NumericReviewChecks
{
    public static int Run()
    {
        // Exactly representable points on y=-x/m. This characterizes a known arithmetic
        // limit, not a permitted geometric difference. Improving the error is welcome.
        double m = 9007199254740992.0;
        Point2[] p = [new(-m, 1), new(m, -1)];
        Point2[] q = [new(-m, 1), new(1, -1 / m), new(m, -1)];
        double area = PolylineArea.BetweenGraphs(p, q);
        Require(double.IsFinite(area) && area >= 0 && area <= 1,
            "The reviewed collinear case exceeded its observed absolute error ceiling of 1.");
        Require(PolylineArea.BetweenGraphs(q, p) == area, "Reviewed numeric-limit symmetry.");
        Require(PolylineArea.BetweenGraphs(p, p) == 0, "Wide-domain same-list identity.");
        Require(PolylineArea.BetweenGraphs(q, q) == 0, "Subdivided wide-domain same-list identity.");
        Console.WriteLine($"Numeric review: exact area=0, computed area={area:R}; 4 limit/identity checks passed.");
        return 4;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
