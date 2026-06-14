# CS2 Metro Diagram

CS2 Metro Diagram is a Cities: Skylines II metro diagram tool in early development.

Current release version: `v0.1.0-alpha.2-candidate`.

This is an alpha release, not a stable release. It is intended for early testing and feedback.

## Alpha Safety Note

- This is alpha software.
- Back up saves before testing any mod.
- The CS2 mod exports data only and should not alter city data.
- Report issues using `docs/FEEDBACK_TEMPLATE.md`.

The current milestone is an alpha.2 candidate build for external validation. The CS2 mod exports JSON only, and the local Windows Viewer opens `metro-export.json`, previews the generated SVG, switches layout mode, adjusts render and label settings, supports basic English/Chinese UI text, and saves SVG output.

## Current Focus

- Keep the alpha default output stable: `geographic + UsePathPoints + service family merge / normalized family rendering`.
- Primary city baseline accepted for alpha.2 candidate: `artifacts\primary-city-baseline\latest\baseline-geographic.full.png`.
- Preserve export history with timestamped real-export snapshots while keeping Viewer `Open Default Export` compatible.
- Core, Rendering, CLI, and Tests must remain independent from Cities: Skylines II game assemblies.
- Shared corridor and express stripe outputs remain experimental and are not the recommended alpha default.
- Transit-map style output is now being explored as an opt-in style preset. In-game SVG preview, PNG/PDF export, drag editing, and mod-launched external processes are intentionally postponed.

Alpha.2 candidate includes CurveElement-first path geometry, service family merge, normalized geographic rendering, station marker readability polish, station route anchor alignment, export snapshot naming, and the primary city baseline workflow. Shared corridor and express stripe renderers remain opt-in experimental comparison modes.

Phase 5A.2 extends line JSON with optional `pathPoints` while preserving the existing `stops` logic. It does not implement corridor rendering, common-track offsets, PNG/PDF export, style presets, or drag editing.
Phase 5A.2b keeps the schema and exporter behavior unchanged while cleaning path geometry at render time and adding Compact / Standard / Poster / Ultra size presets.
Phase 5A.2c keeps the same `line.pathPoints` schema but improves the CS2 exporter extraction order: `RouteSegment.CurveElement`, then `RouteSegment.PathElement`, then `RouteSegment.PathTargets` fallback. This is still experimental until manually validated in game.
Phase 5A.3d keeps the same schema and UI/CLI switches while making geographic path rendering use adaptive simplification by default, protecting station-adjacent points, and splitting suspicious long jumps instead of drawing them as one continuous route segment.
Phase 5A.4a adds an experimental renderer-only parallel corridor offset for geographic pathPoints. It is off by default and does not affect schematic-lite.
Phase 5A.5 merges obvious same-line service variants such as `10号线（机场快线-特快）` and `10号线（机场快线-大站快车）` into one display line family in the main map, while the legend lists the variant stop patterns.
Phase 5A.6's first nested shared corridor composite stroke passed tests but was visually rejected on the real map because it fragmented continuous corridors. Phase 5A.6b keeps the feature renderer-only and default-off, but now builds longer shared corridor runs with a Shanghai 3/4-inspired centerline style. It also adds an opt-in white center stripe for express/rapid-like service families.

## Alpha Release Package

Build the alpha release folder and zip with:

```text
scripts\package-alpha-release.ps1
```

The script writes:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

Release package contents:

- `Mod`: current local CS2 mod artifacts, when available.
- `Viewer`: self-contained Windows x64 Viewer package.
- `docs`: project and release documentation.
- `samples`: sample metro JSON files.
- `README.md`, `QUICK_START.md`, `KNOWN_ISSUES.md`, `CHANGELOG.md`, `build-info.txt`.

Start with `QUICK_START.md` for tester instructions and `KNOWN_ISSUES.md` before reporting bugs.

For the full documentation map, see `docs/README.md`.

## Viewer Usage

Development run:

```text
dotnet run --project src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
```

Publish a self-contained Windows x64 package:

```text
scripts\publish-viewer-self-contained.ps1
```

The self-contained package is written to:

