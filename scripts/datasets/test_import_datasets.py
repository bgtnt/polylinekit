"""Fresh tiny synthetic parser fixtures; no downloaded paths are embedded here."""
import unittest
from unittest.mock import patch

import import_datasets as importer


XML = b'''<Gesture Name="arrow01" Subject="1" Speed="fast" Number="1" NumPts="3">
<Point X="2" Y="7" T="100"/><Point X="2" Y="7" T="101"/><Point X="-3" Y="4" T="105"/>
</Gesture>'''
XML_MEMBER = "xml_logs/s01 (pilot)/fast/arrow01.xml"
UNIPEN = '''.INCLUDE dene.doc
.LEXICON "0" "1"
.HIERARCHY DIGIT
.SEGMENT DIGIT 0 ? "1"
.COMMENT 1 91 22
.PEN_DOWN
8 3
8 3
2 9
.PEN_UP
.DT 100
.SEGMENT DIGIT 1-2 ? "0"
.COMMENT 0 92 23
.PEN_DOWN
1 2
3 4
.PEN_UP
.DT 100
.PEN_DOWN
9 8
7 6
.PEN_UP
.DT 100
'''


class ImportChecks(unittest.TestCase):
    def test_xml_preserves_order_duplicates_time_and_identity(self):
        record = importer.parse_dollar_xml(XML_MEMBER, XML)
        self.assertEqual(record["strokes"], [[{"x": 2, "y": 7, "t": 100}, {"x": 2, "y": 7, "t": 101}, {"x": -3, "y": 4, "t": 105}]])
        self.assertEqual((record["dataset"], record["split"], record["writerId"], record["speed"], record["repetition"]),
                         ("dollar", "pilot", "s01", "fast", 1))
        self.assertTrue(record["supported"])
        self.assertNotIn("session", record)

    def test_xml_rejects_inconsistent_source_metadata(self):
        for original, replacement in ((b'Subject="1"', b'Subject="2"'), (b'NumPts="3"', b'NumPts="4"'),
                                      (b'Speed="fast"', b'Speed="slow"'), (b'Number="1"', b'Number="2"')):
            with self.subTest(original=original), self.assertRaises(ValueError):
                importer.parse_dollar_xml(XML_MEMBER, XML.replace(original, replacement))

    def test_unipen_preserves_strokes_duplicates_axes_and_segment_ids(self):
        first, second = importer.parse_pendigits(UNIPEN, "pendigits-orig.tra", "train")
        self.assertEqual(first["sampleId"], "pendigits/pendigits-orig.tra/000000")
        self.assertEqual(second["sampleId"], "pendigits/pendigits-orig.tra/000001-000002")
        self.assertEqual(first["strokes"][0], [{"x": 8, "y": 3}, {"x": 8, "y": 3}, {"x": 2, "y": 9}])
        self.assertEqual(second["strokes"], [[{"x": 1, "y": 2}, {"x": 3, "y": 4}], [{"x": 9, "y": 8}, {"x": 7, "y": 6}]])
        self.assertTrue(first["supported"])
        self.assertFalse(second["supported"])
        self.assertEqual(second["exclusionReason"], "multiple-strokes")

    def test_unipen_does_not_infer_writer_session_or_point_time(self):
        record = importer.parse_pendigits(UNIPEN, "pendigits-orig.tra", "train")[0]
        self.assertNotIn("writerId", record)
        self.assertNotIn("session", record)
        self.assertEqual(record["sourceComment"], ["1 91 22"])
        self.assertEqual(record["sourceDt"], [100])
        self.assertTrue(all("t" not in point for stroke in record["strokes"] for point in stroke))

    def test_unipen_rejects_range_stroke_mismatch(self):
        with self.assertRaisesRegex(ValueError, "Declared stroke range"):
            importer.parse_pendigits(UNIPEN.replace("1-2 ?", "1-3 ?"), "test", "test")

    def test_unipen_rejects_discontinuous_source_ids(self):
        with self.assertRaisesRegex(ValueError, "Noncontiguous"):
            importer.parse_pendigits(UNIPEN.replace("1-2 ?", "2-3 ?"), "test", "test")

    def test_unipen_rejects_unclosed_or_missing_pen(self):
        for text in (UNIPEN.replace(".PEN_UP", "", 1), UNIPEN.replace(".PEN_DOWN", "", 1)):
            with self.subTest(text=text[:80]), self.assertRaises(ValueError):
                importer.parse_pendigits(text, "test", "test")

    def test_unipen_rejects_unknown_directive_and_missing_dt(self):
        for text in (UNIPEN + ".UNKNOWN 4\n", UNIPEN.replace(".DT 100", "", 1)):
            with self.subTest(text=text[:80]), self.assertRaises(ValueError):
                importer.parse_pendigits(text, "test", "test")

    def test_nonfinite_values_are_rejected(self):
        for value in ("nan", "inf", "-inf", "1e999"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                importer.number(value)

    def test_collapsed_stroke_is_retained_as_unsupported(self):
        record = importer.mark_support({"strokes": [[{"x": 1, "y": 2}, {"x": 1, "y": 2}]]})
        self.assertFalse(record["supported"])
        self.assertEqual(record["exclusionReason"], "zero-length-stroke")

    def test_duplicate_sample_ids_are_rejected(self):
        record = importer.parse_dollar_xml(XML_MEMBER, XML)
        with self.assertRaisesRegex(ValueError, "Duplicate sample IDs"):
            importer.validate_records([record, record], "dollar")

    def test_manifest_contains_metadata_but_no_point_arrays(self):
        record = importer.parse_dollar_xml(XML_MEMBER, XML)
        manifest = importer.make_manifest([record], "dollar", {}, b"synthetic-jsonl", "test")
        self.assertEqual(manifest["records"][0]["pointCount"], 3)
        self.assertNotIn("strokes", manifest["records"][0])
        self.assertEqual(manifest["counts"]["pointCounts"]["total"], 3)

    def test_archive_size_and_sha_are_both_checked(self):
        data = b"fresh synthetic archive bytes"
        with patch.dict(importer.ARCHIVES, {"synthetic": {"bytes": len(data), "sha256": importer.sha256(data)}}):
            importer.verify_archive(data, "synthetic")
            with self.assertRaisesRegex(ValueError, "size"):
                importer.verify_archive(data + b"x", "synthetic")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                importer.verify_archive(b"x" * len(data), "synthetic")


if __name__ == "__main__":
    unittest.main()
