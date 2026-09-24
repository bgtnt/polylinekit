#!/usr/bin/env python3
"""Optional loopback drawing adapter for the existing StrokeTemplates CLI (stdlib only)."""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import math
from pathlib import Path
import subprocess
import uuid

ASSETS = Path(__file__).resolve().parent
MAX_BODY = 1024 * 1024
MAX_POINTS = 10000
METHODS = ("rms", "area", "combined", "protractor", "dtw")


def validate_query(value: object, dataset: str) -> dict:
    if not isinstance(value, dict):
        raise ValueError("Expected a query object.")
    sample_id = value.get("sampleId")
    if not isinstance(sample_id, str) or not sample_id.strip() or len(sample_id) > 128:
        raise ValueError("A query needs a sampleId of at most 128 characters.")
    if (value.get("dataset") != dataset or value.get("split") != "external" or
            value.get("supported") is not True or value.get("label") != ""):
        raise ValueError("Use the selected dataset, split external, supported true and an empty label.")
    strokes = value.get("strokes")
    if not isinstance(strokes, list) or len(strokes) != 1 or not isinstance(strokes[0], list):
        raise ValueError("Draw exactly one continuous stroke. Separate strokes are not joined.")
    points = strokes[0]
    if not 2 <= len(points) <= MAX_POINTS:
        raise ValueError(f"A stroke needs between 2 and {MAX_POINTS} points.")
    previous_time = 0.0
    for point in points:
        if not isinstance(point, dict) or not {"x", "y"} <= point.keys() or point.keys() - {"x", "y", "t"}:
            raise ValueError("Points must contain x, y and optionally t.")
        for axis in ("x", "y"):
            coordinate = point[axis]
            if type(coordinate) not in (int, float) or not math.isfinite(coordinate) or abs(coordinate) > 1000000:
                raise ValueError("Coordinates must be finite numbers within +/-1,000,000.")
        if "t" in point:
            time = point["t"]
            if type(time) not in (int, float) or not math.isfinite(time) or not previous_time <= time <= 86400000:
                raise ValueError("Optional timestamps must be finite, nonnegative and ordered (at most one day).")
            previous_time = time
    if not any((p["x"], p["y"]) != (points[0]["x"], points[0]["y"]) for p in points[1:]):
        raise ValueError("The stroke must have nonzero extent.")
    return {"sampleId": sample_id, "dataset": dataset, "split": "external", "label": "",
            "supported": True, "strokes": strokes}


@dataclass(frozen=True)
class Settings:
    data: Path
    freeze: Path
    consumer: Path
    output: Path
    dataset: str

    @property
    def methods(self) -> tuple[str, ...]:
        return METHODS if self.dataset == "pendigits" else METHODS[:-1]


class CaptureServer(HTTPServer):
    def __init__(self, settings: Settings, port: int = 0):
        self.settings = settings
        self.results: dict[str, Path] = {}
        super().__init__(("127.0.0.1", port), CaptureHandler)
        self.origin = f"http://127.0.0.1:{self.server_port}"
        self.host = f"127.0.0.1:{self.server_port}"


