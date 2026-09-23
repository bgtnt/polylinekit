#!/usr/bin/env python3
"""Pinned public-data import for the recognition experiment; Python standard library only."""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PARSER_VERSION = "polylinekit-recognition-import-v1"
SEVEN_ZIP_VERSION = "26.02"
ARCHIVES = {
    "dollar": {
        "filename": "dollar-xml.zip",
        "url": "https://depts.washington.edu/acelab/proj/dollar/xml.zip",
        "sha256": "c5d81282e46571d813fdcab06d67ef96c848c736922deaba5189a9658b1e43f6",
        "bytes": 4789784,
    },
    "pendigits": {
        "filename": "pendigits.zip",
        "url": "https://archive.ics.uci.edu/static/public/81/pen+based+recognition+of+handwritten+digits.zip",
        "sha256": "1e02bea023613c2b11c9492f6f34caf975420455934f3527d270cee9a1f03b64",
        "bytes": 1668118,
    },
}
DOLLAR_LABELS = {
    "arrow", "caret", "check", "circle", "delete_mark", "left_curly_brace",
    "left_sq_bracket", "pigtail", "question_mark", "rectangle", "right_curly_brace",
    "right_sq_bracket", "star", "triangle", "v", "x",
}
PENDIGITS_CLASSES = {
    "train": [780, 779, 780, 719, 780, 720, 720, 778, 719, 719],
    "test": [363, 364, 364, 336, 364, 335, 336, 364, 336, 336],
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def verify_archive(data: bytes, dataset: str) -> None:
    expected = ARCHIVES[dataset]
    require(len(data) == expected["bytes"], f"{dataset}: archive size changed ({len(data)} bytes)")
    require(sha256(data) == expected["sha256"], f"{dataset}: archive SHA-256 mismatch; inspect before changing the pin")


def fetch_archive(dataset: str, directory: Path, offline: bool = False) -> Path:
    expected = ARCHIVES[dataset]
    path = directory / expected["filename"]
    if path.exists():
        verify_archive(path.read_bytes(), dataset)
        return path
    require(not offline, f"Missing pinned archive: {path}; rerun without --offline to download")
    request = urllib.request.Request(expected["url"], headers={"User-Agent": PARSER_VERSION})
    with urllib.request.urlopen(request, timeout=90) as response:
        data = response.read(expected["bytes"] + 1)
    verify_archive(data, dataset)
    directory.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".part")
    temporary.write_bytes(data)
    temporary.replace(path)
    return path


def number(text: str) -> int | float:
    if re.fullmatch(r"[+-]?\d+", text):
        return int(text)
    value = float(text)
    require(math.isfinite(value), "Nonfinite coordinate or time")
    return value


def mark_support(record: dict) -> dict:
    strokes = record["strokes"]
    reason = None
    if len(strokes) != 1:
        reason = "multiple-strokes" if len(strokes) > 1 else "no-strokes"
    elif len(strokes[0]) < 2 or all(
        (p["x"], p["y"]) == (strokes[0][0]["x"], strokes[0][0]["y"]) for p in strokes[0]
    ):
        reason = "zero-length-stroke"
    record["supported"] = reason is None
    record["exclusionReason"] = reason
    return record


def parse_dollar_xml(member: str, data: bytes) -> dict:
    path = PurePosixPath(member)
    require(len(path.parts) == 4 and path.parts[0] == "xml_logs", f"Unexpected XML path: {member}")
    subject = re.fullmatch(r"s(\d{2})(?: \(pilot\))?", path.parts[1])
    name = re.fullmatch(r"([a-z_]+)(\d{2})\.xml", path.name)
    require(subject is not None and name is not None, f"Malformed XML identifier: {member}")
    writer = int(subject[1])
    label, repetition = name[1], int(name[2])
    speed = path.parts[2]
    require(1 <= writer <= 11 and 1 <= repetition <= 10 and label in DOLLAR_LABELS and
            speed in {"slow", "medium", "fast"}, f"Unexpected gesture metadata: {member}")
    require(("(pilot)" in path.parts[1]) == (writer == 1), f"Pilot directory mismatch: {member}")
    root = ET.fromstring(data)
    require(root.tag == "Gesture", f"Unexpected XML root: {member}")
    require(root.get("Name") == path.stem and int(root.get("Subject", "-1")) == writer and
            root.get("Speed") == speed and int(root.get("Number", "-1")) == repetition,
            f"Folder/file/XML metadata mismatch: {member}")
    require(all(child.tag == "Point" for child in root), f"Unexpected XML child: {member}")
    points = []
    for item in root:
        point = {"x": number(item.attrib["X"]), "y": number(item.attrib["Y"])}
        if "T" in item.attrib:
            point["t"] = number(item.attrib["T"])
        points.append(point)
    require(len(points) == int(root.get("NumPts", "-1")), f"NumPts mismatch: {member}")
    return mark_support({
        "sampleId": f"dollar/{member}", "dataset": "dollar", "split": "pilot" if writer == 1 else "main",
        "writerId": f"s{writer:02d}", "label": label, "speed": speed, "repetition": repetition,
        "strokes": [points], "sourceMember": member,
        # Raw metadata is provenance, not a verified session identifier.
        "sourceMetadata": dict(sorted(root.attrib.items())),
    })


