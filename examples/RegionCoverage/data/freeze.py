"""Freeze/verify the predeclared Census RegionCoverage population; stdlib only.

Default: verify local snapshots and reproduce derived bytes in memory (offline).
--download: fetch a new population only when manifest.json does not exist.
Run from any directory. Never silently refresh a published dataset.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent
BASE = "https://tigerweb.geo.census.gov/arcgis/rest/services/Generalized_ACS2024/"
SPECS = (
    ("zones", "nc-counties", "State_County/MapServer/12", "Counties 5M"),
    ("queries", "nc-districts", "Legislative/MapServer/6", "119th Congressional Districts 5M"),
)
WHERE = "STATE='37'"


def encode(value):
    return (json.dumps(value, indent=2, ensure_ascii=True, allow_nan=False) + "\n").encode("utf-8")


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load(raw):
    value = json.loads(raw)
    require("error" not in value, f"Source error: {value.get('error')}")
    require(not value.get("exceededTransferLimit", False), "Truncated source response")
    return value


def download(name, url):
    request = urllib.request.Request(url, headers={"User-Agent": "PolylineKit-RegionCoverage/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        raw = response.read()
        response_date = response.headers.get("Date")
    load(raw)  # Do not save an ArcGIS error in place of source data.
    path = ROOT / "source" / name
    path.parent.mkdir(exist_ok=True)
    path.write_bytes(raw)
    return {
        "File": "source/" + name, "Url": url,
        "RetrievedUtc": datetime.now(timezone.utc).isoformat(),
        "ResponseDate": response_date, "Bytes": len(raw), "Sha256": sha(raw),
    }


def urls(stem, layer):
    base = BASE + layer
    query = urllib.parse.urlencode({
        "where": WHERE, "outFields": "*", "outSR": "5070",
        "orderByFields": "GEOID", "f": "geojson", "returnGeometry": "true",
    })
    count = urllib.parse.urlencode({"where": WHERE, "returnCountOnly": "true", "f": "json"})
    return (
        (stem + ".geojson", base + "/query?" + query),
        (stem + "-layer.json", base + "?f=json"),
        (stem + "-count.json", base + "/query?" + count),
    )


def derive(role, stem, layer, title):
    collection = load((ROOT / "source" / (stem + ".geojson")).read_bytes())
    metadata = load((ROOT / "source" / (stem + "-layer.json")).read_bytes())
    count = load((ROOT / "source" / (stem + "-count.json")).read_bytes())["count"]
    require(collection.get("type") == "FeatureCollection", "Expected a GeoJSON FeatureCollection")
    require(collection.get("crs", {}).get("properties", {}).get("name") == "EPSG:5070",
            "Returned projection differs from requested EPSG:5070")
    require(metadata["name"] == title and metadata["geometryType"] == "esriGeometryPolygon",
            "Unexpected layer identity")
    features = collection["features"]
    require(len(features) == count, "Count query and feature response disagree")
    ids = [feature["properties"]["GEOID"] for feature in features]
    require(ids == sorted(set(ids)), "Expected unique GEOIDs in requested order")
    regions, inventory = [], []
    for feature in features:
        attributes = feature["properties"]
        require(attributes["STATE"] == "37", "Unexpected state")
        geometry = feature.get("geometry")
        geometry_type = geometry.get("type") if geometry else None
        coordinates = geometry.get("coordinates", []) if geometry else []
        if geometry_type == "Polygon":
            components, rings = 1, [len(coordinates)]
            reason = None if len(coordinates) == 1 else "Polygon contains holes/multiple rings"
        elif geometry_type == "MultiPolygon":
            components, rings = len(coordinates), [len(component) for component in coordinates]
            reason = "MultiPolygon; no component removed or flattened"
        else:
            components, rings = 0, []
            reason = "Unsupported or absent geometry"
        row = {
            "Id": attributes["GEOID"], "Name": attributes["NAME"],
            "GeometryType": geometry_type, "Components": components,
            "RingsPerComponent": rings, "Holes": sum(max(0, n - 1) for n in rings),
            "SourceGeometrySha256": sha(json.dumps(geometry, separators=(",", ":"), allow_nan=False).encode()),
            "EligibleSingleRing": reason is None, "ExclusionReason": reason,
        }
        if reason is None:
            ring = coordinates[0]
            require(len(ring) >= 4 and ring[0] == ring[-1], f"Source ring is not closed: {row['Id']}")
            require(all(len(p) == 2 and all(isinstance(x, (int, float)) and math.isfinite(x) for x in p)
                        for p in ring), f"Invalid coordinate dimensions/values: {row['Id']}")
            points = ring[:-1]
            require(len(set(map(tuple, points))) >= 3, f"Fewer than three distinct vertices: {row['Id']}")
            row["Vertices"] = len(points)
            row["Bounds"] = [min(p[0] for p in points), min(p[1] for p in points),
                             max(p[0] for p in points), max(p[1] for p in points)]
            regions.append({"Id": row["Id"], "Name": row["Name"],
                            "SourceAttributes": attributes, "Points": points})
        inventory.append(row)
    output = {"SchemaVersion": 1, "Role": role, "SpatialReference": 5070,
              "CoordinateUnit": "metre", "Regions": regions}
    raw = encode(output)
    layer_info = {
        "Role": role, "LayerUrl": BASE + layer, "LayerName": title,
        "Description": metadata["description"], "SourceFeatures": count,
        "EligibleSingleRingFeatures": len(regions), "ExcludedFeatures": count - len(regions),
        "GeometryTypes": dict(sorted(Counter(r["GeometryType"] for r in inventory).items())),
        "SourceFeaturesWithHoles": sum(r["Holes"] > 0 for r in inventory),
        "TotalEligibleVertices": sum(len(r["Points"]) for r in regions),
        "MinimumEligibleVertices": min(len(r["Points"]) for r in regions),
        "MaximumEligibleVertices": max(len(r["Points"]) for r in regions),
        "GeometryValidity": "Not inferred; runner independently validates every admitted ring before timing.",
        "DerivedFile": role + ".json", "DerivedBytes": len(raw), "DerivedSha256": sha(raw),
        "Features": inventory,
    }
    return raw, layer_info


def pairs(layers):
    zones = [r for r in layers[0]["Features"] if r["EligibleSingleRing"]]
    queries = [r for r in layers[1]["Features"] if r["EligibleSingleRing"]]
    candidates = []
    for z in zones:
        for q in queries:
            a, b = z["Bounds"], q["Bounds"]
            # Closed AABB intersection: boundary contact remains a candidate.
            if a[0] <= b[2] and b[0] <= a[2] and a[1] <= b[3] and b[1] <= a[3]:
                candidates.append([z["Id"], q["Id"]])
    total = len(zones) * len(queries)
    return {
        "TotalPairsPerDirection": total, "BoundingBoxCandidatesPerDirection": len(candidates),
        "BoundingBoxRejectsPerDirection": total - len(candidates),
        "CandidateIdsCountyThenDistrict": candidates,
        "Directions": ["county zones / district queries", "district zones / county queries"],
        "Filter": "Closed AABBs overlap; no area, convexity, containment or timing selection.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download", action="store_true", help="Create a new freeze only when manifest.json is absent")
    args = parser.parse_args()
    manifest_path = ROOT / "manifest.json"
    if args.download:
        require(not manifest_path.exists(), "Manifest already exists. Use a separate directory for a new freeze.")
        # The protocol must already exist before any geometry is downloaded.
        protocol_sha = sha((ROOT / "PROTOCOL.md").read_bytes())
        source = [download(name, url) for _, stem, layer, _ in SPECS for name, url in urls(stem, layer)]
        manifest = {
            "SchemaVersion": 1, "SelectionProtocol": "PROTOCOL.md", "ProtocolSha256": protocol_sha,
            "FrozenUtc": datetime.now(timezone.utc).isoformat(),
            "Source": "U.S. Census Bureau, TIGERweb Generalized ACS2024; January 1, 2024 vintage",
            "License": "U.S. Census Bureau public data; U.S. government work, public domain in the United States",
            "LicenseUrl": "https://www2.census.gov/foia/ds_policies/ds027.pdf",
            "LicenseReference": "Page 9; Census employee works generally are not subject to U.S. copyright.",
            "CitationUrl": "https://www.census.gov/about/policies/citation.html",
            "Where": WHERE, "SpatialReference": "EPSG:5070 / NAD83 Conus Albers", "CoordinateUnit": "metre",
            "CoordinateChanges": "Remove one duplicate closing vertex only; no translation, scaling, rounding, repair or simplification.",
            "Relationship": "Different administrative layers from the same Census source; not independent observations or land-cover classes.",
            "SourceFiles": source,
        }
    else:
        manifest = json.loads(manifest_path.read_bytes())
        require(sha((ROOT / "PROTOCOL.md").read_bytes()) == manifest["ProtocolSha256"], "Protocol hash mismatch")
        expected_urls = {"source/" + name: url for _, stem, layer, _ in SPECS for name, url in urls(stem, layer)}
        require(len(manifest["SourceFiles"]) == len(expected_urls), "Unexpected number of source files")
        for record in manifest["SourceFiles"]:
            require(expected_urls.pop(record["File"], None) == record["Url"], "Source identity mismatch")
            raw = (ROOT / record["File"]).read_bytes()
            require(len(raw) == record["Bytes"] and sha(raw) == record["Sha256"], "Source hash mismatch: " + record["File"])
        require(not expected_urls, "Missing source files")
    layers = []
    for spec in SPECS:
        raw, info = derive(*spec)
        layers.append(info)
        path = ROOT / info["DerivedFile"]
        if args.download:
            path.write_bytes(raw)
        else:
            require(path.read_bytes() == raw, "Derived data differs from source: " + path.name)
        print(f"{spec[0]}: {info['SourceFeatures']} source; {info['EligibleSingleRingFeatures']} single-ring; "
              f"{info['ExcludedFeatures']} excluded; {info['TotalEligibleVertices']} admitted vertices")
    pair_info = pairs(layers)
    files = {info["DerivedFile"]: {"Sha256": info["DerivedSha256"], "Bytes": info["DerivedBytes"]}
             for info in layers}
    if args.download:
        manifest["Layers"], manifest["Pairs"], manifest["Files"] = layers, pair_info, files
        manifest_path.write_bytes(encode(manifest))
    else:
        require(layers == manifest["Layers"], "Derived inventory mismatch")
        require(pair_info == manifest["Pairs"], "Pair inventory mismatch")
        require(files == manifest["Files"], "Derived file hashes mismatch")
    print(f"Pairs per direction: {pair_info['TotalPairsPerDirection']}; "
          f"closed-AABB candidates: {pair_info['BoundingBoxCandidatesPerDirection']}; "
          f"rejects: {pair_info['BoundingBoxRejectsPerDirection']}")
    print("Frozen source, protocol, derived arrays and complete pair inventory verified.")


if __name__ == "__main__":
    main()
