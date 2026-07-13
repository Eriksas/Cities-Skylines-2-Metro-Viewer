# Phase 7 - In-Game Metro Preview Product Plan

## Product Goal

Build a polished, read-only metro-map workspace inside Cities: Skylines II.
The player opens it from the game's mod UI, previews the current city's metro
map, changes a small set of presentation options, refreshes after network
changes, and saves JSON or SVG without launching an external program.

The feature is complete only after the owner validates it in game. No Phase 7
build may be published to Paradox Mods before explicit approval.

## Product Contract

- The mod never changes city data or save data.
- Existing `Export Real Metro JSON` behavior and JSON schema stay compatible.
- Existing Viewer and CLI remain supported; in-game preview is an additional
  workflow, not a replacement.
- `schematic-anneal` is the recommended preview layout; `geographic` is the
  faithful fallback.
- The game UI never reads ECS directly. ECS capture happens in the C# backend.
- The frontend does not implement a second schematic-layout algorithm.
- No external executable is launched from the mod.
- Localization and visual polish are non-critical: they may degrade, but may
  never prevent exporter or preview startup.

## Target Experience

### Entry

- Add `CS2 Metro Diagram` to the supported universal mod menu/button area.
- Opening it shows one large map workspace rather than a settings page.
- The existing Options page remains for language and export-folder settings.

### Workspace

- Quiet CS2-style translucent shell with a large off-white map canvas.
- Compact top toolbar with familiar icons and tooltips.
- City name, line count, station count, render status, and last refresh time.
- Loading, no-city, no-metro, stale-data, success, and error states.
- Pan, mouse-wheel zoom, fit-to-view, and reset view.
- Responsive at 16:9, ultrawide, and 1080p; map content must not sit inside a
  tiny fixed preview frame.

### Controls

- Refresh current city.
- Fit map / zoom in / zoom out / reset view.
- Layout: `Schematic` or `Geographic`.
- Show default/non-important station names.
- Hide crowded labels.
- Export JSON.
- Save current SVG to the configured export folder.
- Close panel.

Advanced desktop editing, PNG/PDF export, and layout sidecar editing remain in
the Windows Viewer for the first release.

## Architecture

```text
CS2 ECS world
  -> MetroNetworkSnapshotService (game thread)
  -> immutable MetroDiagram.Engine snapshot
  -> portable render service (cached, no ECS access)
  -> SVG string + render diagnostics
  -> official CS2 UI binding/event bridge
  -> in-game SVG workspace
```

### Backend Boundaries

1. `MetroNetworkSnapshotService`
   - Extract the current exporter data-building path into a reusable service.
   - Return an in-memory snapshot plus diagnostics.
   - `RealMetroJsonExporter` consumes the snapshot and writes the same files.
   - Preview consumes the same snapshot without reading the just-written JSON.
   - Preserve current metro filtering, station grouping, names, colors, stops,
     and pathPoints behavior.

2. Portable rendering boundary
   - Audit `MetroDiagram.Core` and `MetroDiagram.Rendering` for the CS2 runtime.
   - Prefer a compatible shared target or extracted `MetroDiagram.Engine`
     project over linked copies of many source files.
   - Keep JSON loading and desktop-only file operations outside the runtime
     render subset.
   - Add parity fixtures: the same snapshot/options must produce equivalent SVG
     in tests and in the CS2-compatible renderer.
   - If full `schematic-anneal` portability is blocked, ship a temporary
     geographic preview only inside the development branch; do not publish the
     product until the recommended schematic mode is available.

3. `InGamePreviewController`
   - Own panel state, snapshot generation, render requests, cancellation, cache,
     export commands, and error reporting.
   - Never query ECS from a browser/UI callback or worker thread.
   - Reuse cached SVG when reopening unchanged data.

4. UI package
   - Use the installed CS2 toolchain's supported UI extension points and game
     design tokens/components.
   - C#/UI messages carry small state DTOs and SVG text; no raw ECS objects.
   - Sanitize generated SVG and avoid arbitrary HTML/script injection.

## Development Phases

### Phase 7A - UI And Binding Spike

