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

Common real export directory:

```text
D:\CS2MetroDiagram
```

The CS2 mod settings page also allows a custom export folder. Recommended
presets are `Documents\CS2MetroDiagram`, `Desktop\CS2MetroDiagram`, and
`D:\CS2MetroDiagram`.

Use PowerShell 7 (`pwsh`) for scripts when available. Older Windows PowerShell can still run many scripts, but screenshot/capture helpers are more reliable through `pwsh`.

## Build And Test

Run from repo root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1
```

The preflight script restores `CS2MetroDiagram.slnx`, builds the offline
solution, and runs the lightweight renderer test project. Use this before
committing or pushing. To keep the working folder light after verification:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1 -CleanBuildOutput
```

Manual equivalent:

```powershell
dotnet restore CS2MetroDiagram.slnx
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Run sequentially to avoid DLL locking.

GitHub Actions runs the same `scripts\preflight.ps1` workflow on pushes and
pull requests to `master`.

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

The selected export folder is used by:

- `Export Real Metro JSON`
- `Export Test Metro JSON`
- `Export Transport Debug Dump`
- `Metro Track Geometry Debug`

Latest files:

```text
<export folder>\metro-export.json
<export folder>\metro-export-diagnostics.txt
```

Snapshot files:

```text
<export folder>\exports\metro-export-{citySlug}-{yyyyMMdd-HHmmss}.json
<export folder>\exports\metro-export-diagnostics-{citySlug}-{yyyyMMdd-HHmmss}.txt
```

Snapshot timestamps use local time. City slugs are sanitized for Windows filenames. If city name is unavailable, `UnnamedCity` is used.

Viewer default export detection checks `D:\CS2MetroDiagram`,
`Documents\CS2MetroDiagram`, and `Desktop\CS2MetroDiagram`. Other custom folders
can be opened manually with `Open JSON`.

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

Viewer publish and release package both completed successfully.

Manual smoke validation passed, then the candidate was uploaded:

- GitHub pre-release tag: `v0.1.0-alpha.2-candidate`
- GitHub release asset: `CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip`
- Paradox Mods ModId: `146643`
- Paradox Mods version: `0.1.0-alpha.2-candidate`
- Paradox Mods access level: `Unlisted`

The first Paradox publish attempt failed because `ModVersion` was still
`0.1.0-alpha.2`, matching an existing server version. Updating
`CS2 Metro\Properties\PublishConfiguration.xml` to
`0.1.0-alpha.2-candidate` fixed the publish.

## Alpha Validation Set Workflow - 2026-06-19

Use the batch wrapper once multiple cities or snapshots exist:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-set.ps1 `
  -IncludeLatest `
  -LatestCount 5 `
  -SkipZip
```

The script:

- scans recent `D:\CS2MetroDiagram\exports\metro-export-*.json` snapshots,
- optionally includes `D:\CS2MetroDiagram\metro-export.json`,
- calls `scripts\generate-alpha-validation-bundle.ps1` per input,
- writes `artifacts\alpha-validation\batch-<timestamp>.csv`,
- refreshes `artifacts\alpha-validation\index.md` and `index.csv`.

Use `-WhatIfOnly` first when checking which exports will be processed.

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
- Fully removed from the toolchain (renderer, CLI, tests) in 2026-07.

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

## Fast Alpha Validation Bundles - 2026-06-20

Batch validation can be expensive because a full bundle captures PNG screenshots
for every rendered layout. Use the fast SVG/diagnostics-only mode for daily
multi-city triage:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-set.ps1 -IncludeLatest -LatestCount 5 -SkipPng -SkipZip
```

