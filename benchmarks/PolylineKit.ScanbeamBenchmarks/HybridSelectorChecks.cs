using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

/// <summary>Full-diagnostic equivalence with the retained, unoptimized stage-two selector.</summary>
internal static class HybridSelectorChecks
{
    internal static void Run(Func<Point2[]?, bool>? publicSelector = null)
    {
        int checks = 0;
        Compare("null", null);
        foreach (HybridInput input in HybridInputs.Create())
        {
            Compare(input.Name, input.Points);
            Compare(input.Name + " reverse", input.Points.Reverse().ToArray());
            Compare(input.Name + " cyclic", [.. input.Points.Skip(3), .. input.Points.Take(3)]);
        }

        int[] lengths = [0, 1, 2, 3, 63, 64, 65, 79, 80, 81, 127, 128, 129, 255, 256, 257, 511, 512, 513, 1023, 1024, 1025];
        foreach (int length in lengths)
        {
            Compare($"count {length} zero", new Point2[length]);
            Compare($"count {length} horizontal", Enumerable.Range(0, length).Select(i => new Point2(i % 9 - 4, 0)).ToArray());
            Compare($"count {length} vertical", Enumerable.Range(0, length).Select(i => new Point2(0, i % 9 - 4)).ToArray());
            Compare($"count {length} diagonal", Enumerable.Range(0, length).Select(i => new Point2(i % 9 - 4, i % 9 - 4)).ToArray());
            Compare($"count {length} grid", Inputs.Grid(length, 0x6387211u + (uint)length));
        }

        // Proper crossings just below, at and above the frozen sample threshold. A single vertical
        // sampled edge meets k distinct horizontal edges; the other samples lie outside its X range.
        foreach (int crossingCount in new[] { 7, 8, 9 })
        {
            Point2[] points = SampledEdges(sample => sample == 0 ? (new(0, -100), new(0, 100))
                : sample <= crossingCount ? (new(-2, sample), new(2, sample))
                : (new(5, 180 + sample), new(6, 180 + sample)));
            HybridSelection expected = HybridClosedAreaV2.Select(points);
            Require(expected.SampleCrossings == crossingCount && expected.SampleCoincidences == 0,
                "The crossing-threshold control no longer has the intended sample structure.");
            Compare($"crossing threshold {crossingCount}", points);
            Compare($"crossing threshold {crossingCount} reverse", points.Reverse().ToArray());
        }

        // Coincidences count pairs. Groups of four plus pairs/triples produce exactly 7, 8 and 9.
        foreach (int coincidenceCount in new[] { 7, 8, 9 })
        {
            Point2[] points = SampledEdges(sample =>
            {
                int group = sample < 4 ? 0 : sample < 6 ? 1 : sample + 10;
                if (coincidenceCount == 8 && sample is 6 or 7) group = 2;
                if (coincidenceCount == 9 && sample is 4 or 5 or 6) group = 1;
                Point2 a = new(4 * group, 0), b = new(4 * group + 2, 0);
                return sample % 2 == 0 ? (a, b) : (b, a);
            });
            HybridSelection expected = HybridClosedAreaV2.Select(points);
            Require(expected.SampleCoincidences == coincidenceCount && expected.SampleCrossings == 0,
                "The coincidence-threshold control no longer has the intended sample structure.");
            Compare($"coincidence threshold {coincidenceCount}", points);
        }

        // Bounding boxes can touch while segment interiors do not cross. Include zero lengths,
        // endpoint contact, collinear overlap/reversal, perpendicular supports and both integer limits.
        (Point2 A, Point2 B)[] touching =
        [
            (new(-2048, 0), new(2048, 0)), (new(0, 0), new(0, 2048)),
            (new(0, -2048), new(0, 0)), (new(-2048, 0), new(0, 0)),
            (new(2048, 0), new(-2048, 0)), (new(0, 0), new(0, 0)),
            (new(-2048, -2048), new(0, 0)), (new(0, 0), new(2048, 2048)),
            (new(-2048, 2048), new(2048, -2048)), (new(2048, 2048), new(-2048, -2048)),
            (new(-2048, 1), new(2048, 1)), (new(1, -2048), new(1, 2048)),
            (new(-1, -2048), new(-1, 2048)), (new(-2048, -1), new(2048, -1)),
            (new(2048, 2048), new(2048, 2048)), (new(-0.0, 0), new(0, -0.0))
        ];
        for (int offset = 0; offset < 16; offset++)
            Compare($"touching support rotation {offset}", SampledEdges(i => touching[(i + offset) % 16]));
        Compare("reversed vertical supports", SampledEdges(i => (i % 3) switch
        {
            0 => (new(1, -2048), new(1, 2048)),
            1 => (new(1, 2048), new(1, -2048)),
            _ => (new(1, -1024), new(1, 1024))
        }));
        foreach (int invalidIndex in new[] { 120, 121 })
        {
            Point2[] points = SampledEdges(i => i < 12 ? (new(0, -0.0), new(-0.0, 0))
                : (new(0, 0), new(1, 1)));
            points[invalidIndex] = new(double.NaN, 0);
            Compare($"zero samples before sampled invalid {invalidIndex}", points);
        }

        Point2[] baseGrid = Inputs.Grid(128, 0x6b792e21u);
        double[] coordinateValues =
        [
            -2049, -2048, Math.BitIncrement(-2048), -2047, -0.0, 0, double.Epsilon,
            .125, 2047, Math.BitDecrement(2048), 2048, 2049,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue
        ];
        // 0,1,120,121 are sampled; 2 and127 are not. Invalid unseen vertices must still be rejected
        // after a positive sample, retaining the reference's partial counters and rejection reason.
        foreach (int index in new[] { 0, 1, 2, 120, 121, 127 })
        foreach (double value in coordinateValues)
        foreach (bool changeX in new[] { false, true })
        {
            Point2[] points = baseGrid.ToArray();
            points[index] = changeX ? new(value, points[index].Y) : new(points[index].X, value);
            Compare($"coordinate {index}/{changeX}/{value:R}", points);
        }

        foreach (int levelCount in new[] { 15, 16, 17 })
        {
            Point2[] points = baseGrid.ToArray();
            int next = 0;
            foreach (int index in Enumerable.Range(0, points.Length).Where(i => i % 8 >= 2))
                points[index] = new(points[index].X, next++ % levelCount);
            Compare($"full level threshold {levelCount}", points);
        }
        foreach (int verticalEdges in new[] { 14, 16, 18 })
        {
            Point2[] points = SampledEdges(_ => (new(0, 0), new(1, 0)));
            // Each isolated unsampled vertex contributes two active edges across the only band.
            for (int i = 0; i < verticalEdges / 2; i++) points[i * 8 + 3] = new(0, 1);
            Require(HybridClosedAreaV2.Select(points).NonhorizontalEdges == verticalEdges,
                "The active-band threshold control no longer has the intended edge count.");
            Compare($"active threshold {verticalEdges}", points);
        }

        uint state = 0xb4829e17u;
        for (int test = 0; test < 768; test++)
        {
            int count = 64 + (int)(Next(ref state) % 961);
            int levels = new[] { 1, 2, 8, 16, 17, 4097 }[test % 6];
            var points = new Point2[count];
            for (int i = 0; i < points.Length; i++)
            {
                int x = (int)(Next(ref state) % 4097) - 2048;
                int y = (int)(Next(ref state) % (uint)levels) - levels / 2;
                points[i] = i > 0 && test % 7 == 0 && i % 3 == 0 ? points[i - 1] : new(x, y);
            }
            if (test % 11 == 0) points[^1] = points[0];
            Compare($"random {test}", points);
            if (test % 16 == 0)
            {
                Compare($"random {test} reverse", points.Reverse().ToArray());
                Compare($"random {test} axis swap", points.Select(p => new Point2(p.Y, p.X)).ToArray());
            }
        }
        Console.WriteLine($"Hybrid selector: {checks} full-record equivalence controls against frozen v2.");

        void Compare(string name, Point2[]? points)
        {
            HybridSelection expected = HybridClosedAreaV2.Select(points);
            HybridSelection actual = HybridClosedArea.Select(points);
            Require(actual == expected, name + $": selector differs; expected {expected}; actual {actual}.");
            if (publicSelector is not null)
                Require(publicSelector(points) == (expected.Backend == "IntegerScanbeam"),
                    name + ": public selector differs from the retained reference.");
            checks++;
        }
    }

    private static Point2[] SampledEdges(Func<int, (Point2 A, Point2 B)> edge)
    {
        var points = new Point2[128];
        for (int sample = 0; sample < 16; sample++)
        {
            (Point2 a, Point2 b) = edge(sample);
            points[sample * 8] = a;
            points[sample * 8 + 1] = b;
        }
        return points;
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return state;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
