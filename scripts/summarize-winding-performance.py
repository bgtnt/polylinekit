"""Summarize the recorded baseline/optimized and ablation measurements (stdlib only)."""
import json
from pathlib import Path
from statistics import median
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "results/winding/performance"

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

rows = {}
for variant in ("baseline", "optimized"):
    for row in read(root / variant / "summary.json"):
        if row["Method"].startswith("Winding"):
            rows.setdefault((row["Workload"], row["Vertices"]), {})[variant] = row

lines = ["# Managed winding comparison", "", "Median of three process medians, microseconds per operation.", "",
         "| Workload | Vertices per path | Baseline us | Optimized us | Baseline / optimized | Optimized bytes/op |",
         "| --- | ---: | ---: | ---: | ---: | ---: |"]
for (name, n), pair in sorted(rows.items()):
    before, after = pair["baseline"], pair["optimized"]
    lines.append(f'| {name} | {n} | {before["MedianUs"]:.2f} | {after["MedianUs"]:.2f} | '
                 f'{before["MedianUs"]/after["MedianUs"]:.2f} | {after["Bytes"]:g} |')
(root / "comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

variants = ("baseline", "scalar", "predicates-only", "checkout")
cases = {}
for variant in variants:
    for run in range(1, 4):
        for row in read(root / "ablations" / f"{variant}-{run}.json")["Measurements"]:
            cases.setdefault((row["Name"], row["N"]), {}).setdefault(variant, []).append(row["MedianUs"])

lines = ["# Managed ablations", "", "Three fresh processes per variant; five samples per workload in each.", "",
         "`scalar` retains all managed improvements but has no FMA product. `predicates-only` retains the old "
         "sweep/input processing with optimized predicates and FMA. `checkout` is the experimental FMA version "
         "at 97d1a97. The final implementation at 6fc1c35 keeps the scalar version.", "",
         "| Workload | N | Baseline us | Scalar us | Predicates only us | All with FMA us | Scalar / FMA | Baseline / scalar |",
         "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
for (name, n), samples in sorted(cases.items()):
    if any(len(samples[v]) != 3 for v in variants):
        raise ValueError("Incomplete ablation group")
    b, s, p, f = (median(samples[v]) for v in variants)
    lines.append(f"| {name} | {n} | {b:.2f} | {s:.2f} | {p:.2f} | {f:.2f} | {s/f:.3f} | {b/s:.2f} |")
(root / "ablations" / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(root / "comparison.md")
print(root / "ablations" / "summary.md")
