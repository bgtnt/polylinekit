#!/usr/bin/env python3
"""Validate and analyze the frozen PolylineKit optimization experiment (stdlib only).

Use after all four variants have completed three, or six, independent rounds.
The unit of comparison is a paired process median, never an individual batch.
Archived evidence can be analyzed without local DLLs. Recorded hashes are always
checked against the manifest; actual binary verification is reported separately.
The script does not change source, rerun timings, or select code.
"""

import argparse
import datetime as dt
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
import struct
import sys


PROTOCOL = "polylinekit-optimization-v1"
BASELINE = "e6b4978522b3221b36f054024be338b8e3618577"
VARIANTS = ("baseline", "cleanup", "modern-scalar", "simd")
VERTICES = (16, 64, 256, 1024)
SPEC = {
    "exact/normalize-align-area": ("open-exact", "NormalizeAlignArea", True, 64),
    "perturbed/normalize-align-area": ("open-perturbed", "NormalizeAlignArea", True, 64),
    "graph/integral": ("graph", "BetweenGraphs", False, None),
    "exact/normalize": ("open-exact", "NormalizeOnePath", False, None),
    "exact/apply": ("open-exact", "AffineApply", False, None),
    "exact/fit64": ("open-exact", "OpenFit", False, 64),
    "perturbed/fit64": ("open-perturbed", "OpenFit", False, 64),
    "closed/fit64": ("closed-asymmetric", "ClosedPhaseFit", False, 64),
    "closed/fit256": ("closed-asymmetric", "ClosedPhaseFit", False, 256),
    "exact/bridge": ("open-exact", "EndpointBridgedArea", False, None),
    "perturbed/bridge": ("open-perturbed", "EndpointBridgedArea", False, None),
    "closed/xor": ("closed-asymmetric", "FilledRegionDifference", False, None),
}
KEYS = tuple((n, name) for n in VERTICES for name in SPEC)
PAIRS = (("cleanup", "baseline"), ("modern-scalar", "baseline"), ("simd", "baseline"),
         ("modern-scalar", "cleanup"), ("simd", "modern-scalar"))
COMMON_METADATA = (
    "Protocol", "HarnessRevision", "InputsSha256", "Runtime", "OS", "Architecture", "CPU",
    "Processors", "StopwatchFrequency", "TieredCompilation", "ReadyToRun", "EnableHWIntrinsic",
    "Hardware", "WarmupMilliseconds", "MinimumCalibrationMilliseconds", "Batches", "Cases",
    "OpenSamples", "ClosedSamples", "Area", "Correspondence",
)


class AuditError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise AuditError(message)


def read_json(path):
    # .NET's round-trip JSON can write negative double zero as -0. Preserve its bit.
    def integer(text):
        return -0.0 if text == "-0" else int(text)

    def invalid_constant(text):
        raise AuditError(f"{path}: nonstandard JSON numeric constant {text}")

    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream, parse_int=integer, parse_constant=invalid_constant)


def finite(value, label, positive=False):
    require(isinstance(value, (int, float)) and not isinstance(value, bool), f"{label}: not numeric")
    require(math.isfinite(value), f"{label}: nonfinite")
    if positive:
        require(value > 0, f"{label}: must be positive")
    return float(value)


def bits(value):
    return struct.pack(">d", float(value)).hex()


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def check_hash(value, length, label):
    require(isinstance(value, str) and re.fullmatch(rf"[0-9a-fA-F]{{{length}}}", value),
            f"{label}: malformed hash")


