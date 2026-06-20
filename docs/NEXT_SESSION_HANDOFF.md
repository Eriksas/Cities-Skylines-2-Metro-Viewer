# Next Session Handoff

## Start Here

Read these first:

```text
docs\PROJECT_STATE.md
docs\DEV_NOTES.md
docs\DECISION_LOG.md
docs\ALPHA_TEST_PLAN.md
```

Use archived history only when you need phase detail:

```text
docs\archive\2026-06-18-doc-consolidation
docs\archive\historical
```

## Current Version

`v0.1.0-alpha.2-candidate`

## Current Direction

The project is focused on multi-city alpha validation and `schematic-map` polish.

Recommended alpha tester output:

```text
geographic + UsePathPoints + service family merge
```

Experimental layouts:

```text
schematic-map
schematic-v2
```

Retired from Viewer:

```text
schematic-lite
```

## Important Current Facts

- Latest real export:
  - `D:\CS2MetroDiagram\metro-export.json`
  - `D:\CS2MetroDiagram\metro-export-diagnostics.txt`
- Snapshot exports:
  - `D:\CS2MetroDiagram\exports\`
- Alpha validation index:
  - `artifacts\alpha-validation\index.md`
  - `artifacts\alpha-validation\index.csv`
- Product candidate comparison helper:
  - `scripts\compare-product-candidates.ps1`
- Schematic regression gate:
  - `scripts\generate-schematic-regression-gate.ps1`
- Latest notable shared-platform/fringe-fix candidate:
  - `artifacts\product-candidate\20260619-171203-sheffield-knockout-fringe-fix`
- Latest real-export regression gate:
  - `artifacts\schematic-regression\20260619-214528`
- Latest sample smoke regression gate:
  - `artifacts\schematic-regression\20260619-215440`
- Latest alpha.2 candidate release package:
  - `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate`
  - `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip`
- GitHub pre-release:
  - `v0.1.0-alpha.2-candidate`
- Paradox Mods:
  - ModId `146643`
  - Version `0.1.0-alpha.2-candidate`
  - Access level `Unlisted`

## Current Schematic-map Notes

- `schematic-map` is the current product-facing schematic direction.
- Non-station crossings are currently rendered as direct pass-through route intersections because exports do not include reliable over/under elevation.
- Exact shared platform corridors mask duplicate base strokes before drawing visible lanes.
- Same-number branch/shared-service families collapse into one visible lane when they share the exact platform segment and color.
- Knockout strokes are clamped inside the visible colored lane envelope to avoid white fringes around shared platforms.
- Synthetic route bends exist as an explicit experiment but are off by default because the first visual result was less natural.

## Current Priorities

Good next tasks:

- Generate alpha validation bundles for every new city export.
- Use `scripts\generate-alpha-validation-set.ps1 -IncludeLatest -LatestCount 5 -SkipPng -SkipZip` for a fast recent-snapshot triage pass and refreshed bundle index.
- Omit `-SkipPng` only for selected cases that need full screenshot bundles for manual review or feedback attachments.
- Generate and compare product candidates for new city exports.
- Run `scripts\generate-schematic-regression-gate.ps1` before accepting schematic-map changes.
- Use `schematic-map-debug.full.png`, `schematic-map-score.csv`, and comparison bundles before changing layout behavior.
- Continue small, evidence-backed `schematic-map` polish for crossings, labels, route badge placement, and compact framing.
- Keep docs concise after each accepted behavior change.

Avoid next:

- Do not modify exporter/schema for renderer polish.
- Do not revive `schematic-lite` patch work.
- Do not add PNG/PDF product export yet.
- Do not start a manual drag editor yet.
- Do not make `schematic-map` the default until more cities pass validation.

## Standard Verification

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

For product candidate review:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 -InputJson "D:\CS2MetroDiagram\metro-export.json" -CaseName review
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-product-candidates.ps1 -LatestCount 4
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1 -LatestExports 2
```
