using PolylineKit;
using System.Globalization;
using System.Windows.Media;

namespace RegionCoverage;

/// <summary>Bounded analytic controls for the optional graphics-precision adapters; no timings.</summary>
internal static class WpfComparatorChecks
{
    // Graphics-precision allowance for these unit-sized controls. This does not replace or
    // relax the shared experiment's independent real-data area/coverage acceptance gate.
    private const double ControlTolerance = 2e-8;

    internal static int Run()
    {
        int checks = 0;
        Point2[] unit = Rectangle(0, 0, 1, 1);
        (Point2[] Points, double Area)[] queries =
        [
            (Rectangle(.5, .5, 1, 1), .25),
            (Rectangle(1, 0, 1, 1), 0),
            (unit, 1),
            (Rectangle(.25, .25, .5, .5), .25)
        ];
        CoverageComparator[] methods =
        [new WpfScannerComparator(FillRule.EvenOdd), new WpfScannerComparator(FillRule.Nonzero), new WpfCombineComparator()];
        foreach (var method in methods)
        {
            // Keep one comparator workspace while replacing its inputs repeatedly. Four
            // direction combinations exercise both the cached zone and query orientation.
            for (int pass = 0; pass < 4; pass++)
            {
                var zone = method.Prepare(pass < 2 ? unit : unit.Reverse().ToArray(), true);
                foreach (var (points, expected) in queries)
                {
                    var query = method.Prepare(pass % 2 == 0 ? points : points.Reverse().ToArray(), false);
                    var value = method.Compare(zone, query);
                    CheckValue(method.Name, value, expected, expected);
                    checks++;
                }
            }

            var fullZone = method.Prepare(unit, true);
            var smallQuery = method.Prepare(queries[3].Points, false);
            CheckValue(method.Name, method.Compare(fullZone, smallQuery), .25, .25);
            var smallZone = method.Prepare(queries[3].Points, true);
            var fullQuery = method.Prepare(unit, false);
            CheckValue(method.Name, method.Compare(smallZone, fullQuery), .25, 1);
            checks += 2;

            // The shared Compare contract returns undefined coverage for a zero-area zone,
            // and zero coverage for an empty-area query against a positive-area zone.
            Point2[] line = [new(0, 0), new(1, 0), new(2, 0)];
            var zeroZone = method.Prepare(line, true);
            var zeroQuery = method.Prepare(line, false);
            Require(zeroZone.Area == 0 && zeroQuery.Area == 0, method.Name + ": collinear input area");
            CheckValue(method.Name, method.Compare(zeroZone, fullQuery), 0, null);
            CheckValue(method.Name, method.Compare(fullZone, zeroQuery), 0, 0);
            CheckValue(method.Name, method.Compare(zeroZone, zeroQuery), 0, null);
            checks += 3;

            var separated = method.Prepare(Rectangle(2, 2, 1, 1), false);
            CheckValue(method.Name, method.Compare(fullZone, separated), 0, 0);
            checks++;

            // Reproducible calibration, NOT a requirement that a future WPF version must
            // lose this overlap. Keep the actual output visible; no clamping or substitution.
            double width = Math.ScaleB(1, -26);
            var tiny = method.Prepare(Rectangle(1 - width, 0, 1, 1), false);
            var observed = method.Compare(fullZone, tiny);
            Require(double.IsFinite(observed.Intersection) && observed.Coverage is double c && double.IsFinite(c),
                method.Name + ": tiny-overlap result is not finite");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"WPF precision observation: {method.Name}; exact intersection={width:R}; observed={observed.Intersection:R}; no accuracy assertion."));
            checks++;
        }
        return checks;
    }

    private static void CheckValue(string method, CoverageValue actual, double intersection, double? coverage)
    {
        Require(double.IsFinite(actual.Intersection) && Math.Abs(actual.Intersection - intersection) <= ControlTolerance,
            method + ": analytic intersection differs");
        if (coverage is null)
            Require(actual.Coverage is null, method + ": zero-area zone coverage must be null");
        else
            Require(actual.Coverage is double value && double.IsFinite(value) && Math.Abs(value - coverage.Value) <= ControlTolerance,
                method + ": directed coverage differs");
    }

    private static Point2[] Rectangle(double x, double y, double width, double height) =>
        [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)];

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
