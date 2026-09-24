"""Validate independent baseline/integrated processes, values and reproducible inputs."""
import json
import argparse
from pathlib import Path
from statistics import median

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path, nargs="?",
                    default=Path(__file__).resolve().parents[1] / "artifacts/assembly-benchmarks",
                    help="Folder containing baseline-1..3.json and integrated-1..3.json")
folder = parser.parse_args().directory
if not __debug__:
    parser.error("Run without Python -O: this validator requires assertion checks.")
runs = {label: [json.loads((folder / f"{label}-{i}.json").read_text()) for i in (1, 2, 3)]
        for label in ("baseline", "integrated")}
environment = ("Runtime", "OS", "Cpu", "LogicalProcessors", "TieredCompilation")
first_run = runs["baseline"][0]
for label, series in runs.items():
    assert [run["Run"] for run in series] == [1, 2, 3]
    assert all(run["Label"] == label for run in series)
    assert len({run["KernelSha256"] for run in series}) == 1
    for run in series:
        for key in environment:
            assert run[key] == first_run[key], ("Benchmark environments differ", key)
lengths = {len(run["Measurements"]) for series in runs.values() for run in series}
assert len(lengths) == 1 and next(iter(lengths)) > 0
lines = ["# Assembly comparison measurements", "",
         "Microseconds per complete call; medians of three process medians. Ratio > 1 favors the candidate (`integrated` file label).",
         "Outcome: -1 = diagnostic unavailable; 0 = no certificate attempted; 1 = rejected and resumed; 2 = certified simple.", "",
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
        # A concatenated vertex hash alone does not identify where two filled regions split.
        assert row.get("FirstVertices") == first.get("FirstVertices")
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
