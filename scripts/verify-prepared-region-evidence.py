#!/usr/bin/env python3
"""Verify extracted prepared-region evidence; optionally re-summarize, never time."""
import argparse
from datetime import datetime
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import shutil
import statistics
import subprocess
import tempfile

METHODS = ("Core-prepared-region", "Clipper64-reused-data", "NTS-OverlayNGRobust")
SCOPES = ("prepare-catalogues", "warm-table", "prepare-plus-one-table")
SCENARIOS = ("census-county-to-district", "census-district-to-county") + tuple(
    f"{kind}-{count}" for count in (16, 64, 256, 1024)
    for kind in ("disjoint-squares", "shells-with-holes"))
IDENTITY_KEYS = ("SchemaVersion", "Runtime", "OS", "Architecture", "Processor",
                 "TieredCompilation", "ForceScalar", "Vector256", "CoreVersion", "CoreTarget",
                 "BinarySha256", "SourceSha256", "InputSha256", "ProtocolSha256", "CommonOrigin",
                 "ClipperGrid", "TimedFillRule", "WarmMilliseconds", "CalibrationMilliseconds",
                 "Batches", "Scenarios", "Accuracy")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def safe_path(value):
    require(isinstance(value, str) and value and "\\" not in value and ":" not in value,
            "Unsafe manifest path.")
    path = PurePosixPath(value)
    require(not path.is_absolute() and ".." not in path.parts and path.as_posix() == value,
            "Unsafe or noncanonical manifest path.")
    return value


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)


def finite(value):
    return not isinstance(value, bool) and isinstance(value, (int, float)) and math.isfinite(value)


