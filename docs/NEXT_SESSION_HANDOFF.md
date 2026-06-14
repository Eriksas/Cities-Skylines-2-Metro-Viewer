# Next Session Handoff

## Current Version

`v0.1.0-alpha.2-candidate`

## Current Status

Phase 4D.4 is closed. The primary city baseline has been accepted for alpha.2 candidate review, and Phase 4E generated the `v0.1.0-alpha.2-candidate` package. Phase 4E.1 adds a renderer-only schematic-lite overlap resolver for exact snapped segment overlaps. Phase 4E.1a trims those overlap segment endpoints near stations/junctions so offset lines do not crowd the node center. Geographic remains the recommended alpha.2 default. Route continuity, stroke width consistency, station marker readability, and station route alignment are acceptable for alpha. Label readability remains a known polish area. Shared corridor and express stripe outputs remain experimental/off by default and should not block alpha testing. Phase 5B.3b now links pathPoints-based shared corridor detection to schematic-v2 route-guide materialization and continuous parallel corridor overlays. The real `10号线` / `2号线` case is visible in diagnostics as detected/materialized/parallelRendered.

Phase 5B.4 changes the schematic-v2 product strategy: express / rapid / skip-stop variants are service attributes, not independent schematic geometry. Schematic-v2 now draws one canonical route per service family and uses a white center stripe when the family contains express variants. The `10号线` canonical route is the station-rich `10号线（机场快线-站站停）`; `大站快车` and `特快` variants are hidden as independent geometry and recorded in diagnostics.

Follow-up work after visual review fixed the remaining `10号线` issue: schematic-v2 now applies route guides to the final render route chain, removes obvious canonical backtracking suffixes, renders the real `2号线` / `10号线` section as a continuous parallel corridor, and keeps the `10号线` white express center stripe visible inside that shared corridor in `artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.svg`. A later guard prevents exact shared-edge route guides from injecting neighboring branch stations, which fixed the observed `3号线` / `4号线` and `2号线` / `8号线` regressions. The current compromise is: exact shared edges alone do not materialize route chains; high-confidence geometry corridors may materialize only when anchored by an express/service family plus a real shared adjacent edge. Parallel overlays are limited to materialized geometry-guide pairs, so the primary-city check keeps `2号线` / `10号线` without reintroducing false-positive overlays. This is still experimental and does not change the geographic alpha baseline.

Latest schematic-v2 polish adds exact shared platform overlays for cases like the west-side `3号线` / `4号线` parallel-track stations and single-edge exact shared segments such as `10号线` / `7号线`. These overlays are built from final route chains only, support any two-or-more display families on the same exact segment, include express/service families, and do not materialize route guides. Current regenerated SVGs show `3号线` / `4号线`, `10号线` / `7号线`, and `10号线` / `1号线` as `exact-shared-platform`, while `2号线` / `10号线` remains `geometry-route-guide`; `2号线` / `8号线` does not receive a false parallel overlay.

Project hygiene after the accidental GitHub alpha.1 pull:

- Merge conflict cleanup has been completed and pushed.
- `scripts\README.md` was added as the script index.
- Loose ignored pathPoints smoke SVGs were moved to `artifacts\archive\pathpoints-smoke\`.
- Fresh validation bundle:

```text
artifacts\alpha-validation\20260605-095556-primary-city-post-cleanup
artifacts\alpha-validation\alpha-validation-20260605-095556-primary-city-post-cleanup.zip
```

- Use this bundle as the latest post-cleanup sanity check before changing renderer behavior again.
- Validation harness improvement after bundle review:
  - `generate-alpha-validation-bundle.ps1` now writes export/tool version freshness warnings,
  - stale `v0.1.0-alpha.1` exports are allowed but clearly marked when reviewed by `v0.1.0-alpha.2-candidate` tooling,
  - filled feedback filenames no longer use PowerShell backticks, fixing the `baseline-geographic.svg` corruption.
- Latest script verification bundle:

```text
artifacts\alpha-validation\20260605-101043-freshness-check-fixed
```

Viewer alpha feedback loop follow-up:

- The Viewer now has a `Map Preview` tab for the SVG map and an `Export Data` tab for read-only export inspection.
- `Export Data` summarizes schema/generator/game versions, export time, line/station totals, total stops/pathPoints, interchange count, matching diagnostics status, per-line details, and per-station details.
- The data-inspection logic is encapsulated in `src\MetroDiagram.Viewer\ExportDataInspector.cs`; future Viewer-only summaries should live there.
- The tab can open matching diagnostics files directly and warns when the export version appears stale or the city name is a placeholder.
- Viewer layout selection now includes experimental `schematic-v2`; do not promote it as the default yet.
- Viewer map preview now uses a temporary HTML file in `%TEMP%\CS2MetroDiagramViewer` for more reliable in-app SVG display and switches back to `Map Preview` after rendering.
- Viewer now has an application icon. Source: `src\MetroDiagram.Viewer\Assets\MetroDiagramViewerIcon.svg`; generated executable icon: `src\MetroDiagram.Viewer\Assets\MetroDiagramViewer.ico`; regeneration script: `scripts\generate-viewer-icon.ps1`.
- This is useful for alpha testers who need to confirm what the JSON contains before attaching feedback. It does not change exporter output or schema.

Phase 5D.1 froze the current schematic-v2 candidate artifacts under:

```text
artifacts\schematic-v2-candidate-freeze\20260604-222351
```

Use this freeze as the regression reference before continuing label, badge, legend, and transit-map frame polish. Do not restart topology work unless a later real-city validation shows a blocking route relationship issue.

## Main Artifacts

```text
artifacts/releases/CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts/releases/CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

