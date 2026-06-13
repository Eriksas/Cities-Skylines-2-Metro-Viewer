# Development Notes

## 2026-05-26

- Started with the existing Cities: Skylines II mod template under `CS2 Metro/`.
- Kept game-specific exporter work out of Phase 0 and Phase 1.
- Created the offline solution under `src/` so Core, Rendering, CLI, and Tests can build without CS2 game assemblies.
- `rg` could not run in this environment because access was denied, so file discovery used PowerShell instead.
- `dotnet new` and `dotnet restore` attempted to read user-level NuGet configuration. A local `NuGet.Config` was added, and restore was completed with explicit configuration.
- The first CLI acceptance command generated `output.svg` from `samples/sample-metro-small.json`.
- The current test project is a simple console test runner instead of xUnit/NUnit/MSTest to avoid external package dependencies during Phase 1.

## Phase 1.5 Notes

- Added samples for branch, loop, missing fields, and a larger five-line network.
- Missing station references are reported with `Missing station reference: ...` and skipped during rendering; this is intentionally non-fatal for now.
- SVG renderer reserves a fixed right-side legend lane by shrinking the coordinate normalization area.
- Generated sample SVGs are written to `samples/generated-svg/` and ignored by `.gitignore`.
- The console tests parse rendered SVG with `System.Xml.Linq.XDocument` to verify the output is legal XML/SVG.

## Phase 1.5 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Render every sample:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $out = Join-Path 'samples\generated-svg' ($_.BaseName + '.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $out
}
```

## Phase 2 Notes

- Used the official local `ColossalOrder.ModTemplate.1.0.0.nupkg` template as the source for settings button patterns.
- The settings UI pattern is `ModSetting`, `RegisterInOptionsUI()`, localization entries, and a bool property with `[SettingsUIButton]`.
- Added `CS2 Metro\Setting.cs` for the options entry and `CS2 Metro\TestMetroJsonExporter.cs` for static JSON export.
- The exporter writes to `Environment.SpecialFolder.MyDocuments\CS2MetroDiagram\test-export.json`; if Documents cannot be resolved, it falls back to the user profile and then temp path.
- The mod logs the export directory on load and logs start/success/failure for every export attempt.
- The local shell did not have `CSII_TOOLPATH` configured, so `CS2 Metro.csproj` now supports `/p:CsiiToolPath=...`.
- The CS2 mod build also needed an explicit `Colossal.Localization` reference once settings localization was added.
- I did not launch CS2 from this environment; in-game load/button verification remains a manual step.

## Phase 2 Build Command Used

```powershell
$tool = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain'
$managed = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$userData = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II'
$unityProject = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\.cache\Modding\UnityModsProject'
$post = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe'
$mscorlib = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\mscorlib.dll'
$localMods = 'E:\CS2\CS2 Metro\artifacts\cs2-local-mods'
dotnet build "CS2 Metro.slnx" --no-restore /p:CsiiToolPath="$tool" /p:ManagedPath="$managed" /p:UserDataPath="$userData" /p:UnityModProjectPath="$unityProject" /p:ModPostProcessorPath="$post" /p:EntitiesVersion="1.3.10" /p:MSCORLIBPath="$mscorlib" /p:LocalModsPath="$localMods"
```

After a successful CS2 mod build/post-process, deploy the latest local mod output before each in-game test:

```text
scripts\deploy-local-mod.ps1
```

The deploy script copies `artifacts\cs2-local-mods\CS2 Metro` to:

```text
E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods\CS2 Metro
```

It then reminds testers to restart Cities: Skylines II.

## Phase 4D.0 Notes

- Real export now keeps the existing latest files:

```text
metro-export.json
metro-export-diagnostics.txt
```

- Each real export also writes timestamped snapshot copies under:

```text
exports\
```

- Snapshot file naming uses local time in this format:

```text
yyyyMMdd-HHmmss
```

- Snapshot city slugs are sanitized for Windows file names:
  - invalid filename characters are removed/replaced,
  - whitespace collapses to `-`,
  - empty/unavailable names fall back to `UnnamedCity`.
- The current real rendering baseline is still tuned mainly against one primary city, but no line/station/city-specific logic should be hard-coded.
- Viewer compatibility is intentionally unchanged:
  - `Open Default Export` still opens the latest `metro-export.json`,
  - snapshot exports are opened manually through `Open JSON`.

## 2026-05-30 Documentation Cleanup

- Added `docs/README.md` as the main documentation index.
- Consolidated route/path geometry docs into `docs/ROUTE_GEOMETRY_NOTES.md`.
- Moved phase-specific historical docs into `docs/archive/`:
  - `PHASE_3C_MEMO.md`,
  - `VIEWER_MANUAL_TEST.md`,
  - `PATH_GEOMETRY_VALIDATION.md`,
  - `PATH_GEOMETRY_VALIDATION_RESULTS.md`,
  - `METRO_TRACK_GEOMETRY_DISCOVERY.md`.
- Updated `scripts/package-alpha-release.ps1` to copy `docs` recursively so archived docs remain available in release packages.

## Phase 4D.0 Manual Validation

- Phase 4D.0 Export Snapshot Naming passed in-game manual validation.
- Passed checks:
  - latest export exists,
  - timestamped snapshot exists,
  - repeated export does not overwrite old snapshots,
  - latest diagnostics exists,
  - snapshot diagnostics exists,
  - Viewer opens latest through `Open Default Export`,
  - Viewer opens snapshot through `Open JSON`.

## Phase 4D.1 Primary City Baseline

- Added `scripts\generate-primary-city-baseline.ps1`.
- The script defaults to:

```text
D:\CS2MetroDiagram\metro-export.json
```

- Baseline output:

```text
artifacts\primary-city-baseline\latest
artifacts\primary-city-baseline\history
```

- Current generated baseline history run:

```text
artifacts\primary-city-baseline\history\20260530-091836
```

- Current baseline settings:
  - layout: `geographic`,
  - `UsePathPoints=true`,
  - service family merge enabled,
  - shared corridor disabled,
  - express stripe disabled,
  - poster size,
  - path simplification enabled.
- Current visual continuity summary:

## Phase 5B.4 Follow-up - Schematic-v2 2/10 Corridor Rendering

- The first service-family simplification pass hid `10号线` express variants correctly, but the selected canonical `10号线（机场快线-站站停）` route still contained an out-and-back style repeated chain.
- Schematic-v2 now feeds geometry/topology corridor route guides back into the final render route chain instead of using only raw canonical stops.
- A conservative render-chain normalization removes obvious backtracking suffixes such as `A-B-C-D-C-B` from schematic-v2 visible geometry only.
- Raw `line.stops`, `line.pathPoints`, exporter output, and JSON schema remain unchanged.
- `schematic-v2` now emits continuous parallel corridor overlays for shared segments found in the final render route chain. The real `2号线` / `10号线` section renders as a 4-point shared corridor run in `artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.svg`.
- Fixed an SVG style bug where `express-decoration` polylines could use the browser default black fill. The stylesheet now sets `.express-decoration { fill: none; ... }`.
- Follow-up: schematic-v2 parallel corridor overlays now also draw the white express center stripe for express service families on the shared corridor itself. The SVG marks these with `data-schematic-v2-parallel-corridor-express-marker="true"`.
- Regenerate diagnostics with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate-schematic-v2-diagnostics.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -OutputDir artifacts\schematic-v2-diagnostics
```
  - route base stroke count: 9,
  - shared corridor stroke count: 0,
  - express stripe stroke count: 0,
  - normal stroke width: 14,
  - no visual continuity risks detected by current thresholds.
- Use this city as the primary validation benchmark, but do not special-case it in code.

## Phase 4D.2 Alpha Candidate Baseline Polish

- Phase 4D.1 primary city baseline was established and initially accepted for alpha review.
- Phase 4D.2 keeps the scope narrow:
  - no new renderer style,
  - no shared corridor redesign,
  - no express stripe tuning,
  - no Phase 5A.9 Route Run Stitcher,
  - no exporter or JSON schema change.
- Renderer title output now uses:
  - `{CityName} Metro Diagram` for normal city names,
  - `CS2 Metro Diagram` for the current `CS2 Metro Export` placeholder,
  - `Unnamed City Metro Diagram` when the city name is blank.
- Baseline default output remains:
  - layout: `geographic`,
  - `UsePathPoints=true`,
  - service family merge enabled,
  - shared corridor disabled,
  - express stripe disabled,
  - poster size,
  - path simplification enabled.
- Legend defaults were lightly polished:
  - wider right-side legend lane,
  - larger legend label font,
  - larger legend variant font,
  - increased row spacing.
- Default padding was reduced slightly so the primary city network has more useful map area while still reserving a stable legend lane.
- `scripts\capture-svg-screenshot.ps1` now removes an existing output PNG before invoking Edge. This prevents stale screenshots from being mistaken for freshly generated baseline images.
- In this Codex sandbox, Microsoft Edge headless needs to run outside the sandbox to write PNG screenshots reliably. Use the existing baseline script normally on the user's machine; inside Codex, allow escalation when regenerating PNG artifacts.
- Latest regenerated primary city baseline run:

```text
artifacts\primary-city-baseline\history\20260530-093512
```

- Latest regenerated baseline artifacts:

```text
artifacts\primary-city-baseline\latest\baseline-geographic.svg
artifacts\primary-city-baseline\latest\baseline-geographic.full.png
artifacts\primary-city-baseline\latest\visual-continuity-summary.txt
artifacts\primary-city-baseline\latest\notes.md
```

## Phase 4D.3 Station Marker And Label Readability Polish

- Phase 4D.3 keeps the scope narrow:
  - no route algorithm change,
  - no exporter change,
  - no JSON schema change,
  - no Viewer UI change,
  - no shared corridor or express stripe work,
  - no Phase 5A.9 Route Run Stitcher.
- Real baseline review showed the Phase 5A.8 transparent station rings were too subtle for normal alpha users.
- Station markers now use a more common metro-map style:
  - white fill,
  - dark outline,
  - ordinary station radius `6.2`,
  - interchange radius `9.8`,
  - outline width derived from the centralized style token.
- Label readability defaults were increased:
  - station label font size `14`,
  - label gap `12`,
  - label halo width derived from the new label size.
- Legend readability defaults were increased:
  - legend width `380`,
  - legend label font size `17`,
  - variant text size and row spacing scale from that value.
- The final Phase 4D.3 baseline run is:

```text
artifacts\primary-city-baseline\history\20260530-095317
```

- Latest visual continuity summary:
  - route base stroke count: 9,
  - shared corridor stroke count: 0,
  - express stripe stroke count: 0,
  - normal stroke width: 14,
  - station marker sizes: ordinary `6.2`, interchange `9.8`,
  - no visual continuity risks detected by current thresholds.

## Phase 4D.4 Station Route Anchor Alignment

- Phase 4D.4 is renderer-only:
  - no exporter change,
  - no JSON schema change,
  - no raw `station.position` mutation,
  - no `line.pathPoints` mutation,
  - no route algorithm change,
  - no Viewer UI change,
  - no Phase 5A.9 Route Run Stitcher.
- Root cause: real CS2 `station.position` and rendered `line.pathPoints` geometry are separate data sources and can be slightly misaligned after projection/simplification.
- Added render-time station route anchoring for `geographic + UsePathPoints`:
  - ordinary stations project to the nearest point on the related display family primary route path,
  - segment projection is preferred over nearest path point snapping,
  - anchoring applies only within `StationRouteAnchorMaxDistance`,
  - interchange stations compute nearest anchors for all related display families,
  - close interchange anchors use an average render point,
  - spread-out interchange anchors fall back to raw position with `multi-family-anchor-spread-too-large`.
- New renderer options:
  - `EnableStationRouteAnchoring = true`,
  - `StationRouteAnchorMaxDistance = 36`,
  - `StationRouteAnchorMultiFamilyMaxSpread = 40`.
- Station circles and station labels both use the same anchored render point.
- SVG debug attributes include:
  - `data-station-anchor`,
  - `data-station-anchor-applied`,
  - `data-station-anchor-distance`,
  - `data-station-anchor-family`,
  - `data-station-anchor-fallback`,
  - `data-station-raw-x`,
  - `data-station-raw-y`.
- Schematic-lite keeps its existing station positions and records `data-station-anchor="raw"` with `data-station-anchor-fallback="schematic-lite"`.
- Latest regenerated primary city baseline run:

