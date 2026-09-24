using PolylineKit.Experiments;

if (Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1")
    AppContext.SetSwitch("PolylineKit.DisableSimd", true);

return args.FirstOrDefault() switch
{
    "benchmark" when args.Length == 4 => Run(() => Benchmarks.Run(args[1], int.Parse(args[2]), args[3])),
    "benchmark-transforms" when args.Length == 4 => Run(() => TransformBenchmarks.Run(args[1], int.Parse(args[2]), args[3])),
    "benchmark-winding" when args.Length == 4 => Run(() => WindingBenchmarks.Run(args[1], int.Parse(args[2]), args[3])),
    "summarize-winding" when args.Length == 2 => Run(() => WindingBenchmarks.Summarize(args[1])),
    "benchmark-clipper" when args.Length == 4 => Run(() => ClipperBenchmarks.Run(args[1], int.Parse(args[2]), args[3])),
    "summarize-clipper" when args.Length == 2 => Run(() => ClipperBenchmarks.Summarize(args[1])),
    "smoke-clipper" when args.Length == 1 => Run(ClipperBenchmarks.Smoke),
    "accuracy-clipper" when args.Length == 2 => Run(() => ClipperAccuracy.Run(args[1])),
    "profile-winding" when args.Length == 5 => Run(() => ClipperBenchmarks.ProfileStorage(args[1], args[2], int.Parse(args[3]), args[4])),
    "smoke" when args.Length == 1 => Run(BenchmarkSmoke.Run),
    _ => Usage()
};

static int Run(Action action) { action(); return 0; }
static int Usage()
{
    Console.Error.WriteLine("Commands: benchmark|benchmark-transforms|benchmark-winding|benchmark-clipper <directory> <run> <revision> | profile-winding <directory> <family/n/operation> <run> <revision> | summarize-winding|summarize-clipper|accuracy-clipper <directory> | smoke|smoke-clipper");
    return 2;
}
