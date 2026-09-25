using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Executable contract for the independently consumable winding assembly.</summary>
internal static class WindingAssemblyChecks
{
    private static int passed;
    private static readonly string[] PublicTypes =
    [
        "PolylineKit.PathFillRule", "PolylineKit.Point2", "PolylineKit.PolylineArea", "PolylineKit.RegionOverlapResult", "PolylineKit.WindingArea",
        "PolylineKit.WindingAreaResult", "PolylineKit.WindingOverlapResult"
    ];
    private static readonly string[] ForwardedTypes =
    [
        "PolylineKit.PathFillRule", "PolylineKit.Point2", "PolylineKit.PolylineArea", "PolylineKit.WindingArea",
        "PolylineKit.WindingAreaResult", "PolylineKit.WindingOverlapResult"
    ];

    // Do not accept arbitrary System.* names: these are concrete framework reference identities.
    // The portable target normally references netstandard; the modern target names individual facades.
    private static readonly HashSet<string> FrameworkReferences = new(StringComparer.Ordinal)
    {
        "mscorlib", "netstandard", "System.Private.CoreLib", "System.Runtime",
        "System.Collections", "System.Collections.Concurrent", "System.Numerics",
        "System.Numerics.Vectors", "System.Runtime.Numerics", "System.Runtime.Intrinsics",
        "System.Runtime.Extensions", "System.Runtime.CompilerServices.Unsafe", "System.Threading",
        "System.Memory", "System.Linq"
    };