```text
artifacts\primary-city-baseline\history\20260530-100650
```

- Latest visual continuity summary remains clean:
  - route base stroke count: 9,
  - shared corridor stroke count: 0,
  - express stripe stroke count: 0,
  - normal stroke width: 14,
  - no visual continuity risks detected by current thresholds.

## Phase 5A.2 Notes

- Phase 5A.2 adds optional `line.pathPoints` while preserving existing `line.stops`.
- Old JSON files without `pathPoints` still load and render.
- Core DTO additions:
  - `MetroLine.PathPoints`,
  - `MetroPathPoint`.
- The loader normalizes missing `pathPoints` to an empty list and removes consecutive near-duplicate path points.
- The real exporter now uses `RoutePathPointExtractor` to read RouteSegment buffer entries, find segment entities, read `Game.Routes.PathTargets`, and export `m_ReadyStartPosition` / `m_ReadyEndPosition` as x/z path points.
- Path point extraction is best-effort:
  - skipped segments are counted,
  - first skip reasons are written to diagnostics,
  - stops export continues even when path geometry fails.
- Geographic rendering can use path points when `SvgRenderOptions.UsePathPoints` is true.
- Schematic-lite still uses station stops by default.
- CLI flag:

```text
--use-path-points
```

- Viewer checkbox:

```text
Use exported path geometry
```

- Added `samples\sample-metro-pathpoints.json`.

## Phase 5A.2 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- samples\sample-metro-pathpoints.json samples\generated-svg\sample-metro-pathpoints.pathpoints.svg --layout geographic --use-path-points
```

## Phase 5A.2b Notes

- Phase 5A.2b does not change `RealMetroJsonExporter` ECS logic or the JSON schema.
- Geographic rendering cleans `line.pathPoints` only when `UsePathPoints` is true.
- Cleanup works on temporary render data and does not mutate the loaded `MetroExportDocument`.
- Cleanup steps:
  - remove consecutive duplicate or near-duplicate path points,
  - remove very short intermediate segments,
  - simplify nearly-collinear points with a conservative RDP-style tolerance,
  - keep first and last points.
- If cleaned path points are unusable, rendering falls back to original path points and then to stops.
- Schematic-lite still uses station stops by default.
- Route polylines rendered from path points include:

```text
data-route-source="pathPoints"
data-path-point-count="..."
data-cleaned-path-point-count="..."
```

- Size presets:
  - Compact: `1600 x 1000`,
  - Standard: `2200 x 1400`,
  - Poster: `3200 x 2000`,
  - Ultra: `4200 x 2600`.
- Viewer settings now persist the selected size preset and path simplification controls.
- CLI flags added:

```text
--size compact|standard|poster|ultra
--simplify-path-points
--no-simplify-path-points
--path-simplification-tolerance <number>
--min-path-segment-length <number>
```

## Phase 5A.2b Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- samples\sample-metro-pathpoints.json samples\generated-svg\sample-metro-pathpoints.pathpoints.svg --layout geographic --use-path-points
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- samples\sample-metro-pathpoints.json samples\generated-svg\sample-metro-pathpoints.poster.svg --layout geographic --size poster --use-path-points
```

## Phase 5A.2c Notes

- Phase 5A.2c keeps the existing `line.pathPoints` schema and does not change `line.stops`, subway filtering, Viewer behavior, CLI behavior, or schematic-lite defaults.
- `RoutePathPointExtractor` now tries path sources in this order:
  - `RouteSegment.CurveElement`,
  - `RouteSegment.PathElement`,
  - `RouteSegment.PathTargets`.
- CurveElement extraction is reflective and best-effort:
  - reads a `CurveElement` buffer or component on the segment entity,
  - looks for Bezier-like structures such as `Bezier4x3` or `float4x3`,
  - samples cubic Bezier curves at `t=0, 0.25, 0.5, 0.75, 1`,
  - records field summaries and fallback reasons when the structure is unknown.
- PathElement extraction uses the same reflective geometry reader for position/path/target-like fields.
- PathTargets remains the final fallback using `m_ReadyStartPosition` and `m_ReadyEndPosition`.
- Added shared math helper `PathGeometrySampler` under Core and linked it into the CS2 mod project as source. This is math-only and does not add game dependencies to Core.
- Diagnostics now include:
  - route segment count,
  - curve element count,
  - path element count,
  - path point counts before/after cleanup,
  - source summary,
  - first 10 path points,
  - CurveElement fallback reasons.

## Phase 5A.2c Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

CS2 mod build/post-process uses the same local toolchain command documented in Phase 5A.1.

## Phase 5A.2d Path Geometry Validation Workflow

- Phase 5A.2d adds scripts and docs only. It does not modify exporter, renderer, Viewer, CLI behavior, or the JSON schema.
- After building the CS2 mod, deploy it before in-game testing:

```text
scripts\deploy-local-mod.ps1
```

- Always restart Cities: Skylines II after deploying the latest mod.
- After exporting `D:\CS2MetroDiagram\metro-export.json` in-game, summarize path geometry with:

```text
scripts\analyze-metro-export-json.ps1
```

- Generate the four SVG comparison outputs with:

```text
scripts\generate-path-geometry-comparison.ps1
```

- Default comparison output:

```text
artifacts\path-geometry-comparison
```

- Full checklist:

```text
docs\ROUTE_GEOMETRY_NOTES.md
```

## Session Handoff Note

- Codex session ended after Phase 4C release generation and a lightweight release polish pass.
- The next session should start from `docs/NEXT_SESSION_HANDOFF.md`.
- Do not start a large new feature until `v0.1.0-alpha.1` external alpha feedback has been reviewed.

## Phase 2 Manual Test

1. Build/deploy the `CS2 Metro` mod from Visual Studio or the CS2 toolchain.
2. Start Cities: Skylines II and enable/load the mod.
3. Open `Options > CS2 Metro Diagram > Main > Export`.
4. Click `Export Test Metro JSON`.
5. Check the mod log for `Export Test Metro JSON succeeded`.
6. Open `Documents\CS2MetroDiagram\test-export.json`.
7. Convert it with `dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- <test-export.json> <output.svg>`.

## Phase 2.5 Notes

- Added a second settings group: `Main > Debug`.
- Added `Export Transport Debug Dump` as a button using `[SettingsUIButton]`.
- `TransportDebugDumpExporter` stores all ECS/game-specific scanning logic inside the CS2 mod project.
- Dump output paths:
  - `Documents\CS2MetroDiagram\debug-dump.json`
  - `Documents\CS2MetroDiagram\debug-dump.txt`
- The dump scans all entities through `UpdateSystem.EntityManager.GetAllEntities(Allocator.Temp)`.
- Candidate entities are selected when one or more component type names contain keywords such as `transport`, `line`, `route`, `stop`, `station`, `metro`, or `subway`.
- For each candidate component type, the dump keeps the total entity count but caps detailed samples at 20.
- Component value reads are best-effort:
  - zero-sized components are recorded as tags,
  - buffers include length and up to 5 sample elements,
  - fields/properties with names like `name`, `color`, `position`, `route`, `stop`, `station`, `type`, and `mode` are recorded when readable,
  - exceptions are captured into the dump and logged where appropriate.
- The dump also records game/world status through `GameManager.instance` and `UpdateSystem.World`.
- No real metro export or `MetroExportDocument` mapping was added.

## Phase 2.5 Verification

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

CS2 mod build/post-process was also run with the same local toolchain property command documented in Phase 2.

## Phase 3A Notes

- Added `Export Real Metro JSON` under `Options > CS2 Metro Diagram > Main > Export`.
- Output paths:
  - `Documents\CS2MetroDiagram\metro-export.json`
  - `Documents\CS2MetroDiagram\metro-export-diagnostics.txt`
- `RealMetroJsonExporter` keeps all CS2 ECS reads inside the `CS2 Metro` mod project.
- The exporter queries entities with `Game.Routes.TransportLine`.
- Subway line detection uses:
  - `PrefabRef -> Game.Prefabs.TransportLineData.m_TransportType == Subway`,
  - `Game.Routes.VehicleModel[0].m_PrimaryPrefab -> Game.Prefabs.PublicTransportVehicleData.m_TransportType == Subway`,
  - or any route waypoint connected to `Game.Routes.SubwayStop`.
- Line data:
  - `line.id` comes from the transport line entity id,
  - `line.name` uses the CS2 name system when possible and falls back to `Metro Line {RouteNumber}`,
  - `line.color` uses `Game.Routes.Color.m_Color` and otherwise a fixed palette,
  - `line.mode` is always `metro`,
  - `line.stops` follows the `Game.Routes.RouteWaypoint` buffer order.
- Station data:
  - route waypoint entity comes from `RouteWaypoint.m_Waypoint`,
  - connected stop comes from `Game.Routes.Connected.m_Connected`,
  - stop validity checks `SubwayStop` or `Game.Routes.TransportStop`,
  - station id source priority is `TransportStop.m_AccessRestriction`, `Game.Common.Owner`, `Game.Objects.Attached.m_Parent`, connected stop, then waypoint,
  - position source priority is station group transform, stop transform, stop route position, waypoint transform, waypoint route position, then zero fallback,
  - station names are best-effort through the CS2 name system and fall back to `Station {n}`.
- The diagnostics file records transport line count, subway line count, route number, color source/value, waypoint counts, skipped waypoint reasons, station id sources, fallback names, and fallback coordinates.
- No-world and no-metro cases intentionally emit an empty `network.stations` and `network.lines` document.
- Existing `Export Test Metro JSON` and `Export Transport Debug Dump` entries are unchanged.
- The exporter does not launch the CLI or any external executable.

## Phase 3A Verification

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

CS2 mod build/post-process was also run with the same local toolchain property command documented in Phase 2 and succeeded with 0 warnings and 0 errors.

## Phase 3B Notes

- The CS2 real exporter was not modified for Phase 3B.
- `SvgRenderOptions` now exposes:
  - `Width`,
  - `Height`,
  - `Padding`,
  - `LegendWidth`,
  - `LineWidth`,
  - `StationRadius`,
  - `InterchangeStationRadius`,
  - `LabelFontSize`,
  - `EnableCenterExpansion`.
- CLI usage remains compatible with the old two-argument form:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- <input.json> <output.svg>
```

- Optional CLI parameters added:

```text
--width N --height N --legend-width N --padding N --line-width N --station-radius N --label-font-size N --center-expansion
```

- Legend sorting:
  - extracts the first numeric sequence from `line.name`,
  - sorts numbered lines before non-numbered lines,
  - keeps same-number ties stable.
- Label placement v1:
  - places labels in priority order,
  - tries eight candidate positions,
  - estimates width from ASCII/CJK character weights,
  - scores overlap against previously placed labels and station circle boxes,
  - writes `data-label-position` for quick SVG inspection.
- Label priority currently boosts interchange stations and line terminals, then named stations.
- Fallback-style station names like `Station 1` are lower priority, but still rendered.
- Center expansion is implemented as a conservative radial transform around the source coordinate center, but it is disabled by default.
- Real SVG validation in PowerShell needs `Get-Content -Encoding UTF8` because generated SVGs are UTF-8 without BOM.
- XML escaping now filters control characters and unpaired surrogate code units from labels/titles/attributes.

## Phase 3B Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Render every sample:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $out = Join-Path 'samples\generated-svg' ($_.BaseName + '.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $out
}
```

Render the available real export:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.svg
```

## Phase 3C Notes

- The CS2 real exporter was not modified for Phase 3C.
- `MetroExportDocument` schema was not changed; layout coordinates are render-only.
- Current real exported JSON files are expected under `D:\CS2MetroDiagram`; repository-root `metro-export.json` is only a local CLI test copy when present.
- Added `SvgLayoutMode`:
  - `Geographic`,
  - `SchematicLite`.
- `SvgRenderOptions` now includes:
  - `LayoutMode`,
  - `GridSize`, default `32`.
- `geographic` is the default and preserves Phase 3B coordinate normalization.
- `schematic-lite` flow:
  - starts from normalized geographic canvas coordinates,
  - snaps station render points to the configured grid,
  - walks each line's stop order,
  - places newly encountered stations relative to the previous stop using the nearest horizontal, vertical, or 45-degree endpoint candidate,
  - clamps render positions inside the route drawing area while keeping grid-aligned bounds when possible.
- Shared stations keep their first placed schematic position. This is intentionally simple and avoids topology optimization.
- Route `<g id="routes">` now includes `data-layout="geographic"` or `data-layout="schematic-lite"` for inspection and tests.
- CLI options added:

```text
--layout geographic
--layout schematic-lite
--grid-size <number>
```

## Phase 3C Commands

Render samples in both layout modes:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $geo = Join-Path 'samples\generated-svg' ($_.BaseName + '.geographic.svg')
  $schematic = Join-Path 'samples\generated-svg' ($_.BaseName + '.schematic-lite.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $geo --layout geographic
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $schematic --layout schematic-lite --grid-size 32
}
```

