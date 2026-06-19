# Development Notes

This file now contains current operational notes only. Full historical notes before the 2026-06-18 cleanup are archived at:

```text
docs\archive\2026-06-18-doc-consolidation\DEV_NOTES.full.md
```

## Repo And Runtime

Workspace:

```text
E:\CS2\CS2 Metro
```

Real export directory:

```text
D:\CS2MetroDiagram
```

Use PowerShell 7 (`pwsh`) for scripts when available. Older Windows PowerShell can still run many scripts, but screenshot/capture helpers are more reliable through `pwsh`.

## Build And Test

Run from repo root:

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Run sequentially to avoid DLL locking.

## CS2 Mod Build And Deploy

Build command template:

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

Deploy latest built mod before in-game testing:

```powershell
scripts\deploy-local-mod.ps1
```

Current deploy destination:

```text
E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods\CS2 Metro
```

Restart Cities: Skylines II after deploying.

## Real Export Behavior

Latest files:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

Snapshot files:

```text
D:\CS2MetroDiagram\exports\metro-export-{citySlug}-{yyyyMMdd-HHmmss}.json
D:\CS2MetroDiagram\exports\metro-export-diagnostics-{citySlug}-{yyyyMMdd-HHmmss}.txt
```

Snapshot timestamps use local time. City slugs are sanitized for Windows filenames. If city name is unavailable, `UnnamedCity` is used.

## Validation Bundle Workflow

Generate a bundle for the latest export:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName primary-city
```

Generate from a snapshot:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson 'D:\CS2MetroDiagram\exports\metro-export-城市名-yyyymmdd-hhmmss.json' `
  -CaseName city-review
```

Refresh the bundle index:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-alpha-validation-bundles.ps1
```

Outputs:

```text
artifacts\alpha-validation\index.md
artifacts\alpha-validation\index.csv
```

## Product Candidate Map Workflow

Generate a product-facing schematic-map candidate:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName product-candidate
```

Use this for visual experiments around `schematic-map`. The script now also runs
the schematic-map SVG audit and writes these files beside the candidate SVG/PNG:

```text
schematic-map-audit.txt
schematic-map-route-segments.csv
schematic-map-layout-conflicts.csv
schematic-map-style-widths.csv
schematic-map-parallel-corridors.csv
```

Run the audit directly on an existing SVG:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\analyze-schematic-map-svg.ps1 `
  -InputSvg artifacts\product-candidate\<case>\product-candidate.svg `
  -InputJson D:\CS2MetroDiagram\exports\metro-export-城市名-yyyymmdd-hhmmss.json `
  -OutputDir artifacts\product-candidate\<case>
```

Use the audit to review:

- route segment angle and octilinear drift;
- synthetic bend count and which routes needed render-only bend points;
- direction divergence against exported station positions when comparable;
- exact shared-platform/parallel corridor overlay consistency;
- route badge and station-label conflicts;
- route badge to route badge conflicts;
- route and corridor stroke-width token consistency.

Recent Sheffield schematic-map candidate after disabling default synthetic bends and tightening route-badge spacing:

```text
artifacts\product-candidate\20260619-143053-sheffield-badge-spacing
```

The audit for this candidate reported 0 synthetic bends, 16 remaining non-octilinear segment warnings, 0 direction-divergence warnings, 0 short-segment warnings, and 0 badge conflicts. Manual review preferred this visual family over the synthetic-bend experiment, so synthetic bends should stay opt-in until a better visual scoring rule exists.

## Schematic Regression Gate

Run this before accepting schematic-map renderer changes:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1
```

