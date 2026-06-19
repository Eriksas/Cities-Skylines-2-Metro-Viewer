# Project State

## Current Version

`v0.1.0-alpha.2-candidate`

This is alpha software. It is not a stable release.

## Current Status

The project is in alpha validation and schematic-map refinement.

Recommended alpha default:

```text
layout = geographic
UsePathPoints = true
service family merge = enabled
shared corridor = disabled
express stripe = disabled
```

`geographic` remains the safest default because it preserves exported route geometry most reliably.

Current schematic directions:

- `schematic-map` - product-facing official-map style experiment; current active polish target.
- `schematic-v2` - topology/diagnostic schematic base; still experimental.
- `schematic-lite` - retired from Viewer and kept only for CLI/script historical comparison.

The latest notable product candidate is:

```text
artifacts\product-candidate\20260619-171203-sheffield-knockout-fringe-fix
```

That candidate keeps the recent shared-platform fixes and clamps white knockout strokes inside the visible colored lane envelope so exact shared platforms do not show white fringes.

Phase 6A.1 adds a schematic-map regression gate so future renderer changes are
checked across real exports and synthetic regression samples before visual
acceptance.

Alpha.2 candidate release package has been regenerated and is ready for manual
release review:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

The package includes a freshly built CS2 mod artifact, the self-contained Viewer,
docs, samples, and regression samples.

Latest real-export regression gate:

```text
artifacts\schematic-regression\20260619-214528
```

Latest sample smoke regression gate:

```text
artifacts\schematic-regression\20260619-215440
```

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
- Viewer no longer exposes legacy `schematic-lite`.
- Alpha validation bundles and product-candidate bundles can be generated for repeatable review.

## Current Validation Workflow

Generate a full alpha validation bundle:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson "D:\CS2MetroDiagram\metro-export.json" -CaseName my-city
```

Generate a product candidate map:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 -InputJson "D:\CS2MetroDiagram\metro-export.json" -CaseName my-city
```

Compare recent product candidates:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-product-candidates.ps1 -LatestCount 4
```

Run the schematic regression gate:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1
```

Important generated artifacts:

- `artifacts\alpha-validation\index.md`
- `artifacts\alpha-validation\index.csv`
- `artifacts\product-candidate\...\product-candidate.full.png`
- `artifacts\product-candidate\...\schematic-map-debug.full.png`
- `artifacts\product-candidate\...\schematic-map-score.csv`
- `artifacts\product-candidate-comparison\...\comparison.html`
- `artifacts\product-candidate-comparison\...\comparison.full.png`
- `artifacts\schematic-regression\...\index.md`
- `artifacts\schematic-regression\...\regression-summary.csv`

## Current Priorities

Short term:

- Keep `geographic` stable as the alpha default.
- Continue polishing `schematic-map` only through evidence-backed candidate bundles.
- Run the schematic regression gate before accepting layout/rendering changes.
- Use debug overlays and scoring reports before layout changes.
- Preserve the current shared-platform behavior: same-number branch/shared-service lanes collapse when they share the exact same platform segment; distinct colored lanes remain visible and consistent width.
- Before public release, manually smoke-test the packaged Viewer and in-game mod export from the release folder/zip.

Medium term:

- Make `schematic-map` closer to an official metro diagram through topology-safe octilinear routing, label placement, crossing readability, and multi-city validation.

Long term:

- Reduce Viewer package size after alpha behavior stabilizes.
- Add image export, style presets, and optional manual overrides only after core rendering is trustworthy.

## Current Engineering Guardrails

- Do not modify `RealMetroJsonExporter` unless the task is explicitly exporter work.
- Do not change `metro-export.json` schema for renderer/viewer polish.
- Do not mutate raw `line.stops` or `line.pathPoints`.
- Keep `geographic` safe and stable.
- Add schematic experiments as opt-in behavior.
- Prefer validation bundles over one-off screenshots when reviewing new cities.
- Do not revive old `schematic-lite` patch work for new product behavior.

## Known Limitations

- Only metro/subway is supported.
- No PNG/PDF export as product features.
- No manual drag editor.
- No offline save parsing.
- Multi-city validation is still limited.
- City/station names may fall back when CS2 data is unavailable.
- `schematic-map` still needs polish for crossings, octilinear grammar, labels, small-network framing, and edge cases around parallel platforms.
- Shared platform corridors are render-only approximations based on exact shared schematic segments, not exporter-provided platform metadata.

## Documentation

Start from:

```text
docs\README.md
docs\PROJECT_STATE.md
docs\NEXT_SESSION_HANDOFF.md
docs\DEV_NOTES.md
docs\DECISION_LOG.md
```

Archived history:

```text
docs\archive\2026-06-18-doc-consolidation
docs\archive\historical
```

## Verification

Run these after code or script changes:

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```
