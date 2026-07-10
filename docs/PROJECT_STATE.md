# Project State

## Current Version

Repository and companion Viewer: `v0.1.0-beta.3`

Current Paradox Mods code-mod release: `v0.1.0-beta.3` (ModId `146643`, Public).

The repository, companion Viewer, and Paradox Mods listing are aligned on the
first beta line.

This is beta software. It is not a stable release.

## Current Status

The project is in multi-city beta validation, `schematic-anneal` hardening,
and Viewer workflow refinement.

Current publication state:

- Repository/Viewer release line: `v0.1.0-beta.3`
- Paradox Mods published version: `0.1.0-beta.3`
- Paradox Mods access level: `Public`
- Paradox Mods ModId: `146643`

Beta.2 was published on 2026-07-10 as an emergency lifecycle/localization
hotfix. Offline build/tests and the CS2 Release post-process passed. Manual
in-game confirmation after the subscription update is still pending.

Default product mode: `schematic-anneal` (the Viewer opens on it). It won every
layout metric on both median and worst case across the current corpus (9 samples
+ 2 real cities), so it is the default schematic direction pending broader
multi-city validation.

`geographic` remains the most faithful render of exported route geometry and is
available for anyone who wants true geometry rather than a schematic
(`layout = geographic`, `UsePathPoints = true`, service family merge enabled).

Current schematic directions:

- `schematic-anneal` - DEFAULT. Global-optimization layout (single cost function + deterministic simulated annealing). Wins every layout metric on the corpus; same-line clearance exemption keeps out-and-back lines straight; output is recentered in the canvas.
- `schematic-map` - previous product-facing pass-stack schematic; retained for comparison until anneal has broader validation.
- `schematic-v2` - topology/diagnostic schematic base; still experimental.
- `schematic-lite` - removed from the toolchain (2026-07). History remains in git.

The latest notable product candidate is:

```text
artifacts\product-candidate\20260624-115621-same-visible-lane-anchor-polish
artifacts\product-candidate\20260623-174304-phase-5c2-corner-parallel-polish
artifacts\product-candidate\20260623-152745-phase-5c2-cartographic-polish-final
artifacts\product-candidate\phase-5c2-cartographic-polish
```

The latest same-visible-lane anchor polish is renderer-only. It treats
same-number/same-color branch or service families as one visible lane for
schematic-map anchor decisions, so shared-service stations can still be
straightened or cleared instead of being frozen as false transfer anchors.
True multi-visible-line transfers and high-degree junctions remain protected.

The latest manual-editing foundation is also renderer-only. When a JSON export
has a sibling sidecar named `*.layout-overrides.json`, the CLI can load it with
`--overrides` and the Viewer auto-loads it with `Open JSON`. The sidecar can
nudge station render positions and label positions without modifying
`metro-export.json`, raw `station.position`, `line.stops`, or `line.pathPoints`.
The Viewer now has `Manual edit` mode: users can drag station circles, drag
labels, hide/show a selected label, reset the selected edit, clear all edits,
open the sidecar file, select a route segment, drag its endpoint station anchors
together, and align the selected station or segment horizontally/vertically. It
is a lightweight map-polish editor, not a CS2 data editor.

The latest Phase 5C.2 corner/parallel polish pass is renderer-only. It makes
schematic-map synthetic route doglegs context-aware so short or already readable
segments are less likely to receive unnecessary hard elbows, and it keeps the
dominant same-number/same-color lane centered on exact shared-platform
corridors while adjacent different-color lines remain visible. On the current
Sheffield product audit, synthetic bends dropped from 34 to 18. The tradeoff is
that the audit now reports more slight non-octilinear direct segments, so visual
review must weigh natural route flow against strict 45-degree grammar.

The earlier Phase 5C.2 cartographic candidate keeps the recent shared-platform
and route-grammar fixes, then adds low-risk product cartography polish: a
stronger official-map header, clearer bottom legend sectioning,
station/terminal/transfer symbol legend entries, more explicit station
hierarchy metadata, and important station-label metadata. It is renderer-only;
exporter data, JSON schema, `line.stops`, and `line.pathPoints` remain
unchanged.

Phase 6A.1 adds a schematic-map regression gate so future renderer changes are
checked across real exports and synthetic regression samples before visual
acceptance.

The regression/product workflow now uses `schematic-anneal` as its candidate
layout by default. Alpha validation bundles still include `geographic`,
`schematic-map`, and `schematic-v2` comparison outputs so regressions remain
visible rather than silently replacing the evidence set.

Current post-alpha.6 engineering changes:

- the in-game options page has an `Auto / English / Simplified Chinese`
  language selector that affects this mod only;
- render option intent is preserved (`MapStyle=Standard` and disabled service
  family merge are no longer overwritten by product-layout defaults);
- the render geometry cache keys only geometry-affecting options, improving
  reuse during label and presentation changes;
- real export files are staged and committed per file, with the latest JSON
  published last so the Viewer does not observe a partially written document;
