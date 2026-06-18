# Project State

## Current Version

`v0.1.0-alpha.2-candidate`

This is alpha software. It is not a stable release.

## Current Status

The project is in an alpha validation and schematic-map refinement stage.

The safe default output remains:

```text
layout = geographic
UsePathPoints = true
service family merge = enabled
shared corridor = disabled
express stripe = disabled
```

`geographic` is still the recommended alpha baseline because it preserves the real exported route geometry most reliably.

`schematic-v2` remains an experimental topology/diagnostic schematic layout.

`schematic-map` is the newer product-facing schematic direction. It builds on schematic-v2 ideas but aims for a cleaner official metro map look: octilinear lines, product-style frame, route badges, compact key, station readability, and more stable preview framing. It is still experimental and should be judged through validation bundles, not promoted as the default until more cities pass review.

Recent schematic-map crossing review rejected both bridge blobs and underpass-gap markers as too visually noisy without real over/under data. Non-station crossings are now rendered as direct pass-through route intersections, with a warning retained for diagnostics. Current JSON exports do not contain real track elevation or over/under order.

Recent shared-platform review fixed a schematic-map/schematic-v2 rendering issue where exact shared platform segments could look uneven because normal route strokes and parallel corridor overlays were both visible. Exact shared platform corridors now mask duplicate base strokes before drawing consistent-width parallel overlays, so close parallel tracks such as `5号线` / `7号线` near Terminal City Bank should stay visually balanced. Same-number branch families such as `7号线` / `7号线支线` collapse into one visible lane when they share the same exact platform segment. This logic now lives in `VisibleLaneResolver`.

## Current Capabilities

- CS2 mod can export real metro JSON.
- Real export writes latest files:
  - `D:\CS2MetroDiagram\metro-export.json`
  - `D:\CS2MetroDiagram\metro-export-diagnostics.txt`
- Real export also writes timestamped snapshots:
  - `D:\CS2MetroDiagram\exports\metro-export-{citySlug}-{yyyyMMdd-HHmmss}.json`
  - `D:\CS2MetroDiagram\exports\metro-export-diagnostics-{citySlug}-{yyyyMMdd-HHmmss}.txt`
- CLI can render SVG from sample or real JSON.
- Viewer can open latest export or manual snapshot JSON.
- Viewer supports in-app SVG preview, export data inspection, Chinese/English UI, render settings, and save SVG.
- Viewer no longer exposes legacy `schematic-lite`; that mode remains CLI-only for historical comparison.
- Alpha validation bundles can be generated and indexed.

## Current Validation Workflow

Generate a full alpha validation bundle:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName primary-city
```

Refresh the validation bundle index:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-alpha-validation-bundles.ps1
```

Current bundle index:

```text
artifacts\alpha-validation\index.md
artifacts\alpha-validation\index.csv
```

## Roadmap Focus

Current short-term focus:

- Stabilize the alpha.2 candidate around repeatable validation bundles.
- Keep the current Viewer preview/open/save workflow reliable.
- Use `schematic-map` as the experimental product-facing map direction, but do
  not promote it as the default until more real cities pass review.
- Fix only clear schematic-map regressions and recurring multi-city issues.

Medium-term focus:

- Make `schematic-map` feel more like an official metro diagram through
  topology-safe octilinear routing, stable visible lane handling, better label
  placement, and multi-city regression checks.

Long-term focus:

- Reduce Viewer package size after alpha behavior stabilizes.
- Add image export, style presets, and optional manual overrides only after the
  core render behavior is trustworthy.

Recent validation examples:

```text
artifacts\alpha-validation\20260618-122028-sheffield-new-export-review
artifacts\product-candidate\20260618-121615-sheffield-new-export-review
```

## Current Engineering Guardrails

- Do not modify `RealMetroJsonExporter` unless the task is explicitly exporter work.
- Do not change `metro-export.json` schema for renderer/viewer polish.
- Do not mutate raw `line.stops` or `line.pathPoints`.
- Keep `geographic` safe and stable.
- Add schematic experiments as opt-in layout behavior.
- Prefer validation bundles over one-off screenshots when reviewing new cities.

## Known Limitations

- Only metro/subway is supported.
- No PNG/PDF export as product features.
- No manual drag editor.
- No offline save parsing.
- Schematic layouts are still experimental.
- `schematic-map` can still need polish for crossings, label density, and small-network framing.
- Shared platform corridors are render-only approximations; they use exact shared schematic segments, not exporter-provided platform metadata.
- Same-number branch/shared-service families are still separate data families, but they are not split into separate visible lanes when they share the same exact track segment and color.
- Multi-city validation is still limited.
- City/station names may fall back when CS2 data is unavailable.

## Documentation Cleanup

On 2026-06-18 the docs were consolidated so future compressed sessions can recover state quickly.

Full pre-cleanup snapshots:

```text
docs\archive\2026-06-18-doc-consolidation
```

Older phase-specific planning notes:

```text
docs\archive\historical
```

Start new sessions from:

```text
docs\README.md
docs\PROJECT_STATE.md
docs\NEXT_SESSION_HANDOFF.md
docs\DEV_NOTES.md
docs\DECISION_LOG.md
```

## Verification

Run these after code or script changes:

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```
