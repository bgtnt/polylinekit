import http.client
import json
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest
from unittest.mock import patch

from serve import CaptureServer, MAX_BODY, MAX_POINTS, Settings, validate_query


def query():
    return {"sampleId": "external-test", "dataset": "pendigits", "split": "external", "label": "",
            "supported": True, "strokes": [[{"x": 0, "y": 0, "t": 0}, {"x": 10, "y": 20, "t": 5}]]}


class QueryChecks(unittest.TestCase):
    def test_preserves_point_order_coordinates_and_timestamps(self):
        value = query()
        value["strokes"][0].append({"x": -2.25, "y": 4, "t": 12})
        self.assertEqual(value, validate_query(value, "pendigits"))

    def test_rejects_multiple_strokes_empty_and_zero_extent(self):
        for strokes in ([], [[], []], [[]], [[{"x": 1, "y": 1}]], [[{"x": 1, "y": 1}] * 2]):
            with self.subTest(strokes=strokes), self.assertRaises(ValueError):
                validate_query({**query(), "strokes": strokes}, "pendigits")

    def test_rejects_invalid_numbers_and_times(self):
        for point in ({"x": True, "y": 1}, {"x": float("nan"), "y": 1},
                      {"x": 1, "y": float("inf")}, {"x": 1e7, "y": 0},
                      {"x": "3", "y": 0}, {"x": 3, "y": 0, "t": -1},
                      {"x": 3, "y": 0, "t": False}, {"x": 3, "y": 0, "t": 86400001}):
            with self.subTest(point=point), self.assertRaises(ValueError):
                validate_query({**query(), "strokes": [[{"x": 0, "y": 0}, point]]}, "pendigits")
        with self.assertRaises(ValueError):
            validate_query({**query(), "strokes": [[{"x": 0, "y": 0, "t": 5}, {"x": 2, "y": 0, "t": 4}]]}, "pendigits")

    def test_limits_points_and_requires_external_metadata(self):
        with self.assertRaises(ValueError):
            validate_query({**query(), "strokes": [[{"x": 1, "y": 2}] * (MAX_POINTS + 1)]}, "pendigits")
        for field, value in (("dataset", "synthetic"), ("split", "test"), ("label", "1"),
                             ("supported", False), ("sampleId", "")):
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate_query({**query(), field: value}, "pendigits")


class HttpChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.server = CaptureServer(Settings(root / "data", root / "frozen.json", root / "consumer.dll", root / "output", "pendigits"))
        self.thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": .01}, daemon=True)
        self.thread.start()
        self.log_patch = patch("serve.CaptureHandler.log_message")
        self.log_patch.start()

    def tearDown(self):
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()
        self.log_patch.stop()
        self.temp.cleanup()

    def request(self, method="POST", path="/compare", body=None, headers=None):
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=3)
        try:
            default_headers = {"Content-Type": "application/json", "Origin": self.server.origin}
            default_headers.update(headers or {})
            if body is None:
                body = json.dumps({"query": query(), "method": "rms"})
            connection.request(method, path, body if method == "POST" else None, default_headers)
            response = connection.getresponse()
            return response.status, response.read(), dict(response.getheaders())
        finally:
            connection.close()

    def test_serves_assets_and_only_selected_bank_methods(self):
        for path in ("/", "/draw.js", "/config"):
            code, body, headers = self.request("GET", path)
            self.assertEqual(200, code)
            self.assertTrue(body)
            self.assertEqual("no-store", headers["Cache-Control"])
        self.assertEqual(5, len(json.loads(body)["methods"]))
        self.assertNotIn("dtw", Settings(Path(), Path(), Path(), Path(), "dollar").methods)

    def test_rejects_cross_origin_and_rebound_host_without_running_consumer(self):
        with patch("serve.subprocess.run") as run:
            for headers in ({"Origin": "https://example.com"}, {"Origin": "null"}, {"Host": "example.com"}):
                self.assertEqual(403, self.request(headers=headers)[0])
            run.assert_not_called()

    def test_rejects_oversized_malformed_and_non_json_requests(self):
        with patch("serve.subprocess.run") as run:
            self.assertEqual(413, self.request(body="", headers={"Content-Length": str(MAX_BODY + 1)})[0])
            self.assertEqual(400, self.request(body="{broken")[0])
            self.assertEqual(415, self.request(headers={"Content-Type": "text/plain"})[0])
            # Negotiate unsupported framing using headers only; do not send an
            # unframed body while pretending it is a chunked HTTP stream.
            self.assertEqual(415, self.request(body="", headers={"Transfer-Encoding": "chunked"})[0])
            run.assert_not_called()

    def test_rejects_unknown_method_and_multi_stroke_before_writing(self):
        with patch("serve.subprocess.run") as run:
            self.assertEqual(400, self.request(body=json.dumps({"query": query(), "method": "rms; command"}))[0])
            value = query()
            value["strokes"].append(value["strokes"][0])
            self.assertEqual(400, self.request(body=json.dumps({"query": value, "method": "rms"}))[0])
            run.assert_not_called()
            self.assertFalse(self.server.settings.output.exists())

    def test_large_rejected_bodies_deliver_complete_errors_without_running_consumer(self):
        # Previously reproducible as WinError 10053: early rejection closed the
        # socket with the client's body still in flight, losing the JSON response.
        cases = (("/compare", {"Content-Type": "text/plain"}, 415),
                 ("/compare", {"Origin": "https://example.com"}, 403),
                 ("/compare", {"Host": "example.com"}, 403),
                 ("/unknown", {}, 404),
                 ("/compare", {"Transfer-Encoding": "chunked", "Content-Length": str(MAX_BODY)}, 415))
        with patch("serve.subprocess.run") as run:
            for path, headers, expected in cases:
                with self.subTest(path=path, headers=headers):
                    code, raw, response_headers = self.request(path=path, body="x" * MAX_BODY, headers=headers)
                    self.assertEqual(expected, code)
                    self.assertEqual(len(raw), int(response_headers["Content-Length"]))
                    self.assertTrue(json.loads(raw)["error"])
            run.assert_not_called()
            self.assertFalse(self.server.settings.output.exists())

    def test_runs_cli_without_shell_and_retains_exact_query_and_result(self):
        def execute(args, **kwargs):
            self.assertNotIn("shell", kwargs)
            self.assertEqual(30, kwargs["timeout"])
            self.assertEqual("dotnet", args[0])
            self.assertEqual("1729", args[args.index("--seed") + 1])
            self.assertEqual("rms", args[args.index("--method") + 1])
            self.assertEqual(query(), json.loads(Path(args[args.index("--query") + 1]).read_text()))
            Path(args[args.index("--output") + 1]).write_text("<html>inspection</html>", encoding="utf-8")
            return subprocess.CompletedProcess(args, 0, "ranking", "")
        with patch("serve.subprocess.run", side_effect=execute) as run:
            code, raw, _ = self.request()
            self.assertEqual(200, code)
            route = json.loads(raw)["resultUrl"]
            self.assertEqual((200, b"<html>inspection</html>"), self.request("GET", route)[:2])
            self.assertEqual(404, self.request("GET", "/../frozen.json")[0])
            self.assertEqual(404, self.request("GET", "/result/unknown")[0])
            self.assertEqual(1, run.call_count)
            self.assertEqual(1, len(list(self.server.settings.output.glob("*/query.json"))))

    def test_two_requests_keep_separate_captures(self):
        def execute(args, **kwargs):
            Path(args[args.index("--output") + 1]).write_text("ok")
            return subprocess.CompletedProcess(args, 0, "", "")
        with patch("serve.subprocess.run", side_effect=execute):
            first = json.loads(self.request()[1])["resultUrl"]
            second = json.loads(self.request()[1])["resultUrl"]
        self.assertNotEqual(first, second)
        self.assertEqual(2, len(list(self.server.settings.output.glob("*/query.json"))))

    def test_consumer_rejection_does_not_publish_result(self):
        with patch("serve.subprocess.run", return_value=subprocess.CompletedProcess([], 1, "", "Rejected input")):
            code, raw, _ = self.request()
            self.assertEqual(422, code)
            self.assertEqual("Rejected input", json.loads(raw)["error"])
            self.assertFalse(self.server.results)

    def test_timeout_and_missing_dotnet_are_reported(self):
        for failure, expected in ((subprocess.TimeoutExpired("dotnet", 30), 504), (FileNotFoundError("dotnet"), 500)):
            with self.subTest(failure=failure), patch("serve.subprocess.run", side_effect=failure):
                self.assertEqual(expected, self.request()[0])
        self.assertFalse(self.server.results)


if __name__ == "__main__":
    unittest.main()
