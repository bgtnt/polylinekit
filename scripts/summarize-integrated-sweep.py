"""Validate independent baseline/integrated processes, values and reproducible inputs."""
import json
from pathlib import Path
from statistics import median

folder = Path(__file__).resolve().parents[1] / "results/winding/integrated-sweep"
runs = {label: [json.loads((folder / f"{label}-{i}.json").read_text()) for i in (1, 2, 3)]
        for label in ("baseline", "integrated")}
for label, series in runs.items():
    assert [run["Run"] for run in series] == [1, 2, 3]
    assert all(run["Label"] == label for run in series)
    assert len({run["KernelSha256"] for run in series}) == 1
lengths = {len(run["Measurements"]) for series in runs.values() for run in series}
assert len(lengths) == 1
lines = ["# Integrated sweep measurements", "",
         "Microseconds per complete call; medians of three process medians. Ratio > 1 favors integration.",
         "Outcome: 0 = no certificate attempted; 1 = rejected and resumed; 2 = certified simple.", "",
         "| Group | Workload | Vertices | Outcome | Baseline us | Integrated us | Ratio | Integrated range us |",
         "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
allocations = []
records = []
for i in range(next(iter(lengths))):
    baseline = [run["Measurements"][i] for run in runs["baseline"]]
    integrated = [run["Measurements"][i] for run in runs["integrated"]]
    first = baseline[0]
    for row in baseline + integrated:
        for key in ("Group", "Operation", "Name", "Vertices", "InputSha256", "Value"):
            assert row[key] == first[key], (key, row["Name"], row["Vertices"])
        sample = row["Sample"]
        assert len(sample["SamplesUs"]) == len(sample["SamplesBytes"]) == 5
        assert median(sample["SamplesUs"]) == sample["MedianUs"]
        assert median(sample["SamplesBytes"]) == sample["MedianBytes"]
        allocations.extend(sample["SamplesBytes"])
    assert len({row["Outcome"] for row in integrated}) == 1
    b = median(row["Sample"]["MedianUs"] for row in baseline)
    values = [row["Sample"]["MedianUs"] for row in integrated]
    n = median(values)
    outcome = integrated[0]["Outcome"]
    records.append({"group": first["Group"], "name": first["Name"], "vertices": first["Vertices"],
                    "outcome": outcome, "baseline_us": b, "integrated_us": n, "ratio": b / n})
    lines.append(f'| {first["Group"]} | {first["Name"]} | {first["Vertices"]} | {outcome} '
                 f'| {b:.3f} | {n:.3f} | {b / n:.2f}x | {min(values):.3f}-{max(values):.3f} |')
lines.extend(["", f"Allocation samples: {len(allocations)}; maximum {max(allocations):g} B/call.", ""])
for group in ("development", "fresh", "control-regions"):
    selected = [r for r in records if r["group"] == group]
    lines.append(f'{group}: {len(selected)} cases, {sum(r["outcome"] == 2 for r in selected)} certified, '
                 f'{sum(r["outcome"] == 1 for r in selected)} rejected attempts.')
lines.append("")
(folder / "summary.md").write_text("\n".join(lines), encoding="utf-8")
(folder / "summary.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
print("\n".join(lines))
