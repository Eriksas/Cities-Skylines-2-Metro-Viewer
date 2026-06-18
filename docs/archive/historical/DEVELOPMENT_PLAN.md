# CS2 Metro Diagram — Development Plan for Codex

> This document is the project memory and execution guide for Codex.  
> Codex should read this file before making architectural decisions, creating tasks, or modifying project structure.  
> The goal is to build a Cities: Skylines II mod + external viewer that exports metro data from the current city and renders a clean schematic metro diagram inspired by Hong Kong / Guangzhou transit maps.

---

## 1. Project Vision

### 1.1 One-sentence goal

Create a **Cities: Skylines II metro diagram exporter and viewer**: the in-game mod exports metro network data from the currently loaded city, and an external viewer converts that data into a readable SVG metro diagram.

### 1.2 Product inspiration

The project is inspired by the workflow of **CS2MapView**:

1. A lightweight in-game exporter mod runs inside Cities: Skylines II.
2. The exporter writes data files to disk.
3. A separate external viewer opens those files.
4. Rendering and UI complexity stay outside the game.

This project does **not** attempt to draw a full city map. It only focuses on the metro / subway network.

### 1.3 Desired user experience

A player should be able to:

1. Build metro lines in Cities: Skylines II.
2. Open the mod options page.
3. Click **Export Metro Network**.
4. Open the generated `metro.json` in the external viewer.
5. Generate a clean SVG diagram.
6. Share the image with other players.

The target experience is simple, friendly, and low-friction. The mod should feel like a “one-click export and draw my metro map” tool.

---

## 2. Product Philosophy

### 2.1 What the project should optimize for

The project should prioritize:

- Correct line order.
- Correct station sequence.
- Correct interchange relationships where possible.
- A readable schematic diagram.
- Easy export and easy sharing.
- Stable file formats.
- Modular code that can survive game updates better.

### 2.2 What the project should not optimize for in v0.1

Do not prioritize:

- Exact real-world scale.
- Exact in-game geographic proportions.
- Perfect automatic layout.
- In-game diagram rendering.
- Direct save-file parsing.
- Complete support for all transport modes.
- Full visual recreation of real Hong Kong / Guangzhou official maps.

The diagram only needs to use in-game positions as **reference positions**. It is acceptable, and expected, that the output will be schematic rather than geographically exact.

### 2.3 Important design constraint

The project must separate responsibilities clearly:

- **Exporter** reads game data and writes JSON.
- **Core** validates and transforms metro data.
- **Layout** computes schematic positions.
- **Rendering** creates SVG output.
- **Viewer** provides a user interface for opening files and exporting diagrams.

Avoid mixing these responsibilities.

---

## 3. High-level Architecture

Recommended solution structure:

```text
CS2MetroDiagram.sln

/src
  /MetroDiagram.Core
    Shared DTOs, schema definitions, validation, topology utilities.

  /MetroDiagram.Layout
    Coordinate normalization, grid snapping, schematic layout algorithms.

  /MetroDiagram.Rendering
    SVG rendering, style presets, labels, legends.

  /MetroDiagram.Viewer
    External desktop viewer. Opens metro.json and exports SVG.

  /MetroDiagram.Exporter
    Cities: Skylines II Code Mod. Reads current city data and writes metro.json.

  /MetroDiagram.Tests
    Unit tests for Core, Layout, Rendering, and sample data.

/docs
  DEVELOPMENT_PLAN.md
  JSON_SCHEMA.md
  USER_GUIDE.md
  DEV_NOTES.md
  DECISION_LOG.md

/samples
  sample-metro-small.json
  sample-metro-interchange.json
  sample-metro-branch.json
  sample-metro-loop.json
```

If creating all projects at once is too much, start with:

```text
MetroDiagram.Core
MetroDiagram.Rendering
MetroDiagram.Viewer or MetroDiagram.Cli
MetroDiagram.Tests
samples/
```

Then add `MetroDiagram.Exporter` after the offline JSON-to-SVG pipeline works.

---

## 4. Canonical Data Flow

The intended data flow is:

```text
Cities: Skylines II city loaded
        ↓
MetroDiagram.Exporter reads metro lines and stops
        ↓
Exporter writes metro.json
        ↓
MetroDiagram.Viewer opens metro.json
        ↓
Core validates and normalizes the network
        ↓
Layout computes schematic diagram coordinates
        ↓
Rendering generates SVG
        ↓
User saves and shares the diagram
```