Render a real export in both layout modes:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.geographic.svg --layout geographic
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.schematic-lite.svg --layout schematic-lite --grid-size 32
```

## Phase 4A Notes

- Added `src/MetroDiagram.Viewer` as a WPF app targeting `net8.0-windows`.
- The viewer references:
  - `MetroDiagram.Core`,
  - `MetroDiagram.Rendering`.
- The viewer does not reference or modify the CS2 mod project.
- The viewer uses:
  - `MetroJsonLoader.LoadFromFile` for JSON loading,
  - `MetroSvgRenderer.Render` for SVG generation,
  - WPF `OpenFileDialog` and `SaveFileDialog`,
  - WPF built-in `WebBrowser` with `NavigateToString` for embedded preview.
- WebView2 was not added because that would require an extra NuGet package. The built-in browser is enough for Phase 4A preview.
- `Open JSON` defaults to `D:\CS2MetroDiagram` when that directory exists.
- Invalid JSON clears the preview, disables save, and displays errors in the window.
- Render option parsing uses invariant culture and reports positive-number validation errors.
- Save writes the current SVG using UTF-8 without BOM.
- Because the WPF project queries local Windows SDK metadata, sandboxed build can fail with access denied for `C:\Users\17865\AppData\Local\Microsoft SDKs`; running the same build under normal permissions succeeds.

## Phase 4A Commands

```text
dotnet restore CS2MetroDiagram.slnx
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
```

## Phase 4A.1 Notes

- Added manual validation checklist, now archived as `docs/archive/VIEWER_MANUAL_TEST.md` after alpha test coverage moved into `docs/ALPHA_TEST_PLAN.md`.
- Added package quick start: `docs/VIEWER_QUICK_START.md`.
- Added framework-dependent publish script:
  - `scripts/publish-viewer-framework-dependent.ps1`
  - output: `artifacts\viewer-win-x64-framework-dependent`
- Added self-contained publish script:
  - `scripts/publish-viewer-self-contained.ps1`
  - output: `artifacts\viewer-win-x64-self-contained`
- The self-contained script uses:

```text
dotnet publish src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

- The self-contained script runs an explicit win-x64 restore first because local `NuGet.Config` clears package sources and self-contained publish needs runtime packs.
- Runtime pack restore uses `https://api.nuget.org/v3/index.json`.
- During validation, NuGet package download showed transient EOF/SSL retry messages, but restore eventually succeeded and the package was produced.
- Both package scripts copy:
  - `docs\VIEWER_QUICK_START.md` as package `README.md`,
  - `samples\sample-metro-small.json`,
  - generated `build-info.txt`.
- The self-contained package produced `MetroDiagram.Viewer.exe`.

## Phase 4A.1 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-framework-dependent.ps1
```

## Phase 4B Notes

- The CS2 real exporter was not modified for Phase 4B.
- The Viewer now checks these default export files on startup:
  - `D:\CS2MetroDiagram\metro-export.json`,
  - `Documents\CS2MetroDiagram\metro-export.json`.
- The Viewer does not auto-open a default export; it enables `Open Default Export` when one exists.
- `Open Export Folder` opens the best available export folder from the default export, current JSON path, `D:\CS2MetroDiagram`, or `Documents\CS2MetroDiagram`.
- Viewer settings are stored as JSON at:

```text
Documents\CS2MetroDiagram\viewer-settings.json
```

- Saved settings include:
  - last opened JSON path,
  - layout mode,
  - width, height, legend width, padding,
  - line width, station radius, label font size, grid size,
  - label strategy options,
  - language.
- The Viewer has a minimal `English` / `中文` language selector implemented through `ViewerResources.cs`.
- No full i18n framework was added.
- `Reset Defaults` restores render and label defaults while keeping the current language and last JSON path.
- Rendering label strategy options added:
  - `HideGenericStationLabels`,
  - `HideCrowdedLabels`,
  - `AlwaysShowInterchanges`,
  - `AlwaysShowTerminals`.
- Generic station names are detected by `StationLabelClassifier`.
- Current generic/fallback detection includes:
  - `小型地铁广场`,
  - `现代地铁站`,
  - `地下地铁站`,
  - `地铁站`,
  - `Subway Station`,
  - `Metro Station`,
  - `Station 1`, `Station 2`, and other `Station <number>` fallback labels.
- Label hiding only affects text labels; station circles are always rendered.
- `HideCrowdedLabels` hides lower-priority labels when their chosen bounding box seriously overlaps already placed higher-priority labels.
- Interchanges and terminals are protected by default.
- CLI options added:

```text
--hide-generic-labels --hide-crowded-labels --always-show-interchanges --always-show-terminals
```

## Phase 4B Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
```

## Phase 4C Notes

- Release version is `v0.1.0-alpha.1`.
- Added `Directory.Build.props` so SDK project assembly/package metadata is aligned with the alpha version.
- Added `MetroDiagramAppInfo.Version` in Core for shared offline version text.
- Viewer window title now appends `v0.1.0-alpha.1`.
- Core default `GeneratorInfo.Version` now uses `v0.1.0-alpha.1`.
- CS2 mod `VersionInfo.ReleaseVersion` is used for:
  - `RealMetroJsonExporter` `generator.version`,
  - `TestMetroJsonExporter` `generator.version`,
  - `TransportDebugDumpExporter.dumpVersion`.
- The CS2 real exporter's ECS reading logic was not changed.
- Sample JSON `generator.version` values were updated to `v0.1.0-alpha.1`.
- Added release-facing docs:
  - `docs/ALPHA_QUICK_START.md`,
  - `docs/KNOWN_ISSUES.md`,
  - `docs/FEEDBACK_TEMPLATE.md`,
  - `docs/CHANGELOG.md`.
- Publish scripts now include `Version`, `BuiltAtUtc`, and `Commit` in `build-info.txt`.
- Added release package script:

```text
scripts\package-alpha-release.ps1
```

- Release package output:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1-win-x64.zip
```

- Package script workflow:
  - build `CS2MetroDiagram.slnx`,
  - run `MetroDiagram.Tests`,
  - publish Viewer self-contained,
  - copy Viewer artifacts,
  - copy current `artifacts\cs2-local-mods` into `Mod` when available,
  - copy docs and sample JSON files,
  - generate release `build-info.txt`,
  - zip the release folder.
- For the final Phase 4C package, the CS2 mod artifacts were rebuilt with the local CS2 modding toolchain before rerunning the package script, so the copied `Mod` folder contains the synced `v0.1.0-alpha.1` version string.
- Release package smoke checks performed:
  - `Mod\CS2 Metro\CS2 Metro.dll` contains `v0.1.0-alpha.1`,
  - release `Viewer\MetroDiagram.Viewer.exe` starts and can be closed,
  - zip contains root README, quick start, known issues, changelog, build info, Viewer exe, Mod DLL, and sample JSON.

## Phase 4C Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1
```

- Current sandbox note: Viewer publish can require access to `C:\Users\17865\AppData\Local\Microsoft SDKs` for Windows SDK metadata. If Codex sandbox execution reports access denied there, run the publish/package scripts from a normal local PowerShell session or allow an elevated Codex command.
- Phase 4E verification completed after allowing elevated local commands:
  - `dotnet build CS2MetroDiagram.slnx --no-restore` passed,
  - `dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore` passed,
  - `dotnet build "CS2 Metro\CS2 Metro.csproj" --no-restore` passed with post-process,
  - `scripts\publish-viewer-self-contained.ps1` passed,
  - `scripts\package-alpha-release.ps1` generated the alpha.2 candidate folder and zip,
  - release Viewer exe started in a short smoke test,
  - `scripts\generate-primary-city-baseline.ps1` passed with Edge headless screenshot after elevation.
- Generated release artifacts:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

## Phase 4E.1 Schematic-lite Overlap Resolver

- Phase 4E.1 is renderer-only and only affects `schematic-lite`.
- Geographic remains the alpha.2 recommended default:

```text
layout = geographic
UsePathPoints = true
service family merge / normalized family rendering = enabled
shared corridor = disabled
express stripe = disabled
```

- Added schematic segment occupancy detection after display-family route point generation:
  - route polylines are split into segments,
  - segment keys are undirected so A->B and B->A match,
  - zero-length segments are ignored,
  - occupancy counts distinct display families, not repeated same-family passes.
- Added schematic-only parallel offsets for occupied segments with 2+ display families:
  - station markers stay on the original schematic station point,
  - offset distance is centralized through render options / style tokens,
  - SVG includes `data-schematic-overlap`, `data-schematic-overlap-family-count`, `data-schematic-overlap-index`, `data-schematic-overlap-offset`, and `data-schematic-segment-key`.
- Generated comparison output:

```text
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.svg
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.full.png
```

- The real schematic output contains 26 offset route segments across 6 distinct overlapping schematic segment keys.
- This does not start Phase 5A.9, shared corridor restyling, express stripe restyling, Viewer UI work, exporter changes, or JSON schema changes.

## Phase 4E.1a Schematic-lite Overlap Junction Cleanup

- Phase 4E.1a keeps the schematic-lite overlap resolver but trims offset overlap segments near station/junction endpoints.
- The issue was renderer-only: each overlap segment was offset independently, and segment endpoints still visually crowded the station center.
- Added `SvgRenderOptions.SchematicOverlapEndpointTrim` and a style-token fallback:

```text
trimDistance = station marker radius + route width * 0.5
```

- The trim applies only to `schematic-lite` overlap segments with `data-schematic-overlap="true"`.
- Geographic rendering, non-overlap schematic segments, exporter output, JSON schema, `line.stops`, and `line.pathPoints` are unchanged.
- Debug attributes:
  - `data-schematic-overlap-trim="true"`
  - `data-schematic-overlap-trim-distance="..."`
  - `data-schematic-overlap-trim-fallback="segment-too-short"` when trim must be clamped.
- Regenerated comparison output:

```text
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.svg
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.full.png
```

- Current real schematic summary:
  - route count: 136,
  - schematic overlap segment count: 26,
  - distinct overlapping schematic segment keys: 6,
  - trimmed overlap count: 26,
  - short fallback count: 0.

CS2 mod artifact rebuild command used before final packaging:

```powershell
$tool = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain'
$managed = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$userData = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II'
$unityProject = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\.cache\Modding\UnityModsProject'
$post = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe'
$mscorlib = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\mscorlib.dll'
$localMods = 'E:\CS2\CS2 Metro\artifacts\cs2-local-mods'
dotnet build "CS2 Metro.slnx" --no-restore /p:CsiiToolPath="$tool" /p:ManagedPath="$managed" /p:UserDataPath="$userData" /p:UnityModProjectPath="$unityProject" /p:ModPostProcessorPath="$post" /p:EntitiesVersion="1.3.10" /p:MSCORLIBPath="$mscorlib" /p:LocalModsPath="$localMods"
```

## Phase 5A.1 Notes

- Phase 5A.1 is diagnostic-only. It does not change `metro-export.json`, Renderer, CLI, or Viewer behavior.
- Added `CS2 Metro\RouteGeometryDiagnostics.cs` inside the CS2 mod project.
- `RealMetroJsonExporter` now appends `Route Geometry Diagnostics` sections to `metro-export-diagnostics.txt` for each recognized subway `TransportLine`.
- `TransportDebugDumpExporter` also includes the route geometry report in `debug-dump.txt` and the debug dump JSON sidecar field.
- Route geometry diagnostics search for a `RouteSegment` buffer by component type name, read it reflectively, and sample at most 10 segments per line.
- Each sampled segment records readable entity references, component type names, Game.Net track/lane/edge/curve hints, geometry-like fields, and per-segment exceptions.
- Summary counts include total subway lines, total route segments found, sampled segments with geometry-like fields, sampled segments referencing Game.Net entities, and a conservative recoverability hint.

