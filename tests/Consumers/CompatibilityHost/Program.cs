using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using PolylineKit;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CompatibilityHost <precompiled-legacy-consumer.dll> <expected-framework>");
    return 2;
}
Assembly core = typeof(PolylineArea).Assembly, winding = typeof(WindingArea).Assembly;
if (core == winding || core.GetName().Name != "PolylineKit" || winding.GetName().Name != "PolylineKit.Winding")
    throw new InvalidOperationException("The compatibility host must load both extracted assemblies.");
foreach (Assembly assembly in new[] { core, winding })
{
    string? target = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
    if (target != args[1]) throw new InvalidOperationException($"Unexpected target for {assembly.GetName().Name}: {target}");
}

// The host supplies the current dependency graph. Only this old compiled component is loaded dynamically;
// it is not rebuilt, recompiled, rewritten, or linked against PolylineKit.Winding before execution.
Assembly legacy = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
AssemblyName[] references = legacy.GetReferencedAssemblies();
if (!references.Any(r => r.Name == "PolylineKit") || references.Any(r => r.Name == "PolylineKit.Winding"))
    throw new InvalidOperationException("The consumer was not compiled against the monolithic assembly.");
MethodInfo run = legacy.GetType("LegacyApiConsumer.LegacyEntry", throwOnError: true)!.GetMethod("Run")!;
try
{
    int checks = (int)run.Invoke(null, null)!;
    Console.WriteLine($"PASS: {checks} precompiled legacy consumer checks; parent and winding target: {args[1]}.");
    return 0;
}
catch (TargetInvocationException error) when (error.InnerException is not null)
{
    Console.Error.WriteLine(error.InnerException);
    return 1;
}