    public static int Run()
    {
        passed = 0;
        Assembly winding = typeof(WindingArea).Assembly, core = typeof(PolylineComparison).Assembly;
        Check(winding.GetName().Name == "PolylineKit.Winding", "WindingArea belongs to PolylineKit.Winding");
        Check(core.GetName().Name == "PolylineKit" && core != winding, "the broader library remains a separate assembly");
        Type[] exported = winding.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        Check(exported.Select(t => t.FullName).SequenceEqual(PublicTypes), "the area assembly exports exactly its seven contract types");
        Check(new[] { typeof(Point2), typeof(PathFillRule), typeof(PolylineArea), typeof(RegionOverlapResult), typeof(WindingAreaResult), typeof(WindingOverlapResult) }
            .All(t => t.Assembly == winding), "every shared winding type has one owning assembly");
        Check(core.GetForwardedTypes().Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(ForwardedTypes),
            "the broader library forwards precisely the six extracted public types");
        foreach (Type type in exported.Where(t => ForwardedTypes.Contains(t.FullName!, StringComparer.Ordinal)))
            Check(core.GetType(type.FullName!, throwOnError: true) == type, $"old assembly identity resolves {type.FullName}");

        CheckFrameworkClosure(winding);
        string snapshot = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "winding-public-api.txt"))
            .Replace("\r\n", "\n").TrimEnd();
        string actual = PublicSurface(exported).TrimEnd();
        Check(snapshot == actual, "public surface matches the reviewed constructor, property, enum and method snapshot\n"
            + "ACTUAL:\n" + actual);
        CheckCoordinateValidation();
        Console.WriteLine($"PASS: {passed} winding assembly, API and validation contract checks.");
        return passed;
    }

    private static void CheckFrameworkClosure(Assembly winding)
    {
        foreach (AssemblyName reference in winding.GetReferencedAssemblies())
            Check(reference.Name is not null && FrameworkReferences.Contains(reference.Name),
                $"direct reference {reference.Name} is on the concrete framework allowlist");

        // Resolve the complete dependency graph, including framework facade forwarders. Every reachable
        // assembly must physically be in this process's installed shared framework, never a package or
        // the consumer's application directory. This also catches an unexpected transitive dependency.
        string runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory()).TrimEnd(Path.DirectorySeparatorChar);
        var seen = new HashSet<string>(StringComparer.Ordinal) { winding.FullName! };
        var pending = new Queue<AssemblyName>(winding.GetReferencedAssemblies());
        while (pending.TryDequeue(out AssemblyName? name))
        {
            if (!seen.Add(name.FullName)) continue;
            Assembly dependency = Assembly.Load(name);
            string? location = string.IsNullOrEmpty(dependency.Location) ? null : Path.GetDirectoryName(Path.GetFullPath(dependency.Location));
            Check(string.Equals(location, runtimeDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                $"dependency closure contains framework assembly {name.Name}, not an external runtime library");
            foreach (AssemblyName transitive in dependency.GetReferencedAssemblies()) pending.Enqueue(transitive);
        }
    }

    private static void CheckCoordinateValidation()
    {
        (double Value, bool Accepted)[] values =
        [
            (0, true), (-0.0, true), (double.Epsilon, true), (-double.Epsilon, true),
            (1e100, true), (-1e100, true), (Math.BitDecrement(1e100), true), (-Math.BitDecrement(1e100), true),
            (Math.BitIncrement(1e100), false), (-Math.BitIncrement(1e100), false),
            (double.MaxValue, false), (double.MinValue, false),
            (double.PositiveInfinity, false), (double.NegativeInfinity, false), (double.NaN, false)
        ];
        foreach ((double value, bool accepted) in values)
        foreach (Point2 point in new[] { new Point2(value, 0), new Point2(0, value), new Point2(value, -value) })
        {
            Exception? core = Capture(() => PathInput.Validate(point, "candidate"));
            Exception? winding = Capture(() => WindingInput.Validate(point, "candidate"));
            string label = $"coordinate validation ({point.X:R}, {point.Y:R})";
            Check((core is null) == accepted && (winding is null) == accepted, label + " acceptance matches the finite/range contract");
            Check(core?.GetType() == winding?.GetType(), label + " exception type parity");
            Check(core?.Message == winding?.Message, label + " exception message parity");
            Check((core as ArgumentException)?.ParamName == (winding as ArgumentException)?.ParamName, label + " parameter name parity");
            if (!accepted)
                Check(core is ArgumentException { ParamName: "candidate" } && winding is ArgumentException { ParamName: "candidate" },
                    label + " rejection identifies the caller's parameter");
        }
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static string PublicSurface(Type[] types)
    {
        var lines = new List<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (Type type in types)
        {
            string kind = type.IsEnum ? "enum" : type.IsAbstract && type.IsSealed ? "static class"
                : type.IsValueType ? (type.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute")
                    ? "readonly struct" : "struct") : "class";
            lines.Add($"public {kind} {TypeName(type)}" + (type.IsEnum ? " : " + TypeName(Enum.GetUnderlyingType(type)) : ""));
            var members = new List<string>();
            // Match DeclaredOnly member inspection: Enum's framework-provided interfaces (for
            // example ISpanFormattable on newer runtimes) are not declared by this library.
            foreach (Type contract in type.GetInterfaces().Except(type.BaseType?.GetInterfaces() ?? Type.EmptyTypes))
                members.Add("implements " + TypeName(contract));
            foreach (ConstructorInfo constructor in type.GetConstructors(flags))
                members.Add($"public {TypeName(type)}({Parameters(constructor.GetParameters())})");
            var accessors = new HashSet<MethodInfo>();
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                foreach (MethodInfo accessor in property.GetAccessors(nonPublic: true)) accessors.Add(accessor);
                string prefix = property.GetAccessors().Any(m => m.IsStatic) ? "public static " : "public ";
                string name = property.Name + (property.GetIndexParameters().Length == 0 ? "" : "[" + Parameters(property.GetIndexParameters()) + "]");
                members.Add(prefix + TypeName(property.PropertyType) + " " + name + " {"
                    + (property.GetMethod?.IsPublic == true ? " get;" : "")
                    + (property.SetMethod?.IsPublic == true ? " set;" : "") + " }");
            }
            foreach (EventInfo item in type.GetEvents(flags))
            {
                if (item.AddMethod is not null) accessors.Add(item.AddMethod);
                if (item.RemoveMethod is not null) accessors.Add(item.RemoveMethod);
                members.Add("public " + (item.AddMethod?.IsStatic == true ? "static " : "")
                    + "event " + TypeName(item.EventHandlerType!) + " " + item.Name);
            }
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (type.IsEnum && field.Name == "value__") continue;
                members.Add("public " + (field.IsLiteral ? "const " : (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : ""))
                    + TypeName(field.FieldType) + " " + field.Name
                    + (field.IsLiteral ? " = " + Constant(field.GetRawConstantValue()) : ""));
            }
            foreach (MethodInfo method in type.GetMethods(flags).Where(m => !accessors.Contains(m)))
                members.Add("public " + (method.IsStatic ? "static " : "") + TypeName(method.ReturnType) + " " + method.Name
                    + (method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(TypeName)) + ">" : "")
                    + "(" + Parameters(method.GetParameters()) + ")");
            lines.AddRange(members.OrderBy(m => m, StringComparer.Ordinal).Select(m => "  " + m));
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    private static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        string name = type.GetGenericTypeDefinition().FullName!;
        return name[..name.IndexOf('`')] + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
    }

    private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : "")
        + TypeName(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) + " " + p.Name
        + (p.HasDefaultValue ? " = " + (p.ParameterType.IsEnum
            ? TypeName(p.ParameterType) + "." + Enum.GetName(p.ParameterType, p.DefaultValue!) + " (" + Constant(p.DefaultValue) + ")"
            : Constant(p.DefaultValue)) : p.IsOptional ? " [optional]" : "")));

    private static string Constant(object? value) => value switch
    {
        null => "null",
        bool boolean => boolean ? "true" : "false",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        Enum item => Convert.ToInt64(item, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable item => item.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Winding assembly contract: " + name);
        passed++;
    }
}
