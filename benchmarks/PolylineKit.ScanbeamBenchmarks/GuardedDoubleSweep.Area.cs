using System.Runtime.CompilerServices;

namespace PolylineKit.ScanbeamBenchmarks;

internal sealed partial class GuardedDoubleSweep
{
    private readonly bool optimizeAreaArithmetic;

    /// <summary>Exercises the actual trapezoid arithmetic and original interval accumulation.</summary>
    /// <remarks>Finite ordered nonnegative width/height intervals are required; no geometry is approximated.</remarks>
    internal static bool TryAreaArithmeticForCheck(double lowerLo, double lowerHi, double upperLo, double upperHi,
        double heightLo, double heightHi, bool optimized, out double resultLo, out double resultHi,
        out string? reason, int repetitions = 1) =>
        Interval.ProbeAreaArithmetic(lowerLo, lowerHi, upperLo, upperHi, heightLo, heightHi, optimized,
            out resultLo, out resultHi, out reason, repetitions);

    private readonly partial struct Interval
    {
        /// <summary>Same bounds as the generic expression for nonnegative widths and height.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Interval NonnegativeTrapezoid(Interval lower, Interval upper, Interval height) =>
            MultiplyByNonnegativeHeight(Half(Add(lower, upper)), height);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Interval Half(Interval value)
        {
            // Match Multiply(value, Point(.5)), including its exact-zero and exact-one shortcuts.
            if (value.IsZero) return Zero;
            if (value.IsPoint && value.Lo == 1) return Point(.5);
            // Multiplying by .5 is monotone, but is not always exact for subnormals. Keep the original
            // outward step even for an exactly representable result, preserving every returned bit.
            return new(Down(value.Lo * .5), Up(value.Hi * .5));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Interval MultiplyByNonnegativeHeight(Interval width, Interval height)
        {
            // These are the generic Multiply shortcuts in their original order.
            if (width.IsZero || height.IsZero) return Zero;
            if (width.IsPoint && width.Lo == 1) return height;
            if (height.IsPoint && height.Lo == 1) return width;
            // Original widths and height are nonnegative, but Add/Half can introduce -epsilon as the
            // lower width bound. Its minimum product then uses the *upper* height bound. Width.Hi
            // remains nonnegative, so its upper product always uses Height.Hi. Do not clamp either.
            double lo = width.Lo * (width.Lo < 0 ? height.Hi : height.Lo);
            double hi = width.Hi * height.Hi;
            return new(Down(lo), Up(hi));
        }

        internal static bool ProbeAreaArithmetic(double lowerLo, double lowerHi, double upperLo, double upperHi,
            double heightLo, double heightHi, bool optimized, out double resultLo, out double resultHi,
            out string? reason, int repetitions)
        {
            resultLo = resultHi = double.NaN;
            reason = null;
            if (repetitions <= 0 || !ValidNonnegative(lowerLo, lowerHi) ||
                !ValidNonnegative(upperLo, upperHi) || !ValidNonnegative(heightLo, heightHi))
            {
                reason = "input-contract";
                return false;
            }
            try
            {
                Interval lower = new(lowerLo, lowerHi), upper = new(upperLo, upperHi);
                Interval height = new(heightLo, heightHi), result = Zero;
                for (int i = 0; i < repetitions; i++)
                {
                    Interval trapezoid = optimized ? NonnegativeTrapezoid(lower, upper, height) :
                        Multiply(Multiply(Add(lower, upper), Point(.5)), height);
                    result = Add(result, trapezoid);
                }
                resultLo = result.Lo;
                resultHi = result.Hi;
                return true;
            }
            catch (Uncertified failure)
            {
                reason = failure.Reason;
                return false;
            }
        }

        private static bool ValidNonnegative(double lo, double hi) =>
            double.IsFinite(lo) && double.IsFinite(hi) && lo >= 0 && lo <= hi;
    }
}
