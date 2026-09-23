#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PolylineKit;

/// <summary>
/// Pointwise SIMD candidate written specifically for PolylineKit.
/// Packed double affine application; no horizontal reductions or fused multiply-adds.
/// </summary>
internal static class SimdTransforms
{
    // Fixed before measurement; see docs/optimization-evaluation.md for the tested cases.
    // Smaller inputs retain scalar dispatch and its validation behavior.
    internal const int MinimumPointCount = 32;
    private static readonly bool HasPackedXyLayout = CheckPackedXyLayout();

    /// <summary>
    /// Fills the whole output and returns true, or returns false without writing output.
    /// Only contiguous arrays are admitted; an IReadOnlyList wrapper is never copied
    /// merely to obtain SIMD access. The caller retains its original scalar fallback.
    /// </summary>
    internal static bool TryApply(AffineTransform2D transform, IReadOnlyList<Point2> input, Point2[] output)
    {
        if (input is not Point2[] points || points.Length < MinimumPointCount ||
            points.Length > int.MaxValue / 2 || // XY reinterpretation must fit a Span<double>.
            output.Length != points.Length || !HasPackedXyLayout ||
            (AppContext.TryGetSwitch("PolylineKit.DisableSimd", out bool disabled) && disabled))
            return false;

        if (Vector256.IsHardwareAccelerated)
        {
            Apply256(transform, points, output);
            return true;
        }
        if (Vector128.IsHardwareAccelerated)
        {
            Apply128(transform, points, output);
            return true;
        }
        return false;
    }

    private static void Apply256(in AffineTransform2D transform, Point2[] points, Point2[] output)
    {
        // Point2's declared value-type layout is sequential. The runtime probe below
        // additionally verifies exact size and XY field order before reinterpretation.
        // Each load is [x0,y0,x1,y1], and every store has the same layout.
        ReadOnlySpan<double> source = MemoryMarshal.Cast<Point2, double>(points.AsSpan());
        Span<double> destination = MemoryMarshal.Cast<Point2, double>(output.AsSpan());
        ref double sourceStart = ref MemoryMarshal.GetReference(source);
        ref double destinationStart = ref MemoryMarshal.GetReference(destination);
        Vector256<double> fromX = Vector256.Create(transform.M11, transform.M21, transform.M11, transform.M21);
        Vector256<double> fromY = Vector256.Create(transform.M12, transform.M22, transform.M12, transform.M22);
        Vector256<double> offset = Vector256.Create(transform.OffsetX, transform.OffsetY, transform.OffsetX, transform.OffsetY);
        Vector256<double> limit = Vector256.Create(1e100);
        Vector256<long> xIndices = Vector256.Create(0L, 0L, 2L, 2L);
        Vector256<long> yIndices = Vector256.Create(1L, 1L, 3L, 3L);

        int i = 0;
        for (; i + 1 < points.Length; i += 2)
        {
            nuint coordinate = (nuint)i * 2;
            Vector256<double> xy = Vector256.LoadUnsafe(ref sourceStart, coordinate);
            if (!Vector256.LessThanOrEqualAll(Vector256.Abs(xy), limit))
            {
                // A later input might be invalid while an earlier transformed point
                // overflows. Replay in scalar order to preserve the first exception.
                ScalarBlock(transform, points, output, i, 2);
                continue;
            }
            Vector256<double> xs = Vector256.Shuffle(xy, xIndices);
            Vector256<double> ys = Vector256.Shuffle(xy, yIndices);
            Vector256<double> values = Vector256.Add(
                Vector256.Add(Vector256.Multiply(fromX, xs), Vector256.Multiply(fromY, ys)), offset);
            if (!Vector256.LessThanOrEqualAll(Vector256.Abs(values), limit))
            {
                ScalarBlock(transform, points, output, i, 2);
                continue;
            }
            values.StoreUnsafe(ref destinationStart, coordinate);
        }
        // Odd point counts retain the original scalar arithmetic and range checks.
        if (i < points.Length) output[i] = transform.Apply(points[i]);
    }

    private static void Apply128(in AffineTransform2D transform, Point2[] points, Point2[] output)
    {
        ReadOnlySpan<double> source = MemoryMarshal.Cast<Point2, double>(points.AsSpan());
        Span<double> destination = MemoryMarshal.Cast<Point2, double>(output.AsSpan());
        ref double sourceStart = ref MemoryMarshal.GetReference(source);
        ref double destinationStart = ref MemoryMarshal.GetReference(destination);
        Vector128<double> fromX = Vector128.Create(transform.M11, transform.M21);
        Vector128<double> fromY = Vector128.Create(transform.M12, transform.M22);
        Vector128<double> offset = Vector128.Create(transform.OffsetX, transform.OffsetY);
        Vector128<double> limit = Vector128.Create(1e100);
        Vector128<long> xIndices = Vector128.Create(0L, 0L);
        Vector128<long> yIndices = Vector128.Create(1L, 1L);

        for (int i = 0; i < points.Length; i++)
        {
            nuint coordinate = (nuint)i * 2;
            Vector128<double> xy = Vector128.LoadUnsafe(ref sourceStart, coordinate);
            if (!Vector128.LessThanOrEqualAll(Vector128.Abs(xy), limit))
            {
                output[i] = transform.Apply(points[i]);
                continue;
            }
            Vector128<double> xs = Vector128.Shuffle(xy, xIndices);
            Vector128<double> ys = Vector128.Shuffle(xy, yIndices);
            Vector128<double> values = Vector128.Add(
                Vector128.Add(Vector128.Multiply(fromX, xs), Vector128.Multiply(fromY, ys)), offset);
            if (!Vector128.LessThanOrEqualAll(Vector128.Abs(values), limit))
            {
                output[i] = transform.Apply(points[i]);
                continue;
            }
            values.StoreUnsafe(ref destinationStart, coordinate);
        }
    }

    private static void ScalarBlock(in AffineTransform2D transform, Point2[] points, Point2[] output,
        int first, int count)
    {
        for (int i = first; i < first + count; i++) output[i] = transform.Apply(points[i]);
    }

    private static bool CheckPackedXyLayout()
    {
        if (!typeof(Point2).IsLayoutSequential) return false;
        Span<Point2> sample = stackalloc Point2[1];
        sample[0] = new Point2(1.25, -2.5);
        if (MemoryMarshal.AsBytes(sample).Length != 2 * sizeof(double)) return false;
        ReadOnlySpan<double> coordinates = MemoryMarshal.Cast<Point2, double>(sample);
        return coordinates.Length == 2 && coordinates[0] == 1.25 && coordinates[1] == -2.5;
    }
}
#endif
