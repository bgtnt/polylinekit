namespace PolylineKit.ActiveSweep;

/// <summary>Experimental dispatch heuristic, not a simplicity or speed certificate.</summary>
internal static class SweepPolicy
{
    internal static bool ShouldTry(IReadOnlyList<Point2> path)
    {
        int n = path.Count;
        if (n < 256) return false;
        int xChanges = 0, yChanges = 0, xFirst = 0, yFirst = 0, xLast = 0, yLast = 0;
        Point2 previous = path[n - 1];
        for (int i = 0; i < n; i++)
        {
            Point2 p = path[i];
            Count(p.X.CompareTo(previous.X), ref xFirst, ref xLast, ref xChanges);
            Count(p.Y.CompareTo(previous.Y), ref yFirst, ref yLast, ref yChanges);
            previous = p;
        }
        if (xLast != xFirst) xChanges++;
        if (yLast != yFirst) yChanges++;
        // Few changes on either axis usually let the existing axis-sorted engine
        // reject pairs cheaply. This intentionally misses some profitable inputs.
        int minimum = Math.Max(16, n / 16);
        return xChanges >= minimum && yChanges >= minimum;
    }

    private static void Count(int direction, ref int first, ref int last, ref int changes)
    {
        if (direction == 0) return;
        if (first == 0) first = direction;
        else if (last != direction) changes++;
        last = direction;
    }
}
