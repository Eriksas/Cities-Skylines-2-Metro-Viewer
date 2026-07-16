# Known Issues - v0.1.0-beta.4

This is a beta build. It is intended for testing and feedback, not production use.

## Current Limitations

- Only metro/subway networks are supported.
- Offline save parsing is not supported. You must load a city and export from inside Cities: Skylines II.
- Legacy `schematic-lite` has been removed from the toolchain. Historical outputs remain in old validation bundles and git history.
- `schematic-v2` is experimental. It is a topology-first diagnostic base intended to preserve stop order, adjacency, interchange nodes, exact shared station-edge corridors, and pathPoints-based physical corridor hints before visual polish. It is not the alpha default.
- `schematic-anneal` is the default product layout. It has the strongest current corpus metrics, but still needs broader multi-city visual validation; deterministic optimization does not eliminate the need for human review.
- `schematic-map` is the previous product-facing pass-stack mode. It is retained for comparison and targeted diagnostics rather than as the default.
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
- `schematic-anneal` is the recommended alpha output. Geographic rendering remains the faithful-geometry fallback and regression reference.
- Product-map header, legend, station hierarchy, and important label hierarchy have improved in Phase 5C.2, but still need future manual cartographic review against more cities and official-style references.
- Transit-map route badges use conservative collision avoidance. In crowded endpoints a route badge may be omitted rather than overlapping station labels or other badges.
- Station names or city names may fall back when CS2 display-name data is unavailable.
- Some geographic route runs may still have small visual discontinuities from route-run fragmentation or near-touching fragments.
- Shared corridor rendering is experimental and disabled by default. It can be useful for comparison but is not recommended as the alpha default.
- Express center stripe rendering is experimental and disabled by default. It can be useful for comparison but is not recommended as the alpha default.
- Multi-city validation is still limited. The automated corpus currently includes synthetic samples and two real cities, but no city may be special-cased in code.
- Real export snapshot naming is available, but if the exporter cannot resolve a real city name yet the snapshot filename falls back to `UnnamedCity`.
- Interchange/station grouping may be imperfect when CS2 data uses unexpected station ownership or access-restriction structures.
- The game mod does not launch the Viewer.
- The in-game schematic uses a portable renderer that now runs the same layout
  math as desktop `schematic-anneal` (cost weights, clearance exemptions,
  parallel shared corridors, fit-to-frame). It still lacks some desktop
  cartographic furniture: label side-scoring parity, route badges, express
  stripes, and marker hierarchy.
- The Viewer is a local Windows app and remains the preferred surface for the
  most polished schematic output and manual editing.
- No Hong Kong, Guangzhou, or other style presets are included yet.
- The Viewer has basic manual edit support for map cleanup, but it is not a full schematic map editor yet.

## Troubleshooting Notes

- If `Open Default Export` is disabled, confirm that `metro-export.json` exists
  under `D:\CS2MetroDiagram`, `Documents\CS2MetroDiagram`, or
  `Desktop\CS2MetroDiagram`. For any other custom export folder, use Viewer
  `Open JSON`.
- Real export snapshots are written under the `exports` subdirectory next to the latest files. Use Viewer `Open JSON` to open a snapshot manually; `Open Default Export` continues to open only the latest file.
- If the diagram is too crowded, start with `schematic-anneal`, a larger size, a smaller label font size, disabling `Show default / non-important station labels`, and enabling `Hide crowded labels`. Use `geographic`, `schematic-map`, or `schematic-v2` as comparison views when diagnosing a layout issue.
- If a player intentionally uses the same custom name for nearby separate platforms or virtual interchange stations, enable `Show virtual transfer hints` in the Viewer or pass `--enable-virtual-transfer-hints` in the CLI to show dashed connectors.
- For alpha feedback, review `schematic-anneal` first and include a geographic render when the reported problem may come from exported geometry rather than schematic layout.
- When reviewing `schematic-v2`, attach `artifacts\schematic-v2-diagnostics\geometry-shared-corridors.txt`, `schematic-v2-route-guides.txt`, and `schematic-v2-parallel-corridors.txt` if a physical shared corridor or skip-stop / express service looks wrong.
- If export fails in-game, attach `metro-export-diagnostics.txt` and the game/mod log when reporting the issue.
