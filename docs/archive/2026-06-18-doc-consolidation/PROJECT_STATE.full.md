# Project State

## Current Phase

Phase 5F.1 - Schematic-map Visual Baseline Polish

## Current Version

`v0.1.0-alpha.2-candidate`

This is an alpha release, not a stable release.

## Current State

Phase 5F starts the medium-term move from an experimental schematic-v2 debug layout toward a product-facing official-map style mode. A new renderer layout mode, `schematic-map`, has been added as the user-facing schematic direction. It is built on the schematic-v2 topology/corridor engine but applies the transit-map frame, exported route geometry, service family merge, and express-service center stripes by default. This keeps `geographic` as the alpha recommended baseline, keeps `schematic-v2` available as an experimental/diagnostic base, and leaves the legacy `schematic-lite` only for CLI historical comparison.

This is render-only. It does not modify the CS2 exporter, `metro-export.json` schema, raw `line.stops`, raw `line.pathPoints`, or the accepted geographic alpha baseline.

Phase 5F.1 adds a narrow visual baseline polish pass for `schematic-map`: product-mode station markers, label spacing, route-badge typography, route-badge collision buffers, and bottom key reserve are more map-like than the raw schematic-v2 diagnostics view. Follow-up passes now put available city names into the transit-map header, hide the duplicate tiny city subtitle in `schematic-map`, add compact service-variant notes to the bottom key, and make `generate-product-candidate-map.ps1` default to `schematic-map`. The goal is a cleaner official-map candidate without changing layout topology or any export data.

The latest `schematic-map` follow-up adds a product-style simple-run linearization pass. Ordinary non-interchange route runs can now be straightened into clean horizontal, vertical, or 45-degree schematic strokes with more even station spacing. A station-spacing hierarchy preserves genuinely long station gaps instead of flattening every segment to the exact same interval, so the map can be abstract without losing airport/suburban distance cues. Interchanges, high-degree stations, shared-corridor anchors, exporter data, JSON schema, raw `line.stops`, raw `line.pathPoints`, and `geographic` output remain unchanged. This is intentionally more abstract than `schematic-v2`: `schematic-v2` remains the diagnostic/topology view, while `schematic-map` is the official-map candidate.

Current validation outputs:

```text
artifacts\product-candidate\20260617-132923-sheffield-abstract-linear-runs
artifacts\product-candidate\20260617-133019-zhaoqing-abstract-linear-runs-regression
artifacts\product-candidate\20260617-134847-sheffield-spacing-hierarchy
artifacts\product-candidate\20260617-134848-zhaoqing-spacing-hierarchy-regression
```

Alpha validation bundles now also include `schematic-map.svg`, `schematic-map.full.png`, and `manifest.json`, so every real-city feedback package contains the current product-facing map candidate plus machine-readable metadata and a recommended review order. A new simple-city smoke bundle was generated from the `谢菲尔德` export:

```text
artifacts\alpha-validation\20260615-104939-sheffield-simple-city-manifest
```

This case has 3 lines and 19 stations. Use it to check simple-network title/header behavior, route badges, bottom key spacing, station markers, and basic label readability. It is intentionally simple and should complement, not replace, the dense primary-city regression case.

Follow-up simple-network polish added an automatic compact transit-map frame for `schematic-map` when the network is small and simple (1-4 display families, 40 or fewer stations, and no service variants). The compact frame reduces header/footer reserve while preserving the product map header and key. Complex maps or service-variant maps keep the standard frame so the key has enough room. Current Sheffield compact-frame check output:

```text
artifacts\product-candidate\20260615-111148-sheffield-compact-frame-final
```

Phase 5C begins the transition from a plain SVG renderer toward a transit-map cartography system. The first implementation adds an opt-in `--style transit-map` preset with a colored title band, centered `Transport System Map` title, bottom key panel, transit-map station/interchange tokens, and geometry bounds that reserve room for the title and key. This is render-only: exporter data, JSON schema, `line.stops`, `line.pathPoints`, and default `standard` output are unchanged.

Phase 5D has started with a narrow schematic-v2 sharp-angle relaxation pass. The primary issue was a visually wrong acute detour on `3号线` in `transit-map-style-schematic-v2.full.png`; geographic path geometry shows that section as a smoother westward corridor. Schematic-v2 now relaxes sharp render-chain detours after final route guides are built, reducing the spike without changing exporter data, JSON schema, geographic rendering, raw stops, or raw path points.

Phase 5C transit-map style now also renders route number badges on the map for each visible display family. Badges are opt-in with `--style transit-map`, are drawn from display family names/colors, and help move the output closer to official transit-system map language without changing the default `standard` style.

Phase 5C/5D follow-up polish adds collision-aware route badge placement and prevents schematic-v2 exact shared-edge guides from being materialized into route chains. This fixed the observed transit-map schematic-v2 `3号线` / `4号线` west-side over-merge and the follow-up `2号线` / `8号线` loop/repeated-stop regression while keeping exporter data, JSON schema, geographic rendering, raw stops, and raw path points unchanged. A later guard restored the real `2号线` / `10号线` corridor by allowing only high-confidence geometry corridors anchored by an express/service family plus a real shared adjacent edge to materialize. Schematic-v2 parallel overlays are now limited to these materialized geometry-guide pairs, so the primary-city check keeps the `2号线` / `10号线` shared section without reintroducing the earlier false positives.

Phase 5D follow-up now also handles exact shared platform corridors in schematic-v2. This is a render-only overlay for exact final-route-chain segments shared by two-or-more display families, intended for cases such as the west-side `3号线` / `4号线` parallel-track stations and single-edge shared sections such as `10号线` / `7号线`. It does not materialize route guides, does not change station/line data, and is separate from the `2号线` / `10号线` geometry-route-guide mechanism.

Phase 5D.1 freezes the current schematic-v2 candidate for future regression checks. The frozen candidate keeps `2号线` / `10号线` geometry-route-guide sharing, `10号线` white express marker, west-side `3号线` / `4号线` exact shared platform overlays, and no false `2号线` / `8号线` parallel overlay. Freeze artifacts live under:

```text
artifacts\schematic-v2-candidate-freeze\20260604-222351
```

Phase 5E performs a narrow transit-map readability polish pass after the schematic-v2 candidate freeze. Route badges now reuse the final placed station-label boxes for collision scoring, skip endpoint badges when every candidate has a severe collision, and remain renderer-only. The bottom key now has more readable wrapping and includes an `Express service marker` sample explaining the white center stripe.