Use the full screenshot mode only after choosing a city/case for manual visual
review:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson "D:\CS2MetroDiagram\metro-export.json" -CaseName review
```

## Schematic-map Route Grammar Stabilization - 2026-06-20

The Zhaoqing 1号线 west-side route exposed a general issue: independent
width/height scaling and conservative route-bend thresholds could leave a
visibly twisty schematic-map route even when a clean octilinear expression was
available.

Accepted renderer-only safeguards:

- `schematic-map` output-size scaling now uses a uniform scale plus centering so
  Compact/Standard/Poster/Ultra change size without changing route shape.
- schematic-map route grammar bends are enabled in the product defaults.
- long non-octilinear route-only spans can be split into 0/45/90-degree legs.
- ordinary non-anchor shallow kinks can be projected back onto the nearest
  octilinear corridor.
- interchange/high-degree anchors remain conservative to avoid damaging
  topology.

Latest validation candidate:

```text
artifacts\product-candidate\20260620-230040-zhaoqing-line1-final-octilinear
```

Audit result for that candidate:

```text
Route warnings: 5
Non-octilinear segments: 5
Schematic-map synthetic bends: 33
Sharp turn candidates: 0
Layout score: 82.27 / 100
```

There are no remaining 1号线 non-octilinear warnings in
`schematic-map-route-segments.csv`. This is a generic route-grammar fix, not a
city-specific override.

## Schematic-map Non-adjacent Route Overlap Separation - 2026-06-21

The Zhaoqing product candidate exposed a route-grammar issue where two
non-adjacent stations in the same schematic-map route could collapse onto the
same visual node after layout transforms. In the real case, 顶峰街站 and
星湖广场站 were geographically separate but ended up too close in the top 8号线
area.

Accepted renderer-only fix:

- After schematic-map linearization/octilinear/local-clearance/shallow-kink
  passes, scan final route chains for non-adjacent station pairs that are closer
  than the safe schematic spacing.
- Never separate adjacent station pairs, including pairs that are adjacent in
  another route chain.
- Push the pair apart using the current direction when available, otherwise the
  original geographic direction, quantized to schematic directions.
- Weight movement by station degree/interchange status so important anchors move
  less.
- Clamp to the render bounds only; this is a post-layout visual guard and does
  not mutate exported data.

Validation candidate:

```text
artifacts\product-candidate\20260621-225839-zhaoqing-route-overlap-separation-v2
```

Audit result:

```text
Route warnings: 5
Non-octilinear segments: 5
Schematic-map route-overlap stations: 4
Remaining dense station pairs: 2
Layout score: 83.36 / 100
```

The previous 顶峰街站 / 星湖广场站 dense-pair warning is gone. Exporter data,
JSON schema, `line.stops`, and `line.pathPoints` remain unchanged.

## Schematic-map Official-map Short Doglegs - 2026-06-22

Reference maps such as Guangzhou-style system maps favor long horizontal,
vertical, and 45-degree corridors. Small geographic offsets are usually absorbed
as compact route grammar bends instead of leaving a route segment slightly off
the octilinear grid.

Accepted renderer-only adjustment:

- `schematic-map` synthetic bends now also apply to compact non-octilinear route
  spans around the 100px range.
- The minimum synthetic-bend leg was reduced enough to allow small official-map
  doglegs while still avoiding zero-length or station-overlapping fragments.
- Added a regression fixture where a locked short non-octilinear segment must
  render as two octilinear legs.

Latest validation candidate:

```text
artifacts\product-candidate\20260622-144311-zhaoqing-official-map-short-doglegs
```

Audit result:

```text
Route warnings: 0
Non-octilinear segments: 0
Direction divergence warnings: 0
Source-direction checks skipped on dogleg routes: 109
Interior route crossings: 2
Schematic-map synthetic bends: 39
Layout score: 88.00 / 100
```

The audit now treats intentional `schematic-map` dogleg polylines as a separate
informational category instead of comparing each generated dogleg leg against
the original geographic station-to-station direction by index. The remaining
penalty is mainly from two interior route crossings. Product-map review should
still inspect `product-candidate.full.png`, `schematic-map-debug.full.png`, and
the CSV diagnostics together before accepting a candidate.

Latest comparison and regression artifacts:

```text
artifacts\product-candidate-comparison\20260622-155000
artifacts\schematic-regression\20260622-155035
```

The 20260622 fast schematic regression gate ran without PNG generation and
passed all 10 cases. The symlink update for `latest` can fail without admin
privileges; the script writes `latest.txt` as the fallback pointer.

## Product Map Cartographic Polish - 2026-06-23

Phase 5C.2 is a low-risk cartographic polish pass for `schematic-map`, not a
topology experiment. It should not revive schematic-v2 route-chain work,
schematic-lite patching, crossing conventions, exporter changes, or JSON schema
changes.

Before/after comparison artifacts:

```text
artifacts\product-candidate\phase-5c2-cartographic-polish\product-candidate-before-cartographic-polish.full.png
artifacts\product-candidate\phase-5c2-cartographic-polish\product-candidate-cartographic-polish.full.png
artifacts\product-candidate\phase-5c2-cartographic-polish\schematic-map-debug.full.png
artifacts\product-candidate\phase-5c2-cartographic-polish\schematic-map-audit.txt
```

Implementation notes:

- `schematic-map` product defaults now keep slightly stronger station and
  interchange marker sizes for the transit-map style.
- Station circles include terminal metadata and a terminal class when applicable.
- Important labels keep the compatible `station-label` class and use
  `data-label-important`, `data-label-terminal`, and `data-label-interchange`
  metadata for styling/debugging.
- The transit-map header has a stronger title, capsule stroke, and bottom
  separator.
- The bottom legend now separates the title/key area and includes ordinary
  station, terminal, transfer, and express/skip-stop marker symbols.

Verification:

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

## Product Map Corner And Parallel Polish - 2026-06-23

Latest Sheffield candidate:

```text
artifacts\product-candidate\20260623-174304-phase-5c2-corner-parallel-polish
```

Generate command:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 `
  -InputJson 'D:\CS2MetroDiagram\exports\metro-export-谢菲尔德-20260623-003129.json' `
  -CaseName 'phase-5c2-corner-parallel-polish' `
  -OutputRoot 'artifacts\product-candidate' `
  -Layout schematic-map `
  -Size ultra `
  -Style transit-map
```

