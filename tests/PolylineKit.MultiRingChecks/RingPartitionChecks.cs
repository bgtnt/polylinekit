namespace PolylineKit.MultiRingChecks;

/// <summary>Independent all-pairs oracle for the bounds components used by region evaluation.</summary>
internal static class RingPartitionChecks
{
    internal static object Run()
    {
        var checks = new Checks();
        checks.Run();
        return new
        {
            status = "passed",
            assertions = checks.Assertions,
            cases = checks.Cases,
            maximumBounds = checks.MaximumBounds,
            randomizedCases = 100,
            gapCases = 16,
            generatorSeed = "0xe5b17a29",
            scope = "Exact connected partition of inclusive ring bounds against independent all-pairs union-find; no timing threshold.",
            ownership = "One workspace is reused across large, small and empty inputs to expose stale pooled metadata."
        };
    }

    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Intersects(Box b) => MinX <= b.MaxX && b.MinX <= MaxX && MinY <= b.MaxY && b.MinY <= MaxY;
    }

    private sealed class Checks
    {
        private readonly WindingEngine.Workspace workspace = new();
        internal int Assertions { get; private set; }
        internal int Cases { get; private set; }
        internal int MaximumBounds { get; private set; }

        internal void Run()
        {
            Case("empty", []);
            Case("single point bounds", [new(4, 7, 4, 7)]);
            Case("edge and corner contacts", [new(0, 0, 1, 1), new(1, 0, 2, 1), new(2, 1, 3, 2), new(4, 1, 5, 2)]);
            Case("line and point contacts", [new(0, 0, 0, 4), new(-1, 2, 1, 2), new(1, 2, 1, 2), new(2, 2, 2, 2)]);
            Case("distant tiny bounds", [new(0, 0, .001, .001), new(1e12, 1e12, 1e12 + .001, 1e12 + .001),
                new(1e14, 1e14, 1e14 + .1, 1e14 + .1)]);

            foreach (int count in new[] { 16, 17, 31, 32, 33, 63, 65, 129, 257, 1024 })
            {
                Box[] grid = Grid(count, paired: false);
                Case($"separate grid {count}", grid);
                Case($"overlapping pairs {count}", Grid(count, paired: true));
                Case($"horizontal strips {count}", Enumerable.Range(0, count).Select(i => new Box(-1e6, i * 4, 1e6, i * 4 + 1)).ToArray());
                Case($"vertical strips {count}", Enumerable.Range(0, count).Select(i => new Box(i * 4, -1e6, i * 4 + 1, 1e6)).ToArray());
                Case($"equal boxes {count}", Enumerable.Repeat(new Box(-1, -2, 3, 4), count).ToArray());
                Case($"nested boxes {count}", Enumerable.Range(0, count).Select(i => new Box(-i - 1, -i - 1, i + 1, i + 1)).ToArray());

                // The last box connects otherwise separated components; it must not be skipped after
                // earlier queries have already assigned those boxes to different roots.
                Box[] bridge = Enumerable.Range(0, count).Select(i => new Box(i * 3, 0, i * 3 + 1, 1)).ToArray();
                bridge[^1] = new Box(0, .5, (count - 2) * 3 + 1, .5);
                Case($"late bridge {count}", bridge);

                Box[] shuffled = Grid(count, paired: true);
                var shuffle = new FixedRandom((uint)count);
                Shuffle(shuffled, ref shuffle);
                Case($"shuffled overlapping pairs {count}", shuffled);
                Case($"small after {count}", [new(0, 0, 1, 1), new(3, 0, 4, 1)]);
                Case($"empty after {count}", []);
                Case($"large after empty {count}", grid);
            }

            foreach (int count in new[] { 31, 63, 65, 129 })
            {
                // An envelope may cover empty space between its members. A query in that space
                // must not connect those members, even when it intersects another small box there.
                // Non-power-of-two sizes exercise uneven partitions without assuming their layout.
                Box[] gaps = Gaps(count);
                Case($"empty gaps between rectangle corners {count}", gaps);
                Case($"reversed gap fixtures {count}", gaps.Reverse().ToArray());
                Case($"transposed gap fixtures {count}", gaps.Select(b => new Box(b.MinY, b.MinX, b.MaxY, b.MaxX)).ToArray());
                Box[] shuffled = (Box[])gaps.Clone();
                var gapShuffle = new FixedRandom((uint)(count * 13 + 7));
                Shuffle(shuffled, ref gapShuffle);
                Case($"permuted gap fixtures {count}", shuffled);
            }

            var random = new FixedRandom(0xe5b17a29);
            for (int run = 0; run < 100; run++)
            {
                int count = run % 3 == 0 ? 16 + random.Next(3) : 32 + random.Next(33);
                var boxes = new Box[count];
                for (int i = 0; i < count; i++)
                {
                    if (i > 0 && random.Next(7) == 0) boxes[i] = boxes[random.Next(i)];
                    else
                    {
                        int x = random.Next(65) - 32, y = random.Next(65) - 32;
                        int width = random.Next(9), height = random.Next(9);
                        boxes[i] = new Box(x, y, x + width, y + height);
                    }
                }
                Shuffle(boxes, ref random);
                Case($"random boxes {run}", boxes);
            }
        }

        private void Case(string name, Box[] boxes)
        {
            Cases++;
            MaximumBounds = Math.Max(MaximumBounds, boxes.Length);
            workspace.RingBuffers(boxes.Length);
            for (int i = 0; i < boxes.Length; i++)
            {
                Box b = boxes[i];
                // Only bounds are needed by GroupRings. Two corners are sufficient to construct them,
                // including zero extents; this deliberately exercises no path-validity machinery.
                Point2[] corners = [new(b.MinX, b.MinY), new(b.MaxX, b.MaxY)];
                workspace.RegionBounds[i] = new WindingEngine.RingBounds(corners, 0, corners.Length);
            }
            int actualCount = WindingEngine.GroupRings(workspace, boxes.Length);

            // This intentionally uses no production bounds helper, spatial index, or group-packing code.
            int[] expectedParents = Enumerable.Range(0, boxes.Length).ToArray();
            for (int a = 0; a < boxes.Length; a++)
                for (int b = 0; b < a; b++)
                    if (boxes[a].Intersects(boxes[b]))
                    {
                        int x = ReferenceRoot(expectedParents, a), y = ReferenceRoot(expectedParents, b);
                        if (x != y) expectedParents[x] = y;
                    }
            int[] expected = Canonical(name + " reference", expectedParents, boxes.Length);
            int[] actual = Canonical(name + " actual", workspace.RingParents, boxes.Length);
            int expectedCount = expected.Distinct().Count();
            True(name + " component count", actualCount == expectedCount);
            for (int i = 0; i < boxes.Length; i++)
                True($"{name} ring {i}: expected component {expected[i]}, actual {actual[i]}", expected[i] == actual[i]);
        }

        private int[] Canonical(string name, int[] parents, int count)
        {
            var roots = new int[count];
            int[] minimum = Enumerable.Repeat(int.MaxValue, count).ToArray();
            for (int i = 0; i < count; i++)
            {
                int root = i, hops = 0;
                while (true)
                {
                    True(name + " parent is in the logical input", parents[root] >= 0 && parents[root] < count);
                    if (parents[root] == root) break;
                    root = parents[root];
                    True(name + " parent chain is acyclic", ++hops < count);
                }
                roots[i] = root;
                minimum[root] = Math.Min(minimum[root], i);
            }
            for (int i = 0; i < count; i++) roots[i] = minimum[roots[i]];
            return roots;
        }

        private void True(string name, bool value)
        {
            if (!value) throw new InvalidOperationException(name);
            Assertions++;
        }
    }

    private static int ReferenceRoot(int[] parents, int at)
    {
        while (parents[at] != at) at = parents[at];
        return at;
    }

    private static Box[] Grid(int count, bool paired)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(paired ? (count + 1) / 2 : count));
        var boxes = new Box[count];
        for (int i = 0; i < count; i++)
        {
            int cell = paired ? i / 2 : i;
            double shift = paired && (i & 1) != 0 ? .25 : 0;
            double x = cell % columns * 4 + shift, y = cell / columns * 4 + shift;
            boxes[i] = new Box(x, y, x + 1, y + 1);
        }
        return boxes;
    }

    private static Box[] Gaps(int count)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt((count + 5) / 6));
        var boxes = new Box[count];
        for (int i = 0; i < count; i++)
        {
            int cell = i / 6;
            double x = cell % columns * 32, y = cell / columns * 32;
            boxes[i] = (i % 6) switch
            {
                0 => new Box(x, y, x + 1, y + 1),
                1 => new Box(x + 8, y, x + 9, y + 1),
                2 => new Box(x, y + 8, x + 1, y + 9),
                3 => new Box(x + 8, y + 8, x + 9, y + 9),
                4 => new Box(x + 3, y + 3, x + 5, y + 5),
                _ => new Box(x + 4, y + 4, x + 4, y + 4)
            };
        }
        return boxes;
    }

    private static void Shuffle(Box[] boxes, ref FixedRandom random)
    {
        for (int i = boxes.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (boxes[i], boxes[j]) = (boxes[j], boxes[i]);
        }
    }

    private struct FixedRandom(uint initial)
    {
        private uint state = initial;
        internal int Next(int maximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)maximum);
        }
    }
}
