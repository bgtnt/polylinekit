using System.Numerics;
using Clipper2Lib;

namespace PolylineKit.IndependentRegions;

/// <summary>Assessment-only exact signed lattice area, preserving output hole signs.</summary>
internal static class IntegerArea
{
    internal static double Area(Paths64 paths) => Area(paths, out _);

    internal static double Area(Paths64 paths, out bool usedBigInteger)
    {
        try
        {
            Int128 sum = 0;
            checked
            {
                foreach (Path64 path in paths)
                {
                    if (path.Count < 3) continue;
                    Point64 origin = path[0];
                    for (int i = 0; i < path.Count; i++)
                    {
                        Point64 a = path[i], b = path[(i + 1) % path.Count];
                        Int128 ax = (Int128)a.X - origin.X, ay = (Int128)a.Y - origin.Y;
                        Int128 bx = (Int128)b.X - origin.X, by = (Int128)b.Y - origin.Y;
                        sum += ax * by - ay * bx;
                    }
                }
                if (sum < 0) sum = -sum;
            }
            usedBigInteger = false;
            return (double)sum / 2 / Contracts.ClipperScale / Contracts.ClipperScale;
        }
        catch (OverflowException)
        {
            // Restart the entire sum exactly; a partial overflowed accumulation is never used.
            BigInteger sum = 0;
            foreach (Path64 path in paths)
            {
                if (path.Count < 3) continue;
                Point64 origin = path[0];
                for (int i = 0; i < path.Count; i++)
                {
                    Point64 a = path[i], b = path[(i + 1) % path.Count];
                    BigInteger ax = (BigInteger)a.X - origin.X, ay = (BigInteger)a.Y - origin.Y;
                    BigInteger bx = (BigInteger)b.X - origin.X, by = (BigInteger)b.Y - origin.Y;
                    sum += ax * by - ay * bx;
                }
            }
            usedBigInteger = true;
            return (double)BigInteger.Abs(sum) / 2 / Contracts.ClipperScale / Contracts.ClipperScale;
        }
    }

    internal static object Check()
    {
        int checks = 0;
        void Equal(double actual, double expected, string input)
        {
            Contracts.Require(double.IsFinite(actual) && actual == expected, "Exact integer-area regression failed: " + input);
            checks++;
        }
        Path64 Rectangle(long x, long y, long width, long height) =>
            [new(x, y), new(checked(x + width), y), new(checked(x + width), checked(y + height)), new(x, checked(y + height))];
        foreach (var (x, y) in new[] { (0L, 0L), (390_000_000_000L, 6_670_000_000_000L), (-390_000_000_000L, -6_670_000_000_000L) })
        foreach (bool reverse in new[] { false, true })
        {
            Path64 rectangle = Rectangle(x, y, 499, 3000);
            if (reverse) rectangle.Reverse();
            Equal(Area([rectangle], out bool fallback), 1_497_000.0 / Contracts.ClipperScale / Contracts.ClipperScale, "translated micrometre rectangle");
            Contracts.Require(!fallback, "Ordinary lattice inputs must fit Int128."); checks++;
            rectangle.Add(rectangle[0]);
            Equal(Area([rectangle]), 1_497_000.0 / Contracts.ClipperScale / Contracts.ClipperScale, "explicit closing vertex");
            // The two offset vectors have determinant -1, despite products near 10^18.
            Path64 triangle = [new(x, y), new(x + 1_000_000_000, y + 1_000_000_001),
                new(x + 1_000_000_001, y + 1_000_000_002)];
            if (reverse) triangle.Reverse();
            Equal(Area([triangle]), .5 / Contracts.ClipperScale / Contracts.ClipperScale,
                "translated thin triangle with exact unit lattice determinant");
        }
        Path64 shell = Rectangle(390_000_000_000, 6_670_000_000_000, 10_000_000, 10_000_000);
        Path64 hole = Rectangle(390_002_000_000, 6_670_002_000_000, 4_000_000, 4_000_000); hole.Reverse();
        Equal(Area([shell, hole]), 84, "signed shell minus hole");
        shell.Reverse(); hole.Reverse();
        Equal(Area([shell, hole]), 84, "reverse every ring");
        Path64 cancelled = new(shell); cancelled.Reverse();
        Equal(Area([shell, cancelled]), 0, "opposite signed chains cancel");
        Equal(Area([]), 0, "empty output");
        Path64 extreme = [new(long.MinValue, long.MinValue), new(long.MaxValue, long.MinValue),
            new(long.MaxValue, long.MaxValue), new(long.MinValue, long.MaxValue)];
        double expectedExtreme = (double)(BigInteger.Pow((BigInteger)long.MaxValue - long.MinValue, 2)) /
            Contracts.ClipperScale / Contracts.ClipperScale;
        Equal(Area([extreme], out bool usedFallback), expectedExtreme, "checked Int128 overflow restarts in BigInteger");
        Contracts.Require(usedFallback, "The extreme test must exercise the BigInteger fallback."); checks++;
        return new { Passed = true, Checks = checks, Strategy = "Checked Int128 signed shoelace with per-path local origins; full BigInteger restart on overflow; one final signed total, absolute only after hole subtraction." };
    }
}
