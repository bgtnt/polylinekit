# Solaris evaluation result

Correctness is verified against the frozen Solaris/SpaceNet 2 sample: all 172
expected building scores and matching decisions agree across four C# backends.
The sample contains six images, with 144 predictions and 169 truth polygons
eligible after the original filters. Totals are 87 TP, 57 FP and 82 FN.

The common matching run visits 205 bounds candidates, including 162 positive
intersections. Core and Clipper need no fallback on these inputs. Both operands
are convex in 49 pairs; the conservative convex backend uses NTS fallback for
the other 156. See [the example](README.md) and [source attribution](data/README.md).

Timing is pending the first clean-source execution of the already-fixed
[protocol](PROTOCOL.md). Correctness alone is not a speed result. The final report
will retain all backends, subgroups, process variation, allocations and source/
binary/data identities, including cases where PolylineKit is slower.
