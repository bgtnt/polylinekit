# Scalar endpoint-order filter

This is an optional experiment in `GuardedDoubleSweep`, not a shipping geometry
backend. `scalarOrderFilter` defaults to `false`. It may replace an endpoint-X
interval comparison only when it certifies a **strict** order of the two exact
supporting lines at the supplied original binary64 Y coordinate. Otherwise the
unchanged interval comparison and exact-source tie handling run. Failure of this
filter does not itself abandon the sweep.

Area integration, crossing construction and ordering, support-line equivalence,
fill rules, and the final area error budget are unchanged. Scalar metadata lives
in a separate array allocated and prepared only when the flag is enabled. No
scalar evaluation or pair result is cached.

## Arithmetic model and scope

The existing experiment requires IEEE binary64 round-to-nearest, gradual
underflow, and correctly rounded arithmetic. This filter uses ordinary scalar
subtraction, multiplication, and addition; it requires no fused-multiply-add
instruction or product residual. It adds no claim about other rounding modes.

For each nonhorizontal edge, let its original endpoints be `(x0,y0)` and
`(x1,y1)`, with `y0 < y1`. All coordinates obey the existing `1e100` magnitude
limit. The tested level is an original endpoint Y from the sweep and lies in
both compared edges' closed Y ranges. There is no extrapolation. The standalone
`TryScalarOrderAtY` probe accepts either endpoint direction, validates this
contract, and constructs slope enclosures using the same interval code as the
sweep. Its accepted sign is the sign of `xA(y)-xB(y)`.

The edge already has a certified enclosure `[sLo,sHi]` of the exact dyadic-input
slope `sExact = (x1-x0)/(y1-y0)`. A floating midpoint is clamped into that
enclosure to obtain `s`; any finite choice inside it is valid. Nonfinite midpoint
or bound construction disables the scalar filter for this edge.

## Prepared uniform bound

Let `u = 2^-53`, `epsilon = 2^-1074`, and `eta = epsilon/2` as a real number.
Preparation computes upward-rounded bounds:

```
H >= y1-y0
r >= max(s-sLo, sHi-s) >= abs(s-sExact)
P >= abs(s)*H
R >= r*H + 4*u*(abs(x0)+P) + 2*epsilon
```

Each nonnegative bound operation is rounded to nearest and then advanced one
binary64 value using `Math.BitIncrement`. Exact zero cases for `r` and `P` are
handled explicitly. This remains an upper bound when a product underflows to
zero: its successor is `epsilon`. Infinity disables the edge's filter rather
than changing the sweep's existing interval/fallback behavior.

Preparation additionally requires `P <= 2^500` and `R <= 2^500`. Together with
the coordinate cap, this leaves ample finite range for every hot-path center,
center difference, and radius sum. An enormous slope is not itself unsafe if
its permitted height makes these bounds small; all accepted input levels remain
inside the edge. The slope enclosure is not assumed relatively narrow.

At evaluation, with exact `d = y-y0` and `0 <= d <= H`, the scalar computation is

```
t = RN(y-y0)
p = RN(s*t)
c = RN(x0+p)
```

Addition/subtraction of two binary64 numbers has **zero** error whenever its
exact result is subnormal, because their inputs lie on the `epsilon` lattice.
Otherwise the usual relative bound applies. Hence
`abs(t-d) <= u*d` and `abs(t) <= (1+u)*H`, including underflowed differences.
The multiplication may underflow and needs an absolute term:

```
abs(p-s*t) <= u*(1+u)*P + eta
abs(p)     <= (1+u)^2*P + eta
abs(c-(x0+p)) <= u*(abs(x0)+abs(p))
```

The last inequality also holds for a subnormal exact sum, whose error is zero.
Combining these expressions gives

```
abs(c-(x0+s*d))
  <= u*abs(x0) + (3*u+3*u^2+u^3)*P + (1+u)*eta
  <= 4*u*(abs(x0)+P) + 2*epsilon.
```

Finally, `abs((s-sExact)*d) <= r*H`, so the exact X coordinate of the original
edge lies in the real interval `[c-R,c+R]`. The prepared `R` includes rounding
of its own construction; it is not an unrounded symbolic threshold.

## Strict comparison without additional outward operations

For two scalar centers and prepared radii, the hot path computes exactly one
rounded operation on each side:

```
D = RN(cB-cA)
T = RN(RA+RB)
```

Round-to-nearest is monotone. Therefore `D > T` implies the **exact real**
center difference `cB-cA > RA+RB`: if the real inequality were reversed or equal,
monotonic rounding could not produce a strict greater result. Thus every point
in A's real enclosure is smaller than every point in B's. The reverse order
uses `D < -T`; rounding is sign-symmetric and unary negation is exact.

This argument does not claim that `D` or `T` is individually outward rounded.
It relies on comparing two correctly rounded expressions with a strict
inequality. Rounded equality is inconclusive and always runs the interval path.
No approximate equality, epsilon tolerance, or guessed order is introduced.

## Observable behavior and limitations

`FilterAttemptCount = FilterAcceptedCount + FilterIntervalCount`. These counters
apply after the existing same-edge/same-support shortcuts; all reset per call.
The existing X evaluation/cache counters describe only the interval evaluator.

The uniform radius can be loose for long edges, large offsets, or broad slope
enclosures, particularly near crossings and exact endpoints. Such comparisons
remain on the previous interval path. Previously certified calls should retain
the same topology, area arithmetic and output/certificate bits; a newly
certified strict order may also allow a previously uncertain call to progress.
Neither correctness nor speed improvement is inferred from the acceptance count.
