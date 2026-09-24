import json
from collections import defaultdict
from pathlib import Path
from statistics import median

root = Path(__file__).resolve().parent
data = defaultdict(lambda: defaultdict(list))
allocations = []
for run in range(1, 4):
    samples = defaultdict(list)
    for line in (root / f"exclusive-run{run}.jsonl").read_text(encoding="utf-8-sig").splitlines():
        row = json.loads(line)
        if "kernel" not in row:
            continue
        samples[(row["kernel"], row["n"], row["method"])].append(row["ns"])
        allocations.append(row["bytes"])
    for (kernel, n, method), values in samples.items():
        data[(kernel, n)][method].append(median(values))
lines = ["# Native microkernel results", "", "Median of the three fresh-process medians; 9 samples in each. `exclusive-run*.jsonl` only; preliminary run files are excluded.", "", "| Kernel | N | C# array ns | C# pointer ns | C++ with P/Invoke ns | array/native | pointer/native |", "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
for (kernel, n), methods in sorted(data.items()):
    a, p, c = (median(methods[name]) for name in ("cs_array", "cs_pointer", "cpp_pinvoke"))
    lines.append(f"| {kernel} | {n} | {a:.2f} | {p:.2f} | {c:.2f} | {a/c:.2f}x | {p/c:.2f}x |")
lines += ["", f"Maximum measured allocation: {max(allocations):g} bytes per operation.", "", "This measures only two small kernels. It does not measure the complete geometry operation, native input adaptation, sorting, exact predicates, boundary chains, or native workspace ownership. No full-engine C++ speedup is established."]
(root / "results.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
