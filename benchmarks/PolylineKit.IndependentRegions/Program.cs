using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using NetTopologySuite.Geometries;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

internal sealed record AssemblyIdentity(string Assembly, string Location, string Sha256, string InformationalVersion, string? TargetFramework);

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static int Main(string[] args)
    {
        if (!(args.Length == 2 && args[0] == "package-check" || args.Length == 3 && args[0] == "check" || args.Length == 4 && args[0] == "benchmark"))
        {
            Console.Error.WriteLine("Usage: package-check <output.json> | check <data-root> <output.json> | benchmark <data-root> <run:1..3> <output.json>");
            return 2;
        }
        string output = args[^1];
        if (File.Exists(Path.GetFullPath(output)))
        {
            Console.Error.WriteLine("Refusing to overwrite an existing report: " + Path.GetFullPath(output));
            return 2;
        }
        try
        {
            AssemblyIdentity[] assemblies = Identities();
            if (args[0] == "package-check")
            {
                Save(output, new { SchemaVersion = 1, Utc = DateTimeOffset.UtcNow, Metadata = Metadata(null, assemblies),
                    Validation = Validation.AnalyticPackageCheck(), IntegerAreaValidation = IntegerArea.Check() });
                Console.WriteLine($"PASS: published {PublishedRelease.Current.Name} identity and 36 analytic metric checks.");
                return 0;
            }
            FrozenCorpus corpus = Corpus.Load(args[1]);
            object integerAreaChecks = IntegerArea.Check();
            string protocolHash = BenchmarkProtocolHash(corpus);
            object metadata = Metadata(corpus, assemblies);
            if (args[0] == "check")
            {
                ValidationReport validation = Validation.Check(corpus);
                VerifyIdentities(assemblies);
                Contracts.Require(BenchmarkProtocolHash(corpus) == protocolHash, "Benchmark protocol changed during verification.");
                Save(output, new { SchemaVersion = 1, Utc = DateTimeOffset.UtcNow, Metadata = metadata, Validation = validation, IntegerAreaValidation = integerAreaChecks });
                Console.WriteLine($"{(validation.Passed ? "PASS" : "FAIL")}: {validation.Assertions} numerical assertions; {validation.ExcludedOriginalInputs} invalid original inputs explicitly excluded; {validation.FailureCount} failures.");
                return validation.Passed ? 0 : 1;
            }
            int run = int.Parse(args[2], CultureInfo.InvariantCulture);
            object analytic = Validation.AnalyticPackageCheck();
            Row[] rows = Measurements.Run(corpus, run);
            VerifyIdentities(assemblies);
            Contracts.Require(BenchmarkProtocolHash(corpus) == protocolHash, "Benchmark protocol changed during the process.");
            Save(output, new
            {
                SchemaVersion = 1, Run = run, ProcessId = Environment.ProcessId, Utc = DateTimeOffset.UtcNow,
                Metadata = metadata,
                Validation = new { Passed = true, Scope = "Analytic package smoke and stable timed digests. Full corpus verification is a separate check command.", Analytic = analytic },
                IntegerAreaValidation = integerAreaChecks,
                Measurements = rows
            });
            return 0;
        }
        catch (Exception error)
        {
            if (!File.Exists(Path.GetFullPath(output)))
                Save(output, new { SchemaVersion = 1, Utc = DateTimeOffset.UtcNow, Passed = false, Stage = args[0], Failure = error.ToString() });
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static object Metadata(FrozenCorpus? corpus, AssemblyIdentity[] assemblies) => new
    {
        Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
        AssessmentRevision = AssessmentMetadata("AssessmentRevision", "POLYLINEKIT_ASSESSMENT_REVISION"),
        AssessmentSourceState = AssessmentMetadata("AssessmentSourceState", "POLYLINEKIT_ASSESSMENT_SOURCE_STATE"),
        ReleaseProfile = PublishedRelease.Current.Name, ReleaseProfilesSha256 = PublishedRelease.ProfilesSha256,
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
        ProcessorCount = Environment.ProcessorCount, StopwatchFrequency = Stopwatch.Frequency,
        TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), TieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
        HardwareIntrinsics = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),
        Assemblies = assemblies,
        PackageVersions = new Dictionary<string, string> { ["PolylineKit.Core"] = PublishedRelease.Current.Version, ["Clipper2"] = "2.0.0", ["NetTopologySuite"] = "2.6.0" },
        SourceFiles = corpus?.Sources, DerivedFiles = corpus?.Derived, ManifestSha256 = corpus?.ManifestSha256,
        SourceProtocolSha256 = corpus?.ProtocolSha256, BenchmarkProtocolSha256 = corpus is null ? null : BenchmarkProtocolHash(corpus),
        Zones = corpus?.Zones.Inventory, Queries = corpus?.Queries.Inventory,
        SourcePairsPerDirection = corpus?.SourcePairs, SourceBoundingBoxCandidatesPerDirection = corpus?.SourceCandidates,
        EligiblePairsPerDirection = corpus?.EligiblePairs, EligibleBoundingBoxCandidatesPerDirection = corpus?.EligibleCandidates,
        InputIssues = corpus?.InvalidInputs, corpus?.MaximumQuantizationDisplacement,
        SpatialReference = "EPSG:3067", CoordinateUnit = "metre", CoordinatePreparation = "Original supplied XY and traversal directions; explicit shell/hole roles. No projection, centering, repair or simplification in this runner.",
        AreaBudgetM2 = Contracts.AreaBudget, FractionBudget = Contracts.FractionBudget, ClipperGridMetres = 1 / Contracts.ClipperScale,
        ClipperRounding = "AwayFromZero; checked scaled magnitude <= 2^52",
        ClipperAreaSummation = "Checked Int128 signed integer shoelace, preserving hole signs; full BigInteger restart on overflow. Convert the final absolute twice-area to binary64 and square-metre units.",
        TimedFillRule = "NonZero", SamplesPerCell = Measurements.SamplesPerCell, TargetMillisecondsPerSample = Measurements.TargetMilliseconds,
        MaximumIterations = Measurements.MaximumIterations,
        Scope = "Original-coordinate bounds are the common outer rejection. Parsing, input eligibility checks, result caches, delegates, sample records and serialization are outside timed counters. Preparation includes factory/copy construction and own areas. Warm queries reuse inputs and mutable per-backend scratch storage, never pair results.",
        DigestMeaning = "warm/fresh: intersection sum, coverage sum and bbox candidates; prepare-only: first own-area sum, second own-area sum and prepared feature count."
    };

    private static AssemblyIdentity[] Identities()
    {
        Assembly[] loaded = [typeof(Program).Assembly, typeof(PreparedRegion).Assembly, typeof(Clipper).Assembly, typeof(Polygon).Assembly];
        AssemblyIdentity[] identities = loaded.Select(a => new AssemblyIdentity(a.GetName().Name!, a.Location,
            Corpus.Hash(File.ReadAllBytes(a.Location)), a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable",
            a.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName)).ToArray();
        AssemblyIdentity core = identities.Single(i => i.Assembly == "PolylineKit.Winding");
        Contracts.Require(core.Sha256 == Contracts.PublishedCoreSha256 &&
            core.InformationalVersion == PublishedRelease.Current.Version + "+" + PublishedRelease.Current.SourceRevision &&
            core.TargetFramework == ".NETCoreApp,Version=v10.0", "The runner did not load the declared published release's net10.0 binary.");
        return identities;
    }

    private static void VerifyIdentities(AssemblyIdentity[] expected) => Contracts.Require(Identities().SequenceEqual(expected), "A loaded binary changed during the process.");
    private static string? AssessmentMetadata(string key, string legacyEnvironment) =>
        typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == key)?.Value
        ?? Environment.GetEnvironmentVariable(legacyEnvironment);
    private static string BenchmarkProtocolHash(FrozenCorpus corpus) => Corpus.Hash(File.ReadAllBytes(Path.Combine(corpus.Root, "..", "PROTOCOL.md")));
    private static void Save(string path, object value)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json) + Environment.NewLine);
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }
}