## Current Working Capabilities

- CS2 real metro JSON export.
- Transport debug dump.
- Test metro JSON export.
- CLI SVG generation.
- `geographic` layout.
- `schematic-lite` layout.
- WPF Viewer exe.
- Chinese/English Viewer UI.
- Label filtering options.
- Release packaging.
- Experimental `line.pathPoints` export.
- Path geometry validation scripts.
- Metro track geometry discovery debug output.
- CurveElement-first path point extraction.
- Route geometry notes and validation summary in `docs\ROUTE_GEOMETRY_NOTES.md`.
- Adaptive geographic pathPoint simplification and suspicious-jump SVG diagnostics.
- Experimental renderer-only parallel corridor offset, default off.
- Renderer-only service family merge for same-line express/local variants.
- Experimental renderer-only shared corridor style, default off.
- Experimental renderer-only express white center stripe, default off.
- Recommended alpha baseline: geographic + UsePathPoints + service family merge / normalized family rendering.
- Schematic-lite secondary layout now offsets exact overlapping snapped segments so multiple display families remain visible.
- Schematic-lite overlap offsets are trimmed near station/junction endpoints so station markers stay visually clean.
- Schematic-v2 geometry shared corridor diagnostics:
  - `geometry-shared-corridors.txt/csv`,
  - `geometry-shared-corridor-debug.svg`,
  - `schematic-v2-route-guides.txt/csv`,
  - real `10号线 + 2号线` corridor detected from pathPoints with confidence `1`.
- Schematic-v2 materialized parallel corridor overlays:
  - `schematic-v2-parallel-corridors.txt/csv`,
  - `schematic-v2-parallel-corridor.svg`,
  - `schematic-v2-parallel-corridor-debug.svg`.
- Schematic-v2 service-family output after follow-up:
  - `artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.svg`,
  - `artifacts\schematic-v2-diagnostics\schematic-v2-service-simplified.full.png`,
  - real `2号线` / `10号线` corridor is visible as a 4-point `schematic-v2-parallel-corridor` run,
  - `10号线` no longer draws the repeated backtracking tail that appeared in the earlier service-simplified SVG,
  - `10号线` shared-corridor express markers use `data-schematic-v2-parallel-corridor-express-marker="true"`.
- Real export snapshot naming:
  - latest files remain `metro-export.json` and `metro-export-diagnostics.txt`,
  - each real export also writes `exports\metro-export-{citySlug}-{yyyyMMdd-HHmmss}.json`,
  - matching diagnostics snapshots use the same timestamp,
  - if a real city name is unavailable, snapshot naming falls back to `UnnamedCity`.
- Primary city baseline generation:
  - use `scripts\generate-primary-city-baseline.ps1`,
  - output lives under `artifacts\primary-city-baseline\latest`,
  - history snapshots live under `artifacts\primary-city-baseline\history`,
  - accepted alpha.2 candidate freeze is `artifacts\primary-city-baseline\history\20260530-102042`,
  - latest package smoke regenerated baseline history `artifacts\primary-city-baseline\history\20260530-223217`,
  - current alpha candidate image is `artifacts\primary-city-baseline\latest\baseline-geographic.full.png`.

## Known Limitations

- Only metro/subway is supported.
- No PNG/PDF export.
- No style presets.
- No drag editing.
- `schematic-lite` is still imperfect.
- Shared corridor and express stripe are experimental and are not recommended as default alpha output.
- Schematic-lite overlap handling is intentionally narrow; it does not replace the simple schematic-lite layout algorithm with full automatic layout.
- Schematic-v2 geometry shared corridor detection is experimental. It now proves that pathPoints/topology guide constraints can expose physical corridor sharing for the `10号线` / `2号线` validation case, and the latest service-simplified output renders the shared section visibly. Schematic-v2 still needs broader manual review before it can be promoted.
- Route-run fragmentation can still cause small visual discontinuities in some geographic outputs.
- Station merge / interchange detection needs more testing across real cities.
- Only one real city has been deeply validated for CurveElement pathPoints.
- Current visual tuning still uses one primary real city as the benchmark. Keep using it for regression checks, but do not special-case any city, line, station, or coordinate in code.
- Phase 4D.2 made only title/legend/framing polish; do not infer that shared corridor, express stripe, or 5A.9 should start next.
- Phase 4D.3 made only station marker, label, and legend polish; shared corridor, express stripe, and 5A.9 remain out of scope unless future baseline review makes them necessary.
- Phase 4D.4 made only renderer-time station marker/label anchor alignment; exporter/schema/pathPoints/stops remain unchanged.
- Phase 4E.1 made only schematic-lite segment overlap visibility changes; exporter/schema/pathPoints/stops/geographic remain unchanged.
- Phase 4E.1a made only schematic-lite overlap endpoint trim changes; it does not rewrite schematic layout or alter exporter/schema/geographic behavior.
- The analyzed diagnostics file did not yet include the newest explicit Phase 5A.3b labels `curve sample point count` and `path targets fallback count`, although final JSON source metadata confirmed CurveElement-only pathPoints.

## Latest Validation Result

Latest default files:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

Timestamped snapshots now live next to the latest files under:

```text
D:\CS2MetroDiagram\exports\
Documents\CS2MetroDiagram\exports\
```

Phase 4D.0 in-game manual validation passed:

