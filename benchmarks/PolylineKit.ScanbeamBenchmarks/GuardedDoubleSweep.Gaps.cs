using System.Runtime.CompilerServices;

namespace PolylineKit.ScanbeamBenchmarks;

internal sealed partial class GuardedDoubleSweep
{
    private readonly bool coalesceGaps;
    private PendingGap[] pendingGaps = [];
    private int gapGeneration;

    private struct PendingGap
    {
        internal int Generation, Right;
        internal Level Start, Finish;
    }

    /// <summary>Filled, nonzero-height pieces emitted by the unchanged sweep.</summary>
    internal long GapContributionCount { get; private set; }
    /// <summary>Actual interval trapezoid integration attempts, including a failing attempt.</summary>
    internal long GapIntegrationCount { get; private set; }
    internal long GapMergedCount { get; private set; }
    /// <summary>Actual calls, including crossing construction and zero-width shortcuts.</summary>
    internal long HorizontalDifferenceEvaluationCount { get; private set; }
    /// <summary>Retained new array element bytes only; excludes headers and all existing workspace.</summary>
    internal long GapWorkspacePayloadBytes => (long)pendingGaps.Length * Unsafe.SizeOf<PendingGap>();

    private void ResetGapContributions()
    {
        GapContributionCount = GapIntegrationCount = GapMergedCount = HorizontalDifferenceEvaluationCount = 0;
        if (!coalesceGaps) return;
        if (gapGeneration == int.MaxValue)
        {
            Array.Clear(pendingGaps);
            gapGeneration = 1;
        }
        else gapGeneration++;
    }

    private void EmitGapContribution(int left, int right, Level start, Level finish)
    {
        // Charge the additional state access even though merging removes later numerical work.
        Charge();
        ref PendingGap pending = ref pendingGaps[left];
        if (pending.Generation == gapGeneration)
        {
            // Do not amplify uncertain crossing-height enclosures over a larger trapezoid. Preserve
            // those boundary pieces separately; point-valued levels need no new tolerance or rounding.
            if (pending.Right == right && pending.Start.Y.IsPoint && pending.Finish.Y.IsPoint &&
                start.Y.IsPoint && finish.Y.IsPoint && SameLevel(pending.Finish, start))
            {
                pending.Finish = finish;
                GapMergedCount++;
                return;
            }
            // Capture IDs and levels, not current active positions or winding prefixes. The old pair
            // may already have left the active status; its geometry is immutable for this whole query.
            IntegratePendingGap(left, in pending);
        }
        pending.Generation = gapGeneration;
        pending.Right = right;
        pending.Start = start;
        pending.Finish = finish;
    }

    private void FlushPendingGaps()
    {
        // Only reached after successful topology processing. An earlier fallback abandons these
        // pending intervals; next-call generation changes prevent them from leaking into another query.
        for (int left = 0; left < edgeCount; left++)
        {
            Charge();
            ref PendingGap pending = ref pendingGaps[left];
            if (pending.Generation != gapGeneration) continue;
            IntegratePendingGap(left, in pending);
            pending.Generation = 0;
        }
    }

    private void IntegratePendingGap(int left, in PendingGap pending)
    {
        Interval height = Interval.Subtract(pending.Finish.Y, pending.Start.Y).Nonnegative();
        IntegrateGap(left, pending.Right, pending.Start, pending.Finish, height);
    }

    private static bool SameLevel(Level first, Level second) => first.A == second.A && first.B == second.B &&
        BitConverter.DoubleToInt64Bits(first.Y.Lo) == BitConverter.DoubleToInt64Bits(second.Y.Lo) &&
        BitConverter.DoubleToInt64Bits(first.Y.Hi) == BitConverter.DoubleToInt64Bits(second.Y.Hi);
}
