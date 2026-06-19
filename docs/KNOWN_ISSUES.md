# Known Issues - v0.1.0-alpha.2-candidate

This is an alpha build. It is intended for testing and feedback, not production use.

## Current Limitations

- Only metro/subway networks are supported.
- Offline save parsing is not supported. You must load a city and export from inside Cities: Skylines II.
- `schematic-lite` is not a professional-grade automatic schematic layout. It is now kept as a CLI/regression comparison mode and is no longer exposed in the Viewer.
- `schematic-v2` is experimental. It is a topology-first diagnostic base intended to preserve stop order, adjacency, interchange nodes, exact shared station-edge corridors, and pathPoints-based physical corridor hints before visual polish. It is not the alpha default.
- `schematic-map` is the newer product-facing schematic mode. It builds on schematic-v2 and applies the transit-map presentation defaults, but still needs broader multi-city validation before replacing the geographic alpha recommendation.
- `schematic-map` currently renders non-station route crossings as direct pass-through intersections. The exported JSON does not yet include real CS2 track elevation or layer order, so factual over/under styling is not available.
- `schematic-map` has an opt-in synthetic-bend experiment for long locked segments, but it is disabled by default because the first real-city review looked less natural despite fewer route-angle warnings.
- `schematic-map` has a score/audit workflow for octilinear grammar, crossings, turns, badges, and stroke widths, but the score is only a comparison aid. Manual visual review remains required.
- In schematic-v2, express / rapid / skip-stop variants are simplified into service metadata. Only one canonical family route is drawn; white center stripes indicate that a family contains express service. Exact skip-stop stopping patterns are not drawn as independent schematic geometry.
- In schematic-v2, exact same final-route-chain segments are protected from draw-order hiding with explicit `exact-shared-platform` overlays for all involved display families. Near-parallel or close-but-not-identical routes are not guaranteed to be detected as shared; if a future city shows hiding without exact segment sharing, attach the SVG and schematic-v2 diagnostics for a new corridor-detection pass.
- Virtual transfer hints are opt-in and heuristic. They only connect nearby same-name stations that look player-named; repeated default CS2 asset names such as `现代地铁站` or `小型地铁广场` are ignored.
- `schematic-v2` route guide reconstruction and materialized parallel corridor rendering are still experimental. The primary `10号线` / `2号线` case now renders as a visible continuous parallel corridor in the service-simplified output, but schematic-v2 still needs broader city validation and should not block the geographic alpha baseline.
- `schematic-v2` may still require canonical route-chain normalization for unusual loop, branch, or bidirectional service exports. Raw exporter data is preserved; normalization is render-only.
- `schematic-v2` now uses a canonical internal layout space before scaling to the requested output size. If different size presets still change topology or shared-corridor materialization, report it as a renderer bug with the SVG and diagnostics attached.
- Station labels can still be crowded, especially in dense city centers.
- Geographic baseline rendering is currently the recommended alpha output. The accepted alpha.2 candidate baseline is `artifacts\primary-city-baseline\latest\baseline-geographic.full.png`.
- Legend and title polish remain limited and may need future refinement.
- Transit-map route badges use conservative collision avoidance. In crowded endpoints a route badge may be omitted rather than overlapping station labels or other badges.
- Station names or city names may fall back when CS2 display-name data is unavailable.
- Some geographic route runs may still have small visual discontinuities from route-run fragmentation or near-touching fragments.
- Shared corridor rendering is experimental and disabled by default. It can be useful for comparison but is not recommended as the alpha default.
- Express center stripe rendering is experimental and disabled by default. It can be useful for comparison but is not recommended as the alpha default.
- Multi-city validation is still limited; the current visual baseline is based on one primary city and must not be special-cased in code.
- Real export snapshot naming is available, but if the exporter cannot resolve a real city name yet the snapshot filename falls back to `UnnamedCity`.
- Interchange/station grouping may be imperfect when CS2 data uses unexpected station ownership or access-restriction structures.
- PNG and PDF export are not supported.
- The game mod does not launch the Viewer.
- The Viewer is a local Windows app, not an in-game preview.
- No Hong Kong, Guangzhou, or other style presets are included yet.
- No drag editing or manual label placement exists yet.

## Troubleshooting Notes

- If `Open Default Export` is disabled, confirm that `metro-export.json` exists under `D:\CS2MetroDiagram` or `Documents\CS2MetroDiagram`.
- Real export snapshots are written under the `exports` subdirectory next to the latest files. Use Viewer `Open JSON` to open a snapshot manually; `Open Default Export` continues to open only the latest file.
- If the diagram is too crowded, try `schematic-map` or `schematic-v2`, larger width/height, smaller label font size, disabling `Show default / non-important station labels`, and enabling `Hide crowded labels`.
- If a player intentionally uses the same custom name for nearby separate platforms or virtual interchange stations, enable `Show virtual transfer hints` in the Viewer or pass `--enable-virtual-transfer-hints` in the CLI to show dashed connectors.
- For alpha feedback, prioritize the geographic normalized baseline before judging experimental shared corridor or express stripe outputs.
- When reviewing `schematic-v2`, attach `artifacts\schematic-v2-diagnostics\geometry-shared-corridors.txt`, `schematic-v2-route-guides.txt`, and `schematic-v2-parallel-corridors.txt` if a physical shared corridor or skip-stop / express service looks wrong.
- If export fails in-game, attach `metro-export-diagnostics.txt` and the game/mod log when reporting the issue.
