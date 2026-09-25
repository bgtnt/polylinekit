namespace PolylineKit;

/// <summary>Quantized filled-region areas and their explicitly named Jaccard/IoU ratios.</summary>
/// <remarks>These values describe independently filled regions, not open strokes or traversal multiplicity.</remarks>
public sealed class FilledRegionOverlapResult
{
    internal FilledRegionOverlapResult(double differenceArea, double unionArea, Bounds2D bounds,
        PathFillRule fillRule, int decimalPrecision)
    {
        SymmetricDifferenceArea = differenceArea; UnionArea = unionArea; UnionBounds = bounds;
        FillRule = fillRule; DecimalPrecision = decimalPrecision;
    }

    /// <summary>Area belonging to exactly one independently filled region, in squared coordinate units.</summary>
    public double SymmetricDifferenceArea { get; }
    /// <summary>Area belonging to either independently filled region, with their overlap counted once and holes subtracted.</summary>
    public double UnionArea { get; }
    /// <summary>SymmetricDifferenceArea / UnionArea; null when quantization leaves a zero-area union. Smaller means more overlap.</summary>
    /// <remarks>No clamping hides floating-point discrepancies. Zero does not establish equal strokes.</remarks>
    public double? JaccardDistance => UnionArea > 0 ? SymmetricDifferenceArea / UnionArea : null;
    /// <summary>One minus JaccardDistance; null when the quantized union has zero area. Larger means more overlap.</summary>
    /// <remarks>Derived from the union and XOR areas; no separate intersection operation or numeric clamping is performed.</remarks>
    public double? IntersectionOverUnion => JaccardDistance.HasValue ? 1 - JaccardDistance.Value : null;
    /// <summary>The original pair's bounding rectangle; its area is not the filled UnionArea.</summary>
    public Bounds2D UnionBounds { get; }
    /// <summary>The fill rule applied independently to each input.</summary>
    public PathFillRule FillRule { get; }
    /// <summary>Common Clipper2 decimal precision after the common origin shift.</summary>
    public int DecimalPrecision { get; }
}