- latest export exists,
- timestamped snapshot exists,
- repeated export does not overwrite old snapshots,
- latest diagnostics exists,
- snapshot diagnostics exists,
- Viewer opens latest through `Open Default Export`,
- Viewer opens snapshot through `Open JSON`.

Key numbers:

- 11 subway lines,
- 157 stops,
- 157 route segments,
- 2432 CurveElements,
- 9739 final pathPoints,
- 9739 `RouteSegment.CurveElement` source points,
- 0 final `RouteSegment.PathTargets` source points,
- 0 CurveElement fallback diagnostics.

Airport express checks:

- `10号线（机场快线-大站快车）`: 8 stops -> 1065 pathPoints.
- `10号线（机场快线-特快）`: 4 stops -> 1025 pathPoints.

Comparison SVGs were generated under:

```text
artifacts\path-geometry-comparison
```

Use `docs\ROUTE_GEOMETRY_NOTES.md` for the route geometry checklist, track geometry discovery notes, and Phase 5A.3c result summary.

## Phase 5B.3a Geometry Shared Corridor Validation

- Run diagnostics:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-schematic-v2-diagnostics.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -OutputDir artifacts\schematic-v2-diagnostics
```

- Important current outputs:

```text
artifacts\schematic-v2-diagnostics\geometry-shared-corridors.txt
artifacts\schematic-v2-diagnostics\geometry-shared-corridors.csv
artifacts\schematic-v2-diagnostics\schematic-v2-route-guides.txt
artifacts\schematic-v2-diagnostics\schematic-v2-route-guides.csv
artifacts\schematic-v2-diagnostics\schematic-v2-geometry-shared-corridor.svg
artifacts\schematic-v2-diagnostics\schematic-v2-geometry-shared-corridor-debug.svg
```

- Current real-city evidence:
  - `10号线 + 2号线` geometry corridor is detected,
  - approximate shared length `541.455`,
  - average distance `16.754`,
  - max distance `65.892`,
  - confidence `1`,
  - route guide rows are emitted for both families.
- Phase 5B.3b evidence:
  - `schematic-v2-parallel-corridors.csv` reports `10号线 + 2号线` as `detected=True`, `materialized=True`, `parallelRendered=True`,
  - host family is `2号线`, follower family is `10号线`,
  - SVG overlay elements include `data-schematic-v2-shared-corridor-run`, `data-schematic-v2-parallel-corridor`, `data-schematic-v2-route-guide-materialized`, and `data-schematic-v2-pass-through-stations`.
- PNG capture is still pending for this validation pass because the Edge screenshot helper required elevation and the escalation request was rejected by the environment quota. Re-run the capture step when permissions are available.

## Phase 5B.3c Render Route Chain Reconstruction

- Phase 5B.3c closes the gap where a geometry corridor was detected and debug attributes existed, but the follower line could still be rendered mostly from raw stops plus a short overlay.
- Real materialization now means:

```text
raw service stops + corridor guide nodes
-> final schematic-v2 render route chain
-> rendered polyline
```

- Pass-through guide nodes are render-only and do not change exporter output, JSON schema, `line.stops`, or `line.pathPoints`.
- Current real `10号线 + 2号线` diagnostics:
  - `detected=True`,
  - `hostFamily=2号线`,
  - `followerFamily=10号线`,
  - `hostIntervalNodeCount=4`,
  - `passThroughNodeCount=2`,
  - `renderRouteChainBeforeCount=14`,
  - `renderRouteChainAfterCount=16`,
  - `materialized=True`,
  - `parallelRendered=True`.
- Current comparison SVGs:

```text
artifacts\schematic-v2-diagnostics\schematic-v2-before-route-chain.svg
artifacts\schematic-v2-diagnostics\schematic-v2-route-chain-materialized.svg
artifacts\schematic-v2-diagnostics\schematic-v2-route-chain-materialized-debug.svg
```

- Next session should visually inspect the route-chain materialized SVG before promoting schematic-v2. Geographic remains the alpha recommended baseline.

## Phase 5A.3d Rendering Polish

- Code-side renderer polish is implemented.
- Geographic `UsePathPoints=true` now simplifies projected SVG path geometry with adaptive tolerances.
- The renderer preserves first/last points and station-nearest path anchors.
- Suspicious long path jumps are counted and split into separate route polylines.
- Path route SVG elements include diagnostics for original/cleaned point counts, reduction ratio, max segment length, suspicious jump count, and effective simplification tolerance.
- No JSON schema, CS2 exporter, Viewer UI, or CLI flag changes were made.

## Phase 5A.4a Parallel Corridor Offset MVP

- Code-side renderer MVP is implemented.
- New option: `SvgRenderOptions.EnableParallelCorridorOffset`, default `false`.
- CLI flag: `--enable-parallel-corridor-offset`.
- The feature only affects `geographic + UsePathPoints`; schematic-lite is not changed.
- Cleaned route fragments are segmentized, grouped through approximate corridor detection, and offset per corridor group.
- SVG debug attributes include `data-corridor-id`, member count, offset index, and offset pixels.
- Viewer checkbox is intentionally deferred to Phase 5A.4b.
- Real visual review showed noticeable jitter, so do not keep tuning lateral offsets as the main strategy.

## Phase 5A.5 Service Family Merge

- Code-side renderer implementation is complete.
- New option: `SvgRenderOptions.EnableServiceFamilyMerge`, default `true`.
- CLI flag: `--disable-service-family-merge`.
- Same-line variants with obvious bracket suffixes are grouped at render time only:
  - `10号线（机场快线-特快）` -> `10号线`,
  - `10号线（机场快线-大站快车）` -> `10号线`,
  - `Line 10 (Express)` -> `Line 10`.
- The main map renders only the primary line for each family; primary selection prefers more pathPoints, then more stops, then stable ordering.
- The legend lists each service variant with stop count and endpoints when stations resolve.
- Route SVG elements include display family debug attributes.
- No exporter or JSON schema changes were made.

## Phase 5A.6 Shared Corridor Composite Stroke

- Code-side renderer implementation is complete.
- New option: `SvgRenderOptions.EnableSharedCorridorCompositeStroke`, default `false`.
- CLI flag: `--enable-shared-corridor-composite-stroke`.
- The feature only affects `geographic + UsePathPoints`; schematic-lite is not changed.
- Detection runs after service family merge and uses display family primary paths, not raw exported lines.
- The first nested outer/separator/inner composite stroke was generated but is no longer the target visual style.
- Real visual review showed the old output was cut into too many small fragments:

```text
artifacts\path-geometry-comparison\08-geographic-shared-corridor-composite.svg
```

- Latest smoke check found 246 composite layer fragments and 26 too-many-family skip fragments in the available real export.

## Phase 5A.6b Continuous Shared Corridor and Express Marker

- Code-side renderer implementation is complete.
- This is still renderer-only:
  - no CS2 exporter change,
  - no JSON schema change,
  - no `line.stops` or `line.pathPoints` semantic change,
  - no schematic-lite behavior change,
  - no Viewer UI change.
- `EnableSharedCorridorCompositeStroke` remains the opt-in switch, but the implementation now builds longer shared corridor runs instead of per-segment nested strokes.
- Shared corridor matching uses display family primary paths after Phase 5A.5 service-family merge.
- Exactly two display families render with `data-shared-corridor-style="shanghai-like"`:
  - continuous corridor base,
  - inner color band,
  - shared centerline geometry,
  - no lateral offset.
- Shared run debug attributes:
  - `data-shared-corridor="true"`,
  - `data-shared-corridor-run-id`,
  - `data-shared-corridor-family-a`,
  - `data-shared-corridor-family-b`,
  - `data-shared-corridor-point-count`,
  - `data-shared-corridor-layer`.
- Three or more shared families still fall back with `data-shared-corridor-skipped="too-many-families"`.
- New option: `SvgRenderOptions.EnableExpressCenterStripe`, default `false`.
- CLI flag: `--enable-express-center-stripe`.
- Express-like families are detected from variant/original names containing `快`, `特快`, `大站快车`, `机场快线`, `express`, or `rapid`.
- Express marker debug attributes:
  - `data-express-marker="white-center-stripe"`,
  - `data-express-family`.
- Real smoke outputs:

```text
artifacts\path-geometry-comparison\09-geographic-shared-corridor-shanghai-like.svg
artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.svg
```

- Latest XML smoke summary:
  - old `08`: 246 shared/composite elements,
  - new `09`: 52 shared elements across 26 shared runs, with 25 too-many-family skip fragments,
  - new `10`: 52 shared elements across 26 shared runs and 14 express stripe elements.
- Full-size PNG screenshots were generated with the Edge headless helper:

```text
artifacts\path-geometry-comparison\09-geographic-shared-corridor-shanghai-like.full.png
artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.full.png
```

- `scripts\capture-svg-screenshot.ps1` avoids the local `npx.ps1` execution-policy issue and the uncached Playwright/npm timeout by using installed Microsoft Edge headless directly.

## Phase 5A.7 Geographic Corridor Rendering Pipeline Stabilization

- Code-side renderer implementation is complete.
- This remains renderer-only:
  - no CS2 exporter change,
  - no JSON schema change,
  - no `line.stops` or `line.pathPoints` semantic change,
  - no schematic-lite behavior change,
  - no Viewer UI change.
- The key conclusion is that the remaining visual problems were pipeline and continuity issues, not simple stroke-width/color tuning.
- Added SVG debug summary script:

```text
scripts\analyze-svg-render-debug.ps1
```

- Reports generated:

```text
artifacts\path-geometry-comparison\svg-render-debug-summary-before-5A7.txt
artifacts\path-geometry-comparison\svg-render-debug-summary-after-5A7.txt
```

- The route renderer now builds a geographic corridor render plan for `geographic + UsePathPoints` output when experimental lateral offset is not enabled.
- Route layers are emitted in this fixed order:
  1. normal route base strokes,
  2. shared corridor base strokes,
  3. shared corridor inner bands,
  4. express center stripe decorations.
- Shared corridor style is now marked as:

```text
data-shared-corridor-style="shanghai-like-continuous"
```

- Express center stripes are decoration commands. They are skipped on shared-corridor/fallback conflicts with:

```text
data-express-marker-skipped="shared-corridor-style-conflict"
```

- Real smoke SVG outputs:

```text
artifacts\path-geometry-comparison\11-geographic-pathpoints-baseline.svg
artifacts\path-geometry-comparison\12-geographic-service-family-merge.svg
artifacts\path-geometry-comparison\13-geographic-shared-corridor-continuous.svg
artifacts\path-geometry-comparison\14-geographic-corridor-express-on.svg
```

- XML debug summary improved from before to after:
  - route path elements: 143 -> 121,
  - shared corridor path elements: 52 -> 34,
  - shared corridor runs: 26 -> 17,
  - express stripe path elements: 14 -> 10,
  - `10号线` fragments: 38 -> 30.
- Full-size PNG screenshots for `13` and `14` were generated after screenshot permissions were granted:

```text
artifacts\path-geometry-comparison\13-geographic-shared-corridor-continuous.full.png
artifacts\path-geometry-comparison\14-geographic-corridor-express-on.full.png
```

## Phase 5A.8 Visual Continuity And Style Normalization

- Code-side renderer implementation is complete.
- This remains renderer-only:
  - no CS2 exporter change,
  - no JSON schema change,
  - no `line.stops` or `line.pathPoints` semantic change,
  - no schematic-lite behavior change,
  - no Viewer UI or CLI flag change.
- The key conclusion is that the remaining post-5A.7 problem is visual continuity and network-level style normalization, not data export.
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

- `SvgVisualStyle` now centralizes route and marker tokens:
  - normal route width,
  - shared corridor outer/inner widths,
  - express stripe width,
  - station/interchange marker radii and stroke widths,
  - label halo width.
- Shared corridor total width now matches the normal route width. Express stripe is a narrower internal decoration and does not change the base route width.
- Station markers now render as unfilled rings so routes remain visually continuous under station markers.
- Shared corridor run merging now supports near-touch append, reverse-append, prepend, and reverse-prepend cases.
- Real smoke outputs:

```text
artifacts\path-geometry-comparison\15-geographic-family-normalized.svg
artifacts\path-geometry-comparison\15-geographic-family-normalized.full.png
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.svg
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.full.png
artifacts\path-geometry-comparison\17-geographic-express-normalized.svg
artifacts\path-geometry-comparison\17-geographic-express-normalized.full.png
```

- Latest visual continuity report still identifies remaining short fragments and near-touching run breaks. Treat those as future renderer run-organization targets, not exporter/schema issues.

## Phase 5A.8 Closeout Default Strategy

- Default recommendation for alpha:

```text
geographic + UsePathPoints + service family merge / normalized family rendering
```

- Current recommended manual review baseline:

```text
artifacts\path-geometry-comparison\15-geographic-family-normalized.svg
artifacts\path-geometry-comparison\15-geographic-family-normalized.full.png
```

- Experimental comparison outputs only:

```text
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.svg
artifacts\path-geometry-comparison\16-geographic-shared-corridor-normalized.full.png
artifacts\path-geometry-comparison\17-geographic-express-normalized.svg
artifacts\path-geometry-comparison\17-geographic-express-normalized.full.png
```

- Shared corridor and express stripe should remain default-off and should not be presented as the recommended alpha output.
- Do not start new visual style work before multi-city alpha validation.
- If rendering refinement is needed before alpha, keep it narrowly scoped to Phase 5A.9 Route Run Stitcher:
  - reduce short route fragments,
  - merge near-touching route runs for the same family/style,
  - preserve suspicious-jump splits,
  - do not add new styles,
  - do not change exporter/schema/stops/pathPoints.

## Phase 4D.1 Primary City Regression Baseline

- Use the current real city as the primary validation benchmark, but do not special-case it.
- Baseline command:

```powershell
scripts\generate-primary-city-baseline.ps1
```

- Baseline latest output:

```text
artifacts\primary-city-baseline\latest\metro-export.json
artifacts\primary-city-baseline\latest\metro-export-diagnostics.txt
artifacts\primary-city-baseline\latest\baseline-geographic.svg
artifacts\primary-city-baseline\latest\baseline-geographic.full.png
artifacts\primary-city-baseline\latest\visual-continuity-summary.txt
artifacts\primary-city-baseline\latest\notes.md
```

- Baseline render settings:
  - layout: `geographic`,
  - `UsePathPoints=true`,
  - service family merge enabled,
  - shared corridor disabled,
  - express stripe disabled,
  - size preset: `poster`.
- Latest generated summary:
  - normal route base strokes: 9,
  - shared corridor strokes: 0,
  - express stripe strokes: 0,
  - normal stroke width: 14,
  - current-threshold visual continuity risks: none.
- Do not start Phase 5A.9 unless the primary city baseline and visual continuity report both show that run stitching is needed.

## Phase 4D.2 Alpha Candidate Baseline Polish

- Code-side polish is complete.
- The refreshed primary city baseline is:

```text
artifacts\primary-city-baseline\latest\baseline-geographic.svg
artifacts\primary-city-baseline\latest\baseline-geographic.full.png
artifacts\primary-city-baseline\latest\visual-continuity-summary.txt
artifacts\primary-city-baseline\latest\notes.md
```

- The same run was archived under:

```text
artifacts\primary-city-baseline\history\20260530-093512
```

- Title behavior:
  - real city name: `{CityName} Metro Diagram`,
  - current CS2 placeholder city name: `CS2 Metro Diagram`,
  - blank city name: `Unnamed City Metro Diagram`.
- Baseline settings remain:
  - layout: `geographic`,
  - `UsePathPoints=true`,
  - service family merge enabled,
  - shared corridor disabled,
  - express stripe disabled,
  - size preset: `poster`.
- Shared corridor and express stripe remain experimental/off by default.
- Do not start Phase 5A.9 unless future manual review finds baseline visual discontinuity that is also supported by the visual continuity report.

## Phase 4D.3 Station Marker And Label Readability Polish

- Code-side polish is complete.
- The refreshed primary city baseline is:

```text
artifacts\primary-city-baseline\latest\baseline-geographic.svg
artifacts\primary-city-baseline\latest\baseline-geographic.full.png
artifacts\primary-city-baseline\latest\visual-continuity-summary.txt
artifacts\primary-city-baseline\latest\notes.md
```

- The same run was archived under:

```text
artifacts\primary-city-baseline\history\20260530-095317
```

- Station marker defaults:
  - white-filled circles,
  - dark outlines,
  - ordinary radius `6.2`,
  - interchange radius `9.8`.
- Label / legend defaults:
  - station label font size `14`,
  - label gap `12`,
  - legend width `380`,
  - legend label font size `17`.
- Baseline settings still remain:
  - layout: `geographic`,
  - `UsePathPoints=true`,
  - service family merge enabled,
  - shared corridor disabled,
  - express stripe disabled,
  - size preset: `poster`.
- Latest visual continuity report shows no current-threshold visual continuity risks.
- Do not start Phase 5A.9 unless route discontinuity becomes a confirmed blocker again.

## Phase 4D.4 Station Route Anchor Alignment

- Code-side renderer implementation is complete.
- The refreshed primary city baseline is:

```text
artifacts\primary-city-baseline\latest\baseline-geographic.svg
artifacts\primary-city-baseline\latest\baseline-geographic.full.png
artifacts\primary-city-baseline\latest\visual-continuity-summary.txt
artifacts\primary-city-baseline\latest\notes.md
```

- The same run was archived under:

```text
artifacts\primary-city-baseline\history\20260530-100650
```

- Station route anchoring behavior:
  - enabled by default for `geographic + UsePathPoints`,
  - ordinary stations project to their related display family primary route path,
  - segment projection is preferred over nearest path point snapping,
  - too-far stations fall back to raw render position,
  - interchanges average close anchors across related display families,
  - spread-out interchange anchors fall back to raw position.
- SVG station and label debug attributes include:
  - `data-station-anchor`,
  - `data-station-anchor-applied`,
  - `data-station-anchor-distance`,
  - `data-station-anchor-family`,
  - `data-station-anchor-fallback`,
  - `data-station-raw-x`,
  - `data-station-raw-y`.
- Latest visual continuity report shows no current-threshold visual continuity risks.
- Do not start Phase 5A.9 unless route-run discontinuity becomes a confirmed blocker again.

## Recommended Next Phases

- Next: manual review of the refreshed Phase 4D.2 alpha candidate baseline.
- After acceptance: external alpha testing and bug triage.
- Optional: validate CurveElement pathPoints on one or two more real cities before broader release.
- Phase 4D / alpha multi-city validation: test simple one-line, multi-line, loop/branch, airport/express, and dense interchange cities.
- Optional narrow Phase 5A.9 Route Run Stitcher only if baseline visual discontinuities block alpha review.
- Later, after alpha feedback: PNG export.
- Later, after alpha feedback: style presets.
- Later, after alpha feedback: manual override / layout tuning.

## Suggested Starting Point

Start the next session by reading:

```text
docs/NEXT_SESSION_HANDOFF.md
docs/PROJECT_STATE.md
docs/README.md
docs/ROUTE_GEOMETRY_NOTES.md
docs/KNOWN_ISSUES.md
docs/FEEDBACK_TEMPLATE.md
```

Avoid starting new renderer feature work. The immediate priority is repeatable baseline generation and manual comparison.

## Latest Renderer Note

- Phase 4E.1b technically passed tests but failed visual acceptance. Do not treat it as a completed visual fix.
- Phase 4E.1c is the current schematic-lite overlap regression fix.
- The schematic-lite short overlap segment `1760,992|1792,1024` was inspected directly in the generated SVG.
- 4E.1c keeps that segment as a full centered connector with:
  - `data-schematic-overlap-fallback="unsafe-short-or-junction"`,
  - `data-schematic-overlap-safe-offset="false"`,
  - `data-schematic-overlap-safe-offset-reason="short-segment"`,
  - `data-schematic-render-dedupe-skipped="continuity-priority"`.
- This means very short overlap segments may not show every color distinctly, but they should not break route continuity.
- This remains schematic-lite-only and renderer-only; geographic rendering, exporter logic, JSON schema, `line.stops`, and `line.pathPoints` were not changed.

## Phase 4E.2 Status

- Phase 4E.2 is implemented code-side.
- Dense schematic-lite station areas are now handled by station spacing relaxation, not additional overlap trimming.
- Schematic-lite render order is now:
  1. compute initial snapped station positions,
  2. relax stations that are too close,
  3. build route polylines from adjusted station positions,
  4. apply conservative overlap resolver,
  5. draw routes, markers, labels, legend.
- Generated comparison artifacts:

```text
artifacts\path-geometry-comparison\schematic-lite-before-spacing.full.png
artifacts\path-geometry-comparison\schematic-lite-station-spacing.full.png
artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.full.png
```

- Geographic remains the alpha recommended baseline. Schematic-lite remains a secondary layout.

## Phase 5B Status

- Phase 5B.0 diagnostics and a first code-side `schematic-v2` layout mode are implemented.
- Do not continue patching schematic-lite-v1 as the main path. The current principle is:

```text
A schematic map may distort geography, but it must not distort topology.
```

- New CLI mode:

```text
--layout schematic-v2
```

- Diagnostics command:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-schematic-v2-diagnostics.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json
```

