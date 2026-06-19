# Changelog

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