def metadata(data, variant, reference, manifest, inputs_hash, label):
    value = data.get("Metadata", {})
    require(value.get("Protocol") == PROTOCOL, f"{label}: wrong protocol")
    require(value.get("Variant") == variant, f"{label}: wrong variant")
    require(value.get("InputsSha256") == inputs_hash, f"{label}: input hash mismatch")
    for name, length in (("CoreRevision", 40), ("HarnessRevision", 40), ("CoreDllSha256", 64)):
        check_hash(value.get(name), length, f"{label}/{name}")
    require(value["CoreRevision"].lower() == manifest[variant]["CoreRevision"].lower(),
            f"{label}: core revision differs from manifest")
    require(value["CoreDllSha256"].lower() == manifest[variant]["CoreDllSha256"].lower(),
            f"{label}: core DLL hash differs from manifest")
    expected_target = ".NETStandard,Version=v2.0" if variant in VARIANTS[:2] else ".NETCoreApp,Version=v10.0"
    require(value.get("CoreTarget") == expected_target, f"{label}: wrong core target")
    require(value.get("ForceScalar") == ("1" if variant == "modern-scalar" else "0"),
            f"{label}: unexpected forced-scalar setting")
    require(value.get("TieredCompilation") == "0", f"{label}: tiered compilation is not disabled")
    require(str(value.get("Runtime", "")).startswith(".NET 10."), f"{label}: expected .NET 10 runtime")
    for name, expected in (("WarmupMilliseconds", 60), ("MinimumCalibrationMilliseconds", 20),
                           ("Batches", 9), ("Cases", 48), ("OpenSamples", 64),
                           ("ClosedSamples", [64, 256]),
                           ("Area", {"DecimalPrecision": 6, "FillRule": "NonZero", "IncludeContours": False}),
                           ("Correspondence", {"AllowScaling": True, "AllowReversal": False, "SearchClosedPhase": True})):
        require(value.get(name) == expected, f"{label}: changed workload metadata {name}")
    finite(value.get("StopwatchFrequency"), f"{label}/StopwatchFrequency", positive=True)
    timestamp = dt.datetime.fromisoformat(value.get("Utc", "").replace("Z", "+00:00"))
    require(timestamp.tzinfo is not None, f"{label}: UTC timestamp lacks an offset")
    if reference is not None:
        for name in COMMON_METADATA:
            require(value.get(name) == reference.get(name), f"{label}: incomparable metadata {name}")
    return value


def cases(rows, probe, label):
    require(isinstance(rows, list) and len(rows) == 48, f"{label}: must contain exactly 48 cases")
    result = {}
    for row in rows:
        key = (row.get("Vertices"), row.get("Name" if probe else "Case"))
        require(key in KEYS and key not in result, f"{label}: unexpected/duplicate case {key}")
        actual = tuple(row.get(name) for name in ("Family", "Stage", "Primary", "AlignmentSamples"))
        require(actual == SPEC[key[1]], f"{label}/{key}: changed case definition")
        finite(row.get("Value"), f"{label}/{key}/Value")
        if not probe:
            samples = row.get("Samples")
            require(isinstance(samples, list) and len(samples) == 9, f"{label}/{key}: expected nine batches")
            iterations = []
            for sample in samples:
                count = sample.get("Iterations")
                require(isinstance(count, int) and not isinstance(count, bool) and
                        0 < count <= 1_048_576 and count & (count - 1) == 0,
                        f"{label}/{key}: invalid calibrated iteration count")
                iterations.append(count)
                finite(sample.get("NanosecondsPerOperation"), f"{label}/{key}/batch time", positive=True)
                require(finite(sample.get("BytesPerOperation"), f"{label}/{key}/batch bytes") >= 0,
                        f"{label}/{key}: negative allocation")
            require(len(set(iterations)) == 1, f"{label}/{key}: changed iterations within process case")
            for recorded, measured in (("MedianNanoseconds", "NanosecondsPerOperation"),
                                       ("MedianBytes", "BytesPerOperation")):
                expected = statistics.median(sample[measured] for sample in samples)
                require(row.get(recorded) == expected, f"{label}/{key}: incorrect stored {recorded}")
        result[key] = row
    require(set(result) == set(KEYS), f"{label}: missing cases")
    return result


