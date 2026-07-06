# JSON Schema

This document describes the schema version 1 `metro.json` shape used by the exporter, CLI, renderer, and Viewer.

## Root

```json
{
  "schemaVersion": 1,
  "generator": {},
  "game": {},
  "city": {},
  "network": {}
}
```

## Required Version

- `schemaVersion`: must be `1`.

Unsupported `schemaVersion` values are load errors.

## Generator

- `name`: generator name.
- `version`: generator version, currently `v0.1.0-alpha.2-candidate` for the alpha.2 candidate.

## Game

- `name`: source game name.
- `version`: source game version, or `unknown`.

## City

- `name`: city name. Missing or blank values are normalized to `Unnamed City`.
- `exportedAtUtc`: ISO-8601 UTC timestamp when available.

## Network

- `type`: currently `metro`.
- `stations`: array of station objects.
- `lines`: array of line objects.

## Station

```json
{
  "id": "station_001",
  "name": "Central",
  "position": { "x": 1200.5, "z": 830.2 },
  "lines": ["line_001"],
  "isInterchange": false
}
```

Required fields:

- `id`
- `position.x`
- `position.z`

Fallbacks:

- Missing or blank `name` becomes `Station N`.
- Missing `lines` becomes an empty array.
- Missing `isInterchange` is treated as `false`.
- Missing `position` records a warning; the station is skipped by the renderer until it has coordinates.

## Line

```json
{
  "id": "line_001",
  "name": "Line 1",
  "color": "#D71920",
  "mode": "metro",
  "stops": ["station_001", "station_002"],
  "pathPoints": [
    {
      "x": 1200.5,
      "z": 830.2,
      "source": "RouteSegment.PathTargets",
      "segmentEntity": "123:4"
    }
  ]
}
```

Required fields:

- `id`
- `stops`

Fallbacks:

- Missing or blank `name` becomes `Line N`.
- Missing or invalid `color` receives an internal palette color.
- Missing `mode` becomes `metro`.
- Missing `stops` becomes an empty array.
- Missing `pathPoints` becomes an empty array.

### Optional Path Points

`pathPoints` is optional and older JSON files without it remain valid. It stores render-time route geometry points in source/game coordinates while preserving the stop list as the canonical station sequence.

Required fields for each path point:

- `x`
- `z`

Optional fields:

- `source`: for real CS2 exports this may be `RouteSegment.CurveElement`, `RouteSegment.PathElement`, or `RouteSegment.PathTargets`.
- `segmentEntity`: diagnostic entity id for the segment that produced the point.

Consecutive duplicate or near-duplicate path points may be removed during loading/exporting. The renderer can also clean path points on temporary render data by removing duplicates, very short segments, and nearly-collinear points. This render-time cleanup does not modify the loaded JSON document.

Path points are route geometry only; they do not create stations and do not affect station circles or labels.

## Validation Warnings

The loader records warnings for duplicate IDs, missing station references, invalid colors, missing fallback values, and lines with fewer than two renderable stops.

Missing station references are non-fatal in Phase 1.5. The loader records a clear message in this form:

```text
Missing station reference: line 'line_id' stop 'station_id' does not match any station id; that stop will be skipped.
```

This allows the CLI to generate an SVG from the remaining valid stops while still reporting the data problem.

## Rendering Rules

- `geographic` route geometry uses raw `position.x` and `position.z` coordinates normalized into the SVG canvas.
- When enabled by render options, `geographic` can use `line.pathPoints` for route polylines when at least two path points are present, falling back to `stops` when path data is missing or unusable.
- Geographic path point rendering records `data-path-point-count` and `data-cleaned-path-point-count` on route polylines for quick inspection.
- The renderer reserves a right-side legend area so route polylines do not overlap the legend.
- Stations shared by more than one line, or marked with `isInterchange = true`, render with a larger interchange marker.
- Empty networks render a valid SVG with an empty-network notice.
- Label hiding is a render option; station circles remain visible.
- Complex automatic schematic layout and manual editing are not part of `v0.1.0-alpha.2-candidate`.