The first development milestone should not require Cities: Skylines II at all. First build and test:

```text
sample JSON → Core → Layout → SVG
```

Only after this offline chain is stable should the project connect to the game exporter.

---

## 5. File Format: `metro.json`

### 5.1 Schema principles

The export format should be:

- Human-readable.
- Stable between versions.
- Easy to validate.
- Easy to generate from the game.
- Easy to render outside the game.
- Extensible without breaking older files.

Always include a `schemaVersion` field.

### 5.2 Minimum v0.1 JSON shape

```json
{
  "schemaVersion": 1,
  "generator": {
    "name": "CS2 Metro Diagram",
    "version": "0.1.0"
  },
  "game": {
    "name": "Cities: Skylines II",
    "version": "unknown"
  },
  "city": {
    "name": "Example City",
    "exportedAtUtc": "2026-05-26T12:00:00Z"
  },
  "network": {
    "type": "metro",
    "stations": [
      {
        "id": "station_001",
        "name": "Central",
        "position": {
          "x": 1200.5,
          "z": 830.2
        },
        "lines": ["line_001", "line_002"],
        "isInterchange": true
      }
    ],
    "lines": [
      {
        "id": "line_001",
        "name": "Line 1",
        "color": "#D71920",
        "mode": "metro",
        "stops": ["station_001", "station_002", "station_003"]
      }
    ]
  }
}
```

### 5.3 Required DTOs

Codex should create DTO classes similar to:

```text
MetroExportDocument
GeneratorInfo
GameInfo
CityInfo
MetroNetwork
MetroStation
MetroLine
MetroPosition
```

Optional later DTOs:

```text
MetroInterchange
MetroStyleSettings
LayoutOverrideDocument
StationLayoutOverride
LineLayoutOverride
```

### 5.4 Fallback rules

When data is missing:

- Missing station name → generate `Station 1`, `Station 2`, etc.
- Missing line name → generate `Line 1`, `Line 2`, etc.
- Missing line color → assign color from internal palette.
- Missing city name → use `Unnamed City`.
- Missing position → do not crash; mark station as invalid or place it using sequence fallback.

Never crash because one line or one station is incomplete.

---

## 6. Visual Target

### 6.1 v0.1 visual output

The v0.1 output should be simple but readable:

- White background.
- Thick colored route lines.
- Rounded line joins where possible.
- Small station dots.
- Larger interchange markers.
- Station names.
- City name title.
- Basic legend.
- SVG export.

### 6.2 Style presets

Initial preset:

```text
Minimal
```

Future presets:

```text
HongKongInspired
GuangzhouInspired
CleanDark
GameColors
```

Important: do not copy official transit maps, logos, fonts, icons, or exact layouts. The presets should be inspired by broad visual principles only.

---

## 7. Phase-based Development Roadmap

Codex must treat the project as a sequence of phases. Do not jump ahead to later features before the current phase meets its definition of done.

---

# Phase 0 — Repository and Project Memory

## Purpose

Create a stable foundation so future development is organized and trackable.

## Goals

- Create repository structure.
- Add documentation files.
- Add sample data folder.
- Add project state tracking.

## Tasks

1. Create solution folder structure.
2. Add this file as `docs/DEVELOPMENT_PLAN.md`.
3. Add `README.md` with a short project overview.
4. Add `docs/DEV_NOTES.md` for discoveries and implementation notes.
5. Add `docs/DECISION_LOG.md` for architectural decisions.
6. Add `docs/JSON_SCHEMA.md` for the export format.
7. Add `docs/PROJECT_STATE.md` for current status.
8. Add `.gitignore` for C#, Visual Studio, Rider, game build outputs, and temporary exports.
9. Add `.editorconfig`.

## Definition of Done

- Repository structure exists.
- Documentation files exist.
- Codex can explain the project goal from README.
- `PROJECT_STATE.md` contains current phase, next task, blocked issues, and completed tasks.

## Codex management rule

After every significant change, update `docs/PROJECT_STATE.md`.

Suggested `PROJECT_STATE.md` format:

```markdown
# Project State

## Current Phase
Phase 1 — Offline JSON to SVG

## Current Goal
Generate a basic SVG from sample metro JSON.

## Completed
- Created solution structure.
- Added DTO models.

## In Progress
- Implementing Minimal SVG renderer.

## Blocked
- None.

## Next Actions
1. Add sample JSON.
2. Implement JSON loader.
3. Render first SVG.
```