- Key outputs:

```text
artifacts\schematic-v2-diagnostics\topology-summary.txt
artifacts\schematic-v2-diagnostics\adjacency-edges.csv
artifacts\schematic-v2-diagnostics\station-degree.csv
artifacts\schematic-v2-diagnostics\dense-junctions.csv
artifacts\schematic-v2-diagnostics\schematic-topology-debug.full.png
artifacts\path-geometry-comparison\schematic-v2-geographic-baseline.full.png
artifacts\path-geometry-comparison\schematic-v2-schematic-lite-v1.full.png
artifacts\path-geometry-comparison\schematic-v2.full.png
```

- Current v2 is intentionally basic: topology-correct/readable exploration, not final visual style.

## Phase 5B.2 Status

- Schematic-v2 direction is accepted; do not roll it back and do not return to schematic-lite-v1 patch-mode work.
- Phase 5B.2 code-side implementation is complete:
  - schematic-v2 selects a topology-rich service variant per display family,
  - exact shared station-edge corridors are preserved in schematic-v2 routing,
  - shared corridor diagnostics are generated.
- Current real diagnostic finding:

```text
2号线 + 10号线 shared edge:
station_1068950_1 -> station_1068953_1
```

- New artifacts:

```text
artifacts\schematic-v2-diagnostics\shared-corridors.txt
artifacts\schematic-v2-diagnostics\shared-corridors.csv
artifacts\schematic-v2-diagnostics\schematic-v2-shared-corridor-debug.full.png
artifacts\path-geometry-comparison\schematic-v2-before-shared-corridor.full.png
artifacts\path-geometry-comparison\schematic-v2-shared-corridor.full.png
```

