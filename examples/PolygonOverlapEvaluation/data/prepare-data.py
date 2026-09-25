#!/usr/bin/env python3
"""Download/check pinned Solaris originals, then convert image-space WKT to JSON.

Python standard library only. No filtering, sorting, normalization or repair.
"""
import argparse
import csv
from decimal import Decimal
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parent
TOKEN = re.compile(r"\s*([A-Za-z]+|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[(),])")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encode(value):
    return (json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(",", ":")) + "\n").encode("utf-8")


def parse_wkt(text):
    """The bounded POLYGON/MULTIPOLYGON grammar needed by this fixture, including EMPTY.

    Optional Z tags and untagged three-coordinate tuples are accepted only when
    the original decimal Z token is exactly zero. Closing vertices are retained.
    Unsupported WKT is an error, never a silently omitted input.
    """
    tokens, position = [], 0
    while position < len(text.rstrip()):
        match = TOKEN.match(text, position)
        if match is None:
            raise ValueError("Unsupported WKT token at character " + str(position))
        tokens.append(match[1]); position = match.end()
    offset = 0

    def pop(expected=None):
        nonlocal offset
        if offset == len(tokens):
            raise ValueError("Unexpected end of WKT.")
        value = tokens[offset]; offset += 1
        if expected is not None and value.upper() != expected:
            raise ValueError("Expected WKT token " + expected)
        return value

    def peek():
        return tokens[offset].upper() if offset < len(tokens) else None

    kind = pop().upper()
    if kind not in ("POLYGON", "MULTIPOLYGON"):
        raise ValueError("Unsupported geometry type: " + kind)
    tagged_z = peek() == "Z"
    if tagged_z:
        pop("Z")
    dimensions = []

    def ring():
        pop("("); points = []
        while True:
            parts = []
            while peek() not in (",", ")", None):
                parts.append(pop())
            if len(parts) not in (2, 3) or (tagged_z and len(parts) != 3):
                raise ValueError("A vertex must have XY or XYZ coordinates.")
            xy = [float(value) for value in parts[:2]]
            if not all(math.isfinite(value) for value in xy):
                raise ValueError("Non-finite XY.")
            if len(parts) == 3 and not Decimal(parts[2]).is_zero():
                raise ValueError("Nonzero Z cannot be discarded.")
            dimensions.append(len(parts)); points.append(xy)
            if peek() != ",":
                break
            pop(",")
        pop(")")
        if len(points) < 4 or points[0] != points[-1]:
            raise ValueError("Expected an explicitly closed ring with at least four supplied vertices.")
        return points

    def polygon():
        pop("("); rings = [ring()]
        while peek() == ",":
            pop(","); rings.append(ring())
        pop(")")
        return rings

    if peek() == "EMPTY":
        pop("EMPTY"); polygons = []
    elif kind == "POLYGON":
        polygons = [polygon()]
    else:
        pop("("); polygons = [polygon()]
        while peek() == ",":
            pop(","); polygons.append(polygon())
        pop(")")
    if offset != len(tokens):
        raise ValueError("Trailing WKT tokens.")
    if len(set(dimensions)) > 1:
        raise ValueError("Mixed coordinate dimensions in a geometry.")
    return polygons, len(dimensions), sum(d == 3 for d in dimensions)


def exact_ring_diagnostics(ring):
    # Scale binary64 values by their common power-of-two denominator. Int arithmetic
    # gives exact orientation/intersection signs on the actual parsed XY values.
    coords = [v.as_integer_ratio() for point in ring[:-1] for v in point]
    shift = max(den.bit_length() - 1 for _, den in coords)
    values = [num << (shift - (den.bit_length() - 1)) for num, den in coords]
    pts = list(zip(values[::2], values[1::2])); count = len(pts)

    def cross(a, b, c):
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])

    def on(a, b, c):
        return min(a[0], b[0]) <= c[0] <= max(a[0], b[0]) and min(a[1], b[1]) <= c[1] <= max(a[1], b[1])

    def intersects(a, b, c, d):
        ac, ad, ca, cb = cross(a, b, c), cross(a, b, d), cross(c, d, a), cross(c, d, b)
        return ((ac > 0) != (ad > 0) and ac != 0 and ad != 0 and
                (ca > 0) != (cb > 0) and ca != 0 and cb != 0) or any([
                    ac == 0 and on(a, b, c), ad == 0 and on(a, b, d),
                    ca == 0 and on(c, d, a), cb == 0 and on(c, d, b)])

    twice = sum(pts[i][0] * pts[(i + 1) % count][1] - pts[i][1] * pts[(i + 1) % count][0] for i in range(count))
    reason = "zero-signed-area" if twice == 0 else None
    for i, a in enumerate(pts):
        b = pts[(i + 1) % count]
        if a == b:
            reason = reason or "zero-length-edge"
        for j in range(i + 1, count):
            if j == i + 1 or (i == 0 and j == count - 1):
                continue
            if intersects(a, b, pts[j], pts[(j + 1) % count]):
                reason = reason or "nonadjacent-edges-intersect"
    signs = {1 if v > 0 else -1 for i in range(count) if (v := cross(pts[i - 1], pts[i], pts[(i + 1) % count])) != 0}
    return {"Simple": reason is None, "Diagnostic": reason, "Convex": reason is None and len(signs) == 1,
            "UnsignedShoelaceArea": abs(twice) / (1 << (shift * 2 + 1))}


