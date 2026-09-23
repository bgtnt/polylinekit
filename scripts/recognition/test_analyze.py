"""Hand-constructed outcomes only; no held-out classification is run by these tests."""
import copy
import gzip
import json
import math
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
import analyze


def prediction(sample="q", method="rms", status="ok", label="a", guessed="a"):
    row = {"dataset": "dollar", "seed": 1729, "sampleId": sample, "writerId": "s02", "trueLabel": label,
           "method": method, "status": status}
    if status == "ok":
        row.update(predictedLabel=guessed, templateId="template", score=.1, rms=.2, area=.3, rotationDegrees=0, margin=.02)
    else:
        row["error"] = "synthetic reason"
    return row


def writer_fixture():
    data, rows = {}, {}
    for number in range(2, 12):
        writer = f"s{number:02d}"
        for label in ("a", "b"):
            identifier = f"dollar/{writer}/{label}"
            data[identifier] = {"sampleId": identifier, "dataset": "dollar", "split": "main", "writerId": writer,
                                "label": label, "supported": True, "speed": "slow" if label == "a" else "fast"}
    for sample in data.values():
        number = int(sample["writerId"][1:])
        other_writer = f"s{2 if number == 11 else number+1:02d}"
        for seed in analyze.SEEDS:
            for method in analyze.METHODS["dollar"]:
                guessed = sample["label"] if method == "combined" or number < 7 else ("a" if sample["label"] == "b" else "b")
                row = prediction(sample["sampleId"], method, label=sample["label"], guessed=guessed)
                row.update(writerId=sample["writerId"], seed=seed, templateId=f"dollar/{other_writer}/{guessed}")
                rows[analyze.row_key(row)] = row
    return data, rows


def performance_fixture():
    quality, metadata = {}, {}
    cases = []
    for dataset in analyze.METHODS:
        metadata[dataset] = {"freezeSha256": "frozen", "inputSha256": dataset + "-input", "configuration": {"dataset": dataset}}
        for seed in analyze.SEEDS:
            for method in analyze.METHODS[dataset]:
                row = prediction(dataset + "/q", method)
                row.update(dataset=dataset, seed=seed)
                if dataset == "pendigits":
                    row.pop("writerId")
                    excluded = prediction(dataset + "/u", method, "unsupported")
                    excluded.update(dataset=dataset, seed=seed)
                    excluded.pop("writerId")
                    quality[analyze.row_key(excluded)] = excluded
                quality[analyze.row_key(row)] = row
                query = {key: row[key] for key in ("sampleId", "status", "predictedLabel", "templateId", "score")}
                query.update(elapsedTicks=1, allocatedBytes=100)
                cases.append({"dataset": dataset, "seed": seed, "method": method,
                    "heldOutWriter": "s02" if dataset == "dollar" else None, "templateIds": ["template"], "templateCount": 1,
                    "officialQueryCount": 1 if dataset == "dollar" else 2, "supportedQueryCount": 1,
                    "unsupportedQueryCount": 0 if dataset == "dollar" else 1, "configuration": {"dataset": dataset},
                    "sharedSamples": 64, "nativeSamples": 0, "warmupSequence": ["development/q"],
                    "warmupQueries": 100, "warmupMilliseconds": 3000, "failures": 0,
                    "p50Microseconds": 1000, "p95Microseconds": 1000, "medianBytesPerQuery": 100, "meanBytesPerQuery": 100,
                    "bankConstructionMilliseconds": 1, "bankConstructionAllocatedBytes": 1000,
                    "retainedPreparedBankBytes": -5, "totalProcessingMilliseconds": 2, "measuredGcCollections": [0, 0, 0],
                    "scoringOnly": {"nanosecondsPerQuery": 50, "bytesPerQuery": 0}, "queries": [query]})
    runs = []
    for variant in analyze.VARIANTS:
        for number in (1, 2, 3):
            measurements = copy.deepcopy(cases)
            for case in measurements:
                case["variant"] = variant
                case["retainedPreparedBankBytes"] = (number - 2) * 5 - 5
            runs.append({"variant": variant, "run": number, "forceScalar": variant != "cached-simd",
                         "coreTarget": ".NETCoreApp,Version=v10.0", "stopwatchFrequency": 1000,
                         "freezeSha256": "frozen", "inputHashes": {dataset: dataset + "-input" for dataset in analyze.METHODS},
                         "measurements": measurements})
    return runs, quality, metadata


