# Changelog

## Unreleased

Desktop sheet header redesign and a whole-map preview mode.

### Viewer / desktop renderer

- **The transit-map header is redesigned.** The old slanted color-block band
  was almost fully covered by a near-full-width title capsule, leaving
  clipped-looking corner fragments, and the fixed-size title could overflow
  the capsule on smaller sheets. The band is now a clean blue-teal-green
  gradient, and the title capsule is centered and sized from its typography
  (CN 54px + EN 24px), with the info icon anchored inside the capsule edge.
- **New "Fit window" preview mode (now the default for fresh installs).**
  Scales the whole sheet to fit both preview dimensions, so tall maps no
  longer open as a narrow scrolling slice. "Fit width" and the percent zooms
  are unchanged; saved user settings are respected.
- **The preview gets much more of the window.** The manual-edit button row
  now appears only while "Manual edit" is checked (the checkbox moved into
  the selector row), the file-info card is a single line, error text shows
  only when present, and chrome margins are tighter — roughly 140px more
  preview height on a standard window.
- **Save-format selector.** A small SVG / PNG / PDF dropdown next to "Save
  Map" pre-selects the format in the save dialog, so players no longer need
  to know the file type is chosen by extension. The choice is remembered.
- **The control system is fully styled.** Dropdowns, checkboxes, the
  advanced-settings expander, and data-grid headers now follow the same flat
  rounded design language as the buttons (accent hover, soft popup shadow,
  tinted selection) instead of stock WPF chrome; the preview pane gets thin
  modern scrollbars and a softer paper shadow.
- **The preview shows the complete sheet.** Content-bounds focusing no longer
  crops the transit-map header at the top or the bottom "Key to lines and
  symbols" legend; plain renders still trim empty margins.
- **Noto Sans SC ships with the toolchain.** All sheet text (title, station
  labels, legend) now renders in 思源黑体 with a Microsoft YaHei fallback. The
  fonts (SIL OFL, license included) are bundled into the exporter library, so
  the Viewer preview, PNG, and PDF look identical on machines without the
  font installed.