def read_csv(original, name):
    with (original / "solaris/data" / name).open(encoding="utf-8", newline="") as stream:
        return list(csv.DictReader(stream))


def geometry_rows(source, predictions):
    result, inventory = [], []
    seen = set()
    for index, row in enumerate(source):
        image, building = row["ImageId"], int(row["BuildingId"])
        if (image, building) in seen:
            raise ValueError("Duplicate image/building key.")
        seen.add((image, building))
        confidence = float(row["Confidence"]) if predictions else None
        if confidence is not None and not math.isfinite(confidence):
            raise ValueError("Non-finite confidence.")
        polygons, vertices, z_count = parse_wkt(row["PolygonWKT_Pix"])
        entry = {"SourceIndex": index, "ImageId": image, "BuildingId": building,
                 "Confidence": confidence, "IsEmpty": not polygons, "Polygons": polygons}
        result.append(entry)
        single = len(polygons) == 1 and len(polygons[0]) == 1
        diagnostic = exact_ring_diagnostics(polygons[0][0]) if single else None
        inventory.append({"SourceIndex": index, "ImageId": image, "BuildingId": building,
                          "Empty": not polygons, "Polygons": len(polygons),
                          "Holes": sum(len(p) - 1 for p in polygons), "SuppliedVertices": vertices,
                          "ZeroZCoordinatesDropped": z_count, "RingDiagnostics": diagnostic})
    return result, inventory


def summary(rows, details, predictions):
    sizes = sorted(d["SuppliedVertices"] for d in details if not d["Empty"])
    simple = [d for d in details if d["RingDiagnostics"] is not None and d["RingDiagnostics"]["Simple"]]
    # Threshold inventory only; output data retains every row, including sentinels.
    removed = [d for d in details if d["Empty"] or (d["RingDiagnostics"] is not None and
               (d["RingDiagnostics"]["UnsignedShoelaceArea"] <= 20 if predictions else
                d["RingDiagnostics"]["UnsignedShoelaceArea"] < 20))]
    ties = []
    for image in dict.fromkeys(r["ImageId"] for r in rows):
        confidences = [r["Confidence"] for r in rows if r["ImageId"] == image]
        if predictions and len(confidences) != len(set(confidences)):
            ties.append(image)
    return {"Rows": len(rows), "Images": len({r["ImageId"] for r in rows}),
            "EmptyRows": sum(d["Empty"] for d in details), "MultipartRows": sum(d["Polygons"] > 1 for d in details),
            "RowsWithHoles": sum(d["Holes"] > 0 for d in details), "SimpleSingleRings": len(simple),
            "ConvexSimpleRings": sum(d["RingDiagnostics"]["Convex"] for d in simple),
            "ConcaveSimpleRings": sum(not d["RingDiagnostics"]["Convex"] for d in simple),
            "NonsimpleSingleRings": sum(d["RingDiagnostics"] is not None and not d["RingDiagnostics"]["Simple"] for d in details),
            "SuppliedVerticesIncludingClosure": sum(d["SuppliedVertices"] for d in details),
            "NonemptyVertexRange": [min(sizes), max(sizes)], "NonemptyVertexMedian": statistics.median(sizes),
            "ZeroZCoordinatesDropped": sum(d["ZeroZCoordinatesDropped"] for d in details),
            "FilterCondition": "area > 20" if predictions else "area >= 20",
            "RowsExcludedByAreaFilterIncludingEmpty": len(removed),
            "ExcludedKeys": [{"ImageId": d["ImageId"], "BuildingId": d["BuildingId"]} for d in removed],
            "ImagesWithConfidenceTies": ties}


