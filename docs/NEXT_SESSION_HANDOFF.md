# Next Session Handoff

## Viewer v0.1.1 Release

Viewer v0.1.1 is the current desktop release candidate. It adds a top-level
Map elements workflow, project-owned transport symbols, editable information
boxes, and Ctrl-click/marquee station-node multi-selection. Manual edits remain
in sibling layout sidecars and never modify exported JSON.

The in-game Mod is `0.1.0` on PDX ModId `146643`. Viewer and Mod releases remain
independent; do not create a PDX binary for a Viewer-only release. Hidden
passenger-intercity development remains outside player-facing controls until
separately approved.

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

## Schematic Layout Study (2026-07-28)

Measured why the geographic view still reads better than the schematic one, and
prototyped an alternative. Read before changing anything in
`MetroSvgRenderer.SchematicAnneal.cs` or `MetroSvgRenderer.LayoutMetrics.cs`:

```text
docs\SCHEMATIC_LAYOUT_STUDY.md
```

Headline: the shipped schematic produces slightly MORE direction changes than the
geographic map, while paying 23 degrees of bearing distortion and reversing 6.4%
of station pairs — it does not schematize. It is genuinely strong at octilinearity
(91.5%) and station separation (0.68%). A line-first prototype cut direction
changes by 29% at the same octilinearity and spacing, but has no separation
constraint. The two are complementary; the next algorithm is their combination,
not a replacement of one by the other. Parameter tuning is exhausted — the
grid-size response is dominated by search noise.

Also recorded there: the multi-start annealer fix was reverted as collateral of
the tram/intercity revert. It was independent of that feature, corpus-validated
(mean layout cost 68.89 to 63.64 over 23 networks, zero regressions), and is
currently absent from HEAD.

## Current Version

Repository and Viewer: `v0.1.1`

Paradox Mods current release: `v0.1.0` (Public, existing ModId `146643`)

Viewer and Mod now use independent version sources. Desktop packaging reads
`MetroDiagramViewerInformationalVersion`; `CS2 Metro` and
`MetroDiagram.Engine` explicitly read the Mod version properties. Do not bump
or republish the PDX Mod for a Viewer-only release.

The PDX page metadata update for ModId `146643` completed successfully without
creating a new Mod version. Viewer v0.1.0 validation is 0-warning build, 167
tests, CS2 post-process, self-contained publish, and startup smoke.

## Beta.6 Publication Note

The 2026-07-16 single-line framing/vector-zoom fix is code-side complete and
staged by the CS2 Release build. Test this with the reported one-line city:

1. Open the in-game schematic and press Fit. The complete line and all stations
   must stay inside the white sheet.
2. Zoom to roughly 200-220%. Labels and route strokes should remain crisp.
3. Drag while zoomed, then press Fit. The map should pan smoothly and reset to
   the centered full sheet.

Offline evidence is under
`artifacts\ingame-schematic-audit\single-line-framing-fix`. On 2026-07-16 the
owner authorized publishing Beta.6 after the universal framing matrix, real-city
comparison, build, tests, and CS2 post-process passed. The Coherent-runtime
crispness check remains requested as post-publication feedback rather than a
release blocker.

The owner accepted `phase7-rc1` on 2026-07-13 and authorized the coordinated
Beta.4 GitHub/PDX release. Both publication endpoints completed successfully.

Beta.2 was broken: it registered locale sources before Options UI, but
`AddSource()` eagerly reads locale IDs that depend on Options registration.
Beta.3 restores the official order and isolates localization failure from core
mod startup. In-game loading is confirmed restored. Current logs still show
locale-source registration warnings and raw locale keys, so localization itself
is not yet fixed and must remain non-blocking.

## Current Direction

The tram/intercity prototype is parked. It was archived on local branch
`archive/tram-intercity-experiment-20260726` (`eab572b`) and reverted from
`master` after its schematic output failed visual review. Continue from the
stable metro-only behavior; do not reintroduce multimodal export or rendering
as a small follow-up patch.

The approved passenger-rail direction is
`docs\INTERCITY_GEOGRAPHIC_PLAN.md`: a fresh, geographic-only, default-off track
with real path geometry, cargo exclusion, outside-tail clipping, and one shared
desktop/in-game geometry pipeline.

IG2.0 through IG2.2 are code-side complete on the local feature branch.
Passenger collection, CurveElement-first path extraction, shared geographic
clipping/projection, desktop/portable rendering, clean clipped outside
endpoints, and three-city audits are present. The approved visual grammar is a dark continuous
rail bed with explicit equal-length white inserts. A shared arc-length sampler
replaced unstable SVG dashed strokes on 2026-07-29; both route surfaces and
their legends now use the same tokens. Mixed-mode legends are separated into
adjacent metro and intercity groups on desktop, transit-map, and portable
in-game output; the synthetic suite and three-city audit enforce that grouping.

