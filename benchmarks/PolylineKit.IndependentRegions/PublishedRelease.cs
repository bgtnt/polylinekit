using System.Reflection;
using System.Text.Json;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

// One profile inventory is shared with the outside-consumer PowerShell checks.
// The loaded package selects its identity; a report cannot relabel an alpha.2 DLL.
internal sealed record PublishedRelease(string Name, string Version, string SourceRevision,
    string CoreNet10Sha256, string CoreContentHash, string LockFile)
{
    private static readonly byte[] ProfileBytes = ReadProfiles();
    internal static readonly string ProfilesSha256 = Corpus.Hash(ProfileBytes);
    internal static readonly PublishedRelease Current = SelectLoadedPackage();
    internal string Backend => "Core-" + Name;

    private static byte[] ReadProfiles()
    {
        using Stream stream = typeof(PublishedRelease).Assembly.GetManifestResourceStream("PolylineKit.IndependentRegions.ReleaseProfiles")
            ?? throw new InvalidOperationException("Published release profiles are missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static PublishedRelease SelectLoadedPackage()
    {
        string identity = typeof(PreparedRegion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("The loaded Core package has no source identity.");
        Dictionary<string, PublishedRelease> profiles = JsonSerializer.Deserialize<Dictionary<string, PublishedRelease>>(ProfileBytes)
            ?? throw new InvalidOperationException("Published release profiles are invalid.");
        PublishedRelease[] matches = profiles.Values.Where(p => identity == p.Version + "+" + p.SourceRevision).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("The loaded Core package is not a declared published release.");
        return matches[0];
    }
}
