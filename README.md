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

The recommended alpha output is still the geographic renderer:

```text
geographic + exported path geometry + service family merge
```

The newer `schematic-map` mode is the product-facing schematic direction. It is
being developed toward a cleaner official metro-map style, but it is still
experimental and should be reviewed through validation bundles.

`schematic-v2` remains available as a topology/diagnostic schematic mode.

Near-term work is focused on alpha.2 validation bundles and small, evidence-led
schematic-map fixes. Larger items such as smaller Viewer packages, PNG export,
style presets, and manual editing are tracked in [docs/ROADMAP.md](docs/ROADMAP.md).

Legacy `schematic-lite` is no longer exposed in the Viewer, but remains in the
CLI for historical comparison.

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
   Options > CS2 Metro Diagram > Main > Export > Export Real Metro JSON
   ```

4. The latest export is written to:

   ```text
   D:\CS2MetroDiagram\metro-export.json
   D:\CS2MetroDiagram\metro-export-diagnostics.txt
   ```

5. Each export also creates timestamped snapshots under:

   ```text
   D:\CS2MetroDiagram\exports\
   ```

6. Open `MetroDiagram.Viewer.exe`.
7. Click `Open Default Export`, or use `Open JSON` to load a snapshot.
8. Preview the map in the app and click `Save SVG` when needed.

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

## Build And Test

Build the offline solution:

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
```

Run tests:

```powershell
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

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
- No drag editor or manual layout override.
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
