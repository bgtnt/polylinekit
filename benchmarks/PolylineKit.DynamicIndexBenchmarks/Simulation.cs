using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using PolylineKit;

namespace DynamicIndexBenchmarks;

internal readonly record struct Counters(long Proposals, long Queries, long Candidates, long Predicates,
    long Accepted, long Remaining, ulong Digest)
{
    internal Counters Add(Counters b) => new(Proposals + b.Proposals, Queries + b.Queries,
        Candidates + b.Candidates, Predicates + b.Predicates, Accepted + b.Accepted, Remaining + b.Remaining,
        Simulation.Mix(Digest ^ b.Digest));
}

internal sealed record Step(int Vertex, int KeptId, int RemovedId, Box2 OldKept, Box2 OldRemoved,
    Box2 Shortcut, int[] Candidates, bool Accepted);
internal sealed record Scenario(Workload Input, Box2[] InitialBounds, Coordinate[] Coordinates,
    Step[] Steps, Counters Expected, Point2[] Simplified);

internal static class Simulation
{
    internal static Scenario Prepare(Workload input)
    {
        var boxes = Enumerable.Range(0, input.Points.Length)
            .Select(i => Bounds(input.Points[i], input.Points[(i + 1) % input.Points.Length])).ToArray();
        var coordinates = input.Points.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        var steps = new List<Step>();
        var provisional = new Scenario(input, boxes, coordinates, [], default, []);
        var value = Run(provisional, new LinearIndexFactory(), steps, out var simplified);
        ValidateRing(simplified);
        return provisional with { Steps = steps.ToArray(), Expected = value, Simplified = simplified };
    }

    internal static Counters Run(Scenario scenario, IEdgeIndexFactory factory) => Run(scenario, factory, null, out _);

    internal static (Counters Value, Step[] Steps, Point2[] Points) Capture(Scenario scenario, IEdgeIndexFactory factory)
    {
        var steps = new List<Step>();
        var value = Run(scenario, factory, steps, out var points);
        return (value, steps.ToArray(), points);
    }