---

# Phase 1 — Offline JSON to SVG Pipeline

## Purpose

Prove that the project can generate a metro diagram without depending on the game. This gives the project a working core early and prevents the game API from blocking all progress.

## Goals

- Define DTOs.
- Load sample JSON.
- Validate the metro network.
- Generate a simple SVG diagram.

## Tasks

1. Create `MetroDiagram.Core`.
2. Add DTOs for `metro.json`.
3. Add JSON serialization / deserialization.
4. Create `samples/sample-metro-small.json`.
5. Create `samples/sample-metro-interchange.json`.
6. Create `MetroDiagram.Rendering`.
7. Implement `MetroSvgRenderer`.
8. Render route lines using station coordinates.
9. Render station dots.
10. Render station labels.
11. Render interchange stations differently.
12. Export `output.svg`.
13. Create tests that load sample JSON and verify SVG output is non-empty.

## Minimum rendering algorithm

For v0.1:

1. Read all station `x/z` coordinates.
2. Compute bounding box.
3. Normalize coordinates into SVG canvas coordinates.
4. Add margins.
5. For each line, connect stations in `stops` order.
6. Draw station markers after drawing lines.
7. Draw labels last.

Do not implement complex schematic layout yet.

## Definition of Done

- Running the viewer or console demo with `sample-metro-small.json` creates an SVG.
- SVG shows at least one colored line.
- SVG shows station dots.
- SVG shows station names.
- Interchange sample draws a visually distinct interchange marker.
- Unit tests pass.

## Notes for Codex

This phase is more important than the exporter. If this phase fails, the project has no useful output. Keep it small and shippable.

---

# Phase 2 — Minimal Viewer or CLI

## Purpose

Give users and developers a convenient way to convert JSON into SVG.

## Goals

- Open a JSON file.
- Generate SVG.
- Save SVG.
- Show basic errors.

## Recommended approach

Start with the simplest possible tool:

```text
MetroDiagram.Cli input.json output.svg
```

Then add a desktop viewer later.

If a GUI is preferred, create `MetroDiagram.Viewer` with:

- Open file button.
- Generate button.
- Export SVG button.
- Basic preview, optional.

## Tasks

1. Create CLI or basic viewer.
2. Allow file selection or command-line input path.
3. Load `metro.json`.
4. Validate data.
5. Generate SVG.
6. Save SVG.
7. Display errors in user-friendly language.

## Definition of Done

- User can run one command or use one simple UI to convert JSON to SVG.
- Invalid JSON produces a clear error message.
- Empty network does not crash.
- Missing station references are reported clearly.

## Management rule

Do not add advanced UI until the CLI or minimal viewer is reliable.

---

# Phase 3 — Basic Schematic Layout

## Purpose

Improve readability by turning raw game coordinates into a cleaner diagram.

## Goals

- Use game positions as reference only.
- Produce a diagram that is readable even when the city is not geometrically clean.
- Avoid chasing perfect automatic layout too early.

## Tasks

1. Create `MetroDiagram.Layout` if not already created.
2. Implement coordinate normalization.
3. Implement optional grid snapping.
4. Implement simple label placement.
5. Implement basic station collision detection.
6. Implement basic line simplification.
7. Add layout options:
   - Raw reference layout.
   - Grid-snapped layout.
   - Schematic experimental layout.

## v0.1 layout rules

Start with these simple rules:

```text
- Preserve station order.
- Preserve rough relative position.
- Normalize coordinates to fit canvas.
- Use margins.
- Snap station coordinates to a grid if enabled.
- Do not modify topology.
```

## Later layout ideas

Do not implement immediately, but document for later:

```text
- Snap line segments to 0 / 45 / 90 degrees.
- Center-area expansion.
- Peripheral compression.
- Label collision scoring.
- Manual layout overrides.
- Branch handling.
- Loop handling.
- Parallel lines.
```

## Definition of Done

- Renderer can choose between raw normalized layout and grid-snapped layout.
- Sample files still render correctly.
- Interchange stations remain visually coherent.
- No topology is broken by layout.

---

# Phase 4 — CS2 Exporter Shell

## Purpose

Create a working in-game mod shell before attempting real metro data extraction.

## Goals

- Game loads the mod.
- Mod options page exists.
- Export button exists.
- Clicking export writes a test JSON file.
- Logging works.

## Tasks

