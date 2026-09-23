using PolylineKit.Experiments;

return args.FirstOrDefault() switch
{
    "check" => RunChecks(),
    "evidence" => WriteEvidence(args),
    "benchmark" => RunBenchmark(args),
    _ => Usage()
};

static int RunChecks() { Checks.Run(); return 0; }
static int WriteEvidence(string[] args) { Evidence.Write(args.ElementAtOrDefault(1) ?? "results/geometry"); return 0; }
static int RunBenchmark(string[] args)
{
    if (args.Length != 4) return Usage();
    Benchmarks.Run(args[1], int.Parse(args[2]), args[3]); return 0;
}
static int Usage() { Console.Error.WriteLine("Commands: check | evidence [directory] | benchmark <directory> <run> <revision>"); return 2; }