Current regenerated check output:

```text
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.svg
artifacts\schematic-v2-diagnostics\transit-map-style-schematic-v2.full.png
```

Manual inspection shows the west-side `3号线` / `4号线` badges are separated from each other and from station labels, the `2号线` / `10号线` shared corridor is still present, and the bottom legend is more readable. This does not change exporter output, JSON schema, geographic rendering, raw stops, or raw path points.

Follow-up schematic-v2 review checked whether Line 7 could hide other lines on exact shared final-route segments. The renderer now treats exact shared platform segments as visibility overlays for any two-or-more display families, including express/service families and single-edge shared runs. The current transit-map schematic-v2 SVG shows explicit `exact-shared-platform` overlays for `10号线` / `7号线`, `10号线` / `1号线`, and `3号线` / `4号线`, plus the existing geometry-route-guide overlay for `10号线` / `2号线`. `10号线` keeps its white express marker inside the `10号线` / `7号线` and `10号线` / `2号线` shared sections. This is renderer-only and does not change exporter output, JSON schema, geographic rendering, raw stops, or raw path points.

Project hygiene follow-up after the GitHub merge cleanup:

- `scripts\README.md` now indexes build, publish, local mod, validation, and diagnostics scripts.
- Loose pathPoints smoke SVGs were moved from `artifacts\` to `artifacts\archive\pathpoints-smoke\`.
- A fresh alpha validation bundle was generated from `D:\CS2MetroDiagram\metro-export.json`:

```text
artifacts\alpha-validation\20260605-095556-primary-city-post-cleanup
artifacts\alpha-validation\alpha-validation-20260605-095556-primary-city-post-cleanup.zip
```

- The bundle includes geographic, schematic-lite, and schematic-v2 SVG/PNG outputs, visual continuity report, schematic-v2 diagnostics, notes, viewer settings, and a filled feedback template.
- The geographic visual continuity report shows 9 route base strokes, 0 shared corridor strokes, 0 express stripe strokes, uniform normal stroke width 14, and no current-threshold visual continuity risks.
- Follow-up validation harness fix: `generate-alpha-validation-bundle.ps1` now compares the export `generator.version` with the current tool version and writes freshness warnings into `notes.md` and `feedback-template-filled.md`. This catches stale alpha.1 exports being reviewed with alpha.2 tooling without blocking historical regression bundles.
- The filled feedback template no longer uses PowerShell-sensitive Markdown backticks around generated filenames, fixing the observed backspace/control-character corruption around `baseline-geographic.svg`.

Viewer alpha feedback loop follow-up:

- The WPF Viewer now exposes a separate `Export Data` tab next to the existing map preview.
- The data tab shows export schema, generator/game version, export time, line/station totals, total stops/pathPoints, interchange count, matching diagnostics-file status, per-line stop/pathPoint/source/termini details, and per-station membership/position details.
- The data-inspection logic is now encapsulated in `ExportDataInspector`, and the tab can open the matching diagnostics file directly when present.
- The data tab warns when an export appears stale compared with the current Viewer/tool version or when the city name is still a placeholder.
- Viewer layout selection now includes `geographic` and experimental `schematic-v2`; the older `schematic-lite` option has been retired from the Viewer.
- Viewer map preview now writes a temporary HTML preview file and navigates the embedded browser to it, improving reliability for larger SVG maps; opening a JSON switches back to the `Map Preview` tab so the map appears in-app immediately.
- Viewer preview now has a `Preview` zoom control. The default `100%` view reads the SVG root `width`/`height` and shows large rendered SVGs at actual pixel size with scrollbars; `Fit width` remains available for whole-map overview.
- The Viewer project now includes a CS2 Metro Diagram app icon generated from `Assets\MetroDiagramViewerIcon.svg` / `Assets\MetroDiagramViewer.ico`.
- Schematic-v2 size stability follow-up: schematic-v2 now computes layout in a canonical Poster-sized space and scales the result to the requested output size, so lower size presets should not change shared corridor topology compared with Poster/Ultra.
- This is Viewer-only inspection/polish work. It does not modify the exporter, JSON schema, geographic default rendering, raw `line.stops`, or raw `line.pathPoints`.

Viewer schematic direction update:

- The Viewer no longer exposes the old `schematic-lite` option. Saved Viewer settings that still contain `schematic-lite` are migrated to `schematic-v2`.
- `schematic-lite` remains available from the CLI for historical comparison and regression scripts, but it is no longer the product-facing schematic direction.
- Future schematic-map work should follow `docs\SCHEMATIC_REBUILD_PLAN.md`: topology/corridor-first graph model, stable route skeleton, continuous shared corridor rules, service-variant semantics, and official-map styling.

Schematic rebuild S2 status:

- Added a render-only `CanonicalSchematicNetwork` model in the Rendering project.
- The model records station nodes, service families, service variants, canonical family routes, adjacency edges, interchange groups, exact shared-edge corridor hints, and pathPoints-based physical corridor hints.
- S3/S4 initial wiring is now in place: schematic-v2 builds this canonical network before layout, uses canonical adjacency and canonical family routes for the layout skeleton, and marks shared corridor overlays with canonical debug attributes.
- This is an incremental skeleton/corridor integration, not a full solver rewrite. It does not change exporter output, JSON schema, geographic rendering, raw stops, or raw pathPoints.
- New tests cover canonical service-route selection, exact shared-edge hints, and geometry/pathPoints corridor hints.

S4 product-candidate validation:

- A fresh Zhaoqing alpha validation bundle was generated from:

```text
D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json
```

- Bundle output:

```text
artifacts\alpha-validation\20260611-232357-zhaoqing-s4-product-candidate
artifacts\alpha-validation\alpha-validation-20260611-232357-zhaoqing-s4-product-candidate.zip
```

- The bundle includes geographic, schematic-lite comparison, schematic-v2, PNG screenshots, visual continuity report, schematic-v2 diagnostics, notes, and a filled feedback template.
- A transit-map style schematic-v2 product-candidate output was also generated:

```text
artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.svg
artifacts\schematic-v2-diagnostics\zhaoqing-product-transit-map-schematic-v2.full.png
```

- The current release package was regenerated and smoke checked:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

- Release Viewer exe was launched successfully in a short smoke test.

Product candidate workflow follow-up:

- Added a focused product-candidate generation script:

```text
scripts\generate-product-candidate-map.ps1
```

- The script renders one human-review map output, copies the source export JSON, captures a full-size PNG with the correct preset viewport, and writes review notes.
- This is intentionally separate from `generate-alpha-validation-bundle.ps1`: validation bundles compare layouts and collect diagnostics, while product-candidate output is the current best-looking map for manual review.

Real export city-name fix:

- The real CS2 exporter now reads the city name from `Game.City.CityConfigurationSystem` during `Export Real Metro JSON`.
- `cityName` is tried first, then `overrideCityName`, then the loaded-name backing field `m_LoadedCityName`.
- The resolved name is written to `city.name`, snapshot filenames, and diagnostics. If CS2 returns no readable city name, the previous fallback remains in place and diagnostics explain which candidates were empty or failed.
- This changes only export metadata; it does not modify the JSON schema, metro line/station extraction, raw `line.stops`, raw `line.pathPoints`, or renderer behavior.

Schematic-v2 terminal-tail polish:

- A new Zhaoqing snapshot showed a low-degree 8号线 terminal tail rendered as an unnecessary zigzag in schematic-v2 while the geographic/pathPoints view read as a simple terminal corridor.
- Schematic-v2 now has a narrow renderer-only terminal-tail straightening pass. It preserves terminal and anchor stations, only moves internal ordinary tail stations, and only activates when the rendered tail has a high detour ratio.
- The current validation output is:

```text
artifacts\schematic-v2-diagnostics\latest-zhaoqing-schematic-v2.svg
```

- This pass is intentionally scoped away from previously fixed 2号线/10号线 geometry sharing, 3号线/4号线 exact shared platform overlays, geographic output, exporter logic, and JSON schema.

Schematic-v2 dense-station diagnostics follow-up:

- Schematic-v2 now reports concrete remaining dense station pairs in renderer warnings instead of only reporting a count.
- Station markers involved in remaining dense pairs include SVG debug attributes such as `data-schematic-v2-dense-station`, paired station ids, minimum distance, adjacent-pair status, and same-name cluster status.
- On the current Zhaoqing product-candidate map, the remaining dense pairs are both same-name station clusters (`肇庆二中站` and `现代地铁站`), which should be treated differently from ordinary accidental crowding.
- This is diagnostics-only and renderer-only; it does not modify exporter output, JSON schema, geographic rendering, raw `line.stops`, or raw `line.pathPoints`.

Paradox Mods publishing status:

- First Paradox Mods upload completed successfully.
- Published mod id: `146643`.
- Current access level: `Unlisted`.
- `CS2 Metro\Properties\PublishConfiguration.xml` now stores the published id so future uploads update the same listing.
- Future binary/version uploads should use `PublishNewVersion`; metadata-only changes should use `UpdatePublishedConfiguration`.

Phase 4D.4 is closed. The primary city baseline has been accepted for alpha.2 candidate review. Route continuity is acceptable, stroke width consistency is acceptable, white-filled station markers are restored, station alignment is acceptable for alpha, and label readability is acceptable while still a known polish area. Shared corridor and express stripe remain experimental and off by default.

Phase 4E alpha.2 candidate package has been generated. Geographic remains the recommended alpha.2 default baseline. Phase 5B starts a topology-first schematic-v2 exploration because schematic-lite-v1 patch fixes are not sufficient for reliable topology. Phase 5B.2 preserves exact topology-level shared corridors in schematic-v2 by selecting a topology-rich service variant for each display family and adding shared-corridor diagnostics. Phase 5B.3 adds pathPoints-based geometry shared corridor detection for skip-stop / express cases where stop sequences alone are insufficient. Phase 5B.3b/5B.3c proved route-guide materialization can work, but the approach is too complex and unstable for the current alpha. Phase 5B.4 switches schematic-v2 to service variant simplification: each service family renders one canonical route, express / rapid / skip-stop variants are hidden as independent geometry, and a white center stripe marks families that contain express service variants. A follow-up schematic-v2 fix now re-applies corridor route guides to the final render route chain, normalizes obvious canonical backtracking chains, renders the real `2号线` / `10号线` shared section as a continuous parallel corridor, and preserves the `10号线` white express center stripe inside that shared section. Geographic remains unchanged and recommended for alpha. Phase 4F adds an alpha validation bundle workflow so each real city export can produce a repeatable feedback package instead of continuing single-city visual tuning.

Release artifacts:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

## Current Goal

Keep the `v0.1.0-alpha.2-candidate` package and geographic baseline stable while adding opt-in cartographic polish. The stable default baseline remains `geographic + UsePathPoints + service family merge / normalized family rendering`; `transit-map` style and schematic-v2 remain experimental/opt-in.

## Completed

- Phase 3A passed with visual caveats:
  - real CS2 metro data can be exported,
  - real line colors, station names, and line names are preserved,
  - real exports can be converted to SVG.
- Phase 3B passed:
  - basic label placement and legend sorting are implemented,
  - output is valid SVG/XML.
- Phase 3C passed:
  - `geographic` and `schematic-lite` layout modes exist,
  - `geographic` remains available,
  - `schematic-lite` is render-time only and does not change the JSON schema.
- Phase 4A and 4A.1 passed:
  - WPF Viewer exists under `src/MetroDiagram.Viewer`,
  - Viewer opens JSON through `MetroJsonLoader`,
  - Viewer renders SVG through `MetroSvgRenderer`,
  - Viewer can switch layout, adjust basic render settings, refresh preview, and save SVG,
  - self-contained and framework-dependent Viewer publish scripts exist.
- Phase 4B passed manual Viewer validation:
  - Viewer detects `D:\CS2MetroDiagram\metro-export.json` and `Documents\CS2MetroDiagram\metro-export.json`,
  - Viewer adds `Open Default Export`, `Open Export Folder`, and `Reset Defaults`,
  - Viewer saves settings to `Documents\CS2MetroDiagram\viewer-settings.json`,
  - Viewer supports minimal English / Chinese UI switching,
  - Rendering and CLI support generic/crowded label hiding options.
- Phase 4C preparation:
  - unified release version is `v0.1.0-alpha.1`,
  - Viewer window title includes the release version,
  - Core default `generator.version` uses `v0.1.0-alpha.1`,
  - CS2 test and real JSON exporters write `generator.version` as `v0.1.0-alpha.1`,
  - publish scripts include version, build time, and commit in `build-info.txt`,
  - added `docs/ALPHA_QUICK_START.md`,
  - added `docs/KNOWN_ISSUES.md`,
  - added `docs/FEEDBACK_TEMPLATE.md`,
  - added `docs/CHANGELOG.md`,
  - added `scripts/package-alpha-release.ps1`.
- Phase 4C verification completed:
  - offline solution build passed,
  - console tests passed,
  - Viewer self-contained publish passed,
  - CS2 mod build/post-process passed with local toolchain,
  - alpha release folder and zip were generated,
  - required zip entries were checked,
  - release Viewer exe started successfully in a short smoke test.
- Lightweight release polish completed:
  - added `docs/NEXT_SESSION_HANDOFF.md`,
  - added `docs/RELEASE_CHECKLIST.md`,
  - added `docs/ROADMAP.md`,
  - added `docs/ALPHA_TEST_PLAN.md`.
- Phase 4D.0 export snapshot naming:
  - real export still writes latest files as `metro-export.json` and `metro-export-diagnostics.txt`,
  - every real export also writes timestamped snapshot files under `exports\`,
  - snapshot naming uses local time `yyyyMMdd-HHmmss`,
  - city names are sanitized for Windows file names and fall back to `UnnamedCity` when unavailable,
  - Viewer `Open Default Export` remains pointed at the latest file,
  - snapshot files can be opened manually through Viewer `Open JSON`.
- Phase 4D.0 in-game manual validation passed:
  - latest export exists,
  - timestamped snapshot exists,
  - repeated export does not overwrite old snapshots,
  - latest diagnostics exists,
  - snapshot diagnostics exists,
  - Viewer opens latest through `Open Default Export`,
  - Viewer opens snapshot through `Open JSON`.
- Phase 4D.1 primary city baseline:
  - added `scripts\generate-primary-city-baseline.ps1`,
  - generated baseline artifacts under `artifacts\primary-city-baseline\latest`,
  - archived the latest Phase 4D.2-polished run under `artifacts\primary-city-baseline\history\20260530-093512`,
  - baseline rendering uses geographic layout, `UsePathPoints=true`, service family merge enabled, shared corridor disabled, express stripe disabled, and poster size,
  - visual continuity summary reports 9 normal route base strokes, 0 shared corridor strokes, 0 express stripe strokes, uniform normal stroke width 14, and no current-threshold visual continuity risks.
- Phase 4D.2 alpha candidate baseline polish:
  - renderer title output now uses `{CityName} Metro Diagram` when a real city name is available,
  - renderer title falls back to `CS2 Metro Diagram` for the current CS2 export placeholder city name and `Unnamed City Metro Diagram` for blank names,
  - default legend width, font size, and row spacing were increased for alpha readability,
  - default padding/framing was lightly adjusted while preserving a right-side legend lane,
  - `scripts\capture-svg-screenshot.ps1` now removes stale PNG output before invoking Edge so screenshots cannot silently reuse an old file,
  - regenerated `artifacts\primary-city-baseline\latest\baseline-geographic.svg`,
  - regenerated `artifacts\primary-city-baseline\latest\baseline-geographic.full.png`,
  - regenerated `artifacts\primary-city-baseline\latest\visual-continuity-summary.txt`,
  - regenerated `artifacts\primary-city-baseline\latest\notes.md`.
- Phase 4D.3 station marker and label readability polish:
  - transparent station rings were replaced with white-filled station circles and dark outlines,
  - ordinary station radius is now slightly larger for visibility,
  - interchange station radius remains larger but was capped to avoid visual continuity risks,
  - station label font size, label halo, and station-label offset were increased,
  - legend width, label font size, variant font size, and row spacing were increased again,
  - no route algorithm, exporter, JSON schema, Viewer UI, shared corridor, express stripe, or 5A.9 work was started,
  - regenerated the primary city baseline under `artifacts\primary-city-baseline\latest`,
  - archived the Phase 4D.3 run under `artifacts\primary-city-baseline\history\20260530-095317`,
  - visual continuity summary reports 9 normal route base strokes, 0 shared corridor strokes, 0 express stripe strokes, uniform normal stroke width 14, station markers at radius 6.2 / 9.8, and no current-threshold visual continuity risks.
- Phase 4D.4 station route anchor alignment:
  - added renderer-only station route anchoring for `geographic + UsePathPoints`,
  - raw `station.position`, `line.pathPoints`, and the JSON schema are unchanged,
  - station markers and label placement now use the computed station render anchor,
  - ordinary stations project to the nearest point on their display family primary path when within threshold,
  - interchange stations average close anchors across related display families,
  - stations too far from a route or with spread-out interchange anchors keep the raw render position and emit debug fallback attributes,
  - SVG station/label debug attributes include anchor source, applied flag, anchor distance, raw render coordinates, related families, and fallback reason when present,
  - schematic-lite keeps its existing station placement behavior,
  - regenerated the primary city baseline under `artifacts\primary-city-baseline\latest`,
  - archived the Phase 4D.4 run under `artifacts\primary-city-baseline\history\20260530-100650`,
  - visual continuity summary still reports 9 normal route base strokes, 0 shared corridor strokes, 0 express stripe strokes, uniform normal stroke width 14, and no current-threshold visual continuity risks.
- Phase 4E baseline freeze:
  - primary city baseline accepted for alpha.2 candidate,
  - accepted reasons: route continuity acceptable, stroke width consistency acceptable, white-filled station markers restored, station alignment acceptable for alpha, label readability acceptable with remaining polish noted,
  - accepted baseline archived under `artifacts\primary-city-baseline\history\20260530-102042`,
  - shared corridor and express stripe remain experimental and off by default,
  - Phase 5A.9 Route Run Stitcher is not started.
- Phase 4E alpha.2 candidate packaging:
  - release version is `v0.1.0-alpha.2-candidate`,
  - Viewer self-contained publish passed,
  - release folder generated under `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate`,
  - release zip generated under `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip`,
  - release zip contains Mod, Viewer, docs, samples, README, QUICK_START, KNOWN_ISSUES, CHANGELOG, and build-info,
  - release Viewer exe starts in a short smoke test,
  - primary city baseline generation passed again and archived `artifacts\primary-city-baseline\history\20260530-223217`.
- Phase 4E.1 schematic-lite overlap resolver:
  - geographic rendering is unchanged and remains the alpha.2 recommended default,
  - schematic-lite now detects distinct display-family occupancy per undirected snapped segment,
  - A->B and B->A are treated as the same schematic segment,
  - same-family repeated segments do not inflate occupancy,
  - overlapping schematic segments render as small centered parallel offsets,
  - station markers remain at the original schematic station point,
  - SVG offset segments include `data-schematic-overlap`, family count, index, offset, and segment key debug attributes,
  - generated comparison output under `artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.svg` and `.full.png`,
  - tests cover two-family, three-family, reverse direction, repeated family, zero-length, geographic unaffected, station center, and loop/branch sample cases.
- Phase 4E.1a schematic-lite overlap junction cleanup:
  - overlap offset segments are trimmed at both endpoints so they do not run directly into station/junction centers,
  - trim distance is centralized in render options / style tokens,
  - station markers remain centered and render above route layers,
  - too-short segments clamp trimming and emit `data-schematic-overlap-trim-fallback`,
  - SVG overlap segments include `data-schematic-overlap-trim` and `data-schematic-overlap-trim-distance`,
  - regenerated `artifacts\path-geometry-comparison\schematic-lite-overlap-resolved.svg` and `.full.png`,
  - real schematic output has 26 trimmed overlap segments across 6 overlapping schematic segment keys.
- Phase 5B topology-first schematic-v2:
  - `schematic-v2` is accepted as the correct direction for future schematic work and remains experimental,
  - the old `schematic-lite` mode is still available,
  - diagnostics are generated under `artifacts\schematic-v2-diagnostics`.
- Phase 5B.2 schematic-v2 shared corridor preservation:
  - schematic-v2 now chooses a topology-rich service variant per display family, so express/local families can preserve common running sections instead of using only the pathPoints-densest primary service,
  - shared corridor diagnostics now emit `shared-corridors.txt`, `shared-corridors.csv`, and `schematic-v2-shared-corridor-debug.svg`,
  - current diagnostics identify a 2号线 / 10号线 shared edge between `station_1068950_1` and `station_1068953_1`,
  - generated comparison output includes `schematic-v2-before-shared-corridor.svg` and `schematic-v2-shared-corridor.svg`,
  - this is renderer-only and does not change exporter logic, JSON schema, geographic rendering, `line.stops`, or `line.pathPoints`.
- Phase 5B.3 / 5B.3a schematic-v2 geometry shared corridor diagnostics:
  - schematic-v2 can use `pathPoints` as physical corridor evidence when shared stop sequence / adjacent stop edge detection is insufficient for skip-stop or express services,
  - diagnostics now emit `geometry-shared-corridors.txt`, `geometry-shared-corridors.csv`, `geometry-shared-corridor-debug.svg`, `schematic-v2-route-guides.txt`, and `schematic-v2-route-guides.csv`,
  - the current real export identifies `10号线 + 2号线` as a geometry corridor with approximate shared length `541.455`, average distance `16.754`, max distance `65.892`, confidence `1`, and route guide entries for both families,
  - schematic-v2 SVG output includes route-guide debug attributes for geometry-corridor constraints,
  - comparison SVGs are generated under `artifacts\schematic-v2-diagnostics`,
  - PNG capture for this pass is blocked in the current sandbox because Edge screenshot execution requires elevated GUI/browser permissions and the escalation request was rejected by the environment quota.
- Phase 5B.3b schematic-v2 corridor guide materialization:
  - geometry corridor detection now feeds materialized schematic-v2 route guides,
  - pass-through guide stations affect only schematic-v2 route geometry and do not change raw `line.stops` or JSON,
  - schematic-v2 renders continuous parallel corridor overlays from materialized shared route runs, not arbitrary short overlap segments,
  - the current `10号线 + 2号线` row in `schematic-v2-parallel-corridors.csv` reports `detected=True`, `materialized=True`, `parallelRendered=True`, host `2号线`, follower `10号线`,
  - generated comparison SVGs include `geographic-baseline.svg`, `schematic-v2-before-guide-materialization.svg`, `schematic-v2-guide-materialized.svg`, `schematic-v2-parallel-corridor.svg`, and `schematic-v2-parallel-corridor-debug.svg`,
  - this remains renderer-only and does not modify exporter logic, JSON schema, geographic rendering, Viewer UI, or the old schematic-lite patch pipeline.
- Phase 4F alpha validation harness:
  - added `scripts\generate-alpha-validation-bundle.ps1`,
  - each bundle copies the input export JSON and diagnostics when available,
  - each bundle renders geographic baseline, schematic-lite, and schematic-v2 SVG/PNG outputs,
  - each bundle includes visual continuity output, schematic-v2 diagnostics, `notes.md`, and `feedback-template-filled.md`,
  - bundle output is written under `artifacts\alpha-validation\<timestamp>-<caseName>`,
  - optional zip output is generated next to the bundle unless `-SkipZip` is passed,
  - this does not change renderer defaults, exporter logic, JSON schema, shared corridor defaults, express stripe defaults, or Viewer behavior.
- Phase 5A.1 diagnostic preparation:
  - added route geometry diagnostics for recognized subway `TransportLine` entities,
  - records RouteSegment buffer presence, segment count, sampled segment entity references, component type names, Game.Net references, and geometry-like fields,
  - adds a diagnostic-only summary of sampled geometry hints,
  - includes the same route geometry report in the transport debug dump text/JSON sidecar data.
- Phase 5A.1 in-game diagnostics validated:
  - 42 `TransportLine` entities found,
  - 11 subway lines exported,
  - 48 stations exported,
  - 157 route segments found,
  - sampled route segments exposed geometry-like fields,
  - route geometry was judged likely recoverable from RouteSegment references.
- Phase 5A.2 implementation:
  - schema v1 now supports optional `line.pathPoints`,
  - Core loads old JSON without `pathPoints` and normalizes missing values to an empty list,
  - real exporter reads RouteSegment PathTargets `m_ReadyStartPosition` / `m_ReadyEndPosition` through a bounded best-effort extractor,
  - exporter diagnostics include per-line path point counts and skipped segment counts,
  - geographic rendering can use path points when enabled,
  - CLI adds `--use-path-points`,
  - Viewer adds `Use exported path geometry`,
  - added `samples\sample-metro-pathpoints.json` and path point tests.
- Phase 5A.2b implementation:
  - geographic path rendering removes consecutive duplicate/near-duplicate points,
  - geographic path rendering removes very short segments and simplifies nearly-collinear points when enabled,
  - path cleanup works on temporary render data and does not mutate `MetroExportDocument`,
  - route SVGs rendered from path points include original and cleaned path point counts,
  - render size presets exist for Compact, Standard, Poster, and Ultra,
  - CLI adds `--size`, `--simplify-path-points`, `--no-simplify-path-points`, `--path-simplification-tolerance`, and `--min-path-segment-length`,
  - Viewer adds a size preset dropdown and path simplification controls,
  - tests cover duplicate cleanup, collinear simplification, first/last preservation, old JSON compatibility, and size preset dimensions.
- Phase 5A.2c implementation:
  - `RoutePathPointExtractor` now tries `RouteSegment.CurveElement` first, `RouteSegment.PathElement` second, and `RouteSegment.PathTargets` last,
  - CurveElement extraction can sample Bezier-like `Bezier4x3` / `float4x3` structures at t=0, 0.25, 0.5, 0.75, 1,
  - PathElement extraction uses the same reflective geometry/position reader as a best-effort second source,
  - final path points are deduplicated without changing `line.stops`,
  - diagnostics now include CurveElement count, PathElement count, path point count before/after cleanup, source summary, first 10 path points, and CurveElement fallback reasons,
  - added a shared Bezier sampling helper with offline tests.
- Phase 5A.2d implementation:
  - added `scripts\analyze-metro-export-json.ps1`,
  - added `scripts\generate-path-geometry-comparison.ps1`,
  - added path geometry validation notes, now consolidated in `docs\ROUTE_GEOMETRY_NOTES.md`,
  - updated next-session handoff and development notes for the manual validation workflow.
- Phase 5A.3 implementation:
  - added `CS2 Metro\MetroTrackGeometryDebugExporter.cs`,
  - extended the existing `Export Transport Debug Dump` action to also write `metro-track-geometry-debug.json` and `metro-track-geometry-debug.txt`,
  - subway `TransportLine` diagnostics now sample `RouteSegment` items and record segment entity ids, component type names, entity reference fields, referenced `Game.Net` entity component types, geometry-like fields, and likely curve source candidates,
  - added metro track geometry discovery notes, now consolidated in `docs\ROUTE_GEOMETRY_NOTES.md`.
- Phase 5A.3b implementation:
  - `RoutePathPointExtractor` now explicitly reads `Game.Routes.CurveElement` buffers first,
  - each `CurveElement.m_Curve` Bezier is sampled into 8 points when control points are readable,
  - extraction now checks public and private instance fields/properties for Bezier control points,
  - adjacent duplicate or near-duplicate path points are still removed after extraction,
  - fallback order remains `CurveElement`, then `PathElement`, then `PathTargets`,
  - `metro-export-diagnostics.txt` now reports curve sample point count, PathTargets fallback count, first CurveElement read failures, first sampled pathPoints, and a bounded deep dump of `CurveElement.m_Curve` fields.
- Phase 5A.3c validation:
  - added path geometry validation results, now summarized in `docs\ROUTE_GEOMETRY_NOTES.md`,
  - latest `D:\CS2MetroDiagram\metro-export.json` contains 11 subway lines, 48 stations, 157 stops, 157 route segments, and 9739 pathPoints,
  - all 9739 exported pathPoints use `RouteSegment.CurveElement`,
  - no exported pathPoints use `RouteSegment.PathTargets`,
  - no CurveElement fallback diagnostics were present,
  - generated comparison SVGs under `artifacts\path-geometry-comparison`,
  - `10号线（机场快线-大站快车）` renders from 1065 path points instead of 8 stop points when pathPoints are enabled,
  - `10号线（机场快线-特快）` renders from 1025 path points instead of 4 stop points when pathPoints are enabled.

- Phase 5A.3d implementation:
  - geographic pathPoint rendering now simplifies projected SVG path geometry with adaptive pixel-scale tolerances,
  - default path simplification is slightly stronger while preserving first/last points and station-nearest anchors,
  - suspicious long jumps are diagnosed and split into separate route polylines instead of being drawn as one continuous segment,
  - route SVG diagnostics now include original pathPoints count, cleaned pathPoints count, reduction ratio, max segment length, suspicious jump count, and effective simplification tolerance,
  - tests cover point reduction, first/last preservation, suspicious jump splitting, and valid SVG output.
- Phase 5A.4a implementation:
  - added `SvgRenderOptions.EnableParallelCorridorOffset`, default `false`,
  - CLI adds `--enable-parallel-corridor-offset`,
  - corridor offset only runs for `geographic + UsePathPoints`,
  - cleaned path polylines are split into segments, indexed through a spatial grid, and grouped when distance, direction, and projected overlap match,
  - grouped corridor fragments receive stable perpendicular offsets based on line order,
  - station endpoints taper toward zero offset using the line's own stops,
  - SVG route fragments include `data-parallel-corridor-offset`, `data-corridor-id`, member count, offset index, and offset pixels,
  - tests cover default-off behavior, two-line and three-line shared corridors, reverse direction sharing, non-overlap rejection, station tapering, and valid SVG output.
- Phase 5A.5 implementation:
  - added render-only `DisplayLineFamilyResolver` in `MetroDiagram.Rendering`,
  - added `SvgRenderOptions.EnableServiceFamilyMerge`, default `true`,
  - CLI adds `--disable-service-family-merge`,
  - obvious Chinese and English bracketed service variants are grouped by display family key,
  - main map renders only the selected primary line for each display family,
  - primary line selection prefers more `pathPoints`, then more stops, then stable name/index order,
  - legend shows service variants with variant name, stop count, and endpoint names when available,
  - route SVGs include display family debug attributes,
  - tests cover Chinese merge, English merge, non-merge across different lines, primary selection, legend variant text, and valid SVG output.
- Phase 5A.6 implementation:
  - added `SvgRenderOptions.EnableSharedCorridorCompositeStroke`, default `false`,
  - CLI adds `--enable-shared-corridor-composite-stroke`,
  - composite stroke only runs for `geographic + UsePathPoints`,
  - detection works on display family primary routes after service-family merge,
  - the first nested/layered composite stroke approach rendered two-family shared corridors on the same centerline,
  - three-or-more-family shared corridors fall back and emit `data-shared-corridor-skipped="too-many-families"`,
  - real visual review showed the nested composite result was fragmented and not the target style.
- Phase 5A.6b implementation:
  - shared corridor detection still runs only in rendering and only for `geographic + UsePathPoints`,
  - shared corridor matching still uses display family primary routes after service-family merge, not raw exported lines,
  - a new shared corridor run builder merges adjacent same-family-set fragments into longer maximal contiguous runs,
  - exactly two display families render with a Shanghai 3/4-inspired `shanghai-like` shared corridor style: one continuous corridor base plus an inner color band,
  - three-or-more-family shared corridors still fall back and emit `data-shared-corridor-skipped="too-many-families"`,
  - SVG shared runs include `data-shared-corridor`, `data-shared-corridor-run-id`, `data-shared-corridor-family-a`, `data-shared-corridor-family-b`, `data-shared-corridor-style`, and `data-shared-corridor-point-count`,
  - added `SvgRenderOptions.EnableExpressCenterStripe`, default `false`,
  - CLI adds `--enable-express-center-stripe`,
  - express/rapid-like display families can draw a thin white center stripe over the family color,
  - tests cover default-off behavior, continuous long runs, station-through continuity, family-set-change splitting, reverse-direction sharing, nearby non-overlap rejection, three-family fallback, same-family variant merge, express stripe detection, ordinary-line non-detection, and valid SVG output.
- Phase 5A.7 implementation:
  - added `scripts\analyze-svg-render-debug.ps1`,
  - generated `artifacts\path-geometry-comparison\svg-render-debug-summary-before-5A7.txt`,
  - refactored `geographic + UsePathPoints` route drawing into a renderer-only corridor run plan,
  - fixed route drawing order to normal route base strokes, shared corridor base strokes, shared corridor inner strokes, then express center stripe decorations,
  - centralized route stroke width helpers for normal routes, shared corridor total width, shared inner band width, and express center stripe width,
  - express stripes are now decoration commands over continuous normal runs,
  - express stripes are skipped on shared corridor/fallback conflicts and marked with `data-express-marker-skipped="shared-corridor-style-conflict"`,
  - shared corridor runs are merged a second time when adjacent runs have the same family pair and close endpoints,
  - generated `artifacts\path-geometry-comparison\svg-render-debug-summary-after-5A7.txt`,
  - after the pipeline refactor, real smoke output route elements dropped from 143 to 121, shared corridor elements from 52 to 34, shared runs from 26 to 17, and express stripes from 14 to 10,
  - generated full-size PNG screenshots for the updated shared-corridor and express-on SVGs.
- Phase 5A.8 implementation:
  - added `scripts\analyze-visual-continuity.ps1`,
  - generated a visual continuity report and debug SVG/PNG for the real express-on output,
  - introduced a centralized `SvgVisualStyle` token set for base route width, shared corridor width, express stripe width, station marker radii/strokes, and label halo width,
  - normalized shared corridor total width to match the normal route width instead of appearing as a much thicker special case,
  - kept express center stripes as internal decoration strokes that do not change the base route width,
  - changed station markers to unfilled rings so station circles sit above routes without visually knocking out or cutting route strokes,
  - strengthened shared corridor run merging for near-touching endpoints and reverse/prepend merge cases,
  - added tests for near-touch shared run merging, normalized corridor widths, station marker no-knockout behavior, and express stripe/base width separation,
  - generated real smoke SVG/PNG outputs for normalized baseline, shared corridor, express-on, and visual-continuity debug views.
- Phase 5A.8 closeout:
  - confirmed shared corridor and express stripe remain default-off experimental renderer options,
  - confirmed the recommended alpha baseline is `geographic + UsePathPoints + service family merge / normalized family rendering`,
  - selected `artifacts\path-geometry-comparison\15-geographic-family-normalized.full.png` as the current recommended manual review baseline,
  - kept `16-geographic-shared-corridor-normalized` and `17-geographic-express-normalized` as experimental comparison outputs only,
  - decided shared corridor / express stripe visual issues should not block external alpha testing.

## Current Capability

- Can export real metro/subway data from CS2.
- Can render real network SVG by CLI.
- Can preview and save SVG through the local Windows Viewer.
- Can publish a self-contained win-x64 single-file Viewer package.
- Can assemble an alpha release folder and zip under `artifacts\releases`.
- Viewer can open the default real export with one button when it exists.
- Viewer can remember user settings between runs.
- Viewer can reduce default/generic station-name clutter by default, and users can explicitly enable `Show default / non-important station labels` when they want every unrenamed/default station name visible.
- Basic label placement, legend sorting, and schematic-lite layout are available.
- Real exports can include optional `pathPoints` for route geometry while keeping `stops` as the station sequence.
- Geographic rendering can clean and simplify `pathPoints` before drawing routes when path geometry is enabled.
- Geographic path rendering uses adaptive simplification by default and reports per-line path geometry diagnostics in SVG route attributes.
- Experimental parallel corridor offset can separate shared geographic pathPoint segments when explicitly enabled.
- Service family merge can collapse same-line express/local variants into one main-map display line while listing variants in the legend.
- Experimental shared corridor style can show two-family shared path geometry as longer continuous centerline runs without lateral offsets.
- Experimental express center stripe can mark express/rapid-like display families with a thin white line over the main family color.
- A lightweight Edge headless screenshot helper can render generated SVG files to PNG for visual checks without relying on npm/npx/Playwright.
- CLI and Viewer can choose Compact, Standard, Poster, or Ultra output sizes.
- The real exporter can emit denser experimental `pathPoints` by sampling `RouteSegment.CurveElement.m_Curve` Beziers when readable.
- The latest validated real export uses CurveElement as the only final pathPoints source.
- Transport debug dump can emit dedicated metro track geometry discovery files for investigating true `Game.Net` track geometry sources.

## Export Location Note

- Current real exported JSON files are expected under `D:\CS2MetroDiagram`.
- The Viewer also checks `Documents\CS2MetroDiagram\metro-export.json`.
- Viewer settings are stored at `Documents\CS2MetroDiagram\viewer-settings.json`.
- Alpha validation bundles are generated under `artifacts\alpha-validation`.
- Local artifacts are trimmed to the current alpha.2 release package, self-contained Viewer package, latest alpha validation bundle, and latest product candidate map. Regenerate older diagnostics/comparison outputs from scripts when needed.

## In Progress

- Short-term alpha.2 candidate validation and packaging.
- External/manual alpha.2 candidate review.
- Generate alpha validation bundles for each real city before making further renderer decisions.
- External multi-city validation remains the next larger testing direction after the alpha.2 candidate package is reviewed.
- Short-term station-name semantics work is active: default CS2 asset names are separated from likely player-authored station names, and virtual transfer hints are opt-in only.
- `schematic-map` now has a conservative octilinear normalization pass for product-style output: route segments that are already close to horizontal, vertical, or 45 degrees are snapped toward those directions while keeping interchange/high-degree anchors stable.
- `schematic-map` also applies a narrow local-clearance pass for ordinary stations that sit too close to unrelated route segments. This is intended to improve small-city readability, such as the Sheffield 1号线 / 3号线 crowded area, without changing topology.
- `schematic-map` now adds bridge-style markers for non-station route crossings. This is a renderer-only visual semantics layer: station/interchange crossings are ignored, route topology is not changed, and geographic output is unaffected.
- Latest Sheffield export review uses `D:\CS2MetroDiagram\exports\metro-export-谢菲尔德-20260617-223135.json` (8 display families, 43 stations, 12 geometry shared corridors). The generated `schematic-map` candidate has no dense station pairs and uses two bridge markers for non-station crossings.
- `scripts\generate-alpha-validation-bundle.ps1` now prefers PowerShell 7 (`pwsh`) for screenshot/diagnostics helper scripts, with Windows PowerShell fallback. This fixed the failed `schematic-map.full.png` capture seen on the new Sheffield bundle.
- Added `scripts\summarize-alpha-validation-bundles.ps1` to generate `artifacts\alpha-validation\index.md` and `index.csv` from bundle manifests. This makes multi-city alpha review status visible without manually opening every bundle directory.

## Blocked

- None in code.

## Known Issues

- Only metro/subway networks are supported.
- Offline save parsing is not supported; users must load a city and export from inside CS2.
- `schematic-lite` is intentionally simple and not a professional-grade automatic schematic layout.
- Labels can still be crowded.
- Player-created virtual transfers are not inferred automatically by default. The renderer can optionally draw dashed same-name transfer hints, but the feature is off unless enabled in CLI or Viewer.
- `schematic-lite` now offsets exact shared snapped segments, but it is still a secondary layout and not a full automatic map layout engine.
- Phase 4E.1b technically passed tests but failed visual acceptance: short trimmed fragments and render de-duplication could make schematic-lite routes look broken around junctions.
- Phase 4E.1c changes schematic-lite overlap handling to a conservative policy: only long, simple, safe overlap segments are offset; short or high-degree junction segments fall back to full centered connectors and keep continuity.
- Phase 4E.2 reframes the dense schematic-lite junction issue as station spacing, not low-level overlap trimming. Schematic-lite now relaxes stations that snap too close together before routes, markers, and labels are rendered.
- Phase 5B starts topology-first schematic exploration with a new independent `schematic-v2` layout mode. It does not replace `schematic-lite` or the geographic alpha baseline.
- Phase 5B.2 preserves exact shared station-edge corridors in schematic-v2, but approximate pathPoints-only common corridors remain diagnostic hints rather than forced merges.
- Route-run fragmentation can still create minor visual discontinuities in some real geographic outputs.
- Shared corridor and express stripe rendering remain experimental and are not recommended as default alpha output.
- Interchange grouping may not be perfect for every city.
- No PNG/PDF export.
- No in-game SVG preview.
- The game mod does not launch the Viewer.
- See `docs/KNOWN_ISSUES.md` for the release-facing known issue list.

## Next Actions

1. Run `scripts\validate-local.ps1` before committing broad changes.
2. Use `docs\ALPHA2_SHORT_TERM_CHECKLIST.md` for the current manual smoke pass.
3. Run `scripts\generate-alpha-validation-bundle.ps1` for each real city export.
4. Run `scripts\summarize-alpha-validation-bundles.ps1` after generating bundles, then review `artifacts\alpha-validation\index.md`.
5. Review the generated `baseline-geographic.full.png`, `schematic-lite.full.png`, `schematic-v2.full.png`, and `notes.md`.
6. Keep geographic as the recommended alpha.2 baseline.
7. Keep CLI `schematic-lite` only for historical comparison; continue schematic-map work through experimental `schematic-v2`.
8. For same-name close stations, distinguish known asset/default names from likely player-authored names before treating them as virtual transfer candidates.
9. For `schematic-map`, manually compare the octilinear candidate outputs against the latest real exports before treating the style as user-facing default.
10. For small/simple city outputs, verify the `schematic-map` local-clearance pass improves route breathing room without introducing odd bends.
11. For `schematic-map`, check non-station X crossings are bridged and true station/interchange crossings are still represented by station markers.
12. Manually review `artifacts\alpha-validation\20260618-122028-sheffield-new-export-review` and `artifacts\product-candidate\20260618-121615-sheffield-new-export-review`.
13. Manually review the `v0.1.0-alpha.2-candidate` package.
14. Run in-game smoke for `Export Real Metro JSON` on the primary city and future alpha cities.
15. Confirm latest and snapshot exports still appear under `D:\CS2MetroDiagram`.
16. Defer new renderer feature work until alpha validation bundles show repeated issues.
17. Keep obsolete generated artifacts out of the workspace; use scripts to regenerate rather than preserving every historical output.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\validate-local.ps1`
- `dotnet build CS2MetroDiagram.slnx --no-restore`
- `dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore`
- `dotnet build "CS2 Metro\CS2 Metro.csproj" --no-restore`
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1`
- CS2 mod build/post-process with the local CS2 modding toolchain command documented in `docs\DEV_NOTES.md`.

## Design Decisions

- Phase 4C only synchronizes `generator.version` in the CS2 exporters; it does not change real exporter ECS reading logic.
- Viewer reuses Core loader and Rendering renderer; it does not copy SVG rendering logic.
- Viewer uses WPF built-in `WebBrowser` for embedded preview to avoid a WebView2 package dependency in this release.
- Viewer does not launch external programs from the CS2 mod.
- Alpha releases are packaged as a folder plus zip, not an installer.
- Viewer settings use a small JSON file in `Documents\CS2MetroDiagram`.
- Viewer bilingual UI uses a small in-process resource dictionary instead of a full i18n framework.
