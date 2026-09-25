# Specialized interval trapezoid arithmetic

This experiment changes only the arithmetic used after the sweep has certified
the nonnegative lower width, upper width and band height. It does not change
the geometry, crossing order, projections, area/error budgets or fallback.
The optional fifth constructor flag `optimizeAreaArithmetic` is off by default.
The measurement baseline uses copied prepared geometry with ROI, endpoint cache
and scalar order filter enabled; direct prepared access remains disabled.

The objective is exact endpoint-bit parity with the previous expression, not a
tighter enclosure or a different numerical tolerance. This note assumes the
same binary64 round-to-nearest and gradual-underflow model as the existing
interval engine. All interval endpoints are finite and ordered on entry.

## Original expression and unchanged steps

Write lower width L, upper width U and height H as closed intervals. The sweep's
certified nonnegative projection establishes `L.Lo, U.Lo, H.Lo >= 0` and ordered
upper endpoints. The original expression is:

```text
S = Add(L, U)
W = Multiply(S, Point(0.5))
T = Multiply(W, H)
area = Add(area, T)
```

`Add` and final accumulation remain the original methods, in the original order.
Neither multiplication is reassociated across the sum, height or accumulator.
`Down`/`Up` mean the existing adjacent-binary64 outward steps, including the
existing `nonfinite-arithmetic` failure when the step is nonfinite. All exact
zero/one shortcuts remain in their original order.

Do not assume S or W is nonnegative merely because L and U are. If both lower
width endpoints are zero and neither entire width is zero, `Add` evaluates
`Down(0)` and introduces a lower endpoint of `-double.Epsilon`. The half step
can retain that negative lower endpoint. It is not clamped away.

## Halving

For finite ordered S, round-to-nearest multiplication by the positive constant
0.5 is monotone. The original four products reduce to two duplicate pairs:

```text
p0 = p1 = RN(S.Lo * 0.5)
p2 = p3 = RN(S.Hi * 0.5)
```

Thus the same minimum and maximum are obtained by the specialized result
`[Down(RN(S.Lo * 0.5)), Up(RN(S.Hi * 0.5))]`. It first preserves the generic
identities: an exact zero interval returns Zero, and `S == Point(1)` returns
`Point(0.5)` without an extra outward step.

Halving is not assumed exact for subnormals. A smallest-subnormal endpoint
times 0.5 can round to zero; the same `Down`/`Up` operations still surround it.
No exact-product-residual or relative-error approximation is used. W.Hi stays
nonnegative, while W.Lo is allowed to be negative.

## Multiplication by nonnegative height

The only required sign facts are `W.Hi >= 0` and `H.Lo >= 0`. H is ordered, so
the extrema of the real endpoint products are:

```text
minimum = W.Lo * (W.Lo < 0 ? H.Hi : H.Lo)
maximum = W.Hi * H.Hi
```

If W.Lo is nonnegative, the product is increasing in both arguments. If W.Lo
is negative, multiplying that endpoint by the greater height produces the
more negative lower bound. The nonnegative upper endpoint always attains its
maximum with H.Hi. This also covers intervals that straddle zero.

Round-to-nearest is monotone, so these two selected products have the same
rounded extrema as the original four-product `Math.Min`/`Math.Max` expression.
Applying the unchanged `Down` and `Up` produces the same result endpoints.
The exact-zero and point-one shortcuts are checked before these products,
matching generic `Multiply(W, H)` in their original order.

Signed-zero ties cannot alter the returned endpoint bits: both zero signs have
the same adjacent values under `Math.BitDecrement`/`Math.BitIncrement`. Identity
branches return the same operand or Zero as before. Inputs are finite, so no
discarded product can create `0 * Infinity` NaN. If a discarded product would
overflow, the corresponding extremum also overflows; the existing outward
step therefore rejects both paths with the same numerical failure reason.

## Scope, costs and validation contract

The ordinary non-identity source expression evaluates eight endpoint-product
expressions across its two generic multiplications; the specialized form has
four and removes their general min/max reductions. The first generic multiply
contains duplicate expressions that a JIT may already eliminate. These are
source-level counts, not a claim of four fewer machine multiplies or a CPU
speedup. Complete-query measurements decide whether the change is useful.

`TryAreaArithmeticForCheck` accepts finite ordered nonnegative width/height
intervals. It exposes the actual generic or specialized trapezoid and original
accumulation, optionally repeated from Zero, so independent exact arithmetic
can check the mathematical enclosure and endpoint-bit parity. Invalid inputs
or a nonpositive repeat count report `input-contract`; arithmetic uncertainty
reports the same engine failure reason. The test-only helper is not a public
library API.

Required controls include exact zero/one identities, signed zeros, adjacent
subnormals, underflowed halves/products, a negative outward lower width with a
large height, overflow boundaries, wide exponent ranges and repeated sums.
Geometry validation must retain result/radius bits, fallback reason and every
existing diagnostic against the generic copied-prepared baseline, normally
and with hardware intrinsics disabled. No new certificate is introduced: exact
parity preserves the original certificate and its limitations.