- **The header band fully encloses the title card** (official metro-map
  convention, per the owner's reference), and the whole title block scales
  down proportionally on short sheets instead of overflowing.

First player-reported bug (issue #4): an 8-line city rendered only 3 lines.

### Fixed

- **Auto-named routes export with their real numbered names.** Routes the
  player never renamed exported as the route tool's prefab name (e.g. six
  lines all called `地铁路线工具`), because `GetRenderedLabelName` does not
  resolve generated route names. The exporter now prefers the player's custom
  name and otherwise rebuilds the numbered name (`地铁 3 号线` / `Metro
  Line 3`) from the route number, in the mod interface language. Diagnostics
  record the name source (`custom` / `generated` / `rendered` / `fallback`).
- **Same-name lines with different colors are no longer merged into one
  display family.** Number-less duplicate names (the placeholder case above)
  now merge by name + color, so all 8 lines of the reported city render.
  Numbered families (`10号线`) still merge by name alone, keeping the
  legitimate shared-corridor/service-variant merges and existing outputs
  byte-identical (verified on both validation cities, in-game and desktop).

### Validation

- 163 tests (new: family split/merge matrix, exporter name-source contract).
- Reported city renders 8/8 route families in-game and in the Viewer.

Dense-center label decluttering and player-facing polish for the in-game
preview.

### In-game preview

- Wheel zoom now anchors to the cursor: the map point under the pointer stays
  put while zooming, instead of the view always scaling around the sheet
  center. The math lives in an exported pure function
  (`computeCursorAnchoredPan`) verified numerically outside the game.
- The sheet title and legend header now follow the mod interface language:
  Chinese players see `谢菲尔德地铁线路图` and `线路` instead of
  "`谢菲尔德 Metro Diagram`" and "Lines". Desktop and CLI outputs are
  byte-identical to before; switching the language re-renders an open preview
  (the render cache keys include the language).
- The `−`/`+`/fit zoom buttons are compact instead of inheriting the
  toolbar-width button style.
- Small networks no longer freeze geographically straight lines into zigzags:
  the schematic anneal spends its leftover attempt budget on up to three
  independent restarts (distinct fixed seeds, cheapest polished result wins)
  when a single pass would use less than half the budget. A real 22-station
  city whose straight east-west line rendered as a large Λ detour now renders
  flat. Networks of roughly 40+ stations fill the budget in one pass and stay
  byte-identical (verified point-for-point on both validation cities).
- Bracketed CS2 default asset names (`高架地铁站（小型）`, `地铁站（高架旁路）`,
  `Metro Station (…)`) are now recognized as default names: hidden while
  "Show default station names" is off, and no longer rendered as confusing
  duplicate labels. Player names that merely end in brackets (e.g.
  `中央站（东）`) are unaffected. Applies to both the in-game preview and the
  desktop toolchain.

- Station labels gained a second, farther candidate ring (8 -> 16 slots). In
  dense centers a label now steps outward instead of fusing with a neighbour
  (the Sheffield `环境保护中心站` / `生活中心站` fusion resolves to zero overlap).
- The route-under-label penalty now measures the clipped segment length inside
  the label box (desktop parity) instead of counting boolean hits, so labels
  choose sides that clear nearby lines (route length under labels: Sheffield
  66px -> 33px, Zhaoqing 59px -> 14px).
- Top/bottom label slots use `middle`/`end` text anchors so rendered glyphs
  stay inside the estimated collision box even when game font metrics differ.
- In-game base label font is now 11px (was 12). Labels were proportionally
  ~2.5x larger on the 1800x1100 panel than on the desktop poster; with beta.6
  vector zoom, readability is preserved while the dense center gains breathing
  room. Visible label counts are unchanged on both validation cities.

### Validation

- 156 tests pass; layout math untouched (label-render layer only).
- Before/after audits on both real cities: label-overlap pairs 1 -> 0
  (Sheffield) and 0 -> 0 (Zhaoqing); zero frame-bounds violations.

## v0.1.0-beta.6 - 2026-07-16

Hardens the in-game preview for compact and extreme-aspect networks.

### Fixed

- In-game previews now keep a stable `1800x1100` viewport, so vertical,
  horizontal, single-line, parallel, and large-coordinate networks no longer
  extend beyond the white map sheet.
- Fit-to-window consistently contains routes, station markers, labels, title,
  and legend in the panel.
- Zoom and pan now change the inline SVG `viewBox` instead of scaling a cached
  HTML layer. Enlarged station labels and route strokes remain vector-rendered
  rather than becoming blurry.

### Validation

- Added a seven-station vertical single-line regression fixture.
- Added synthetic horizontal, vertical, diagonal, dual-parallel, crossing, and
  very-large-coordinate coverage for both in-game layouts.
- Revalidated the real 59-station/10-line city and the 24-station/5-line large
  sample without canvas clipping.

## v0.1.0-beta.5 - 2026-07-16

The in-game schematic reaches desktop layout quality. Owner-validated in game.

### In-game preview

- **The in-game schematic now runs the desktop layout math.** The portable
  engine ports the product cost model (octilinearity, bends, crossings,
  clearance with same-line exemptions, spacing gate), tracks the best state
  with a fixed seed - layouts no longer change between refreshes - and scales
  the result to fill the panel.
- **Shared tracks render as parallel lines in-game**, with same-color branches
  collapsing onto one lane and miter-joined corners, matching the desktop look.
- **Mirrored out-and-back routes no longer draw false terminals**, and station
  labels avoid routes, stations, and each other (collision-aware placement).
- Schematic render stays fast: ~320 ms for a 59-station city (int-indexed
  solver), geographic ~30 ms.

## v0.1.0-beta.4 - 2026-07-13

Adds the first owner-validated, read-only in-game metro preview while preserving
the established exporter, JSON schema, desktop Viewer, and CLI workflows.

### Added

- A top-right CS2 toolbar entry and responsive in-game metro-map workspace.
- Geographic and lightweight schematic previews rendered directly from the
  current city's immutable metro snapshot.
- Refresh, layout switching, persisted label filters, zoom/pan/Fit, JSON export,
  and saving the currently visible SVG from the panel.
- Chinese/English interface support plus explicit loading, no-city, no-metro,
  saving, and error states.
- Runtime capture/render telemetry and categorized Lifecycle, Capture, Render,
  Export, Save, and Settings logging.

### Improved

- Marker-aware safe framing keeps edge stations and labels inside the canvas.
- Repeated panel use and rapid controls are hardened with request coalescing,
  cancellation of stale visual work, and a four-entry LRU render cache.
- Export and preview now share one immutable network snapshot without changing
  the public `metro-export.json` schema or real ECS extraction semantics.

### Notes

- Geographic is the reliable default in game. The in-game schematic remains a
  lightweight preview; the desktop Viewer is recommended for the complete
  product schematic and PNG/PDF output.
- This exact Phase 7 candidate passed owner in-game acceptance before release.

## v0.1.0-beta.3 - 2026-07-10

Emergency mod-loading hotfix. beta.2 failed to initialize in-game, which took
the exporter down entirely.

### Fixed

- **The mod loads again.** beta.2 registered localization sources before the
  options page. CS2's `AddSource` eagerly reads the setting's locale IDs
  through the registered options machinery, so every locale registration threw
  and mod initialization aborted before the exporter existed. The official
  template order (options page first, locale sources second) is restored.
- **Localization can no longer break the exporter.** If locale registration
  fails for any reason, the mod now logs a warning and continues with raw
  locale keys instead of aborting initialization.

## v0.1.0-beta.2 - 2026-07-10 (superseded)

Attempted localization hotfix; the reordering it introduced prevented the mod
from initializing in-game. Replaced by v0.1.0-beta.3 within the hour. Its
null-safe cleanup and locale-ID handling improvements are retained.

## v0.1.0-beta.1 - 2026-07-10

First beta. The core pipeline — in-game export, automatic octilinear layout,
hand editing, and now multi-format output — is feature-complete and has been
stable across the alpha series, so the project moves from alpha to beta.

### Added

- **PNG and PDF export.** The Viewer's save dialog now offers SVG, PNG, and PDF;
  the CLI picks the format from the output file extension (`map.png`,
  `map.pdf`). PNG rasterizes at the SVG's pixel size; PDF is a real vector
  document with embedded, subsetted fonts (Chinese labels included).

### Internal

- The 131-test suite now also runs under `dotnet test` (xunit adapter) with
  per-test reporting; the classic `dotnet run` runner is unchanged.
- Dated development-journal entries moved to `docs/archive/`.

## v0.1.0-alpha.7 - 2026-07-10

### Mod usability and export reliability

- Added a mod-only options language selector: Auto (follow the game), English,
  and Simplified Chinese. It refreshes this mod's settings text without
  changing the game's global language.
- Real exports are staged before publication and each destination is replaced
  atomically where supported. The latest JSON is committed last so the Viewer
  cannot open a half-written document.
- Added a new Paradox Mods hero image built from a real generated metro map;
  the editable source is retained in Figma for future release updates.

### Renderer and workflow hardening

- Explicit render choices are now preserved: selecting standard map styling or
  disabling service-family merge is no longer silently overridden by
  schematic product defaults.
- Geometry caching now keys only geometry-affecting options, reducing needless
  solver work when labels or presentation settings change.
- Product candidate, alpha bundle, and regression gate scripts now exercise
  `schematic-anneal` as the default candidate while retaining comparison
  layouts in validation bundles.
- Consolidated duplicated PowerShell naming, runtime discovery, and diagnostics
  path helpers into `MetroScriptCommon.psm1`.
- CI now includes a Release Viewer publish smoke step in addition to build and
  tests.

## v0.1.0-alpha.6 - 2026-07-08

Performance and map-quality update. The in-game mod is unchanged and stays at
`0.1.0-alpha.3` on Paradox Mods; only the companion Viewer is updated here.

### Performance

- **Editing re-renders skip the layout solver.** The layout result is cached, so
  dragging stations, undo/redo, and other edits re-render in a fraction of a
  second even in the slower legacy `schematic-map` mode.
- **The preview keeps your scroll position** across edits, and window resizes no
  longer jump the view back to the top-left corner.
- **Rendering runs off the UI thread**, so switching layouts or sizes no longer
  freezes the window.
- **The anneal layout solver is ~3x faster** (spatial pruning; identical output).

### Map quality

- **Labels stay off route lines.** Label placement now treats routes as
  obstacles and flips a label to a clear side when one exists; on real cities
  the route-through-label overlap drops to zero in the default layout.
- **A "Label Side" button** cycles the selected station's label through
  auto/left/right/top/bottom.
- **Parallel corridors keep their lane spacing through bends** (miter joins
  instead of averaged corners).

## v0.1.0-alpha.5 - 2026-07-07

Viewer editing update (Tier 2). The in-game mod is unchanged and stays at
`0.1.0-alpha.3` on Paradox Mods; only the companion Viewer is updated here.

### Added

- **Multi-select.** In station edit mode, Ctrl/Cmd+click toggles stations in and
  out of a selection (highlighted in magenta). Drag any member to move the whole
  group; horizontal/vertical align, reset, and clear all work on the group.
- **Arrow-key nudging.** Nudge the selected station or group with the arrow keys;
  hold Shift for a coarser step. A burst of nudges is a single undo.
- **Bend handles.** A new "Bends" edit mode: drag a route segment to drop a bend
  on the edge between two stations, so a line can be routed by hand. Bends undo,
  reset, and clear like other edits.

## v0.1.0-alpha.4 - 2026-07-07

Viewer editing update. The in-game mod is unchanged and stays at
`0.1.0-alpha.3` on Paradox Mods; only the companion Viewer is updated here.

### Added

- **Undo / redo for manual editing.** Every layout override change — station,
  segment, and label drags, horizontal/vertical alignment, hide-label, and
  clear-all — can now be undone and redone. Toolbar buttons plus `Ctrl+Z` /
  `Ctrl+Y` (`Ctrl+Shift+Z`) shortcuts; history resets when you load an export.
- **Octilinear snapping while dragging stations.** Dropping a station near a
  horizontal, vertical, or 45° line to its nearest neighbour snaps it onto that
  axis, so hand edits stay on the schematic grid. Drops beyond ~16° stay free.

## v0.1.0-alpha.3 - 2026-07-07

Public Paradox Mods alpha release.

### Changed

- Publishes the Paradox Mods listing publicly.
- Keeps `schematic-anneal` as the default Viewer/CLI schematic layout.
- Keeps the simplified in-game export options and custom export-folder support.
- Keeps Viewer manual editing for station, label, and segment sidecar overrides.
- Keeps `geographic` available as the faithful exported-geometry fallback.

## v0.1.0-alpha.2 - 2026-07-07

First public Paradox Mods release. This build makes the schematic map the
default and readable out of the box, and gives the Viewer a proper interface.

### New default map: schematic-anneal

- A new `schematic-anneal` layout is now the default. Instead of a stack of
  local touch-up passes, it lays the whole network out by minimizing one global
  quality score (octilinearity, even spacing, few bends, no crossings) with
  deterministic simulated annealing. The same input always produces the same
  map.
- Across sample and real-city networks it produced cleaner, more consistent maps
  than the previous `schematic-map` mode on every measured quality metric.
- **Shared corridors now render as parallel lines.** Where several lines run
  through the same segment they are drawn side by side instead of stacked, and a
  line and its branch (same color) stay a single line.
- **Straight lines stay straight.** A stations-that-should-be-in-a-row bug that
  put kinks in through-running lines is fixed.
- **Maps fill the canvas.** The poster canvas now adapts to the shape of the
  network and the map is centered and scaled to fill it, instead of sitting in a
  letterboxed strip.

### Redesigned Viewer

- New visual design: brand header, clear primary actions, hover states, and a
  metro-map teal accent throughout.
- Advanced numeric settings are tucked into a collapsible "Advanced settings"
  panel, so the default screen is clean; manual editing controls are grouped in
  their own section.
- Opening a city now shows the whole map (fit-to-width) in the new default
  schematic-anneal style right away.

### Mod

- The in-game options page is simplified to what a player needs: pick an export
  folder and click **Export Real Metro JSON**. The developer-only "Export Test
  Metro JSON" button and the "Debug" transport-dump group were removed.

### Also

- Retired the legacy `schematic-lite` mode. `geographic` (faithful geometry),
  `schematic-map`, and `schematic-v2` remain selectable in the Viewer/CLI.
- CLI can emit objective layout scores with `--emit-layout-score`; new
  `scripts\compare-schematic-layouts.ps1` compares layouts across the whole
  sample corpus.

## v0.1.0-alpha.2-candidate - 2026-06-19

Alpha.2 candidate for focused external validation after the alpha.1 release.

### Added

- CurveElement-first route geometry extraction for real metro `line.pathPoints`.
- Geographic rendering baseline using exported pathPoints by default in the recommended alpha workflow.
- Renderer-only service family merge for same-line express/local variants, with service variants listed in the legend.
- Centralized visual style tokens for route widths, station markers, label halo, shared corridors, and express markers.
- White-filled station marker readability polish.
- Renderer-only station route anchor alignment so station markers and labels visually sit on geographic pathPoint routes.
- Timestamped export snapshot naming while preserving the latest `metro-export.json` / `metro-export-diagnostics.txt` paths.
- Primary city baseline workflow under `artifacts\primary-city-baseline`.
- Visual continuity diagnostics and SVG/PNG baseline generation helpers.
- Viewer in-app SVG preview and export-data inspection.
- Viewer no longer exposes legacy `schematic-lite`; it focuses on `geographic`, `schematic-v2`, and `schematic-map`.
- Product-facing experimental `schematic-map` layout with transit-map framing, route badges, compact legend/key behavior, and official-map-style direction normalization.
- Schematic-map SVG audit reports for octilinear grammar, crossings, turns, route badges, shared platform overlays, and stroke-width consistency.
- Schematic regression gate for running real exports and synthetic regression samples together before accepting renderer changes.
- Shared-platform rendering fixes for close parallel tracks, same-number branch lane collapse, and knockout-width clamping to avoid white fringes.
- Regression samples for simple networks and shared-platform/branch cases.

### Experimental

- Shared corridor composite/normalized rendering remains off by default.
- Express center stripe rendering remains off by default.
- Parallel corridor offset remains off by default and is no longer the recommended direction.
- `schematic-map` is available for validation but is not yet the default alpha recommendation.

### Notes

- The accepted alpha.2 candidate baseline is `geographic + UsePathPoints + service family merge / normalized family rendering`.
- Shared corridor and express stripe outputs are comparison artifacts only and should not block alpha testing.
- Phase 5A.9 Route Run Stitcher is not started for this candidate.
- Use `scripts\generate-schematic-regression-gate.ps1` before accepting future schematic-map renderer changes.

## v0.1.0-alpha.1 - 2026-05-26

First alpha release package for external testing.

### Added

- Real Cities: Skylines II metro/subway JSON export.
- `metro-export-diagnostics.txt` for real export troubleshooting.
- Offline JSON loader, validation, and fallback handling.
- SVG rendering for metro networks.
- Natural numeric legend sorting.
- Basic label placement and label-priority handling.
- `geographic` and `schematic-lite` renderer layout modes.
- CLI conversion from metro JSON to SVG.
- Local Windows Viewer for opening JSON, previewing SVG, changing render settings, and saving SVG.
- Viewer support for default export detection.
- Viewer settings persistence.
- Label strategy options for hiding generic and crowded station labels.
- Minimal English/Chinese Viewer UI.
- Self-contained Windows x64 Viewer packaging.
- Alpha release package structure with mod artifacts, Viewer, docs, samples, known issues, and feedback template.

### Known Limitations

- Metro/subway only.
- No offline save parsing.
- `schematic-lite` is intentionally simple and not a full schematic layout engine.
- Dense station labels can still overlap or feel crowded.
- Interchange grouping may not be perfect for every city.
- No PNG/PDF export.
- No in-game SVG preview.
- No game-to-Viewer launch integration.
