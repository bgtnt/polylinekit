#!/usr/bin/env python3
"""Curate original historical measurements. No download, build, timing or publication.

Requires the original ignored artifacts and historical Git objects in this clone.
Writes a deterministic ZIP only after the three frozen summarizers reproduce their
original reports byte-for-byte. Missing or changed evidence is a hard error.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

REPO = Path(__file__).resolve().parents[1]
ARCHIVED = "5ef33e8e0f11ba955cf8fc078e91dc87a325f7b3"
INTERSECTION = "f93cc25d589a471e405af9ed53d094dd311aac61"
AREA = {
    "0e53b07": ("0e53b0713b2d0c6ebd0b7603125010defccb81ed",
                "6553cdf2344c7e1974d37b4f0e1e312d80ca685be90c069af269b7d7382da5ea",
                "90d7c2f921805bdd5a20cba74784e614fabfba4f77820db0a4face9cb6c1936f"),
    "eea1671": ("eea1671c6288ea444e98cddcca9ee6d18669124b",
                "171a8d9893d23b6cd6a285288f68341c211a6056661a0ba597f0657d11500cc6",
                "4f27d0e356b94aad12c3f913cb4a1b1f1a5a77c5f0f6fa18ba8d35e29b423b79"),
}
INTERSECTION_SOURCE = [
    "ComparatorChecks.cs", "Comparators.cs", "PackedBoundsIndex.cs", "PackedBoundsIndexChecks.cs",
    "Program.cs", "RegionCoverage.csproj", "packages.lock.json", "INTERSECTION-PROTOCOL.md",
    "data/README.md", "data/PROTOCOL.md", "data/freeze.py", "data/manifest.json", "data/zones.json",
    "data/queries.json", "data/source/nc-counties-count.json", "data/source/nc-counties-layer.json",
    "data/source/nc-counties.geojson", "data/source/nc-districts-count.json",
    "data/source/nc-districts-layer.json", "data/source/nc-districts.geojson",
]
AREA_SOURCE = ["Corpus.cs", "Program.cs", "PROTOCOL.md", "PolylineKit.AreaBenchmarks.csproj",
               "packages.lock.json", "run.ps1", "data/README.md", "data/counties-5070.json", "data/layer.json"]
payload = {}
origins = {}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def checked(data, expected, label):
    if sha(data) != expected:
        raise ValueError("Original checksum differs: " + label)
    return data


def git(revision, path):
    return subprocess.check_output(["git", "show", revision + ":" + path], cwd=REPO)


def add(path, data, origin):
    if path in payload or Path(path).is_absolute() or ".." in Path(path).parts:
        raise ValueError("Duplicate or unsafe archive path: " + path)
    # Binaries necessarily preserve known public-repository CodeView build paths.
    # Never include PDB/EXE, private attachments or personal files. No glob imports.
    if path.endswith((".pdb", ".exe")):
        raise ValueError("Excluded binary kind: " + path)
    if re.search(rb"MPR001|\.codex[\\/]|\.codex-remote-attachments|gh[pousr]_[A-Za-z0-9]{20}", data, re.I):
        raise ValueError("Private workspace marker or credential-shaped token: " + path)
    if not path.endswith(".dll") and re.search(rb"[A-Za-z]:[\\/]+Users[\\/]", data, re.I):
        raise ValueError("Personal absolute path in text: " + path)
    payload[path] = data
    origins[path] = origin


def source(prefix, revision, directory, names):
    for name in names:
        path = directory + "/" + name
        add(prefix + "/source/" + name, git(revision, path), "git:" + revision + ":" + path)


def local(path, destination, expected=None):
    data = (REPO / path).read_bytes()
    if expected:
        checked(data, expected, path)
    add(destination, data, path)


def measured_binaries(prefix, directory, runner, hashes, data_names):
    for name, expected in hashes.items():
        local(directory + "/" + name + ".dll", prefix + "/bin/" + name + ".dll", expected)
    for suffix in [".deps.json", ".runtimeconfig.json"]:
        local(directory + "/" + runner + suffix, prefix + "/bin/" + runner + suffix)
    for name in data_names:
        original = payload[prefix + "/source/data/" + name]
        local(directory + "/data/" + name, prefix + "/bin/data/" + name, sha(original))


README = """# PolylineKit historical evidence for alpha review

This local publication candidate preserves original measurements; it is not a
performance claim for the alpha release binary. No new timings were taken when
assembling it. The full file allowlist, provenance and SHA-256 values are in
`manifest.json` and `SHA256SUMS`. Verify the ZIP's external SHA-256 before extraction.

## Inspect and re-summarize

