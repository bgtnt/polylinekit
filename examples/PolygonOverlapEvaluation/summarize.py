#!/usr/bin/env python3
"""Validate three frozen Solaris benchmark processes and report their medians.

This only reads original measurements: no timing, source build or data download.
"""
import argparse
import csv
import hashlib
import io
import json
import math
from pathlib import Path
import statistics
import struct
from datetime import datetime

BACKENDS = ["core", "clipper", "nts", "convex"]
STRATA = ["all", "both-convex", "has-concavity", "positive", "zero", "vertices-0-32", "vertices-33-64", "vertices-65-plus"]
FIXTURE = "883b7d327f1e082a8a34bbe065a40d8fb5a89bddd5d5c1014be5fbcf62fd4b20"
# Frozen after correctness verification, before timings: all visited pairs plus
# independent NTS classifications. Prevent all three runs omitting the same work.
SCHEDULE = "ac4616719102133bdd0a6e7c68b6d3f76573a846736c626b40d3c3b624246f7b"
# Independent reproduction from the original CSVs and pinned source matching logic.
SCHEDULE_KEYS = "5bb64b34cb9253b4844b78de03190616db6a8bddb9174945643c89f72e5ee8b1"
MASK = (1 << 64) - 1


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def integer(value, minimum=0):
    return type(value) is int and value >= minimum


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def same(a, b):
    return finite(a) and finite(b) and math.isclose(a, b, rel_tol=1e-12, abs_tol=1e-9)


def timestamp(value):
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(result.tzinfo is not None, "Timestamps must have an explicit offset.")
    return result


def strata(schedule):
    predicates = [lambda p: True, lambda p: p["BothConvex"], lambda p: not p["BothConvex"],
                  lambda p: p["Positive"], lambda p: not p["Positive"],
                  lambda p: p["SuppliedVertices"] <= 32, lambda p: 33 <= p["SuppliedVertices"] <= 64,
                  lambda p: p["SuppliedVertices"] > 64]
    return [{"Name": name, "PairIndices": [i for i, pair in enumerate(schedule) if predicate(pair)]}
            for name, predicate in zip(STRATA, predicates) if any(predicate(p) for p in schedule)]


