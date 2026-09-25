"""Summarize an evented dotnet-trace Speedscope file; no third-party modules."""

import hashlib
import json
import sys
from collections import defaultdict
from pathlib import Path


def summarize(path):
    data = json.loads(path.read_text(encoding="utf-8"))
    frames = [frame["name"] for frame in data["shared"]["frames"]]
    inclusive, exclusive = defaultdict(float), defaultdict(float)
    total = 0.0
    units = set()
    for profile in data["profiles"]:
        if profile["type"] != "evented":
            raise ValueError("Expected dotnet-trace evented Speedscope profiles")
        units.add(profile["unit"])
        stack, previous = [], profile["startValue"]
        for event in profile["events"]:
            duration = event["at"] - previous
            if duration < 0:
                raise ValueError("Profile timestamps are not ordered")
            names = [frames[index] for index in stack]
            if any(".TraverseReal(" in name for name in names):
                total += duration
                for name in set(names):
                    inclusive[name] += duration
                # TraceEvent appends these synthetic leaves; they are not methods.
                methods = [name for name in names if name not in ("CPU_TIME", "UNMANAGED_CODE_TIME")]
                if methods:
                    exclusive[methods[-1]] += duration
            if event["type"] == "O":
                stack.append(event["frame"])
            elif event["type"] != "C" or not stack or stack.pop() != event["frame"]:
                raise ValueError("Profile frames are not properly nested")
            previous = event["at"]
        if stack:
            raise ValueError("Unclosed profile frames")
    if total <= 0 or len(units) != 1:
        raise ValueError("No TraverseReal samples or inconsistent profile units")

    def rows(values):
        return [dict(Frame=name, Time=value, Percent=100 * value / total)
                for name, value in sorted(values.items(), key=lambda item: -item[1])
                if ("GuardedDouble" in name or "ScalarOrderFilter" in name) and "!" in name][:30]

    return dict(Unit=units.pop(), TraverseRealSampledTime=total,
                Inclusive=rows(inclusive), Exclusive=rows(exclusive),
                SpeedscopeSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                Limit="Managed sampled thread time within TraverseReal, including its warmup calls. "
                      "Inclusive shares overlap. JIT inlining affects attribution. "
                      "These are not exact CPU percentages or benchmark timings.")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: profile-summary.py trace.speedscope.json summary.json")
    Path(sys.argv[2]).write_text(json.dumps(summarize(Path(sys.argv[1])), indent=2) + "\n", encoding="utf-8")