- shared PowerShell naming/runtime/diagnostics helpers were consolidated and CI
  now includes a Release Viewer publish smoke step.

Historical alpha.2 candidate release package:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

The package includes a freshly built CS2 mod artifact, the self-contained Viewer,
docs, samples, and regression samples. It is retained as historical release
evidence; the current Paradox Mods public version is `0.1.0-alpha.7`.

Latest real-export regression gate:

```text
artifacts\schematic-regression\20260622-155035
```

Latest external audit bundle:

```text
artifacts\external-audit\20260623-144955-external-audit-current
docs\EXTERNAL_AUDIT_SUMMARY.md
```

This bundle includes the current product-style map PNG/SVG, the official
reference image, debug overlays, machine-readable schematic-map diagnostics,
and a progress/engineering review summary for external reviewers.

Latest sample smoke regression gate:

```text
artifacts\schematic-regression\20260619-215440
```

## Current Capabilities

- CS2 mod can export real metro JSON.
- The mod settings page has an `Export Folder` group with an editable path and
  presets for `Documents\CS2MetroDiagram`, `Desktop\CS2MetroDiagram`, and
  `D:\CS2MetroDiagram`.
- The mod settings page can follow the game language or be fixed to English /
  Simplified Chinese without changing the global game locale.
- Real export writes latest files:
  - `<export folder>\metro-export.json`
  - `<export folder>\metro-export-diagnostics.txt`
- Real export also writes timestamped snapshots:
  - `<export folder>\exports\metro-export-{citySlug}-{yyyyMMdd-HHmmss}.json`
  - `<export folder>\exports\metro-export-diagnostics-{citySlug}-{yyyyMMdd-HHmmss}.txt`
- CLI can render SVG from sample or real JSON.
- Viewer can open latest exports from common folders or manual custom-folder/snapshot JSON.
- Viewer auto-loads a sibling `*.layout-overrides.json` sidecar when present.
- Viewer can use `Manual edit` mode to drag station circles, drag labels,
  hide/show selected labels, reset selected edits, clear all edits, and open the
  sidecar file.
- Viewer preview now uses WebView2 instead of the legacy WPF WebBrowser/IE
  control, avoiding local-file active-content security prompts and improving
  the preview/edit surface.
- CLI supports `--overrides <path>` for render-time station/label nudges.
- Viewer supports in-app SVG preview, export data inspection, Chinese/English UI, render settings, and save SVG.
- Legacy `schematic-lite` has been removed from the toolchain.
- Alpha validation bundles and product-candidate bundles can be generated for repeatable review.
- `schematic-map` uses render-time route grammar safeguards so map size changes
  should scale the same schematic content instead of changing route shape.

## Current Validation Workflow

Generate a full alpha validation bundle:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 -InputJson "D:\CS2MetroDiagram\metro-export.json" -CaseName my-city
```

Generate a batch of validation bundles from latest/recent exports:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-set.ps1 -IncludeLatest -LatestCount 5 -SkipPng -SkipZip
```

This is the preferred daily triage mode. It keeps SVGs, diagnostics, notes, and
the index, but skips full PNG screenshots. Omit `-SkipPng` for a full
screenshot bundle after choosing cases for manual review.

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
- Collect more real-city validation bundles before accepting broad layout changes.
- Continue polishing `schematic-map` only through evidence-backed candidate bundles.
- Treat Phase 5C.2 as cartographic polish only: header/footer/legend,
  station hierarchy, label hierarchy, and framing refinements. Do not use it
  as a reason to restart schematic-v2 route-chain or crossing-convention work.
- Run the schematic regression gate before accepting layout/rendering changes.
- Use debug overlays and scoring reports before layout changes.
- Preserve the current shared-platform behavior: same-number branch/shared-service lanes collapse when they share the exact same platform segment; distinct colored lanes remain visible and consistent width.
- Treat same-number/same-color branch families as the same visible lane when
  deciding whether a schematic-map station is an anchor. This avoids preserving
  artificial kinks around branch/shared-service stations that are not true
  transfer nodes.

Medium term:

- Build a multi-city regression set from alpha tester exports.
- Make `schematic-map` closer to an official metro diagram through topology-safe octilinear routing, label placement, crossing readability, and validation across that regression set.

Long term:

- Reduce Viewer package size after alpha behavior stabilizes.
- Add image export, style presets, and optional manual overrides only after core rendering is trustworthy.
- Manual overrides should be added as a separate render-time override file or
  Viewer layer, not by mutating `metro-export.json`, raw station positions, or
  raw route geometry.

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
- Manual map edits exist as Viewer/CLI render-time sidecar overrides, not as
  exporter data or CS2 city-data edits.
- No offline save parsing.
- Multi-city validation is still limited.
- City/station names may fall back when CS2 data is unavailable.
- `schematic-map` still needs polish for crossings, octilinear grammar, labels, small-network framing, and edge cases around parallel platforms.
- Intentional schematic-map doglegs are counted as audit notes, not direction
  divergence warnings, because their route points no longer map one-to-one to
  exported stop positions.
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