- Next manual review should compare:
  - geographic baseline,
  - schematic-lite-v1,
  - schematic-v2-before-shared-corridor,
  - schematic-v2-shared-corridor,
  - schematic-v2 shared corridor debug.
- Geographic remains the alpha recommended baseline. Schematic-v2 remains experimental until shared corridor behavior is manually validated.

## Phase 4F Status

- Phase 4F shifts the next work from visual feature development to repeatable alpha validation bundles.
- New script:

```text
scripts\generate-alpha-validation-bundle.ps1
```

- Recommended command:

```text
powershell -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -CaseName primary-city
```

- Output:

```text
artifacts\alpha-validation\<timestamp>-<caseName>
artifacts\alpha-validation\alpha-validation-<timestamp>-<caseName>.zip
```

- Each bundle contains:
  - copied export JSON and diagnostics when available,
  - geographic baseline SVG/PNG,
  - schematic-lite SVG/PNG,
  - schematic-v2 SVG/PNG,
  - visual continuity report,
  - schematic-v2 diagnostics,
  - notes and a filled feedback template.
- Next priority: collect validation bundles from multiple real cities.
- Do not continue shared corridor / express stripe styling work unless multiple validation bundles show a repeated issue.
- Do not make schematic-v2 the default; geographic remains the alpha recommended baseline.