Python 3 (standard library only) verifies every file. The optional second command
requires the .NET 10 runtime. Byte-identical report checks were repeated on Windows;
the historical reports use Windows newlines, so another OS may differ in formatting.
No build, NuGet restore, network access or benchmark run is part of verification.

```text
python verify-alpha-evidence.py .
python verify-alpha-evidence.py . --summarize
```

The verifier copies raw files to temporary directories and invokes the original
frozen DLL for each experiment. It recalculates all 2,940 sample-derived records
through the original summarizers and checks regenerated reports byte-for-byte.
The original protocol files contain the measurement commands; do not mistake those
commands for the re-summarization above. Sources are the unmodified measured Git
versions of each harness, comparator, oracle, input and protocol, under `source/`.
They refer to library projects in their original full repository checkout; use the
recorded immutable Git commit for a new build, not a partial source folder here.
Original build/test/control records are preserved as historical records; their
builds and corruption-control suites were not newly executed for this bundle.

## Supported historical observations

* `intersection-f93cc25`: source f93cc25d589a471e405af9ed53d094dd311aac61;
  98 county rings + 11 congressional district rings, 137 positive noncontained
  pairs, both complete traversal directions. Three processes, 540 samples.
  All four primary gates passed: intersection-only used 26.1–28.9% less time
  than the reusable Clipper baseline. See the preserved archived report, its
  immutable manifest and the complete raw pair values, first-use and timing data.
  The old full-result Winding method was measured in the same processes.
* `area-0e53b07`: source 0e53b0713b2d0c6ebd0b7603125010defccb81ed;
  the complete-batch API/fallback gate **failed** (EvenOdd +23.19%).
* `area-eea1671`: source eea1671c6288ea444e98cddcca9ee6d18669124b;
  the same area workload **passed**. Both area records have three processes,
  1,200 samples each, 9 original rings from 8 Delaware/Rhode Island counties,
  both fills and all per-ring/batch rows. All 18 per-ring/fill ratios in both
  experiments were within 10%. These are ring areas, not county totals.

The later area result does not establish why the earlier anomaly disappeared.
The experiments were not a paired causal comparison; Clipper also ran faster in
the later session. Neither experiment demonstrates a gain over simple shoelace.
The full experiment-specific environments and binary hashes are in raw JSON.
Historical common environment: Windows build 26200, x64, .NET 10.0.12,
Intel Core i9-9900K / Family 6 Model 158, tiered compilation disabled.
Observed process ranges are not confidence intervals. Clipper constructs
quantized contours while PolylineKit returns areas on binary64 coordinates;
these are distinct output/precision contracts, not universal speed claims.
The intersection measurement predates the bounded workspace-retention policy.
Its warm 0 B/op observations must not be generalized to current oversized calls.

## Inputs, attribution and binaries

The U.S. Census Bureau TIGERweb original responses, exact queries, metadata,
structural exclusions, attribution, terms links and hashes are under each
`source/data/` folder. Inputs were not selected by observed speed. The area fixture
uses unmodified projected EPSG:5070 doubles; the intersection protocol applies one
common translation. No geometry is silently repaired. NTS is an agreement check,
not an exact oracle; the area harness includes its independent exact simple-ring
shoelace oracle. See each protocol for its numerical acceptance contract.

The bundle contains only allowlisted unchanged DLLs and runtime metadata, not PDBs
or native launchers. The DLL CodeView records retain public-repository build paths
from the original machines, including the Clipper publisher's build path. Removing
those records would change the binary identities required by the summarizers.
No private project sources, attachments, account configuration or credentials are
included. PolylineKit is MIT; Clipper2 2.0.0 is Boost 1.0; NetTopologySuite 2.6.0
is BSD-3-Clause with its full upstream notices, all retained under `licenses/`.

## Historical cache probe

`cache-policy/` contains the original reflection probe and privacy-redacted output
copies. Only the JSON `Source` string is replaced by a relative descriptive label;
`provenance.json` records the original hashes, binary identities and recipe. No
numeric value was rewritten or remeasured. The after binary was a dirty working
build: its embedded 0e53 commit does not identify the changed implementation.
Both sides include reflection-call allocation overhead. These observations are
not a peak-memory limit, standalone library B/op benchmark or clean-revision A/B.
The exact before/after DLLs are retained, so the probe can be inspected/repeated
if explicitly desired; verification above does not execute allocation experiments.

