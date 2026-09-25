#!/usr/bin/env python3
"""Verify an extracted historical evidence bundle; optionally re-summarize, never time."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify(root, summarize=False):
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    expected = {entry["Path"] for entry in manifest["Files"]}
    actual = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
    if actual != expected | {"manifest.json", "SHA256SUMS"}:
        raise ValueError("Bundle file inventory differs from the explicit manifest.")
    for entry in manifest["Files"]:
        relative = Path(entry["Path"])
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError("Unsafe manifest path.")
        data = (root / relative).read_bytes()
        if len(data) != entry["Bytes"] or digest(data) != entry["Sha256"]:
            raise ValueError("Checksum mismatch: " + entry["Path"])
    checksum_lines = [f'{e["Sha256"]}  {e["Path"]}' for e in manifest["Files"]]
    checksum_lines.append(f'{digest((root / "manifest.json").read_bytes())}  manifest.json')
    if (root / "SHA256SUMS").read_text(encoding="utf-8").splitlines() != checksum_lines:
        raise ValueError("SHA256SUMS differs from manifest and payload.")
    results = []
    if summarize:
        # Summarizers write only to temporary copies. Original raw evidence stays untouched.
        with tempfile.TemporaryDirectory(prefix="polylinekit-evidence-") as temp:
            for experiment in manifest["Experiments"]:
                name = experiment["Name"]
                copied = Path(temp) / name
                shutil.copytree(root / name / "raw", copied)
                command = ["dotnet", str(root / name / "bin" / experiment["Runner"]),
                           experiment["SummarizeCommand"], str(copied)]
                result = subprocess.run(command, capture_output=True, text=True, check=True)
                for filename in experiment["GeneratedFiles"]:
                    if (copied / filename).read_bytes() != (root / name / "raw" / filename).read_bytes():
                        raise ValueError(name + ": regenerated file differs: " + filename)
                results.append({"Name": name, "ExitCode": result.returncode,
                                "ByteIdenticalReports": experiment["GeneratedFiles"],
                                "Console": result.stdout.replace("\r\n", "\n")})
    return {"VerifiedFiles": len(manifest["Files"]), "Summarizations": results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Extracted evidence bundle root")
    parser.add_argument("--summarize", action="store_true", help="Run the original frozen .NET summarizers")
    args = parser.parse_args()
    print(json.dumps(verify(args.directory.resolve(), args.summarize), indent=2))
