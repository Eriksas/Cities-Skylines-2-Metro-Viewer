# CS2 Metro Diagram

CS2 Metro Diagram is an alpha-stage toolchain for exporting metro networks from
Cities: Skylines II and turning them into readable SVG metro diagrams.

The project contains:

- a Cities: Skylines II code mod that exports metro data as JSON,
- a local Windows Viewer for opening JSON exports and previewing SVG diagrams,
- a command-line renderer for batch generation and diagnostics,
- sample JSON files and lightweight tests for offline development.

Current version: `v0.1.0-alpha.2-candidate`

> This is alpha software. Back up your saves before testing any mod. The mod is
> designed to export data only and should not alter city data.

## Current Status

`schematic-anneal` is the default product-facing schematic mode. It lays out
the schematic with a single global quality cost and deterministic simulated
annealing instead of stacked local repair passes, and it won every layout
metric (octilinearity, bends, crossings, spacing, clearance, weighted cost) on
both median and worst case across the current corpus of samples plus real-city
exports. It is the default selection in the Viewer.

```text
schematic-anneal (default) — global-optimization octilinear schematic
```

`geographic` remains available as the most faithful render of the exported CS2
route geometry (`geographic + exported path geometry + service family merge`);
use it when you want true geometry rather than a schematic.

`schematic-map` is the previous product-facing schematic (a stack of render-time
route-grammar repair passes); it is retained for comparison until
`schematic-anneal` has broader multi-city validation. `schematic-v2` remains a
topology/diagnostic schematic mode.

Compare the two schematic directions over the whole sample corpus with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-schematic-layouts.ps1
```

Every CLI render can also emit objective layout metrics via
`--emit-layout-score path.csv`.

Near-term work is focused on alpha.2 validation bundles and small, evidence-led
schematic-map fixes. Larger items such as smaller Viewer packages, PNG export,
style presets, and manual editing are tracked in [docs/ROADMAP.md](docs/ROADMAP.md).

Legacy `schematic-lite` has been removed from the toolchain; its history
remains available in git.

## What Works Today

- Export real CS2 metro/subway lines to JSON.
- Preserve latest export and timestamped export snapshots.
- Render SVG diagrams from real or sample JSON.
- Preview SVG inside the Windows Viewer.
- Switch between `geographic`, `schematic-v2`, and `schematic-map`.
- Inspect export data in the Viewer.
- Save SVG output.
- Hide default/generic station names.
- Keep important interchanges and terminal labels visible.
- Generate alpha validation bundles for real-city feedback.

## Quick Start For Testers

1. Install or deploy the CS2 mod.
2. Launch Cities: Skylines II and load a city.
3. Open the mod options page:

   ```text
   Options > CS2 Metro Diagram > Main
   ```

4. Optional: under `Export Folder`, paste a full folder path or click one of
   the presets:

   ```text
   Documents\CS2MetroDiagram
   Desktop\CS2MetroDiagram
   D:\CS2MetroDiagram
   ```

5. Under `Export`, click `Export Real Metro JSON`.
6. The latest export is written to the selected export folder:

   ```text
   <export folder>\metro-export.json
   <export folder>\metro-export-diagnostics.txt
   ```

7. Each export also creates timestamped snapshots under:

   ```text
   <export folder>\exports\
   ```

8. Open `MetroDiagram.Viewer.exe`.
9. Click `Open Default Export`, or use `Open JSON` to load a snapshot or a
   custom-folder export.
10. Preview the map in the app and click `Save SVG` when needed.

For a fuller tester guide, see [docs/ALPHA_QUICK_START.md](docs/ALPHA_QUICK_START.md).

## Viewer

Development run:

```powershell
dotnet run --project src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
```

Publish a self-contained Windows x64 Viewer:

```powershell
scripts\publish-viewer-self-contained.ps1
```

The published package is written to:

```text
artifacts\viewer-win-x64-self-contained
```

The Viewer supports:

- in-app SVG preview,
- Chinese and English UI,
- layout selection,
- render size presets,
- label filtering,
- export data inspection,
- diagnostics-file detection,
- SVG saving.

The in-app preview uses Microsoft WebView2 instead of the legacy WPF
`WebBrowser`/Internet Explorer control, so it should not show local-file active
content security prompts. If the Viewer does not start on a clean Windows
machine, install the Microsoft Edge WebView2 Runtime.

## Command Line Usage

Render a sample JSON:

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  samples\sample-metro-small.json output.svg
```