Implementation notes:

- `schematic-map` synthetic bends are now context-aware. Segments with
  neighboring route context are allowed to stay as direct spans when they are
  already close to an octilinear direction, short enough to read naturally, or
  when a dogleg would backtrack against the incoming/outgoing route direction.
- Exact shared-platform parallel corridors now keep a dominant collapsed lane
  centered when that lane represents multiple same-number/same-color services,
  such as `7号线` plus `7号线支线`, while adjacent different-color lines are offset
  to the side.
- The Sheffield audit changed from 34 synthetic bends to 18 synthetic bends.
  This intentionally favors fewer unnecessary hard elbows over maximizing the
  numeric octilinear score. Do not accept or reject this class of change by
  score alone; inspect `product-candidate.full.png`,
  `schematic-map-debug.full.png`, and `schematic-map-parallel-corridors.csv`.

Important audit checks:

```text
schematic-map-audit.txt
schematic-map-parallel-corridors.csv
schematic-map-score.csv
```

The parallel-corridor CSV should show the same-number/same-color shared-service
lane with offset `0` on exact shared-platform segments, and adjacent
different-color lanes with a non-zero side offset.

## Schematic-map Visible-lane Anchor Polish - 2026-06-24

Latest Sheffield candidate:

```text
artifacts\product-candidate\20260624-115621-same-visible-lane-anchor-polish
```

Generate command:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 `
  -InputJson 'D:\CS2MetroDiagram\exports\metro-export-谢菲尔德-20260623-232831.json' `
  -CaseName 'same-visible-lane-anchor-polish' `
  -Size ultra `
  -Layout schematic-map `
  -Style transit-map
```

Implementation notes:

- `schematic-map` anchor decisions now use visible-lane groups, not only raw
  display-family keys.
- Same-number/same-color branch families such as `7号线` and `7号线支线` are
  one visible lane for anchor and local-clearance purposes, so shared-service
  corridor stations are allowed to participate in straightening and kink cleanup.
- Stations with multiple distinct visible lanes, or route degree >= 3, remain
  protected as true transfer/junction anchors.
