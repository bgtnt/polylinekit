#!/usr/bin/env python3
"""Bundle the original paired prepared-region evidence; no build, download or timing.

The original ignored artifacts are required. Both measured source snapshots and
frozen .NET summarizers are preserved. The ZIP has a fixed allowlist, timestamps,
ordering and file attributes, and is written only after independent Python and
frozen .NET re-summarization succeeds. Existing output is never overwritten.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import zipfile

REPO = Path(__file__).resolve().parents[1]
ORIGINAL = REPO / "artifacts/paired-ring-buckets-coordinated-20260928"
REPORT_REVISION = "bf02523724d4677e03c6b8bf259d8a20ef5b23e4"
REVISIONS = {"baseline": "70291d2c0db4898d15e601eb11a972895338dafa",
             "candidate": "6de6d38e9c86f5402d77346531a5b3917256e7c7"}
RAW_HASHES = {
    "baseline-run-1.json": "ae591e66ebf3df086c2cd07927599e26b5c2bdae80ae74d2adb2ce3eb596c152",
    "baseline-run-2.json": "568f5a0fb68e469fd561889b5a5d901b771065b447bfb89db55793a8d1c4f1a3",
    "baseline-run-3.json": "245f831474b47453395b90b5c7d4dc8a54fa7757321912b0426d2f2fdf1e1f85",
    "candidate-run-1.json": "16ec201dac29762862e1879734317ecd034c5b522b16b5a538a99c664691a816",
    "candidate-run-2.json": "a473c1968b270d7fbd51333ee02ca88bc5b5bd133d49545fd2188006890824ba",
    "candidate-run-3.json": "7686ea3bfbda18045679d06d9316052d54b595d9aef8eb7d4deed94d46ee5ff7",
    "baseline-summary.json": "f27717a83a89c8da5491e5d05e3ce4598802de64589304214381c1efe123b561",
    "candidate-summary.json": "abc4e49b89171040cf903894c4a2fde215b250be91fc02169d518e1dac643e56",
    "comparison.json": "f57279d04a35006b62f0dc34e3935e1d244896ecc9e32577570b96313e21cc58",
    "execution.json": "7d5cb5f7e56956322203ec7a34908b2ddd6123a66beaa4d2c5d96bdf634862c2",
    "baseline-source.zip": "f689bf16e3df8542f5491f0992ea19ec72da224c13be6765e2ee02621c667729",
    "candidate-source.zip": "2777ac79c78e8701e80e515a646342b6f3e4cb4dd89c45d59140af62c790ddf8",
    "Run-HostBenchmark.ps1": "c3c7b888f10c3d86bd7f4572d4c21cb44cc3f4688a313c1a4d2285d1d9af5b69",
}
CORE_FILES = (
    "AffineTransform2D.cs", "Bounds2D.cs", "FilledAreaSelector.cs", "IntegerFilledAreaSweep.cs",
    "MultiRingArea.cs", "PathFillRule.cs", "PathInput.cs", "Point2.cs", "PolylineAlignment.cs",
    "PolylineArea.cs", "PolylineKit.Core.csproj", "PolylineNormalization.cs", "PolylineSampling.cs",
    "PreparedRegion.cs", "PreparedSimpleSweep.cs", "README.md", "RegionArea.cs", "RegionOverlapResult.cs",
    "RobustOrientation.cs", "SimdTransforms.cs", "WindingArea.cs", "WindingEngine.RingGroups.cs",
    "WindingEngine.Rings.cs", "WindingEngine.cs", "WindingInput.cs", "packages.lock.json")
HARNESS_FILES = (
    "AnalyticChecks.cs", "GisChecks.cs", "PROTOCOL.md", "PolylineKit.MultiRingChecks.csproj",
    "PreparedRegionChecks.cs", "Program.cs", "README.md", "RESULTS.md", "RegionBenchmarks.cs",
    "RingGroupingChecks.cs", "RingPartitionChecks.cs", "compare-grouping.ps1", "packages.lock.json", "run.ps1")
INPUT_FILES = ("PROTOCOL.md", "README.md", "freeze.py", "manifest.json", "queries.json", "zones.json",
               "source/nc-counties-count.json", "source/nc-counties-layer.json", "source/nc-counties.geojson",
               "source/nc-districts-count.json", "source/nc-districts-layer.json", "source/nc-districts.geojson")
SOURCE_FILES = {"Directory.Build.props", "global.json", "LICENSE"} | {
    "src/PolylineKit.Core/" + name for name in CORE_FILES} | {
    "tests/PolylineKit.MultiRingChecks/" + name for name in HARNESS_FILES} | {
    "examples/RegionCoverage/data/" + name for name in INPUT_FILES}
PROCESS_ORDER = ("baseline-1", "candidate-1", "candidate-2", "baseline-2", "baseline-3", "candidate-3")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def checked(data, expected, label):
    if sha(data) != expected:
        raise ValueError("Original checksum differs: " + label)
    return data


README = """# Original paired prepared-region evidence

