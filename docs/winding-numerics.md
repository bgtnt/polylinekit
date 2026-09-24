# Winding edge-term accuracy

The engine sums directed boundary contributions. For endpoints `a`, `b` and the
chain's local origin `o`, its unweighted contribution is

\[
C = 2\,\operatorname{orient2d}(o,a,b)
  = ((a-o)+(b-o))\mathbin\times(b-a).
\]

The accumulated contributions are divided by four. The original midpoint
expression avoids subtracting two large endpoint products for a short edge far
from the origin. It does **not** by itself prevent cancellation inside its two products.
In particular, the three skinny-triangle examples from the numerical review
lost 32.9%, 9.95% and 9.93% before this correction. Compensated accumulation
cannot recover an error already introduced inside a contribution.

Those triangles have vertices `(0,0)`, `(L,L)`,
`(2L, BitIncrement(2L))`, for `L = 1e8, 1e12, 1e16`. Their exact binary64-input
areas are `L * (BitIncrement(2L) - 2L) / 2`. All three now agree exactly, as do
the regression variants at `L = 1e4`, reversed/cyclic input orders and very
small scales. Translated variants use the exact area of the translated,
rounded coordinates; translation may already have erased the thinness before
the library receives the input.

## Adaptive compensated determinant

The implementation now evaluates the equivalent `2 * orient2d(o,a,b)` directly.
It first tries a cheap value filter, then compensates the two products and the
four coordinate differences. Full expansion summation is needed only when the
remaining correction is poorly conditioned. This avoids paying for a full
expansion on every ordinary short edge whose endpoint products nearly cancel.
The existing orientation-sign predicates are unchanged.

Let `u = 2^-53` and `c = u/(1-u)`. For a normal result `z` of one rounded
operation, its local error is at most `c |z|`. Write the rounded offsets from
the origin as `x1,y1,x2,y2`, and form

```
L = fl(x1 * y2)
R = fl(y1 * x2)
q = fl(L - R)
S = fl(|L| + |R|)
```

The exact determinant is `D`. Each product has two rounded differences as
operands. Including these errors, the product rounding and the final
subtraction gives

\[
|D-q| \le c\left[(3+3c+c^2)(|L|+|R|)+|q|\right].
\]

Since `|q| <= (1+u)(|L|+|R|)`, and allowing for the rounding in `S`, this is
less than `(4+32u)u S` for binary64. The quick filter accepts only when
`S > 1e-250` and `S <= 1.75 |q|`. Thus its error is less than `8u |q|`.
The factor `1.75`, rather than `2`, leaves margin for the higher-order errors
and the rounded threshold multiplication. This is a value-accuracy test,
independent of the sign predicate's filter.

When that test fails, `TwoDiff` recovers the exact tail of each coordinate
difference and of `L-R`. The two product tails are obtained with scalar FMA on
supported modern .NET targets; the portable implementation uses Dekker's split
product. Their arithmetic has the same exact residual. In the compensated
branch the products have the same sign and comparable normal magnitudes
(each exceeds approximately `0.21 S`), so their product residuals do not
underflow. More explicitly, each operand has at most 53 significand bits:
the product and its split partial products share a binary quantum greater
than approximately `0.21e-250 * 2^-106`, well above `2^-1022`. This remains
true when one operand is subnormal; its significand simply has fewer bits.
The splitter multiplication cannot overflow under the coordinate cap, and
any subnormal additions or subtractions in splitting are exact. There is no
additional rounding to single precision.

The exact determinant is now the head `q`, its difference tail, the two signed
product tails, and six products containing coordinate-difference tails:

\[
\begin{aligned}
D=q+q_t+L_t-R_t
 &+x_1 y_{2t}+x_{1t}y_2+x_{1t}y_{2t}\\
 &-y_1 x_{2t}-y_{1t}x_2-y_{1t}x_{2t}.
\end{aligned}
\]

These small terms are summed in fixed groups into a correction `r`, then
`v = fl(q+r)` is formed. Let `T` be the computed sum of the absolute values of
the three exact residuals and the six rounded small products. A path from any
small product to `r` has at most five rounding steps including its product;
the positive calculation of `T` has at most six. With
`gamma(k) = ku/(1-ku)`, the correction's error is bounded by
`gamma(5)/(1-u)^6 * T < 7u T`. The final addition contributes at most `c |v|`.

The compensated filter accepts only if `|v| > 1e-250` and `T <= 0.5 |v|`.
Consequently its error is at most `3.5u |v| + c |v|`, below the same `8u |v|`
contract with a substantial margin. If the coordinate tails, both product
tails and the subtraction tail are all zero, the result is exact and accepted
directly, including an exact zero. No expansion array is needed in these cases.

The magnitude guards also cover gradual underflow. Sums and differences of
binary64 values are integer multiples of `2^-1074`; an exact subnormal sum or
difference is representable. Thus no lost subnormal difference is subsequently
multiplied by a large coordinate. Underflow in a small product or the bound
contributes only a few subnormal quanta, without a subsequent large multiplier.
At the accepted magnitude, this is far smaller than the unused relative-error
margin. Tiny or uncertain values take the exact route. The public coordinate
cap keeps all operations far from overflow.

The `8u` tolerance is a chosen per-contribution accuracy contract, and the
acceptance tests follow from the derived bounds above. Accepted filtered terms
are **not claimed to be faithfully or correctly rounded**. Their relative
error against the exact determinant is at most `8u/(1-8u)`, about `8.9e-16`.
Doubling is exact in the accepted magnitude range. Canonical endpoint ordering
and final negation guarantee reversal antisymmetry in every arithmetic path.

## Exact fallback and subnormal values

An uncertain contribution evaluates `orient2d(o,a,b)` as an exact expansion.
The existing shortcut first checks exact differences, exact products and an
exact product subtraction. Coarse dyadic and integer inputs satisfying that
shortcut need no expansion array or expansion summation, including exactly
cancelling products. Otherwise the expansion is summed to a faithful value.
The expansion rejects nonzero components smaller than `2^-400`, ensuring that
its products and residuals cannot underflow. Doubling the returned value is
therefore safe in the expansion case.

Rejected extreme-exponent cases use integers with a shared power-of-two scale.
The determinant is evaluated exactly, its exponent is incremented to represent
the **doubled** determinant, and that result is rounded once to binary64 using
nearest, ties to even. The final quantum is the larger of the normal 53-bit
precision quantum and `2^-1074`. Converting the integer mantissa and applying
its final power of two cannot cause an earlier rounding or underflow.

It is essential not to compute `2 * round(det)`: a determinant of half a
subnormal quantum would round to zero before doubling. A square with side
`2^-537`, whose area is exactly `double.Epsilon`, is a regression for this case.
The fallback orders endpoints canonically and negates on reversal, preserving
the antisymmetry of the contribution. It does not apply symbolic perturbation
to an area value.

## Scope of the correction

This fixes cancellation **within an edge contribution**, including contributions
at extreme exponents. It does not turn the complete engine into an exact-area
algorithm. Crossing coordinates, sub-edge fractions, multiplication by those
fractions and the sum across edges are still binary64 operations. The term
errors may accumulate on the scale of `u * sum |terms|`; compensating the sum
cannot undo them. Local origins and exact netting of shared boundary segments
reduce that exposure without eliminating it for every input.

In particular, the documented tilted-strip crossing-coordinate limitation
remains. Exact orientation decisions certify topology for the binary64 inputs;
they do not imply exact intersection coordinates, exact area, or recovery of
detail already lost when the caller constructed those inputs.
