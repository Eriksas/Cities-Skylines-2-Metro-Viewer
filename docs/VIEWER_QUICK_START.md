# CS2 Metro Diagram Viewer Quick Start

Version: `v0.1.0-alpha.5`

This is an alpha build, not a stable release.

## Run

Double-click `MetroDiagram.Viewer.exe`.

The preview pane uses Microsoft WebView2. Current Windows installs usually have
the WebView2 Runtime already. If the Viewer opens but cannot show the preview,
install the Microsoft Edge WebView2 Runtime and restart the Viewer.

## Open JSON

Click `Open Default Export` if it is enabled, or click `Open JSON` and choose a metro JSON file.

Real CS2 exports are checked in this order:

```text
D:\CS2MetroDiagram\metro-export.json
Documents\CS2MetroDiagram\metro-export.json
Desktop\CS2MetroDiagram\metro-export.json
```

A sample file is included in the `samples` folder.

`Open Export Folder` opens the export folder so you can find JSON and saved SVG files.

Latest real exports remain `metro-export.json`. Timestamped snapshots are written under the `exports` subdirectory and can be opened manually with `Open JSON`.
If the CS2 mod is configured to export to another custom folder, use `Open JSON`
and choose that folder manually.

If a sibling layout override sidecar exists, the Viewer loads it automatically:

```text
metro-export-City-20260624-123456.json
metro-export-City-20260624-123456.layout-overrides.json
```

The sidecar can nudge station render positions, move labels, and hide/show
individual labels without modifying the export JSON.

## Preview

The Viewer renders the SVG preview inside the app using WebView2 and the same
renderer as the CLI. After you open a JSON file, the app switches to `Map
Preview` and shows the generated metro diagram directly; you do not need to save
the SVG first or open it manually in a browser.

Use `Preview` to choose the in-app preview scale. `100%` shows the SVG at its actual rendered pixel size, based on the SVG root `width` and `height`, and lets you scroll around large maps. `Fit width` gives an overview by scaling the whole SVG down to the preview pane width.

Layout modes:

- `geographic`: keeps normalized source coordinate geometry.
- `schematic-v2`: experimental topology-first schematic layout for validation and diagnostics.
- `schematic-map`: product-facing official-map style schematic output built on `schematic-v2`, with transit-map framing and service/express visual defaults.

The older `schematic-lite` renderer has been removed from the toolchain.

The Viewer has two main tabs:

- `Map Preview`: renders the selected JSON as SVG.
- `Export Data`: shows schema/generator/game versions, export time, line and station counts, per-line stop/pathPoint details, per-station membership, warnings for stale exports or placeholder city names, and matching diagnostics-file status.

If a matching diagnostics file is found, click `Open Diagnostics` from the `Export Data` tab to open it.

The Windows executable includes a CS2 Metro Diagram app icon.

## Manual Edit

Change width, height, legend width, padding, line width, station radius, label font size, or grid size, then click `Refresh Preview`.

To make a manual station adjustment:

1. Open a JSON export.
2. Enable `Manual edit`.
3. Choose `Stations`.
4. Drag a station circle in the map preview.
5. Release the mouse button.

Station dragging moves the station render anchor and the route geometry for that
render. It does not modify `metro-export.json`.

While dragging, the WebView2 preview updates the selected station, its label, and
nearby route endpoints directly in the SVG so movement feels immediate. When you
release the mouse button, the Viewer saves the sidecar override and performs a
short delayed full renderer refresh to reconcile labels, routes, and diagnostics.

To move a label:

1. Enable `Manual edit`.
2. Choose `Labels`.
3. Drag a station label in the map preview.
4. Release the mouse button.

Label dragging moves only the label text. The station marker and routes stay in
place.

To adjust a route segment:

1. Enable `Manual edit`.
2. Choose `Segments`.
3. Drag a visible route segment in the preview.
4. Release the mouse button.

Segment dragging moves the segment endpoint station anchors together. It is a
sidecar-only edit, so the source export JSON is unchanged. If the Viewer cannot
identify two nearby endpoint stations for the clicked segment, it will ignore the
drag rather than guessing and risking a topology edit.

Use `Align H` or `Align V` after selecting a station or segment:

- With a station selected, the station aligns horizontally or vertically to the
  nearest connected neighbor station.
- With a segment selected, the two endpoint station anchors align to a shared
  horizontal or vertical axis.

These controls are intended for small cartographic corrections such as making a
nearly horizontal or vertical route segment read cleanly.

Useful manual-edit buttons:

- `Hide Label` / `Show Label`: hide or restore the selected station label.
- `Reset Selected`: remove the saved edit for the selected station or label.
- `Clear Edits`: remove all station and label edits for the current JSON.
- `Open Edit File`: open or create the sidecar file in the system editor.

The Viewer saves these adjustments to the sibling layout override sidecar:

```text
metro-export-City-20260624-123456.layout-overrides.json
```

Disable `Manual edit` when you only want to pan or inspect the preview.

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
