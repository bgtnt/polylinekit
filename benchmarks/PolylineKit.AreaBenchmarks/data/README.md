# Frozen Census contour fixture

Source: U.S. Census Bureau, [TIGERweb State_County layer 11](https://tigerweb.geo.census.gov/arcgis/rest/services/TIGERweb/State_County/MapServer/11).
The frozen layer metadata describes counties with January 1, 2026 vintage. The Census
[REST service directory](https://tigerweb.geo.census.gov/tigerwebmain/TIGERweb_restmapservice.html)
documents public geographic services. Census attribution is retained; the coordinates and
county facts are provided here as a reproducible benchmark fixture, not under an assertion
that the project's MIT license replaces the source's terms.

Query the complete populations of Delaware (STATE 10) and Rhode Island (STATE 44), ordered
by GEOID, with `outSR=5070`, `returnGeometry=true`, and no `maxAllowableOffset`, geometry
precision, quantization or local simplification parameter:

[Exact frozen query](https://tigerweb.geo.census.gov/arcgis/rest/services/TIGERweb/State_County/MapServer/11/query?where=STATE%20IN%20%28%2710%27%2C%2744%27%29&outFields=GEOID%2CSTATE%2CNAME&outSR=5070&returnGeometry=true&orderByFields=GEOID&f=pjson)

The 1,164,917-byte response is retained byte-for-byte as `counties-5070.json` (SHA-256
`a2147b794f7395a103727bc67373b63d33a21dbcb1d601aa32f9a3572b98749c`).
The 7,474-byte layer response is `layer.json` (SHA-256
`0c68871f7842ac564550f837a99ca6dd164d8fb3a084b19b8f7573c9a627a7f0`).
The query was retrieved on 2026-09-25. Upstream data may change; running the current URL does
not replace or redefine this frozen fixture. Metadata lists Web Mercator as the source CRS;
the service returned the requested EPSG:5070 projection, and those returned binary64 values
are used unchanged.

| GEOID | County | Supplied vertices per ring |
|---|---|---|
| 10001 | Kent, Delaware | 2395 |
| 10003 | New Castle, Delaware | 5384 |
| 10005 | Sussex, Delaware | 1856 |
| 44001 | Bristol, Rhode Island | 250 |
| 44003 | Kent, Rhode Island | 2036 |
| 44005 | Newport, Rhode Island | 592 |
| 44007 | Providence, Rhode Island | 2078 |
| 44009 | Washington, Rhode Island | 2008, 69 |

Total: 8 features, 9 rings, 16,668 supplied vertices. All are included; exclusions: none.
No separate rings are joined with synthetic connector edges. Every ring's area is measured
independently, including both Washington County rings; their relationship is not interpreted
as a county union or hole hierarchy. The service geometry can include water boundaries and
source generalization. Its coordinates are test inputs, not cadastral or survey truth.