1. Create `MetroDiagram.Exporter` using the official Cities: Skylines II code mod template.
2. Ensure the mod builds and loads in-game.
3. Add mod settings.
4. Add export directory setting.
5. Add `Export Metro Network` button.
6. On click, write `test-export.json` containing static sample data.
7. Log success and failure.
8. Show a user-facing success/failure message if possible.

## Definition of Done

- Cities: Skylines II can load the mod.
- Mod options page appears.
- Export button creates a JSON file.
- Export failure writes a useful log entry.
- The exported test JSON can be opened by the viewer.

## Important rule

Do not attempt to extract real game data until this shell works reliably.

---

# Phase 5 — CS2 Metro Data Discovery

## Purpose

Discover the correct CS2 ECS components / data structures for metro lines, stops, names, colors, and positions.

## Goals

- Identify where public transport line data lives.
- Identify how to filter metro lines.
- Identify station / stop sequence data.
- Identify line color and names.
- Identify station positions.

## Tasks

1. Add a `DebugDump` export mode.
2. Dump candidate public transport entities.
3. Dump component names and relevant fields where possible.
4. Search for route, line, stop, transport, station, and metro-related data.
5. Export discovery logs to a separate debug file.
6. Document findings in `docs/DEV_NOTES.md`.
7. Keep game-specific reading code isolated in one service.

## Suggested output files

```text
metro-debug-entities.txt
metro-debug-components.json
metro-debug-lines.json
```

## Definition of Done

- Codex can document which game data structures are used for:
  - Metro line entity.
  - Line name.
  - Line color.
  - Stop sequence.
  - Stop position.
- `docs/DEV_NOTES.md` has a section called `CS2 Metro Data Discovery`.
- The exporter has enough information to attempt real export.

## Important rule

Do not scatter game-specific type names across the project. Keep them inside `MetroDiagram.Exporter` services.

Recommended service names:

```text
Cs2MetroNetworkReader
Cs2EntityDebugDumper
Cs2TransportLineMapper
```

---

# Phase 6 — Real Metro Export

## Purpose

Export real metro network data from the currently loaded city.

## Goals

- Export real metro lines.
- Export stops in correct order.
- Export station positions.
- Export names and colors when available.
- Produce a viewer-compatible `metro.json`.

## Tasks

1. Implement `Cs2MetroNetworkReader`.
2. Query public transport lines.
3. Filter to metro / subway mode.
4. Map game entities to `MetroLine` DTOs.
5. Map stops to `MetroStation` DTOs.
6. Preserve stop order.
7. Export line colors.
8. Export station coordinates.
9. Add fallbacks for missing data.
10. Write final `metro.json`.
11. Test with several cities:
    - No metro.
    - One simple line.
    - Two lines with interchange.
    - Branching line.
    - Large network.

## Definition of Done

- A real city with metro lines exports a valid `metro.json`.
- Viewer can open the exported file.
- SVG shows the correct number of lines.
- SVG shows stations in the right sequence.
- Empty metro networks do not crash.

## Fallback policy

Real game data can be messy. Codex must implement defensive fallbacks:

```text
- If line color is missing, assign palette color.
- If station name is missing, generate station name.
- If position is missing, approximate from line order.
- If a stop cannot be resolved, skip it and log warning.
- If a line has fewer than 2 valid stops, export it but mark warning or skip rendering.
```

---

# Phase 7 — Interchange Detection

## Purpose

Make diagrams readable by detecting shared or nearby stations used by multiple lines.

## Goals

- Identify obvious interchanges.
- Avoid duplicate station markers for the same interchange.
- Render interchanges clearly.

## Detection priority

Use these rules in order:

1. Same station entity used by multiple lines.
2. Same building / station group.
3. Same or normalized station name.
4. Coordinates within a threshold.
5. Manual override in future versions.

## Tasks

1. Add interchange detection in Core or Exporter.
2. Prefer Core if detection can be done from exported data.
3. Mark `isInterchange = true` when station belongs to multiple lines.
4. Merge station display where appropriate.
5. Add tests for interchange sample.

## Definition of Done

- Two lines sharing a station render one interchange marker.
- Two nearby station stops can be grouped if threshold mode is enabled.
- False positives are minimized.
- Interchange detection can be disabled for debugging.

---

# Phase 8 — Style Presets

## Purpose

Make diagrams visually appealing and recognizable without copying official maps.

## Goals

