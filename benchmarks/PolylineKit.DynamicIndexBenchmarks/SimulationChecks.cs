using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using PolylineKit;

namespace DynamicIndexBenchmarks;

/// <summary>Independent analytic decisions, not results recorded from the index under test.</summary>
internal static class SimulationChecks
{
    private sealed record Contact(string Name, Point2 U, Point2 V, bool Incoming, bool Outgoing, bool Blocked);

    internal static int Run()
    {
        int checks = CheckContacts();
        // Reflection reverses ring orientation without changing original vertex IDs or distance.
        foreach (bool reflected in new[] { false, true })
        {
            Point2[] Map(Point2[] points) => points.Select(p => reflected ? new Point2(-p.X, p.Y) : p).ToArray();
            checks += CheckTrace("collinear-rectangle", Map([
                new(0,0), new(5,0), new(10,0), new(10,5), new(10,10), new(5,10), new(0,10), new(0,5)]),
                8, 4, 16, 8, [1,3,5,7], [true,true,true,true], [0,2,4,6],
                [[0,1,2,7], [0,2,3,4], [2,4,5,6], [0,4,6,7]]);
            checks += CheckTrace("four-vertex-stop", Map([new(0,0), new(10,0), new(10,10), new(0,10)]),
                0, 0, 0, 0, [], [], [0,1,2,3], []);
            checks += CheckTrace("no-eligible-proposal", Map([new(0,0), new(10,0), new(12,5), new(6,10), new(0,5)]),
                5, 0, 0, 0, [], [], [0,1,2,3,4], []);
            // The removed point's distance is exactly the double tolerance (0.01 * 10).
            checks += CheckTrace("inclusive-distance", Map([new(0,0), new(5,.1), new(10,0), new(10,10), new(0,10)]),
                2, 1, 4, 2, [1], [true], [0,2,3,4], [[0,1,2,4]]);
            checks += CheckTrace("above-distance", Map([new(0,0), new(5,Math.BitIncrement(.1)), new(10,0), new(10,10), new(0,10)]),
                5, 0, 0, 0, [], [], [0,1,2,3,4], []);
            // The shallow top corner is eligible, but its shortcut crosses both sides of a
            // thin inward finger. All four nondeleted candidates must still be tested.
            checks += CheckTrace("blocked-inward-finger", Map([
                new(0,0), new(5,.125), new(10,0), new(10,-20), new(5,-20),
                new(5,.0625), new(4,.0625), new(4,-20), new(0,-20)]),
                9, 1, 6, 4, [1], [false], [0,1,2,3,4,5,6,7,8], [[0,1,2,4,6,8]]);
        }
        return checks;
    }

    private static int CheckContacts()
    {
        Contact[] cases =
        [
            new("proper crossing", new(2,-2), new(2,2), false, false, true),
            new("parallel disjoint", new(0,1), new(4,1), false, false, false),
            new("collinear disjoint", new(5,0), new(8,0), false, false, false),
            new("partial overlap", new(2,0), new(6,0), false, false, true),
            new("contained overlap", new(1,0), new(3,0), false, false, true),
            new("containing overlap", new(-1,0), new(5,0), false, false, true),
            new("identical segment", new(0,0), new(4,0), false, false, true),
            new("identical adjacent segment", new(0,0), new(4,0), true, true, true),
            new("legal incoming endpoint", new(-1,-1), new(0,0), true, false, false),
            new("legal incoming collinear endpoint", new(-1,0), new(0,0), true, false, false),
            new("legal outgoing endpoint", new(4,0), new(5,1), false, true, false),
            new("legal outgoing collinear endpoint", new(4,0), new(5,0), false, true, false),
            new("nonadjacent start contact", new(-1,-1), new(0,0), false, false, true),
            new("nonadjacent end contact", new(4,0), new(5,1), false, false, true),
            new("interior T contact", new(2,-1), new(2,0), false, false, true),
            new("interior point segment", new(2,0), new(2,0), false, false, true),
            new("start contact with wrong adjacency", new(-1,-1), new(0,0), false, true, true),
            new("end contact with wrong adjacency", new(4,0), new(5,1), true, false, true),
            new("incoming overlap", new(0,0), new(1,0), true, false, true),
            new("outgoing overlap", new(3,0), new(4,0), false, true, true),
            new("crossing with adjacency flag", new(2,-2), new(2,2), true, false, true),
            new("nonadjacent point at start", new(0,0), new(0,0), false, false, true),
            new("near start exterior", new(-1,-1), new(0,-double.Epsilon), false, false, false),
            new("near end interior crossing", new(Math.BitDecrement(4d),-1), new(Math.BitDecrement(4d),1), false, false, true)
        ];
        int checks = 0;
        var intersector = new RobustLineIntersector();
        foreach (var item in cases)
        for (int transform = 0; transform < 4; transform++)
        for (int reverseChord = 0; reverseChord < 2; reverseChord++)
        for (int reverseEdge = 0; reverseEdge < 2; reverseEdge++)
        {
            Coordinate Map(Point2 p) => transform switch
            {
                0 => new(p.X, p.Y), 1 => new(p.X, -p.Y),
                2 => new(p.Y, p.X), _ => new(-p.Y, p.X)
            };
            Coordinate[] points = [Map(new(0,0)), Map(new(4,0)), Map(item.U), Map(item.V)];
            bool actual = Simulation.Blocks(intersector, points, reverseChord, 1-reverseChord,
                2+reverseEdge, 3-reverseEdge,
                reverseChord == 0 ? item.Incoming : item.Outgoing,
                reverseChord == 0 ? item.Outgoing : item.Incoming);
            Require(actual == item.Blocked, item.Name + ": incorrect blocking decision.");
            checks++;
        }
        return checks;
    }

    private static int CheckTrace(string name, Point2[] points, long proposals, long queries, long candidates,
        long predicates, int[] vertices, bool[] decisions, int[] finalIds, int[][] candidateIds)
    {
        Workloads.Validate(name, points);
        var scenario = Simulation.Prepare(new Workload(name, "analytic-control", points));
        var expected = scenario.Expected;
        Require(expected.Proposals == proposals && expected.Queries == queries &&
            expected.Candidates == candidates && expected.Predicates == predicates &&
            expected.Accepted == decisions.Count(v => v) && expected.Remaining == finalIds.Length,
            name + ": scheduler/predicate counters differ from the analytic expectation.");
        Require(scenario.Steps.Select(s => s.Vertex).SequenceEqual(vertices), name + ": proposal order differs.");
        Require(scenario.Steps.Select(s => s.Accepted).SequenceEqual(decisions), name + ": acceptance differs.");
        Require(scenario.Simplified.SequenceEqual(finalIds.Select(i => points[i])), name + ": final vertices differ.");
        Require(scenario.Steps.Length == candidateIds.Length, name + ": candidate trace length differs.");
        for (int i = 0; i < candidateIds.Length; i++)
            Require(scenario.Steps[i].Candidates.SequenceEqual(candidateIds[i]), name + ": candidate set differs.");
        foreach (IEdgeIndexFactory factory in new IEdgeIndexFactory[] { new LinearIndexFactory(), new FixedSlotIndexFactory() })
        {
            Require(Simulation.Run(scenario, factory) == expected, name + ": full run counters/digest differ for " + factory.Name);
            var replay = Simulation.Replay(scenario, factory, verify: true);
            Require(replay.Queries == queries && replay.Candidates == candidates &&
                replay.Accepted == expected.Accepted && replay.Remaining == expected.Remaining,
                name + ": replay counters differ for " + factory.Name);
        }
        return 9 + candidateIds.Length;
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