Fast smoke mode:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1 -LatestExports 0 -SkipPng
```

The script discovers the latest real export, recent snapshot exports,
`samples\regression\*.json`, and selected legacy samples. It writes:

```text
artifacts\schematic-regression\<timestamp>\index.md
artifacts\schematic-regression\<timestamp>\regression-summary.csv
artifacts\schematic-regression\<timestamp>\cases\...
```

Recent real-export gate:

```text
artifacts\schematic-regression\20260619-214528
```

Recent sample smoke gate:

```text
artifacts\schematic-regression\20260619-215440
```

## Alpha.2 Candidate Package - 2026-06-19

Release package generated:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

The CS2 mod was rebuilt with the local modding toolchain before packaging and
copied into `artifacts\cs2-local-mods\CS2 Metro`. The package script then copied
that artifact into the release `Mod` folder.

Viewer publish and release package both completed successfully. The package is
ready for manual smoke testing before public upload.

## Roadmap Operating Rule

Short-term work should improve alpha validation reliability before adding larger
features. Prefer this order:

1. keep build/test green,
2. generate validation bundles for new real exports,
3. fix obvious Viewer or schematic-map regressions,
4. update docs and bundle notes,
5. only then consider new renderer behavior.

Viewer package-size reduction is intentionally deferred until the alpha.2
candidate is stable. The current self-contained package is large, but changing
publish strategy too early would add packaging noise while layout behavior is
still moving.

## Layout Modes

`geographic`

- Alpha recommended baseline.
- Uses exported path geometry when enabled.
- Keep stable.

`schematic-v2`

- Experimental topology/diagnostic schematic.
- Used as a base for schematic-map work.

`schematic-map`

- Product-facing official-map candidate.
- More abstract and map-like than schematic-v2.
- Still experimental.
- Uses a more assertive octilinear snap tolerance than schematic-v2 so product
  candidates better follow horizontal, vertical, and 45-degree metro map grammar.
- Has an opt-in render-only synthetic bend experiment for long locked route
  segments. This does not move station markers, labels, stops, pathPoints, or
  exported data.
- Synthetic bends are disabled by default for product candidates after manual
  review found the initial experiment less visually natural despite fewer audit
  warnings.
- Direction polish should be evidence-led through `schematic-map-audit.txt`, not
  one-off screenshot memory.

`schematic-lite`

- Legacy mode.
- Removed from Viewer.
- Still available in CLI for historical comparison.

## Current Guardrails

- Do not modify exporter or JSON schema for renderer/layout polish.
- Do not mutate `line.stops` or `line.pathPoints`.
- Keep new schematic work render-only.
- Prefer validation bundles over ad hoc screenshots.
- Keep docs short; archive long historical notes.

## Schematic-map Crossing Notes

- Current `schematic-map` non-station crossings are rendered directly with no extra bridge, gap, or overpass marker.
- The renderer still audits non-station crossings and reports them in warnings.
- Current `metro-export.json` path points store planar route geometry only, so the renderer cannot yet know real CS2 over/under order.
- To make over/under order factual later, the exporter would need to capture a stable elevation/layer signal from route segments, lanes, tracks, or sampled curve points.

## Schematic-map Shared Platform Notes

- Exact shared platform corridors are rendered through a dedicated overlay layer after masking duplicate base strokes.
- `VisibleLaneResolver` collapses same-number/same-color branch families such as `7号线` and `7号线支线` into one visible lane on the exact shared segment.
- Shared-platform lane ordering is intentionally generic:
  - collapsed multi-family lanes are placed before single-family lanes,
  - ties use continuation-side ordering from the adjacent route geometry,
  - family-name order is only the final fallback.
- This keeps cases like Terminal City Bank -> 现代地铁站 readable (`7号线`/`7号线支线` above `5号线`) without special-casing station names or line ids.

## Schematic Shared Platform Notes

- Exact shared platform corridors are renderer-only and are detected from exact shared schematic segments after service-family simplification.
- The renderer first writes a white `data-schematic-v2-parallel-corridor-knockout="true"` polyline to mask duplicate base route strokes, then draws the visible parallel overlays.
- Visible overlay strokes include `data-schematic-v2-parallel-stroke-width` for debugging.
- This prevents close parallel platform sections from looking uneven because of base route plus overlay double drawing.
- Overlay count is based on `VisibleLaneResolver`, not raw display family count. Same-number and same-color branch families such as `7号线` / `7号线支线` collapse into one visible lane, while different visible lines such as `5号线` / `7号线` still render as parallel lanes.
- SVG debug attributes include `data-schematic-v2-visible-lane-key`, `data-schematic-v2-visible-lane-token`, `data-schematic-v2-visible-lane-reason`, and `data-schematic-v2-visible-lane-families`.
- Recent validation candidate:

```text
artifacts\product-candidate\20260618-163119-sheffield-shared-platform-widths
artifacts\product-candidate\20260618-175734-sheffield-branch-lane-collapse
artifacts\product-candidate\20260618-182727-sheffield-visible-lane-resolver
```

## Schematic-map Scoring Audit - 2026-06-19

Phase 6A.0 adds a lightweight SVG scoring audit for `schematic-map` product candidates.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 `
  -InputJson 'D:\CS2MetroDiagram\exports\metro-export-谢菲尔德-20260617-223135.json' `
  -CaseName sheffield-layout-score-framework
```

