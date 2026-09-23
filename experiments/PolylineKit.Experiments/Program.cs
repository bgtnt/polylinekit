using PolylineKit.Experiments;

if (Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1")
    AppContext.SetSwitch("PolylineKit.DisableSimd", true);

return args.FirstOrDefault() switch
{
    "check" => RunChecks(),
    "evidence" => WriteEvidence(args),
    "benchmark" => RunBenchmark(args),
    "benchmark-transforms" => RunTransformBenchmark(args),
    "benchmark-optimization" => RunOptimization(args),
    "probe-optimization" => ProbeOptimization(args),
    "profile-optimization" => ProfileOptimization(args),
    _ => Usage()
};

static int RunChecks()
{
    int total = Checks.Run() + AlignmentChecks.Run() + ComparisonChecks.Run() + NumericReviewChecks.Run() + GenLipReviewChecks.Run();
    Console.WriteLine($"PASS: {total} total checks."); return 0;
}
static int WriteEvidence(string[] args) { Evidence.Write(args.ElementAtOrDefault(1) ?? "results/geometry"); return 0; }
static int RunBenchmark(string[] args)
{
    if (args.Length != 4) return Usage();
    Benchmarks.Run(args[1], int.Parse(args[2]), args[3]); return 0;
}
static int RunTransformBenchmark(string[] args)
{
    if (args.Length != 4) return Usage();
    TransformBenchmarks.Run(args[1], int.Parse(args[2]), args[3]); return 0;
}
static int RunOptimization(string[] args)
{
    if (args.Length != 6) return Usage();
    OptimizationBenchmarks.Run(args[1], int.Parse(args[2]), args[3], args[4], args[5]); return 0;
}
static int ProbeOptimization(string[] args)
{
    if (args.Length != 5) return Usage();
    OptimizationBenchmarks.Probe(args[1], args[2], args[3], args[4]); return 0;
}
static int ProfileOptimization(string[] args)
{
    if (args.Length != 4) return Usage();
    OptimizationBenchmarks.Profile(args[1], int.Parse(args[2]), int.Parse(args[3])); return 0;
}
static int Usage() { Console.Error.WriteLine("Commands: check | evidence [directory] | benchmark|benchmark-transforms <directory> <run> <revision> | benchmark-optimization <directory> <run> <variant> <coreRevision> <harnessRevision> | probe-optimization <directory> <variant> <coreRevision> <harnessRevision> | profile-optimization <case> <vertices> <milliseconds>"); return 2; }
