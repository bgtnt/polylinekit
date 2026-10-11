"""Freeze/verify the predeclared Helsinki population; Python stdlib only.

Default: verify hashes, completeness and derived bytes offline.
--download: create a freeze only if manifest.json does not exist.
Live responses never silently replace a published fixture.
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
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
BASE = "https://kartta.hel.fi/ws/geoserver/avoindata/wfs"
EPSG = "urn:ogc:def:crs:EPSG::3067"
MAX_BYTES = 15 * 1024 * 1024
SPECS = (
    ("zones", "districts", "Piirijako_peruspiiri", 34),
    ("queries", "vistra", "Tavoitteellinen_viher_ja_virkistysverkosto_VISTRA_alue", 1095),
)
PAGES = (
    ("districts-metadata.html", "https://hri.fi/data/dataset/helsingin-piirijako"),
    ("vistra-metadata.html", "https://hri.fi/data/dataset/helsingin-tavoitteellinen-viher-ja-virkistysverkostokartta"),
    ("city-geodata-terms.html", "https://www.hel.fi/fi/paatoksenteko-ja-hallinto/tietoa-helsingista/kartat-ja-paikkatieto/paikkatiedot-tehokayttoon/avoimet-paikkatiedot"),
)


def encode(value, *, compact=False):
    options = {"separators": (",", ":")} if compact else {"indent": 2}
    return (json.dumps(value, ensure_ascii=False, allow_nan=False, **options) + "\n").encode("utf-8")


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def wfs(**parameters):
    values = {"service": "WFS", "version": "2.0.0", **parameters}
    return BASE + "?" + urllib.parse.urlencode(values)


def fetch(name, url):
    request = urllib.request.Request(url, headers={"User-Agent": "PolylineKit-IndependentRegions/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        advertised = response.headers.get("Content-Length")
        require(advertised is None or int(advertised) <= MAX_BYTES, "Source exceeds 15 MiB limit")
        raw = response.read(MAX_BYTES + 1)
        require(len(raw) <= MAX_BYTES, "Source exceeds 15 MiB limit")
        headers = {"ResponseDate": response.headers.get("Date"),
                   "ContentType": response.headers.get("Content-Type"),
                   "AdvertisedBytes": int(advertised) if advertised is not None else None}
    require(raw and not raw.lstrip().startswith(b"{\"error\""), "Empty or error response")
    target = ROOT / "source" / name
    target.parent.mkdir(exist_ok=True)
    target.write_bytes(raw)
    return {"File": "source/" + name, "Url": url,
            "RetrievedUtc": datetime.now(timezone.utc).isoformat(),
            "Bytes": len(raw), "Sha256": sha(raw), **headers}


def hits(stem, suffix):
    root = ET.fromstring((ROOT / "source" / f"{stem}-hits-{suffix}.xml").read_bytes())
    require(root.tag == "{http://www.opengis.net/wfs/2.0}FeatureCollection", "Unexpected WFS hits root")
    return int(root.attrib["numberMatched"])


def derive(role, stem, layer, expected):
    raw = (ROOT / "source" / (stem + ".geojson")).read_bytes()
    collection = json.loads(raw)
    require(collection.get("type") == "FeatureCollection", "Expected a GeoJSON collection")
    require(collection.get("crs", {}).get("properties", {}).get("name") == EPSG, "Wrong returned CRS")
    features = collection["features"]
    require(hits(stem, "before") == hits(stem, "after") == expected == len(features), "Population count drift/truncation")
    for key in ("totalFeatures", "numberMatched", "numberReturned"):
        if key in collection:
            require(collection[key] == expected, f"Unexpected {key}")
    ids = [feature["properties"]["id"] for feature in features]
    require(all(isinstance(x, int) and not isinstance(x, bool) for x in ids), "IDs must be integers")
    require(ids == sorted(set(ids)), "IDs must be unique and in source-request order")
    regions, rows = [], []
    for feature in features:
        properties = feature["properties"]
        identity = str(properties["id"])
        geometry = feature.get("geometry")
        require(geometry is not None, f"Null geometry: {identity}")
        kind = geometry.get("type")
        require(kind in ("Polygon", "MultiPolygon"), f"Unsupported geometry: {identity}, {kind}")
        components = [geometry["coordinates"]] if kind == "Polygon" else geometry["coordinates"]
        require(components, f"Empty polygon collection: {identity}")
        polygons, all_points = [], []
        for component in components:
            require(component, f"Empty component: {identity}")
            for ring in component:
                require(len(ring) >= 4 and ring[0] == ring[-1], f"Unclosed/short ring: {identity}")
                require(all(isinstance(p, list) and len(p) == 2 and
                            all(isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x) for x in p)
                            for p in ring), f"Invalid dimensions/coordinate: {identity}")
                require(len(set(map(tuple, ring))) >= 3, f"Fewer than three distinct points: {identity}")
                all_points.extend(ring)
            # Repackage roles only; preserve all source coordinates and closing duplicates.
            polygons.append({"Shell": component[0], "Holes": component[1:]})
        name = properties.get("nimi_fi") or properties.get("kohdetyyppi") or identity
        regions.append({"Id": identity, "Name": name, "SourceFeatureId": feature["id"],
                        "SourceAttributes": properties, "Polygons": polygons})
        rows.append({"Id": identity, "Name": name, "SourceFeatureId": feature["id"],
                     "GeometryType": kind, "Components": len(components),
                     "Rings": sum(map(len, components)), "Holes": sum(len(c) - 1 for c in components),
                     "Vertices": len(all_points),
                     "Bounds": [min(p[0] for p in all_points), min(p[1] for p in all_points),
                                max(p[0] for p in all_points), max(p[1] for p in all_points)]})
    derived = encode({"SchemaVersion": 1, "Role": role, "SpatialReference": 3067,
                      "CoordinateUnit": "metre", "Regions": regions}, compact=True)
    info = {"Role": role, "LayerName": layer, "SourceFeatures": expected,
            "NullOrUnsupportedGeometry": 0, "ExcludedFeatures": 0,
            "GeometryTypes": dict(sorted(Counter(r["GeometryType"] for r in rows).items())),
            "Components": sum(r["Components"] for r in rows), "Rings": sum(r["Rings"] for r in rows),
            "Holes": sum(r["Holes"] for r in rows), "Vertices": sum(r["Vertices"] for r in rows),
            "FeaturesWithHoles": sum(r["Holes"] > 0 for r in rows),
            "MinimumVertices": min(r["Vertices"] for r in rows), "MaximumVertices": max(r["Vertices"] for r in rows),
            "DerivedFile": role + ".json", "DerivedBytes": len(derived), "DerivedSha256": sha(derived),
            "TopologyValidity": "Not inferred by freezer; runner must report NTS validity for every original feature.",
            "Features": rows}
    if role == "queries":
        info["Categories"] = dict(sorted(Counter(r["SourceAttributes"].get("kohdetyyppi") for r in regions).items()))
    return derived, info


def pair_inventory(layers):
    zones, queries = [layer["Features"] for layer in layers]
    candidates = []
    for zone in zones:
        for query in queries:
            a, b = zone["Bounds"], query["Bounds"]
            if a[0] <= b[2] and b[0] <= a[2] and a[1] <= b[3] and b[1] <= a[3]:
                candidates.append([zone["Id"], query["Id"]])
    total = len(zones) * len(queries)
    return {"TotalPairsPerDirection": total, "BoundingBoxCandidatesPerDirection": len(candidates),
            "BoundingBoxRejectsPerDirection": total - len(candidates),
            "CandidateIdsZoneThenQuery": candidates,
            "Filter": "Closed AABB overlap only; no geometry, category, timing or result filter."}


def verify_service_metadata():
    caps = ET.fromstring((ROOT / "source" / "capabilities.xml").read_bytes())
    ns = {"wfs": "http://www.opengis.net/wfs/2.0", "xsd": "http://www.w3.org/2001/XMLSchema"}
    types = {f.findtext("wfs:Name", namespaces=ns): f for f in caps.findall("wfs:FeatureTypeList/wfs:FeatureType", ns)}
    for _, stem, layer, _ in SPECS:
        entry = types["avoindata:" + layer]
        require(entry.findtext("wfs:DefaultCRS", namespaces=ns) == "urn:ogc:def:crs:EPSG::3879", "Wrong native CRS")
        require(EPSG in [n.text for n in entry.findall("wfs:OtherCRS", ns)], "Requested CRS not advertised")
        schema = ET.fromstring((ROOT / "source" / (stem + "-schema.xsd")).read_bytes())
        geometry = schema.find(".//xsd:element[@name='geom']", ns)
        require(geometry is not None, "Missing geometry field in source schema")
    for filename, _ in PAGES[:2]:
        page = (ROOT / "source" / filename).read_text("utf-8")
        require("creativecommons.org/licenses/by/4.0" in page, "Dataset license link missing in saved metadata")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download", action="store_true", help="Create a new fixture; never overwrite an existing manifest")
    args = parser.parse_args()
    manifest_path = ROOT / "manifest.json"
    if args.download:
        require(not manifest_path.exists(), "Manifest exists. Freeze a new population in a separate directory.")
        protocol_hash = sha((ROOT / "PROTOCOL.md").read_bytes())
        source_files = [fetch("capabilities.xml", wfs(request="GetCapabilities"))]
        source_files.extend(fetch(name, url) for name, url in PAGES)
        for _, stem, layer, _ in SPECS:
            source_files.append(fetch(stem + "-schema.xsd", wfs(request="DescribeFeatureType", typeNames="avoindata:" + layer)))
            hit_url = wfs(request="GetFeature", typeNames="avoindata:" + layer, resultType="hits")
            source_files.append(fetch(stem + "-hits-before.xml", hit_url))
            source_files.append(fetch(stem + ".geojson", wfs(request="GetFeature", typeNames="avoindata:" + layer,
                srsName="EPSG:3067", outputFormat="application/json", sortBy="id", count=10000)))
            source_files.append(fetch(stem + "-hits-after.xml", hit_url))
        verify_service_metadata()
        derived = [derive(*spec) for spec in SPECS]
        layers = [info for _, info in derived]
        manifest = {"SchemaVersion": 1, "SelectionProtocol": "PROTOCOL.md", "ProtocolSha256": protocol_hash,
                    "FrozenUtc": datetime.now(timezone.utc).isoformat(),
                    "Source": "City of Helsinki official open-geodata WFS; full district and VISTRA area layers",
                    "License": "Creative Commons Attribution 4.0 (data only; not replaced by repository MIT license)",
                    "LicenseUrl": "https://creativecommons.org/licenses/by/4.0/",
                    "SpatialReference": "EPSG:3067 / ETRS89-TM35FIN(E,N)", "CoordinateUnit": "metre",
                    "CoordinateChanges": "Source service reprojection from EPSG:3879 to EPSG:3067. No local XY, direction or closure changes; roles repackaged from GeoJSON hierarchy.",
                    "Exclusions": "None; all source features retained; validity assessed separately before comparator timings.",
                    "SourceFiles": source_files, "Layers": layers, "PairInventory": pair_inventory(layers)}
        for raw, info in derived:
            (ROOT / info["DerivedFile"]).write_bytes(raw)
        manifest_path.write_bytes(encode(manifest))
    manifest = json.loads(manifest_path.read_bytes())
    require(sha((ROOT / "PROTOCOL.md").read_bytes()) == manifest["ProtocolSha256"], "Selection protocol changed")
    for entry in manifest["SourceFiles"]:
        raw = (ROOT / entry["File"]).read_bytes()
        require(len(raw) == entry["Bytes"] and sha(raw) == entry["Sha256"], f"Source hash mismatch: {entry['File']}")
    verify_service_metadata()
    derived = [derive(*spec) for spec in SPECS]
    for (raw, info), expected in zip(derived, manifest["Layers"]):
        require(info == expected, f"Inventory drift: {info['Role']}")
        require((ROOT / info["DerivedFile"]).read_bytes() == raw, f"Derived coordinates/order changed: {info['Role']}")
    require(pair_inventory([info for _, info in derived]) == manifest["PairInventory"], "Pair inventory drift")
    print(json.dumps({"VerifiedOffline": not args.download, "SourceFiles": len(manifest["SourceFiles"]),
                      "Features": sum(l["SourceFeatures"] for l in manifest["Layers"]),
                      "VerticesIncludingClosures": sum(l["Vertices"] for l in manifest["Layers"]),
                      "Pairs": manifest["PairInventory"]["TotalPairsPerDirection"],
                      "BoundingBoxCandidates": manifest["PairInventory"]["BoundingBoxCandidatesPerDirection"]}, indent=2))


if __name__ == "__main__":
    main()
