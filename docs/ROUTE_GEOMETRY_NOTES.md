# Route Geometry Notes

This document consolidates the older route-geometry discovery and validation notes. It is the current entry point for `line.pathPoints`, `RouteSegment.CurveElement`, route geometry diagnostics, and path geometry comparison workflows.

## Current Status

The exporter keeps the existing JSON schema and station semantics:

- `line.stops` remains the station stop sequence.
- `line.pathPoints` is optional route geometry for geographic drawing.
- Viewer, CLI, and renderer behavior can use `pathPoints`, but the raw export schema is not expanded for diagnostics.

The latest validated real city showed:

- 11 subway lines,
- 48 stations,
- 157 route segments,
- 9739 exported `line.pathPoints`,
- all final path points sourced from `RouteSegment.CurveElement`,
- 0 final `RouteSegment.PathTargets` fallback points,
- 0 CurveElement read failures.

This means `RouteSegment.CurveElement` is currently the primary route-geometry source for the validated export.

## Why pathPoints Exist

`line.stops` is sparse by design. It is good for station circles, labels, terminals, and interchange logic, but it cannot describe the physical path between served stations. Express or cross-stop services can jump directly from one served station to another if rendering only follows stops.

`line.pathPoints` gives the renderer intermediate route geometry. Geographic rendering can then follow the route corridor more closely while station drawing still comes from `line.stops`.

## Extraction Order

The CS2 real exporter currently tries these sources in order:

1. `RouteSegment.CurveElement`
2. `RouteSegment.PathElement`
3. `RouteSegment.PathTargets`

`CurveElement` is expected to expose `m_Curve=Colossal.Mathematics.Bezier4x3`. The exporter samples readable Beziers before falling back to sparse start/end target points.

## Track Geometry Debug Discovery

The in-game debug button:

```text
Options > CS2 Metro Diagram > Main > Debug > Export Transport Debug Dump
```

Writes the original debug files:

```text
D:\CS2MetroDiagram\debug-dump.json
D:\CS2MetroDiagram\debug-dump.txt
```

It also writes route/track geometry diagnostics:

```text
D:\CS2MetroDiagram\metro-track-geometry-debug.json
D:\CS2MetroDiagram\metro-track-geometry-debug.txt
```

For each recognized subway `TransportLine`, the debug output records route segment counts, sampled segment component types, referenced `Game.Net` entities, geometry-like fields, likely curve source candidates, and per-segment warnings.

## Manual Validation Workflow

Build and deploy the latest mod:

```powershell
scripts\deploy-local-mod.ps1
```

Restart Cities: Skylines II, load a city with subway lines, then click:

```text
Options > CS2 Metro Diagram > Main > Export > Export Real Metro JSON
```

Confirm the latest files exist:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

Phase 4D.0 also writes timestamped snapshots under:

```text
D:\CS2MetroDiagram\exports\
```

Use the analyzer:

```powershell
scripts\analyze-metro-export-json.ps1
```

Generate comparison SVGs:

```powershell
scripts\generate-path-geometry-comparison.ps1
```

The comparison output includes:

```text
01-geographic-stops.svg
02-geographic-pathpoints.svg
03-geographic-pathpoints-simplified.svg
04-schematic-lite.svg
```

## What To Check

Signs that route geometry is healthy:

- `pathPoints` exist in the export.
- Most lines have `pathPoints` count greater than `stops` count.
- Source summary contains `RouteSegment.CurveElement`.
- `PathTargets` fallback count is low or zero.
- Geographic pathPoints output has fewer fly-lines than stop-only output.
- Simplified pathPoints look smoother without losing route shape.

Signs that need more investigation:

- All pathPoints come from `RouteSegment.PathTargets`.
- pathPoints count is equal to or lower than stops count for most lines.
- Raw and simplified pathPoints look nearly identical to stop-only rendering.
- Diagnostics show CurveElement fields but no sampled points.

## Latest Validation Result

Phase 5A.3c validated:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

Key totals:

```text
lines: 11
stops: 157
route segments: 157
CurveElements: 2432
pathPoints before cleanup: 12160
cleaned pathPoints: 9739
CurveElement source count: 9739
PathTargets fallback count: 0
CurveElement read failures: 0
skipped path segments: 0
```

Airport express checks:

- `10号线（机场快线-大站快车）`: 8 stops, 1065 exported pathPoints.
- `10号线（机场快线-特快）`: 4 stops, 1025 exported pathPoints.

Conclusion: CurveElement is the primary source, and express-line fly-lines are considered resolved for the validated export when `--use-path-points` is enabled.

## Historical Source Notes

This file consolidates the older path geometry validation and metro track geometry discovery phase notes. The original per-phase notes were removed from the working tree during project cleanup; use git history if exact historical wording is needed.