- Local clearance ignores paths from the same visible lane, preventing a line
  family from pushing itself apart.
- This is renderer-only. Exporter logic, JSON schema, raw `line.stops`, and raw
  `line.pathPoints` are unchanged.

Audit snapshot for the candidate:

```text
Route warnings: 18
Parallel corridor elements: 6
Schematic-map synthetic bends: 14
Interior route crossings: 5
Stroke-width-consistency: 100
```

Manual override planning note: future Viewer editing should write a separate
render override file, for example station nudges, label nudges, route bend locks,
and lane ordering. Do not store those edits back into `metro-export.json`.

## Render Layout Override Sidecar - 2026-06-24

The first manual-adjustment persistence layer is implemented as a renderer-only
sidecar file. For an export:

```text
D:\CS2MetroDiagram\exports\metro-export-City-20260624-123456.json
```

the default sidecar path is:

```text
D:\CS2MetroDiagram\exports\metro-export-City-20260624-123456.layout-overrides.json
```

Example:

```json
{
  "version": 1,
  "stations": {
    "station_id": {
      "dx": 24,
      "dy": -12,
      "note": "Move marker and route anchor slightly."
    }
  },
  "labels": {
    "station_id": {
      "dx": 16,
      "dy": 8,
      "position": "manual",
      "note": "Move label only."
    }
  }
}
```

CLI usage:

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  D:\CS2MetroDiagram\exports\metro-export-City-20260624-123456.json output.svg `
  --layout schematic-map `
  --overrides D:\CS2MetroDiagram\exports\metro-export-City-20260624-123456.layout-overrides.json
```

Viewer behavior:

- `Open JSON` auto-loads the sibling sidecar when it exists.
- Loaded sidecars are reported in the Viewer status warnings.
- Station overrides move the station marker and route geometry for that render.
- Label overrides move or hide labels without moving stations.
- `Manual edit` mode injects a small preview script into the WebView2 preview.
  In `Stations` mode, dragging a station circle sends the SVG-space delta back
  to WPF through `window.chrome.webview.postMessage`; WPF updates the station
  sidecar entry and schedules a refresh. In `Labels` mode, dragging a station
  label updates the label sidecar entry only.
- Viewer controls now cover the basic manual-edit loop: select station/label by
  clicking it, drag, hide/show selected label, reset selected edit, clear all
  edits, and open/create the sidecar file.
- The sidecar is not part of `metro-export.json` and does not modify exporter
  data, JSON schema, raw station positions, stops, or path points.

Current limits:

- Station and label dragging are implemented.
- Label hide/show, selected reset, clear-all, and open-sidecar are implemented.
- There is no undo stack, multi-select, or route-bend/lane-order editor yet.
  Use `Reset Selected` or `Clear Edits` to revert manual edits.

Verification:

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

## Viewer WebView2 Preview - 2026-07-01

The Viewer preview was migrated from WPF `WebBrowser`/Internet Explorer to
Microsoft WebView2. Preview HTML is now loaded in-memory with `NavigateToString`
instead of a temporary `file://` page, removing the local active-content
security warning and avoiding the old COM `ObjectForScripting` bridge.

Manual edit messages now use `window.chrome.webview.postMessage`. This keeps the
same sidecar override behavior while giving a more modern preview surface for
dragging and future UI work. The package reference is
`Microsoft.Web.WebView2`; NuGet restore uses the repo `NuGet.Config` source for
nuget.org and the local package cache can stay under `E:\CS2\.tool-cache`.

Drag responsiveness is intentionally split into two layers:

- During pointer movement, the preview performs lightweight in-SVG updates for
  the selected station/label and nearby route endpoints. This avoids invoking
  the full renderer on every mousemove and keeps WebView2 responsive.
- On mouse release, WPF saves the sidecar override and schedules a short delayed
  renderer refresh. The full refresh is still authoritative for final route,
  label, and diagnostic output.

## Viewer Segment Manual Editing - 2026-07-01