def validate_experiment(root, experiment):
    """Independently check all samples and recreate every summary cell with Python."""
    side = experiment["Name"]
    require(side in ("baseline", "candidate"), "Unexpected experiment.")
    runs = [read_json(root / side / "raw" / f"{side}-run-{run}.json") for run in (1, 2, 3)]
    first = runs[0]
    require(first["CoreVersion"].endswith("+" + experiment["Revision"]), "Source revision differs.")
    require(first["SchemaVersion"] == 1 and first["TieredCompilation"] == "0" and
            first["ForceScalar"] is False and first["CoreTarget"] == ".NETCoreApp,Version=v10.0" and
            first["TimedFillRule"] == "NonZero" and first["Batches"] == 5,
            "Unexpected original measurement mode.")
    metadata = {row["Name"]: row for row in first["Scenarios"]}
    require(len(first["Scenarios"]) == len(metadata) == 10 and set(metadata) == set(SCENARIOS),
            "Incomplete scenario inventory.")
    accuracy_keys = set()
    for group in first["Accuracy"]:
        scenario, method, rule = group["Scenario"], group["Method"], group["FillRule"]
        key = (scenario, method, rule)
        require(scenario in metadata and method in METHODS and rule in ("NonZero", "EvenOdd") and
                key not in accuracy_keys, "Unexpected or duplicate accuracy group.")
        accuracy_keys.add(key)
        meta = metadata[scenario]
        require(group["Pairs"] == meta["Pairs"] and group["Candidates"] == meta["ExpectedCandidates"],
                "Accuracy pair inventory differs.")
        own = meta["ExpectedOwnArea"] if not meta["Gis"] else 0
        intersection = meta["ExpectedIntersection"] if not meta["Gis"] else 0
        budgets = (1, 1, 1e-8) if meta["Gis"] else (
            2e-12 * max(1, own), 2e-12 * max(1, intersection),
            2e-12 * max(1, intersection / own))
        for field, budget in zip(("MaximumOwnAreaError", "MaximumIntersectionError", "MaximumCoverageError"), budgets):
            error = group[field]
            require(finite(error) and 0 <= error <= budget, "Recorded accuracy exceeds its fixed budget.")
    require(len(accuracy_keys) == 60, "Incomplete accuracy inventory.")
    for name, expected in first["BinarySha256"].items():
        require(sha((root / side / "bin" / (name + ".dll")).read_bytes()) == expected,
                "Frozen binary identity differs: " + name)
    for path, expected in first["InputSha256"].items():
        require(sha((root / side / "source/examples/RegionCoverage/data" / safe_path(path)).read_bytes()) == expected,
                "Input source hash differs: " + path)
    protocol = root / side / "source/tests/PolylineKit.MultiRingChecks/PROTOCOL.md"
    require(sha(protocol.read_bytes()) == first["ProtocolSha256"], "Protocol hash differs.")
    differences = []
    for path, expected in first["SourceSha256"].items():
        data = (root / side / "source" / safe_path(path)).read_bytes()
        if sha(data) != expected:
            # The report explicitly records these two checkout CRLF/Git archive LF differences.
            require(side == "candidate" and path in (
                "src/PolylineKit.Core/packages.lock.json", "tests/PolylineKit.MultiRingChecks/packages.lock.json") and
                b"\r" not in data and sha(data.replace(b"\n", b"\r\n")) == expected,
                "Measured source hash differs: " + path)
            differences.append(path)
    require(differences == experiment["CheckoutCrlfArchiveLf"], "Source line-ending inventory differs.")
    expected_keys = {(s, m, scope) for s in SCENARIOS for m in METHODS for scope in SCOPES}
    matrices = []
    for index, run in enumerate(runs, 1):
        require(run["Run"] == index, "Run identity differs.")
        require(all(run[key] == first[key] for key in IDENTITY_KEYS), "Run metadata differs.")
        matrix = {}
        for row in run["Measurements"]:
            key = (row["Scenario"], row["Method"], row["Scope"])
            require(key in expected_keys and key not in matrix, "Unexpected or duplicate timing row.")
            matrix[key] = row
            require(len(row["Samples"]) == 5, "Expected five batches per timing row.")
            for sample in row["Samples"]:
                count = sample["Iterations"]
                require(type(count) is int and 0 < count <= 16384 and count & (count - 1) == 0,
                        "Invalid iteration count.")
                require(finite(sample["Nanoseconds"]) and sample["Nanoseconds"] > 0 and
                        finite(sample["Bytes"]) and sample["Bytes"] >= 0, "Invalid sample.")
            require(row["MedianNanoseconds"] == statistics.median(s["Nanoseconds"] for s in row["Samples"]) and
                    row["MedianBytes"] == statistics.median(s["Bytes"] for s in row["Samples"]),
                    "Wrong recorded batch median.")
        require(set(matrix) == expected_keys, "Incomplete timing matrix.")
        for scenario in SCENARIOS:
            for method in METHODS:
                warm = matrix[(scenario, method, "warm-table")]["Value"]
                fresh = matrix[(scenario, method, "prepare-plus-one-table")]["Value"]
                require(warm == fresh and warm["Candidates"] == metadata[scenario]["ExpectedCandidates"],
                        "Prepared/fresh digests or candidate inventory differ.")
        matrices.append(matrix)
    summary = read_json(root / side / "raw" / f"{side}-summary.json")
    cells = []
    for scenario, method, scope in sorted(expected_keys):
        rows = [matrix[(scenario, method, scope)] for matrix in matrices]
        require(all(row["Value"] == rows[0]["Value"] for row in rows), "Digests differ across processes.")
        times = [row["MedianNanoseconds"] for row in rows]
        allocations = [row["MedianBytes"] for row in rows]
        cells.append({"Scenario": scenario, "Method": method, "Scope": scope,
                      "MedianNanoseconds": statistics.median(times), "MinimumNanoseconds": min(times),
                      "MaximumNanoseconds": max(times), "MedianBytes": statistics.median(allocations),
                      "MinimumBytes": min(allocations), "MaximumBytes": max(allocations), "Value": rows[0]["Value"]})
    expected_summary = {"Samples": 1350, "Processes": 3, "RowsPerProcess": 90,
                        "CoreVersion": first["CoreVersion"],
                        "RunFiles": [{"File": f"{side}-run-{run}.json",
                                      "Sha256": sha((root / side / "raw" / f"{side}-run-{run}.json").read_bytes())}
                                     for run in (1, 2, 3)],
                        "Measurements": cells,
                        "Interpretation": "No speed threshold: compare matching scenarios and scopes; disagreement and unfavorable results remain reportable."}
    require(summary == expected_summary, "Python re-summarization differs from original summary: " + side)
    return {"Name": side, "Samples": 1350, "SummaryCells": 90, "PythonSummaryMatches": True,
            "FrozenSummarySha256": sha((root / side / "raw" / f"{side}-summary.json").read_bytes())}


