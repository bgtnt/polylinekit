using System.Runtime.CompilerServices;
using PolylineKit;

namespace PolylineKit.ScanbeamBenchmarks;

internal sealed partial class GuardedDoubleSweep
{
    /// <summary>Snapshots coordinates and prepares immutable edge geometry and a sorted endpoint stream.</summary>
    /// <remarks>
    /// Preparation does not choose a fill rule or engine flags. Uncertifiable input/preparation is retained
    /// for the ordinary whole-call fallback at query time; null itself has no snapshot and is rejected here.
    /// </remarks>
    internal static PreparedPath PreparePath(Point2[] points) => new(points);

    internal double MeasureIntersection(PreparedPath first, PreparedPath second,
        PathFillRule rule = PathFillRule.NonZero) => PreparedPath.Measure(this, first, second, rule);

    /// <summary>An immutable snapshot shareable by separate, non-thread-safe sweep instances.</summary>
    internal sealed class PreparedPath
    {
        private readonly Point2[] snapshot;
        private readonly Edge[] preparedEdges = [];
        private readonly ScalarOrderFilter.PreparedEdge[] preparedScalarEdges = [];
        private readonly Endpoint[] sortedEndpoints = [];
        private readonly Bounds bounds;
        private readonly string? inputFailure, edgePreparationFailure;

        internal int VertexCount => snapshot.Length;
        /// <summary>The number of retained nonhorizontal edge records; zero if preparation failed.</summary>
        internal int EdgeCount => preparedEdges.Length;
        /// <summary>
        /// Retained array element bytes, including struct padding. Excludes array/object headers,
        /// references, scalar fields, allocator overhead, transient preparation and engine scratch.
        /// </summary>
        internal long PayloadBytes => (long)snapshot.Length * Unsafe.SizeOf<Point2>() +
            (long)preparedEdges.Length * Unsafe.SizeOf<Edge>() +
            (long)preparedScalarEdges.Length * Unsafe.SizeOf<ScalarOrderFilter.PreparedEdge>() +
            (long)sortedEndpoints.Length * Unsafe.SizeOf<Endpoint>();

        internal PreparedPath(Point2[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            snapshot = (Point2[])points.Clone();
            if (snapshot.Length < 3) { inputFailure = "input-contract"; return; }
            if (snapshot.Length > VertexBudget) { inputFailure = "vertex-budget"; return; }
            try
            {
                // The same full-input validation as the ordinary path, operating only on our clone.
                bounds = CopyValidated(snapshot, snapshot, 0);
            }
            catch (Uncertified failure)
            {
                inputFailure = failure.Reason;
                return;
            }

            int count = 0;
            for (int i = 0; i < snapshot.Length; i++)
                if (snapshot[i].Y != snapshot[i + 1 == snapshot.Length ? 0 : i + 1].Y) count++;
            try
            {
                Edge[] edgeData = new Edge[count];
                ScalarOrderFilter.PreparedEdge[] scalarData = new ScalarOrderFilter.PreparedEdge[count];
                Endpoint[] endpointData = new Endpoint[2 * count];
                int edgeAt = 0, endpointAt = 0;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Point2 a = snapshot[i], b = snapshot[i + 1 == snapshot.Length ? 0 : i + 1];
                    if (a.Y == b.Y) continue;
                    int delta = a.Y < b.Y ? 1 : -1;
                    if (delta < 0) (a, b) = (b, a);
                    // Preserve the unprepared arithmetic and edge enumeration exactly. The query supplies
                    // the loop tag and maps these local edge IDs into the same concatenated pair order.
                    Interval slope = Interval.Divide(Interval.Difference(b.X, a.X), Interval.Difference(b.Y, a.Y));
                    edgeData[edgeAt] = new(a, b, delta, 0, slope);
                    scalarData[edgeAt] = ScalarOrderFilter.Prepare(a.X, a.Y, b.Y, slope.Lo, slope.Hi);
                    endpointData[endpointAt++] = new(a.Y, edgeAt, true);
                    endpointData[endpointAt++] = new(b.Y, edgeAt, false);
                    edgeAt++;
                }
                endpointData.AsSpan().Sort(CompareEndpoints);
                preparedEdges = edgeData;
                preparedScalarEdges = scalarData;
                sortedEndpoints = endpointData;
            }
            catch (Exception exception) when (FindReason(exception) is not null)
            {
                // In particular, a very steep finite-input edge can have an uncertifiable slope. Do not
                // confuse this with invalid coordinates: the baseline returns zero for disjoint bounds
                // before it constructs any slopes, even for these inputs.
                edgePreparationFailure = FindReason(exception);
            }
        }