def validate(directory):
    runs = [read(directory / f"run-{i}.json") for i in range(1, 4)]
    identity = read(directory / "launch-identity.json")
    launches = read(directory / "process-order.json")
    require(isinstance(launches, list) and len(launches) == 3, "Expected three successful sequential launches.")
    first = runs[0]
    require(first["FixtureHash"] == FIXTURE == identity["FixtureHash"] == sha(directory / "corpus.json"), "Frozen fixture hash differs.")
    require(first["ProtocolHash"] == identity["ProtocolHash"] == sha(directory / "bin/PROTOCOL.md"), "Frozen protocol hash differs.")
    revision = identity["Revision"]
    require(len(revision) == 40 and all(c in "0123456789abcdef" for c in revision), "Invalid source revision.")
    require(isinstance(identity["Sdk"], str) and identity["Sdk"], "Missing SDK identity.")
    binaries = first["Binaries"]
    require(len(binaries) == 4 and len({b["Name"] for b in binaries}) == 4, "Incomplete binary identities.")
    require([b["Name"] for b in binaries][1:] == ["PolylineKit.Winding", "Clipper2Lib", "NetTopologySuite"], "Unexpected measured assemblies.")
    require(identity["Binaries"] == {b["Name"]: b["Sha256"] for b in binaries}, "Launcher/binary hash mismatch.")
    for binary in binaries:
        require(binary["Sha256"] == sha(directory / "bin" / (binary["Name"] + ".dll")), "Frozen DLL hash differs.")
    for binary in binaries[:2]:
        require(binary["Version"].endswith("+" + revision), "Embedded harness/Core source differs.")
        require(("+" + revision).encode() in (directory / "bin" / (binary["Name"] + ".dll")).read_bytes(), "Recorded revision is absent from binary.")
    runtime_files = identity["RuntimeFiles"]
    require(set(runtime_files) == {"bin/" + binaries[0]["Name"] + ".deps.json",
                                  "bin/" + binaries[0]["Name"] + ".runtimeconfig.json", "summarize.py"}, "Incomplete runtime metadata identity.")
    for name, digest in runtime_files.items():
        require(sha(directory / name) == digest, "Frozen runtime metadata/summarizer hash differs.")
    schedule = first["Schedule"]
    require(len(schedule) == 205 and len({(p["PredictionIndex"], p["TruthIndex"]) for p in schedule}) == len(schedule), "Incomplete or duplicated pair schedule.")
    require(hashlib.sha256(json.dumps(schedule, sort_keys=True, separators=(",", ":")).encode()).hexdigest() == SCHEDULE,
            "Visited pair schedule/classifications differ from pre-timing reference verification.")
    keys = [[p["PredictionIndex"], p["TruthIndex"]] for p in schedule]
    require(hashlib.sha256(json.dumps(keys, separators=(",", ":")).encode()).hexdigest() == SCHEDULE_KEYS,
            "Visited pair keys differ from independent source-logic reproduction.")
    corpus = read(directory / "corpus.json")
    for pair in schedule:
        pi, gi = pair["PredictionIndex"], pair["TruthIndex"]
        require(integer(pi) and pi < len(corpus["Predictions"]) and integer(gi) and gi < len(corpus["Truth"]), "Invalid pair indices.")
        prediction, truth = corpus["Predictions"][pi], corpus["Truth"][gi]
        require(prediction["ImageId"] == truth["ImageId"] and not prediction["IsEmpty"] and not truth["IsEmpty"], "Invalid pair population.")
        require(all(type(pair[k]) is bool for k in ["BothConvex", "Positive", "Intersects"]) and
                (not pair["Positive"] or pair["Intersects"]), "Invalid pair classification.")
        vertices = sum(len(ring) for row in [prediction, truth] for polygon in row["Polygons"] for ring in polygon)
        require(pair["SuppliedVertices"] == vertices, "Vertex classification differs from original input.")
    groups = strata(schedule)
    require(groups == first["Strata"], "Stratum membership is incomplete or inconsistent.")
    names = ["kernel/" + s["Name"] for s in groups] + ["prepare/all", "evaluator/all"]
    pair_counts = {"kernel/" + s["Name"]: len(s["PairIndices"]) for s in groups} | {"prepare/all": 0, "evaluator/all": len(schedule)}
    convex_fallbacks = {"kernel/" + s["Name"]: sum(not schedule[i]["BothConvex"] for i in s["PairIndices"]) for s in groups}
    convex_fallbacks.update({"prepare/all": 0, "evaluator/all": sum(not p["BothConvex"] for p in schedule)})
    invariants = ["SchemaVersion", "Kind", "Revision", "Runtime", "OS", "Architecture", "Cpu", "LogicalProcessors",
                  "VectorHardwareAccelerated", "Avx2", "Fma", "TieredCompilation", "StopwatchFrequency", "FixtureHash",
                  "ProtocolHash", "ClipperVersion", "ClipperScale", "NtsVersion", "Binaries", "Schedule", "Strata", "Reference"]
    require(first["SchemaVersion"] == 1 and first["Kind"] == "solaris-polygon-overlap" and first["Revision"] == revision,
            "Unexpected benchmark/source identity.")
    require(first["ClipperVersion"] == "2.0.0" and first["ClipperScale"] == 1e10 and first["NtsVersion"] == "2.6.0", "Comparator precision/version differs.")
    require(first["TieredCompilation"] == "0" and integer(first["StopwatchFrequency"], 1), "Invalid runtime settings/timer.")
    reference = first["Reference"]
    require(reference["EligiblePredictions"] == 144 and reference["EligibleTruth"] == 169 and
            (reference["TruePos"], reference["FalsePos"], reference["FalseNeg"]) == (87, 57, 82), "Solaris reference counts differ.")
    require(reference["BoundsFalsePositives"] == sum(not p["Intersects"] for p in schedule) and
            reference["Positive"] == sum(p["Positive"] for p in schedule) == 162 and
            reference["BoundsRejected"] == 3931, "Pair diagnostic counts differ.")
    matrices = []
    previous_end = None
    for number, (run, launch) in enumerate(zip(runs, launches), 1):
        require(run["Run"] == launch["Run"] == number and run["Smoke"] is False, "Wrong run ID or smoke evidence.")
        require(all(run[key] == first[key] for key in invariants), "Measured environment/population differs across processes.")
        require(launch["ExitCode"] == 0 and launch["Revision"] == revision and launch["HarnessHash"] == binaries[0]["Sha256"], "Unsuccessful/unidentified launch.")
        start, finish = timestamp(launch["StartedUtc"]), timestamp(launch["FinishedUtc"])
        require(start <= timestamp(run["StartedUtc"]) <= timestamp(run["FinishedUtc"]) <= finish and
                (previous_end is None or previous_end <= start), "Processes overlap or raw UTC lies outside launch interval.")
        previous_end = finish
        order = BACKENDS[number - 1:] + BACKENDS[:number - 1]
        require(run["BackendOrder"] == order, "Expected rotated backend order.")
        expected_keys = [(backend, name) for backend in order for name in names]
        require([(r["Backend"], r["Name"]) for r in run["Rows"]] == expected_keys, "Missing, duplicated or reordered measurement rows.")
        matrix = {}
        total_ticks = 0
        for row in run["Rows"]:
            require(row["PairCount"] == pair_counts[row["Name"]] and integer(row["FallbackPairs"]) and
                    row["FallbackPairs"] <= row["PairCount"], "Invalid pair/fallback count.")
            require(row["FallbackPairs"] == (convex_fallbacks[row["Name"]] if row["Backend"] == "convex" else 0),
                    "Fallback count differs from the backend's fixed eligibility contract.")
            require(finite(row["Value"]), "Non-finite operation digest.")
            bits = struct.unpack(">Q", struct.pack(">d", row["Value"]))[0]
            require(row["ValueBits"] == f"{bits:016x}", "Value bits mismatch.")
            samples = row["Samples"]
            require(len(samples) == 5, "Expected five samples per row.")
            iterations = samples[0]["Iterations"]
            require(integer(iterations, 1) and iterations <= 65536 and iterations & (iterations - 1) == 0, "Invalid calibrated iteration count.")
            checksum = 14695981039346656037
            for _ in range(iterations):
                checksum = ((checksum ^ bits) * 1099511628211) & MASK
            for sample in samples:
                require(sample["Iterations"] == iterations and integer(sample["Ticks"], 1) and integer(sample["AllocatedBytes"]), "Invalid sample counters/count.")
                require(sample["Checksum"] == f"{checksum:016x}", "Operation checksum mismatch.")
                total_ticks += sample["Ticks"]
            ns = [s["Ticks"] * (1e9 / run["StopwatchFrequency"]) / iterations for s in samples]
            allocations = [s["AllocatedBytes"] / iterations for s in samples]
            require(same(row["MedianNanoseconds"], statistics.median(ns)) and same(row["MedianBytes"], statistics.median(allocations)), "Published process median differs from raw samples.")
            matrix[row["Backend"], row["Name"]] = row
        elapsed = (timestamp(run["FinishedUtc"]) - timestamp(run["StartedUtc"])).total_seconds()
        require(total_ticks / run["StopwatchFrequency"] <= elapsed + .01, "Recorded sample duration exceeds process duration.")
        matrices.append(matrix)
    aggregates = []
    for name in names:
        for backend in BACKENDS:
            rows = [matrix[backend, name] for matrix in matrices]
            require(all((r["ValueBits"], r["PairCount"], r["FallbackPairs"]) ==
                        (rows[0]["ValueBits"], rows[0]["PairCount"], rows[0]["FallbackPairs"]) for r in rows), "Operation result/counters changed across processes.")
            times = [r["MedianNanoseconds"] for r in rows]
            aggregates.append({"Name": name, "Backend": backend, "PairCount": rows[0]["PairCount"],
                               "FallbackPairs": rows[0]["FallbackPairs"], "MedianNanoseconds": statistics.median(times),
                               "MinProcessMedianNanoseconds": min(times), "MaxProcessMedianNanoseconds": max(times),
                               "MedianBytes": statistics.median(r["MedianBytes"] for r in rows),
                               "MaxProcessMedianBytes": max(r["MedianBytes"] for r in rows)})
    for row in aggregates:
        baseline = next(r for r in aggregates if r["Name"] == row["Name"] and r["Backend"] == "core")
        row["TimeOverCore"] = row["MedianNanoseconds"] / baseline["MedianNanoseconds"]
    files = [f"run-{i}.json" for i in range(1, 4)] + ["launch-identity.json", "process-order.json", "corpus.json", "bin/PROTOCOL.md"]
    files += ["bin/" + b["Name"] + ".dll" for b in binaries]
    files += list(runtime_files)
    return {"SchemaVersion": 1, "Revision": revision, "Environment": {key: first[key] for key in invariants if key not in ["Schedule", "Strata"]},
            "Sdk": identity["Sdk"], "Processes": 3, "Samples": sum(len(r["Samples"]) for run in runs for r in run["Rows"]),
            "SpeedGate": "None predeclared; correctness is independent of performance.", "Measurements": aggregates,
            "EvidenceFiles": [{"Path": path, "Bytes": (directory / path).stat().st_size, "Sha256": sha(directory / path)} for path in files]}


