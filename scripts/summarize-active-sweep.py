"""Validate and summarize three paired active-sweep processes; standard library only."""
import json
import sys
from pathlib import Path
from statistics import median

root = Path(__file__).resolve().parents[1]
folder = root / "results/winding/active-sweep"
prefix = sys.argv[1] if len(sys.argv) > 1 else ""
runs = [json.loads((folder / f"{prefix}run{i}.json").read_text()) for i in (1, 2, 3)]
policy = bool(prefix)
assert [run["Run"] for run in runs] == [1, 2, 3]
rows = [run["Measurements"] for run in runs]
assert len({len(run) for run in rows}) == 1
lines = [
    "# Active sweep: paired process medians",
    "",
    "Microseconds per complete ClosedPath NonZero request; hybrid includes fallback.",
    "Ratio = current engine / hybrid. Values above 1 favor the prototype.",
    "",
    "| Workload | Vertices | Certified | Engine us | Hybrid us | Ratio | Hybrid range us | Peak active | Neighbor checks |" +
    (" Selected | Guarded us | Guarded ratio |" if policy else ""),
    "| --- | ---: | :---: | ---: | ---: | ---: | ---: | ---: | ---: |" +
    (" :---: | ---: | ---: |" if policy else ""),
]
allocation_samples = []
for group in zip(*rows):
    first = group[0]
    for item in group:
        for key in ("Name", "Vertices", "InputSha256", "Accepted", "Value", "Statistics"):
            assert item[key] == first[key], (key, first["Name"])
        if policy:
            assert item["Selected"] == first["Selected"]
        for method in (("Baseline", "Hybrid", "Guarded") if policy else ("Baseline", "Hybrid")):
            sample = item[method]
            assert len(sample["SamplesUs"]) == len(sample["SamplesBytes"]) == 5
            assert median(sample["SamplesUs"]) == sample["MedianUs"]
            assert median(sample["SamplesBytes"]) == sample["MedianBytes"]
            allocation_samples.extend(sample["SamplesBytes"])
    baseline = median(item["Baseline"]["MedianUs"] for item in group)
    hybrid_values = [item["Hybrid"]["MedianUs"] for item in group]
    hybrid = median(hybrid_values)
    stats = first["Statistics"]
    guarded = median(item["Guarded"]["MedianUs"] for item in group) if policy else None
    lines.append(
        f'| {first["Name"]} | {first["Vertices"]} | {"yes" if first["Accepted"] else "no"} '
        f'| {baseline:.3f} | {hybrid:.3f} | {baseline / hybrid:.2f}x '
        f'| {min(hybrid_values):.3f}–{max(hybrid_values):.3f} '
        f'| {stats["PeakActive"]} | {stats["NeighborChecks"]} |' +
        (f' {"yes" if first["Selected"] else "no"} | {guarded:.3f} | {baseline / guarded:.2f}x |' if policy else "")
    )
lines.extend(["", f"Warm allocation samples: {len(allocation_samples)}; maximum {max(allocation_samples):g} B/call.", ""])
(folder / f"{prefix}summary.md").write_text("\n".join(lines), encoding="utf-8")
print("\n".join(lines))
