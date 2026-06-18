# CS2 Metro Diagram Viewer Quick Start

Version: `v0.1.0-alpha.2-candidate`

This is an alpha build, not a stable release.

## Run

Double-click `MetroDiagram.Viewer.exe`.

## Open JSON

Click `Open Default Export` if it is enabled, or click `Open JSON` and choose a metro JSON file.

Real CS2 exports are checked in this order:

```text
D:\CS2MetroDiagram\metro-export.json
Documents\CS2MetroDiagram\metro-export.json
```

A sample file is included in the `samples` folder.

`Open Export Folder` opens the export folder so you can find JSON and saved SVG files.

Latest real exports remain `metro-export.json`. Timestamped snapshots are written under the `exports` subdirectory and can be opened manually with `Open JSON`.

## Preview

The Viewer renders the SVG preview inside the app using the same renderer as the CLI. After you open a JSON file, the app switches to `Map Preview` and shows the generated metro diagram directly; you do not need to save the SVG first or open it manually in a browser.

Use `Preview` to choose the in-app preview scale. `100%` shows the SVG at its actual rendered pixel size, based on the SVG root `width` and `height`, and lets you scroll around large maps. `Fit width` gives an overview by scaling the whole SVG down to the preview pane width.

Layout modes:

- `geographic`: keeps normalized source coordinate geometry.
- `schematic-v2`: experimental topology-first schematic layout for validation and diagnostics.
- `schematic-map`: product-facing official-map style schematic output built on `schematic-v2`, with transit-map framing and service/express visual defaults.

The older `schematic-lite` renderer remains available from the CLI for regression comparison, but it is no longer exposed in the Viewer because it is not the target schematic-map direction.

The Viewer has two main tabs:

- `Map Preview`: renders the selected JSON as SVG.
- `Export Data`: shows schema/generator/game versions, export time, line and station counts, per-line stop/pathPoint details, per-station membership, warnings for stale exports or placeholder city names, and matching diagnostics-file status.

If a matching diagnostics file is found, click `Open Diagnostics` from the `Export Data` tab to open it.

The Windows executable includes a CS2 Metro Diagram app icon.

## Adjust

Change width, height, legend width, padding, line width, station radius, label font size, or grid size, then click `Refresh Preview`.

Use the label checkboxes to tune clutter:

- `Show default / non-important station labels`
- `Hide crowded labels`
- `Always show interchanges`
- `Always show terminals`

Use `Language` to switch the Viewer UI between English and Chinese. Viewer settings are saved to:

```text
Documents\CS2MetroDiagram\viewer-settings.json
```

`Reset Defaults` restores render and label defaults while keeping the current language.

## Save

Click `Save SVG` and choose a destination path. The saved SVG can be opened in a browser.