def write_reports(directory, evidence):
    rows = evidence["Measurements"]
    (directory / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    stream = io.StringIO(newline="")
    writer = csv.DictWriter(stream, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    (directory / "summary.csv").write_text(stream.getvalue(), encoding="utf-8", newline="")
    lines = ["# Solaris polygon-overlap measurements", "", "Source: `" + evidence["Revision"] + "`.", "",
             "Three process medians; ranges are observed process medians, not confidence intervals. Lower time is better.",
             "Rows are whole batches, not time per pair. Kernel strata overlap; do not sum them. No speed gate was declared.", "",
             "| Scope | Backend | Pairs | Fallback | Median ms | Range ms | B/invocation | Time / Core |",
             "|---|---|---:|---:|---:|---:|---:|---:|"]
    for r in rows:
        lines.append(f'| {r["Name"]} | {r["Backend"]} | {r["PairCount"]} | {r["FallbackPairs"]} | '
                     f'{r["MedianNanoseconds"] / 1e6:.6f} | {r["MinProcessMedianNanoseconds"] / 1e6:.6f}–'
                     f'{r["MaxProcessMedianNanoseconds"] / 1e6:.6f} | {r["MedianBytes"]:.1f} | {r["TimeOverCore"]:.3f} |')
    lines += ["", "Preparation excludes file parsing and pair computation. Complete evaluator includes warm-cache file reading,",
              "validation, fresh preparation/matching and output rows; final JSON serialization/disk writing are excluded.",
              "Repeated small-fixture timing does not establish performance on new data or a universal backend ranking.", ""]
    (directory / "summary.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    result = validate(args.directory.resolve())
    write_reports(args.directory.resolve(), result)
    print(f'Validated {result["Samples"]} samples and {len(result["Measurements"])} aggregate rows; no timing gate.')
