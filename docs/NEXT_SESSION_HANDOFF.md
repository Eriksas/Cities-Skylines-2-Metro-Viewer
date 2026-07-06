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

Removed from the toolchain (2026-07):

```text
schematic-lite
```

## Important Current Facts

- Latest real export:
  - `<export folder>\metro-export.json`
  - `<export folder>\metro-export-diagnostics.txt`
- Snapshot exports:
  - `<export folder>\exports\`
- Mod export-folder presets:
  - `Documents\CS2MetroDiagram`
  - `Desktop\CS2MetroDiagram`
  - `D:\CS2MetroDiagram`
- Alpha validation index:
  - `artifacts\alpha-validation\index.md`
  - `artifacts\alpha-validation\index.csv`
- Product candidate comparison helper:
  - `scripts\compare-product-candidates.ps1`
- Schematic regression gate:
  - `scripts\generate-schematic-regression-gate.ps1`
- Latest notable shared-platform/fringe-fix candidate:
  - `artifacts\product-candidate\20260619-171203-sheffield-knockout-fringe-fix`
- Latest notable schematic-map route-overlap candidate:
  - `artifacts\product-candidate\20260621-225839-zhaoqing-route-overlap-separation-v2`
- Latest notable official-map route-grammar candidate:
  - `artifacts\product-candidate\20260622-144311-zhaoqing-official-map-short-doglegs`
- Latest real-export regression gate:
  - `artifacts\schematic-regression\20260622-155035`
- Latest external audit bundle:
  - `artifacts\external-audit\20260623-144955-external-audit-current`
  - `docs\EXTERNAL_AUDIT_SUMMARY.md`
- Latest product cartographic polish candidate:
  - `artifacts\product-candidate\20260624-115621-same-visible-lane-anchor-polish`
  - `artifacts\product-candidate\20260623-174304-phase-5c2-corner-parallel-polish`
  - `artifacts\product-candidate\20260623-152745-phase-5c2-cartographic-polish-final`
  - `artifacts\product-candidate\phase-5c2-cartographic-polish`
- Render-time manual adjustment sidecar:
  - default naming: `metro-export-*.layout-overrides.json`
  - CLI flag: `--overrides <path>`
  - Viewer: auto-loads sibling sidecar when opening JSON
  - Viewer: `Manual edit` mode can drag station circles, drag labels,
    drag route-segment endpoint station anchors together, align the selected
    station or segment horizontally/vertically, hide/show selected labels, reset
    selected edits, clear all edits, and open the sidecar file
- Latest product-candidate comparison:
  - `artifacts\product-candidate-comparison\20260622-155000`
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
- Earlier route-only synthetic bends were experimental and off by default; that
  older caution is superseded for the current product-facing `schematic-map` by
  the narrower route-grammar safeguards below.
- For current product-facing `schematic-map`, route grammar safeguards are now
  enabled by default: uniform output-size scaling, route-only octilinear bends
  for long non-octilinear spans, and conservative shallow-kink straightening for
  ordinary non-anchor stations. This fixed the Zhaoqing 1号线 west-side route
  without exporter/schema changes.
- `schematic-map` also has a post-layout guard for same-route non-adjacent
  station collapses. It fixed the Zhaoqing top-area 顶峰街站 / 星湖广场站 overlap
  warning in the latest product candidate while keeping adjacent/shared-platform
  pairs intact.
- `schematic-map` now allows compact route-only doglegs for shorter
  non-octilinear spans. The audit now treats those intentional doglegs as
  informational notes instead of source-direction warnings, so the latest
  Zhaoqing candidate reports zero route warnings, zero non-octilinear route
  segments, and an overall score of 88.00 / 100. Remaining penalties are mainly
  interior route crossings that need visual review.
- Phase 5C.2 is a cartographic polish pass for the product-style map, not a new
  topology phase. It strengthens the transit-map header, bottom legend
  sectioning, station/terminal/transfer symbols, station hierarchy metadata,
  and important label metadata. Exporter data, JSON schema, geographic output,
  `line.stops`, and `line.pathPoints` are unchanged.
- The latest Phase 5C.2 corner/parallel polish pass makes synthetic route
  doglegs context-aware and keeps dominant same-number/same-color
  shared-platform lanes centered. The Sheffield audit reduced synthetic bends
  from 34 to 18, but now reports more slight non-octilinear direct segments.
  Treat this as a visual tradeoff, not a pure score regression.
- The latest visible-lane anchor polish extends this rule beyond exact shared
  platform overlays: same-number/same-color branch/service families are one
  visible lane for schematic-map station-anchor decisions and local clearance.
  This lets shared-service corridor stations be straightened instead of frozen
  as false transfer anchors. Distinct visible lanes and high-degree junctions
  remain protected.

## Current Priorities

Good next tasks:

- Generate alpha validation bundles for every new city export.
- Use `scripts\generate-alpha-validation-set.ps1 -IncludeLatest -LatestCount 5 -SkipPng -SkipZip` for a fast recent-snapshot triage pass and refreshed bundle index.
- Omit `-SkipPng` only for selected cases that need full screenshot bundles for manual review or feedback attachments.
- Generate and compare product candidates for new city exports.
- Run `scripts\generate-schematic-regression-gate.ps1` before accepting schematic-map changes.
- Use `schematic-map-debug.full.png`, `schematic-map-score.csv`, and comparison bundles before changing layout behavior.
- When a route has `data-schematic-map-synthetic-bends`, do not compare its
  generated dogleg points to raw stop directions by index; use the audit note
  and PNG/SVG review.
- Continue small, evidence-backed `schematic-map` polish for crossings, labels, route badge placement, and compact framing.
- For exact shared-platform segments, preserve the current visible-lane rule:
  same-number/same-color branch lanes stay centered when collapsed; adjacent
  different-color lines offset beside them.
- Prefer product-map polish that improves hierarchy and framing first; do not
  restart schematic-v2 route-chain or old schematic-lite overlap work unless
  a future task explicitly asks for it.
- Watch for regressions where a route that should be straight becomes a shallow
  zigzag; use the product-candidate audit and debug overlay rather than adding
  city-specific overrides.
- When a user asks for manual adjustment, use the sidecar render override layer.
  Station dragging, label dragging, label hide/show, selected reset, clear-all,
  and open-sidecar are implemented in the Viewer. Future undo/multi-select,
  route-bend locks, and lane ordering should use the same sidecar instead of
  writing changes into `metro-export.json`.
- The Viewer preview has been migrated to WebView2. Do not revive WPF
  `WebBrowser`/IE preview or `ObjectForScripting`; manual edit messages should
  keep using WebView2 `postMessage`.
- Watch for non-adjacent same-route stations collapsing into one route node;
  the audit warning `schematic-map route-overlap stations` indicates the guard
  ran, and remaining dense-pair details show unresolved cases.
- Keep docs concise after each accepted behavior change.

Avoid next:

- Do not modify exporter/schema for renderer polish.
- Do not revive `schematic-lite` patch work.
- Do not add PNG/PDF product export yet.
- Do not turn manual edits into exporter/schema data. Keep them as render-time
  sidecar overrides.
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
