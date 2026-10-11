# Frozen Helsinki districts and planning areas

This fixture contains the complete official City of Helsinki **basic-district
division** and **prospective green/recreation area plan**, requested from its
open WFS service on **11 October 2026**. It is new input for PolylineKit,
independent of the previously used Census, Natural Earth and building samples.
The two Helsinki layers share a municipal provider; they are not independent
measurements of ground truth.

The practical numerical question is: **how much of each planning-area feature
lies in each district, and what fraction of either feature does it cover?**
This is a pairwise region comparison. The planning layer contains overlapping
categories, including proposed construction pixels and islands. It is not an
inventory of present-day parks, and adding its feature areas does not produce
the union of disjoint green space.

## Complete population

The [protocol](PROTOCOL.md) was written before the full feature downloads or
geometric measurements. No source feature is excluded by the freezer.

| Inventory | Districts (`zones.json`) | Planning areas (`queries.json`) |
|---|---:|---:|
| Features | 34 | 1,095 |
| GeoJSON Polygon | 34 | 1,038 |
| GeoJSON MultiPolygon | 0 | 57 |
| Polygon components | 34 | 1,296 |
| Rings | 34 | 2,132 |
| Holes | 0 | 836 |
| Features with holes | 0 | 52 |
| Supplied vertices, including ring closures | 9,165 | 207,665 |
| Minimum / maximum vertices per feature | 33 / 597 | 4 / 30,536 |
| Null, empty or unsupported geometry | 0 | 0 |

Total: **1,129 features, 2,166 rings and 216,830 supplied vertices**. Every
ring is closed and has at least three distinct finite 2D points. These are
structural checks, **not topology validity assertions**. The runner separately
reports every original NTS-invalid feature and the corresponding comparative
population; it must not silently repair the input.

There are **37,230 cross-layer pairs in each direction**. Closed AABB overlap
retains **2,560** candidate pairs, including possible boundary contact, and
rejects 34,670 disjoint bounds. Candidate IDs, every feature's bounds and
inventory, all 19 source planning categories, and source/derived hashes are
recorded in [manifest.json](manifest.json).

## Frozen requests and coordinates

The service advertises native ETRS-GK25 / EPSG:3879 and the requested
**EPSG:3067, ETRS89 / TM35FIN(E,N)**. Its returned axes are easting/northing
in metres. The full GeoJSON responses explicitly identify
`urn:ogc:def:crs:EPSG::3067`.

- [Complete districts request](https://kartta.hel.fi/ws/geoserver/avoindata/wfs?service=WFS&version=2.0.0&request=GetFeature&typeNames=avoindata%3APiirijako_peruspiiri&srsName=EPSG%3A3067&outputFormat=application%2Fjson&sortBy=id&count=10000)
- [Complete planning areas request](https://kartta.hel.fi/ws/geoserver/avoindata/wfs?service=WFS&version=2.0.0&request=GetFeature&typeNames=avoindata%3ATavoitteellinen_viher_ja_virkistysverkosto_VISTRA_alue&srsName=EPSG%3A3067&outputFormat=application%2Fjson&sortBy=id&count=10000)

Neither request supplies a spatial/attribute filter, simplification,
quantization or geometry precision setting. Count-only queries immediately
before and after each full request agree with all returned features. Raw
responses, WFS capabilities, layer schemas and official metadata/license pages
are retained under `source/`, byte-for-byte with UTC times, response headers
and SHA-256 hashes. The live URL may change; the frozen bytes define this fixture.

| File | Bytes | SHA-256 |
|---|---:|---|
| `source/districts.geojson` | 333,183 | `fa41b7e391d9b5042e816d25d7723d222aba139436e3470ce1afbc0580e7da44` |
| `source/vistra.geojson` | 7,657,646 | `6bcf9a8e493be6743cad66c55a8bedf213a30c5b1a190423bb0aaf81f676465a` |
| `zones.json` | 333,089 | `c6b430681a4ed88a08c55a4dae50e1451517301a27c6b4c221e1e1db96fbc617` |
| `queries.json` | 7,676,613 | `59078c91426659dd9998b6b144e90db021c0cbbe1ac1e557d86087af12d66876` |

The derived files repackage only the explicit GeoJSON hierarchy:
`Regions[].Polygons[].Shell` is the first ring and `Holes` are subsequent
rings in that component. They retain the numeric source ID, original feature
identifier and properties. Component, ring and point order, repeated closing
vertices and the binary64 values parsed from source numbers are unchanged.
No independent components are joined. There is no local reprojection,
translation, scaling, reorientation, rounding, simplification or repair.
Service reprojection and its serialization are source processing; the fixture
does not claim access to unrounded native survey coordinates.

## Offline verification

From the repository root:

```powershell
python benchmarks/PolylineKit.IndependentRegions/data/freeze.py
```

The default uses the Python standard library only and makes no network
requests. It verifies the protocol hash, all 12 source files, native and
requested CRS declarations, count completeness and unique ordered IDs. It
recreates both derived files from the raw GeoJSON and compares their complete
bytes, inventory and all closed-AABB candidate IDs. Thus checking only a
summary count cannot hide changed coordinates or hole roles.

`--download` creates a new freeze **only when `manifest.json` is absent**.
Use a separate directory and a new declared protocol for a future snapshot;
do not overwrite this published population. A downloader must have a valid
TLS trust chain for the official hosts; verification is never disabled.

## Data license and attribution

These data are **Creative Commons Attribution 4.0**, independently of the
repository's MIT-licensed code. The official dataset pages and license
declarations are retained in `source/` and linked here:

- [Helsinki district division (`Helsingin piirijako`)](https://hri.fi/data/dataset/helsingin-piirijako).
  Maintainer: City of Helsinki Urban Environment Division / City Survey Services
  (`Helsingin kaupunkiympäristön toimiala / Kaupunkimittauspalvelut`).
- [Helsinki prospective green/recreation network (`Helsingin tavoitteellinen viher- ja virkistysverkostokartta`)](https://hri.fi/data/dataset/helsingin-tavoitteellinen-viher-ja-virkistysverkostokartta).
  Maintainer: City of Helsinki Urban Environment Division / Land Use and City
  Structure / Urban Space and Landscape Planning
  (`Maankäyttö ja kaupunkirakenne / Kaupunkitila ja maisemasuunnittelu`).
- [CC BY 4.0 license](https://creativecommons.org/licenses/by/4.0/).
- [City of Helsinki open geodata terms](https://www.hel.fi/fi/paatoksenteko-ja-hallinto/tietoa-helsingista/kartat-ja-paikkatieto/paikkatiedot-tehokayttoon/avoimet-paikkatiedot).

Attribution: **Source: Helsinki district division and Helsinki prospective
green/recreation network; City of Helsinki, respective maintainers above;
Helsinki Region Infoshare / official Helsinki WFS. Downloaded 11 October 2026
under CC BY 4.0. The source service was requested to reproject to EPSG:3067;
PolylineKit repackaged the returned GeoJSON hierarchy as shell/hole JSON,
without subsequent coordinate or geometry changes.** No endorsement is implied.