def parse_pendigits(text: str, member: str, split: str) -> list[dict]:
    """Read this pinned archive's explicit UNIPEN subset, never interpolate pen-up gaps."""
    require(split in {"train", "test"}, "Pendigits split must be train or test")
    records: list[dict] = []
    current = None
    stroke = None
    declared_first = declared_last = next_stroke = 0
    segment_pattern = re.compile(r'\.SEGMENT\s+DIGIT\s+(\d+)(?:-(\d+))?\s+\?\s+"([0-9])"')

    def finish() -> None:
        if current is None:
            return
        require(stroke is None, f"Unclosed PEN_DOWN: {current['sampleId']}")
        require(len(current["strokes"]) == declared_last - declared_first + 1,
                f"Declared stroke range differs from PEN_DOWN count: {current['sampleId']}")
        require(len(current["sourceDt"]) == len(current["strokes"]), f"Missing DT directive: {current['sampleId']}")
        records.append(mark_support(current))

    for line_number, raw in enumerate(text.splitlines(), 1):
        line = raw.strip()
        if not line:
            continue
        if line.startswith(".SEGMENT"):
            finish()
            match = segment_pattern.fullmatch(line)
            require(match is not None, f"Unsupported SEGMENT syntax at {member}:{line_number}")
            declared_first, declared_last = int(match[1]), int(match[2] or match[1])
            require(declared_first == next_stroke and declared_last >= declared_first,
                    f"Noncontiguous source stroke IDs at {member}:{line_number}")
            next_stroke = declared_last + 1
            segment_id = f"{declared_first:06d}" + (f"-{declared_last:06d}" if declared_first != declared_last else "")
            current = {
                "sampleId": f"pendigits/{member}/{segment_id}", "dataset": "pendigits", "split": split,
                "label": match[3], "strokes": [], "sourceMember": member,
                "sourceSegment": line.split()[2], "sourceComment": [], "sourceDt": [],
            }
        elif line == ".PEN_DOWN":
            require(current is not None and stroke is None, f"Misplaced PEN_DOWN at {member}:{line_number}")
            stroke = []
            current["strokes"].append(stroke)
        elif line == ".PEN_UP":
            require(stroke is not None and len(stroke) > 0, f"Empty/misplaced PEN_UP at {member}:{line_number}")
            stroke = None
        elif line.startswith(".COMMENT"):
            require(current is not None and stroke is None, f"Misplaced COMMENT at {member}:{line_number}")
            current["sourceComment"].append(line[len(".COMMENT"):].strip())
        elif line.startswith(".DT"):
            require(current is not None and stroke is None and len(line.split()) == 2,
                    f"Misplaced DT at {member}:{line_number}")
            current["sourceDt"].append(number(line.split()[1]))
        elif line.startswith((".INCLUDE ", ".INCLUDE\t", ".LEXICON ", ".HIERARCHY ", ".HIERARCHY\t")):
            require(current is None, f"Unexpected repeated header at {member}:{line_number}")
            # No INCLUDE file is followed or executed. dene.doc is absent from the archive.
        else:
            require(not line.startswith(".") and stroke is not None,
                    f"Unsupported directive/point outside stroke at {member}:{line_number}")
            values = line.split()
            require(len(values) == 2, f"Expected exactly two XY columns at {member}:{line_number}")
            stroke.append({"x": number(values[0]), "y": number(values[1])})
    finish()
    return records


