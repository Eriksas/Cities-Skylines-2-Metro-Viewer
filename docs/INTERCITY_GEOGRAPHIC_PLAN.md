# Intercity Geographic v2 Plan

## Status

IG2.0 through IG2.2 are code-side complete as of 2026-07-28. Passenger-rail
rendering is connected only through hidden/default-off renderer options;
player-facing controls are not connected.

The current product remains metro/subway only. This is a new, independent
implementation track for passenger intercity rail. It does not reactivate the
archived tram/intercity patch series.

IG2.0 established:

- a pure, portable `MetroDiagram.Engine.IntercityGeographicGeometry`
  clipping/projection helper;
- synthetic outside-tail and through-route fixtures;
- a local real-city fixture inventory for 奥斯特, 肇庆, and 谢菲尔德;
- a reproducible metro-only geographic SVG hash/DOM baseline;
- a renderer-independent railway style comparison.

The selected visual candidate is a continuous dark-grey railway with equally
spaced white sleeper marks. It is now wired into desktop and portable
geographic rendering behind development-only options.

IG2.1 added an isolated `PassengerIntercityRailCollector`, explicit cargo
vehicle/station exclusions, CurveElement-first path extraction, and
developer-only diagnostics. The collector is gated by a hidden setting that
defaults to false, so the existing metro-only export remains unchanged unless
the development gate is deliberately enabled.

IG2.2 connected the shared clipping/projection result to both renderers and
excluded outside stations and tails from the city frame. The first candidate
also drew continuation arrows, but the 2026-07-29 polish pass removed those
arrows from both renderers because the clipped railway endpoint already
communicates continuation without adding a visually awkward symbol. The
three-city offline audit now requires matching desktop/portable railway-part
counts and zero rendered continuation arrows.

The 2026-07-29 visual stabilization pass replaced SVG `stroke-dasharray` with a
shared arc-length pattern generator. It emits explicit equal-length white
inserts for desktop routes, portable routes, and their legend samples. The
three-city audit now fails on legacy dashed intercity route layers and records
the explicit pattern counts. Browser screenshots pass visual review; the
remaining gate is owner validation in CS2/Coherent at multiple zoom levels.
The railway base uses about 72% of metro route width, with one shared
desktop/portable resolver and a portable legibility floor.

## Product Goal

Add passenger intercity rail to geographic maps while keeping the rendered
rail alignment highly faithful to the physical CS2 route.

The product rule is:

```text
Metro remains the primary network.
Intercity rail is an optional geographic overlay.
Physical route geometry is preserved before cartographic decoration.
```

The first accepted version must be useful even if intercity rail never appears
in a schematic layout.

## Scope

### Included

- Passenger intercity train lines only.
- Exact `RouteSegment.CurveElement` / `pathPoints` geometry where available.
- Passenger railway stations and clean clipped outside-connection endpoints.
- Desktop Viewer geographic rendering.
- CLI geographic rendering.
- In-game geographic preview after offline and owner validation.
- A separate, default-off export/render setting.
- Real regression fixtures from multiple available cities.

### Excluded

- Tram.
- Freight or cargo train lines.
- Intercity rail in `schematic-map`, `schematic-v2`, or legacy schematic modes.
- Automatic rail schematic layout.
- Shared-corridor styling between metro and railway.
- Changes to metro stop semantics.
- Structural changes to `metro-export.json`.
- PDX publication before owner validation in CS2.

## Why The Previous Attempt Failed

The parked experiment mixed four separate problems:

1. tram export;
2. passenger train export;
3. desktop multimodal rendering;
4. portable in-game schematic rendering.

The geographic train data was usable, but very long outside-connection tails
participated in map bounds and compressed the visible city. Desktop and
portable renderers also developed separate geometry behavior. The schematic
then had to invent a multimodal visual grammar before the geographic contract
was stable.

The new track removes those coupled risks. It starts with one mode, one layout,
one geometry contract, and one shared implementation.

## Data Contract

The existing line model already contains:

```text
line.mode
line.stops
line.pathPoints
```