- Add multiple style presets.
- Keep styles separate from data and layout.
- Allow future customization.

## Tasks

1. Define `IMetroStylePreset` or equivalent.
2. Implement `MinimalStyle`.
3. Add `HongKongInspiredStyle`.
4. Add `GuangzhouInspiredStyle`.
5. Add style selection in viewer.
6. Ensure all styles work with the same layout data.

## Preset principles

### Minimal

- Clean white background.
- Game line colors.
- Simple station dots.
- Basic legend.

### HongKongInspired

- Strong route colors.
- Compact labels.
- Clear interchange circles.
- Optional bilingual label support later.
- Optional simplified water/background shapes later.

### GuangzhouInspired

- Strong line-number identity.
- Large network readability.
- Clear legend.
- More structured spacing.
- Optional terminal emphasis later.

## Definition of Done

- Viewer can switch styles.
- Same JSON can produce different SVG visual styles.
- No official logo, map, or copyrighted graphic is used.

---

# Phase 9 — User-friendly Viewer

## Purpose

Make the tool usable by normal players, not only developers.

## Goals

- Simple file opening.
- Basic preview.
- Export SVG.
- Display network summary.
- Display warnings.

## UI requirements

The viewer should show:

```text
- City name.
- Export date.
- Number of lines.
- Number of stations.
- Number of interchanges.
- Style preset selector.
- Layout mode selector.
- Export SVG button.
- Warning list.
```

## Tasks

1. Create GUI if only CLI exists.
2. Add file picker.
3. Add SVG preview using WebView2 or another suitable preview method.
4. Add style selector.
5. Add export button.
6. Add warning display.
7. Add recent files if easy.

## Definition of Done

- A non-technical user can open `metro.json` and export SVG without command-line usage.
- Errors are understandable.
- Viewer does not crash on invalid files.

---

# Phase 10 — Packaging and Release

## Purpose

Prepare the project for real users.

## Goals

- Package exporter mod.
- Package viewer.
- Provide clear install instructions.
- Provide clear usage instructions.
- Provide known limitations.

## Tasks

1. Create release build configuration.
2. Package `MetroDiagram.Exporter` for CS2 mod installation.
3. Package viewer as Windows x64 app.
4. Include sample files.
5. Write `docs/USER_GUIDE.md`.
6. Write `docs/KNOWN_LIMITATIONS.md`.
7. Add screenshots or sample output.
8. Add version number.
9. Add changelog.

## Definition of Done

- A user can download the release package.
- A user can install the exporter.
- A user can run the viewer.
- A user can export a metro diagram from a real city.
- README explains the whole workflow.

---

## 8. Project Management Rules for Codex

### 8.1 Always know the current phase

Codex must maintain `docs/PROJECT_STATE.md` and update it when:

- A phase starts.
- A phase is completed.
- A blocker appears.
- A design decision is made.
- A task is postponed.

### 8.2 Do not skip definitions of done

A phase is not complete until its definition of done is satisfied.

If a definition of done is unrealistic, Codex should update the plan with a reason in `DECISION_LOG.md` rather than silently skipping it.

### 8.3 Prefer small working increments

Each commit or work unit should produce something testable:

```text
Good: sample JSON can now render a line.
Good: export button now writes static JSON.
Good: viewer now reports missing stations.
Bad: large rewrite with no visible behavior change.
```

### 8.4 Keep game code isolated

All Cities: Skylines II-specific code should stay inside `MetroDiagram.Exporter`.

Core, Layout, Rendering, and Tests should not reference game assemblies.

This makes it possible to test most of the project without launching the game.

### 8.5 Log all uncertainty

If Codex is unsure about a CS2 internal component or behavior, it must:

1. Add a note in `docs/DEV_NOTES.md`.
2. Add a debug dump if possible.
3. Avoid building assumptions into shared DTOs.

### 8.6 Keep the file format stable

Breaking changes to `metro.json` require:

1. Incrementing `schemaVersion`.
2. Updating `docs/JSON_SCHEMA.md`.
3. Updating sample files.
4. Updating tests.

### 8.7 Maintain sample files

Every new renderer/layout feature should be tested against sample JSON files.

Minimum samples:

```text
sample-metro-small.json          One line, few stations.
sample-metro-interchange.json    Two lines sharing a station.
sample-metro-branch.json         One branching line or service.
sample-metro-loop.json           Loop line.
sample-metro-empty.json          No metro lines.
```

