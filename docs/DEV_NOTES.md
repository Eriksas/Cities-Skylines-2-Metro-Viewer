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

Use this for visual experiments around `schematic-map`.

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