class AnalysisChecks(unittest.TestCase):
    def test_failures_and_unsupported_preserve_different_denominators(self):
        data = {"a": {"supported": True}, "b": {"supported": True}, "c": {"supported": False}}
        rows = [prediction("a"), prediction("b", status="failed"), prediction("c", status="unsupported")]
        actual = analyze.metrics(rows, data)
        self.assertEqual(actual["supportedAccuracy"], .5)
        self.assertEqual(actual["allTestAccuracy"], 1/3)
        self.assertEqual(actual["failedTrials"], 1)
        self.assertEqual(actual["unsupportedTrials"], 1)
        self.assertEqual(actual["confusion"], {"a": {"a": 1, "[failed]": 1, "[unsupported]": 1}})

    def test_paired_corrections_and_regressions_are_oriented_correctly(self):
        data = {key: {"supported": True} for key in ("a", "b", "c", "d")}
        baseline = [prediction("a"), prediction("b"), prediction("c", guessed="b"), prediction("d", status="failed")]
        candidate = [prediction("a", "combined"), prediction("b", "combined", guessed="b"),
                     prediction("c", "combined"), prediction("d", "combined", guessed="b")]
        result = analyze.paired_counts(baseline, candidate, data)
        for key in ("bothCorrect", "baselineOnlyCorrect", "candidateOnlyCorrect", "bothWrong"):
            self.assertEqual(result[key], 1)
        self.assertEqual(result["accuracyDifference"], 0)

    def test_replay_selection_uses_first_ordinal_successful_correction_and_regression(self):
        rows = {}
        for identifier in ("c", "b", "a"):
            for method in ("rms", "combined"):
                guessed = "a" if (identifier == "b") == (method == "rms") else "b"
                row = prediction(identifier, method, guessed=guessed)
                row["dataset"] = "pendigits"
                rows[analyze.row_key(row)] = row
        selected = analyze.pendigits_replay_cases(rows)["cases"]
        self.assertEqual([(r["kind"], r["sampleId"]) for r in selected],
                         [("combined-correct-rms-wrong", "a"), ("rms-correct-combined-wrong", "b")])
        self.assertNotIn("strokes", str(selected))

    def test_complete_grid_and_ten_writer_blocks(self):
        data, rows = writer_fixture()
        report = analyze.analyze_rows(rows, data)["dollar"]
        primary = report["primaryCombinedMinusRms"]
        self.assertEqual(primary["writerBlocks"], 10)
        self.assertEqual(primary["replicates"], 10000)
        self.assertEqual(primary["seed"], 424242)
        self.assertEqual(primary["writerMeanDifference"], .5)
        self.assertEqual(len(primary["writerRows"]), 10)
        self.assertEqual(primary["writerRows"][0]["querySeedTrials"], 6)
        self.assertEqual(set(report["methods"]["rms"]["bySpeed"]), {"slow", "fast"})
        self.assertEqual(report["methods"]["rms"]["correctTrials"], 30)

    def test_missing_failure_row_cannot_improve_denominator(self):
        data, rows = writer_fixture()
        rows.pop(next(iter(rows)))
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            analyze.validate_coverage(rows, data)

    def test_wrong_label_writer_support_and_template_leakage_fail(self):
        data, original = writer_fixture()
        for field, value in (("trueLabel", "wrong"), ("writerId", "s99"), ("status", "unsupported"),
                             ("templateId", next(iter(data)))):
            rows = copy.deepcopy(original)
            row = rows[next(iter(rows))]
            row[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                analyze.validate_coverage(rows, data)

    def test_bootstrap_constant_and_deterministic_nonconstant(self):
        constant = analyze.writer_bootstrap([.25] * 10)
        self.assertEqual((constant["writerMeanDifference"], constant["lower95"], constant["upper95"]), (.25, .25, .25))
        first = analyze.writer_bootstrap([-.2, .1, .3], replicates=100)
        self.assertEqual(first, analyze.writer_bootstrap([-.2, .1, .3], replicates=100))
        self.assertEqual(first["writerBlocks"], 3)

    def test_identical_comparison_is_exact(self):
        row = prediction()
        result = analyze.compare_rows({analyze.row_key(row): row}, {analyze.row_key(row): copy.deepcopy(row)})
        self.assertTrue(result["exact"])
        self.assertEqual(result["numericFields"]["score"]["maxAbsoluteDifference"], 0)

    def test_one_ulp_score_change_is_reported_without_prediction_change(self):
        left, right = prediction(), prediction()
        right["score"] = math.nextafter(left["score"], math.inf)
        result = analyze.compare_rows({analyze.row_key(left): left}, {analyze.row_key(right): right})
        self.assertFalse(result["exact"])
        self.assertEqual(result["changedPredictions"], 0)
        self.assertEqual(result["numericFields"]["score"]["bitwiseEqual"], 0)
        self.assertEqual(result["numericFields"]["score"]["maxAbsoluteDifference"], right["score"]-left["score"])

    def test_signed_zero_difference_is_not_hidden(self):
        left, right = prediction(), prediction()
        left["score"], right["score"] = 0.0, -0.0
        result = analyze.compare_rows({analyze.row_key(left): left}, {analyze.row_key(right): right})
        self.assertFalse(result["exact"])
        self.assertEqual(result["numericFields"]["score"]["maxAbsoluteDifference"], 0)

    def test_extra_empty_object_is_a_full_row_difference(self):
        left, right = prediction(), prediction()
        right["extra"] = {}
        result = analyze.compare_rows({analyze.row_key(left): left}, {analyze.row_key(right): right})
        self.assertFalse(result["exact"])

    def test_nested_nonfinite_value_rejected(self):
        row = prediction()
        row["extra"] = {"values": [math.inf]}
        with self.assertRaisesRegex(ValueError, "Nonfinite"):
            analyze.validate_prediction(row)

    def test_pendigits_descriptive_seeds_keep_unsupported_queries(self):
        data = {}
        for identifier, split, supported in (("t", "train", True), ("q", "test", True), ("u", "test", False)):
            data[identifier] = {"sampleId": identifier, "dataset": "pendigits", "split": split,
                                "label": "0", "supported": supported}
        rows = {}
        for seed in analyze.SEEDS:
            for method in analyze.METHODS["pendigits"]:
                for identifier in ("q", "u"):
                    row = prediction(identifier, method, "ok" if identifier == "q" else "unsupported", "0", "0")
                    row.pop("writerId")
                    row.update(dataset="pendigits", seed=seed)
                    if identifier == "q":
                        row["templateId"] = "t"
                    rows[analyze.row_key(row)] = row
        result = analyze.analyze_rows(rows, data)["pendigits"]
        self.assertNotIn("primaryCombinedMinusRms", result)
        self.assertEqual(result["methods"]["dtw"]["supportedAccuracy"], 1)
        self.assertEqual(result["methods"]["dtw"]["allTestAccuracy"], .5)
        self.assertEqual(result["methods"]["dtw"]["bySeed"]["1729"]["unsupportedTrials"], 1)

    def test_missing_rows_and_changed_prediction_are_reported(self):
        a, b = prediction("a"), prediction("b")
        changed = prediction("a", guessed="b")
        result = analyze.compare_rows({analyze.row_key(a): a, analyze.row_key(b): b}, {analyze.row_key(changed): changed})
        self.assertEqual(result["changedPredictions"], 1)
        self.assertEqual(len(result["missingFromRight"]), 1)

    def test_ties_and_smallest_margin_are_separate(self):
        rows = [prediction("a"), prediction("b"), prediction("c")]
        for row, margin in zip(rows, (0.0, 1e-15, .1)):
            row["margin"] = margin
        result = analyze.margin_summary(rows)
        self.assertEqual(result["exactDistinctClassTies"], 1)
        self.assertEqual(result["smallestPositiveMargin"], 1e-15)

    def test_nonfinite_missing_score_and_negative_margin_rejected(self):
        for field, value in (("score", math.nan), ("rms", math.inf), ("area", None), ("margin", -.1)):
            row = prediction()
            row[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                analyze.validate_prediction(row)

    def test_duplicate_compressed_rows_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "predictions.jsonl.gz"
            text = json.dumps(prediction()) + "\n"
            with gzip.open(path, "wt", encoding="utf-8", newline="\n") as stream:
                stream.write(text + text)
            with self.assertRaisesRegex(ValueError, "Duplicate"):
                analyze.load_predictions(path)

    def test_jsonl_nonfinite_literals_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "sample.jsonl"
            path.write_bytes(b'{"score":NaN}\n')
            with self.assertRaisesRegex(ValueError, "Nonfinite"):
                list(analyze.read_jsonl(path))

    def test_writes_utf8_lf_without_bom(self):
        with tempfile.TemporaryDirectory() as directory:
            analyze.write_report(Path(directory), {"value": "δ"}, "δ\n")
            for name in ("summary.json", "summary.md"):
                content = (Path(directory) / name).read_bytes()
                self.assertNotIn(b"\r\n", content)
                self.assertFalse(content.startswith(b"\xef\xbb\xbf"))


class PerformanceChecks(unittest.TestCase):
    def test_nine_runs_validate_predictions_and_preserve_negative_retained_noise(self):
        runs, quality, metadata = performance_fixture()
        result = analyze.performance_rows(runs, quality, metadata)
        self.assertEqual(result["runs"], 9)
        self.assertEqual(result["cases"], 243)
        self.assertEqual(len(result["summaries"]), 27)
        self.assertTrue(result["qualityAgreement"]["exact"])
        self.assertTrue(result["variantAgreement"]["exact"])
        self.assertEqual(result["variantAgreement"]["comparedRows"], 216)
        self.assertEqual(result["summaries"][0]["retainedPreparedBankBytes"], {"median": -5, "minimum": -10, "maximum": 0})

    def test_missing_and_duplicate_process_runs_fail(self):
        runs, quality, metadata = performance_fixture()
        for bad in (runs[:-1], runs + [runs[0]]):
            with self.assertRaisesRegex(ValueError, "three variants"):
                analyze.performance_rows(bad, quality, metadata)

    def test_query_identity_cannot_silently_change(self):
        runs, quality, metadata = performance_fixture()
        runs[0]["measurements"][0]["queries"][0]["sampleId"] = "missing"
        with self.assertRaisesRegex(ValueError, "query identities"):
            analyze.performance_rows(runs, quality, metadata)

    def test_one_ulp_score_change_is_reported_against_quality_and_variants(self):
        runs, quality, metadata = performance_fixture()
        query = runs[-1]["measurements"][0]["queries"][0]
        query["score"] = math.nextafter(query["score"], math.inf)
        result = analyze.performance_rows(runs, quality, metadata)
        self.assertFalse(result["qualityAgreement"]["exact"])
        self.assertFalse(result["variantAgreement"]["exact"])
        self.assertEqual(len(result["qualityAgreement"]["deviations"]), 1)
        self.assertEqual(len(result["variantAgreement"]["deviations"]), 1)

    def test_switch_and_quantile_errors_fail(self):
        original, quality, metadata = performance_fixture()
        for kind in ("switch", "quantile"):
            runs = copy.deepcopy(original)
            if kind == "switch":
                runs[0]["forceScalar"] = False
            else:
                runs[0]["measurements"][0]["p95Microseconds"] = 999
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                analyze.performance_rows(runs, quality, metadata)

    def test_compressed_json_reader(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "case.json.gz"
            with gzip.open(path, "wt", encoding="utf-8", newline="\n") as stream:
                json.dump({"value": [1, "x"]}, stream)
            self.assertEqual(analyze.read_json(path), {"value": [1, "x"]})


if __name__ == "__main__":
    unittest.main()