def summarize(values):
    # Zero/zero allocation ratios remain undefined, not an invented percentage.
    if any(value is None for value in values):
        return {"Median": None, "Minimum": None, "Maximum": None}
    return {"Median": statistics.median(values), "Minimum": min(values), "Maximum": max(values)}


def ratio_rows(runs, count, candidate, denominator):
    result = []
    for n, name in KEYS:
        paired = []
        for number in range(1, count + 1):
            current, reference = runs[candidate, number][n, name], runs[denominator, number][n, name]
            bytes_ratio = current["MedianBytes"] / reference["MedianBytes"] if reference["MedianBytes"] else None
            paired.append({"Round": number,
                           "TimeRatio": current["MedianNanoseconds"] / reference["MedianNanoseconds"],
                           "BytesRatio": bytes_ratio,
                           "CandidateNanoseconds": current["MedianNanoseconds"],
                           "ReferenceNanoseconds": reference["MedianNanoseconds"],
                           "CandidateBytes": current["MedianBytes"], "ReferenceBytes": reference["MedianBytes"],
                           "ZeroReferenceBytes": reference["MedianBytes"] == 0,
                           "IntroducedAllocation": reference["MedianBytes"] == 0 and current["MedianBytes"] > 0})
        sets = []
        for start in range(0, count, 3):
            group = paired[start:start + 3]
            sets.append({"Set": start // 3 + 1, "Rounds": [p["Round"] for p in group],
                         "Time": summarize([p["TimeRatio"] for p in group]),
                         "Bytes": summarize([p["BytesRatio"] for p in group])})
        result.append({"Vertices": n, "Case": name, "Primary": SPEC[name][2],
                       "PrimaryGate": SPEC[name][2] and n in (256, 1024),
                       "Pairs": paired, "Sets": sets,
                       "Time": summarize([p["TimeRatio"] for p in paired]),
                       "Bytes": summarize([p["BytesRatio"] for p in paired])})
    return result


def primary_pass(time_ratio, bytes_ratio):
    return time_ratio <= .8 or (bytes_ratio is not None and bytes_ratio <= .7)


