using NetTopologySuite.Geometries;
using NetTopologySuite.Index.HPRtree;
using NetTopologySuite.Index.Strtree;

namespace RegionCoverage;

internal static class PackedBoundsIndexChecks
{
    internal static int Run()
    {
        int checks = 0;
        RegionBounds[] analytic =
        [
            new(0, 0, 2, 2), new(2, 0, 3, 2), new(2, 2, 3, 3), new(1, 1, 1, 1),
            new(1, 0, 1, 2), new(0, 1, 2, 1), new(1, 0, 1, 2), new(-10, -10, 10, 10)
        ];
        CheckSet(analytic, [.. analytic, new(20, 20, 21, 21), new(-20, -20, 20, 20), new(1, 2, 1, 3)]);
        CheckSet([], [new(0, 0, 0, 0)]);
        var random = new Random(938_751);
        // Fanout and upper-level boundaries exercise partial pages and covered subtree ranges.
        foreach (int count in new[] { 1, 31, 32, 33, 98, 1023, 1024, 1025, 2049 })
        {
            var boxes = new RegionBounds[count];
            for (int i = 0; i < count; i++)
            {
                double x = random.Next(-100, 100), y = random.Next(-100, 100);
                boxes[i] = new(x, y, x + random.Next(0, 15), y + random.Next(0, 15));
            }
            var queries = new List<RegionBounds> { new(-200, -200, 200, 200), new(300, 300, 300, 300), boxes[0] };
            for (int i = 0; i < 100; i++)
            {
                double x = random.Next(-120, 120), y = random.Next(-120, 120);
                queries.Add(new(x, y, x + random.Next(0, 90), y + random.Next(0, 90)));
            }
            CheckSet(boxes, queries);
        }
        // Identical centers and equal endpoints retain distinct IDs, even through full coverage.
        CheckSet(Enumerable.Repeat(new RegionBounds(1, 1, 1, 1), 1100).ToArray(),
            [new(1, 1, 1, 1), new(0, 0, 1, 1), new(1, 1, 2, 2), new(2, 2, 2, 2)]);
        var copy = (RegionBounds[])analytic.Clone();
        var immutable = new PackedBoundsIndex(copy); copy[0] = new(100, 100, 101, 101);
        var destination = new int[analytic.Length];
        int found = immutable.Query(new(0, 0, 0, 0), destination);
        if (!destination.AsSpan(0, found).Contains(0)) throw new InvalidOperationException("Index retained caller's mutable bounds.");
        checks++;
        bool capacityRejected = false;
        try { immutable.Query(new(-20, -20, 20, 20), new int[analytic.Length - 1]); }
        catch (ArgumentException) { capacityRejected = true; }
        if (!capacityRejected) throw new InvalidOperationException("Index silently truncated candidates.");
        checks++;
        foreach (RegionBounds invalid in new[] { new RegionBounds(1, 0, 0, 1), new(0, 1, 1, 0),
            new(double.NaN, 0, 1, 1), new(0, 0, double.PositiveInfinity, 1) })
        {
            bool buildRejected = false, queryRejected = false;
            try { _ = new PackedBoundsIndex([invalid]); } catch (ArgumentException) { buildRejected = true; }
            try { immutable.Query(invalid, destination); } catch (ArgumentException) { queryRejected = true; }
            if (!buildRejected || !queryRejected) throw new InvalidOperationException("Invalid bounds accepted.");
            checks += 2;
        }
        for (int i = 0; i < 20; i++) immutable.Query(new(1, 1, 1, 1), destination);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            immutable.Query(new(1, 1, 1, 1), destination);
            immutable.Query(new(-20, -20, 20, 20), destination);
        }
        if (GC.GetAllocatedBytesForCurrentThread() != before) throw new InvalidOperationException("Warm packed traversal allocated memory.");
        checks++;
        Console.WriteLine($"PackedBoundsIndex controls: {checks} assertions.");
        return checks;

        void CheckSet(RegionBounds[] boxes, IEnumerable<RegionBounds> queries)
        {
            var packed = new PackedBoundsIndex(boxes); var nts = new STRtree<int>(); var hilbert = new HPRtree<int>();
            for (int i = 0; i < boxes.Length; i++)
            { nts.Insert(Envelope(boxes[i]), i); hilbert.Insert(Envelope(boxes[i]), i); }
            nts.Build(); hilbert.Build();
            var output = new int[boxes.Length];
            foreach (RegionBounds query in queries)
            {
                int[] expected = Enumerable.Range(0, boxes.Length).Where(i => !query.Disjoint(boxes[i])).ToArray();
                int length = packed.Query(query, output); int[] actual = output.AsSpan(0, length).ToArray();
                Array.Sort(actual);
                int[] reference = nts.Query(Envelope(query)).Order().ToArray();
                int[] hilbertReference = hilbert.Query(Envelope(query)).Order().ToArray();
                if (!expected.SequenceEqual(actual) || !expected.SequenceEqual(reference) || !expected.SequenceEqual(hilbertReference))
                    throw new InvalidOperationException("Packed, linear, STRtree and HPRtree candidate IDs differ.");
                checks += 3;
            }
        }
    }

    private static Envelope Envelope(RegionBounds b) => new(b.MinX, b.MaxX, b.MinY, b.MaxY);
}