### 8.8 Use tests as project memory

Whenever a bug is fixed, add a test or sample that reproduces it.

Good tests for this project:

```text
- JSON can deserialize.
- Missing optional fields do not crash.
- Line references valid station IDs.
- SVG contains expected station names.
- Interchange station is marked correctly.
- Empty network produces valid empty SVG.
```

---

## 9. Suggested Issue Breakdown

Codex can create or follow these tasks:

```text
#001 Create repository structure and documentation files
#002 Add DEVELOPMENT_PLAN.md and PROJECT_STATE.md
#003 Create solution and core projects
#004 Define metro export DTOs
#005 Add sample JSON files
#006 Implement JSON loader and validation
#007 Implement basic SVG renderer
#008 Add CLI conversion from JSON to SVG
#009 Add tests for sample JSON rendering
#010 Add basic layout normalization
#011 Add grid snapping option
#012 Create minimal desktop viewer shell
#013 Add viewer file open and export flow
#014 Create CS2 exporter mod shell
#015 Add exporter settings and export button
#016 Export static sample JSON from game
#017 Add debug dump mode for CS2 entities
#018 Discover metro route components
#019 Implement real metro line export
#020 Implement station and stop sequence export
#021 Implement line color and station name export
#022 Implement interchange detection
#023 Connect real exported JSON to viewer
#024 Add style preset infrastructure
#025 Add HongKongInspired style
#026 Add GuangzhouInspired style
#027 Add packaging scripts
#028 Write user guide and known limitations
#029 Prepare first public release
```

---

## 10. Coding Standards

### 10.1 General

- Use clear names.
- Prefer simple classes over clever abstractions.
- Avoid global mutable state.
- Keep DTOs serializable and plain.
- Avoid game dependencies outside Exporter.
- Use nullable annotations if available.
- Validate external input.

### 10.2 Error handling

All file operations must handle exceptions.

Common cases to handle:

```text
- File not found.
- Access denied.
- Invalid JSON.
- Unsupported schema version.
- Missing stations.
- Lines with fewer than two stops.
- Duplicate IDs.
- Export path not writable.
```

### 10.3 Logging

Exporter should log:

```text
- Export started.
- Export path.
- Number of lines found.
- Number of stations found.
- Number of warnings.
- Export success or failure.
```

Viewer should show:

```text
- Parse errors.
- Validation warnings.
- Render warnings.
```

### 10.4 Versioning

Use semantic versioning:

```text
0.1.0 = first working JSON-to-SVG pipeline
0.2.0 = in-game static exporter works
0.3.0 = real metro data export works
0.4.0 = basic viewer works
0.5.0 = interchange and style presets improve output
1.0.0 = stable user-facing release
```

---

## 11. First Development Command for Codex

Codex should begin with this exact focus:

```text
Start Phase 0 and Phase 1 only.

Create the repository structure, documentation files, Core DTOs, sample JSON files, JSON loader, and a Minimal SVG renderer. Do not implement the Cities: Skylines II exporter yet. The first milestone is: sample-metro-small.json can be converted into output.svg through a CLI or simple viewer.

After completing the milestone, update docs/PROJECT_STATE.md with completed tasks, remaining tasks, and any design decisions.
```

---

## 12. First Milestone Acceptance Test

The first milestone is accepted when the following works:

```text
Input:
  samples/sample-metro-small.json

Command or UI action:
  Generate SVG

Output:
  output.svg

Expected result:
  SVG file opens in browser or Inkscape.
  It shows a title, at least one metro line, station dots, station names, and a legend.
```

If this is not working, do not move to CS2 integration.

---

## 13. Long-term Roadmap

After v0.1, future improvements may include:

```text
- Better schematic layout.
- Manual layout override JSON.
- Drag-and-drop viewer editor.
- PNG/PDF export.
- Bilingual station labels.
- Support for trains, trams, buses, ferries.
- District or water background hints.
- Line terminal labels.
- Station code labels.
- Multiple diagram sizes.
- Community style presets.
```

These are not required for the first working version.

---

## 14. Final Reminder for Codex

The project should always move in this order:

```text
1. Make sample data render.
2. Make viewer usable.
3. Make exporter write static data.
4. Discover real CS2 metro data.
5. Export real data.
6. Improve layout.
7. Improve style.
8. Package for users.
```

Do not reverse this order.

A beautiful map is the final reward. A reliable data pipeline is the foundation.