def decide(rows, count):
    primary, controls = [], []
    for row in rows:
        initial = row["Pairs"][:3]
        if row["PrimaryGate"]:
            passes = [primary_pass(p["TimeRatio"], p["BytesRatio"]) for p in row["Pairs"]]
            primary.append({"Vertices": row["Vertices"], "Case": row["Case"],
                            "MedianGatePass": primary_pass(row["Time"]["Median"], row["Bytes"]["Median"]),
                            "RoundGatePasses": passes,
                            "InitialStraddle": any(passes[:3]) and not all(passes[:3]),
                            "AnyStraddle": any(passes) and not all(passes),
                            "SetMedianPasses": [primary_pass(s["Time"]["Median"], s["Bytes"]["Median"])
                                                for s in row["Sets"]]})
        elif not row["Primary"]:
            first_median = row["Sets"][0]["Time"]["Median"]
            initial_confirmed = all(p["TimeRatio"] > 1.05 for p in initial)
            initial_uncertain = first_median > 1.05 and not initial_confirmed
            second_median = row["Sets"][1]["Time"]["Median"] if count == 6 else None
            controls.append({"Vertices": row["Vertices"], "Case": row["Case"],
                             "InitialConfirmedRegression": initial_confirmed,
                             "InitialUncertainRegression": initial_uncertain,
                             "ConfirmedAcrossSets": count == 6 and first_median > 1.05 and second_median > 1.05,
                             "NewConfirmationConcern": count == 6 and first_median <= 1.05 and second_median > 1.05,
                             "ClearedInitialConcern": count == 6 and initial_uncertain and second_median <= 1.05,
                             "SetMedianTimeRatios": [s["Time"]["Median"] for s in row["Sets"]]})
    confirmed = [r for r in controls if r["InitialConfirmedRegression"] or r["ConfirmedAcrossSets"]]
    unresolved = [r for r in controls if r["InitialUncertainRegression"]] if count == 3 else [r for r in controls if r["NewConfirmationConcern"]]
    primary_straddles = [r for r in primary if r["InitialStraddle"]]
    primary_failures = [r for r in primary if not r["MedianGatePass"]]
    stable_initial_failures = [r for r in primary_failures if not r["InitialStraddle"]]
    reasons = []
    if confirmed:
        status = "FAIL_CONFIRMED_CONTROL_REGRESSION"
        reasons.append("A control regression is confirmed; an initial all-three failure is terminal.")
    elif count == 3 and stable_initial_failures:
        status = "FAIL_PRIMARY_THRESHOLD"
        reasons.append("At least one primary gate fails consistently in the initial set.")
    elif count == 3 and (unresolved or primary_straddles):
        status = "NEEDS_ONE_COMPLETE_CONFIRMATION_SET"
        reasons.append("The initial set has a primary OR-gate straddle or an uncertain control regression.")
    elif primary_failures:
        status = "FAIL_PRIMARY_THRESHOLD"
        reasons.append("At least one combined median primary gate fails.")
    elif unresolved:
        status = "UNRESOLVED_NO_ACCEPTANCE_CLAIM"
        reasons.append("A new control concern appears in confirmation; no additional rounds are allowed.")
    else:
        status = "PASS_PERFORMANCE_GATES_ONLY"
        reasons.append("All four primary gates pass and no control blocker remains.")
    return {"Status": status, "Reasons": reasons, "PrimaryGates": primary,
            "Controls": controls, "ConfirmedControlFailures": confirmed,
            "UnresolvedControls": unresolved,
            "PrimaryStraddlesRemainVisible": [r for r in primary if r["AnyStraddle"]],
            "CorrectnessAndDisassemblyStillRequired": True}


def validate_inputs(path):
    data = read_json(path)
    require(data.get("Protocol") == PROTOCOL, "inputs.json: wrong protocol")
    fixtures = data.get("Fixtures", [])
    require(len(fixtures) == 4 and {f.get("Vertices") for f in fixtures} == set(VERTICES),
            "inputs.json: incomplete vertex sizes")
    for fixture in fixtures:
        for name in ("OpenReference", "OpenMoving", "OpenPerturbed", "ClosedReference", "ClosedMoving", "GraphReference", "GraphMoving"):
            points = fixture.get(name, [])
            require(len(points) == fixture["Vertices"], f"inputs.json: wrong {name} length")
            for point in points:
                finite(point.get("X"), f"inputs.json/{name}/X")
                finite(point.get("Y"), f"inputs.json/{name}/Y")
    return sha256(path)