def convert(original, commit):
    pred_source = read_csv(original, "SN2_sample_preds.csv")
    truth_source = read_csv(original, "SN2_sample_truth.csv")
    expected_source = read_csv(original, "SN2_sample_iou_by_building.csv")
    predictions, pred_details = geometry_rows(pred_source, True)
    truth, truth_details = geometry_rows(truth_source, False)
    expected = [{"SourceIndex": i, "ImageId": r["ImageId"], "BuildingId": int(r["BuildingId"]),
                 "IoU": float(r["iou_score"])} for i, r in enumerate(expected_source)]
    if len(expected) != len(truth) or any(not math.isfinite(r["IoU"]) for r in expected):
        raise ValueError("Unexpected expected-score population.")
    if [(r["ImageId"], r["BuildingId"], r["PolygonWKT_Pix"]) for r in expected_source] != [
            (r["ImageId"], r["BuildingId"], r["PolygonWKT_Pix"]) for r in truth_source]:
        raise ValueError("Expected CSV keys/order/geometries differ from original truth.")
    fixture = {"SchemaVersion": 1, "SourceCommit": commit, "CoordinateSystem": "ImagePixelXY",
               "Predictions": predictions, "Truth": truth, "Expected": expected}
    inventory = {"SchemaVersion": 1, "SourceCommit": commit,
                 "Predictions": summary(predictions, pred_details, True), "Truth": summary(truth, truth_details, False),
                 "ExpectedRows": len(expected), "Images": list(dict.fromkeys(r["ImageId"] for r in truth)),
                 "Diagnostics": "Exact binary64 segment signs for single rings; runtime backend validity is checked separately.",
                 "PredictionRows": pred_details, "TruthRows": truth_details}
    return fixture, inventory


def check_parser():
    polygon, count, z_count = parse_wkt("POLYGON ((0 0 0,1 0 0,0 1 0,0 0 0))")
    assert polygon == [[[[0., 0.], [1., 0.], [0., 1.], [0., 0.]]]] and count == z_count == 4
    assert parse_wkt("POLYGON EMPTY") == ([], 0, 0)
    assert parse_wkt("MULTIPOLYGON EMPTY") == ([], 0, 0)
    assert len(parse_wkt("MULTIPOLYGON (((0 0,1 0,0 1,0 0)),((2 0,3 0,2 1,2 0)))")[0]) == 2
    assert len(parse_wkt("POLYGON ((0 0,4 0,0 4,0 0),(1 1,2 1,1 2,1 1))")[0][0]) == 2
    for wkt in ["POLYGON ((0 0 1,1 0 0,0 1 0,0 0 1))", "POLYGON ((0 0 1e-9999,1 0 0,0 1 0,0 0 1e-9999))",
                "POLYGON ((0 0,1 0,0 1))", "POLYGON ((0 0,1e999 0,0 1,0 0))", "LINESTRING (0 0,1 1)",
                "POLYGON EMPTY extra", "POLYGON ((0 0,1 0,0 1,0 0))$"]:
        try:
            parse_wkt(wkt)
        except (ValueError, ArithmeticError):
            pass
        else:
            raise AssertionError("Malformed WKT was accepted: " + wkt)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", help="Verify/convert already downloaded originals; never access network")
    args = parser.parse_args()
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    check_parser()
    for entry in manifest["Files"]:
        destination = ROOT / "original" / entry["Path"]
        if destination.exists():
            data = destination.read_bytes()
        elif args.offline:
            raise FileNotFoundError("Missing pinned original: " + entry["Path"])
        else:
            with urlopen(entry["Url"], timeout=60) as response:
                data = response.read()
        if len(data) != entry["Bytes"] or sha(data) != entry["Sha256"]:
            raise ValueError("Pinned original differs; will not overwrite: " + entry["Path"])
        if not destination.exists():
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
    fixture, inventory = convert(ROOT / "original", manifest["SourceCommit"])
    fixture_bytes = encode(fixture)
    expected_hash = manifest.get("ConvertedFixtureSha256")
    if expected_hash is not None and sha(fixture_bytes) != expected_hash:
        raise ValueError("Converted fixture checksum differs from the frozen manifest.")
    output = ROOT / "generated"; output.mkdir(exist_ok=True)
    (output / "solaris.json").write_bytes(fixture_bytes)
    inventory["FixtureSha256"] = sha(fixture_bytes)
    inventory_bytes = encode(inventory)
    expected_inventory = manifest.get("ConvertedInventorySha256")
    if expected_inventory is not None and sha(inventory_bytes) != expected_inventory:
        raise ValueError("Converted inventory checksum differs from the frozen manifest.")
    (output / "inventory.json").write_bytes(inventory_bytes)
    print(json.dumps({"Output": "data/generated/solaris.json", "Sha256": sha(fixture_bytes),
                      "Predictions": inventory["Predictions"], "Truth": inventory["Truth"],
                      "ExpectedRows": len(fixture["Expected"])}, indent=2))


if __name__ == "__main__":
    main()