        internal static double Measure(GuardedDoubleSweep sweep, PreparedPath first, PreparedPath second,
            PathFillRule rule)
        {
            sweep.ResetState();
            if (rule != PathFillRule.NonZero && rule != PathFillRule.EvenOdd)
                return sweep.Fallback(first?.snapshot!, second?.snapshot!, rule, "input-contract");
            if (first is null || second is null)
                return sweep.Fallback(first?.snapshot!, second?.snapshot!, rule, "input-contract");
            if (first.VertexCount < 3 || second.VertexCount < 3)
                return sweep.Fallback(first.snapshot, second.snapshot, rule, "input-contract");
            if (first.VertexCount > VertexBudget || second.VertexCount > VertexBudget - first.VertexCount)
                return sweep.Fallback(first.snapshot, second.snapshot, rule, "vertex-budget");
            // Both complete input contracts are checked before an AABB result, in baseline input order.
            if (first.inputFailure is not null)
                return sweep.Fallback(first.snapshot, second.snapshot, rule, first.inputFailure);
            if (second.inputFailure is not null)
                return sweep.Fallback(first.snapshot, second.snapshot, rule, second.inputFailure);
            Bounds a = first.bounds, b = second.bounds;
            if (a.HasNoArea || b.HasNoArea || a.NoAreaOverlap(b))
            {
                sweep.LastErrorBound = 0;
                return 0;
            }
            if (first.edgePreparationFailure is not null)
                return sweep.Fallback(first.snapshot, second.snapshot, rule, first.edgePreparationFailure);
            if (second.edgePreparationFailure is not null)
                return sweep.Fallback(first.snapshot, second.snapshot, rule, second.edgePreparationFailure);
            try
            {
                sweep.EnsureVertices(first.VertexCount + second.VertexCount);
                sweep.nonZero = rule == PathFillRule.NonZero;
                first.CopyEdgesTo(sweep, 0, 0);
                second.CopyEdgesTo(sweep, first.EdgeCount, 1);
                sweep.edgeCount = first.EdgeCount + second.EdgeCount;
                Array.Fill(sweep.positions, -1, 0, sweep.edgeCount);
                MergeEndpoints(sweep, first, second);
                return sweep.SweepSortedEndpoints(a, b);
            }
            catch (Exception exception) when (FindReason(exception) is not null)
            {
                return sweep.Fallback(first.snapshot, second.snapshot, rule, FindReason(exception)!);
            }
        }

        private void CopyEdgesTo(GuardedDoubleSweep sweep, int offset, int loop)
        {
            for (int i = 0; i < preparedEdges.Length; i++)
            {
                Edge edge = preparedEdges[i];
                sweep.edges[offset + i] = new(edge.Lower, edge.Upper, edge.Delta, loop, edge.Slope);
            }
            if (sweep.scalarOrderFilter)
                Array.Copy(preparedScalarEdges, 0, sweep.scalarEdges, offset, preparedScalarEdges.Length);
        }

        private static void MergeEndpoints(GuardedDoubleSweep sweep, PreparedPath first, PreparedPath second)
        {
            Endpoint[] a = first.sortedEndpoints, b = second.sortedEndpoints;
            int ai = 0, bi = 0, at = 0, offset = first.EdgeCount;
            while (ai < a.Length && bi < b.Length)
            {
                Endpoint local = b[bi];
                Endpoint mapped = new(local.Y, local.Edge + offset, local.Starts);
                // Edge IDs are the final tie-breaker. Merging by Y alone would change endpoint insertion
                // order and could alter interval decisions or fallback in an otherwise identical sweep.
                if (CompareEndpoints(a[ai], mapped) <= 0) sweep.endpoints[at++] = a[ai++];
                else { sweep.endpoints[at++] = mapped; bi++; }
            }
            if (ai < a.Length)
            {
                Array.Copy(a, ai, sweep.endpoints, at, a.Length - ai);
                at += a.Length - ai;
            }
            while (bi < b.Length)
            {
                Endpoint local = b[bi++];
                sweep.endpoints[at++] = new(local.Y, local.Edge + offset, local.Starts);
            }
            sweep.endpointCount = at;
        }
    }
}