Status (2026-07-13): passed and closed after owner in-game validation.

Implemented on `feature/ingame-preview`:

- Official `GameTopRight` entry and a binding-driven panel root appended through
  the current CS2 UI extension API.
- Responsive CS2-style panel with a large static sample SVG.
- `InGamePreviewUISystem` bindings for panel state, health JSON, refresh, and
  lifecycle logging.
- Preview registration is isolated so a preview failure cannot disable the
  existing exporter or Options page.
- CS2 Release build/post-process, offline solution build, and tests pass.

This spike intentionally contains no ECS queries, real-city snapshot capture,
or MetroDiagram renderer integration.

Runtime correction (2026-07-13): the first spike used only `game.togglePanel`
and game-panel renderer registration. The entry appeared, but the panel did not
mount in the owner's game. The corrected spike drives a `Game` root component
directly from the C# `panelOpen` binding. This keeps the official top-right
entry while removing the failed toggle-only dependency.

Owner validation confirmed that the corrected entry opens and closes normally,
the static workspace is visible, and the existing Options/export workflow is
still intact. Phase 7B/7C therefore proceed from this accepted UI shell.

Deliver:

- A feature branch, with no PDX publication.
- Universal mod-menu entry.
- Open/close responsive preview panel using a static sample SVG.
- Minimal C# -> UI state binding and UI -> C# refresh command.
- Lifecycle logging and a development-only health status.

Acceptance:

- Game loads with and without a city.
- Button appears once, panel opens/closes repeatedly, and no raw locale keys or
  JavaScript errors block the mod.
- Existing export button still works.

Manual validation checklist:

1. Restart Cities: Skylines II with the development build installed.
2. Confirm `CS2 Metro Diagram` appears exactly once in the top-right game UI.
3. Open the panel and confirm the static map uses the main responsive workspace,
   rather than a small fixed preview frame.
4. Press Refresh several times and confirm its counter/time changes and the mod
   log records each request.
5. Close and reopen the panel at least 10 times; confirm there are no duplicate
   buttons, stuck overlays, or uncaught UI errors.
6. Repeat at the main menu/no-city state and in a loaded city. If practical,
   also test a city with no metro lines.
7. Open the existing Options page and run `Export Real Metro JSON`; confirm the
   established latest/snapshot export behavior remains intact.
8. Inspect the game log and UI console. There must be no outer `failed to
   initialize` error and no JavaScript exception that blocks the mod.

Only after all checks pass should Phase 7A be marked closed and Phase 7B begin.

### Phase 7B - Reusable In-Memory Snapshot

Status (2026-07-13): code-side complete.

Implemented:

- `MetroDiagram.Engine.MetroNetworkSnapshot` is an immutable, dependency-light
  capture contract for city, stations, lines, stops, colors, and pathPoints.
- `MetroNetworkSnapshotService` captures once on the game thread and retains the
  latest successful snapshot plus diagnostics for later preview use.
- `RealMetroJsonExporter` consumes that same snapshot; its ECS filtering,
  station grouping, stop/path extraction, schema version, and output paths are
  unchanged.
- Stable snapshot revisions exclude export time, allowing unchanged cities to
  reuse rendered output.
- Empty/no-city snapshots serialize and render without exceptions.

Deliver:

- Extract exporter data capture from file writing.
- Preview snapshot DTO and diagnostics.
- Exporter output parity tests against current JSON fixtures.
- Snapshot version/revision used for cache invalidation.

Acceptance:

- Existing JSON shape remains unchanged.
- One capture can feed both JSON export and preview.
- No city and no metro produce valid empty states, not exceptions.

### Phase 7C - Portable Rendering Runtime

Status (2026-07-13): code-side complete; game-panel wiring is intentionally
deferred to Phase 7D.

Implemented:

- New `MetroDiagram.Engine` targets `netstandard2.0` and is packaged beside the
  net48 CS2 mod without desktop-only dependencies.
- The portable renderer supports `geographic` and deterministic
  `schematic-anneal` SVG output directly from `MetroNetworkSnapshot`.
- Dedicated in-game render profiles centralize bounded canvas, labels, service
  family merge, and annealing limits.
