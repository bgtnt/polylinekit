# North Carolina administrative coverage inputs

Source: U.S. Census Bureau, TIGERweb **Generalized ACS2024**, January 1, 2024
vintage, 1:5 million county and 119th congressional district boundaries. Retrieved
2026-09-25. See [the selection protocol](PROTOCOL.md), declared before timing,
and [manifest.json](manifest.json) for all URLs, retrieval times, byte hashes,
attributes, source exclusions and the complete bounding-box candidate inventory.

| Population | Source features | Complete single-ring polygons | Excluded | Admitted vertices |
|---|---:|---:|---:|---:|
| Counties (`zones.json`) | 100 | 98 | 2 | 6,660 |
| Congressional districts (`queries.json`) | 14 | 11 | 3 | 3,928 |

These are structural counts, **before independent validity checks**. The runner
must report invalid geometries separately and must never repair them silently.
The structural population has 1,078 pairs in each direction: 211 pairs have
overlapping closed bounding boxes, including boundary contacts; 867 do not.
No candidate is filtered by intersection area, convexity, nesting, numeric
agreement, or measured speed.

Excluded source features remain complete in the frozen responses:

| GEOID | Feature | Reason |
|---|---|---|
| 37055 | Dare County | MultiPolygon, four components |
| 37095 | Hyde County | MultiPolygon, two components |
| 3702 | Congressional District 2 | MultiPolygon, two components |
| 3703 | Congressional District 3 | MultiPolygon, four components |
| 3713 | Congressional District 13 | Polygon with one hole |

Every coordinate is the Census server's projected binary64 value in
**EPSG:5070, NAD83 / Conus Albers**, in metres. The exported `Points` arrays retain
source order and values; only the exact repeated closing vertex is removed.
The source uses projected GeoJSON with an explicit CRS member, rather than
RFC 7946 longitude/latitude coordinates. Consumers must honor EPSG:5070.
The runner may subtract the single common origin specified in the protocol;
these files retain the untranslated values. No local projection library,
simplification, resampling, normalization, or geometry repair is used to freeze
them. Source `AREALAND` / `AREAWATER` attributes are retained as metadata, not used
as planar area oracles.

`zones.json` and `queries.json` contain:

```json
{
  "SchemaVersion": 1,
  "Role": "zones",
  "SpatialReference": 5070,
  "CoordinateUnit": "metre",
  "Regions": [
    {"Id": "GEOID", "Name": "source name", "SourceAttributes": {}, "Points": [[0, 0]]}
  ]
}
```

The example above shows the schema only. Each real ring has at least three
distinct points and closes implicitly. `manifest.json` inventories all source
features; `source/*.geojson` preserve their complete original geometry and
attributes, including excluded holes and components. The corresponding layer
and count-query JSON responses preserve source metadata and verify complete
retrieval. Feature ordering is `GEOID`, requested before download.

To verify hashes and reproduce the exported data in memory, offline, using only
the Python standard library:

```text
python examples/RegionCoverage/data/freeze.py
```

To create a **new** freeze, copy `freeze.py` and `PROTOCOL.md` to a separate
directory without an existing manifest, and run `python freeze.py --download`.
The script refuses to download over an existing manifest. A changed upstream
response is a different dataset, even if the feature IDs have not changed.

This is U.S. government geographic data, public domain in the United States;
the layer metadata credits the U.S. Census Bureau. The Census
[public-access policy, page 9](https://www2.census.gov/foia/ds_policies/ds027.pdf)
describes the copyright status of employee-created work, and its
[citation guidance](https://www.census.gov/about/policies/citation.html) explains
source attribution. The Census Bureau is the source of the original data;
the benchmark's analysis and conclusions belong to this project.

The two administrative layers share a provider and may share boundary segments.
They are not independent observations and this experiment does not validate
land-cover classification, demographic estimates, or a general GIS workload.