Intercity lines will use `line.mode = "train"` or one consistently documented
equivalent. This is a semantic extension of the existing `mode` field, not a
new JSON structure or schema-version change.

`line.stops` remains the passenger stopping sequence. `line.pathPoints`
contains the physical route geometry and does not imply station stops.

All game-specific ECS reading remains inside the CS2 Mod/Exporter projects.
Core, Rendering, CLI, and Viewer consume the existing document model.

## Exporter Architecture

Add an isolated passenger-rail collector instead of expanding the metro query
in place:

```text
RealMetroJsonExporter
  -> existing metro collector
  -> optional PassengerIntercityRailCollector
  -> one MetroExportDocument
```

The collector must:

- require passenger `TransportType.Train` evidence;
- exclude cargo vehicle/station prefabs using confirmed cargo component data;
- preserve line entity IDs and display names;
- export real passenger stops only;
- read `RouteSegment` in order;
- sample `CurveElement.m_Curve` before using lower-quality fallbacks;
- record all filters and fallbacks in diagnostics;
- fail per line without failing the metro export.

The opt-in remains hidden/developer-only until the offline geographic output is
accepted.

## Geographic Geometry Contract

### One shared geometry pipeline

Desktop and portable in-game geographic rendering must call the same
`IntercityGeographicGeometry` helper. No duplicate clipping or projection logic
is allowed in JavaScript and C#.

The helper returns render-ready polylines and continuation markers. Presentation
layers may style those results differently, but may not recompute geometry.

### Preserve physical alignment

For in-city railway geometry:

- keep path-point order;
- use one uniform scale plus translation;
- invert the vertical axis only as required by SVG coordinates;
- do not grid-snap;
- do not insert schematic guide nodes;
- do not simplify initially;
- if simplification is later enabled, keep projected deviation below a
  conservative sub-pixel tolerance.

This preserves angles, relative distances, bends, and route direction as seen
in the CS2 geographic map.

### Clip outside-connection tails

Outside tails must never participate in visible network bounds.

1. Build a city envelope from finite metro/passenger station positions.
2. Expand it by a conservative geographic margin.
3. Keep every path segment inside that envelope.
4. Intersect an exiting segment with the envelope boundary.
5. Replace the remaining off-map tail with a short continuation stub and arrow.
6. Retain station-to-boundary geometry when a line has one in-city terminal and
   one outside connection.

The same rule applies to every city. It must not use city, line, or station
names.

## Cartographic Grammar

The first version intentionally stays restrained:

- railway stroke thinner than metro;
- neutral rail color with a darker edge or sleeper treatment;
- small square or rail-style station marker;
- distinct outside-continuation arrow;
- actual line and station names where available;
- no heavy hatching that obscures nearby metro lines;
- station and label layers remain above route geometry.

When the user selects a schematic layout, intercity rail stays hidden and the
UI may show a subtle note:

```text
Intercity rail is currently available in geographic layout only.
```

## Implementation Stages

### IG2.0 - Baseline and fixtures

- Work from the stable metro-only tree; use a dedicated feature branch before
  exporter integration is committed.
- Treat `archive/tram-intercity-experiment-20260726` as read-only research.
- Freeze metro-only geographic outputs before changing code.
- Add real fixtures from the available 奥斯特, 肇庆, and 谢菲尔德 exports.
- Add synthetic fixtures for:
  - one city terminal plus one outside connection;
  - a through-running passenger line;
  - a metro/rail interchange;
  - a cargo train that must be excluded.

Exit gate: fixture inventory and byte/DOM-level metro-only regression baseline
are reproducible.

Status: passed code-side on 2026-07-28. Evidence:

```text
src\MetroDiagram.Engine\IntercityGeographicGeometry.cs
samples\intercity-geographic\
scripts\initialize-intercity-geographic-baseline.ps1
scripts\generate-intercity-style-study.ps1
artifacts\intercity-geographic-v2\baseline\
artifacts\intercity-geographic-v2\style-study\
```

The helper exists early to make the clipping/projection contract testable.
Desktop and portable renderer integration remains an IG2.2 task.

### IG2.1 - Passenger rail export