The candidate folder now includes:

```text
schematic-map-audit.txt
schematic-map-route-segments.csv
schematic-map-layout-conflicts.csv
schematic-map-style-widths.csv
schematic-map-parallel-corridors.csv
schematic-map-crossings.csv
schematic-map-turns.csv
schematic-map-score.csv
```

Latest scoring candidate:

```text
artifacts\product-candidate\20260619-150108-sheffield-layout-score-framework-final
```

Current score summary:

```text
overall: 67.46 / 100
octilinear-grammar: 79.46 / 100
route-crossings: 88.00 / 100
badge-layout: 100.00 / 100
stroke-width-consistency: 100.00 / 100
```

Use the score to compare candidate versions and prioritize work. Do not optimize the numeric score blindly; manual visual review still wins when a score improvement makes the map feel worse.

## Product Candidate Comparison - 2026-06-19

Use this before and after schematic-map changes:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-product-candidates.ps1 -LatestCount 4
```

The script writes:

```text
comparison.html
comparison.md
comparison.csv
comparison.full.png
```

Latest comparison:

```text
artifacts\product-candidate-comparison\20260619-151758
```

This is the preferred screenshot-review helper for future large layout work. It does not change rendering output; it only compares existing product candidate folders.

## Schematic-map Debug Overlay - 2026-06-19

`scripts\analyze-schematic-map-svg.ps1` now also writes:

```text
schematic-map-debug.svg
schematic-map-debug.full.png
```

The overlay marks non-octilinear route segments in orange and interior route
crossings with red target markers. Use it together with
`schematic-map-score.csv` before changing schematic-map layout behavior. The
latest Sheffield debug overlay is:

```text
artifacts\product-candidate\20260619-150108-sheffield-layout-score-framework-final\schematic-map-debug.full.png
```

Current measurable Sheffield issues remain 16 non-octilinear route segments and
2 interior crossings. Badge conflicts and normal route stroke-width drift are
currently clean.

## Shared Platform Knockout Fringe Fix - 2026-06-19

Exact shared platform corridors use a white knockout stroke to hide duplicate
base route strokes before drawing visible parallel lanes. The previous knockout
width included an extra outer buffer, which could show as white fringes around
parallel platform segments and near station markers.

The knockout width is now clamped to the visible colored lane envelope and
records both values in SVG debug attributes:

```text
data-schematic-v2-knockout-width
data-schematic-v2-visible-envelope-width
```

Recent validation candidate:

```text
artifacts\product-candidate\20260619-171203-sheffield-knockout-fringe-fix
```

## Documentation Cleanup - 2026-06-18

The docs root was simplified to reduce context pressure.

Long pre-cleanup snapshots:

```text
docs\archive\2026-06-18-doc-consolidation
```

Older phase-specific docs:

```text
docs\archive\historical
```

## Documentation Maintenance - 2026-06-19

Root docs are the current operational surface:

```text
docs\README.md
docs\PROJECT_STATE.md
docs\NEXT_SESSION_HANDOFF.md
docs\DEV_NOTES.md
docs\DECISION_LOG.md
```

Keep `PROJECT_STATE.md` short and current. Put durable why/why-not decisions in
`DECISION_LOG.md`, operational commands and paths in `DEV_NOTES.md`, and long
chronological evidence in validation artifacts or `docs\archive`.

If a root doc starts reading like a transcript, archive the full version and
replace it with a concise summary plus artifact paths.
