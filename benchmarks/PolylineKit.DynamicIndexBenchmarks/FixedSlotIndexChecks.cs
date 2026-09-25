namespace DynamicIndexBenchmarks;

/// <summary>Focused contract checks with an independently maintained active-array oracle.</summary>
public static class FixedSlotIndexChecks
{
    public static int Run()
    {
        IEdgeIndexFactory[] factories = [new LinearIndexFactory(), new DenseLinearIndexFactory(), new FixedSlotIndexFactory()];
        int checks = 0;
        foreach (var factory in factories)
        {
            checks += CheckContracts(factory);
            checks += CheckNoWarmAllocations(factory);
        }

        foreach (int count in new[] { 0, 1, 2, 3, 5, 16, 63, 128 })
        for (int seed = 0; seed < 8; seed++)
        {
            var random = new Random(0x5eed + seed * 397 + count);
            var expected = Enumerable.Range(0, count).Select(_ => RandomBox(random)).ToArray();
            var active = Enumerable.Repeat(true, count).ToArray();
            var indices = factories.Select(f => f.Create(expected)).ToArray();
            int remaining = count;
            while (true)
            {
                var queries = new List<Box2> { new(-1000, -1000, 1000, 1000), new(2000, 2000, 2001, 2001) };
                for (int i = 0; i < 4; i++) queries.Add(RandomBox(random));
                if (count != 0)
                {
                    Box2 b = expected[random.Next(count)];
                    queries.Add(new(b.MaxX, b.MaxY, b.MaxX, b.MaxY));
                    queries.Add(new(b.MinX, b.MinY, b.MinX, b.MaxY));
                }
                foreach (var query in queries)
                foreach (var index in indices)
                {
                    CheckQuery(index, query, expected, active);
                    checks++;
                }
                if (remaining <= 1) break;
                int[] alive = Enumerable.Range(0, count).Where(i => active[i]).ToArray();
                int keptPosition = random.Next(alive.Length);
                int removedPosition = random.Next(alive.Length - 1);
                if (removedPosition >= keptPosition) removedPosition++;
                int kept = alive[keptPosition], removed = alive[removedPosition];
                Box2 replacement = RandomBox(random);
                foreach (var index in indices)
                    index.ApplyShortcut(kept, expected[kept], replacement, removed, expected[removed]);
                expected[kept] = replacement;
                active[removed] = false;
                remaining--;
            }
        }
        return checks;
    }

    private static int CheckContracts(IEdgeIndexFactory factory)
    {
        int checks = 0;
        var all = new Box2(-100, -100, 100, 100);
        var invalid = new Box2(double.NaN, 0, 1, 1);
        Throws<ArgumentNullException>(() => factory.Create(null!)); checks++;
        foreach (var bad in new[] { invalid, new Box2(0, 0, double.PositiveInfinity, 1), new Box2(1, 0, 0, 1) })
        { Throws<ArgumentException>(() => factory.Create([bad])); checks++; }
        var empty = factory.Create([]);
        Require(empty.Query(all, Span<int>.Empty) == 0, "Empty query result."); checks++;
        Throws<ArgumentException>(() => empty.Query(invalid, Span<int>.Empty)); checks++;
        Throws<ArgumentOutOfRangeException>(() => empty.ApplyShortcut(0, default, default, 1, default)); checks++;

        Box2[] boxes = [new(0, 0, 1, 1), new(1, 0, 2, 1), new(2, 0, 3, 1)];
        Box2[] supplied = (Box2[])boxes.Clone();
        var index = factory.Create(supplied);
        supplied[0] = new(50, 50, 51, 51); // construction must not retain mutable caller storage
        bool[] active = [true, true, true];
        CheckQuery(index, new(.5, .5, .5, .5), boxes, active); checks++;
        CheckQuery(index, new(1, .5, 1, .5), boxes, active); checks++; // inclusive shared edge
        CheckQuery(index, new(1, 1, 1, 1), boxes, active); checks++; // inclusive vertex contact
        Require(index.Query(new(10, 10, 11, 11), Span<int>.Empty) == 0, "Zero-capacity empty result."); checks++;
        Throws<ArgumentException>(() => index.Query(all, Span<int>.Empty)); checks++;
        Throws<ArgumentException>(() => index.Query(all, new int[2])); checks++;
        Throws<ArgumentException>(() => index.Query(invalid, new int[3])); checks++;

        Box2 shortcut = new(0, 0, 2, 1);
        Action[] invalidUpdates =
        [
            () => index.ApplyShortcut(-1, boxes[0], shortcut, 1, boxes[1]),
            () => index.ApplyShortcut(0, boxes[0], shortcut, 3, boxes[1]),
            () => index.ApplyShortcut(0, boxes[0], shortcut, 0, boxes[0]),
            () => index.ApplyShortcut(0, shortcut, shortcut, 1, boxes[1]),
            () => index.ApplyShortcut(0, boxes[0], shortcut, 1, boxes[2]),
            () => index.ApplyShortcut(0, boxes[0], invalid, 1, boxes[1])
        ];
        foreach (var update in invalidUpdates)
        {
            Throws<ArgumentException>(update);
            // Invalid calls must not partially change either leaf or the hierarchy.
            CheckQuery(index, all, boxes, active);
            CheckQuery(index, new(2.5, .5, 2.5, .5), boxes, active);
            checks += 3;
        }

        index.ApplyShortcut(0, boxes[0], shortcut, 1, boxes[1]);
        boxes[0] = shortcut; active[1] = false;
        CheckQuery(index, all, boxes, active); checks++;
        Throws<InvalidOperationException>(() => index.ApplyShortcut(1, boxes[1], shortcut, 0, boxes[0])); checks++;
        Throws<InvalidOperationException>(() => index.ApplyShortcut(0, boxes[0], shortcut, 1, boxes[1])); checks++;
        Box2 point = new(.5, .5, .5, .5);
        index.ApplyShortcut(0, boxes[0], point, 2, boxes[2]);
        boxes[0] = point; active[2] = false;
        CheckQuery(index, all, boxes, active); checks++;
        CheckQuery(index, point, boxes, active); checks++;
        CheckQuery(index, new(2, 0, 3, 1), boxes, active); checks++;
        return checks;
    }