class CaptureHandler(BaseHTTPRequestHandler):
    server: CaptureServer

    def setup(self):
        super().setup()
        self.connection.settimeout(30)

    def reply(self, code: int, content: bytes, content_type: str):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(content)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Content-Security-Policy", "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; "
                         "connect-src 'self'; frame-src 'self'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'")
        self.end_headers()
        self.wfile.write(content)

    def json_reply(self, code: int, value: dict):
        self.reply(code, json.dumps(value, allow_nan=False).encode("utf-8"), "application/json; charset=utf-8")

    def valid_host(self) -> bool:
        if self.headers.get("Host") != self.server.host:
            self.json_reply(403, {"error": "Use the printed loopback URL."})
            return False
        return True

    def do_GET(self):
        if not self.valid_host():
            return
        if self.path == "/config":
            self.json_reply(200, {"dataset": self.server.settings.dataset, "methods": self.server.settings.methods,
                                  "maxPoints": MAX_POINTS})
        elif self.path in ("/", "/draw.js"):
            file = ASSETS / ("draw.html" if self.path == "/" else "draw.js")
            mime = "text/html" if self.path == "/" else "text/javascript"
            self.reply(200, file.read_bytes(), mime + "; charset=utf-8")
        elif self.path in self.server.results:
            self.reply(200, self.server.results[self.path].read_bytes(), "text/html; charset=utf-8")
        else:
            self.json_reply(404, {"error": "Not found."})

    def do_POST(self):
        if not self.valid_host():
            return
        if self.path != "/compare":
            self.json_reply(404, {"error": "Not found."})
            return
        if self.headers.get("Origin") != self.server.origin:
            self.json_reply(403, {"error": "Compare from this server's drawing page."})
            return
        if self.headers.get_content_type() != "application/json" or self.headers.get("Transfer-Encoding"):
            self.json_reply(415, {"error": "Send JSON with a Content-Length."})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if not 0 < length <= MAX_BODY:
            self.json_reply(413, {"error": "Request is empty or exceeds 1 MiB."})
            return
        try:
            raw = self.rfile.read(length)
            if len(raw) != length:
                raise ValueError("Incomplete JSON request.")
            body = json.loads(raw)
            if not isinstance(body, dict) or body.get("method") not in self.server.settings.methods:
                raise ValueError("Select one of the available scorers.")
            query = validate_query(body.get("query"), self.server.settings.dataset)
        except (ValueError, OverflowError) as error:
            self.json_reply(400, {"error": str(error)})
            return
        settings = self.server.settings
        request_id = uuid.uuid4().hex
        directory = settings.output / request_id
        try:
            directory.mkdir(parents=True, exist_ok=False)
            query_path, result_path = directory / "query.json", directory / "result.html"
            query_path.write_text(json.dumps(query, indent=2, allow_nan=False) + "\n", encoding="utf-8")
            completed = subprocess.run([
                "dotnet", str(settings.consumer), "--data", str(settings.data), "--freeze", str(settings.freeze),
                "--query", str(query_path), "--seed", "1729", "--method", body["method"],
                "--output", str(result_path), "--contours"], capture_output=True, text=True,
                encoding="utf-8", errors="replace", timeout=30, check=False)
            if completed.returncode != 0:
                self.json_reply(422, {"error": completed.stderr.strip() or "The consumer rejected this query."})
                return
            if not result_path.is_file():
                raise OSError("The consumer did not produce an inspection page.")
            route = f"/result/{request_id}"
            self.server.results[route] = result_path
            self.json_reply(200, {"resultUrl": route, "summary": completed.stdout.strip()})
        except subprocess.TimeoutExpired:
            self.json_reply(504, {"error": "Comparison exceeded 30 seconds. The captured query remains on disk."})
        except OSError as error:
            self.json_reply(500, {"error": f"Local consumer could not run: {error}"})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--freeze", type=Path, required=True)
    parser.add_argument("--dataset", choices=("pendigits", "dollar"), default="pendigits")
    parser.add_argument("--consumer", type=Path, default=ASSETS / "bin/Release/net10.0/StrokeTemplates.dll")
    parser.add_argument("--output", type=Path, default=Path("artifacts/stroke-input"))
    parser.add_argument("--port", type=int, default=0)
    args = parser.parse_args()
    for path in (args.consumer, args.freeze, args.data / (args.dataset + ".jsonl")):
        if not path.is_file():
            parser.error(f"Missing {path}; build the consumer and import the dataset first.")
    if not 0 <= args.port <= 65535:
        parser.error("Port must be between 0 and 65535; 0 chooses an available port.")
    settings = Settings(args.data.resolve(), args.freeze.resolve(), args.consumer.resolve(), args.output.resolve(), args.dataset)
    with CaptureServer(settings, args.port) as server:
        print(f"Draw a stroke: {server.origin}/", flush=True)
        print(f"Captures/results: {settings.output}\nLoopback only. Stop with Ctrl+C.", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