This archive preserves the complete measured comparison from 2026-09-28:
baseline `70291d2c0db4898d15e601eb11a972895338dafa` and candidate
`6de6d38e9c86f5402d77346531a5b3917256e7c7`. It contains **all 2,700 original
samples**, including all slower cases, preparation costs and allocations.
The final report is the unmodified `bf02523` report under `reports/`.
These historical binaries are not the subsequently packed alpha binaries.
No new timing, download, build or NuGet restore was performed to assemble or
verify this archive. Publication does not turn the observations into guarantees.

## Verify without this repository

Compare the ZIP's SHA-256 with the separately published release checksum before
extraction. Python 3 uses only its standard library and independently recalculates
all 180 summary cells from the six original files. It also checks the complete file
inventory, checksums, fixed accuracy budgets, paired identities, both measured source
snapshots and the six nonoverlapping process windows inside the shared-host slot.

```text
python verify-prepared-region-evidence.py .
```

With the .NET 10 runtime installed, also invoke both unchanged frozen summarizers:

```text
python verify-prepared-region-evidence.py . --summarize
```

The second command works from an extracted archive outside a repository. It uses
temporary copies and requires byte-identical original summaries; it never runs the
benchmark entry point. It requires neither Git, SDK, network nor input downloads.
The full measured sources and original input snapshots are included under each
`source/` directory. Their benchmark protocols contain commands to take **new**
measurements; those commands are distinct from verification above. A new build may
need the original full Git revision and an SDK, unlike re-summarization.

## Interpretation

The unchanged protocol covers ten scenarios, three backends, three scopes and
five batches in each of three independent processes per revision. Each process
rotates backend order. All builds finished before timing. `coordination/` retains
the original paired revision metadata, execution result and six external windows.
The shared helper excludes cooperating workloads, not all background activity.
The earlier pilot and uncoordinated series are deliberately excluded; they are
not substituted for any process or row in this coordinated series.

On the complete frozen Census layers, candidate Core used 30–33% less warm time
and 24–25% less preparation-plus-one time than the measured Clipper adapter.
At 1,024 generated rings per operand, Core still used 18–34% **more** warm time
and 46–97% **more** preparation-plus-one time. The one 16-ring hole warm case is
close to parity; Clipper is faster on all other generated warm cases. The bucket
change reduced all eight generated Core warm medians by 6.3–9.9%, while GIS was
essentially unchanged. Process ranges are not confidence intervals.

Comparators are Clipper2 **C# 2.0.0**, with reusable inputs/engine and a `1e-6`
grid, and NTS **2.6.0** `OverlayNGRobust`. Core returns numbers on the original
translated doubles; comparators construct output geometry. This is not a C++
measurement or a comparison of every NTS strategy. Preparation-plus-one is a
fresh session with already warm process/JIT/workspaces, not cold-process timing.
Warm managed-thread 0 B observations exclude retained storage and first use.
The Census layers are the complete population of an already observed dataset,
not an independent holdout; generated collections stress ring count, not dense
crossing ring sets. Exact fixture agreements are not exact-arithmetic guarantees.

## Provenance and privacy

`manifest.json` and `SHA256SUMS` identify every allowlisted file. Original raw JSON,
summaries, source contents and DLLs are unchanged. Two candidate lockfiles use LF
in the original Git archive but CRLF in the measured checkout; the verifier checks
the explicitly recorded reversible line-ending difference. No numeric value is
redacted or recomputed in original files. `resummarization.json` and
`negative-controls.json` are new verification records, not new measurements.

