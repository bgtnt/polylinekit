using PolylineKit;

Point2[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
Point2[] shifted = [new(1, 0), new(3, 0), new(3, 2), new(1, 2)];
var closed = WindingArea.ClosedPath(square);
if (closed.NonZero != 4 || closed.EvenOdd != 4 || closed.Signed != 4 || closed.AbsoluteWinding != 4)
    throw new InvalidOperationException("Standalone closed-path values disagree with the analytic square.");
foreach (PathFillRule rule in new[] { PathFillRule.NonZero, PathFillRule.EvenOdd })
{
    var overlap = WindingArea.FilledRegions(square, shifted, rule);
    if (WindingArea.IntersectionArea(square, shifted, rule) != 2)
        throw new InvalidOperationException("Standalone intersection-only value disagrees with analytic squares.");
    if (overlap.FirstArea != 4 || overlap.SecondArea != 4 || overlap.IntersectionArea != 2 || overlap.UnionArea != 6
        || overlap.SymmetricDifferenceArea != 4 || overlap.IntersectionOverUnion != 1.0 / 3)
        throw new InvalidOperationException("Standalone filled-region values disagree with analytic squares.");
}
var bridge = WindingArea.EndpointBridged([new(0, 0), new(2, 0)], [new(0, 2), new(2, 2)]);
if (bridge.NonZero != 4) throw new InvalidOperationException("Standalone bridged area is incorrect.");

var framework = new HashSet<string>(StringComparer.Ordinal)
{
    "mscorlib", "netstandard", "System.Private.CoreLib", "System.Runtime", "System.Collections",
    "System.Collections.Concurrent", "System.Numerics", "System.Numerics.Vectors", "System.Runtime.Numerics",
    "System.Runtime.Intrinsics", "System.Runtime.Extensions", "System.Runtime.CompilerServices.Unsafe",
    "System.Threading", "System.Memory", "System.Linq"
};
var assembly = typeof(WindingArea).Assembly;
if (assembly.GetName().Name != "PolylineKit.Winding" || typeof(Point2).Assembly != assembly
    || assembly.GetReferencedAssemblies().Any(r => r.Name is null || !framework.Contains(r.Name)))
    throw new InvalidOperationException("The standalone library has an unexpected runtime dependency.");
if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "PolylineKit" or "Clipper2Lib"))
    throw new InvalidOperationException("The standalone consumer unexpectedly loaded the broader library or Clipper2.");
Console.WriteLine("PASS: standalone net10.0 consumer; analytic areas and framework-only runtime references.");
return 0;