## Phase 5A.1 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

CS2 mod build/post-process command:

```powershell
$tool = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain'
$managed = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$userData = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II'
$unityProject = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\.cache\Modding\UnityModsProject'
$post = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe'
$mscorlib = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\mscorlib.dll'
$localMods = 'E:\CS2\CS2 Metro\artifacts\cs2-local-mods'
dotnet build "CS2 Metro.slnx" --no-restore /p:CsiiToolPath="$tool" /p:ManagedPath="$managed" /p:UserDataPath="$userData" /p:UnityModProjectPath="$unityProject" /p:ModPostProcessorPath="$post" /p:EntitiesVersion="1.3.10" /p:MSCORLIBPath="$mscorlib" /p:LocalModsPath="$localMods"
```

## Phase 5A.3 Notes

- Phase 5A.3 is diagnostic-only and does not change `metro-export.json`, Viewer, CLI, renderer behavior, or the existing stop/pathPoint export shape.
- Added `CS2 Metro\MetroTrackGeometryDebugExporter.cs`.
- The existing `Export Transport Debug Dump` button now also writes:

```text
D:\CS2MetroDiagram\metro-track-geometry-debug.json
D:\CS2MetroDiagram\metro-track-geometry-debug.txt
```

- The new output focuses on subway `TransportLine` entities and samples at most 10 `RouteSegment` items per line.
- Each sampled segment records:
  - segment entity id,
  - component type names,
  - entity reference fields,
  - referenced `Game.Net` entities and their component type names,
  - geometry-like fields whose names look like Bezier, curve, position, start/end, node, edge, lane, track, or path,
  - likely curve source candidates,
  - per-segment warnings and exceptions.
- Use `docs\ROUTE_GEOMETRY_NOTES.md` for route geometry manual validation.
- Before game-side testing, build the CS2 mod, run `scripts\deploy-local-mod.ps1`, and restart Cities: Skylines II.

## Phase 5A.3 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" --no-restore
```

## Phase 5A.3b Notes

- Phase 5A.3b changes only CS2 exporter path point extraction. It does not change the JSON schema, `line.stops`, Viewer UI, CLI flags, or renderer behavior.
- `RoutePathPointExtractor` now treats `Game.Routes.CurveElement` as the primary real path geometry source.
- For each route segment, extraction order is:
  1. `RouteSegment.CurveElement` buffer,
  2. `RouteSegment.PathElement`,
  3. `RouteSegment.PathTargets`.
- CurveElement handling now explicitly looks for `m_Curve` / Bezier-like members, including public and private instance fields/properties.
- `Colossal.Mathematics.Bezier4x3` control points are read from names such as `a/b/c/d`, `m_A/m_B/m_C/m_D`, or `p0/p1/p2/p3` when present.
- Each readable Bezier is sampled with 7 intervals, producing 8 points per curve before adjacent duplicate cleanup.
- `metro-export-diagnostics.txt` now includes:
  - curve sample point count,
  - PathTargets fallback count,
  - first CurveElement read failures,
  - first 10 sampled pathPoints,
  - bounded deep dumps of `CurveElement.m_Curve` fields/properties and control point values.
- Manual validation should focus on:
  - `10号线（机场快线-特快）`,
  - `10号线（机场快线-大站快车）`.
- Expected validation signs:
  - source summary mostly shows `RouteSegment.CurveElement`,
  - `curve sample point count` is much larger than route segment count,
  - `PathTargets fallback count` is low or zero for readable segments,
  - geographic SVG with `--use-path-points` shows fewer express-line fly-lines.

## Phase 5A.3b Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" --no-restore
```

## Phase 5A.3c Notes

- Phase 5A.3c analyzed the latest real export:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

- Export timestamp: `2026-05-28 10:03:36`.
- Added validation result document:

```text
docs\ROUTE_GEOMETRY_NOTES.md
```

- Summary from the latest export:
  - subway lines: 11,
  - stations: 48,
  - stops: 157,
  - route segments: 157,
  - CurveElements: 2432,
  - pathPoints before cleanup: 12160,
  - cleaned/exported pathPoints: 9739,
  - final CurveElement source count: 9739,
  - final PathTargets source count: 0,
  - CurveElement fallback diagnostics: 0,
  - skipped path segments: 0.
- Airport express checks:
  - `10号线（机场快线-大站快车）`: 8 stops, 1065 CurveElement pathPoints,
  - `10号线（机场快线-特快）`: 4 stops, 1025 CurveElement pathPoints.
- Generated comparison SVGs:

```text
artifacts\path-geometry-comparison\01-geographic-stops.svg
artifacts\path-geometry-comparison\02-geographic-pathpoints.svg
artifacts\path-geometry-comparison\03-geographic-pathpoints-simplified.svg
artifacts\path-geometry-comparison\04-schematic-lite.svg
```

- Validation conclusion: CurveElement is the primary source, PathTargets fallback is not present in final pathPoints, and the airport express fly-line issue is considered resolved for this tested export when geographic rendering uses `--use-path-points`.
- Note: the analyzed diagnostics file did not yet include the newest explicit Phase 5A.3b labels `curve sample point count` and `path targets fallback count`; those counts were inferred from `metro-export.json` source metadata and existing diagnostics.

## Phase 5A.3d Notes

- Phase 5A.3d is renderer-only. It does not change the JSON schema, CS2 exporter ECS logic, Viewer UI, or CLI flags.
- Geographic rendering with `UsePathPoints=true` now treats simplified pathPoints as the recommended default.
- Path cleanup now runs after projection to SVG coordinates, so tolerance and short-segment handling are based on visual pixel scale rather than raw CS2 coordinates.
- Adaptive simplification uses canvas size, line length, and median projected segment length to choose conservative tolerances:
  - longer lines can simplify more aggressively,
  - first/last points are preserved,
  - the nearest path point to each station is treated as an anchor.
- Suspicious long path jumps are counted and split into separate route polylines. This avoids drawing one continuous line across a likely geometry discontinuity.
- Route SVG diagnostics for pathPoint routes include:
  - `data-path-point-count`,
  - `data-cleaned-path-point-count`,
  - `data-path-reduction-ratio`,
  - `data-max-path-segment-length`,
  - `data-suspicious-jump-count`,
  - `data-path-simplification-tolerance`.
- Added tests for suspicious jump splitting and verified existing duplicate cleanup, collinear simplification, first/last preservation, and valid SVG coverage.

## Phase 5A.4a Notes

- Phase 5A.4a is renderer-only. It does not change the CS2 exporter, JSON schema, `line.stops`, or `line.pathPoints` semantics.
- `SvgRenderOptions.EnableParallelCorridorOffset` defaults to `false`; default SVG output should remain unchanged.
- The feature only runs when all are true:
  - `LayoutMode == Geographic`,
  - `UsePathPoints == true`,
  - `EnableParallelCorridorOffset == true`.
- CLI flag:

```text
--enable-parallel-corridor-offset
```

- MVP corridor detection works on cleaned/projected SVG path geometry:
  - split route polylines into segment fragments,
  - use a spatial grid to find nearby candidates,
  - require close distance, direction within 15 degrees including reverse direction, and projected overlap,
  - group matching fragments into corridor groups,
  - assign stable perpendicular offsets using line order,
  - taper offsets near the line's own stop positions.
- Offset route fragments carry debug attributes:
  - `data-parallel-corridor-offset="true"`,
  - `data-corridor-id`,
  - `data-corridor-member-count`,
  - `data-corridor-offset-index`,
  - `data-corridor-offset-px`.
- Viewer UI for this option is deferred to Phase 5A.4b unless manual validation shows the MVP is useful enough to expose.
- Real export smoke output, when `D:\CS2MetroDiagram\metro-export.json` exists:

```text
artifacts\path-geometry-comparison\06-geographic-pathpoints-corridor-offset.svg
```

## Phase 5A.5 Notes

- Phase 5A.5 is renderer-only. It does not change the CS2 exporter, JSON schema, `line.stops`, or `line.pathPoints`.
- `SvgRenderOptions.EnableServiceFamilyMerge` defaults to `true`.
- CLI flag:

```text
--disable-service-family-merge
```

- `DisplayLineFamilyResolver` exists only in `MetroDiagram.Rendering`.
- Family key extraction is intentionally simple:
  - `10号线（机场快线-特快）` -> `10号线`,
  - `10号线（机场快线-大站快车）` -> `10号线`,
  - `Line 10 (Express)` -> `Line 10`,
  - no recognized bracket suffix falls back to the full line name,
  - missing names fall back to line id.
- The main map renders one primary line per family. Primary selection prefers:
  1. highest pathPoints count,
  2. highest stops count,
  3. stable name/index ordering.
- Legend rows show service variants when a family has multiple members, including variant name, stop count, and endpoint names when station ids resolve.
- Route SVG debug attributes include:
  - `data-display-family-key`,
  - `data-display-family-member-count`,
  - `data-display-family-primary-line-id`,
  - `data-display-family-merged`,
  - optional `data-display-family-color-mismatch`.
- The Phase 5A.4a parallel corridor option remains present but experimental/default-off. The new preferred direction for same-line variants is service family merge, not lateral offset tuning.
- Real export smoke output, when `D:\CS2MetroDiagram\metro-export.json` exists:

```text
artifacts\path-geometry-comparison\07-geographic-service-family-merge.svg
```

- Latest smoke check found `10号线` as a merged display family with member count 3 in the available real export, and the legend text contained both `机场快线-特快` and `机场快线-大站快车`.

## Phase 5A.6 Notes

- Phase 5A.6 is renderer-only. It does not change the CS2 exporter, JSON schema, `line.stops`, or `line.pathPoints`.
- `SvgRenderOptions.EnableSharedCorridorCompositeStroke` defaults to `false`.
- CLI flag:

```text
--enable-shared-corridor-composite-stroke
```

- The feature only runs when all are true:
  - `LayoutMode == Geographic`,
  - `UsePathPoints == true`,
  - `EnableSharedCorridorCompositeStroke == true`.
- Detection is based on display family primary routes after Phase 5A.5 service-family merge, not raw exported lines.
- v1 only composites exactly two display families:
  - outer layer uses the naturally sorted earlier family color,
  - separator layer uses white/background,
  - inner layer uses the later family color,
  - all layers use the same centerline geometry.
- Three or more shared families are not nested in v1; those fragments fall back to normal route rendering with `data-shared-corridor-skipped="too-many-families"`.
- Composite SVG debug attributes include:
  - `data-shared-corridor="true"`,
  - `data-shared-corridor-id`,
  - `data-shared-family-count`,
  - `data-shared-family-outer`,
  - `data-shared-family-inner`,
  - `data-composite-stroke="true"`,
  - `data-composite-layer`.
- Real export smoke output, when `D:\CS2MetroDiagram\metro-export.json` exists:

```text
artifacts\path-geometry-comparison\08-geographic-shared-corridor-composite.svg
```

- Latest smoke check generated 246 composite layer fragments and 26 `too-many-families` skip fragments on the available real export.

## Phase 5A.6b Notes

- The first Phase 5A.6 nested/composite stroke passed build and tests, but real visual review showed the semantics were wrong:
  - shared corridors were split into many short elements,
  - continuous shared runs looked visually interrupted,
  - the concentric nested stroke did not feel like a transit-map shared corridor.
- The new renderer-only direction keeps the same option name for compatibility but changes the target style:
  - `EnableSharedCorridorCompositeStroke` now builds longer continuous shared corridor runs,
  - the style is marked as `data-shared-corridor-style="shanghai-like"`,
  - the visual uses one continuous corridor base plus an inner color band, inspired by Shanghai Metro 3/4 line shared-corridor notation.
- The shared corridor run builder:
  - still uses display family primary routes after Phase 5A.5 service-family merge,
  - still only runs for `geographic + UsePathPoints`,
  - still does not touch exporter data, JSON schema, station positions, `line.stops`, or `line.pathPoints`,
  - merges adjacent matched segment fragments when the family set remains the same,
  - splits only when the family set changes, geometry continuity breaks, or the shared corridor no longer matches tolerance.
