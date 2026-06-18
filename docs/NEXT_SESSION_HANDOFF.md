# Next Session Handoff

## Start Here

Read these files first:

```text
docs\PROJECT_STATE.md
docs\DEV_NOTES.md
docs\DECISION_LOG.md
docs\ALPHA_TEST_PLAN.md
```

For full pre-cleanup history, use:

```text
docs\archive\2026-06-18-doc-consolidation
docs\archive\historical
```

## Current Version

`v0.1.0-alpha.2-candidate`

## Current Direction

The project is now focused on alpha validation and schematic-map polish.

Recommended default for alpha testers:

```text
geographic + UsePathPoints + service family merge
```

Experimental directions:

```text
schematic-v2
schematic-map
```

Retired from Viewer:

```text
schematic-lite
```

`schematic-lite` still exists in CLI/scripts only for historical comparison.

## Important Current Facts

- Real export path:
  - `D:\CS2MetroDiagram\metro-export.json`
  - `D:\CS2MetroDiagram\metro-export-diagnostics.txt`
- Snapshot exports:
  - `D:\CS2MetroDiagram\exports\`
- Current alpha validation index:
  - `artifacts\alpha-validation\index.md`
  - `artifacts\alpha-validation\index.csv`
- Recent Sheffield review bundle:
  - `artifacts\alpha-validation\20260618-122028-sheffield-new-export-review`
- Recent product candidate:
  - `artifacts\product-candidate\20260618-121615-sheffield-new-export-review`
- Recent shared-platform width candidate:
  - `artifacts\product-candidate\20260618-163119-sheffield-shared-platform-widths`

## What Was Recently Added

- Viewer in-app SVG preview and export data inspection.
- Viewer no longer exposes `schematic-lite`.
- `schematic-map` layout mode for product-facing official-map style experiments.
- Product candidate and alpha validation scripts now generate `schematic-map` outputs.
- Exact shared platform corridors now mask duplicate base route strokes before drawing parallel overlays, which avoids uneven line thickness on close parallel station tracks.
- Shared platform overlays are assigned by `VisibleLaneResolver`. Same-number same-color branches such as `7号线` / `7号线支线` collapse into one visible lane on exact shared track segments.
- Alpha validation bundle summarizer:

```text
scripts\summarize-alpha-validation-bundles.ps1
```

This writes:

```text
artifacts\alpha-validation\index.md
artifacts\alpha-validation\index.csv
```

## Current Priorities

1. Use alpha validation bundles for every new real city export.
2. Keep improving `schematic-map` through small, evidence-backed changes.
3. Keep `geographic` stable.
4. Do not revive old `schematic-lite` patch work.
5. Do not change exporter/schema for renderer-only polish.
6. Defer Viewer package-size reduction until alpha.2 validation and packaging
   are stable.

## Good Next Tasks

- Generate an alpha validation bundle for each new city export.
- Compare `geographic`, `schematic-v2`, and `schematic-map` PNGs.
- Record recurring schematic-map issues in the bundle notes.
- Improve schematic-map only when the same issue appears across real cases.
- Keep docs short and archive long historical notes.
- If packaging comes up, keep self-contained Viewer as the alpha path and treat
  smaller package options as a later investigation.

## Avoid Next

- Do not start Phase 6 manual editor yet.
- Do not add PNG/PDF export unless explicitly requested.
- Do not change `metro-export.json` schema.
- Do not change `RealMetroJsonExporter` for layout problems.
- Do not make `schematic-map` the default until more cities pass validation.

## Standard Verification

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```