- `InGamePreviewRenderService` provides a bounded revision/options cache for the
  future controller.
- Tests cover schema round-trip, immutability/revision behavior, desktop/runtime
  route semantics, valid deterministic SVG, empty networks, and a 200-station
  performance fixture.

Deliver:

- CS2-compatible rendering project or multi-targeted runtime subset.
- `schematic-anneal` and `geographic` SVG generation from an in-memory snapshot.
- Render-option profile dedicated to the game workspace.
- Desktop/runtime SVG parity and valid-XML tests.

Acceptance:

- No duplicated JavaScript layout engine.
- Existing Viewer/CLI SVG output does not regress.
- A representative 200-station fixture renders within the agreed budget.

### Phase 7D - Real In-Game Preview MVP

Status (2026-07-13): code-side complete; real map visibility owner-validated.
The remaining D acceptance checks are cache reopen, refresh after a network
change, both layouts, and explicit empty/error-state exercises.

Implemented:

- The accepted panel now captures the current city on the game update thread
  and renders the immutable snapshot with `MetroDiagram.Engine`.
- Real inline SVG replaces the Phase 7A sample map. Schematic and geographic
  layouts, revision/options caching, refresh, fit, wheel zoom, and pointer pan
  are wired end to end.
- Explicit idle, loading, rendering, no-city, no-metro, and error states are
  exposed through one small state binding. Reopening an unchanged snapshot
  restores the cached SVG without another ECS capture.
- UI trigger callbacks only queue operations. ECS capture never runs from a
  browser callback or worker thread.

Deliver:

- Current-city capture, render, and inline SVG display.
- Refresh, layout switch, fit, zoom, and pan.
- City/line/station summary and complete empty/error/loading states.

Acceptance:

- User can load a real city and see its metro map without exporting first.
- Reopening the panel uses cache and feels immediate.
- Refresh updates the map after a network change.

### Phase 7E - Product Controls And Export

Status (2026-07-13): code-side complete; panel visibility owner-validated.
Export/save/control and persistence checks remain in the owner checklist.

Implemented:

- `Show default station names` and `Hide crowded labels` rerender from the
  cached snapshot and persist as hidden mod presentation preferences.
- `Export JSON` calls the established real exporter, whose resulting snapshot
  also refreshes the visible map.
- `Save current SVG` writes `<configured folder>\metro-diagram.svg` plus a
  unique timestamped file under `<configured folder>\exports\`.
- Busy/disabled/success/failure states, last output path, and copyable error
  details are present. Captured city data itself is never persisted as a UI
  preference.

Deliver:

- Generic-station-name and crowded-label controls.
- Export JSON and Save SVG buttons using the configured folder.
- Last-success path, copyable error details, and non-blocking notifications.
- Persist only presentation preferences, never captured city data.

Acceptance:

- Buttons have clear disabled/loading/success/failure states.
- Saved SVG matches the visible layout and is valid XML.
- Existing snapshot naming and latest-export behavior remain compatible.

### Phase 7F - Visual Design And Localization

Status (2026-07-13): owner-reviewed and closed for continued development.

Implemented:

- Geographic is the in-game first-open default, with a one-time migration for
  older development preferences. Schematic remains selectable; desktop defaults
  are unchanged.
- Layout and command actions use a separate wrapping toolbar, while Close stays
  in the header and label filters remain a distinct row.
- Binary filters use game-style pressed buttons with switch indicators; command
  buttons use CS2 `flat` and selected `primary` variants rather than native
  browser checkboxes/default buttons.
- The panel honors Auto/English/Simplified Chinese from the mod setting.
- Inline renderer SVG inherits CS2's locale-aware `--fontFamily`; standalone
  portable SVG declares Noto CJK fallbacks, removing the Arial tofu-box issue.
- Source tests cover the geographic migration, language state, CJK font stack,
  and Coherent-safe SVG preparation.

Deliver:

- CS2-consistent panel, toolbar, icons, spacing, focus, hover, and tooltips.
- Chinese and English UI text.
- Keyboard/controller-safe focus order where supported.
- 1080p, 1440p, 4K, and ultrawide screenshot review.

Acceptance:

- Map remains the dominant visual surface.
- No overlap, clipped controls, unreadable text, or tiny preview canvas.
- Localization failure cannot block panel or exporter startup.

### Phase 7G - Hardening And Regression

Status (2026-07-13): started. The first low-risk tranche coalesces redundant
refresh/export render work and adds a subdued schematic-only recommendation for
the desktop Viewer. Geographic remains the in-game default.

Deliver:

- No per-frame ECS scanning or layout computation.
- Snapshot/render cancellation, stale-result rejection, and bounded caches.
- Large-city stress fixture and repeated open/close/refresh soak test.
- Logs identify capture, layout, UI binding, and export failures separately.
- Multi-city validation bundle with screenshots and timings.

Initial performance targets:

- Cached panel reopen: perceived immediate, target under 200 ms.
- UI interaction while rendering: remains responsive.
- Typical real-city refresh: target under 1.5 seconds.
- No simulation-thread stall longer than one visible frame for snapshot capture;
  revise the capture strategy if profiling disproves this target.

### Phase 7H - Owner Acceptance And Release Candidate

Deliver:

- Private/local release candidate and manual test checklist.
- Owner tests mod loading, no-city, empty city, both real cities, refresh,
  layouts, labels, export paths, SVG save, language, and repeated panel use.
- Fix every blocker found by owner testing.

Release gate:

- Do not change the PDX public version during Phases 7A-7G.
- Do not publish Phase 7H until the owner explicitly says the game test passed.
- After approval: version bump, changelog, full preflight, CS2 build/post-process,
  package, GitHub release, then PDX update.

## Validation Matrix

Every implementation phase must keep these checks green:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore
```

