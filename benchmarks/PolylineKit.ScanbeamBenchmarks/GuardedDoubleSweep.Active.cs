namespace PolylineKit.ScanbeamBenchmarks;

internal sealed partial class GuardedDoubleSweep
{
    private readonly bool optimizeActivePasses;

    /// <summary>Bands whose complete inversion enumeration found no interior crossing.</summary>
    internal int NoCrossingBandCount { get; private set; }
    internal int CrossingBandCount { get; private set; }
    /// <summary>Actual initial copy/gap-start/prefix visits, excluding crossing prefix updates.</summary>
    internal long BandInitializationVisits { get; private set; }
    /// <summary>Actual writes to the top-order buffer, including insertion-sort shifts.</summary>
    internal long TopOrderWrites { get; private set; }
    internal long TopOrderVerificationVisits { get; private set; }

    private void ResetActivePassDiagnostics()
    {
        NoCrossingBandCount = CrossingBandCount = 0;
        BandInitializationVisits = TopOrderWrites = TopOrderVerificationVisits = 0;
    }

    private void ProcessBandWithFewerPasses(double bottom, double top)
    {
        crossingCount = 0;
        // Only the sorted prefix is read by insertion sort. Unvisited IDs come from the unchanged status,
        // so its comparisons, inversions and crossing construction retain the original enumeration order.
        if (activeCount > 0) { topOrder[0] = active[0]; TopOrderWrites++; }
        for (int i = 1; i < activeCount; i++)
        {
            int id = active[i], j = i;
            while (j > 0 && CompareAtEndpoint(id, topOrder[j - 1], top, true) < 0)
            {
                Visit();
                int other = topOrder[j - 1];
                if (crossingCount == BandEventBudget) throw new Uncertified("event-budget");
                if (crossingCount == crossings.Length) Array.Resize(ref crossings, Math.Max(256, crossings.Length * 2));
                Level level = ConstructCrossing(other, id, bottom, top);
                crossings[crossingCount] = new(level, crossingCount);
                crossingCount++; EventCount++;
                topOrder[j] = other; TopOrderWrites++; j--;
            }
            topOrder[j] = id; TopOrderWrites++;
        }

        Level start = Level.At(bottom), finish = Level.At(top);
        if (crossingCount == 0)
        {
            NoCrossingBandCount++;
            // No inversion means no top-order shift; no event can mutate the active order. Stream the
            // same winding prefixes and filled gaps without materializing either per-band prefix array.
            int a = 0, b = 0;
            for (int i = 0; i + 1 < activeCount; i++)
            {
                Visit();
                int left = active[i];
                ref readonly Edge edge = ref EdgeAt(left);
                if (IsFirstLoop(left, in edge)) a += edge.Delta; else b += edge.Delta;
                if (Filled(a) && Filled(b))
                    AccumulateFilledGap(left, active[i + 1], start, finish);
            }
            return;
        }

        CrossingBandCount++;
        // A crossing band needs random-access prefixes and gap starts. Initialize both in one walk;
        // crossing-local updates, event order, verification and final gap accumulation stay separate.
        int windingA = 0, windingB = 0;
        for (int i = 0; i < activeCount; i++)
        {
            Visit(); BandInitializationVisits++;
            gapStarts[i] = start; prefixA[i] = windingA; prefixB[i] = windingB;
            int id = active[i];
            ref readonly Edge edge = ref EdgeAt(id);
            if (IsFirstLoop(id, in edge)) windingA += edge.Delta; else windingB += edge.Delta;
        }
        crossings.AsSpan(0, crossingCount).Sort(crossingComparison);
        for (int c = 1; c < crossingCount; c++)
            if (!(crossings[c - 1].Level.Y.Hi < crossings[c].Level.Y.Lo)) throw new Uncertified("event-order");
        for (int c = 0; c < crossingCount; c++)
        {
            Charge();
            Level level = crossings[c].Level;
            int lo = Math.Min(positions[level.A], positions[level.B]), hi = Math.Max(positions[level.A], positions[level.B]);
            if (lo < 0 || hi != lo + 1) throw new Uncertified("nonadjacent-event");
            int wA = prefixA[lo], wB = prefixB[lo];
            for (int i = Math.Max(0, lo - 1); i <= Math.Min(activeCount - 2, hi); i++) AccumulateGap(i, level);
            (active[lo], active[hi]) = (active[hi], active[lo]);
            positions[active[lo]] = lo; positions[active[hi]] = hi;
            SetWinding(lo, hi + 1, wA, wB);
        }
        for (int i = 0; i < activeCount; i++)
        {
            Visit(); TopOrderVerificationVisits++;
            if (active[i] != topOrder[i]) throw new Uncertified("top-order-status");
        }
        for (int i = 0; i + 1 < activeCount; i++) AccumulateGap(i, finish);
    }
}
