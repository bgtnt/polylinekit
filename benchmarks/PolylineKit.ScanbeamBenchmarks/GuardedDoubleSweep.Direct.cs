using System.Runtime.CompilerServices;

namespace PolylineKit.ScanbeamBenchmarks;

internal sealed partial class GuardedDoubleSweep
{
    private readonly bool directPreparedEdges, borrowPreparedScalars;
    // Only the current prepared query can borrow these immutable arrays. The scalar-only experiment
    // keeps copied geometry while borrowing filter records. The original scratch allocation is retained.
    private Edge[]? borrowedFirstEdges, borrowedSecondEdges;
    private ScalarOrderFilter.PreparedEdge[]? borrowedFirstScalarEdges, borrowedSecondScalarEdges;
    private int borrowedFirstEdgeCount;

    /// <summary>Filter records actually copied from prepared snapshots in the current query.</summary>
    internal int PreparedScalarRecordsCopied { get; private set; }
    /// <summary>Prepared records bound for filtering without copying; zero when the filter is disabled.</summary>
    internal int PreparedScalarRecordsBorrowed { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref readonly Edge EdgeAt(int edge)
    {
        Edge[]? first = borrowedFirstEdges;
        if (first is null) return ref edges[edge];
        if (edge < borrowedFirstEdgeCount) return ref first[edge];
        return ref borrowedSecondEdges![edge - borrowedFirstEdgeCount];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref readonly ScalarOrderFilter.PreparedEdge ScalarEdgeAt(int edge)
    {
        ScalarOrderFilter.PreparedEdge[]? first = borrowedFirstScalarEdges;
        if (first is null) return ref scalarEdges[edge];
        if (edge < borrowedFirstEdgeCount) return ref first[edge];
        return ref borrowedSecondScalarEdges![edge - borrowedFirstEdgeCount];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFirstLoop(int edgeId, in Edge edge) =>
        borrowedFirstEdges is null ? edge.Loop == 0 : edgeId < borrowedFirstEdgeCount;

    private void ClearBorrowedGeometry()
    {
        borrowedFirstEdges = borrowedSecondEdges = null;
        borrowedFirstScalarEdges = borrowedSecondScalarEdges = null;
        borrowedFirstEdgeCount = 0;
    }
}