def analyze(directory, manifest_path, repo_root, binary_mode="auto"):
    manifest_document = read_json(manifest_path)
    entries = manifest_document.get("Variants", [])
    require(len(entries) == 4 and {r.get("Name") for r in entries} == set(VARIANTS), "Manifest requires exactly four variants")
    manifest = {row["Name"]: row for row in entries}
    for variant, entry in manifest.items():
        check_hash(entry.get("CoreRevision"), 40, f"Manifest/{variant}/CoreRevision")
        check_hash(entry.get("CoreDllSha256"), 64, f"Manifest/{variant}/CoreDllSha256")
        require(entry.get("ForceScalar") is (variant == "modern-scalar"),
                f"Manifest/{variant}: unexpected forced-scalar setting")
    require(manifest["baseline"]["CoreRevision"] == BASELINE, "Wrong baseline revision")
    require(manifest["modern-scalar"]["CoreRevision"] == manifest["simd"]["CoreRevision"] and
            manifest["modern-scalar"]["CoreDllSha256"].lower() == manifest["simd"]["CoreDllSha256"].lower(),
            "Modern scalar and SIMD must use identical core source and DLL")
    check_hash(manifest_document.get("HarnessRevision"), 40, "Manifest/HarnessRevision")
    check_hash(manifest_document.get("HarnessDllSha256"), 64, "Manifest/HarnessDllSha256")
    verified_binaries, unchecked_binaries = [], []
    for variant in VARIANTS:
        directory_name = manifest[variant].get("Directory")
        for file, expected in (("PolylineKit.dll", manifest[variant]["CoreDllSha256"]),
                               ("PolylineKit.Experiments.dll", manifest_document["HarnessDllSha256"])):
            if binary_mode == "evidence-only":
                unchecked_binaries.append({"Variant": variant, "File": file, "Reason": "Evidence-only mode"})
                continue
            if directory_name is None:
                require(binary_mode != "required", f"Manifest/{variant}: binary Directory is required for --verify-binaries")
                unchecked_binaries.append({"Variant": variant, "File": file, "Reason": "No binary directory in manifest"})
                continue
            require(isinstance(directory_name, str), f"Manifest/{variant}: invalid binary directory")
            relative = Path(directory_name.replace("\\", "/"))
            require(not relative.is_absolute() and ".." not in relative.parts and
                    re.match(r"^[A-Za-z]:", directory_name) is None,
                    f"Manifest/{variant}: binary directory must be relative to the repository")
            path = repo_root / relative / file
            if not path.is_file():
                require(binary_mode != "required", f"{variant}: required {file} is absent")
                unchecked_binaries.append({"Variant": variant, "File": file, "Reason": "Local binary absent"})
                continue
            require(sha256(path) == expected.lower(), f"{variant}: {file} hash differs from manifest")
            verified_binaries.append({"Variant": variant, "File": file, "Sha256": expected.lower()})
    inputs_hash = validate_inputs(directory / "inputs.json")
    probes, metadatas, common = {}, {}, None
    for variant in VARIANTS:
        path = directory / f"{variant}-probe.json"
        data = read_json(path)
        meta = metadata(data, variant, common, manifest, inputs_hash, path.name)
        require(meta["HarnessRevision"] == manifest_document["HarnessRevision"], f"{path.name}: wrong harness revision")
        common = common or meta
        metadatas[variant] = meta
        probes[variant] = cases(data.get("Values"), True, path.name)
    for variant in VARIANTS:
        for key in KEYS:
            require(bits(probes[variant][key]["Value"]) == bits(probes["baseline"][key]["Value"]),
                    f"Preflight observation differs: {variant}/{key}; this is not full output equivalence")
    files = {}
    for path in directory.glob("*-run-*.json"):
        match = re.fullmatch(r"(baseline|cleanup|modern-scalar|simd)-run-([1-6])\.json", path.name)
        require(match is not None, f"Unexpected measurement filename: {path.name}")
        files[match[1], int(match[2])] = path
    numbers = {number for _, number in files}
    require(numbers in (set(range(1, 4)), set(range(1, 7))), "Need exactly three or six complete rounds; partial data is not analyzed")
    count = len(numbers)
    expected = {(variant, number) for variant in VARIANTS for number in numbers}
    require(set(files) == expected, f"Incomplete variant/round matrix: missing {sorted(expected - set(files))}")
    runs, timestamps = {}, []
    for (variant, number), path in sorted(files.items()):
        data = read_json(path)
        require(data.get("Run") == number, f"{path.name}: run number mismatch")
        meta = metadata(data, variant, common, manifest, inputs_hash, path.name)
        require({k: v for k, v in meta.items() if k != "Utc"} ==
                {k: v for k, v in metadatas[variant].items() if k != "Utc"}, f"{path.name}: metadata changed since preflight")
        measured = cases(data.get("Measurements"), False, path.name)
        for key in KEYS:
            require(bits(measured[key]["Value"]) == bits(probes[variant][key]["Value"]), f"{path.name}/{key}: value changed since preflight")
        runs[variant, number] = measured
        timestamps.append({"Variant": variant, "Round": number, "Utc": meta["Utc"]})
    comparisons = []
    for candidate, denominator in PAIRS:
        rows = ratio_rows(runs, count, candidate, denominator)
        comparisons.append({"Candidate": candidate, "Reference": denominator, "Cases": rows,
                            "Decision": decide(rows, count) if denominator == "baseline" else None})
    return {"Protocol": PROTOCOL, "Rounds": count, "CasesPerProcess": 48,
            "InputsSha256": inputs_hash, "HarnessRevision": manifest_document["HarnessRevision"],
            "HarnessDllSha256": manifest_document["HarnessDllSha256"].lower(),
            "RecordedCoreHashesMatchManifest": True, "BinaryVerificationMode": binary_mode,
            "BinaryFilesVerified": len(verified_binaries) == 8, "BinaryHashes": verified_binaries,
            "BinaryFilesNotChecked": unchecked_binaries,
            "PreflightValuesBitIdenticalToBaseline": {variant: 48 for variant in VARIANTS},
            "MetadataByVariant": metadatas,
            "ProcessCompletionOrder": sorted(timestamps, key=lambda row: dt.datetime.fromisoformat(row["Utc"])),
            "Comparisons": comparisons,
            "Interpretation": [
                "Ratios divide paired process medians. Batch observations are not pooled as independent runs.",
                "A primary gate is time<=0.8 OR bytes<=0.7; stable byte savings remove a time-only straddle.",
                "Only four primary gates (two families, n=256/1024) and forty explicitly labeled controls gate acceptance.",
                "The n=16/64 primary workflows are visible but are not silently relabeled as controls.",
                "An initial all-three control failure remains terminal after confirmation.",
                "For six rounds, combined primary medians are final; all straddles/ranges remain visible as limitations.",
                "Both control set medians above1.05 confirm regression; a new concern only in set2 is unresolved.",
                "A first-set uncertain control concern is cleared when set2 median<=1.05; variability remains visible.",
                "Null allocation ratios mean zero reference bytes; no 0/0 percentage is invented.",
                "Performance passes remain conditional on semantic tests, fallback validation, CI and packed-arithmetic disassembly.",
                "Identical scalar preflight observations do not prove equivalence of complete returned transforms or contours.",
                "Recorded source/DLL hashes identify claimed artifacts; local DLL verification is a separate, explicitly reported check.",
                "Metadata timestamps record process completion but do not independently prove fresh processes or absence of competing workloads.",
                "Input hashes cover raw file bytes, including newlines; no line-ending normalization is silently accepted.",
                "The timing ranges are descriptive, not confidence intervals; three or six processes do not establish universal speedup.",
            ]}


