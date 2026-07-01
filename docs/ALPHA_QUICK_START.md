# CS2 Metro Diagram v0.1.0-alpha.2-candidate Quick Start

This is an alpha build for testing. It is not a stable release.

## What Is Included

- A Cities: Skylines II mod that exports metro/subway data to JSON.
- A local Windows Viewer that opens the exported JSON, previews SVG, switches layout mode, adjusts basic render options, and saves SVG.
- Sample JSON files for testing without the game.

## Install The CS2 Mod

1. Copy the packaged `Mod\CS2 Metro` folder into the Cities: Skylines II local mods folder used by your game setup.
2. Start Cities: Skylines II.
3. Enable `CS2 Metro Diagram` in the game's mod list.
4. Load or create a city before exporting real metro data.

The exact mod folder can vary by CS2 installation and local modding setup. If you built the project locally, the current local mod artifact is usually under:

```text
artifacts\cs2-local-mods\CS2 Metro
```

## Export Real Metro JSON

1. Open a city in Cities: Skylines II.
2. Open `Options > CS2 Metro Diagram > Main`.
3. Optional: in `Export Folder`, paste a full folder path or click one of the
   recommended presets:
   - `Use Documents folder` -> `Documents\CS2MetroDiagram`
   - `Use Desktop folder` -> `Desktop\CS2MetroDiagram`
   - `Use D:\CS2MetroDiagram`
4. Open the `Export` group and click `Export Real Metro JSON`.
5. Check the game/mod log if export fails.

The latest real export path is:

```text
<export folder>\metro-export.json
```

The Viewer default button checks the common locations:

```text
D:\CS2MetroDiagram\metro-export.json
Documents\CS2MetroDiagram\metro-export.json
Desktop\CS2MetroDiagram\metro-export.json
```

Diagnostics are written next to the export:

```text
<export folder>\metro-export-diagnostics.txt
```

Each real export also writes timestamped snapshots under:

```text
<export folder>\exports\
```

`Open Default Export` opens the latest `metro-export.json` from a common
location; use `Open JSON` to open a custom-folder export or a snapshot manually.

## Start The Viewer

1. Open the release package.
2. Go to the `Viewer` folder.
3. Double-click `MetroDiagram.Viewer.exe`.

## Open The Export

1. Click `Open Default Export` if it is enabled.
2. If it is disabled, click `Open JSON`.
3. Select `D:\CS2MetroDiagram\metro-export.json`, a custom-folder export, or another metro export JSON.

## Switch Layout

Use the `Layout` dropdown:

- `geographic`: normalized source coordinate rendering.
- `schematic-map`: experimental product-facing schematic map style.
- `schematic-v2`: experimental topology/diagnostic schematic layout.

Click `Refresh Preview` after changing numeric settings.

## Save SVG

1. Click `Save SVG`.
2. Choose an output path.
3. Open the saved `.svg` in a browser or vector editor.

## Sample Files

The release package includes sample JSON files under `samples`. They can be opened directly in the Viewer without launching Cities: Skylines II.
