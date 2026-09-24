# Winding edge-term accuracy

The engine sums directed boundary contributions. For endpoints `a`, `b` and the
chain's local origin `o`, its unweighted contribution is

\[
C = 2\,\operatorname{orient2d}(o,a,b)
  = ((a-o)+(b-o))\mathbin\times(b-a).
\]

The accumulated contributions are divided by four. The midpoint expression
avoids subtracting two large endpoint products for a short edge far from the
origin. It does **not** by itself prevent cancellation inside its two products.
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

## Fast value filter

This filter certifies a bound on the **value**, independently of the orientation
predicate's sign filter. Let `u = 2^-53` and `c = u/(1-u)`. For a normal result
`z` of one rounded operation, its local error is at most `c |z|`.

`TwoDiff` represents each offset exactly as a head and a tail. In one coordinate,
the engine forms

```
h = fl(head(a-o) + head(b-o))
t = fl(tail(a-o) + tail(b-o))
d = fl(h + t)
H = |h| + |t| + |d|
```

The error between `d` and the exact midpoint sum is at most `c H`: this includes
the addition of the heads, the addition of the tails and the final addition.
For `e = fl(b-a)`, the difference error is at most `c |e|`. Define

```
L = fl(dx * ey)
R = fl(dy * ex)
q = fl(L - R)
```

Propagating both operand errors through each product, and including product and
final-subtraction rounding, gives

\[
|C-q| \le c\left[(1+c)(H_x|e_y|+H_y|e_x|)
                  +(2+c)(|L|+|R|)+|q|\right].
\]

The implementation uses the conservative computable bound

\[
E = \operatorname{fl}\left(B\operatorname{fl}
  (H_x|e_y|+H_y|e_x|+2(|L|+|R|)+|q|)\right),
\]

where `B = 1.1102230246251606e-16`, at least `u(1+32u)`. The positive expression
has at most six rounded operations along a dependency chain, plus the final
multiplication by `B`. Their possible downward rounding and the factors
`c(1+c)/u` together require less than `11u` of inflation; `32u` leaves a
conservative margin. Multiplication by two is exact in this range. The public
coordinate cap keeps every operation far from overflow.

The filter accepts `q` only if:

* `|L| + |R| > 1e-250`; and
* `E <= 8u |q|`.

The first guard also covers gradual underflow. Sums and differences of
binary64 values are integer multiples of `2^-1074`; when their exact result is
subnormal it is representable and the operation is exact. In particular, there
is no underflow error in a midpoint or edge difference that could later be
amplified by multiplication by a large coordinate. Underflow can instead occur
in the two final products or the evaluation of their positive error bound.
Each such local error is at most half a subnormal quantum and has no subsequent
large multiplier. The unused inflation margin at the normal-magnitude
threshold exceeds `1e-281`, much greater than their combined underflow error.
Tiny contributions instead take the exact route. The tolerance `8u` is a chosen
per-contribution accuracy contract; the error bound and its inflation are
derived from the operations above. The accepted result is **not claimed to be
faithfully or correctly rounded**. Its absolute error is bounded by `E`, and
its relative error against the exact contribution is at most
`8u/(1-8u)` (about `8.9e-16`).

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