def validate_records(records: list[dict], dataset: str) -> None:
    ids = [r["sampleId"] for r in records]
    require(len(ids) == len(set(ids)), f"Duplicate sample IDs in {dataset}")
    if dataset == "dollar":
        require(len(records) == 5280 and all(r["supported"] for r in records), "Unexpected dollar total/support count")
        cells = Counter((r["writerId"], r["label"], r["speed"], r["repetition"]) for r in records)
        expected = {(f"s{writer:02d}", label, speed, repetition)
                    for writer in range(1, 12) for label in DOLLAR_LABELS
                    for speed in ("slow", "medium", "fast") for repetition in range(1, 11)}
        require(set(cells) == expected and all(n == 1 for n in cells.values()), "Dollar factorial coverage differs")
    else:
        for split, total, supported in (("train", 7494, 5564), ("test", 3498, 2744)):
            selected = [r for r in records if r["split"] == split]
            require(len(selected) == total and sum(r["supported"] for r in selected) == supported,
                    f"Unexpected Pendigits {split} total/support count")
            classes = Counter(r["label"] for r in selected)
            require([classes[str(i)] for i in range(10)] == PENDIGITS_CLASSES[split],
                    f"Pendigits {split} original class counts differ")
        require(all("writerId" not in r and "session" not in r for r in records), "Undocumented Pendigits identity inference")


def find_seven_zip(override: str | None) -> tuple[str, dict]:
    executable = override or shutil.which("7zz") or shutil.which("7z")
    if executable is None:
        candidate = Path("C:/Program Files/7-Zip/7z.exe")
        if candidate.is_file():
            executable = str(candidate)
    require(executable is not None, "Pendigits needs development-only 7-Zip 26.02; provide --seven-zip /path/to/7zz (no decoder is bundled)")
    result = subprocess.run([executable], check=True, capture_output=True, text=True, errors="replace")
    match = re.search(r"^7-Zip(?: \[[^]]+\])?\s+(\d+\.\d+).*", result.stdout, re.MULTILINE)
    require(match is not None and match[1] == SEVEN_ZIP_VERSION, f"Expected 7-Zip {SEVEN_ZIP_VERSION}; got {result.stdout.splitlines()[:3]}")
    return executable, {"tool": "7-Zip", "version": match[1], "header": match[0].strip()}


def import_archive(dataset: str, archive_path: Path, root: Path, seven_zip: str | None) -> tuple[list[dict], dict]:
    verify_archive(archive_path.read_bytes(), dataset)
    details = {}
    with zipfile.ZipFile(archive_path) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), "Duplicate ZIP members")
        if dataset == "dollar":
            records = [parse_dollar_xml(name, archive.read(name)) for name in sorted(names) if name.endswith(".xml")]
        else:
            executable, details["decompressor"] = find_seven_zip(seven_zip)
            records = []
            details["members"] = []
            raw = root / "raw"
            raw.mkdir(parents=True, exist_ok=True)
            for member, split in (("pendigits-orig.tra", "train"), ("pendigits-orig.tes", "test")):
                compressed = archive.read(member + ".Z")
                path = raw / (member + ".Z")
                path.write_bytes(compressed)
                # Absolute input path; only decompressed stdout is consumed. No archive paths are extracted.
                result = subprocess.run([executable, "e", "-so", str(path.resolve())], check=True, capture_output=True)
                text = result.stdout.decode("ascii")
                (raw / member).write_bytes(result.stdout)
                records.extend(parse_pendigits(text, member, split))
                details["members"].append({"name": member, "compressedSha256": sha256(compressed),
                                           "decompressedSha256": sha256(result.stdout), "bytes": len(result.stdout)})
            documentation = archive.read("pendigits-orig.names")
            (raw / "pendigits-orig.names").write_bytes(documentation)
            details["sourceDocumentation"] = {"member": "pendigits-orig.names", "sha256": sha256(documentation)}
    records.sort(key=lambda record: record["sampleId"])
    validate_records(records, dataset)
    return records, details


def point_distribution(records: list[dict]) -> dict:
    values = sorted(sum(len(s) for s in r["strokes"]) for r in records)
    def quantile(fraction: float) -> int:
        return values[max(0, math.ceil(fraction * len(values)) - 1)]
    return {"minimum": values[0], "p25": quantile(.25), "p50": quantile(.5), "p75": quantile(.75),
            "p95": quantile(.95), "maximum": values[-1], "total": sum(values),
            "quantileDefinition": "nearest rank; all strokes counted; original duplicate points retained"}


def summarize(records: list[dict]) -> dict:
    return {
        "rawCount": len(records), "supportedCount": sum(r["supported"] for r in records),
        "supportFraction": sum(r["supported"] for r in records) / len(records),
        "strokeCounts": dict(sorted(Counter(str(len(r["strokes"])) for r in records).items())),
        "exclusions": dict(sorted(Counter(r["exclusionReason"] for r in records if not r["supported"]).items())),
        "pointCounts": point_distribution(records),
    }


