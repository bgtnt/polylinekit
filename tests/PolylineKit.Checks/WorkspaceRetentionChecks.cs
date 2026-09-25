using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using PolylineKit;

namespace PolylineKit.Experiments;

/// <summary>Retention policy checks use object identity/capacities, without GC timing assertions.</summary>
internal static class WorkspaceRetentionChecks
{
    private static int passed;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Point2[] Square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];

    internal static int Run()
    {
        passed = 0;
        OnThread(CheckGeneralPool);
        OnThread(CheckThrowingOuter);
        Type? integers = typeof(WindingArea).Assembly.GetType("PolylineKit.IntegerFilledAreaSweep");
        if (integers is not null) OnThread(() => CheckIntegerPool(integers));
        OnThread(() => CheckDenseGeometry(integers));
        Console.WriteLine($"PASS: {passed} workspace retention checks.");
        return passed;
    }

    private static void CheckGeneralPool()
    {
        var outer = WindingEngine.Workspace.Rent();
        CheckArrayInventory(outer, "Vertices Next Keys Order MergeKeys MergeOrder Runs Candidates SweepMax SweepMinOther SweepMaxOther Start Overlapping Found Sorted CrossingKeys CrossingOrder Shared Slots MinX MinY MaxX MaxY Used Origin Group EdgeTerms Sums");
        outer.SimpleSweep = new PreparedSimpleSweep();
        WindingStatistics statistics = default;
        Check(outer.SimpleSweep.TryCertify(Square, Square.Length, ref statistics), "independent simple sweep exercised");
        CheckArrayInventory(outer.SimpleSweep, "vertices events start end left right parent priority");
        Check(((Array)typeof(PreparedSimpleSweep).GetField("vertices", Instance)!.GetValue(outer.SimpleSweep)!).Length == 0,
            "simple sweep releases its borrowed vertex reference");
        Check(outer.SimpleSweep.RetainedArrayBytes == ArrayBytes(outer.SimpleSweep), "simple sweep accounts for every owned array");
        Check(outer.RetainedArrayBytes >= ArrayBytes(outer) + ArrayBytes(outer.SimpleSweep), "general accounting includes nested sweep arrays");
        Check(outer.RetainedArrayBytes < WindingEngine.Workspace.MaxCachedArrayBytes, "small workspace fits budget");
        WindingEngine.Workspace.Return(outer);
        Check(ReferenceEquals(outer, WindingEngine.Workspace.Rent()), "small general workspace reused");
        long extra = WindingEngine.Workspace.MaxCachedArrayBytes - outer.RetainedArrayBytes;
        outer.Overlapping = new bool[checked(outer.Overlapping.Length + (int)extra)];
        Check(outer.RetainedArrayBytes == WindingEngine.Workspace.MaxCachedArrayBytes, "general exact retention boundary");
        WindingEngine.Workspace.Return(outer);
        Check(ReferenceEquals(outer, WindingEngine.Workspace.Rent()), "exact budget retained inclusively");
        outer.Overlapping = new bool[outer.Overlapping.Length + 1];
        var nested = WindingEngine.Workspace.Rent();
        Check(!ReferenceEquals(outer, nested), "active general workspace unavailable to nested call");
        WindingEngine.Workspace.Return(nested);
        WindingEngine.Workspace.Return(outer);
        Check(ReferenceEquals(nested, WindingEngine.Workspace.Rent()), "oversized outer preserves smaller nested cache");
        WindingEngine.Workspace.Return(outer);
        Check(Cached(typeof(WindingEngine.Workspace)) is null, "oversized general workspace discarded when cache empty");
        var next = WindingEngine.Workspace.Rent();
        Check(!ReferenceEquals(outer, next), "oversized general buffers are not rerented");
        WindingEngine.Workspace.Return(next);
        Check(WindingArea.ClosedPath(Square).NonZero == 4, "general geometry recovers after oversized discard");
    }

    private static void CheckThrowingOuter()
    {
        var path = new NestedThrowingPath();
        bool threw = false;
        try { WindingArea.ClosedPath(path); }
        catch (ProbeException) { threw = true; }
        Check(threw, "public call preserves indexer failure");
        Check(path.Nested is not null && ReferenceEquals(path.Nested, WindingEngine.Workspace.Rent()),
            "throwing oversized public call preserves nested small workspace");
        Check(WindingArea.ClosedPath(Square).NonZero == 4, "public geometry works after indexer failure");
    }

    private sealed class ProbeException : Exception { }
    private sealed class NestedThrowingPath : IReadOnlyList<Point2>
    {
        internal WindingEngine.Workspace? Nested;
        public int Count => checked((int)(WindingEngine.Workspace.MaxCachedArrayBytes / 16) + 1);
        public Point2 this[int index]
        {
            get
            {
                Check(WindingArea.ClosedPath(Square).NonZero == 4, "nested public call during oversized validation");
                Nested = WindingEngine.Workspace.Rent();
                WindingEngine.Workspace.Return(Nested);
                throw new ProbeException();
            }
        }
        public IEnumerator<Point2> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // The portable DLL deliberately has no integer specialization. Reflection keeps these checks
    // loadable when verify-implementations swaps the portable assembly into the net10 test host.
    private static void CheckIntegerPool(Type type)
    {
        MethodInfo rent = type.GetMethod("Rent", Static)!, giveBack = type.GetMethod("Return", Static)!, measure = type.GetMethod("Measure", Instance)!;
        PropertyInfo payload = type.GetProperty("RetainedArrayBytes", Instance)!;
        long limit = (long)type.GetField("MaxCachedArrayBytes", Static)!.GetRawConstantValue()!;
        object Rent() => rent.Invoke(null, null)!;
        void Return(object value) => giveBack.Invoke(null, [value]);
        long Bytes(object value) => (long)payload.GetValue(value)!;
        object outer = Rent();
        measure.Invoke(outer, [new Point2[] { new(0, 0), new(2, 2), new(2, 0), new(0, 2) }, PathFillRule.NonZero]);
        CheckArrayInventory(outer, "vertices edges levels active topOrder positions prefix lastLevel crossings");
        Check(Bytes(outer) == ArrayBytes(outer), "integer accounting includes every array and managed struct padding");
        Return(outer);
        Check(ReferenceEquals(outer, Rent()), "small integer workspace reused");
        FieldInfo crossings = type.GetField("crossings", Instance)!, positions = type.GetField("positions", Instance)!;
        Type crossingType = crossings.FieldType.GetElementType()!;
        int crossingBytes = ElementBytes(crossingType);
        long withoutCrossings = Bytes(outer) - ((Array)crossings.GetValue(outer)!).LongLength * crossingBytes;
        crossings.SetValue(outer, Array.CreateInstance(crossingType, checked((int)((limit - withoutCrossings) / crossingBytes))));
        int padding = checked((int)(limit - Bytes(outer)));
        Check(padding % sizeof(int) == 0, "integer budget remainder expressible by index capacity");
        positions.SetValue(outer, new int[((Array)positions.GetValue(outer)!).Length + padding / sizeof(int)]);
        Check(Bytes(outer) == limit, "integer exact retention boundary");
        Return(outer);
        Check(ReferenceEquals(outer, Rent()), "integer exact budget retained inclusively");
        crossings.SetValue(outer, Array.CreateInstance(crossingType, ((Array)crossings.GetValue(outer)!).Length + 1));
        object nested = Rent();
        Check(!ReferenceEquals(outer, nested), "active integer workspace unavailable to nested call");
        Return(nested); Return(outer);
        Check(ReferenceEquals(nested, Rent()), "oversized integer outer preserves smaller nested cache");
        bool threw = false;
        try
        {
            try { measure.Invoke(outer, [null, PathFillRule.NonZero]); }
            finally { Return(outer); }
        }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentNullException) { threw = true; }
        Check(threw && Cached(type) is null, "throwing oversized integer call returns without retention");
        object next = Rent();
        Check(!ReferenceEquals(outer, next), "oversized integer workspace not rerented");
        Check((double)measure.Invoke(next, [Square, PathFillRule.NonZero])! == 4, "integer geometry recovers after oversized failure");
        Return(next);
    }

    private static void CheckDenseGeometry(Type? integers)
    {
        var random = new Random(3);
        Point2[] dense = Enumerable.Range(0, 1024).Select(i => new Point2(random.Next(-2048, 2049), i % 2 == 0 ? -2048 : 2048)).ToArray();
        double general = WindingArea.ClosedPath(dense).NonZero;
        Check(double.IsFinite(general) && general > 0, "dense1024 general geometry is finite and positive");
        Check(Cached(typeof(WindingEngine.Workspace)) is null, "actual dense1024 general buffers exceed retention budget and are discarded");
        if (integers is null) return;
        Type selector = typeof(WindingArea).Assembly.GetType("PolylineKit.FilledAreaSelector", true)!;
        Check((bool)selector.GetMethod("ShouldUseIntegerSweep", Static)!.Invoke(null, [dense])!, "dense1024 selects integer specialization");
        double selected = PolylineArea.FilledArea(dense);
        Check(Math.Abs(selected - general) <= 1e-10 * general, "dense1024 numeric parity after retention-only change");
        Check(Cached(integers) is null, "actual dense1024 integer buffers exceed retention budget and are discarded");
    }

    private static object? Cached(Type type) => type.GetField("cached", Static)!.GetValue(null);
    private static void CheckArrayInventory(object instance, string names) =>
        Check(instance.GetType().GetFields(Instance).Where(f => f.FieldType.IsArray).Select(f => f.Name).Order().SequenceEqual(names.Split(' ').Order()),
            instance.GetType().Name + " retained-array inventory is explicit");
    private static long ArrayBytes(object instance) => instance.GetType().GetFields(Instance).Where(f => f.FieldType.IsArray)
        .Sum(f => ((Array)f.GetValue(instance)!).LongLength * ElementBytes(f.FieldType.GetElementType()!));
    private static int ElementBytes(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Workspace retention: " + name); passed++; }
    private static void OnThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.Start(); thread.Join();
        if (error is not null) throw new InvalidOperationException("Workspace retention thread failed.", error);
    }
}