Render a real export with the recommended geographic settings:

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  D:\CS2MetroDiagram\metro-export.json output.svg `
  --layout geographic `
  --size poster `
  --use-path-points `
  --hide-generic-labels `
  --hide-crowded-labels
```

Render the experimental product-facing schematic map:

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  D:\CS2MetroDiagram\metro-export.json schematic-map.svg `
  --layout schematic-map `
  --size poster `
  --hide-generic-labels `
  --hide-crowded-labels
```

Optional render-time layout overrides can be supplied as a sidecar file:

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  D:\CS2MetroDiagram\exports\metro-export-mycity-20260624-123456.json output.svg `
  --layout schematic-map `
  --overrides D:\CS2MetroDiagram\exports\metro-export-mycity-20260624-123456.layout-overrides.json
```

The Viewer also auto-loads a sidecar next to the opened JSON when it follows the
same naming pattern. This enables safe manual map edits without modifying the
exported JSON.

In the Viewer, enable `Manual edit` to adjust the rendered map directly:

- choose `Stations` and drag station circles to move station render anchors and
  the connected route geometry;
- choose `Labels` and drag station labels to move text without moving the
  station or route;
- use `Hide Label` / `Show Label`, `Reset Selected`, `Clear Edits`, and
  `Open Edit File` to manage the sidecar.

Manual edits are saved into the sidecar and re-applied on the next render.

## Alpha Validation Bundles

For real feedback, generate a bundle instead of sending loose screenshots:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName my-city
```

Refresh the validation index:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-alpha-validation-bundles.ps1
```

Outputs:

```text
artifacts\alpha-validation\<timestamp>-<caseName>\
artifacts\alpha-validation\index.md
artifacts\alpha-validation\index.csv
```

Bundles include JSON, diagnostics, SVG/PNG renders, notes, and a filled feedback
template.

## Schematic Regression Gate

Before accepting schematic-map renderer changes, run the multi-case regression gate:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1
```

Outputs:

```text
artifacts\schematic-regression\<timestamp>\index.md
artifacts\schematic-regression\<timestamp>\regression-summary.csv
```

The gate renders real exports and regression samples, then records schematic-map
scores, crossings, badge conflicts, and stroke-width consistency. It is a safety
check, not a replacement for visual review.

## Build And Test

Run the local preflight check:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1
```

This restores the offline solution, builds it, and runs the renderer test
project. To remove generated `bin`/`obj` output afterward:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1 -CleanBuildOutput
```

Equivalent manual commands:

```powershell
dotnet restore CS2MetroDiagram.slnx
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

GitHub Actions also runs `scripts\preflight.ps1` on pushes and pull requests to
`master`.

The CS2 mod project requires local Cities: Skylines II modding toolchain paths.
See [docs/DEV_NOTES.md](docs/DEV_NOTES.md) for the local build/deploy command
shape used during development.

## Project Layout

```text
CS2 Metro\                 CS2 code mod project
src\MetroDiagram.Core\      export document model and loader
src\MetroDiagram.Rendering\ SVG rendering and layout modes
src\MetroDiagram.Cli\       command-line renderer
src\MetroDiagram.Viewer\    Windows Viewer app
src\MetroDiagram.Tests\     lightweight console test runner
samples\                    sample JSON exports
scripts\                    build, publish, diagnostics, validation scripts
docs\                       current docs and archived phase notes
```

## Known Limitations

- Only metro/subway is supported.
- No offline save parsing; export from a loaded city.
- No PNG/PDF product export yet.
- No in-game SVG preview.
- Manual map edits are render-time sidecar overrides. They are useful for
  polishing a generated map, but they are not written back to CS2 or to
  `metro-export.json`.
- `schematic-map` and `schematic-v2` are experimental.
- Multi-city validation is still ongoing.

See [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) before reporting bugs.

## Documentation

- [docs/README.md](docs/README.md) - documentation index
- [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) - current project state
- [docs/ALPHA_TEST_PLAN.md](docs/ALPHA_TEST_PLAN.md) - validation workflow
- [docs/FEEDBACK_TEMPLATE.md](docs/FEEDBACK_TEMPLATE.md) - issue report template
- [docs/JSON_SCHEMA.md](docs/JSON_SCHEMA.md) - export JSON format
- [docs/CHANGELOG.md](docs/CHANGELOG.md) - release notes

## License

See [LICENSE.txt](LICENSE.txt).