def verify(root, summarize=False):
    manifest_bytes = (root / "manifest.json").read_bytes()
    manifest = read_json(root / "manifest.json")
    require(manifest["Kind"] == "paired-prepared-region-evidence" and manifest["SchemaVersion"] == 1 and
            manifest["NewTimings"] is False, "Unexpected evidence manifest.")
    expected = set()
    for entry in manifest["Files"]:
        path = safe_path(entry["Path"])
        require(path not in expected and path not in ("manifest.json", "SHA256SUMS"), "Duplicate manifest path.")
        expected.add(path)
    for path in root.rglob("*"):
        require(not path.is_symlink(), "Symlinks are not supported in evidence bundles.")
    actual = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
    require(actual == expected | {"manifest.json", "SHA256SUMS"}, "Bundle file inventory differs.")
    for entry in manifest["Files"]:
        data = (root / entry["Path"]).read_bytes()
        require(len(data) == entry["Bytes"] and sha(data) == entry["Sha256"], "Checksum differs: " + entry["Path"])
    lines = [f'{entry["Sha256"]}  {entry["Path"]}' for entry in manifest["Files"]]
    lines.append(sha(manifest_bytes) + "  manifest.json")
    require((root / "SHA256SUMS").read_text(encoding="utf-8").splitlines() == lines, "SHA256SUMS differs.")
    require([e["Name"] for e in manifest["Experiments"]] == ["baseline", "candidate"], "Expected both experiments.")
    experiments = [validate_experiment(root, experiment) for experiment in manifest["Experiments"]]
    baseline = read_json(root / "baseline/raw/baseline-run-1.json")
    candidate = read_json(root / "candidate/raw/candidate-run-1.json")
    for key in ("InputSha256", "ProtocolSha256", "Accuracy", "Scenarios"):
        require(baseline[key] == candidate[key], "Paired identity differs: " + key)
    comparison = read_json(root / "coordination/comparison.json")
    require(comparison["BaselineRevision"] == manifest["Experiments"][0]["Revision"] and
            comparison["CandidateRevision"] == manifest["Experiments"][1]["Revision"], "Paired revision differs.")
    order = ("baseline-1", "candidate-1", "candidate-2", "baseline-2", "baseline-3", "candidate-3")
    require(comparison["ProcessOrder"] == list(order), "Process order differs.")
    previous_end = None
    windows = []
    for sequence, label in enumerate(order, 1):
        window = read_json(root / f"coordination/{sequence}-{label}-window.json")
        side, run = label.split("-")
        start, end = datetime.fromisoformat(window["StartUtc"]), datetime.fromisoformat(window["EndUtc"])
        require(window["Sequence"] == sequence and window["Name"] == side and window["Run"] == int(run) and
                window["Returned"] is True and window["ExitCode"] == 0 and end >= start and
                (previous_end is None or start > previous_end), "Stopped, overlapping or invalid process window.")
        require(window["Revision"] == comparison["BaselineRevision" if side == "baseline" else "CandidateRevision"],
                "Window source revision differs.")
        previous_end = end
        windows.append((start, end))
    host_log = (root / "coordination/host-log.redacted.txt").read_text(encoding="utf-8")
    starts = re.findall(r"HOST_BENCHMARK START (\S+)", host_log)
    ends = re.findall(r"HOST_BENCHMARK DONE (\S+)", host_log)
    require(len(starts) == len(ends) == 1 and datetime.fromisoformat(starts[0]) <= windows[0][0] and
            datetime.fromisoformat(ends[0]) >= windows[-1][1], "Process windows lie outside the reserved host slot.")
    execution = read_json(root / "coordination/execution.json")
    require(execution["Completed"] is True and execution["Failure"] is None, "Original measurement did not finish.")
    if summarize:
        with tempfile.TemporaryDirectory(prefix="polylinekit-prepared-summary-") as temp:
            for experiment, result in zip(manifest["Experiments"], experiments):
                side = experiment["Name"]
                copied = Path(temp) / side
                shutil.copytree(root / side / "raw", copied)
                output = copied / "regenerated-summary.json"
                command = ["dotnet", str(root / side / "bin/PolylineKit.Experiments.dll"), "summarize"]
                command.extend(str(copied / f"{side}-run-{run}.json") for run in (1, 2, 3))
                command.append(str(output))
                process = subprocess.run(command, capture_output=True, text=True, check=True)
                original = (copied / f"{side}-summary.json").read_bytes()
                require(output.read_bytes() == original, "Frozen .NET summary is not byte-identical: " + side)
                result.update(FrozenDotnetExitCode=process.returncode, ByteIdenticalDotnetSummary=True)
    return {"VerifiedFiles": len(manifest["Files"]), "OriginalSamples": 2700,
            "NewTimings": False, "Experiments": experiments}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Extracted evidence root")
    parser.add_argument("--summarize", action="store_true", help="Also run original frozen .NET 10 summarizers")
    args = parser.parse_args()
    print(json.dumps(verify(args.directory.resolve(), args.summarize), indent=2))