Next: owner-only CS2/Coherent validation. Build from the feature branch and
deliberately enable the hidden development gate, then compare passenger and
cargo lines and inspect curves at several zoom levels. Do not expose
Viewer/CLI/game controls, do not render train lines in schematic layouts, do
not copy/deploy automatically, and do not publish to PDX before that validation.

The active long-line track is Phase 7, documented in
`docs\INGAME_PREVIEW_PLAN.md`: build a polished game-native metro preview while
preserving the exporter, Viewer, CLI, and JSON schema. Beta.8 is the current
published Phase 7 package. It retains Beta.6 universal preview framing and
vector `viewBox` navigation, then adds in-game label decluttering, localized
sheet text, cursor-anchored zoom, compact zoom controls, deterministic
small-network schematic restarts, and bracketed default-station-name handling.

Phase 7A passed owner in-game validation on 2026-07-13 and is closed. Phase 7B
through 7E are code-side complete on branch `feature/ingame-preview`:

- `MetroNetworkSnapshotService` captures the existing real-export data into an
  immutable `MetroDiagram.Engine` snapshot and caches the latest capture;
- `RealMetroJsonExporter` writes the same schema from that shared snapshot;
- portable geographic and deterministic schematic-anneal SVG rendering target
  `netstandard2.0` and are included in the CS2 package;
- a bounded render cache and game-specific profiles are ready for the next UI
  controller and now drive the accepted panel;
- empty snapshots, JSON round-trip, route semantics, XML validity, determinism,
  and a 200-station budget are covered by tests.
- the panel displays real inline SVG and supports refresh, layout switching,
  fit/zoom/pan, persisted label controls, JSON export, and SVG latest/snapshot
  save;
- no-city, no-metro, loading, rendering, saving, and error states are explicit.

The owner confirmed after the 2026-07-13 sizing hotfix that the real current-city
map is visible. The blank-canvas blocker is closed. Geographic is now the
in-game default because it was clearer than the portable schematic at the time
(superseded — see the 2026-07-17 player-perspective review below); the desktop
Viewer default remains `schematic-anneal` and schematic remains selectable in
game.

Phase 7F is owner-reviewed and closed for continued development. Inline SVG removes renderer-level font
overrides and inherits CS2's locale-aware `--fontFamily`, while standalone SVG
keeps a Noto CJK fallback stack. The panel also receives the mod's explicit
English/Simplified Chinese override and has a regrouped layout/command toolbar.
Phase 7G and the earlier Beta.4 release are closed. The later portable-renderer
hardening, mirrored-route normalization, collision-aware labels, stable preview
sheet, and vector zoom are published in Beta.6. The earlier "keep geographic as
the in-game default" guidance is superseded by the 2026-07-17 player-perspective
review below: after the owner validates commit `57b987c` in game, flipping the
default to schematic is the recommended (owner-approved-first) next step.

Phase 7A development output is staged by the CS2 toolchain at:

```text
E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods\CS2 Metro
```

Expected files include `CS2 Metro.dll`, `CS2 Metro.mjs`, and
`MetroDiagram.Engine.dll`. The published Beta.7 package was built through
independent E-drive staging at
`artifacts\pdx-staging\beta7-20260717-224610` because the running game locked
the normal local Mods staging DLL. The source and published-staging MJS hashes
match.

Recommended beta tester output:

```text
schematic-anneal
```

Reference and comparison layouts:

```text
geographic (faithful geometry fallback)
schematic-map
```

Removed from the toolchain (2026-07 / 2026-08):

