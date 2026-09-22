# ADR 0003 — Map tile provider

- **Status:** Accepted
- **Date:** 2026-09-18
- **Component:** A — Incident & Disaster Map (React console only)
- **Decision owner:** Student A

## Context

The project proposal names map tiles as the platform's **primary third-party
integration**, and is specific about why:

> Primary integration: OpenStreetMap tiles with Leaflet for the live disaster
> map, and OSRM for routing/ETA... This is free, **requires no API key**, and
> directly powers both the citizen map screen and the Resource & Logistics
> Planning Agent's routing tool. *Only one third-party integration is required
> by the module.*

Both clients were built that way and worked: the React console used
`tile.openstreetmap.org` through react-leaflet, the Flutter app the same tiles
through flutter_map. This ADR records replacing that provider.

## Decision

**Mapbox in the React console, CARTO Voyager in the Flutter app.** Neither
client draws raw OpenStreetMap tiles any more, though both still render
OpenStreetMap *data*.

| Client | Tile source | Details | Where |
| --- | --- | --- | --- |
| React (coordinator) | Mapbox `streets-v12`, OSM without a token | 512 @2x, `zoomOffset -1` | `src/config/tiles.ts` |
| Flutter (citizen) | CARTO Voyager | 256 @2x, **keyless** | `disaster_map_screen.dart` |

Both were switched to Mapbox initially. The Flutter map then rendered grey — no
tiles — on the development emulator, while the React map worked from the same
token. The tile URL itself was verified good at every zoom level the app
requests (HTTP 200, real PNGs, 58–87 KB), so the fault lay between the emulator
and Mapbox rather than in the URL or the credential. Rather than spend the
remaining days before the deadline on an emulator networking problem, the mobile
client was moved off Mapbox.

It went to CARTO Voyager rather than back to raw OpenStreetMap. CARTO renders
OpenStreetMap data with considerably better cartography and still requires no
API key, so the citizen app gains legibility without gaining a credential — the
property that made the keyless option attractive in the first place. Tiles were
verified at the zoom levels the app uses (HTTP 200, 20–39 KB PNGs).

The root cause of the Mapbox failure on the emulator was never established. A
stale Flutter build is as plausible as an emulator networking fault, and the
`errorTileCallback` added to diagnose it was never read before the provider was
switched. This is recorded as an unresolved question rather than a finding.

Leaflet and flutter_map are unchanged throughout — only the tile URL moved. The
mapping libraries the proposal names are still the mapping libraries in use.

### Why leaving them different is acceptable

The two maps serve different users and are never seen side by side: a
coordinator works in the console, a citizen on their phone. The data drawn on
top — incident markers, severity colours, safety-zone circles — is identical and
comes from the same API, so the *information* is consistent even though the
basemap is not. Had these been two views for the same user, the split would not
have been acceptable.

It also leaves the citizen-facing app entirely free and keyless, which is the
half of the system the proposal's no-cost argument was really about. Nothing
sensitive ships in the APK.

The style is `streets-v12` by default because legible road names are what a
coordinator routing a team actually needs. `outdoors-v12` (terrain contours, of
interest in landslide country) and `satellite-streets-v12` are one configuration
value away in the console.

### Token handling

**No token is committed.** It is read from the environment at build time and
lives in `frontend/.env`, which is gitignored:

```bash
VITE_MAPBOX_TOKEN=pk.…
VITE_MAPBOX_STYLE=mapbox/outdoors-v12   # optional
```

The token was briefly hard-coded as a fallback, on the reasoning that a `pk.`
token is public by design — it ships in the JavaScript bundle and anyone can
read it, so hiding it in the repository achieves little. GitHub's push
protection rejected the push anyway, which was the right call: a credential in
version control is wrong whether or not the scanner is being strict about it,
and the repository is the one place it should never be.

**Without a token the console falls back to OpenStreetMap.** A teammate can
clone and run the project with no Mapbox account and get a working map, which
also means the Flutter and React maps look identical in that case. `TILE_SIZE`
and `TILE_ZOOM_OFFSET` switch with the provider, because Leaflet assumes 256px
tiles and Mapbox serves 512px ones.

Protection for the token itself comes from **URL restrictions configured in the
Mapbox account**, not from where it is stored. The Flutter app holds no Mapbox
token at all, so nothing ships in the APK.

## Consequences

**Positive**

- Better cartography and a retina-resolution tile set, which matters on a map
  whose entire job is to be read quickly under pressure.
- Satellite and terrain styles become available by changing one value — useful
  for a hazard type like landslides.
- The module's requirement for a third-party integration is still satisfied;
  the integration is simply Mapbox rather than OpenStreetMap.
- Mapbox serves from a CDN with a usage dashboard, where the public OSM tile
  servers are volunteer-funded infrastructure with a usage policy that a
  demonstrated application is expected to respect.

**Negative — and these are the substance of this decision**

- **It contradicts the proposal directly.** The proposal praises OSM for
  requiring *no API key*; this replaces it with a provider that requires one.
  That sentence is now false about the running system, and this document is the
  correction.
- **It replaces the integration the proposal nominated as primary.** Not a
  secondary or optional one — the named one.
- **A quota now exists.** Mapbox's free tier covers 50,000 map loads per month,
  which is far beyond anything this project will do during assessment, but it is
  a commercial relationship with a billing threshold where there was none.
- **Third external dependency.** With [ADR 0001](0001-llm-provider.md) (Gemini)
  and [ADR 0002](0002-incident-photo-storage.md) (Cloudinary), the platform now
  depends on three hosted services. The proposal's risk table promised to avoid
  *"reliance on paid or unstable external services"* and committed to free,
  keyless ones. That commitment has not been kept, and it is more honest to say
  so in one place than to defend each deviation in isolation.

**Neutral**

- OpenStreetMap data underlies both providers' tiles, and the OSM credit is
  retained in both clients' attribution — alongside Mapbox's in the console and
  CARTO's in the app, as both providers' terms require.
- OSRM routing is untouched by this. It remains free and keyless, and belongs to
  Component C's Resource & Logistics Planning Agent.

## Reverting

This is the cheapest of the three deviations to undo, which is worth knowing if
the deviation is challenged. Only the console is affected, and it is one
constant:

Remove `VITE_MAPBOX_TOKEN` from `frontend/.env` and rebuild — `tiles.ts` falls
back to OpenStreetMap on its own, including the tile size and zoom offset. No
code, component, agent or database change is involved, and the Flutter app is
already there.

## Alternatives considered

| Option | Rejected because |
| --- | --- |
| Keep OpenStreetMap, per the proposal | Works and costs nothing, but the owner chose Mapbox's cartography and style range. |
| Mapbox on both clients | Attempted first. The Flutter map rendered grey on the emulator while the same token worked in the browser, and an emulator networking problem was not worth the remaining schedule. |
| A keyless alternative (Carto, Stadia) | Better tiles than raw OSM without a token, but does not offer the satellite and terrain styles that motivated the change. |

## Required follow-up

Set URL restrictions on the public token in the Mapbox account. Until that is
done, the token works from anywhere it is copied to.

## Related

- `frontend/src/config/tiles.ts` — React tile source, Mapbox or OSM by env
- `mobile/lib/screens/map/disaster_map_screen.dart` — Flutter tile layer (CARTO Voyager)
- [ADR 0001](0001-llm-provider.md), [ADR 0002](0002-incident-photo-storage.md) —
  the project's other two deviations from the no-external-services commitment
