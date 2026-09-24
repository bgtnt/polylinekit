using PolylineKit.Experiments;

if (Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1")
    AppContext.SetSwitch("PolylineKit.DisableSimd", true);

if (args.Length != 1 || args[0] != "check")
{
    Console.Error.WriteLine("Usage: dotnet run --project tests/PolylineKit.Checks -- check");
    return 2;
}
return RunChecks();

static int RunChecks()
{
    var target = (System.Runtime.Versioning.TargetFrameworkAttribute)typeof(PolylineKit.Point2).Assembly
        .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false).Single();
    AppContext.TryGetSwitch("PolylineKit.DisableSimd", out bool simdDisabled);
    Console.WriteLine($"Core target: {target.FrameworkName}; Vector128={System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated}; Vector256={System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated}; force-scalar={Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR")}; simd-disabled={simdDisabled}; process-arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
    int total = Checks.Run() + AlignmentChecks.Run() + ComparisonChecks.Run() + NumericReviewChecks.Run() + GenLipReviewChecks.Run() + OptimizationChecks.Run() + WindingAreaChecks.Run();
    Console.WriteLine($"PASS: {total} total checks."); return 0;
}
