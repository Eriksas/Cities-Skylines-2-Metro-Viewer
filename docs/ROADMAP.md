# Roadmap

This roadmap is intentionally lightweight and may change after alpha feedback.

## v0.1-alpha: Real Export + Viewer

- Real CS2 metro/subway JSON export.
- Debug dump for transport data investigation.
- CLI JSON to SVG generation.
- `geographic` layout and historical CLI-only `schematic-lite` comparison output.
- Experimental topology-first `schematic-v2` layout.
- WPF Viewer with basic render controls.
- English/Chinese Viewer UI.
- Label filtering.
- Alpha release packaging.

## v0.2: Image Export And Style Presets

- PNG export.
- Basic style presets.
- Better release packaging and user docs.
- Improve feedback and diagnostics workflow.

## v0.3: Better Schematic Layout And Manual Overrides

- Rebuild schematic layout around a topology/corridor-first model rather than continuing schematic-lite patches.
- Preserve shared corridors, express-service semantics, stop order, and interchange topology before visual styling.
- Better label placement and label hiding controls.
- Manual station/label/layout overrides if feasible.
- Persist viewer-side layout adjustments without changing raw export data.

## v0.4: Broader Transit Support If Feasible

- Investigate support for other public transport modes.
- Decide whether non-metro modes belong in this tool or a separate workflow.
- Keep metro/subway behavior stable while experimenting.

## Future Ideas

- More export formats.
- Better installer/distribution story.
- Optional richer Viewer preview technology.
- More robust station grouping diagnostics.
- Community style packs after core behavior is stable.
