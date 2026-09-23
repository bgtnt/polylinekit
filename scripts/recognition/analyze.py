#!/usr/bin/env python3
"""Audit frozen recognition predictions. No classification, fitting or parameter selection."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import gzip
import hashlib
import json
import math
from pathlib import Path
import platform
import random
import statistics
import struct
import sys

SEEDS = (1729, 2718, 31415)
METHODS = {"dollar": ("rms", "area", "combined", "protractor"),
           "pendigits": ("rms", "area", "combined", "protractor", "dtw")}
SCORE_FIELDS = ("score", "rms", "area", "margin")
VARIANTS = ("uncached-scalar", "cached-scalar", "cached-simd")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def invalid_constant(value: str):
    raise ValueError(f"Nonfinite JSON constant: {value}")


def read_jsonl(path: Path):
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", encoding="utf-8") as stream:
        for index, line in enumerate(stream, 1):
            require(bool(line.strip()), f"Blank JSONL record: {path}:{index}")
            row = json.loads(line, parse_constant=invalid_constant)
            require(isinstance(row, dict), f"Expected JSON object: {path}:{index}")
            yield row


def read_json(path: Path):
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", encoding="utf-8") as stream:
        return json.load(stream, parse_constant=invalid_constant)


def row_key(row: dict) -> tuple:
    return row["dataset"], row["seed"], row["sampleId"], row["method"]


def query_key(row: dict) -> tuple:
    return row["dataset"], row["seed"], row["sampleId"]


def finite_number(value) -> bool:
    return isinstance(value, (float, int)) and not isinstance(value, bool) and math.isfinite(value)


def validate_finite_values(value) -> None:
    if isinstance(value, float):
        require(math.isfinite(value), "Nonfinite prediction value")
    elif isinstance(value, dict):
        for item in value.values():
            validate_finite_values(item)
    elif isinstance(value, list):
        for item in value:
            validate_finite_values(item)


def validate_prediction(row: dict) -> None:
    validate_finite_values(row)
    require(row.get("dataset") in METHODS, "Unknown prediction dataset")
    require(isinstance(row.get("seed"), int) and not isinstance(row["seed"], bool), "Invalid seed")
    for field in ("sampleId", "trueLabel", "method", "status"):
        require(isinstance(row.get(field), str) and bool(row[field]), f"Missing prediction {field}")
    require(row["method"] in METHODS[row["dataset"]], "Unknown prediction method")
    require(row["status"] in {"ok", "failed", "unsupported"}, "Unknown prediction status")
    for field in SCORE_FIELDS:
        value = row.get(field)
        require(value is None or finite_number(value), f"Nonfinite/non-numeric {field}: {row_key(row)}")
    if row["status"] == "ok":
        require(all(row.get(field) is not None for field in SCORE_FIELDS), f"Missing successful-row score: {row_key(row)}")
        require(isinstance(row.get("predictedLabel"), str) and bool(row["predictedLabel"]) and
                isinstance(row.get("templateId"), str) and bool(row["templateId"]), "Missing successful prediction/template")
        require(row["margin"] >= 0, f"Negative top distinct-class margin: {row_key(row)}")
        require(isinstance(row.get("rotationDegrees"), int) and not isinstance(row["rotationDegrees"], bool),
                "Missing shared-preparation rotation")
    else:
        require(row.get("predictedLabel") is None and row.get("templateId") is None,
                f"Non-success row contains a prediction: {row_key(row)}")


def prediction_files(location: Path) -> list[Path]:
    files = [location] if location.is_file() else sorted(location.glob("*-predictions.jsonl.gz"))
    require(bool(files), f"No prediction files at {location}")
    return files


def load_predictions(location: Path) -> tuple[dict, list[dict]]:
    rows = {}
    sources = []
    for path in prediction_files(location):
        sources.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        for row in read_jsonl(path):
            validate_prediction(row)
            key = row_key(row)
            require(key not in rows, f"Duplicate prediction key: {key}")
            rows[key] = row
    require(bool(rows), "No prediction records")
    return rows, sources


def load_data(directory: Path, datasets: set[str]) -> tuple[dict, list[dict]]:
    rows = {}
    sources = []
    for dataset in sorted(datasets):
        path = directory / (dataset + ".jsonl")
        sources.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        for row in read_jsonl(path):
            identifier = row.get("sampleId")
            require(isinstance(identifier, str) and identifier not in rows and row.get("dataset") == dataset,
                    f"Duplicate/invalid input identity: {identifier}")
            require(isinstance(row.get("supported"), bool), f"Missing support flag: {identifier}")
            # Coordinates are not needed by this analysis and never enter its outputs.
            rows[identifier] = {key: value for key, value in row.items() if key != "strokes"}
    return rows, sources


def validate_coverage(rows: dict, data: dict, seeds: tuple[int, ...] = SEEDS) -> None:
    datasets = {row["dataset"] for row in rows.values()}
    for dataset in datasets:
        split = "main" if dataset == "dollar" else "test"
        expected_ids = {identifier for identifier, sample in data.items() if sample["dataset"] == dataset and sample["split"] == split}
        require(bool(expected_ids), f"No held-out input records for {dataset}")
        actual = {key for key in rows if key[0] == dataset}
        expected = {(dataset, seed, identifier, method) for identifier in expected_ids for seed in seeds for method in METHODS[dataset]}
        require(actual == expected, f"Incomplete/unexpected evaluation grid for {dataset}: missing={len(expected-actual)}, extra={len(actual-expected)}")
    for row in rows.values():
        sample = data[row["sampleId"]]
        require(row["trueLabel"] == sample["label"] and row["dataset"] == sample["dataset"],
                f"Prediction/input label or dataset mismatch: {row_key(row)}")
        require(row.get("writerId") == sample.get("writerId"), f"Writer provenance mismatch: {row_key(row)}")
        require((row["status"] == "unsupported") == (not sample["supported"]),
                f"Support status contradicts input: {row_key(row)}")
        if row["status"] == "ok":
            template = data.get(row["templateId"])
            require(template is not None and template["dataset"] == row["dataset"] and template["supported"],
                    f"Unknown/unsupported winning template: {row_key(row)}")
            require(row["predictedLabel"] == template["label"], f"Winning label differs from template: {row_key(row)}")
            require(template["sampleId"] != row["sampleId"], f"Query is its own winning template: {row_key(row)}")
            if row["dataset"] == "pendigits":
                require(template["split"] == "train", "Digit winner is not an official training sample")
            else:
                require(template["split"] == "main" and template.get("writerId") != sample.get("writerId"),
                        "Dollar winning template leaks held-out writer")


def correct(row: dict) -> bool:
    return row["status"] == "ok" and row["predictedLabel"] == row["trueLabel"]


def metrics(rows: list[dict], data: dict) -> dict:
    statuses = Counter(row["status"] for row in rows)
    n = len(rows)
    supported = sum(data[row["sampleId"]]["supported"] for row in rows)
    right = sum(correct(row) for row in rows)
    confusion = defaultdict(Counter)
    for row in rows:
        outcome = row["predictedLabel"] if row["status"] == "ok" else "[" + row["status"] + "]"
        confusion[row["trueLabel"]][outcome] += 1
    return {
        "querySeedTrials": n, "uniqueQueries": len({row["sampleId"] for row in rows}),
        "supportedTrials": supported, "unsupportedTrials": statuses["unsupported"], "failedTrials": statuses["failed"],
        "successfulTrials": statuses["ok"], "correctTrials": right,
        "supportedAccuracy": right / supported if supported else None,
        "allTestAccuracy": right / n if n else None,
        "coverage": supported / n if n else None,
        "confusion": {label: dict(sorted(counts.items())) for label, counts in sorted(confusion.items())},
    }


def grouped_metrics(rows: list[dict], data: dict, field: str) -> dict:
    groups = defaultdict(list)
    for row in rows:
        value = row["seed"] if field == "seed" else data[row["sampleId"]].get(field)
        if value is not None:
            groups[str(value)].append(row)
    return {key: metrics(value, data) for key, value in sorted(groups.items())}


def paired_counts(baseline: list[dict], candidate: list[dict], data: dict, supported_only: bool = False) -> dict:
    left, right = {query_key(r): r for r in baseline}, {query_key(r): r for r in candidate}
    require(left.keys() == right.keys(), "Paired methods have different query/seed coverage")
    counts = Counter()
    for key in sorted(left):
        if supported_only and not data[left[key]["sampleId"]]["supported"]:
            continue
        a, b = correct(left[key]), correct(right[key])
        counts["bothCorrect" if a and b else "baselineOnlyCorrect" if a else "candidateOnlyCorrect" if b else "bothWrong"] += 1
    total = sum(counts.values())
    return {"querySeedTrials": total, **{key: counts[key] for key in
            ("bothCorrect", "baselineOnlyCorrect", "candidateOnlyCorrect", "bothWrong")},
            "unsupportedPairTrials": 0 if supported_only else sum(not data[left[key]["sampleId"]]["supported"] for key in left),
            "accuracyDifference": (counts["candidateOnlyCorrect"] - counts["baselineOnlyCorrect"]) / total if total else None,
            "interpretation": "candidateOnlyCorrect corrects a baseline error; baselineOnlyCorrect introduces an error; bothWrong means neither correct and includes unsupported trials in all-test counts; failures count as incorrect"}


def percentile(values: list[float], fraction: float) -> float:
    values = sorted(values)
    position = (len(values) - 1) * fraction
    lower = math.floor(position)
    upper = math.ceil(position)
    return values[lower] + (values[upper] - values[lower]) * (position - lower)


def writer_bootstrap(writer_deltas: list[float], replicates: int = 10000, seed: int = 424242) -> dict:
    require(bool(writer_deltas), "No paired writer blocks")
    random_source = random.Random(seed)
    n = len(writer_deltas)
    means = [sum(writer_deltas[random_source.randrange(n)] for _ in range(n)) / n for _ in range(replicates)]
    return {"writerMeanDifference": sum(writer_deltas) / n, "lower95": percentile(means, .025),
            "upper95": percentile(means, .975), "writerBlocks": n, "replicates": replicates, "seed": seed,
            "interval": "paired writer-block percentile bootstrap; linear interpolation at 2.5/97.5 percentiles",
            "unit": "accuracy fraction; multiply by 100 for percentage points",
            "resamplingUnit": "whole writer block, preserving every sample and every template-bank seed together",
            "randomGenerator": "Python random.Random (MT19937), recorded interpreter version"}


def margin_summary(rows: list[dict], limit: int = 20) -> dict:
    valid = [r for r in rows if r["status"] == "ok" and r.get("margin") is not None]
    ordered = sorted(valid, key=lambda r: (r["margin"], row_key(r)))
    positive = [r["margin"] for r in valid if r["margin"] > 0]
    fields = ("dataset", "seed", "sampleId", "method", "predictedLabel", "templateId", "score", "margin")
    return {"exactDistinctClassTies": sum(r["margin"] == 0 for r in valid),
            "smallestPositiveMargin": min(positive) if positive else None,
            "closest": [{key: r[key] for key in fields} for r in ordered[:limit]],
            "meaning": "best different-class score minus winner; native score units; no epsilon near-tie threshold"}


def analyze_rows(rows: dict, data: dict) -> dict:
    validate_coverage(rows, data)
    result = {}
    for dataset in sorted({r["dataset"] for r in rows.values()}):
        selected = [r for r in rows.values() if r["dataset"] == dataset]
        methods = {method: [r for r in selected if r["method"] == method] for method in METHODS[dataset]}
        report = {"methods": {}, "paired": {}}
        for method, method_rows in methods.items():
            report["methods"][method] = {**metrics(method_rows, data),
                "bySeed": grouped_metrics(method_rows, data, "seed"),
                "byClass": grouped_metrics(method_rows, data, "label"),
                "byWriter": grouped_metrics(method_rows, data, "writerId"),
                "bySpeed": grouped_metrics(method_rows, data, "speed"), "margins": margin_summary(method_rows)}
        for candidate in ("area", "combined"):
            report["paired"][candidate + "MinusRms"] = {
                "allTest": paired_counts(methods["rms"], methods[candidate], data),
                "supported": paired_counts(methods["rms"], methods[candidate], data, supported_only=True),
                "bySeed": {str(seed): paired_counts([r for r in methods["rms"] if r["seed"] == seed],
                                                    [r for r in methods[candidate] if r["seed"] == seed], data)
                           for seed in SEEDS}}
        if dataset == "dollar":
            writers = sorted({data[r["sampleId"]]["writerId"] for r in selected})
            require(writers == [f"s{i:02d}" for i in range(2, 12)], "Primary bootstrap requires the ten declared main writers")
            writer_rows = []
            for writer in writers:
                rms = [r for r in methods["rms"] if data[r["sampleId"]]["writerId"] == writer]
                combined = [r for r in methods["combined"] if data[r["sampleId"]]["writerId"] == writer]
                pair = paired_counts(rms, combined, data)
                writer_rows.append({"writerId": writer, "rmsAccuracy": sum(correct(r) for r in rms) / len(rms),
                                    "combinedAccuracy": sum(correct(r) for r in combined) / len(combined), **pair})
            report["primaryCombinedMinusRms"] = {"writerRows": writer_rows,
                **writer_bootstrap([r["accuracyDifference"] for r in writer_rows])}
        else:
            require(not any(data[r["sampleId"]].get("writerId") for r in selected),
                    "Unexpected Pendigits writer provenance; review before changing analysis")
            report["uncertaintyNote"] = "Descriptive per-bank results only; no verified per-record writer IDs, no writer bootstrap or independent-seed confidence interval"
        result[dataset] = report
    return result


def pendigits_replay_cases(rows: dict) -> dict:
    dataset, seed = "pendigits", 1729
    cases = []
    identifiers = sorted({row["sampleId"] for row in rows.values() if row["dataset"] == dataset and row["seed"] == seed})
    for kind, rms_correct, combined_correct in (("combined-correct-rms-wrong", False, True),
                                                ("rms-correct-combined-wrong", True, False)):
        for identifier in identifiers:
            rms, combined = rows.get((dataset, seed, identifier, "rms")), rows.get((dataset, seed, identifier, "combined"))
            if rms and combined and rms["status"] == combined["status"] == "ok" and correct(rms) == rms_correct and correct(combined) == combined_correct:
                cases.append({"kind": kind, "dataset": dataset, "seed": seed, "sampleId": identifier,
                              "trueLabel": rms["trueLabel"], "predictions": [rows[dataset, seed, identifier, method]
                                  for method in METHODS[dataset] if (dataset, seed, identifier, method) in rows]})
                break
    return {"selection": "First ordinal sampleId in Pendigits seed 1729 for each requested correction/regression; both methods successfully classified the sample",
            "purpose": "Post-evaluation illustrative replay cases, not parameter-selection inputs; no raw coordinates", "cases": cases}


def flatten(value, prefix: str = "") -> dict:
    if isinstance(value, dict):
        result = {prefix + "{object}": True}
        for key in sorted(value):
            result.update(flatten(value[key], prefix + "." + key if prefix else key))
        return result
    if isinstance(value, list):
        result = {prefix + ".length": len(value)}
        for index, item in enumerate(value):
            result.update(flatten(item, prefix + f"[{index}]"))
        return result
    return {prefix: value}


def same_value(left, right) -> bool:
    if finite_number(left) and finite_number(right):
        return struct.pack(">d", left) == struct.pack(">d", right)
    return type(left) is type(right) and left == right


def compare_rows(left: dict, right: dict) -> dict:
    left_keys, right_keys = set(left), set(right)
    changes = []
    numeric = defaultdict(lambda: {"compared": 0, "bitwiseEqual": 0, "maxAbsoluteDifference": 0.0})
    changed_predictions = changed_statuses = 0
    for key in sorted(left_keys & right_keys):
        a, b = flatten(left[key]), flatten(right[key])
        fields = []
        for field in sorted(set(a) | set(b)):
            present_a, present_b = field in a, field in b
            equal = present_a and present_b and same_value(a[field], b[field])
            if present_a and present_b and finite_number(a[field]) and finite_number(b[field]):
                state = numeric[field]
                state["compared"] += 1
                state["bitwiseEqual"] += equal
                difference = abs(a[field] - b[field])
                require(math.isfinite(difference), "Finite scores produced an unrepresentable difference")
                state["maxAbsoluteDifference"] = max(state["maxAbsoluteDifference"], difference)
            if not equal:
                fields.append({"field": field, "leftPresent": present_a, "rightPresent": present_b,
                               "left": a.get(field), "right": b.get(field)})
        if fields:
            changes.append({"key": list(key), "differences": fields})
            changed_predictions += left[key].get("predictedLabel") != right[key].get("predictedLabel")
            changed_statuses += left[key]["status"] != right[key]["status"]
    margins = {}
    for dataset, method in sorted({(r["dataset"], r["method"]) for r in list(left.values()) + list(right.values())}):
        margins[dataset + "/" + method] = {
            "left": margin_summary([r for r in left.values() if r["dataset"] == dataset and r["method"] == method]),
            "right": margin_summary([r for r in right.values() if r["dataset"] == dataset and r["method"] == method])}
    return {"exact": not changes and left_keys == right_keys, "leftRows": len(left), "rightRows": len(right),
            "matchedRows": len(left_keys & right_keys), "missingFromRight": [list(k) for k in sorted(left_keys - right_keys)],
            "missingFromLeft": [list(k) for k in sorted(right_keys - left_keys)],
            "changedRows": len(changes), "changedPredictions": changed_predictions, "changedStatuses": changed_statuses,
            "numericFields": dict(sorted(numeric.items())), "differences": changes, "marginsByMethod": margins,
            "comparison": "full keyed rows, exact strings/structure and binary64 numeric bits; signed zero distinguished; nonfinite scores rejected"}


def percent(value: float | None) -> str:
    return "n/a" if value is None else f"{100 * value:.3f}%"


def analysis_markdown(report: dict) -> str:
    lines = ["# Frozen recognition quality", "", "Counts are query × template-bank-seed trials. Failures stay in accuracy denominators.",
             "Unsupported records are excluded only from supported accuracy, and remain in all-test accuracy.", ""]
    for dataset, item in report["datasets"].items():
        lines += [f"## {dataset}", "", "| Method | Supported accuracy | All-test accuracy | Correct / all trials | Failed | Unsupported |",
                  "|---|---:|---:|---:|---:|---:|"]
        for method, values in item["methods"].items():
            lines.append(f"| {method} | {percent(values['supportedAccuracy'])} | {percent(values['allTestAccuracy'])} | {values['correctTrials']}/{values['querySeedTrials']} | {values['failedTrials']} | {values['unsupportedTrials']} |")
        lines += ["", "| Candidate versus RMS | Corrected errors | Introduced errors | Neither correct (all) | Unsupported pairs | Supported change | All-test change |",
                  "|---|---:|---:|---:|---:|---:|---:|"]
        for candidate, pair in item["paired"].items():
            p = pair["allTest"]
            lines.append(f"| {candidate} | {p['candidateOnlyCorrect']} | {p['baselineOnlyCorrect']} | {p['bothWrong']} | {p['unsupportedPairTrials']} | {100*pair['supported']['accuracyDifference']:+.3f} pp ({pair['supported']['querySeedTrials']} trials) | {100*p['accuracyDifference']:+.3f} pp ({p['querySeedTrials']} trials) |")
        lines += ["", "‘Neither correct’ includes unsupported pairs and failed queries; it is not a count of classified errors alone."]
        if dataset == "dollar":
            primary = item["primaryCombinedMinusRms"]
            lines += ["", f"Primary combined−RMS writer-mean difference: **{100*primary['writerMeanDifference']:+.3f} pp**; paired 95% writer-block bootstrap interval **[{100*primary['lower95']:+.3f}, {100*primary['upper95']:+.3f}] pp**.",
                      "Ten writers; 10,000 resamples with seed 424242. Repeated banks remain inside each writer block.", "",
                      "| Writer | RMS | Combined | Difference |", "|---|---:|---:|---:|"]
            for writer in primary["writerRows"]:
                lines.append(f"| {writer['writerId']} | {percent(writer['rmsAccuracy'])} | {percent(writer['combinedAccuracy'])} | {100*writer['accuracyDifference']:+.3f} pp |")
        else:
            lines += ["", item["uncertaintyNote"], "", "| Method | Seed | Supported accuracy | All-test accuracy |", "|---|---:|---:|---:|"]
            for method, values in item["methods"].items():
                for seed, seed_values in values["bySeed"].items():
                    lines.append(f"| {method} | {seed} | {percent(seed_values['supportedAccuracy'])} | {percent(seed_values['allTestAccuracy'])} |")
        lines += ["", "Class/writer/speed confusion counts, exact ties and closest score margins are in the accompanying JSON.", ""]
    return "\n".join(lines) + "\n"


def compare_markdown(report: dict) -> str:
    result = report["comparison"]
    lines = ["# Scalar/accelerated prediction audit", "", f"Exact full-row agreement: **{result['exact']}**.",
             f"Matched rows: {result['matchedRows']}; changed rows: {result['changedRows']}; changed predictions: {result['changedPredictions']}; changed statuses: {result['changedStatuses']}.",
             f"Missing from right: {len(result['missingFromRight'])}; missing from left: {len(result['missingFromLeft'])}.", "",
             "| Numeric field | Comparisons | Bitwise equal | Maximum absolute difference |", "|---|---:|---:|---:|"]
    for field, values in result["numericFields"].items():
        lines.append(f"| {field} | {values['compared']} | {values['bitwiseEqual']} | {values['maxAbsoluteDifference']:.17g} |")
    lines += ["", "No epsilon clamping is used. JSON contains every differing row and per-method ties/margins. This checks exported winning-score diagnostics, not unexported pair scores.", ""]
    return "\n".join(lines)


def range_summary(values: list[float]) -> dict:
    require(bool(values) and all(finite_number(value) for value in values), "Missing/nonfinite performance values")
    return {"median": statistics.median(values), "minimum": min(values), "maximum": max(values)}


def performance_rows(runs: list[dict], quality: dict, quality_metadata: dict) -> dict:
    expected_runs = {(variant, run) for variant in VARIANTS for run in (1, 2, 3)}
    actual_runs = [(run["variant"], run["run"]) for run in runs]
    require(len(actual_runs) == len(set(actual_runs)) and set(actual_runs) == expected_runs,
            "Performance requires exactly three variants and three independent runs")
    expected_cases = {(dataset, seed, method) for dataset in METHODS for seed in SEEDS for method in METHODS[dataset]}
    reference = runs[0]
    metadata_fields = ("protocol", "implementationId", "sourceRevision", "freezeSourceRevision", "freezeSha256",
                       "inputHashes", "runtime", "os", "architecture", "cpu", "processors", "coreTarget",
                       "coreDllSha256", "engineDllSha256", "serverGc", "gcLatencyMode", "stopwatchFrequency",
                       "tieredCompilation", "tieredPgo", "readyToRun", "enableHardwareIntrinsics", "vector128", "vector256")
    core_fields = ("status", "predictedLabel", "templateId", "score")
    groups = defaultdict(list)
    bank_settings = {}
    selected_quality = {}
    for dataset, seed, method in sorted(expected_cases):
        available = [r for r in quality.values() if (r["dataset"], r["seed"], r["method"]) == (dataset, seed, method)]
        require(bool(available), f"Missing quality reference case: {dataset}/{seed}/{method}")
        writer = min(r["writerId"] for r in available) if dataset == "dollar" else None
        selected = [r for r in available if dataset != "dollar" or r["writerId"] == writer]
        selected_quality[dataset, seed, method] = (writer, selected)
    deviations = []
    scores = {variant: {"compared": 0, "bitwiseEqual": 0, "maxAbsoluteDifference": 0.0} for variant in VARIANTS}
    variant_reference = {}
    variant_changed = []
    variant_comparisons = 0
    variant_scores = {"compared": 0, "bitwiseEqual": 0, "maxAbsoluteDifference": 0.0}
    for run in sorted(runs, key=lambda r: (VARIANTS.index(r["variant"]), r["run"])):
        validate_finite_values(run)
        variant = run["variant"]
        require(run.get("forceScalar") is (variant != "cached-simd"), "Variant/scalar-switch mismatch")
        require(run.get("coreTarget") == ".NETCoreApp,Version=v10.0", "Application timings must load the net10.0 core")
        require(finite_number(run.get("stopwatchFrequency")) and run["stopwatchFrequency"] > 0, "Invalid stopwatch frequency")
        for field in metadata_fields:
            require(run.get(field) == reference.get(field), f"Inconsistent performance metadata: {field}")
        case_keys = [(c["dataset"], c["seed"], c["method"]) for c in run["measurements"]]
        require(len(case_keys) == len(set(case_keys)) and set(case_keys) == expected_cases, "Incomplete/duplicate performance cases")
        for case in run["measurements"]:
            key = case["dataset"], case["seed"], case["method"]
            dataset, seed, method = key
            writer, official = selected_quality[key]
            require(case.get("heldOutWriter") == writer, "Performance selected the wrong held-out writer")
            require(case["variant"] == variant, "Case/file variant mismatch")
            meta = quality_metadata[dataset]
            require(run["freezeSha256"] == meta["freezeSha256"] and run["inputHashes"][dataset] == meta["inputSha256"],
                    "Performance and quality use different frozen inputs")
            require(case["configuration"] == meta["configuration"], "Performance configuration differs from quality")
            supported = {r["sampleId"]: r for r in official if r["status"] != "unsupported"}
            require(case["officialQueryCount"] == len(official) and case["supportedQueryCount"] == len(supported) and
                    case["unsupportedQueryCount"] == len(official) - len(supported), "Performance coverage denominator mismatch")
            measured = {r["sampleId"]: r for r in case["queries"]}
            require(len(measured) == len(case["queries"]) and measured.keys() == supported.keys(),
                    "Performance query identities differ from supported quality bank")
            require(case["templateCount"] == len(case["templateIds"]) == len(set(case["templateIds"])), "Invalid performance template IDs")
            settings = {field: case.get(field) for field in ("templateIds", "configuration", "sharedSamples", "nativeSamples", "warmupSequence")}
            if key in bank_settings:
                require(settings == bank_settings[key], "Frozen bank/preparation/warmup differs across variants or runs")
            else:
                bank_settings[key] = settings
            require(case["failures"] == sum(q["status"] != "ok" for q in measured.values()), "Performance failure count mismatch")
            require(case["warmupQueries"] >= 100 and case["warmupMilliseconds"] >= 3000, "Insufficient declared warmup")
            ticks, allocations = [], []
            for identifier, query in sorted(measured.items()):
                expected = supported[identifier]
                require(query["status"] in ("ok", "failed"), "Unsupported/unknown record entered performance scoring")
                require(isinstance(query["elapsedTicks"], int) and query["elapsedTicks"] >= 0 and
                        isinstance(query["allocatedBytes"], int) and query["allocatedBytes"] >= 0, "Invalid query timing/allocation")
                ticks.append(query["elapsedTicks"])
                allocations.append(query["allocatedBytes"])
                if query["status"] == "ok":
                    require(finite_number(query.get("score")), "Missing/nonfinite performance score")
                    require(query.get("templateId") in case["templateIds"], "Winner outside the declared performance bank")
                else:
                    require(query.get("score") is None and query.get("predictedLabel") is None and query.get("templateId") is None,
                            "Failed performance row contains a winner")
                differing = [field for field in core_fields if not same_value(query.get(field), expected.get(field))]
                if differing:
                    deviations.append({"variant": variant, "run": run["run"], "dataset": dataset, "seed": seed,
                                       "method": method, "sampleId": identifier, "fields": differing,
                                       "performance": {f: query.get(f) for f in core_fields}, "quality": {f: expected.get(f) for f in core_fields}})
                if finite_number(query.get("score")) and finite_number(expected.get("score")):
                    score = scores[variant]
                    score["compared"] += 1
                    score["bitwiseEqual"] += same_value(query["score"], expected["score"])
                    score["maxAbsoluteDifference"] = max(score["maxAbsoluteDifference"], abs(query["score"] - expected["score"]))
                # Compare every later variant/run with uncached scalar run 1, excluding timing fields.
                query_identity = dataset, seed, method, identifier
                comparable = {f: query.get(f) for f in core_fields}
                if query_identity in variant_reference:
                    variant_comparisons += 1
                    previous = variant_reference[query_identity]
                    if finite_number(comparable["score"]) and finite_number(previous["score"]):
                        variant_scores["compared"] += 1
                        variant_scores["bitwiseEqual"] += same_value(comparable["score"], previous["score"])
                        variant_scores["maxAbsoluteDifference"] = max(variant_scores["maxAbsoluteDifference"], abs(comparable["score"] - previous["score"]))
                    if any(not same_value(comparable[f], previous[f]) for f in core_fields):
                        variant_changed.append({"variant": variant, "run": run["run"], "key": list(query_identity),
                                                "baseline": variant_reference[query_identity], "current": comparable})
                else:
                    variant_reference[query_identity] = comparable
            # Recompute reported quantiles from all measured rows; failures remain in both lists.
            micros = sorted(t * 1e6 / run["stopwatchFrequency"] for t in ticks)
            allocated = sorted(allocations)
            for field, values, proportion in (("p50Microseconds", micros, .5), ("p95Microseconds", micros, .95),
                                               ("medianBytesPerQuery", allocated, .5)):
                require(same_value(case[field], values[math.ceil(proportion * len(values)) - 1]), "Reported query quantile mismatch: " + field)
            require(same_value(case["meanBytesPerQuery"], sum(allocated) / len(allocated)), "Reported mean allocation mismatch")
            groups[variant, dataset, method].append({"run": run["run"], "seed": seed, "measurement": case})
    metrics_to_summarize = ("p50Microseconds", "p95Microseconds", "medianBytesPerQuery", "meanBytesPerQuery",
                            "bankConstructionMilliseconds", "bankConstructionAllocatedBytes", "retainedPreparedBankBytes", "totalProcessingMilliseconds")
    summaries = []
    for (variant, dataset, method), cases in sorted(groups.items(), key=lambda x: (VARIANTS.index(x[0][0]), x[0][1], x[0][2])):
        require(len(cases) == 9, "Expected nine seed/run observations per summary")
        item = {"variant": variant, "dataset": dataset, "method": method, "cases": len(cases),
                "heldOutWriters": sorted({c["measurement"]["heldOutWriter"] for c in cases if c["measurement"].get("heldOutWriter")}),
                "supportedQueriesPerCase": range_summary([c["measurement"]["supportedQueryCount"] for c in cases]),
                "failures": sum(c["measurement"]["failures"] for c in cases),
                **{field: range_summary([c["measurement"][field] for c in cases]) for field in metrics_to_summarize},
                "scoringOnlyNanosecondsPerQuery": range_summary([c["measurement"]["scoringOnly"]["nanosecondsPerQuery"] for c in cases]),
                "scoringOnlyBytesPerQuery": range_summary([c["measurement"]["scoringOnly"]["bytesPerQuery"] for c in cases]),
                "measuredGcCollectionCounts": [sum(c["measurement"]["measuredGcCollections"][g] for c in cases) for g in range(3)],
                "observations": [{"run": c["run"], "seed": c["seed"], **{f: c["measurement"][f] for f in metrics_to_summarize}} for c in cases]}
        summaries.append(item)
    return {"runs": len(runs), "cases": sum(len(run["measurements"]) for run in runs), "summaries": summaries,
            "qualityAgreement": {"exact": not deviations, "scoreFieldsByVariant": scores, "deviations": deviations},
            "variantAgreement": {"exact": not variant_changed, "comparedRows": variant_comparisons, "deviations": variant_changed,
                                 "scores": variant_scores,
                                 "reference": "uncached-scalar run 1; status, winning class, template ID, binary64 score"},
            "metadata": {field: reference.get(field) for field in metadata_fields},
            "summaryUnit": "Median and minimum/maximum across nine case statistics (3 frozen seeds x 3 independent process runs); not pooled query percentiles or a confidence interval",
            "scope": "Dollar uses the first ordinal held-out writer, Pendigits all supported test queries. Uncached scalar is attribution only. Retained-bank measurements preserve negative GC noise. GC pause durations were not measured."}


def performance_markdown(report: dict) -> str:
    result = report["performance"]
    lines = ["# Application performance", "", result["summaryUnit"] + ".", result["scope"], "",
             f"Quality winner/status/score agreement: **{result['qualityAgreement']['exact']}**. Cross-variant/run agreement: **{result['variantAgreement']['exact']}**.", "",
             "Each cell is median [minimum, maximum] of nine case observations.", "",
             "| Variant | Dataset | Method | p50 µs | p95 µs | Median B/query | Retained bank B | Total pass ms |",
             "|---|---|---|---:|---:|---:|---:|---:|"]
    def cell(values):
        return f"{values['median']:.3f} [{values['minimum']:.3f}, {values['maximum']:.3f}]"
    for row in result["summaries"]:
        lines.append(f"| {row['variant']} | {row['dataset']} | {row['method']} | " + " | ".join(cell(row[field]) for field in
                     ("p50Microseconds", "p95Microseconds", "medianBytesPerQuery", "retainedPreparedBankBytes", "totalProcessingMilliseconds")) + " |")
    lines += ["", "Construction costs, allocations, GC collection counts, scoring-only diagnostics and individual seed/run observations are in JSON. Construction precedes warmup and can include first-use JIT; retained bytes are incremental live managed memory, not allocations.", ""]
    return "\n".join(lines)


def write_report(output: Path, report: dict, markdown: str) -> None:
    output.mkdir(parents=True, exist_ok=True)
    (output / "summary.json").write_bytes((json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8"))
    (output / "summary.md").write_bytes(markdown.encode("utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    analyze = commands.add_parser("analyze")
    analyze.add_argument("--data", type=Path, required=True)
    analyze.add_argument("--results", type=Path, required=True)
    analyze.add_argument("--output", type=Path, required=True)
    compare = commands.add_parser("compare")
    compare.add_argument("--left", type=Path, required=True)
    compare.add_argument("--right", type=Path, required=True)
    compare.add_argument("--output", type=Path, required=True)
    performance = commands.add_parser("performance")
    performance.add_argument("--results", type=Path, required=True)
    performance.add_argument("--quality", type=Path, required=True)
    performance.add_argument("--output", type=Path, required=True)
    commands.add_parser("check")
    args = parser.parse_args()
    if args.command == "check":
        import unittest
        sys.dont_write_bytecode = True
        suite = unittest.defaultTestLoader.discover(str(Path(__file__).resolve().parent), pattern="test_analyze.py")
        return 0 if unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful() else 1
    provenance = {"analysisVersion": "polylinekit-recognition-analysis-v1", "python": platform.python_version(),
                  "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    if args.command == "performance":
        paths = sorted(list(args.results.glob("*-run-*.json")) + list(args.results.glob("*-run-*.json.gz")))
        require(bool(paths), "No application performance JSON files")
        runs = [read_json(path) for path in paths]
        quality, quality_sources = load_predictions(args.quality)
        metadata = {dataset: read_json(args.quality / (dataset + "-evaluation.json")) for dataset in METHODS}
        result = performance_rows(runs, quality, metadata)
        report = {**provenance, "performanceFiles": [{"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in paths],
                  "qualityFiles": quality_sources,
                  "qualityMetadataFiles": [{"file": dataset + "-evaluation.json", "sha256": hashlib.sha256((args.quality / (dataset + "-evaluation.json")).read_bytes()).hexdigest()} for dataset in METHODS],
                  "performance": result}
        write_report(args.output, report, performance_markdown(report))
        exact = result["qualityAgreement"]["exact"] and result["variantAgreement"]["exact"]
        print(f"Validated {result['runs']} performance runs and {result['cases']} cases; exact winner/score agreement: {exact}")
        return 0 if exact else 1
    if args.command == "analyze":
        predictions, sources = load_predictions(args.results)
        data, input_sources = load_data(args.data, {r["dataset"] for r in predictions.values()})
        report = {**provenance, "predictionFiles": sources, "inputFiles": input_sources,
                  "datasets": analyze_rows(predictions, data)}
        write_report(args.output, report, analysis_markdown(report))
        if "pendigits" in report["datasets"]:
            replay = {**provenance, "predictionFiles": sources, **pendigits_replay_cases(predictions)}
            (args.output / "replay-cases.json").write_bytes((json.dumps(replay, indent=2, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8"))
        print("Validated complete held-out grid; wrote quality summary JSON/Markdown.")
        return 0
    left, left_sources = load_predictions(args.left)
    right, right_sources = load_predictions(args.right)
    result = compare_rows(left, right)
    report = {**provenance, "leftFiles": left_sources, "rightFiles": right_sources, "comparison": result}
    write_report(args.output, report, compare_markdown(report))
    print(f"Full-row agreement: {result['exact']}; changed predictions: {result['changedPredictions']}")
    return 0 if result["exact"] else 1


if __name__ == "__main__":
    sys.exit(main())