- SVG debug attributes for the new shared runs include:
  - `data-shared-corridor="true"`,
  - `data-shared-corridor-run-id`,
  - `data-shared-corridor-family-a`,
  - `data-shared-corridor-family-b`,
  - `data-shared-corridor-style="shanghai-like"`,
  - `data-shared-corridor-point-count`,
  - `data-shared-corridor-layer`.
- Three or more shared families still fall back with `data-shared-corridor-skipped="too-many-families"`.
- `SvgRenderOptions.EnableExpressCenterStripe` defaults to `false`.
- CLI flag:

```text
--enable-express-center-stripe
```

- Express marker detection is intentionally simple and renderer-only. A display family is considered express-like if variant/original names contain `快`, `特快`, `大站快车`, `机场快线`, `express`, or `rapid`.
- Express marker SVG output uses:
  - `data-express-marker="white-center-stripe"`,
  - `data-express-family`.
- Real export smoke outputs, when `D:\CS2MetroDiagram\metro-export.json` exists:

```text
artifacts\path-geometry-comparison\09-geographic-shared-corridor-shanghai-like.svg
artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.svg
```

- Latest smoke XML summary:
  - old `08` nested composite: 246 shared/composite elements,
  - new `09` shared corridor style: 52 shared elements across 26 shared runs, plus 25 too-many-family skip fragments,
  - new `10` express stripe output: 52 shared elements across 26 shared runs and 14 express stripe elements.
- Initial browser screenshot validation was blocked because `npx.ps1` hit PowerShell execution policy and `npx.cmd playwright` timed out. This was replaced by the Edge headless helper below, which successfully generated PNG screenshots from the real smoke SVGs.

### SVG Screenshot Validation

- The local `npx.ps1` path is blocked by PowerShell execution policy.
- `npx.cmd playwright` also timed out because Playwright is not cached locally and npm would need registry access.
- Added a lightweight Edge-based screenshot helper that avoids npm/npx entirely:

```text
scripts\capture-svg-screenshot.ps1
```

- Example:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\capture-svg-screenshot.ps1 -InputSvg artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.svg -OutputPng artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.full.png -Width 3200 -Height 2000
```

- The helper uses installed Microsoft Edge headless from `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`.
- Latest generated screenshots:

```text
artifacts\path-geometry-comparison\09-geographic-shared-corridor-shanghai-like.full.png
artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.full.png
```

## Phase 5A.7 Notes

- Phase 5A.7 is renderer-only. It does not change the CS2 exporter, JSON schema, `line.stops`, `line.pathPoints`, Viewer UI, CLI flags, or schematic-lite behavior.
- The real visual issue after Phase 5A.6b is a geographic route rendering pipeline issue, not just a stroke width or color tuning problem:
  - shared corridor elements were still too fragmented,
  - express center stripes amplified existing breaks,
  - stroke widths were selected in multiple rendering branches.
- Added SVG diagnostic script:

```text
scripts\analyze-svg-render-debug.ps1
```

- Diagnostic reports:

```text
artifacts\path-geometry-comparison\svg-render-debug-summary-before-5A7.txt
artifacts\path-geometry-comparison\svg-render-debug-summary-after-5A7.txt
```

- Before Phase 5A.7, the real express-on SVG contained:
  - 143 route path elements,
  - 52 shared corridor path elements,
  - 14 express stripe path elements,
  - 26 shared corridor runs,
  - 38 route fragments for `10号线`.
- After Phase 5A.7, the real express-on SVG contains:
  - 121 route path elements,
  - 34 shared corridor path elements,
  - 10 express stripe path elements,
  - 17 shared corridor runs,
  - 30 route fragments for `10号线`.
- Geographic `UsePathPoints=true` now builds a corridor render plan before writing route SVG elements:
  1. normal route base strokes,
  2. shared corridor base strokes,
  3. shared corridor inner color bands,
  4. express center stripe decorations.
- Station circles, interchange markers, labels, and legend are still drawn above route layers by the existing renderer order.
- Stroke widths are centralized through helper methods for:
  - normal routes,
  - shared corridor total width,
  - shared corridor inner band width,
  - express center stripe width.
- Express stripes are now decoration commands over continuous normal runs. They are skipped on shared-corridor or too-many-family fallback conflicts and marked with:

```text
data-express-marker-skipped="shared-corridor-style-conflict"
```

- The shared corridor style marker is now:

```text
data-shared-corridor-style="shanghai-like-continuous"
```

- Real smoke SVGs generated for Phase 5A.7:

```text
artifacts\path-geometry-comparison\11-geographic-pathpoints-baseline.svg
artifacts\path-geometry-comparison\12-geographic-service-family-merge.svg
artifacts\path-geometry-comparison\13-geographic-shared-corridor-continuous.svg
artifacts\path-geometry-comparison\14-geographic-corridor-express-on.svg
```

- Full-size PNG screenshots for `13` and `14` were generated with the Edge headless helper after screenshot permissions were granted:

```text
artifacts\path-geometry-comparison\13-geographic-shared-corridor-continuous.full.png
artifacts\path-geometry-comparison\14-geographic-corridor-express-on.full.png
```

- Quick visual check: the updated outputs use the stabilized route layer order and avoid the Phase 5A.4a lateral-offset jitter. Shared corridor and express marker styling still need human taste review before treating them as final release styles.

## Phase 5A.8 Notes

- Phase 5A.8 is renderer-only. It does not change the CS2 exporter, JSON schema, `line.stops`, `line.pathPoints`, Viewer UI, CLI flags, or schematic-lite behavior.
- The remaining problem after Phase 5A.7 is visual continuity and style normalization:
  - some runs are still split into short fragments,
  - some fragments nearly touch but remain separate SVG route elements,
  - station markers and layered styles can make a connected route look visually broken,
  - shared corridors and express markers must read as one map language rather than separate special-case styles.
- Added visual continuity diagnostic script:

```text
scripts\analyze-visual-continuity.ps1
```

- Diagnostic outputs:

```text
artifacts\path-geometry-comparison\visual-continuity-summary-5A8.txt
artifacts\path-geometry-comparison\18-geographic-visual-continuity-debug.svg
artifacts\path-geometry-comparison\18-geographic-visual-continuity-debug.full.png
```

- The diagnostic report records:
  - display family run counts,
  - shared corridor run point counts and lengths,
  - start/end coordinates,
  - near-touching but unmerged endpoints,
  - suspicious short fragments,
  - route/shared/express stroke counts,
  - actual stroke widths,
  - station marker sizes,
  - style-layer overlap risks.
- Added centralized renderer style tokens through `SvgVisualStyle`:
  - `BaseRouteWidth`,
  - `SharedCorridorOuterWidth`,
  - `SharedCorridorInnerWidth`,
  - `ExpressStripeWidth`,
  - `StationMarkerOuterRadius`,
  - `StationMarkerStrokeWidth`,
  - `InterchangeMarkerRadius`,
  - `InterchangeMarkerStrokeWidth`,
  - `LabelHaloWidth`.
- Current normalized widths for the default 14px line setting:
  - normal route: `14`,
  - shared corridor outer/base: `14`,
  - shared corridor inner band: `6.72`,
  - express center stripe: `3.36`.
- Station markers now render as unfilled rings (`fill: none`) so station circles sit above routes without visually cutting the route stroke.
- Shared corridor near-touch merging now checks all endpoint orientations and can merge append, reverse-append, prepend, and reverse-prepend cases within the visual continuity tolerance.
- Real smoke outputs:

```text
artifacts\path-geometry-comparison\15-geographic-family-normalized.svg
artifacts\path-geometry-comparison\15-geographic-family-normalized.full.png
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.svg
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.full.png
artifacts\path-geometry-comparison\17-geographic-express-normalized.svg
artifacts\path-geometry-comparison\17-geographic-express-normalized.full.png
```

- Latest visual continuity summary for `17`:
  - route base stroke count: `52`,
  - shared corridor stroke count: `36`,
  - express stripe stroke count: `10`,
  - shared fallback stroke count: `25`,
  - station marker sizes: `20` ordinary stations at radius `5.5`, `28` interchange stations at radius `9`.
- The report still flags remaining short fragments and near-touching run breaks; those are renderer-organization targets for future refinement rather than exporter/schema issues.

## Phase 5A.8 Closeout Notes

- Phase 5A.8 is closed for alpha purposes.
- Current recommended alpha baseline:

```text
geographic + UsePathPoints + service family merge / normalized family rendering
```

- Recommended manual review artifact:

```text
artifacts\path-geometry-comparison\15-geographic-family-normalized.full.png
```

- Experimental outputs retained for comparison only:

```text
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.full.png
artifacts\path-geometry-comparison\17-geographic-express-normalized.full.png
```

- `EnableSharedCorridorCompositeStroke` and `EnableExpressCenterStripe` remain default-off.
- Do not expose shared corridor or express stripe as the recommended alpha output.
- Shared corridor / express stripe visual issues should not block alpha testing.
- Next priority is external alpha and multi-city validation:
  - simple one-line city,
  - ordinary multi-line city,
  - loop/branch city,
  - airport/express-service city,
  - dense interchange city.
- If a rendering fix is required before alpha, keep it narrowly scoped to Phase 5A.9 Route Run Stitcher. Do not add new visual styles.

## Phase 4E Alpha.2 Candidate Release Prep

- Phase 4D.4 is closed.
- Primary city baseline accepted for alpha.2 candidate:

```text
artifacts\primary-city-baseline\latest\baseline-geographic.full.png
```

- Accepted reasons:
  - route continuity acceptable,
  - stroke width consistency acceptable,
  - white-filled station markers restored,
  - station alignment acceptable for alpha,
  - label readability acceptable but still a known polish area,
  - shared corridor / express stripe remain experimental and off by default.
- Accepted baseline was frozen to:

```text
artifacts\primary-city-baseline\history\20260530-102042
```

- Release version is now `v0.1.0-alpha.2-candidate`.
- Do not start Phase 5A.9, new shared corridor styles, new express stripe styles, Viewer UI work, PNG/PDF export, JSON schema changes, or exporter restructuring before the alpha.2 candidate unless a blocking bug appears.
- Required candidate verification:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1
```

## Phase 4E.1b Schematic-lite Short Overlap Segment Fallback

- Phase 4E.1b technically passed tests but failed visual acceptance. Do not record it as a completed visual fix.
- The problematic schematic-lite segment was:

```text
1760,992|1792,1024
```

- This was not a missing trim issue. The segment is only about `45.255px` long, so endpoint trim left a very short visible fragment; the same family was also rendering the segment in reverse, which made the station/junction area look twisted.
- 4E.1b renderer-only behavior:
  - short overlap segments use `data-schematic-overlap-fallback="short-segment"`,
  - short overlap render mode is `data-schematic-overlap-render-mode="centered"`,
  - short overlap offset is `0`,
  - overlap fragments use `stroke-linecap: butt`,
  - final schematic overlap rendering de-duplicates same-family reverse output with `data-schematic-render-deduped="true"`.
- Visual regression: the trimmed/butt-capped short segment and aggressive final de-duplication made some schematic-lite routes look discontinuous around station junctions.

## Phase 4E.1c Schematic-lite Overlap Regression Fix

- Current strategy: restore route continuity first, overlap color visibility second.
- Conservative offset policy:
  - only long, simple, safe overlap segments are parallel-offset,
  - short segments fall back to centered full connectors,
  - high-degree station/junction endpoints fall back to centered full connectors,
  - if trim would leave too little visible length, the segment falls back to centered full connector.
- New SVG debug attributes:
  - `data-schematic-overlap-safe-offset="true|false"`,
  - `data-schematic-overlap-fallback="unsafe-short-or-junction"`,
  - `data-schematic-overlap-safe-offset-reason="short-segment|trim-too-short|high-degree-junction"`,
  - `data-schematic-render-duplicate-count="..."`,
  - `data-schematic-render-dedupe-skipped="continuity-priority"`.
- The specific `1760,992|1792,1024` segment now remains a full centered connector with duplicate reverse route output preserved for continuity:
  - `data-schematic-overlap-offset="0"`,
  - `data-schematic-overlap-render-mode="centered"`,
  - `data-schematic-overlap-safe-offset-reason="short-segment"`.
- Regenerated artifacts:

```text
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.svg
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.full.png
artifacts\path-geometry-comparison\schematic-lite-overlap-4E1c.svg
artifacts\path-geometry-comparison\schematic-lite-overlap-4E1c.full.png
```

## Phase 4E.2 Schematic-lite Station Spacing Relaxation