    // All candidate edges are tested, without traversal-order-dependent early termination.
    // Preparation, mutable simulation storage and the index are included in a fresh run.
    private static Counters Run(Scenario scenario, IEdgeIndexFactory factory, List<Step>? record, out Point2[] simplified)
    {
        Point2[] points = scenario.Input.Points; int size = points.Length;
        var next = new int[size]; var previous = new int[size]; var active = new bool[size];
        var boxes = (Box2[])scenario.InitialBounds.Clone(); var ids = new int[size];
        for (int i = 0; i < size; i++) { next[i] = (i + 1) % size; previous[i] = (i + size - 1) % size; active[i] = true; }
        var index = factory.Create(boxes); var intersector = new RobustLineIntersector();
        double tolerance = .01 * Math.Max(points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
        long proposals = 0, queries = 0, candidates = 0, predicates = 0, accepted = 0;
        int remaining = size; ulong digest = 0;
        for (int pass = 0; pass < 3 && remaining > 4; pass++)
        {
            long before = accepted;
            for (int b = 0; b < size && remaining > 4; b++)
            {
                if (!active[b]) continue;
                proposals++;
                int a = previous[b], c = next[b];
                if (points[a].Equals(points[c]) || !NearSegment(points[b], points[a], points[c], tolerance)) continue;
                queries++;
                Box2 shortcut = Bounds(points[a], points[c]);
                int count = index.Query(shortcut, ids); candidates += count;
                digest = Mix(digest ^ CandidateDigest(ids.AsSpan(0, count)) ^ (uint)b);
                bool blocked = false;
                for (int j = 0; j < count; j++)
                {
                    int edge = ids[j];
                    if (edge == a || edge == b) continue;
                    predicates++;
                    if (Blocks(intersector, scenario.Coordinates, a, c, edge, next[edge], edge == previous[a], edge == c)) blocked = true;
                }
                if (record is not null)
                {
                    int[] expected = ids.AsSpan(0, count).ToArray(); Array.Sort(expected);
                    record.Add(new(b, a, b, boxes[a], boxes[b], shortcut, expected, !blocked));
                }
                if (blocked) continue;
                index.ApplyShortcut(a, boxes[a], shortcut, b, boxes[b]);
                boxes[a] = shortcut; next[a] = c; previous[c] = a; active[b] = false;
                accepted++; remaining--; digest = Mix(digest ^ (uint)(b + 1) ^ 0xd6e8feb86659fd93UL);
            }
            if (before == accepted) break;
        }
        // Consume actual final vertex identities even when no output contour is requested.
        for (int i = 0; i < size; i++) if (active[i]) digest = Mix(digest ^ (uint)(i + 1));
        simplified = record is null ? [] : Enumerable.Range(0, size).Where(i => active[i]).Select(i => points[i]).ToArray();
        return new(proposals, queries, candidates, predicates, accepted, remaining, digest);
    }

    internal static Counters Replay(Scenario scenario, IEdgeIndexFactory factory, bool verify = false)
    {
        var index = factory.Create(scenario.InitialBounds); var ids = new int[scenario.InitialBounds.Length];
        long candidates = 0, accepted = 0; ulong digest = 0;
        foreach (Step step in scenario.Steps)
        {
            int count = index.Query(step.Shortcut, ids); candidates += count;
            if (verify)
            {
                Array.Sort(ids, 0, count);
                Require(ids.AsSpan(0, count).SequenceEqual(step.Candidates), $"{factory.Name}: candidate IDs differ in {scenario.Input.Id} at vertex {step.Vertex}.");
            }
            digest = Mix(digest ^ CandidateDigest(ids.AsSpan(0, count)) ^ (uint)step.Vertex);
            if (step.Accepted)
            {
                index.ApplyShortcut(step.KeptId, step.OldKept, step.Shortcut, step.RemovedId, step.OldRemoved);
                accepted++; digest = Mix(digest ^ (uint)(step.Vertex + 1) ^ 0xd6e8feb86659fd93UL);
            }
        }
        return new(0, scenario.Steps.Length, candidates, 0, accepted, scenario.InitialBounds.Length - accepted, digest);
    }

    internal static Counters Build(Scenario scenario, IEdgeIndexFactory factory)
    {
        var index = factory.Create(scenario.InitialBounds); GC.KeepAlive(index);
        return new(0, 0, 0, 0, 0, scenario.InitialBounds.Length, (uint)scenario.InitialBounds.Length);
    }

    internal static bool Blocks(RobustLineIntersector intersector, Coordinate[] p, int a, int c, int u, int v,
        bool incomingAdjacent, bool outgoingAdjacent)
    {
        intersector.ComputeIntersection(p[a], p[c], p[u], p[v]);
        if (!intersector.HasIntersection) return false;
        if (intersector.IntersectionNum != 1) return true;
        var contact = intersector.GetIntersection(0);
        return !((incomingAdjacent && contact.Equals2D(p[a])) || (outgoingAdjacent && contact.Equals2D(p[c])));
    }

    private static bool NearSegment(Point2 p, Point2 a, Point2 b, double tolerance)
    {
        double x = b.X - a.X, y = b.Y - a.Y;
        double t = Math.Clamp(((p.X - a.X) * x + (p.Y - a.Y) * y) / (x * x + y * y), 0, 1);
        double dx = p.X - (a.X + t * x), dy = p.Y - (a.Y + t * y);
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    internal static Box2 Bounds(Point2 a, Point2 b) => new(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), Math.Max(a.X,b.X), Math.Max(a.Y,b.Y));
    internal static ulong Mix(ulong value)
    {
        unchecked { value ^= value >> 30; value *= 0xbf58476d1ce4e5b9UL; value ^= value >> 27; value *= 0x94d049bb133111ebUL; return value ^ (value >> 31); }
    }
    private static ulong CandidateDigest(ReadOnlySpan<int> ids)
    {
        ulong value = 0; unchecked { foreach (int id in ids) value += Mix((uint)id + 1); }
        return value ^ (uint)ids.Length;
    }
    internal static void ValidateRing(Point2[] points)
    {
        var coordinates = points.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(points[0].X, points[0].Y)).ToArray();
        var polygon = new GeometryFactory().CreatePolygon(coordinates);
        Require(polygon.IsValid && polygon.IsSimple && polygon.Area > 0, "Simplification produced an invalid or zero-area ring.");
    }
    internal static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}