Additional Phase 7 evidence:

- UI build/test command once the frontend project exists.
- Game log free of outer `failed to initialize` errors.
- UI console free of uncaught errors.
- Screenshot set for all supported resolutions.
- Timing report for snapshot, render, transfer, and first paint.
- Existing exporter JSON fixture comparison.

## Primary Risks

1. **Runtime compatibility** - current renderer targets .NET 8 while the CS2 mod
   targets the game toolchain/.NET Framework runtime. Resolve this before UI
   feature work expands.
2. **Main-thread stalls** - ECS capture and annealing must not run per frame.
3. **UI API drift** - validate against the installed toolchain and current game,
   not copied names from old community examples.
4. **Lifecycle regressions** - Options/localization incident rules apply to all
   preview registration and disposal code.
5. **Scope creep** - first release is preview and export, not the full desktop
   manual editor inside the game.

## Definition Of Done

The feature is done when a subscribed user can load a city, open an attractive
CS2-native panel, see a correct schematic metro map, pan/zoom/fit it, change the
supported label/layout options, refresh after edits, export JSON or save SVG,
close/reopen without lag or errors, and encounter clear empty/error states.
The owner must approve the exact build in game before PDX publication.

## Phase 7F Follow-up Acceptance

- Mouse dragging must work through CS2 Coherent without pointer capture and
  must keep tracking until a global mouse release.
- Geographic and schematic portable renders must keep station markers and
  labels inside a shared marker-aware map frame.
- These fixes are presentation-only: they do not change exporter ECS reads,
  `metro-export.json`, raw station positions, or route geometry.
- Code-side validation and owner game review passed for this follow-up.

## Phase 7G Runtime Evidence

The state payload and categorized game log must make these boundaries visible:

- ECS snapshot capture duration and captured revision/counts.
- End-to-end render request duration versus renderer duration.
- Render-cache hit/miss and bounded cache entry count.
- Panel open count and redundant request coalescing count.
- Lifecycle, capture, render, export, save, and settings failures as separate
  log categories.

The current controller is synchronous and processes at most one queued
operation per update. Phase 7G therefore stabilizes it with request coalescing,
panel-close cancellation, and a bounded LRU cache; it does not introduce an
asynchronous renderer or change the map output.

Owner acceptance: passed in game on 2026-07-13 with no blocking issue reported.
Phase 7G is closed; proceed to the Phase 7H release gate without adding new
preview behavior to the accepted candidate.