def make_manifest(records: list[dict], dataset: str, details: dict, jsonl: bytes, parser_revision: str) -> dict:
    groups = {}
    for split in sorted({r["split"] for r in records}):
        selected = [r for r in records if r["split"] == split]
        groups[split] = summarize(selected)
        groups[split]["classes"] = {label: summarize([r for r in selected if r["label"] == label])
                                    for label in sorted({r["label"] for r in selected})}
    metadata_keys = ("sampleId", "dataset", "split", "label", "writerId", "speed", "repetition", "supported", "exclusionReason")
    manifest = {
        "schemaVersion": 1, "dataset": dataset, "parserVersion": PARSER_VERSION,
        "parserRevision": parser_revision, "parserSourceSha256": sha256(Path(__file__).read_bytes()),
        "archive": ARCHIVES[dataset], "jsonlSha256": sha256(jsonl), "counts": summarize(records), "splits": groups,
        "coordinatePolicy": {
            "x": "original X attribute" if dataset == "dollar" else "original first numeric column",
            "y": "original Y attribute" if dataset == "dollar" else "original second numeric column",
            "transform": "none; no swapping, reflection, closure, deduplication, resampling or normalization during import",
            "display": "dollar uses screen-style XY; Pendigits retains source tablet axes, without an inferred display rotation",
            "time": "original Point.T retained without rebasing" if dataset == "dollar" else "no point timestamps; DT directives retained as opaque sourceDt, not expanded into invented t values",
        },
        "identityPolicy": "Subject verified against sNN directory; Date/TimeOfDay retained as raw metadata, not session IDs" if dataset == "dollar"
                          else "writerId/session omitted; COMMENT numbers are opaque; original documentation describes disjoint train/test writers",
        "supportPolicy": "Exactly one stroke containing at least two distinct XY positions; all other records retained and marked unsupported",
        "templateBankIds": [], "templateBankNote": "Assigned by the frozen evaluation protocol, not by the importer",
        "records": [{**{key: r[key] for key in metadata_keys if key in r}, "strokeCount": len(r["strokes"]),
                     "pointCount": sum(len(s) for s in r["strokes"])} for r in records],
        **details,
    }
    return manifest


def write_import(records: list[dict], dataset: str, details: dict, root: Path, parser_revision: str) -> dict:
    jsonl = b"".join((json.dumps(record, ensure_ascii=False, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")
                     for record in records)
    manifest = make_manifest(records, dataset, details, jsonl, parser_revision)
    for filename, content in ((dataset + ".jsonl", jsonl), (dataset + "-manifest.json",
                              (json.dumps(manifest, indent=2, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8"))):
        path = root / filename
        temporary = path.with_suffix(path.suffix + ".part")
        temporary.write_bytes(content)
        temporary.replace(path)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("fetch", "import", "check"))
    parser.add_argument("--dataset", choices=("all", "dollar", "pendigits"), default="all")
    parser.add_argument("--data-root", type=Path, default=Path(__file__).resolve().parents[2] / "artifacts/recognition/data")
    parser.add_argument("--seven-zip", help="7-Zip 26.02 executable (7z.exe or 7zz); development/import only")
    parser.add_argument("--offline", action="store_true", help="Require already downloaded, hash-verified archives")
    parser.add_argument("--parser-revision", default="working-tree", help="Source commit when committed; source SHA is always included")
    args = parser.parse_args()
    if args.command == "check":
        import unittest
        suite = unittest.defaultTestLoader.discover(str(Path(__file__).resolve().parent), pattern="test_import_datasets.py")
        return 0 if unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful() else 1
    args.data_root.mkdir(parents=True, exist_ok=True)
    for dataset in ARCHIVES if args.dataset == "all" else (args.dataset,):
        archive = fetch_archive(dataset, args.data_root / "downloads", args.offline)
        if args.command == "fetch":
            print(f"{dataset}: pinned archive verified ({archive.stat().st_size} bytes)")
            continue
        records, details = import_archive(dataset, archive, args.data_root, args.seven_zip)
        manifest = write_import(records, dataset, details, args.data_root, args.parser_revision)
        print(json.dumps({"dataset": dataset, "jsonlSha256": manifest["jsonlSha256"],
                          "splits": {name: {k: value[k] for k in ("rawCount", "supportedCount", "supportFraction")}
                                     for name, value in manifest["splits"].items()}}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
