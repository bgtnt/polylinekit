namespace PolylineKit;

/// <summary>Which portions of a closed walk count as filled. Neither rule counts traversal multiplicity.</summary>
public enum PathFillRule
{
    /// <summary>Count points with nonzero winding once.</summary>
    NonZero,
    /// <summary>Count points with odd winding once.</summary>
    EvenOdd
}

