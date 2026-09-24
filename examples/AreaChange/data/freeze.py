"""Reproduce the four frozen map-plane contours; Python standard library only.

Run from anywhere. A supplied original GeoJSON avoids downloading:
    python freeze.py /path/to/ne_50m_admin_0_countries.geojson
Without an argument, download the pinned public-domain source (about 3 MB).
"""
import hashlib
import json
import math
from pathlib import Path
import sys
import urllib.request

ROOT = Path(__file__).resolve().parent
URL = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_50m_admin_0_countries.geojson"
SOURCE_SHA256 = "3e458fc036ad0a66411f2c1e6cac49c5d7bfb81cb1123bc513b22511a2b7fdeb"
SOURCE_PAGE = "https://github.com/nvkelso/natural-earth-vector/blob/v5.1.2/geojson/ne_50m_admin_0_countries.geojson"
LICENSE_URL = "https://www.naturalearthdata.com/about/terms-of-use/"
CODES = ("BGR", "CHE", "LSO", "NPL")

raw = Path(sys.argv[1]).read_bytes() if len(sys.argv) > 1 else urllib.request.urlopen(URL).read()
assert hashlib.sha256(raw).hexdigest() == SOURCE_SHA256, "Source bytes changed"
features = {f["properties"]["ADM0_A3"]: f for f in json.loads(raw)["features"]}
manifest = {
    "Source": SOURCE_PAGE, "Download": URL, "SourceSha256": SOURCE_SHA256,
    "SourceVersion": "Natural Earth v5.1.2, 1:50m Admin 0 Countries",
    "License": "Public domain", "LicenseUrl": LICENSE_URL,
    "Selection": "Complete Polygon features with exactly one ring; no island or hole removed.",
    "Transform": "Local equirectangular x=(longitude-minLongitude)*cos(centerLatitude), y=latitude-minLatitude; uniform scale to longest bound 1000. These are map-plane coordinates, not geodesic areas.",
    "Contours": [],
}
for code in CODES:
    feature = features[code]
    geometry = feature["geometry"]
    assert geometry["type"] == "Polygon" and len(geometry["coordinates"]) == 1
    ring = geometry["coordinates"][0]
    assert ring[0] == ring[-1] and len(ring) >= 4
    ring = ring[:-1]  # Implicit closure; no independent contours are joined.
    min_x = min(p[0] for p in ring); max_x = max(p[0] for p in ring)
    min_y = min(p[1] for p in ring); max_y = max(p[1] for p in ring)
    latitude = (min_y + max_y) / 2
    cosine = math.cos(math.radians(latitude))
    scale = 1000 / max((max_x - min_x) * cosine, max_y - min_y)
    points = [[(x - min_x) * cosine * scale, (y - min_y) * scale] for x, y in ring]
    data = {
        "Code": code, "Name": feature["properties"]["ADMIN"],
        "CenterLatitudeDegrees": latitude, "LongitudeCosine": cosine,
        "Scale": scale, "SourceCoordinates": ring, "Points": points,
    }
    encoded = (json.dumps(data, indent=2, ensure_ascii=True) + "\n").encode()
    path = ROOT / (code.lower() + ".json")
    path.write_bytes(encoded)
    manifest["Contours"].append({"File": path.name, "Sha256": hashlib.sha256(encoded).hexdigest(), "Vertices": len(points)})
    print(f"{code}: {len(points)} vertices, {len(encoded)} bytes")
(ROOT / "manifest.json").write_bytes((json.dumps(manifest, indent=2) + "\n").encode())