- The latest judgment is that the dense schematic-lite issue near `端州火车站` / `小型地铁广场` is primarily a station spacing problem, not another overlap-trim or short-segment fallback problem.
- Priority order for schematic-lite:

```text
station readability > route continuity > topology clarity > overlap color visibility > geographic accuracy
```

- Renderer-only implementation:
  - initial schematic station positions are still grid-snapped,
  - then `RelaxSchematicStationSpacing` separates stations that are closer than `SchematicMinimumStationSpacing`,
  - adjusted station positions feed route polylines, station markers, and labels,
  - overlap resolver runs after station spacing and only offsets long/simple/safe overlaps.
- New CLI option for comparison/debug output:

```text
--schematic-min-station-spacing N
```

- To generate the before/after comparison:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- D:\CS2MetroDiagram\metro-export.json artifacts\path-geometry-comparison\schematic-lite-before-spacing.svg --layout schematic-lite --size poster --hide-generic-labels --hide-crowded-labels --schematic-min-station-spacing 1
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- D:\CS2MetroDiagram\metro-export.json artifacts\path-geometry-comparison\schematic-lite-station-spacing.svg --layout schematic-lite --size poster --hide-generic-labels --hide-crowded-labels
```

- Generated comparison artifacts:

```text
artifacts\path-geometry-comparison\schematic-lite-before-spacing.svg
artifacts\path-geometry-comparison\schematic-lite-before-spacing.full.png
artifacts\path-geometry-comparison\schematic-lite-station-spacing.svg
artifacts\path-geometry-comparison\schematic-lite-station-spacing.full.png
```

- Renderer warnings include a lightweight summary:

```text
Schematic station spacing conflicts: N; adjusted stations: N; max adjustment distance: ...; remaining spacing conflicts: ...
```

- Adjusted station markers and labels carry:
  - `data-schematic-station-adjusted="true"`,
  - `data-schematic-station-adjustment-distance`,
  - `data-schematic-station-adjustment-reason="min-spacing"`,
  - original schematic x/y attributes.

## Phase 5B Schematic Layout v2

- Stop patching `schematic-lite-v1` overlap/trim/fallback as the main direction. The guiding rule is:

```text
A schematic map may distort geography, but it must not distort topology.
```

- Added independent layout mode:

```text
--layout schematic-v2
```

- `schematic-v2` is topology-first and experimental:
  - builds adjacency from display family service variants,
  - preserves stop sequence in route polyline output,
  - preserves shared interchange render nodes,
  - enforces minimum spacing and short-edge constraints before overlap visibility,
  - keeps route, marker, and label coordinates on one final station position set.
- Geographic remains the alpha default. `schematic-lite` remains available and unchanged as a separate mode.
- Diagnostics script:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-schematic-v2-diagnostics.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json
```

- Diagnostics output:

```text
artifacts\schematic-v2-diagnostics\topology-summary.txt
artifacts\schematic-v2-diagnostics\adjacency-edges.csv
artifacts\schematic-v2-diagnostics\station-degree.csv
artifacts\schematic-v2-diagnostics\dense-junctions.csv
artifacts\schematic-v2-diagnostics\schematic-lite-edge-check.txt
artifacts\schematic-v2-diagnostics\schematic-topology-debug.svg
artifacts\schematic-v2-diagnostics\schematic-topology-debug.full.png
```

- Comparison outputs:

```text
artifacts\path-geometry-comparison\schematic-v2-geographic-baseline.svg
artifacts\path-geometry-comparison\schematic-v2-geographic-baseline.full.png
artifacts\path-geometry-comparison\schematic-v2-schematic-lite-v1.svg
artifacts\path-geometry-comparison\schematic-v2-schematic-lite-v1.full.png
artifacts\path-geometry-comparison\schematic-v2.svg
artifacts\path-geometry-comparison\schematic-v2.full.png
```

## Phase 5B.2 Schematic-v2 Shared Corridor Preservation

- Do not return to the 4E.1 / 4E.2 schematic-lite patch pipeline. The accepted direction is topology-first schematic-v2.
- Principle:

```text
A schematic map may distort geography, but it should preserve corridor topology.
```

- Schematic-v2 now chooses a topology-rich service variant per display family. This is schematic-v2-only; geographic still uses the existing display family primary route/path behavior.
- The real primary city case that motivated this phase:
  - `10号线` display family primary service can be a pathPoints-dense express service,
  - `10号线（机场快线-站站停）` carries the topology-rich stop sequence,
  - `2号线` and `10号线` share the exact station edge `station_1068950_1 -> station_1068953_1`,
  - schematic-v2 should keep that shared edge before the two families diverge.
- Diagnostics command:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-schematic-v2-diagnostics.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json
```

- New/updated diagnostics output:

```text
artifacts\schematic-v2-diagnostics\shared-corridors.txt
artifacts\schematic-v2-diagnostics\shared-corridors.csv
artifacts\schematic-v2-diagnostics\schematic-v2-family-topology-lines.csv
artifacts\schematic-v2-diagnostics\schematic-v2-shared-corridor-debug.svg
artifacts\schematic-v2-diagnostics\schematic-v2-shared-corridor-debug.full.png
```

- Comparison outputs:

```text
artifacts\path-geometry-comparison\schematic-v2-before-shared-corridor.svg
artifacts\path-geometry-comparison\schematic-v2-before-shared-corridor.full.png
artifacts\path-geometry-comparison\schematic-v2-shared-corridor.svg
artifacts\path-geometry-comparison\schematic-v2-shared-corridor.full.png
```

- This remains renderer-only. Do not modify exporter logic, JSON schema, `line.stops`, or `line.pathPoints` for this phase.

## Artifact Housekeeping

- Generated files were organized on 2026-05-31.
- Active outputs remain in:
  - `artifacts\primary-city-baseline\latest`,
  - `artifacts\path-geometry-comparison`,
  - `artifacts\schematic-v2-diagnostics`,
  - `artifacts\releases`,
  - `artifacts\viewer-win-x64-self-contained`,
  - `artifacts\cs2-local-mods`.
- Older exploratory outputs were moved to:

```text
artifacts\archive\20260531-cleanup
```

- The repository root scratch files `metro-export.json` and `output.svg` were archived under `root-scratch`.
- Old phase smoke folders `phase2`, `phase3a`, and `phase5a2-real-test` were archived under `legacy-phase-artifacts`.
- Old sample generated SVGs were moved from `samples\generated-svg` to the archive; the empty `samples\generated-svg` folder remains available for README commands that regenerate sample SVGs.

## Phase 4F Alpha Validation Bundle Workflow

- Use this workflow before making more renderer decisions from a real city export:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -CaseName primary-city
```

- Default input:

```text
D:\CS2MetroDiagram\metro-export.json
```

- Default output:

```text
artifacts\alpha-validation\<yyyyMMdd-HHmmss>-<caseName>
artifacts\alpha-validation\alpha-validation-<yyyyMMdd-HHmmss>-<caseName>.zip
```

- Each bundle contains:
  - copied `metro-export.json`,
  - copied `metro-export-diagnostics.txt` when available,
  - optional `viewer-settings.json` from `Documents\CS2MetroDiagram`,
  - `baseline-geographic.svg` / `.full.png`,
  - `schematic-lite.svg` / `.full.png`,
  - `schematic-v2.svg` / `.full.png`,
  - `visual-continuity-summary.txt`,
  - `visual-continuity-debug.svg`,
  - `schematic-v2-diagnostics\shared-corridors.txt`,
  - `schematic-v2-diagnostics\topology-summary.txt`,
  - `notes.md`,
  - `feedback-template-filled.md`.
- Bundle render settings:
  - geographic baseline: `--layout geographic --size poster --use-path-points --hide-generic-labels --hide-crowded-labels`,
  - schematic-lite: `--layout schematic-lite --size poster --hide-generic-labels --hide-crowded-labels`,
  - schematic-v2: `--layout schematic-v2 --size poster --hide-generic-labels --hide-crowded-labels`.
- Keep geographic as the alpha recommended baseline. Schematic-v2 is included for topology comparison only.
- Do not continue shared corridor / express stripe styling work until validation bundles from more cities show a repeated need.

## Phase 5B.3a Geometry Shared Corridor Diagnostics

