using System.IO;
using System.Reflection;
using System.Windows.Media;
using RegionCoverage;

// The shared runner's top-level entry class is in the global namespace; adapters are in RegionCoverage.
internal static partial class CoverageExperiment
{
    static partial void CheckPlatformComparators()
    {
        int checks = WpfComparatorChecks.Run();
        Console.WriteLine($"WPF: {checks} analytic/reuse/coverage controls passed; no timing.");
    }

    static partial void AddPlatformComparators(List<CoverageComparator> methods)
    {
        methods.Add(new WpfScannerComparator(FillRule.EvenOdd));
        methods.Add(new WpfScannerComparator(FillRule.Nonzero));
        methods.Add(new WpfCombineComparator());
    }

    static partial void CreatePlatformComparator(string name, ref CoverageComparator? method)
    {
        method = name switch
        {
            "wpf-evenodd-area" => new WpfScannerComparator(FillRule.EvenOdd),
            "wpf-nonzero-area" => new WpfScannerComparator(FillRule.Nonzero),
            "wpf-combine-explicit" => new WpfCombineComparator(),
            _ => method
        };
    }

    static partial void AddPlatformHashes(Dictionary<string, string> hashes)
    {
        var assembly = typeof(Geometry).Assembly;
        string native = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "wpfgfx_cor3.dll");
        hashes.Add("WPF-PresentationCore", Hash(File.ReadAllBytes(assembly.Location)));
        hashes.Add("WPF-wpfgfx_cor3", Hash(File.ReadAllBytes(native)));
        hashes.Add("WPF-version", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
    }
}