def format_ratio(value):
    return "undefined" if value is None else f"{value:.6f}"


def markdown(report):
    lines = ["# Optimization paired-process analysis", "",
             f"Validated {report['Rounds']} rounds × 4 variants × 48 cases. Each process case has nine batches.", "",
             f"Inputs SHA-256: `{report['InputsSha256']}`. Recorded core hashes match manifest: `{report['RecordedCoreHashesMatchManifest']}`.", "",
             f"Actual DLL verification: `{report['BinaryVerificationMode']}`; all eight files verified: `{report['BinaryFilesVerified']}`; "
             f"checked {len(report['BinaryHashes'])}, omitted {len(report['BinaryFilesNotChecked'])}.", "",
             "All 48 preflight scalar observations match the baseline bit for bit in every variant.", "",
             "## Performance decisions", ""]
    for comparison in report["Comparisons"]:
        decision = comparison["Decision"]
        if decision is None:
            continue
        lines += [f"- **{comparison['Candidate']}: {decision['Status']}**. {' '.join(decision['Reasons'])}"]
        for row in decision["ConfirmedControlFailures"]:
            lines.append(f"  - Confirmed control: {row['Vertices']}/{row['Case']}; set medians {row['SetMedianTimeRatios']}.")
        for row in decision["UnresolvedControls"]:
            lines.append(f"  - Unresolved control: {row['Vertices']}/{row['Case']}; set medians {row['SetMedianTimeRatios']}.")
        for row in decision["PrimaryStraddlesRemainVisible"]:
            lines.append(f"  - Primary OR-gate straddle: {row['Vertices']}/{row['Case']}; round passes {row['RoundGatePasses']}.")
    lines += ["", "## All paired ratios", "", "Each list follows round order. Lower is better. Set medians remain separate in JSON.", ""]
    for comparison in report["Comparisons"]:
        lines += [f"### {comparison['Candidate']} / {comparison['Reference']}", "",
                  "| Vertices | Case | Role | Time median | Time range | Round time ratios | Bytes median | Round byte ratios |",
                  "| --- | --- | --- | ---: | --- | --- | ---: | --- |"]
        for row in comparison["Cases"]:
            role = "Primary gate" if row["PrimaryGate"] else "Primary, visible" if row["Primary"] else "Control"
            time = row["Time"]
            times = ", ".join(format_ratio(p["TimeRatio"]) for p in row["Pairs"])
            allocations = ", ".join(format_ratio(p["BytesRatio"]) for p in row["Pairs"])
            lines.append(f"| {row['Vertices']} | {row['Case']} | {role} | {format_ratio(time['Median'])} | "
                         f"{format_ratio(time['Minimum'])}–{format_ratio(time['Maximum'])} | {times} | "
                         f"{format_ratio(row['Bytes']['Median'])} | {allocations} |")
        lines.append("")
    lines += ["## Interpretation and limits", ""] + [f"- {item}" for item in report["Interpretation"]]
    return "\n".join(lines) + "\n"