The Viewer manual-edit layer now supports route segment selection and drag in
addition to station and label editing. Segment edits are intentionally stored as
station endpoint overrides in the existing layout sidecar:

- No exporter logic changes.
- No `metro-export.json` schema changes.
- No raw `line.stops`, `line.pathPoints`, or station coordinates are mutated.
- If the preview script cannot identify two nearby endpoint stations for the
  clicked SVG route segment, the drag is ignored rather than guessed.

`Align H` and `Align V` are cartographic repair tools:

- With a station selected, the selected station is aligned horizontally or
  vertically with its nearest connected stop-sequence neighbor.
- With a segment selected, the endpoint station anchors are aligned to a shared
  horizontal or vertical axis.

This is the short-term solution for local map cleanup where an automatically
generated schematic-map segment is almost horizontal/vertical but visually
awkward. Longer-term manual editing should continue to build on the same
sidecar layer, adding explicit bend/route-handle overrides only after the
station/segment workflow is stable.

## Schematic-anneal Experiment - 2026-07-06

`schematic-anneal` is a CLI-only experimental layout mode that replaces the
schematic-map local repair passes with one global quality cost (octilinearity,
edge-length uniformity, bends, crossings, clearance) minimized by
deterministic, grid-constrained simulated annealing. Key properties:

- Renderer-only; no exporter or JSON schema changes.
- Deterministic: fixed seed and schedule, identical input -> identical SVG.
- Emits a `Schematic-anneal audit` warning with cost/crossing movement.
- Every render now exposes objective layout metrics; the CLI writes them with
  `--emit-layout-score path.csv`.

Layout acceptance rule going forward: run
`scripts\compare-schematic-layouts.ps1` and judge corpus medians plus worst
case. Do not tune layout coefficients against a single map. First corpus run
(2026-07-06, 9 samples): schematic-map slightly ahead on simple-map medians;
schematic-anneal far ahead on worst case (bends 7 vs 15, crossings 0 vs 1,
spacing violations 0 vs 19, weighted cost 18.7 vs 216.6).

## Schematic-anneal Canvas Fit - 2026-07-06

The default schematic-anneal mode now adapts the output canvas and fills it:

- `AdaptCanvasHeightToNetwork` sets the canvas height (width fixed) so the
  drawing area matches the network's geographic bounding-box aspect ratio,
  clamped to [0.6, 1.5] x width. This means a `--size poster` anneal render is
  no longer a fixed 3200x2000 - the height varies with the city shape so the
  map is not letterboxed. Set a mode other than schematic-anneal if you need
  exact fixed dimensions.
- `FitPointsToBounds` uniformly scales + centers the final layout (angles and
  all layout metrics preserved) within a label-reserved drawing area.

Real-city fill went from ~60% to ~93% of the width. The clearance metric now
exempts same-line station/edge pairs, so out-and-back lines stay straight
(fixed the Sheffield line-1 kink at 谢菲尔德二中站).

## Schematic-anneal Parallel Corridors - 2026-07-06

Lines that share a station-to-station edge now render side by side in
schematic-anneal (`MetroSvgRenderer.AnnealParallel.cs`) instead of stacking.

- Shared edges are found by canonical rounded render-point-pair, so out-and-back
  lines and same-geometry shared families merge to one edge.
- Each line gets a symmetric signed lane offset along the edge normal, ordered by
  a STABLE global key (line number, then family key). This sidesteps the NP-hard
  line-ordering/crossing-minimisation problem, which is fine because shared runs
  are sparse and short (Sheffield: 5 of 81 edges). Long shared trunks that show
  extra crossings are the place to add barycentre/median lane ordering later.
- Vertices offset by the average of incident shared-edge offset vectors, so the
  fan-out ramp lands on the adjacent non-shared segment.
- Render-only: layout scores are unchanged. Synthetic doglegs are disabled for
  anneal so route polylines stay clean station-to-station segments.

Open refinements: crossing-minimising lane order for long trunks; drawing station
ticks along a parallel bundle; tuning lane spacing vs stroke width.