- `schematic-v2` now has a diagnostics pass for pathPoints-based shared corridors. This is needed because stop-sequence shared corridor detection is not enough for skip-stop / express services.
- Main command:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-schematic-v2-diagnostics.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -OutputDir artifacts\schematic-v2-diagnostics
```

- New/updated outputs:

```text
artifacts\schematic-v2-diagnostics\geometry-shared-corridors.txt
artifacts\schematic-v2-diagnostics\geometry-shared-corridors.csv
artifacts\schematic-v2-diagnostics\geometry-shared-corridor-debug.svg
artifacts\schematic-v2-diagnostics\schematic-v2-route-guides.txt
artifacts\schematic-v2-diagnostics\schematic-v2-route-guides.csv
```

- Current primary validation case:
  - `10号线 + 2号线` is detected as a geometry shared corridor,
  - approximate shared length: `541.455`,
  - average distance: `16.754`,
  - max distance: `65.892`,
  - confidence: `1`,
  - route guide rows exist for both `10号线` and `2号线`.
- Schematic-v2 SVG route elements can now include geometry corridor debug attributes such as `data-schematic-v2-geometry-corridor`, `data-schematic-v2-corridor-source="pathPoints"`, and `data-schematic-v2-route-guide="true"`.
- PNG screenshot capture was not completed in this pass because the Edge screenshot helper needs elevated GUI/browser execution and the escalation request was rejected by the environment quota. The SVG and text diagnostics were generated successfully.

## Phase 5B.3b Corridor Guide Materialization

- Phase 5B.3b closes the loop from detection to rendering:

```text
geometry shared corridor detection
-> schematic-v2 route guide materialization
-> continuous parallel corridor overlay
```

- The new schematic-v2 overlay is based on final materialized shared route runs, not schematic-lite-style arbitrary overlap segments.
- Pass-through guide stations are only render-time route geometry. They do not change `line.stops`, `line.pathPoints`, exporter output, or the JSON schema.
- Diagnostics now include:

```text
artifacts\schematic-v2-diagnostics\schematic-v2-parallel-corridors.txt
artifacts\schematic-v2-diagnostics\schematic-v2-parallel-corridors.csv
```

- Current primary validation row:

```text
10号线 + 2号线: detected=True, materialized=True, parallelRendered=True, host=2号线, follower=10号线
```

- Generated SVGs:

```text
artifacts\schematic-v2-diagnostics\geographic-baseline.svg
artifacts\schematic-v2-diagnostics\schematic-v2-before-guide-materialization.svg
artifacts\schematic-v2-diagnostics\schematic-v2-guide-materialized.svg
artifacts\schematic-v2-diagnostics\schematic-v2-parallel-corridor.svg
artifacts\schematic-v2-diagnostics\schematic-v2-parallel-corridor-debug.svg
```

- `MetroSvgRenderer.cs` now carries even more schematic-v2 logic. A future cleanup should split this into renderer-internal helpers such as `GeometrySharedCorridorDetector`, `SchematicRouteGuideBuilder`, and `SchematicSharedCorridorRenderer`.

## Phase 5B.3c Render Route Chain Reconstruction

- The 5B.3b debug attributes were not enough by themselves: the real `2号线` / `10号线` case could still look like a short overlay instead of a true follower route geometry change.
- Phase 5B.3c defines real materialization as:

```text
raw service stops + corridor guide nodes
-> final schematic-v2 render route chain
-> rendered polyline
```

- Pass-through guide nodes are render-only. They do not change `line.stops`, `line.pathPoints`, exporter output, or JSON schema.
- The synthetic regression case now covers a local corridor with two pass-through stations:

```text
local:   A - B - C - D - E
express: A - D - F
render:  A - B(pass-through) - C(pass-through) - D - F
```

- Current real diagnostics for `10号线 + 2号线`:
  - host family: `2号线`,
  - follower family: `10号线`,
  - `hostIntervalNodeCount=4`,
  - `passThroughNodeCount=2`,
  - `renderRouteChainBeforeCount=14`,
  - `renderRouteChainAfterCount=16`,
  - `materialized=True`,
  - `parallelRendered=True`.
- Current SVG comparison outputs:

```text
artifacts\schematic-v2-diagnostics\schematic-v2-before-route-chain.svg
artifacts\schematic-v2-diagnostics\schematic-v2-route-chain-materialized.svg
artifacts\schematic-v2-diagnostics\schematic-v2-route-chain-materialized-debug.svg
```

## Phase 5B.4 Schematic-v2 Service Variant Simplification

- Product strategy changed: express / rapid / skip-stop variants are service attributes, not independent schematic geometry.
- Schematic-v2 now renders one canonical route per service family. Selection prefers:
  - highest valid stop count,
  - then longest pathPoints length,
  - then ordinary/local naming,
  - then non-express naming.
- Express / rapid / skip-stop variants are recorded as hidden variants and no longer drive independent schematic-v2 route geometry.
- In schematic-v2, families with express service variants get a white center stripe on the canonical route. This is a service marker, not a separate stop pattern drawing.
- Geographic rendering remains unchanged. Old `schematic-lite` remains available. Exporter, JSON schema, raw `line.stops`, and raw `line.pathPoints` are unchanged.
- New diagnostics:

```text
artifacts\schematic-v2-diagnostics\service-family-simplification.txt
artifacts\schematic-v2-diagnostics\service-family-simplification.csv
```

- Current real `10号线` row:
  - canonical route: `line_1140583_1` / `10号线（机场快线-站站停）`,
  - hidden variants: `10号线（机场快线-大站快车）`, `10号线（机场快线-特快）`,
  - express marker applied: `True`.
- Current comparison SVGs:

```text
artifacts\schematic-v2-diagnostics\geographic-baseline.svg
artifacts\schematic-v2-diagnostics\schematic-v2-before-service-simplification.svg
artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.svg
artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified-debug.svg
```

## Phase 5C Transit Map Style System v1

- Added an opt-in CLI/render style:

```text
--style transit-map
```

- The default remains `--style standard`.
- Transit-map style is render-only and does not change exporter output, JSON schema, `line.stops`, or `line.pathPoints`.
- Transit-map style currently adds:
  - colored top title band,
  - centered `Transport System Map` title,
  - bottom key / legend panel,
  - transit-map station and interchange marker tokens,
  - geometry bounds that reserve room for the header and bottom key.
- Generated current primary-city style checks:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-geographic.svg
artifacts\schematic-v2-diagnostics\transit-map-style-geographic.full.png
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- This is a cartography/style framework v1. It does not solve final official-map layout by itself; future work should combine schematic-v2 quality improvements with render-time layout overrides.

## Phase 5D Schematic-v2 Sharp Angle Relaxation

- `transit-map-style-schematic-v2.full.png` exposed a very sharp `3号线` V-shaped detour.
- Geographic/pathPoints output shows the same section as a smoother westward corridor rather than a hard V.
- Added a renderer-only schematic-v2 sharp-detour relaxation pass after final route guides are built.
- The pass detects route-chain triples with:
  - acute turn angle,
  - high detour ratio,
  - enough direct distance to avoid tiny-junction false positives.
- Matching middle stations are moved toward the midpoint between their neighboring render-chain nodes, snapped to the schematic grid and kept within geometry bounds.
- This does not change exporter data, JSON schema, geographic output, raw `line.stops`, or raw `line.pathPoints`.
- Current regenerated image:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

## Phase 5C Follow-up - Transit Map Route Badges

- Transit-map style now draws route number badges for each visible display family.
- Badges are renderer-only and only appear with:

```text
--style transit-map
```

- Badge text is extracted from the display family name, for example `3号线` -> `3`.
- Badges use the display family route color, a white outline, and white centered text.
- The default `standard` style does not draw route badges.
- Current check image:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- Badge placement now tries multiple endpoint-side candidate positions and scores them against station marker boxes, rough station-label boxes, and already placed route badges.
- If a second endpoint badge cannot be placed without severe collision, it is skipped; each route still keeps at least one badge when possible.
- Current SVG diagnostics confirmed the primary-city transit-map schematic-v2 output has no route-badge-to-route-badge overlap.
- Remaining polish area: badge placement still uses approximate station-label boxes. Future work can reuse the final label placement boxes if route badges need stricter label avoidance.

## Phase 5D Follow-up - Shared Edge Guide Guard

- Real transit-map schematic-v2 review showed `3号线` west-side geometry partially covered `4号线`.
- Root cause: topology-level shared-edge route guides were expanding a true shared edge to neighboring stations from the host route, which could inject a neighboring branch station from one family into another family's render chain.
- Follow-up review showed the same mechanism could badly break `2号线` / `8号线` because repeated loop/return stops made `IndexOf` select the first occurrence and replace a long route interval with an unrelated shared edge.
- Fix:
  - exact topology shared-edge guides are no longer fed into schematic-v2 route-chain materialization,
  - exact shared edges are preserved by the raw service stop order and can still be detected from the final route chains,
  - route-guide metadata/materialization is only recorded when applying the guide actually changes a family render chain,
  - a regression test now covers the case `Line 3: A-B-C-D` and `Line 4: X-B-C-Y-Z`, ensuring `X/Y` are not injected into Line 3.
- This remains renderer-only and does not change exporter output, JSON schema, raw `line.stops`, raw `line.pathPoints`, or geographic rendering.

## Phase 5D Follow-up - 2/10 Geometry Guide Guard

- After guarding exact shared-edge materialization, the real `2号线` / `10号线` shared corridor briefly regressed because the schematic-v2 route guide could be reduced to a short overlay instead of shaping the `10号线` final render chain.
- The fix keeps exact shared-edge guides out of route-chain materialization, but allows high-confidence geometry corridors to materialize when they are anchored by an express/service family and a real shared adjacent edge.
- Geometry detection for schematic-v2 now uses standard map bounds internally even when rendering `--style transit-map`, so transit-map header/footer framing does not distort `pathPoints` corridor detection.
- Parallel corridor overlays are restricted to materialized geometry guide pairs. This keeps the real `2号线` / `10号线` corridor visible while preventing earlier false positives such as `2号线` / `8号线`, `3号线` / `4号线`, and `10号线` / `7号线`.
- Regenerated check:

```text
artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
```

- Current SVG diagnostics show `10号线` has the guide `station_602298_1>station_1068950_1>station_1068953_1>station_1152818_1`, and the only schematic-v2 parallel corridor overlay in the primary-city check is `2号线` / `10号线`.

## Phase 5D Follow-up - Exact Shared Platform Corridors

- Real schematic-v2 review showed the west-side `3号线` / `4号线` station group represents parallel tracks through the same stations, but the schematic route strokes were exactly coincident so `3号线` visually covered `4号线`.
- Added a separate renderer-only `exact-shared-platform` overlay source inside the schematic-v2 parallel corridor layer.
- This source is intentionally narrower than the `2号线` / `10号线` geometry guide:
  - it only inspects final schematic-v2 route chains,
  - it supports any two-or-more display families on the same exact final route-chain segment,
  - it supports single-edge shared runs,
  - it includes express/service families and keeps their white center stripe on the offset shared segment,
  - it does not materialize route guides or alter `line.stops` / `line.pathPoints`.
- Current regenerated checks:

```text
artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.svg
artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.full.png
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- SVG diagnostics now show:
  - `3号线` / `4号线` with `data-schematic-v2-parallel-corridor-source="exact-shared-platform"`,
  - `2号线` / `10号线` with `data-schematic-v2-parallel-corridor-source="geometry-route-guide"`,
  - no `2号线` / `8号线` parallel overlay in the primary-city check.

## Phase 5D.1 Schematic-v2 Candidate Freeze

- Current schematic-v2 candidate outputs were copied to:

```text
artifacts\schematic-v2-candidate-freeze\20260604-222351
```

- Freeze contents:
  - `schematic-v2-service-simplified.svg`
  - `schematic-v2-service-simplified.full.png`
  - `transit-map-style-schematic-v2.svg`
  - `transit-map-style-schematic-v2.full.png`
  - `notes.md`
- This freeze is the current regression reference before further label, badge, legend, and transit-map frame polish.
- Do not use this freeze to promote schematic-v2 as the default layout; geographic remains the alpha recommended baseline.

## Phase 5E Transit-map Readability Polish

- Route badge placement now uses the same placed station-label boxes as the final label renderer instead of rough label estimates.
- Badge placement still uses endpoint-side candidates, but a badge is now skipped when every candidate has a severe collision score. This prevents route-number badges from being forced on top of station names or other dense map content.
- The transit-map bottom key now wraps into fewer columns with more row spacing and includes an `Express service marker` sample for the white center stripe used by express / rapid / skip-stop service families.
- Regenerated primary schematic-v2 transit-map check:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- Manual check: west-side `3号线` / `4号线` badges are no longer stacked on station labels; `2号线` / `10号线` shared corridor remains visible; the bottom legend is easier to read.
- This remains renderer-only. No exporter, JSON schema, geographic output, raw `line.stops`, or raw `line.pathPoints` changes were made.

## Phase 5E Follow-up - Exact Shared Segment Visibility

- Reviewed the transit-map schematic-v2 output for a possible Line 7 draw-order problem.
- Root cause: exact final-route shared segments were only handled for narrow two-family/non-express cases and longer shared chains. Single shared edges involving an express/service family could still rely on normal route draw order.
- Fix:
  - schematic-v2 exact shared platform overlays now support single-edge shared runs,
  - they support two-or-more display families on the same final route-chain segment,
  - offsets are centered across all families, so one later route stroke does not hide the others,
  - express/service families keep the white center stripe on their offset shared segment.
- Current check SVG/PNG:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- Current SVG diagnostics show `exact-shared-platform` overlays for `10号线` / `7号线`, `10号线` / `1号线`, and `3号线` / `4号线`, and the geometry-route-guide overlay for `10号线` / `2号线`.
- This protects exact coincident schematic-v2 final segments from draw-order hiding. It does not claim to solve arbitrary near-parallel or close-but-not-identical segments; those still need future diagnostics if a validation city exposes them.

## Project Hygiene - 2026-06-05

- After resolving the accidental GitHub alpha.1 pull/merge, the repository was clean and synchronized with `origin/master`.
- Added `scripts\README.md` so the script folder has a stable entry point.
- Moved loose ignored smoke files:

```text
artifacts\tmp-pathpoints.svg
artifacts\tmp-pathpoints-composite.svg
artifacts\tmp-pathpoints-corridor.svg
```

to:

```text
artifacts\archive\pathpoints-smoke\
```

- Generated a fresh alpha validation bundle:

```text
artifacts\alpha-validation\20260605-095556-primary-city-post-cleanup
artifacts\alpha-validation\alpha-validation-20260605-095556-primary-city-post-cleanup.zip
```

- Bundle source input:

```text
D:\CS2MetroDiagram\metro-export.json
```

- The bundle generated baseline geographic, schematic-lite, schematic-v2 SVG/PNG outputs, schematic-v2 diagnostics, visual continuity report, notes, viewer settings, and a filled feedback template.
- Follow-up fix:
  - validation bundle notes and filled feedback now include both export generator version and current tool version,
  - stale export warnings are written when the versions differ,
  - placeholder city names such as `CS2 Metro Export` are called out so testers can identify the real city manually,
  - generated feedback file lists no longer wrap filenames in PowerShell backticks, avoiding the `baseline-*` backspace/control-character corruption.