The archive is prepared locally. This file and its checksums do not imply that it
has been published or reviewed as a release asset.
"""


def collect():
    manifest_bytes = git(ARCHIVED, "benchmarks/intersection-evidence.json")
    historical = json.loads(manifest_bytes)
    prefix = "intersection-f93cc25"
    add(prefix + "/archived/intersection-evidence.json", manifest_bytes,
        "git:" + ARCHIVED + ":benchmarks/intersection-evidence.json")
    add(prefix + "/archived/INTERSECTION-RESULTS.md",
        git(ARCHIVED, "examples/RegionCoverage/INTERSECTION-RESULTS.md"),
        "git:" + ARCHIVED + ":examples/RegionCoverage/INTERSECTION-RESULTS.md")
    archive = historical["Archive"]
    original_zip = checked((REPO / "artifacts" / archive["Name"]).read_bytes(), archive["Sha256"], archive["Name"])
    if len(original_zip) != archive["Bytes"]:
        raise ValueError("Original archive length differs.")
    with zipfile.ZipFile(REPO / "artifacts" / archive["Name"]) as zipped:
        if set(zipped.namelist()) != {row["Name"] for row in historical["RawFiles"]}:
            raise ValueError("Original archive inventory differs.")
        for row in historical["RawFiles"]:
            path = "artifacts/coverage-intersection/" + row["Name"]
            local(path, prefix + "/raw/" + row["Name"], row["Sha256"])
            if zipped.read(row["Name"]) != payload[prefix + "/raw/" + row["Name"]]:
                raise ValueError("Original archive and original folder differ.")
    # Do not nest an opaque ZIP: its verified, identical allowlisted members are above.
    source(prefix, INTERSECTION, "examples/RegionCoverage", INTERSECTION_SOURCE)
    measured_binaries(prefix, "artifacts/intersection-measured-bin-f93cc25", "RegionCoverage",
                      historical["Assemblies"], ["manifest.json", "zones.json", "queries.json"])
    experiments = [{"Name": prefix, "Revision": INTERSECTION, "Runner": "RegionCoverage.dll",
                    "SummarizeCommand": "summarize-intersection", "GeneratedFiles": ["summary.md", "decision.json"],
                    "HistoricalGatePassed": True, "Samples": 540}]
    for short, (revision, evidence_hash, controls_hash) in AREA.items():
        prefix = "area-" + short
        raw = "artifacts/area-real-contours-" + short
        local(raw + "/evidence.json", prefix + "/raw/evidence.json", evidence_hash)
        evidence = json.loads(payload[prefix + "/raw/evidence.json"])
        env = evidence["Environment"]
        if env["Revision"] != revision:
            raise ValueError("Area source identity differs.")
        for row in evidence["RawFiles"]:
            local(raw + "/" + row["File"], prefix + "/raw/" + row["File"], row["Sha256"])
        local(raw + "/validation.json", prefix + "/raw/validation.json", evidence["ValidationHash"])
        local(raw + "/process-order.json", prefix + "/raw/process-order.json", evidence["ProcessOrderHash"])
        local(raw + "/negative-controls.json", prefix + "/raw/negative-controls.json", controls_hash)
        local(raw + "/summary.md", prefix + "/raw/summary.md")
        source(prefix, revision, "benchmarks/PolylineKit.AreaBenchmarks", AREA_SOURCE)
        measured_binaries(prefix, "artifacts/area-measured-bin-" + short, "PolylineKit.AreaBenchmarks",
                          {"PolylineKit.AreaBenchmarks": env["HarnessHash"], "PolylineKit.Winding": env["LibraryHash"],
                           "Clipper2Lib": env["ClipperHash"]}, ["counties-5070.json", "layer.json"])
        experiments.append({"Name": prefix, "Revision": revision, "Runner": "PolylineKit.AreaBenchmarks.dll",
                            "SummarizeCommand": "summarize", "GeneratedFiles": ["summary.md", "evidence.json"],
                            "HistoricalGatePassed": evidence["Pass"], "Samples": evidence["SampleCount"]})
    memory = []
    for side, original_hash, label in [
        ("before", "2e159bfad93cc3671fdd4a72e0595628bf9e1d7b93f99d8e56a3c6b8f8f51b1d", "before/PolylineKit.Winding.dll"),
        ("after", "1a9d4dd1e13995aeaec89966b6bdb2c348f9ea53023b316de99a0b6c662a41b0", "after-dirty/PolylineKit.Winding.dll"),
    ]:
        raw = checked((REPO / "artifacts/cache-policy-probe" / (side + ".json")).read_bytes(), original_hash, side)
        # Byte-level single-token replacement preserves every numeric token and newline.
        redacted, count = re.subn(rb'("Source":\s*)"(?:[^"\\]|\\.)*"',
                                 lambda match: match[1] + json.dumps(label).encode(), raw, count=1)
        if count != 1:
            raise ValueError("Expected exactly one Source string in memory probe.")
        record = json.loads(raw)
        add("cache-policy/" + side + ".redacted.json", redacted, "privacy-redacted original; see provenance.json")
        dll = ("artifacts/area-measured-bin-0e53b07/PolylineKit.Winding.dll" if side == "before" else
               "artifacts/cache-policy-probe/current/PolylineKit.Winding.dll")
        local(dll, "cache-policy/" + label, record["AssemblySha256"])
        memory.append({"Side": side, "OriginalSha256": original_hash, "OriginalBytes": len(raw),
                       "RedactedSha256": sha(redacted), "AssemblySha256": record["AssemblySha256"],
                       "EmbeddedVersion": record["Version"], "CleanCommitIdentifiesBinary": side == "before",
                       "PublishedSourceLabel": label})
    for name in ["Program.cs", "cache-policy-probe.csproj", "packages.lock.json"]:
        local("artifacts/cache-policy-probe/" + name, "cache-policy/" + name)
    add("cache-policy/provenance.json", encoded({"Kind": "historical-allocation-probe", "NewMeasurement": False,
        "Redaction": "Replace only the JSON Source string token with PublishedSourceLabel; preserve all other bytes.",
        "AfterBuild": "Dirty working tree after cache-cap changes; embedded 0e53 commit is not its source identity.",
        "Records": memory}), "curation metadata")
    for name, expected in [
        ("Clipper2-2.0.0.txt", "36266a8fd073568394cb81cdb2b124f7fdae2c64c1a7ed09db34b4d22efa2951"),
        ("NetTopologySuite-2.6.0.md", "81be2ef7f10ff8f27339062bf1a9182c1f8b3a4fe7882699fc328a1a8f8b2693")]:
        local("scripts/evidence-licenses/" + name, "licenses/" + name, expected)
    add("licenses/PolylineKit-MIT.txt", git(ARCHIVED, "LICENSE"), "git:" + ARCHIVED + ":LICENSE")
    add("licenses/README.md", b"Clipper2 license: original NuGet 2.0.0 License.txt.\n\n"
        b"NetTopologySuite license and all upstream notices: immutable package repository revision\n"
        b"https://github.com/NetTopologySuite/NetTopologySuite/blob/97d26b92586448b52d5a4f0c060217f94f882c75/License.md\n"
        b"Package copyright: 2006-2025 NetTopologySuite - Team, Diego Guidi, John Diss, Felix Obermaier, Todd Jackson, Joe Amenta.\n"
        b"Census attribution and source terms: each source/data/README.md.\n", "curation metadata")
    add("README.md", README.encode(), "curation metadata")
    local("scripts/verify-alpha-evidence.py", "verify-alpha-evidence.py")
    return experiments, archive


def write_manifest(directory, experiments, archive):
    manifest = {"SchemaVersion": 1, "Kind": "historical-alpha-review-evidence", "NewTimings": False,
                "Repository": "https://github.com/bgtnt/polylinekit", "Experiments": experiments,
                "OriginalIntersectionArchive": archive,
                "Files": [{"Path": name, "Bytes": len(data), "Sha256": sha(data), "Origin": origins[name]}
                          for name, data in sorted(payload.items())]}
    for name, data in payload.items():
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    data = encoded(manifest)
    (directory / "manifest.json").write_bytes(data)
    lines = [f'{row["Sha256"]}  {row["Path"]}' for row in manifest["Files"]]
    lines.append(sha(data) + "  manifest.json")
    (directory / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=REPO / "artifacts/polylinekit-alpha-evidence.zip")
    args = parser.parse_args()
    output = args.output.resolve()
    experiments, archive = collect()
    spec = importlib.util.spec_from_file_location("evidence_verifier", REPO / "scripts/verify-alpha-evidence.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    with tempfile.TemporaryDirectory(prefix="polylinekit-evidence-bundle-") as temp:
        directory = Path(temp)
        write_manifest(directory, experiments, archive)
        verification = verifier.verify(directory, summarize=True)
        add("resummarization.json", encoded(verification), "new verification of original raw evidence; no timings")
        write_manifest(directory, experiments, archive)
        verifier.verify(directory)
        output.parent.mkdir(parents=True, exist_ok=True)
        # Fixed member order, timestamp, attributes and compression make output reproducible.
        with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
            for path in sorted(p for p in directory.rglob("*") if p.is_file()):
                info = zipfile.ZipInfo(path.relative_to(directory).as_posix(), (2026, 9, 25, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.create_system = 3
                info.external_attr = 0o100644 << 16
                zipped.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    result = {"Archive": output.name, "Bytes": output.stat().st_size, "Sha256": sha(output.read_bytes()),
              "PayloadFiles": len(payload), "SummarizedOriginalSamples": sum(e["Samples"] for e in experiments),
              "NewTimings": False, "Published": False}
    output.with_suffix(".json").write_bytes(encoded(result))
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
