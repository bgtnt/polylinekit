using PolylineKit.Recognition;

AppContext.SetSwitch("PolylineKit.DisableSimd", Environment.GetEnvironmentVariable("POLYLINEKIT_FORCE_SCALAR") == "1");
try
{
    switch (args.Length > 0 ? args[0] : "")
    {
        case "check": EngineChecks.Run(); break;
        case "develop" when args.Length == 4: EvaluationRunner.Develop(args[1], args[2], args[3]); break;
        case "freeze" when args.Length == 5: EvaluationRunner.Freeze(args[1], args[2], args[3], args[4]); break;
        case "evaluate" when args.Length == 5: EvaluationRunner.Evaluate(args[1], args[2], args[3], args[4]); break;
        default:
            Console.Error.WriteLine("Commands: check | develop <data> <output> <source-revision> | freeze <data> <development> <freeze.json> <source-revision> | evaluate <data> <freeze.json> <output> <source-revision>");
            return 2;
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    return 1;
}