    private static int CheckNoWarmAllocations(IEdgeIndexFactory factory)
    {
        Box2[] small = [new(0, 0, 1, 1), new(1, 0, 2, 1), new(2, 0, 3, 1)];
        var warm = factory.Create(small);
        var all = new Box2(-1000, -1000, 1000, 1000);
        _ = warm.Query(all, new int[3]);
        warm.ApplyShortcut(0, small[0], new(0, 0, 2, 1), 1, small[1]);
        _ = warm.Query(all, new int[3]);

        var initial = Enumerable.Range(0, 257).Select(i => new Box2(i, 0, i + 1, 1)).ToArray();
        var index = factory.Create(initial);
        var ids = new int[initial.Length];
        Box2 current = initial[0];
        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i < initial.Length; i++)
        {
            Box2 replacement = new(0, 0, i + 1, 1);
            index.ApplyShortcut(0, current, replacement, i, initial[i]);
            checksum += index.Query(all, ids);
            current = replacement;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(bytes == 0, factory.Name + " allocated managed bytes during warm updates/queries.");
        Require(checksum == 256 * 257 / 2, "Warm query active counts differ.");
        return 2;
    }

    private static void CheckQuery(IEdgeIndex index, Box2 query, Box2[] expected, bool[] active)
    {
        var destination = new int[expected.Length + 2];
        Array.Fill(destination, -12345);
        int count = index.Query(query, destination.AsSpan(1, expected.Length));
        Require(count >= 0 && count <= expected.Length, "Invalid result count.");
        Require(destination[0] == -12345 && destination[^1] == -12345, "Query wrote outside destination span.");
        var seen = new bool[expected.Length];
        for (int i = 1; i <= count; i++)
        {
            int id = destination[i];
            Require((uint)id < (uint)expected.Length && !seen[id], "Invalid or duplicate result ID.");
            seen[id] = true;
        }
        for (int i = 0; i < expected.Length; i++)
        {
            Box2 b = expected[i];
            // Independent inclusive intersection expression; do not reuse Box2.Disjoint/Covers.
            bool hit = active[i] && b.MinX <= query.MaxX && query.MinX <= b.MaxX &&
                b.MinY <= query.MaxY && query.MinY <= b.MaxY;
            Require(seen[i] == hit, "Query differs from the active-array oracle.");
        }
    }

    private static Box2 RandomBox(Random random)
    {
        double x = random.Next(-100, 101) * .25, y = random.Next(-100, 101) * .25;
        return new(x, y, x + random.Next(17) * .25, y + random.Next(17) * .25);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