```text
schematic-lite
schematic-v2   (mode only; its layout engine still draws schematic-map)
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
- Mod options language:
  - `Auto` follows the game locale
  - `English` and `简体中文` override this mod's options text only
- Product/regression scripts default to `schematic-anneal`; alpha bundles also
  retain geographic and schematic-map evidence.
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
- Historical alpha.2 candidate release package:
  - `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate`
  - `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip`
- Repository/Viewer release line:
  - `v0.1.1`
- Paradox Mods:
  - ModId `146643`
  - Version `0.1.0`
  - Access level `Public`
  - Publish mode: update the existing listing with `PublishNewVersion`.

## Schematic-map Comparison Notes

- `schematic-map` is the previous product-facing pass-stack direction. Keep it
  for comparison and targeted diagnostics; new default workflow work belongs to
  `schematic-anneal` unless a shared renderer primitive is being fixed.
- Non-station crossings are currently rendered as direct pass-through route intersections because exports do not include reliable over/under elevation.
- Exact shared platform corridors mask duplicate base strokes before drawing visible lanes.
- Same-number branch/shared-service families collapse into one visible lane when they share the exact platform segment and color.
- Knockout strokes are clamped inside the visible colored lane envelope to avoid white fringes around shared platforms.
- Earlier route-only synthetic bends were experimental and off by default; that
  older caution is superseded inside the retained `schematic-map` comparison by
  the narrower route-grammar safeguards below.
- In `schematic-map`, route grammar safeguards are
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
  restart schematic-map route-chain or old schematic-lite overlap work unless
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

## 2026-07-17 Player-perspective Preview Review (for Codex / next session)

A full player-view audit of the in-game preview was done offline (simulated
1080p panel: viewport ~1768x730, fit renders the 1800x1100 sheet at ~66%,
zoomed reading views verified). Evidence renders live in
artifacts\ingame-schematic-audit\20260717-player-view-review; the label-declutter engine change it builds on is commit
`57b987c` (unreleased, pending owner in-game validation).

What already works well from a player's seat: the schematic at ~2x zoom is
crisp, well-separated, and genuinely poster-like; vector zoom (beta.6) holds
up; the panel chrome (city stats, refresh/export/save, bilingual UI) is
complete and predictable.

Findings, ordered by player pain, with proposed ownership:

1. **The default first impression is the weakest view we ship.** The panel
   defaults to geographic, whose dense center fuses labels into unreadable
   strings on real cities (Sheffield: `生活中心`+`伊丽莎白消防局站`,
   `帝国大厦站`+`双子塔广场站`+`绿地广场站`). The far better schematic sits
   behind a second click, and the on-screen hint actively tells players the
   schematic "is still being refined; use the desktop Viewer" — that copy
   predates beta.5/beta.6 and the 57b987c label pass, and now undersells the
   product. The handoff line "Geographic is now the in-game default because
   it is currently clearer than the portable schematic" no longer holds.
   Proposal (frontend/mod-UI, Codex domain): after the owner validates
   57b987c in game, flip the in-game default to schematic (one-time settings
   migration, same pattern as `InGamePreviewGeographicDefaultApplied`) and
   soften/remove the stale `schematicViewerHint` copy. Owner decides; do not
   flip silently.
2. **Wheel zoom is not cursor-anchored** (`CS2 Metro.mjs` `zoom()` scales
   around the sheet center). Players zoom toward a district and land
   somewhere else, then pan back. With the viewBox math already in place this
   is a small change: adjust `pan` so the point under the cursor stays fixed
   while scaling. (Frontend, Codex domain.)
3. **Hardcoded English strings inside the SVG sheet**: the title is always
   "`{city} Metro Diagram`" and the legend header is always "Lines"
   (`PortableMetroSvgRenderer.GetTitle` / legend emitter), regardless of the
   mod interface language. A fully Chinese network renders with an English
   title. Proposal (engine + small mod plumbing, Claude domain): optional
   `PortableRenderOptions` strings for the title suffix and legend header,
   passed from the mod's effective interface language; defaults unchanged so
   desktop/CLI output stays byte-identical.
4. **Fit view readability/utilization** (informational, no action proposed
   yet): at 1080p the fit view shows station labels at ~7px — players must
   zoom before reading anything, and the 1.64:1 sheet in a ~2.4:1 viewport
   leaves large empty side bands. Fixing this properly means either a wider
   canvas profile or an initial fit-to-width, both of which touch the
   1800x1100 frontend contract (the exact contract that broke beta.5). Treat
   as a deliberate joint design decision later, not a quick fix.
5. Minor cosmetics: the `−`/`+` zoom buttons inherit `minWidth: 76rem` and
   read as oversized for single glyphs; geographic mode will always fuse some
   protected labels in dense centers (physical density, accepted limitation).

Status 2026-07-17: items 2, 3, and 5 are implemented and committed (cursor-
anchored wheel zoom with a Node-verified pure function, localized sheet
title/legend following the mod interface language with byte-identical
defaults, compact zoom buttons). Item 1 (schematic default + hint copy) is
deferred by the owner until the preview is further polished — do not flip the
default yet. Item 4 remains a joint design decision. The next owner in-game
pass validates the 57b987c label declutter plus these three changes together.

## In-game Preview Follow-up

The post-Beta.4 portable schematic audit is at:

```text
artifacts\ingame-schematic-audit\verification
```

Before publication, open the staged mod in CS2 and check schematic route
continuity, the 7/7-branch area, dense central labels, generic/crowded label
toggles, and the unchanged geographic default. The source and staged Engine DLL
hashes already match; only owner-visible game validation remains.

## Phase 7G Handoff

Runtime hardening now publishes `captureMs`, `renderMs`, `rendererMs`,
`renderCacheHit`, `renderCacheEntries`, `openCount`, and `coalescedRequests`.
Game logs use `[CS2MetroPreview][Lifecycle|Capture|Render|Export|Save|Settings]`
categories. The render cache is bounded to four LRU entries, and closing the
panel discards pending visual work but never an explicit JSON export.

Next priority is owner stress testing with repeated open/close, rapid setting
changes, refresh/export overlap, and the largest available city. Treat
geographic as the reliable default and do not add new visual features before
that evidence is reviewed.

Owner game validation completed on 2026-07-13 with no blocking issues.
**Phase 7G is closed and passed.** The next work should enter Phase 7H release
candidate preparation: freeze behavior, review the accumulated Phase 7 diff,
run the full release checklist, package a candidate, and obtain explicit owner
approval before GitHub/PDX publication.

Use `scripts\package-phase7-release-candidate.ps1` for the private Phase 7H
bundle. It deliberately writes to `artifacts\release-candidates`, includes the
E-drive staged mod and self-contained Viewer, verifies source/staged MJS hashes,
and records dirty working-tree state. Do not use the normal public release
script or edit `PublishConfiguration.xml` until the exact RC passes
`docs\PHASE7_RC_MANUAL_TEST.md`.

`phase7-rc1` was generated, mechanically verified, and owner-approved at:

```text
artifacts\release-candidates\CS2MetroDiagram-phase7-rc1
artifacts\release-candidates\CS2MetroDiagram-phase7-rc1-win-x64.zip
```

Automated verification passed for build/tests, Viewer publish, CS2 post-process,
MJS source/staging identity, package manifest, and ZIP contents. The public
Viewer asset has since advanced to the formal Beta.7 package; the private
`phase7-rc1` ZIP is retained only as historical acceptance evidence.

Published endpoints (Beta.7 completed 2026-07-17):

```text
GitHub: https://github.com/Eriksas/Cities-Skylines-2-Metro-Viewer/releases/tag/v0.1.0-beta.7
PDX: ModId 146643, version 0.1.0-beta.7, Public
```

Tag `v0.1.0-beta.7` points to release source commit `69ef6f1`. The uploaded
Viewer ZIP SHA-256 is
`FA48990FF923698F0AAEE9F08B06FAC808C743547BB1A69DB21B2869EA4ADB2E`.

## Current Intercity Geographic v2 Handoff

IG2.0 through IG2.2 are code-side complete on
`feature/intercity-geographic-v2`.

Completed:

- shared pure clipping/projection contract and synthetic fixtures;
- three-city local real-fixture inventory;
- isolated passenger Train collector and pure classifier;
- explicit cargo vehicle/station exclusion;
- CurveElement-first rail path extraction and per-line diagnostics;
- shared city-envelope clipping/projection in desktop and portable renderers;
- dark-grey railway bed plus equal-spaced white marks;
- outside-city station/tail clipping with clean clipped endpoints;
- hidden/default-off development gate;
- archived fixture audit: 17 train lines, 46,830 path points;
- three-city renderer parity audit:
  - 奥斯特: 6 desktop/portable railway parts, 0 rendered arrows;
  - 肇庆: 22 desktop/portable railway parts, 0 rendered arrows;
  - 谢菲尔德: 7 desktop/portable railway parts, 0 rendered arrows;
- Viewer manual icon editing through layout sidecars:
  - add, drag, reset, and delete;
  - airport, railway, intercity, bus, ferry, landmark, and information;
  - no exporter or schema changes;
- self-contained Viewer publish passed after the icon editor update;
- solution build, full tests, and CS2 Release post-process.

Next:

1. Validate the newly built collector in CS2 with both passenger and cargo
   rail. Old exported files cannot close that gate.
2. Inspect the fresh export diagnostics for accepted passenger lines, rejected
   cargo lines, path-point density, and outside connections.
3. Only after owner acceptance, begin IG2.3 Viewer/CLI/in-game controls. Keep
   every control default-off and geographic-only.

Do not expose, deploy, publish, or enable intercity by default yet. Do not add
train to any schematic layout. The archived real fixtures contain legacy tram
lines from the parked experiment; those are not part of the new collector.

For future icon work, use Rail Map Painter only as an external taxonomy and UX
reference. Do not copy its GPL-3.0 code or bundled icon assets into this MIT
repository.
