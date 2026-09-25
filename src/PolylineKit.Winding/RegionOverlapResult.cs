namespace PolylineKit;

/// <summary>Areas and overlap ratios of two independently filled closed paths.</summary>
/// <remarks>
/// Areas use squared coordinate units. No normalization or decimal grid is applied. Values carry ordinary
/// floating-point rounding and are not clamped. These describe filled regions, not open-stroke similarity.
/// </remarks>
public readonly struct RegionOverlapResult
{
    internal RegionOverlapResult(WindingOverlapResult result)
    {
        FirstArea = result.FirstArea;
        SecondArea = result.SecondArea;
        IntersectionArea = result.IntersectionArea;
        UnionArea = result.UnionArea;
        SymmetricDifferenceArea = result.SymmetricDifferenceArea;
        FillRule = result.FillRule;
    }

    /// <summary>Filled area of the first path under FillRule.</summary>
    public double FirstArea { get; }
    /// <summary>Filled area of the second path under FillRule.</summary>
    public double SecondArea { get; }
    /// <summary>Area filled by both paths.</summary>
    public double IntersectionArea { get; }
    /// <summary>Area filled by at least one path, with overlap counted once.</summary>
    public double UnionArea { get; }
    /// <summary>Area filled by exactly one path.</summary>
    public double SymmetricDifferenceArea { get; }
    /// <summary>The fill rule applied independently to each path.</summary>
    public PathFillRule FillRule { get; }
    /// <summary>IntersectionArea / UnionArea; null when UnionArea is not positive. Larger means more overlap.</summary>
    public double? IntersectionOverUnion => UnionArea > 0 ? IntersectionArea / UnionArea : null;
    /// <summary>SymmetricDifferenceArea / UnionArea; null when UnionArea is not positive. Smaller means more overlap.</summary>
    public double? JaccardDistance => UnionArea > 0 ? SymmetricDifferenceArea / UnionArea : null;
}
