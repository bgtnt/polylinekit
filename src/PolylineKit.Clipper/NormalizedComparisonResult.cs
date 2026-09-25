namespace PolylineKit;

/// <summary>Area comparison after independently normalizing both paths, with both transforms retained.</summary>
public sealed class NormalizedComparisonResult
{
    internal NormalizedComparisonResult(NormalizationResult first, NormalizationResult second, AreaComparisonResult comparison)
    { First = first; Second = second; Comparison = comparison; }
    /// <summary>First normalized path and applied transform.</summary>
    public NormalizationResult First { get; }
    /// <summary>Second normalized path and applied transform.</summary>
    public NormalizationResult Second { get; }
    /// <summary>Area result in the common normalized coordinate system.</summary>
    public AreaComparisonResult Comparison { get; }
}