## Phase 5C Status

- Phase 5C starts the transit-map cartography direction.
- New CLI/render style:

```text
--style transit-map
```

- Default output remains `--style standard`.
- Transit-map style is renderer-only and currently adds:
  - colored top title band,
  - centered `Transport System Map` title,
  - bottom key / legend panel,
  - transit-map station and interchange marker tokens,
  - route number badges for visible display families,
  - geometry bounds that reserve room for the title and key.
- Generated current checks:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-geographic.svg
artifacts\schematic-v2-diagnostics\transit-map-style-geographic.full.png
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- Next likely direction: continue toward official-map output by improving schematic-v2 map skeleton quality and eventually adding render-time layout overrides. Do not change exporter or JSON schema for this.

## Phase 5D Status

- Phase 5D started with a narrow schematic-v2 skeleton quality fix.
- The first issue fixed was a sharp `3号线` V-shaped detour in:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- The fix adds schematic-v2 sharp-angle relaxation after final route guide construction.
- It is renderer-only and does not affect exporter, JSON schema, geographic rendering, raw stops, or raw path points.
- Build/tests passed after the fix.
- Follow-up transit-map route badges are also renderer-only and appear only with `--style transit-map`.
- Route badges now use collision-aware endpoint placement. They avoid station marker zones, approximate station-label zones, and previously placed badges. A second endpoint badge may be skipped if all candidates are too crowded.
- A schematic-v2 shared-edge guard prevents exact shared-edge guides from being materialized into route chains. This fixed the observed `3号线` / `4号线` west-side over-merge and the follow-up `2号线` / `8号线` loop/repeated-stop regression in the transit-map schematic-v2 output.
- Remaining transit-map polish candidates are badge collision avoidance, label hierarchy, and optional per-line label overrides. Keep these separate from exporter/schema work.

