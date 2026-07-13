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
  -> immutable MetroExportDocument-compatible snapshot
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

### Phase 7B - Reusable In-Memory Snapshot

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

Deliver:

- Current-city capture, render, and inline SVG display.
- Refresh, layout switch, fit, zoom, and pan.
- City/line/station summary and complete empty/error/loading states.

Acceptance:

- User can load a real city and see its metro map without exporting first.
- Reopening the panel uses cache and feels immediate.
- Refresh updates the map after a network change.

### Phase 7E - Product Controls And Export

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