```text
artifacts\viewer-win-x64-self-contained
```

Framework-dependent package:

```text
scripts\publish-viewer-framework-dependent.ps1
```

The framework-dependent package is written to:

```text
artifacts\viewer-win-x64-framework-dependent
```

For normal users:

1. Open the published package folder.
2. Double-click `MetroDiagram.Viewer.exe`.
3. Click `Open Default Export` if `D:\CS2MetroDiagram\metro-export.json` or `Documents\CS2MetroDiagram\metro-export.json` exists.
4. Otherwise click `Open JSON` and choose a sample JSON or real export.

The Viewer opens the map inside the app after JSON load; saving SVG is only needed when you want to export the preview. Use the `Preview` zoom control to switch between actual SVG pixel-size preview (`100%`) and scaled overview (`Fit width`). The Windows executable includes a CS2 Metro Diagram app icon.

Inside the viewer:

1. Click `Open JSON` or `Open Default Export`.
2. Switch `Layout` between `geographic` and experimental `schematic-v2`.
3. Adjust width, height, legend width, padding, line width, station radius, label font size, or grid size.
4. Use label options to show or hide default/non-important station names, hide crowded low-priority labels, and keep interchanges and terminals visible.
5. Use `Use exported path geometry` to draw geographic routes from exported `pathPoints` when available.
6. Choose a size preset (`Compact`, `Standard`, `Poster`, `Ultra`) or keep `Custom` width and height.
7. Use `Simplify path geometry` and path tolerance to remove duplicate, very short, or nearly-collinear path points in the preview.
8. Switch `Language` between English and Chinese when needed.
9. Click `Refresh Preview`.
10. Click `Save SVG`.

The Viewer has two main tabs:

- `Map Preview`: renders the selected JSON as SVG using the current render options.
- `Export Data`: shows a read-only inspection view of the loaded export, including schema/generator/game versions, export time, line and station counts, total stops/pathPoints, interchange count, per-line stop/pathPoint details, per-station line membership, and whether a matching diagnostics file was found next to the JSON. It also warns when the export generator version differs from the current Viewer/tool version or when the city name looks like a placeholder. Use `Open Diagnostics` from this tab to open the matching diagnostics text file when available.

Current real exports are expected under `D:\CS2MetroDiagram`, with `Documents\CS2MetroDiagram` also supported as a fallback. The latest files remain `metro-export.json` and `metro-export-diagnostics.txt`; real exports also create timestamped snapshots under the `exports` subdirectory. Viewer settings are saved to `Documents\CS2MetroDiagram\viewer-settings.json`.

`Open Default Export` always opens the latest `metro-export.json`. Use `Open JSON` to load a timestamped snapshot manually.

Real metro export diagnostics are written next to the export as `metro-export-diagnostics.txt`. Phase 5A.2c records per-line route segment count, CurveElement count, PathElement count, path point counts before/after cleanup, source summary, skipped segment reasons, and the first sampled path points.

## Offline Usage

After building the offline projects, convert one sample file with:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- samples\sample-metro-small.json output.svg
```

The generated SVG should include a city title, colored route lines, station dots, station labels, interchange markers, and a legend.

Optional render parameters:

```text
--layout geographic|schematic-lite|schematic-v2 --style standard|transit-map --size compact|standard|poster|ultra --grid-size N --schematic-min-station-spacing N --width N --height N --legend-width N --padding N --line-width N --station-radius N --label-font-size N --center-expansion --hide-generic-labels --hide-crowded-labels --always-show-interchanges --always-show-terminals --use-path-points --simplify-path-points --no-simplify-path-points --path-simplification-tolerance N --min-path-segment-length N --enable-parallel-corridor-offset --disable-service-family-merge --enable-shared-corridor-composite-stroke --enable-express-center-stripe
```

For a dense real export, a larger canvas can help:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.svg --width 1600 --height 1100 --label-font-size 11 --hide-generic-labels --hide-crowded-labels
```

To use exported route geometry in geographic mode:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.pathpoints.svg --layout geographic --use-path-points
```

To render a larger poster-sized SVG:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.poster.svg --layout geographic --size poster --use-path-points
```