## Phase 5E Status

- Phase 5E performed a narrow readability polish pass for the opt-in transit-map output.
- Route badges now score against final placed station-label boxes, not rough estimates.
- Endpoint badges are skipped when every candidate has a severe collision score, including the first endpoint badge. This is intentional: a missing badge is preferable to one covering a station name.
- The bottom key now includes an `Express service marker` sample when a white center stripe is present.
- Current regenerated check:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- Manual review after regeneration:
  - west-side `3号线` / `4号线` badges are separated and no longer sit on station labels,
  - `2号线` / `10号线` shared corridor remains visible,
  - the legend wraps more cleanly and explains the express stripe marker.
- Verified:

```text
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build CS2MetroDiagram.slnx --no-restore
```

- Follow-up check for Line 7 hiding other routes:
  - schematic-v2 exact shared platform overlays now support single-edge runs and two-or-more display families,
  - express/service families are included, so `10号线` keeps its white center stripe on shared exact segments,
  - current SVG has explicit `exact-shared-platform` overlays for `10号线` / `7号线`, `10号线` / `1号线`, and `3号线` / `4号线`,
  - current SVG still has the materialized geometry-route-guide overlay for `10号线` / `2号线`.
- Regenerated output:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

- Verified after this follow-up:

```text
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build CS2MetroDiagram.slnx --no-restore
```