- Verified with:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -CaseName freshness-check-fixed -SkipZip
```

- Test bundle:

```text
artifacts\alpha-validation\20260605-101043-freshness-check-fixed
```

## Viewer Export Data Inspector - 2026-06-05

- The Viewer now has two tabs:
  - `Map Preview` keeps the existing SVG preview.
  - `Export Data` is a read-only inspection panel for the loaded JSON.
- `Export Data` shows schema/generator/game versions, export time, line/station counts, total stops/pathPoints, interchange count, diagnostics-file status, line details, and station details.
- The inspection code is encapsulated in `src\MetroDiagram.Viewer\ExportDataInspector.cs`; keep future derived Viewer-only data summaries there instead of growing `MainWindow.xaml.cs`.
- `Open Diagnostics` opens the matching diagnostics text file when one is found.
- The inspector adds warnings for stale generator/tool version mismatch and placeholder city names.
- The SVG preview now writes a temporary HTML file under `%TEMP%\CS2MetroDiagramViewer` and navigates the embedded browser to that file. This is more reliable for large inline SVG than `NavigateToString`; the temp file is deleted on Viewer close when possible.
- Viewer preview scale is controlled by `PreviewZoom` in `viewer-settings.json`. Default `100` now reads the SVG root `width`/`height` and displays the SVG at actual rendered pixel size with scrollbars; `fit-width` gives a whole-map overview by scaling to the preview pane width.
- The Viewer app icon source is `src\MetroDiagram.Viewer\Assets\MetroDiagramViewerIcon.svg`; generated assets are `MetroDiagramViewerIcon-256.png` and `MetroDiagramViewer.ico`.
- Regenerate the icon with:

```text
scripts\generate-viewer-icon.ps1
```

- The `.ico` is wired through `MetroDiagram.Viewer.csproj` `ApplicationIcon` for the executable icon.
- Avoid setting `MainWindow.xaml` `Icon="Assets/MetroDiagramViewer.ico"` unless the pack URI is verified in the self-contained package. That XAML startup-time icon lookup caused a `XamlParseException` (`could not find resource assets/metrodiagramviewer.ico`) and made the Viewer appear to do nothing on double-click. The safer current behavior is to keep the executable icon and leave the window `Icon` attribute unset.
- The diagnostics lookup is path-based:
  - `metro-export.json` -> `metro-export-diagnostics.txt`
  - `exports\metro-export-{slug}-{timestamp}.json` -> `exports\metro-export-diagnostics-{slug}-{timestamp}.txt`
  - then falls back to `metro-export-diagnostics.txt` in the same folder.
- Viewer layout selection now includes `schematic-v2`, but geographic remains the recommended alpha default.
- This is Viewer-only alpha feedback tooling. Exporter logic, JSON schema, geographic rendering, raw `line.stops`, and raw `line.pathPoints` were not changed.

## Viewer Header Layout Polish - 2026-06-05

- Moved the loaded JSON path, city summary, and error text out of the cramped right-side toolbar area into a full-width header row below the render controls.
- Long loaded JSON paths now use text trimming plus a tooltip so the top-right header no longer clips export information when the Viewer is opened wide with many controls visible.
- Added Viewer render-setting sanitization for obviously broken saved values such as `legendWidth=24` or `padding=800`. Loading settings, saving settings, rendering, and size preset changes now normalize legend width and padding back to stable values so schematic-v2 is not distorted by stale custom settings.

## Schematic-v2 Size Stability - 2026-06-06

- Schematic-v2 now computes topology/grid/shared-corridor layout in a stable Poster-sized canonical space (`3200 x 2000`) and then maps the result into the requested output canvas.
- This keeps schematic-v2 content relationships stable across Compact, Standard, Poster, Ultra, and Custom sizes: output size should scale/framing the diagram, not change whether shared corridors such as 2号线 / 10号线 materialize.
- The change is renderer-only. It does not modify exporter logic, JSON schema, raw `line.stops`, raw `line.pathPoints`, geographic rendering, or schematic-lite.
- Added a regression test for a skip-stop geometry shared corridor fixture to ensure Standard and Poster both render the same materialized route-guide parallel corridor with the same pass-through station chain.

## Real Export City Name - 2026-06-06

- `metro-export.json` previously fell back to `CS2 Metro Export` / `UnnamedCity` because `RealMetroJsonExporter.MetroExport.CityName` was never populated.
- The real exporter now reads `Game.City.CityConfigurationSystem` after the world and `EntityManager` are available.
- Read order:
  - `cityName`
  - `overrideCityName`
  - private backing field `m_LoadedCityName`
- Each candidate is written to `metro-export-diagnostics.txt`, so manual validation can confirm which CS2 field supplied the city name.
- The resolved value is used for `city.name` and timestamped snapshot filenames. If all candidates are empty, the old fallback behavior remains.
- This is exporter metadata only. It does not alter line/station extraction, schema version, raw stops, pathPoints, or renderer behavior.

## Schematic-v2 Terminal Tail Straightening - 2026-06-07

- New Zhaoqing export exposed a schematic-v2 artifact where a near-terminal 8号线 tail rendered as a zigzag even though the geographic/pathPoints view reads as a simple straight terminal corridor.
- Cause: schematic-v2 was using the normalized station route chain and snapped station positions; a low-degree terminal tail with lateral station offsets could become a 45-degree back-and-forth even when no topology issue existed.
- Fix: renderer-only terminal-tail straightening for schematic-v2. It only applies to short tails from a terminal endpoint back to an interchange/high-degree anchor, only when the rendered polyline detour ratio is high, and only moves internal low-degree tail stations onto the anchor-to-terminal line.
- This does not modify exporter output, JSON schema, geographic rendering, schematic-lite, raw `line.stops`, raw `line.pathPoints`, or previously materialized shared corridor logic.
- Validation snapshot:

```text
D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json
artifacts\schematic-v2-diagnostics\latest-zhaoqing-schematic-v2.svg
```

- The regenerated CLI warning showed `terminal tail straightening: 1`, with two adjusted stations on the 8号线 tail.

## Paradox Mods Publish - 2026-06-06

- First Paradox Mods upload completed through the CS2 mod project publish profile.
- Published mod id:

```text
146643
```

- The mod is currently published as `Unlisted` in `CS2 Metro\Properties\PublishConfiguration.xml`.
- `PublishConfiguration.xml` now stores `<ModId Value="146643" />`; do not use `PublishNewMod` again unless intentionally creating a separate Paradox Mods listing.
- Future mod binary/version uploads should use:

```text
dotnet publish "CS2 Metro\CS2 Metro.csproj" --no-restore /p:PublishProfile=PublishNewVersion
```

- Metadata-only updates should use:

```text
dotnet publish "CS2 Metro\CS2 Metro.csproj" --no-restore /p:PublishProfile=UpdatePublishedConfiguration
```

- The Paradox publisher auto-logged in with the account currently authenticated in-game. If upload auth fails later, launch CS2 and sign in to the PDX account first.

## Project Review / Workflow Guardrails - 2026-06-11

- Added a single local validation entrypoint:

```text
scripts\validate-local.ps1
```

- By default it runs:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

- Use this when reviewing broad project changes or before committing. If the CS2 modding toolchain is configured, add `-IncludeModBuild` to also build `CS2 Metro\CS2 Metro.csproj`.
- Added a guarded Paradox Mods publish script:

```text
scripts\publish-mod.ps1 -Mode NewVersion
scripts\publish-mod.ps1 -Mode UpdateConfiguration
```

- The script requires `PublishConfiguration.xml` to contain the existing `ModId` and intentionally does not support `PublishNewMod`, reducing the chance of accidentally creating a duplicate Paradox Mods listing. Add `-WhatIf` to verify the target/profile without uploading.
- Added `docs\PROJECT_REVIEW_NOTES.md` to keep broad review findings and low-risk optimization backlog separate from phase state.

## Viewer schematic-lite retirement - 2026-06-11

- Removed `schematic-lite` from the Viewer layout dropdown.
- Existing Viewer settings with `layoutMode = schematic-lite` now normalize to `schematic-v2` so old settings do not silently reselect the retired mode.
- CLI `--layout schematic-lite` remains available for historical comparison and regression scripts.
- Added `docs\SCHEMATIC_REBUILD_PLAN.md` as the staged plan for a topology/corridor-first schematic-map rebuild.

## Schematic rebuild S2 canonical network model - 2026-06-11

- Added `src\MetroDiagram.Rendering\CanonicalSchematicNetwork.cs`.
- The new model is render-only and separate from `metro-export.json`:
  - station nodes,
  - service families,
  - service variants,
  - canonical route selection,
  - adjacency edges,
  - exact shared-edge corridor hints,
  - pathPoints-based geometry corridor hints,
  - interchange groups.
- `CanonicalSchematicNetworkBuilder.Build(document)` is now the intended S2 entrypoint for future schematic-v2 skeleton work.
- Current schematic-v2 rendering is not switched over yet; S2 is a stable graph layer for S3.
- Added tests:
  - `canonical schematic network selects service family route`,
  - `canonical schematic network records exact shared edges`,
  - `canonical schematic network records geometry corridor hints`.
- This does not modify exporter logic, JSON schema, geographic rendering, raw `line.stops`, or raw `line.pathPoints`.

## Schematic rebuild S3/S4 canonical-backed renderer wiring - 2026-06-11

- Schematic-v2 now builds `CanonicalSchematicNetwork` before layout and passes it into the layout pipeline.
- `ApplySchematicV2Layout` uses canonical adjacency edges for station graph constraints and canonical service-family stops for family path skeletons when the canonical network is available.
- Route-guide construction starts from canonical family routes, then applies the existing conservative geometry corridor materialization rules.
- Schematic-v2 shared corridor overlays now emit `data-schematic-v2-canonical-corridor="true"` for exact shared-platform and materialized geometry-route-guide overlays.
- This is S3/S4 initial wiring, not the final schematic-map solver. It deliberately leaves exporter logic, JSON schema, geographic output, raw `line.stops`, raw `line.pathPoints`, and Viewer defaults unchanged.
- Verified:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

## S4 product-candidate validation bundle - 2026-06-11

- `scripts\generate-alpha-validation-bundle.ps1` was updated for PowerShell 7 strict binding: empty validation-warning arrays are now valid for both `Write-ValidationNotes` and `Write-FilledFeedbackTemplate`.
- This fixed a failure where a clean export with no freshness warnings could not produce a bundle.
- Generated validation bundle:

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson 'D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json' -CaseName 'zhaoqing-s4-product-candidate'
```

- Output:

```text
artifacts\alpha-validation\20260611-232357-zhaoqing-s4-product-candidate
artifacts\alpha-validation\alpha-validation-20260611-232357-zhaoqing-s4-product-candidate.zip
```

- Generated transit-map style schematic-v2 product candidate:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- 'D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json' artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.svg --layout schematic-v2 --size ultra --style transit-map --hide-generic-labels --hide-crowded-labels --use-path-points
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\capture-svg-screenshot.ps1 -InputSvg artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.svg -OutputPng artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.full.png -Width 4200 -Height 2600
```

- Important screenshot note: when capturing `--size ultra`, pass `-Width 4200 -Height 2600`; otherwise Edge captures a smaller viewport with scrollbars instead of the full SVG canvas.
- Verified product candidate:

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\validate-local.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1
```

- Release package regenerated:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

- Release Viewer exe started successfully in a short smoke test.

## Product candidate map script - 2026-06-13

- Added:

```text
scripts\generate-product-candidate-map.ps1
```

- Default behavior:
  - input: `D:\CS2MetroDiagram\metro-export.json`
  - output root: `artifacts\product-candidate`
  - layout: `schematic-v2`
  - size: `ultra`
  - style: `transit-map`
  - enabled: `--use-path-points --hide-generic-labels --hide-crowded-labels`
- The script writes:
  - `metro-export.json`
  - `product-candidate.svg`
  - `product-candidate.full.png`
  - `render-log.txt`
  - `notes.md`
- It passes the correct screenshot viewport for the selected size preset, avoiding the earlier Edge screenshot issue where ultra SVGs were captured with scrollbars.

## Viewer default/non-important station label option - 2026-06-13

- The Viewer now exposes the generic-label setting as a positive checkbox:

```text
Show default / non-important station labels
显示默认/非重要站名
```

- The persisted setting remains `hideGenericStationLabels` in `Documents\CS2MetroDiagram\viewer-settings.json` for backward compatibility.
- Mapping:
  - checkbox checked -> `HideGenericStationLabels = false`
  - checkbox unchecked -> `HideGenericStationLabels = true`
- Renderer, CLI, exporter, and JSON schema behavior were not changed.

## Alpha.2 short-term validation pass - 2026-06-14

- Added:

```text
docs\ALPHA2_SHORT_TERM_CHECKLIST.md
```

- Generated the latest short-term validation bundle from:

```text
D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json
```

- Outputs:

```text
artifacts\alpha-validation\20260614-002122-zhaoqing-alpha2-short-term
artifacts\alpha-validation\alpha-validation-20260614-002122-zhaoqing-alpha2-short-term.zip
artifacts\product-candidate\20260614-002504-zhaoqing-alpha2-short-term
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

- Refreshed the self-contained Viewer package:

```text
artifacts\viewer-win-x64-self-contained\MetroDiagram.Viewer.exe
```

- NuGet / dotnet / npm cache should stay on `E:\CS2\.tool-cache` for future work to avoid filling C drive.