- Add isolated passenger-train collection.
- Add cargo exclusion.
- Export stops and CurveElement-first path geometry.
- Add per-line diagnostics:
  - passenger/cargo classification;
  - route segment and path-point counts;
  - station count;
  - outside-connection references;
  - fallback reasons.

Exit gate: every real passenger line is exported, no freight line is exported,
and metro-only behavior is unchanged when the feature is off.

Status: passed code-side on 2026-07-28. Evidence:

```text
CS2 Metro\PassengerIntercityRailCollector.cs
src\MetroDiagram.Engine\PassengerIntercityRailClassifier.cs
scripts\audit-intercity-real-fixtures.ps1
artifacts\intercity-geographic-v2\classification-audit\
```

The solution build, full tests, and CS2 Release post-process pass. The archived
real-city inventory contains 17 `mode=train` lines and 46,830 CurveElement path
points across 奥斯特, 肇庆, and 谢菲尔德. Every archived train line has more
path points than stops.

This does not close the in-game release gate. The archived exports predate the
new collector, and cargo-name checks are heuristic only. A newly built mod must
still be validated in CS2 with passenger and cargo rail present before the
hidden setting may be exposed.

### IG2.2 - Shared geographic geometry

- Implement the shared clipping/projection helper.
- Exclude off-map tails from bounds.
- Generate continuation stubs and arrows.
- Use the same helper from desktop and portable renderers.

Exit gate: no outside tail compresses or expands the city map, and desktop and
portable geometry agree.

Status: passed code-side on 2026-07-28. Evidence:

```text
src\MetroDiagram.Rendering\MetroSvgRenderer.Intercity.cs
src\MetroDiagram.Engine\PortableMetroSvgRenderer.cs
scripts\generate-intercity-geographic-render-audit.ps1
artifacts\intercity-geographic-v2\renderer-audit\
```

The renderer audit reports exact desktop/portable parity:

| City | Railway parts | Continuations |
| --- | ---: | ---: |
| 奥斯特 | 6 | 4 |
| 肇庆 | 22 | 10 |
| 谢菲尔德 | 7 | 2 |

This closes the offline geometry gate only. A new in-game export with passenger
and cargo rail is still required before any control is exposed.

### IG2.3 - Viewer, CLI, and in-game controls

- Add `Show intercity rail (geographic only)` to Viewer, default off.
- Add an equivalent CLI option, default off.
- Add the in-game option only after IG2.0-IG2.2 visual acceptance.
- Disable or ignore the option in schematic layouts with a clear note.
- Keep train export and train display as separate settings.

Exit gate: existing users see no changed output until they explicitly opt in.

### IG2.4 - Visual audit

For each real fixture generate:

```text
geographic-metro-only.svg
geographic-metro-intercity.svg
geographic-metro-intercity-debug.svg
intercity-geographic-audit.txt
```

The audit records:

- line/station/path-point counts;
- source and clipped bounds;
- clipped-tail count;
- maximum source-to-render projection deviation;
- lines rejected and reasons;
- desktop/portable geometry parity.

Compare the output with a CS2 transport-map screenshot from the same city.

### IG2.5 - Owner validation and release decision

- Run solution build/tests and CS2 post-process.
- Validate in the real CS2 Coherent preview.
- Verify multiple open/close and refresh cycles.
- Verify export with zero, one, and several passenger rail lines.
- Require explicit owner approval before exposing the option or publishing.

## Acceptance Criteria

The track can close only when:

1. metro-only output has no regression when intercity is off;
2. all known passenger intercity lines render continuously;
3. no cargo line or cargo station is exported;
4. in-city rail geometry remains geographically faithful;
5. outside tails do not affect city framing;
6. station markers and labels remain inside the page;
7. desktop and in-game geographic geometry use the same result;
8. three real-city fixtures and all synthetic fixtures pass;
9. build, tests, CS2 post-process, and manual in-game validation pass;
10. the owner explicitly approves release.

## First Implementation Step

Start IG2.0 only. Do not copy the archived experiment wholesale.

The first code change should be a pure, testable geographic clipping/projection
helper plus fixture tests. Exporter integration comes after that geometry
contract is proven.