To render the experimental transit-map style frame:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.transit-map.svg --layout geographic --style transit-map --size poster --use-path-points --hide-generic-labels --hide-crowded-labels
```

Layout modes:

- `geographic`: default Phase 3B behavior using normalized source coordinates.
- `schematic-lite`: render-time layout that snaps stations to a grid, lightly separates stations that snap too close together, and tries to make route segments horizontal, vertical, or 45-degree diagonal. It does not change the JSON data.
- `schematic-v2`: experimental topology-first schematic layout. It prioritizes stop order, adjacency, interchange nodes, and route continuity before geographic accuracy or visual polish.

The Viewer no longer exposes `schematic-lite`; use CLI `--layout schematic-lite` only for historical comparison. Future schematic-map work should build on the topology-first `schematic-v2` path rather than extending the old schematic-lite patch pipeline.

`line.pathPoints` is optional and experimental. Geographic rendering can use it for route polylines when `--use-path-points` or the Viewer `Use exported path geometry` option is enabled. Schematic-lite keeps using station stops by default.

Map styles:

- `standard`: current default SVG frame and right-side legend.
- `transit-map`: experimental official-map style frame with a colored title band, bottom key panel, transit-map station tokens, route number badges, and more map-like framing. It is render-only and does not change exporter data or the JSON schema.

Path point simplification is enabled by default when path points are used. It works on temporary render data only and does not modify the loaded `MetroExportDocument`. Geographic path rendering now adds SVG diagnostics such as original/cleaned point counts, reduction ratio, max segment length, and suspicious jump count.

Parallel corridor offset is experimental and off by default. Use `--enable-parallel-corridor-offset` with `--layout geographic --use-path-points` to draw nearby shared path segments as separate parallel fragments. SVG route fragments include `data-corridor-id` and related debug attributes when an offset is applied.

Service family merge is enabled by default. It is render-only and does not change `metro-export.json`: obvious bracketed variants share one main-map route, and the legend shows each service variant with stop count and endpoint names. Use `--disable-service-family-merge` to render every exported line separately.

Shared corridor styling is experimental and off by default. Use `--enable-shared-corridor-composite-stroke` with `--layout geographic --use-path-points` to draw exactly two shared display families as continuous `shanghai-like` centerline corridors. Three or more shared families are skipped in this version and marked with debug attributes.

Express center stripe is experimental and off by default. Use `--enable-express-center-stripe` to draw a thin white center stripe over express/rapid-like display families.

To capture a generated SVG as PNG for visual review without Playwright:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\capture-svg-screenshot.ps1 -InputSvg samples\generated-svg\metro-export.svg -OutputPng samples\generated-svg\metro-export.png -Width 3200 -Height 2000
```

Render a real export both ways:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.geographic.svg --layout geographic
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.schematic-lite.svg --layout schematic-lite --grid-size 32
```

Label options:

- `--hide-generic-labels`: hides ordinary generic/default station labels such as `Station 1`, `Metro Station`, and common CS2 default subway station names.
- `--hide-crowded-labels`: hides low-priority labels when they overlap already placed higher-priority labels.
- `--always-show-interchanges`: keeps interchange labels visible even if other label filters are enabled.
- `--always-show-terminals`: keeps terminal labels visible even if other label filters are enabled.

To render every sample JSON into `samples/generated-svg/`:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $out = Join-Path 'samples\generated-svg' ($_.BaseName + '.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $out
}
```

## Samples

- `sample-metro-small.json`: one simple line.
- `sample-metro-interchange.json`: two lines sharing one station.
- `sample-metro-branch.json`: a trunk and branch service.
- `sample-metro-loop.json`: a loop line plus a spur.
- `sample-metro-missing-fields.json`: missing names, missing color, blank city name, and one missing stop reference.
- `sample-metro-large-network.json`: five-line network with multiple interchanges.
- `sample-metro-pathpoints.json`: one line with optional `pathPoints` route geometry.

## Verification

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
scripts\publish-viewer-self-contained.ps1
scripts\package-alpha-release.ps1
```