Only two text files are privacy-redacted: the host log's two repository-directory
references and the host helper's example absolute path. `coordination/provenance.json`
records original and redacted hashes, sizes, replacement counts and recipes.
The log timestamps and helper code remain unchanged. Private attachments, account
settings, credentials, PDBs, EXEs and unrelated local files are excluded.
Unmodified DLL CodeView records retain original build paths for this public
repository and the upstream Clipper publisher. Removing them would change the
measured binary identities; their presence is disclosed, not silently altered.

## Attribution

PolylineKit is MIT; Clipper2 2.0.0 is Boost 1.0; NetTopologySuite 2.6.0 is
BSD-3-Clause with upstream notices. License copies are under `licenses/`.
The U.S. Census Bureau TIGERweb data provenance, original queries, source terms
and all six original response snapshots are retained under each `source/`.
See `reports/THIRD-PARTY-NOTICES.md` and each `source/examples/RegionCoverage/data/README.md`.
The old reports inside the measured source snapshots predate the final report;
`reports/RESULTS.md` is the final assessment of this exact coordinated series.
"""


class Bundle:
    def __init__(self, verifier):
        self.payload = {}
        self.origins = {}
        self.verifier = verifier

    def add(self, name, data, origin):
        self.verifier.safe_path(name)
        if name in self.payload or name.endswith((".exe", ".pdb")):
            raise ValueError("Duplicate or excluded archive member: " + name)
        if re.search(rb"MPR001|\.codex[\\/]|\.codex-remote-attachments|gh[pousr]_[A-Za-z0-9]{20}", data, re.I):
            raise ValueError("Private workspace marker or credential-shaped token: " + name)
        if not name.endswith(".dll") and re.search(rb"[A-Za-z]:[\\/]+Users[\\/]", data, re.I):
            raise ValueError("Personal absolute path in text: " + name)
        self.payload[name], self.origins[name] = data, origin

    def local(self, filename, destination, expected=None):
        data = (ORIGINAL / filename).read_bytes()
        if expected is not None:
            checked(data, expected, filename)
        self.add(destination, data, "artifacts/paired-ring-buckets-coordinated-20260928/" + filename)
        return data

    def collect(self):
        experiments = []
        for side, revision in REVISIONS.items():
            for run in (1, 2, 3):
                filename = f"{side}-run-{run}.json"
                self.local(filename, f"{side}/raw/{filename}", RAW_HASHES[filename])
            filename = f"{side}-summary.json"
            self.local(filename, f"{side}/raw/{filename}", RAW_HASHES[filename])
            raw = json.loads(self.payload[f"{side}/raw/{side}-run-1.json"])
            for name, expected in raw["BinarySha256"].items():
                if name not in ("PolylineKit.Experiments", "PolylineKit.Winding", "Clipper2Lib", "NetTopologySuite"):
                    raise ValueError("Unexpected measured binary.")
                self.local(f"{side}-runner/{name}.dll", f"{side}/bin/{name}.dll", expected)
            for suffix in (".deps.json", ".runtimeconfig.json"):
                self.local(f"{side}-runner/PolylineKit.Experiments{suffix}", f"{side}/bin/PolylineKit.Experiments{suffix}")
            original_zip = ORIGINAL / f"{side}-source.zip"
            checked(original_zip.read_bytes(), RAW_HASHES[original_zip.name], original_zip.name)
            with zipfile.ZipFile(original_zip) as source:
                files = [entry.filename for entry in source.infolist() if not entry.is_dir()]
                if len(files) != len(set(files)) or set(files) != SOURCE_FILES:
                    raise ValueError("Original source ZIP differs from the explicit member allowlist.")
                for name in sorted(SOURCE_FILES):
                    self.add(f"{side}/source/{name}", source.read(name), "git:" + revision + ":" + name)
            experiments.append({"Name": side, "Revision": revision, "Samples": 1350,
                                "OriginalSourceArchiveSha256": RAW_HASHES[original_zip.name],
                                "CheckoutCrlfArchiveLf": [] if side == "baseline" else [
                                    "src/PolylineKit.Core/packages.lock.json",
                                    "tests/PolylineKit.MultiRingChecks/packages.lock.json"]})
        for name in ("comparison.json", "execution.json"):
            self.local(name, "coordination/" + name, RAW_HASHES[name])
        for sequence, label in enumerate(PROCESS_ORDER, 1):
            for suffix in ("start", "window"):
                name = f"{sequence}-{label}-{suffix}.json"
                self.local(name, "coordination/" + name)
        redactions = []
        host = (REPO / "artifacts/ring-buckets-coordinated-host.log").read_bytes()
        checked(host, "6e889667f82c035b5b6b1d1f732a5fbc18aeac56da48680057f95bef7f9b5443", "host log")
        helper = checked((ORIGINAL / "Run-HostBenchmark.ps1").read_bytes(), RAW_HASHES["Run-HostBenchmark.ps1"], "host helper")
        for name, original, count, pattern, replacement, recipe in (
            ("host-log.redacted.txt", host, 2,
             rb"[A-Za-z]:\\Users\\[^\r\n]+?\\source\\repos\\polylinekit\\artifacts\\paired-ring-buckets-coordinated-20260928",
             b"artifacts/paired-ring-buckets-coordinated-20260928", "Replace exactly two repository-directory references by the relative artifact label; preserve all other bytes."),
            ("Run-HostBenchmark.redacted.ps1", helper, 1,
             rb"[A-Za-z]:\\Users\\[^\r\n]+?\\source\\repos\\tools\\benchmarks\\Run-HostBenchmark\.ps1",
             b"./Run-HostBenchmark.ps1", "Replace exactly one absolute example helper path by ./Run-HostBenchmark.ps1; preserve all other bytes.")):
            redacted, replacements = re.subn(pattern, replacement, original)
            if replacements != count:
                raise ValueError("Unexpected privacy-redaction count: " + name)
            self.add("coordination/" + name, redacted, "privacy-redacted original; see coordination/provenance.json")
            redactions.append({"PublishedFile": name, "OriginalSha256": sha(original), "OriginalBytes": len(original),
                               "RedactedSha256": sha(redacted), "RedactedBytes": len(redacted),
                               "Replacements": count, "Recipe": recipe})
        self.add("coordination/provenance.json", encoded({"NewTimings": False, "NumericValuesChanged": False,
                                                          "Redactions": redactions}), "curation metadata")
        for original, destination in (("tests/PolylineKit.MultiRingChecks/RESULTS.md", "reports/RESULTS.md"),
                                      ("docs/performance.md", "reports/performance.md"),
                                      ("THIRD-PARTY-NOTICES.md", "reports/THIRD-PARTY-NOTICES.md"),
                                      ("LICENSE", "licenses/PolylineKit-MIT.txt")):
            data = subprocess.check_output(["git", "show", REPORT_REVISION + ":" + original], cwd=REPO)
            self.add(destination, data, "git:" + REPORT_REVISION + ":" + original)
        for name, expected in (("Clipper2-2.0.0.txt", "36266a8fd073568394cb81cdb2b124f7fdae2c64c1a7ed09db34b4d22efa2951"),
                               ("NetTopologySuite-2.6.0.md", "81be2ef7f10ff8f27339062bf1a9182c1f8b3a4fe7882699fc328a1a8f8b2693")):
            data = checked((REPO / "scripts/evidence-licenses" / name).read_bytes(), expected, name)
            self.add("licenses/" + name, data, "scripts/evidence-licenses/" + name)
        self.add("README.md", README.encode("utf-8"), "curation metadata")
        for name in ("verify-prepared-region-evidence.py",):
            self.add(name, (REPO / "scripts" / name).read_bytes(), "scripts/" + name)
        return experiments

    def write(self, directory, experiments):
        manifest = {"SchemaVersion": 1, "Kind": "paired-prepared-region-evidence", "NewTimings": False,
                    "Repository": "https://github.com/bgtnt/polylinekit", "ReportRevision": REPORT_REVISION,
                    "Experiments": experiments,
                    "Files": [{"Path": name, "Bytes": len(data), "Sha256": sha(data), "Origin": self.origins[name]}
                              for name, data in sorted(self.payload.items())]}
        for name, data in self.payload.items():
            path = directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        data = encoded(manifest)
        (directory / "manifest.json").write_bytes(data)
        lines = [f'{entry["Sha256"]}  {entry["Path"]}' for entry in manifest["Files"]]
        lines.append(sha(data) + "  manifest.json")
        (directory / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def negative_controls(directory, verifier):
    """Check that publication mistakes and semantically malformed evidence fail closed."""
    controls = []
    with tempfile.TemporaryDirectory(prefix="polylinekit-prepared-controls-") as temp:
        copied = Path(temp) / "bundle"
        for label in ("changed-payload", "unexpected-file", "missing-file", "unsafe-path", "duplicate-path"):
            if copied.exists():
                shutil.rmtree(copied)
            shutil.copytree(directory, copied)
            raw = copied / "candidate/raw/candidate-run-1.json"
            if label == "changed-payload":
                raw.write_bytes(raw.read_bytes() + b" ")
            elif label == "unexpected-file":
                (copied / "unexpected.txt").write_text("unexpected", encoding="utf-8")
            elif label == "missing-file":
                raw.unlink()
            else:
                manifest = json.loads((copied / "manifest.json").read_bytes())
                if label == "unsafe-path":
                    manifest["Files"][0]["Path"] = "../outside.json"
                else:
                    manifest["Files"].append(manifest["Files"][0])
                (copied / "manifest.json").write_bytes(encoded(manifest))
            try:
                verifier.verify(copied)
            except ValueError:
                controls.append({"Control": label, "Rejected": True})
            else:
                raise ValueError("Malformed evidence accepted: " + label)
        # Integrity checks must not be the only reason a wrong sample median is rejected.
        if copied.exists():
            shutil.rmtree(copied)
        shutil.copytree(directory, copied)
        raw = copied / "candidate/raw/candidate-run-1.json"
        parsed = json.loads(raw.read_bytes())
        parsed["Measurements"][0]["MedianNanoseconds"] += 1
        raw.write_bytes(encoded(parsed))
        try:
            verifier.validate_experiment(copied, {"Name": "candidate", "Revision": REVISIONS["candidate"],
                "CheckoutCrlfArchiveLf": ["src/PolylineKit.Core/packages.lock.json", "tests/PolylineKit.MultiRingChecks/packages.lock.json"]})
        except ValueError:
            controls.append({"Control": "changed-sample-median-independent-of-file-hash", "Rejected": True})
        else:
            raise ValueError("Wrong sample median accepted.")
    return {"NewTimings": False, "Controls": controls}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=REPO / "artifacts/polylinekit-prepared-region-evidence.zip")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() or output.with_suffix(".json").exists():
        raise ValueError("Output already exists; choose a new path. Nothing is overwritten.")
    spec = importlib.util.spec_from_file_location("prepared_evidence_verifier", REPO / "scripts/verify-prepared-region-evidence.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    bundle = Bundle(verifier)
    experiments = bundle.collect()
    with tempfile.TemporaryDirectory(prefix="polylinekit-prepared-bundle-") as temp:
        directory = Path(temp)
        bundle.write(directory, experiments)
        verification = verifier.verify(directory, summarize=True)
        bundle.add("resummarization.json", encoded(verification), "new verification of original raw evidence; no timings")
        bundle.add("negative-controls.json", encoded(negative_controls(directory, verifier)), "new malformed-evidence verification; no timings")
        bundle.write(directory, experiments)
        final = verifier.verify(directory)
        output.parent.mkdir(parents=True, exist_ok=True)
        with output.open("xb") as stream, zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for path in sorted(p for p in directory.rglob("*") if p.is_file()):
                info = zipfile.ZipInfo(path.relative_to(directory).as_posix(), (2026, 9, 28, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.create_system = 3
                info.external_attr = 0o100644 << 16
                archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    result = {"Archive": output.name, "Bytes": output.stat().st_size, "Sha256": sha(output.read_bytes()),
              "PayloadFiles": final["VerifiedFiles"], "ZipMembers": final["VerifiedFiles"] + 2,
              "SummarizedOriginalSamples": 2700, "IndependentSummaryCells": 180,
              "NewTimings": False, "Published": False}
    output.with_suffix(".json").write_bytes(encoded(result))
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