- Next good step: continue alpha validation with more city bundles, or do a small label hierarchy pass if the user wants another visual polish round. Do not change exporter/schema/geographic defaults for this work.
- 2026-06-06 schematic-v2 size stability follow-up: schematic-v2 now computes topology/grid/shared-corridor layout in a canonical Poster-sized space and scales it to the requested output size. Standard/Compact should no longer change whether `2号线` / `10号线` is materialized compared with Poster/Ultra; if it does, treat that as a renderer regression rather than a tuning issue.
- 2026-06-06 real export city-name fix: `RealMetroJsonExporter` now populates `city.name` from `Game.City.CityConfigurationSystem` (`cityName`, `overrideCityName`, then `m_LoadedCityName`). Diagnostics record each candidate. Manual next step: deploy the latest mod, restart CS2, export real metro JSON, and confirm `D:\CS2MetroDiagram\metro-export.json` plus the timestamped snapshot use the real in-game city name instead of `CS2 Metro Export` / `UnnamedCity`.
- 2026-06-06 Paradox Mods first upload completed. Published mod id is `146643`, currently `Unlisted`. `CS2 Metro\Properties\PublishConfiguration.xml` stores the id; future uploads should use `PublishNewVersion`, while metadata-only edits should use `UpdatePublishedConfiguration`.
- 2026-06-07 schematic-v2 terminal-tail straightening added after a Zhaoqing export showed the 8号线 southern terminal tail as a zigzag. The fix is renderer-only, applies only to short terminal tails with high detour ratio, and regenerated `artifacts\schematic-v2-diagnostics\latest-zhaoqing-schematic-v2.svg` with `terminal tail straightening: 1`.
- 2026-06-11 lightweight project review added workflow guardrails:
  - `scripts\validate-local.ps1` for the normal local build/test validation flow,
  - `scripts\publish-mod.ps1` for guarded existing-listing Paradox Mods updates,
  - `docs\PROJECT_REVIEW_NOTES.md` for broad review findings and refactor backlog.
  Keep broad renderer refactors out of alpha validation unless a repeated city bundle shows the need.
- 2026-06-11 schematic rebuild S2 added `CanonicalSchematicNetworkBuilder`. It builds a render-only graph with station nodes, service families, variants, canonical routes, adjacency edges, interchange groups, exact shared-edge hints, and pathPoints geometry corridor hints.
- 2026-06-11 schematic rebuild S3/S4 initial wiring is complete. Schematic-v2 now builds the canonical network before layout, uses canonical adjacency and canonical service-family routes for the layout skeleton, and marks exact/materialized shared corridor overlays with `data-schematic-v2-canonical-corridor="true"`.
- This is not the final schematic-map solver. Next schematic work should continue toward S5/S6: official-map styling and validation bundles, or a later full skeleton solver if repeated real-city cases show the need. Keep exporter, schema, geographic output, raw stops, and raw pathPoints unchanged unless the user explicitly changes direction.
- 2026-06-11 S4 product-candidate validation was generated from `D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json`.
  - Bundle: `artifacts\alpha-validation\20260611-232357-zhaoqing-s4-product-candidate`
  - Bundle zip: `artifacts\alpha-validation\alpha-validation-20260611-232357-zhaoqing-s4-product-candidate.zip`
  - Transit-map schematic candidate: `artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.svg`
  - Transit-map schematic PNG: `artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.full.png`
  - Release package refreshed: `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip`
  - `scripts\generate-alpha-validation-bundle.ps1` now supports empty validation-warning arrays under PowerShell 7.
  - Release Viewer exe launched successfully in a smoke test.
- 2026-06-13 added `scripts\generate-product-candidate-map.ps1` for the current best human-review output. It renders one focused product candidate, captures a full-size PNG with the correct preset viewport, copies the source export JSON, and writes notes.
  - Latest generated candidate: `artifacts\product-candidate\20260613-225609-zhaoqing-schematic-v2-transit-map`
  - SVG: `artifacts\product-candidate\20260613-225609-zhaoqing-schematic-v2-transit-map\product-candidate.svg`
  - PNG: `artifacts\product-candidate\20260613-225609-zhaoqing-schematic-v2-transit-map\product-candidate.full.png`
  - Verified with `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\validate-local.ps1`.
- Viewer label option follow-up: the old negative `Hide generic station labels` checkbox is now a positive `Show default / non-important station labels` checkbox. It still writes the existing `hideGenericStationLabels` setting internally, so old settings remain compatible.
- 2026-06-14 short-term alpha.2 pass refreshed the validation bundle, product-candidate map, self-contained Viewer, and alpha.2 release package. Start manual smoke from `docs\ALPHA2_SHORT_TERM_CHECKLIST.md`.
  - Validation bundle: `artifacts\alpha-validation\20260614-002122-zhaoqing-alpha2-short-term`
  - Validation zip: `artifacts\alpha-validation\alpha-validation-20260614-002122-zhaoqing-alpha2-short-term.zip`
  - Product candidate: `artifacts\product-candidate\20260614-002504-zhaoqing-alpha2-short-term`
  - Release zip: `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip`
- 2026-06-14 cleanup pass removed obsolete local artifacts, build outputs, `docs\archive`, and tracked `CS2 Metro\Library\ilpp.pid`. Regenerate deleted diagnostics/comparison outputs with the scripts in `scripts\` if needed.