def main():
    default_root = next((p for p in Path(__file__).resolve().parents if (p / "PolylineKit.slnx").exists()), Path.cwd())
    public_results = default_root / "results/optimization/final"
    default_results = public_results if public_results.exists() else default_root / "artifacts/optimization/measured"
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", nargs="?", type=Path, default=default_results)
    parser.add_argument("--repo-root", type=Path, default=default_root)
    parser.add_argument("--manifest", type=Path)
    binary_options = parser.add_mutually_exclusive_group()
    binary_options.add_argument("--evidence-only", "--skip-binaries", dest="binary_mode", action="store_const", const="evidence-only",
                                help="Validate archived metadata and recorded hashes without opening local DLLs")
    binary_options.add_argument("--verify-binaries", dest="binary_mode", action="store_const", const="required",
                                help="Require and hash all eight local DLL files; default auto verifies only files present")
    parser.set_defaults(binary_mode="auto")
    parser.add_argument("--output", type=Path, help="JSON report; default DIRECTORY/analysis.json")
    parser.add_argument("--markdown", type=Path, help="Markdown report; default DIRECTORY/analysis.md")
    args = parser.parse_args()
    try:
        local_manifest = args.directory / "variants.json"
        manifest_path = args.manifest or (local_manifest if local_manifest.exists() else args.directory.parent / "variants.json")
        report = analyze(args.directory, manifest_path, args.repo_root, args.binary_mode)
        output = args.output or args.directory / "analysis.json"
        markdown_path = args.markdown or args.directory / "analysis.md"
        output.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        markdown_path.write_text(markdown(report), encoding="utf-8")
        print(f"Validated {report['Rounds']} rounds, 4 variants, 48 cases/process and 48 bit-identical preflight values/variant.")
        for comparison in report["Comparisons"]:
            if comparison["Decision"] is not None:
                print(f"{comparison['Candidate']}: {comparison['Decision']['Status']}")
        print(f"JSON: {output}\nMarkdown: {markdown_path}")
        return 0
    except (AuditError, OSError, ValueError, TypeError, KeyError) as error:
        print(f"VALIDATION FAILED: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
