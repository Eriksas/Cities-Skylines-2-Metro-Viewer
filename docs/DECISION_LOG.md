# Decision Log

## 2026-06-04 - Add exact shared platform overlays for schematic-v2

Decision: Schematic-v2 can render a separate `exact-shared-platform` parallel overlay for exact final-route-chain segments shared by two-or-more display families, including single-edge runs and express/service families.

Reason: The west-side `3号线` / `4号线` segment represents parallel tracks through the same stations, not an express route guide. It should be visible as two parallel route strokes, but it should not re-enable topology shared-edge route materialization or arbitrary schematic-lite overlap patches that previously broke `2号线` / `8号线`.

Follow-up reason: Later review showed single-edge exact shared segments such as `10号线` / `7号线` can have the same draw-order hiding problem, and express/service families need to keep their white center stripe on shared exact segments. Exact shared platform rendering is therefore generalized, but it still does not materialize route guides or change exporter/schema/geographic data.

## 2026-06-02 - Restrict schematic-v2 corridor materialization to anchored express geometry corridors

Decision: Do not materialize exact topology shared edges by themselves. Schematic-v2 route-chain materialization is allowed only for high-confidence pathPoints geometry corridors that are anchored by an express/service family and a real shared adjacent edge; parallel overlays are limited to the materialized geometry-guide pair.

Reason: Exact shared-edge expansion caused false positives on `3号线` / `4号线` and then `2号线` / `8号线`. The real `2号线` / `10号线` case still needs a visible shared corridor, but it should be restored through the physical geometry corridor path, not by arbitrary snapped overlap or topology-edge expansion.

## 2026-06-01 - Normalize schematic-v2 service family render chains

Decision: Keep express / rapid / skip-stop variants as service metadata in schematic-v2, but normalize the chosen canonical route chain before rendering and apply materialized corridor guides to the final schematic-v2 route geometry.

Reason: The real `10号线` family was correctly simplified to one canonical route, but that canonical route contained an out-and-back style stop chain. Rendering it directly reintroduced a distorted schematic. Normalizing only the render chain preserves exporter/schema/raw stops while making the visible route usable.

## 2026-06-01 - Render schematic-v2 shared corridors only from final route chains

Decision: Schematic-v2 parallel corridor overlays are generated from shared segments in the final render route chains, not from arbitrary snapped schematic-lite overlap fragments.

Reason: The `2号线` / `10号线` shared section needs to be expressed as a continuous visible corridor after route guide materialization. Reusing schematic-lite patch-style segment offsets caused local node distortion and did not guarantee topology-correct shared runs.

## 2026-05-26 - Keep the first milestone offline

Decision: Phase 1 uses a standalone .NET offline solution with Core, Rendering, CLI, and Tests. The existing CS2 mod template remains untouched for a later exporter phase.

Reason: The development plan requires the sample JSON to SVG pipeline to work before any Cities: Skylines II exporter implementation begins.

## 2026-05-26 - Use raw coordinate normalization for v0.1 rendering

Decision: The Minimal SVG renderer normalizes station `x/z` coordinates into a fixed SVG canvas with margins and connects stations in each line's stop order.

Reason: Phase 1 explicitly avoids complex schematic layout and only needs a readable first SVG output.

## 2026-05-26 - Use a dependency-free console test runner for Phase 1

Decision: `MetroDiagram.Tests` is a console project that runs focused assertions and exits non-zero on failure.

Reason: The first milestone should stay offline and avoid NuGet test package dependencies while the repository foundation is still being established.

## 2026-05-26 - Keep Phase 1.5 entirely offline

Decision: Phase 1.5 adds more samples, fallback tests, and renderer checks without creating or modifying a CS2 exporter.

Reason: The offline JSON-to-SVG path should be reliable before any game API or real city data can complicate debugging.

## 2026-05-26 - Treat missing station references as non-fatal validation issues

Decision: Missing station references are reported with clear warnings and skipped by the renderer instead of failing the whole document.

Reason: Partially valid exported data should still produce a useful diagram, while the CLI and tests expose the data issue clearly.

## 2026-05-26 - Reserve fixed SVG space for the legend

Decision: The renderer reserves a right-side legend lane during coordinate normalization.

Reason: This keeps the legend from covering route geometry without introducing complex layout or collision avoidance in Phase 1.5.

## 2026-05-26 - Phase 2 exports static JSON only

Decision: The CS2 mod shell writes a static v0.1-compatible `test-export.json` from a settings button.

Reason: The phase goal is to verify the game mod pipeline, options entry, file writing, and logging before any real CS2 data discovery.

## 2026-05-26 - Use Documents as the first export location

Decision: Phase 2 writes `test-export.json` under `Documents\CS2MetroDiagram`.

Reason: The user can find the file without knowing CS2 internal paths, and the exact path is still logged for troubleshooting.

## 2026-05-26 - Do not read ECS or real city data in Phase 2

Decision: The exporter does not query transport lines, stations, ECS components, save files, or loaded city state.

Reason: Real data discovery belongs to a later phase after the static exporter shell is proven in-game.

## 2026-05-26 - Keep command-line CS2 toolchain path configurable

Decision: The mod csproj supports a `CsiiToolPath` MSBuild property while preserving the existing `CSII_TOOLPATH` environment variable behavior.

Reason: This allows local command-line verification in environments where the user-level toolchain variable is missing, without changing the offline solution.

## 2026-05-26 - Add debug dump before real metro export

Decision: Phase 2.5 adds a transport debug dump button and files, but still does not export real `metro.json`.

Reason: We need evidence of CS2's transport-related ECS components before choosing a stable real exporter design.

## 2026-05-26 - Keep debug scanning inside the CS2 mod project

Decision: `TransportDebugDumpExporter.cs` and `TransportDebugDumpModels.cs` live in the existing `CS2 Metro` mod project.

Reason: Core, Rendering, CLI, and tests must stay free of game assembly dependencies.

## 2026-05-26 - Use bounded best-effort reflection for ECS data

Decision: The dump scans candidate entities by component type keywords and reads component data reflectively with sample limits and exception capture.

Reason: This lets the project collect useful discovery data without hardcoding guessed ECS component names or risking a full dump failure from one unreadable component.

## 2026-05-26 - Emit both JSON and TXT debug dumps

Decision: The debug export writes compact structured JSON and a human-readable TXT report.

Reason: JSON is easier to inspect programmatically, while TXT is faster for a first manual pass.

## 2026-05-26 - Implement Phase 3A as a narrow real exporter

Decision: The first real exporter only reads confirmed `Game.Routes.TransportLine` data and writes `metro-export.json` plus `metro-export-diagnostics.txt`.

Reason: Phase 3A needs to prove the real CS2 data path while avoiding layout work, previews, style presets, and broader ECS exploration.

## 2026-05-26 - Keep real CS2 reads inside the mod project

Decision: `RealMetroJsonExporter.cs` lives in the `CS2 Metro` project, and Core/Rendering/CLI continue to consume only schema-compatible JSON.

Reason: The offline pipeline should remain buildable and testable without CS2 game assemblies.

## 2026-05-26 - Prefer diagnostics over schema expansion for exporter uncertainty

Decision: Station id sources, fallback names, fallback coordinates, skipped waypoints, and subway detection reasons are written to a sidecar diagnostics file instead of extending the JSON schema.

Reason: The existing CLI needs stable input, while Phase 3A still needs rich evidence for manual review and Phase 3 follow-up.

## 2026-05-26 - Keep Phase 3B out of the CS2 exporter

Decision: Basic readability improvements are implemented in Rendering/CLI, with no changes to real CS2 export behavior.

Reason: Phase 3A already proved the real data chain; Phase 3B should make the generated SVG easier to read without increasing game-side risk.

## 2026-05-26 - Sort legends by line names, not entity ids

Decision: Legend ordering extracts the first number from `line.name` and leaves non-numbered lines last.

Reason: Real exporter entity ids contain unrelated numbers, while displayed line names carry the user-facing route order.

## 2026-05-26 - Use heuristic label placement before schematic layout

Decision: Label placement tries eight positions and chooses the lowest-overlap candidate using approximate text bounds and station-circle obstacles.

Reason: This reduces dense center collisions without introducing advanced schematic layout or label hiding yet.

## 2026-05-26 - Keep center expansion opt-in

Decision: A conservative center expansion transform exists but is disabled by default.

Reason: It may help dense cores, but it distorts real coordinates and needs more visual review before becoming default behavior.

## 2026-05-26 - Add schematic-lite as a render-only layout mode

Decision: Phase 3C adds `geographic` and `schematic-lite` renderer modes without changing `MetroExportDocument` or the CS2 exporter.

Reason: The exported game coordinates should remain source data; schematic coordinates are presentation-only and can be recalculated with different options.

## 2026-05-26 - Keep geographic as the default layout

Decision: `geographic` remains the default CLI/rendering behavior.

Reason: Phase 3B outputs must stay usable and predictable unless the user explicitly asks for schematic-lite.

## 2026-05-26 - Use a simple grid walk for schematic-lite

Decision: Schematic-lite starts from normalized geographic coordinates, snaps to a configurable grid, and places newly seen stops using the nearest horizontal, vertical, or 45-degree segment candidate.

Reason: This produces visibly more regular route geometry without introducing a large graph-layout dependency or a complex topology optimizer.

## 2026-05-26 - Build Phase 4A as a WPF viewer

Decision: Add `MetroDiagram.Viewer` as a WPF desktop app that references Core and Rendering.

Reason: The target user needs a simple local Windows UI, while the existing offline libraries already contain the JSON loading and SVG rendering logic.

## 2026-05-26 - Use built-in WPF WebBrowser for the first embedded preview

Decision: Use WPF `WebBrowser.NavigateToString` instead of adding WebView2 in Phase 4A.

Reason: WebView2 would introduce an additional package dependency; the built-in control is sufficient for a local SVG preview milestone.

## 2026-05-26 - Package Viewer as folder outputs, not an installer

Decision: Phase 4A.1 adds PowerShell publish scripts that create framework-dependent and self-contained folder packages under `artifacts/`.

Reason: The phase needs a user-runnable exe package without taking on installer design, signing, update flows, or distribution infrastructure.

## 2026-05-26 - Keep self-contained package single-file

Decision: The self-contained win-x64 Viewer package uses `PublishSingleFile=true` and `IncludeNativeLibrariesForSelfExtract=true`.

Reason: A single exe is easier for normal users to run while still allowing package docs, sample JSON, and build metadata to sit next to it.

## 2026-05-26 - Keep Phase 4B label filtering render-only

Decision: Add generic and crowded label hiding as `SvgRenderOptions`, CLI flags, and Viewer controls without changing `MetroExportDocument` or the CS2 real exporter.

Reason: Label visibility is presentation policy. The exported JSON should keep the full network data so users can rerender with different settings.

## 2026-05-26 - Store Viewer settings in Documents

Decision: Save Viewer preferences to `Documents\CS2MetroDiagram\viewer-settings.json`.

Reason: The project already uses `CS2MetroDiagram` as a user-visible export folder, and a small JSON settings file is enough for the alpha Viewer without adding a configuration framework.

## 2026-05-26 - Use a small Viewer resource dictionary for bilingual UI

Decision: Implement English/Chinese Viewer UI text in `ViewerResources.cs`.

Reason: Phase 4B only needs a minimal bilingual UI, so a full i18n framework would be unnecessary overhead.

## 2026-05-26 - Package v0.1.0-alpha.1 as folder plus zip

Decision: Phase 4C creates `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1` and `CS2MetroDiagram-v0.1.0-alpha.1-win-x64.zip`.

Reason: Alpha testers need one predictable folder structure with Mod, Viewer, docs, samples, and build metadata, without taking on installer work yet.

## 2026-05-26 - Synchronize exporter version without changing exporter behavior

Decision: Update CS2 exporter `generator.version` values to `v0.1.0-alpha.1` through a small mod-side version constant.

Reason: Release metadata should be consistent, but Phase 4C must not change the real exporter ECS reading logic.

## 2026-05-26 - Keep release docs separate from development notes

Decision: Add `ALPHA_QUICK_START.md`, `KNOWN_ISSUES.md`, `FEEDBACK_TEMPLATE.md`, and `CHANGELOG.md` as release-facing documents.

Reason: External testers need concise instructions and feedback guidance without reading the full development plan or internal project state.

## 2026-05-26 - Investigate route geometry through diagnostics first

Decision: Phase 5A.1 samples CS2 `RouteSegment` buffers and referenced Game.Net entities only in diagnostics/debug dump output.

Reason: Path rendering and corridor rendering need evidence of recoverable track/lane geometry before changing the JSON schema or renderer behavior.

## 2026-05-26 - Add optional pathPoints without replacing stops

Decision: Phase 5A.2 adds optional `line.pathPoints` but keeps `line.stops` as the station sequence and fallback render source.

Reason: RouteSegment geometry improves geographic route shape, but stations, labels, interchange logic, and old JSON compatibility still depend on the established stop list.

## 2026-05-26 - Keep path geometry opt-in for rendering

Decision: CLI uses `--use-path-points`, and Viewer exposes `Use exported path geometry`; schematic-lite continues to use stops by default.

Reason: The first path geometry export is useful but still raw. Keeping rendering opt-in avoids destabilizing existing geographic and schematic-lite outputs.

## 2026-05-26 - Clean path geometry only during rendering

Decision: Phase 5A.2b removes duplicate, very short, and nearly-collinear path points in the rendering layer without mutating `MetroExportDocument` or changing exporter ECS logic.

Reason: Real path geometry still needs in-game validation, so cleanup should improve SVG readability while preserving the exported source data for diagnostics and future corridor rendering work.

## 2026-05-26 - Add fixed size presets before style presets

Decision: Add Compact, Standard, Poster, and Ultra render size presets to CLI and Viewer while leaving width and height editable.

Reason: Large real cities need quick canvas sizing, but this does not require a new style system or renderer behavior change.

## 2026-05-27 - Prefer CurveElement before PathTargets for pathPoints

Decision: Phase 5A.2c changes real exporter path point extraction to try RouteSegment CurveElement first, PathElement second, and PathTargets ready start/end only as fallback.

Reason: In-game comparison showed PathTargets endpoints still produced cross-station shortcut lines, so the exporter needs denser route geometry while preserving the existing `line.pathPoints` schema.

## 2026-05-27 - Keep Bezier sampling math shared but game-free

Decision: Add a small Core `PathGeometrySampler` helper and compile it into the CS2 mod as linked source.

Reason: Bezier sampling can be tested offline without adding CS2 game assembly dependencies to Core, Rendering, CLI, or Tests.

## 2026-05-28 - Use CurveElement Beziers before PathTargets

Decision: Phase 5A.3b makes `RouteSegment.CurveElement.m_Curve` the primary source for real `line.pathPoints`, sampling each readable Bezier before falling back to PathElement or PathTargets.

Reason: Manual debug output confirmed subway route segment entities expose `Game.Routes.CurveElement` buffers with `Colossal.Mathematics.Bezier4x3`, while PathTargets start/end points are too sparse and still create express-line fly-lines.

## 2026-05-28 - Simplify pathPoints in projected SVG space

Decision: Phase 5A.3d keeps path geometry cleanup in the renderer but applies adaptive simplification after projecting pathPoints to SVG coordinates, while splitting suspicious long jumps into separate route polylines.

Reason: CurveElement exports can contain thousands of dense points. Pixel-space cleanup removes visual jitter more predictably than raw game-coordinate cleanup, and splitting abnormal jumps avoids drawing misleading continuous fly-lines without changing `metro-export.json`.

## 2026-05-28 - Keep parallel corridor offsets opt-in

Decision: Phase 5A.4a adds renderer-only parallel corridor offset behind `EnableParallelCorridorOffset` / `--enable-parallel-corridor-offset`, default off, and only for `geographic + UsePathPoints`.

Reason: Shared-corridor detection is approximate and should not change existing alpha output until real maps confirm the visual tradeoffs. Keeping it renderer-only preserves exporter/schema stability and allows side-by-side comparison.

## 2026-05-28 - Merge same-line service variants before corridor styling

Decision: Phase 5A.5 adds renderer-only display line families and merges obvious bracketed service variants into one main-map route by default, with variant stop patterns shown in the legend.

Reason: Real output showed lateral parallel offsets can introduce visible jitter on dense geographic path geometry. Same-line express/local variants are better represented as one line family first, preserving exporter/schema data while reducing overdraw without tuning side offsets.

## 2026-05-29 - Use composite stroke instead of lateral offset for shared corridors

Decision: Phase 5A.6 adds renderer-only shared corridor composite strokes behind `EnableSharedCorridorCompositeStroke` / `--enable-shared-corridor-composite-stroke`, default off, only for `geographic + UsePathPoints` display families.

Reason: Different display families sharing the same track should remain on the same centerline to avoid offset jitter. A layered outer/separator/inner stroke can communicate two-family sharing without changing exporter data, schema, station positions, or labels.

## 2026-05-29 - Replace nested composite with continuous shared corridor runs

Decision: Keep shared-corridor work renderer-only, but stop treating the first nested/concentric Phase 5A.6 composite stroke as the target style. The renderer now builds longer maximal contiguous shared corridor runs and renders exactly two display families with a `shanghai-like` corridor base plus inner color band.

Reason: Real output from `08-geographic-shared-corridor-composite.svg` showed shared corridors were cut into many short fragments and did not read like a transit-map shared corridor. The new run builder prioritizes continuity first, keeps station geometry untouched, and preserves exporter/schema stability.

## 2026-05-29 - Use white center stripe for express service markers

Decision: Add `SvgRenderOptions.EnableExpressCenterStripe` and CLI `--enable-express-center-stripe`, both opt-in, to draw a thin white center stripe over express/rapid-like display families.

Reason: Same-line fast service variants are already merged by Phase 5A.5, so a lightweight white center stripe better matches the desired Nanjing-style express notation than adding more nested/shared-corridor layers.

## 2026-05-29 - Use Edge headless for local SVG screenshot checks

Decision: Add `scripts/capture-svg-screenshot.ps1` to render generated SVG files to PNG with installed Microsoft Edge headless.

Reason: Local `npx.ps1` is blocked by PowerShell execution policy, and `npx.cmd playwright` depends on an uncached Playwright package and registry access. Edge headless gives a stable local visual validation path without adding npm dependencies.

## 2026-05-29 - Stabilize geographic corridor rendering as a run pipeline

Decision: Phase 5A.7 refactors `geographic + UsePathPoints` route rendering into a renderer-only corridor run plan with fixed layer ordering: normal route bases, shared corridor bases, shared inner bands, then express center stripe decorations.

Reason: Real output showed the remaining shared-corridor and express-stripe issues were caused by route fragmentation, repeated overlays, and inconsistent per-branch stroke handling. A run pipeline makes continuity and layer order explicit while preserving the exporter, JSON schema, station data, and schematic-lite behavior.

## 2026-05-29 - Treat express center stripes as decorations

Decision: Express center stripes are drawn as a decoration layer over continuous normal route runs. If the route is part of a shared corridor or a too-many-family fallback, the stripe is skipped and the SVG records `data-express-marker-skipped="shared-corridor-style-conflict"`.

Reason: Express markers should not change the base route width or amplify shared-corridor breaks. Skipping conflict cases is clearer than forcing another style layer onto already shared geometry.

## 2026-05-29 - Treat visual continuity as a renderer correctness requirement

Decision: Phase 5A.8 treats "looks disconnected" as a rendering bug even when route geometry is technically connected. The project now has a visual continuity diagnostic script that marks run endpoints, short fragments, near-touching gaps, and style-layer risks.

Reason: Real PNG review showed that users perceive breaks from SVG fragmentation, round caps, station marker knockout, and layered style conflicts. Geometry continuity alone is not enough for a readable transit map.

## 2026-05-29 - Normalize map styles through renderer tokens

Decision: Add a centralized renderer-only `SvgVisualStyle` token set and make normal routes, shared corridors, express stripes, station markers, and label halos derive widths/radii from it.

Reason: Shared corridors and express markers must belong to the same visual language as ordinary routes. In particular, shared corridor total width now matches the normal route width, express stripes stay internal, and station markers no longer use a white fill that visually cuts route strokes.

## 2026-05-29 - Keep latest export paths stable and add snapshot sidecars

Decision: Real metro export continues to write the latest files as `metro-export.json` and `metro-export-diagnostics.txt`, and also writes timestamped snapshot copies under the `exports` subdirectory.

Reason: Viewer `Open Default Export` and the existing user flow depend on a stable latest path, while alpha validation and rendering regression work need non-destructive historical exports for comparison and issue attachments.

## 2026-05-29 - Use sanitized local-time snapshot names

Decision: Snapshot exports use local timestamps in `yyyyMMdd-HHmmss` format and sanitize city names for Windows filenames, falling back to `UnnamedCity` when a city name is unavailable.

Reason: Local timestamps are easier for manual testing sessions to read, and sanitized city slugs make the export history browsable without changing the JSON schema.

## 2026-05-30 - Establish a primary city regression baseline before multi-city testing

Decision: Phase 4D.1 uses the currently available real city as the primary validation benchmark and generates repeatable baseline artifacts under `artifacts\primary-city-baseline`.

Reason: The project currently has one real city save available. A stable baseline makes renderer and exporter regressions easier to detect before broader alpha testing, while the rule remains that no city name, line, station, coordinate, or network shape may be hard-coded.

## 2026-05-30 - Keep experimental styles out of the primary baseline

Decision: The primary city baseline uses `geographic + UsePathPoints + service family merge / normalized family rendering`, with shared corridor and express stripe disabled.

Reason: Phase 5A.8 showed the normalized family rendering is the most stable current default. Shared corridor and express stripe outputs remain useful comparisons, but they are not mature enough to define the alpha baseline or block testing.

## 2026-05-30 - Polish the alpha candidate baseline before new rendering work

Decision: Phase 4D.2 only fixes the baseline title, legend readability, framing defaults, and screenshot freshness; it does not start Phase 5A.9 or add new shared corridor / express stripe behavior.

Reason: The primary city baseline is already visually acceptable for alpha review. The next useful step is making the default output look less like a raw technical artifact, while preserving the stable renderer language and avoiding another style branch.

## 2026-05-30 - Treat CS2 export placeholder names as unavailable city names in titles

Decision: The renderer displays `CS2 Metro Diagram` when the city name is the current exporter placeholder `CS2 Metro Export`, while real city names render as `{CityName} Metro Diagram`.

Reason: The placeholder is not a user city name, and appending `Metro` produced awkward titles such as `CS2 Metro Export Metro`. This is a render-time title fallback and does not change the JSON schema or exporter data.

## 2026-05-30 - Restore white-filled station markers for alpha readability

Decision: Phase 4D.3 changes station markers from transparent rings back to white-filled circles with dark outlines, while keeping radii and stroke widths modest.

Reason: Transparent rings preserved route continuity, but the primary city baseline showed station presence was too weak for alpha users. A white-filled circle with a controlled outline is a familiar metro-map symbol and improves readability without changing route geometry.

## 2026-05-30 - Do not start Route Run Stitcher for station readability polish

Decision: Phase 4D.3 only adjusts station marker, label, and legend defaults. It does not start Phase 5A.9, shared corridor styling, express stripe tuning, or exporter/schema work.

Reason: The current primary city baseline has acceptable route continuity and uniform line widths. The blocking polish item is readability of stations, labels, and legend, not route-run fragmentation.

## 2026-05-30 - Align station markers to rendered route geometry at render time

Decision: Phase 4D.4 adds renderer-only station route anchoring for `geographic + UsePathPoints`, projecting station marker and label anchors onto related display family primary route paths when the correction is within conservative thresholds.

Reason: Real CS2 `station.position` and exported `line.pathPoints` come from different structures and can be slightly misaligned. The map should show station markers on the rendered line, but the exporter data and JSON schema should remain unchanged for diagnostics and future rendering options.

## 2026-05-30 - Keep interchange anchoring conservative

Decision: Interchange stations average nearby route anchors across related display families, but fall back to the raw render point when the candidate anchors are spread too far apart.

Reason: A transfer marker should sit near the visual meeting point of multiple lines, but forcing it onto one unrelated path can make the interchange semantically wrong. Debug fallback attributes make these cases inspectable without hiding the issue.

## 2026-05-30 - Freeze the primary city baseline for alpha.2 candidate

Decision: Accept the current primary city baseline as the alpha.2 candidate baseline and freeze it under `artifacts\primary-city-baseline\history\20260530-102042`.

Reason: Manual review found route continuity, stroke width consistency, white-filled station markers, and station route alignment acceptable for alpha. Label and legend polish remain known follow-up areas but do not block the candidate.

## 2026-05-30 - Stop new visual feature work before alpha.2 candidate

Decision: Do not start Phase 5A.9, new shared corridor styles, new express stripe styles, Viewer UI additions, PNG/PDF export, JSON schema changes, or exporter restructuring before the alpha.2 candidate unless a blocking bug is found.

Reason: The project needs a stable external validation package more than another visual branch. Shared corridor and express stripe remain experimental/off by default, and the accepted default remains `geographic + UsePathPoints + service family merge / normalized family rendering`.

## 2026-05-30 - Use alpha.2 candidate version marker

Decision: Use `v0.1.0-alpha.2-candidate` for the post-alpha.1 release candidate package, generator version, Viewer title, release folder, build-info, README, changelog, and release documentation.

Reason: The code now includes substantial post-alpha.1 changes: CurveElement pathPoints, service family merge, visual style tokens, station marker polish, station route anchoring, export snapshot naming, primary city baseline workflow, and experimental shared corridor / express options.

## 2026-05-30 - Resolve schematic-lite exact segment overlap in the renderer

Decision: Phase 4E.1 adds schematic-lite-only segment occupancy detection and small centered parallel offsets for snapped segments used by multiple display families.

Reason: The geographic baseline is stable and remains the alpha.2 default, but schematic-lite can snap unrelated or shared services onto the same grid segment. Without a schematic-only overlap resolver, later lines hide earlier lines. This fix keeps station markers centered, does not change exporter data or JSON schema, and avoids reopening geographic shared-corridor or route-run stitcher work.

## 2026-05-30 - Trim schematic overlap offsets before station centers

Decision: Phase 4E.1a trims schematic-lite overlap-offset segment endpoints before drawing station markers above them.

Reason: Offsetting each schematic segment independently made some station/junction areas look twisted because offset route endpoints crowded the node center. Endpoint trimming keeps the visible offset on segment interiors, leaves station markers centered, and avoids changing layout, exporter data, JSON schema, or geographic rendering.

## 2026-05-30 - Fall back short schematic overlap segments to centered connectors

Decision: Phase 4E.1b treats very short schematic-lite overlap segments as centered, butt-capped connectors and de-duplicates same-family reverse overlap output at final render time.

Reason: The `1760,992|1792,1024` segment showed that trimming can be technically applied but still leave a tiny visible fragment. Combined with reverse duplicate rendering, that produced a spike/blob around the junction. The fix is renderer-only and avoids tuning global offset or trim parameters for a local short-segment artifact.

## 2026-05-31 - Prioritize schematic-lite continuity over short-segment de-duplication

Decision: Phase 4E.1c supersedes the 4E.1b short-fragment approach. The renderer now offsets only long, simple, safe schematic overlap segments; short or high-degree junction overlaps use full centered connectors, and duplicate reverse render segments are preserved when removing them could hurt visual continuity.

Reason: 4E.1b passed unit tests but failed visual acceptance because trimmed/butt-capped short fragments and final render de-duplication made some schematic-lite connections look broken. Schematic-lite is still secondary to the geographic alpha baseline, so the safer choice is to keep route continuity and station connections stable even when a very short overlap cannot show every color.

## 2026-05-31 - Treat dense schematic-lite junctions as station spacing problems

Decision: Phase 4E.2 adds renderer-only schematic station spacing relaxation after grid snapping and before route rendering. Routes, station markers, and labels all use the adjusted schematic positions.

Reason: The `端州火车站` / `小型地铁广场` area showed that continued overlap trimming/fallback tweaks were addressing symptoms. The root issue is that multiple snapped station points can be too close for a readable schematic map. Schematic-lite may sacrifice local geographic proportion to improve station readability while keeping exporter data and JSON schema unchanged.

## 2026-05-31 - Start topology-first schematic-v2 instead of patching schematic-lite-v1

Decision: Add an independent `schematic-v2` layout mode and diagnostics workflow instead of continuing small fixes inside the 4E.1 / 4E.2 patch chain.

Reason: Recent overlap offsets, endpoint trims, short-segment fallbacks, and station-spacing relaxation exposed that schematic-lite-v1 lacks a topology-first architecture. Future schematic work should preserve stop order, adjacency, interchange nodes, and route continuity first, even when it distorts geography. Geographic remains the alpha recommended baseline, and schematic-v2 remains experimental.

## 2026-05-31 - Preserve exact shared corridor topology in schematic-v2

Decision: Phase 5B.2 keeps schematic-v2 on the topology-first path and chooses a topology-rich service variant per display family for schematic-v2 routing, instead of always using the pathPoints-densest primary service. Shared corridor diagnostics now report exact shared station sequences and shared station edges.

Reason: Same-line service family merge is correct for geographic rendering, but a pathPoints-densest express service can omit intermediate shared-corridor stops that are needed for schematic topology. In the primary city, `2号线` and `10号线` share `station_1068950_1 -> station_1068953_1`; schematic-v2 should preserve that common running section before divergence. This stays renderer-only and does not change exporter data, JSON schema, `line.stops`, or `line.pathPoints`.

## 2026-05-31 - Move alpha progress to validation bundles before new visual features

Decision: Phase 4F adds a repeatable alpha validation bundle workflow instead of continuing new shared corridor, express stripe, or route-run stitcher work.

Reason: The geographic alpha baseline is stable enough to test, while schematic-v2 is still experimental. Multi-city evidence is now more valuable than single-city visual tuning. Validation bundles keep export JSON, diagnostics, baseline images, schematic comparisons, notes, and feedback files together so future renderer changes can be judged against concrete cases.

## 2026-05-31 - Use pathPoints for schematic-v2 physical corridor diagnostics

Decision: Phase 5B.3 keeps `line.stops` as the service-order source of truth, but uses `line.pathPoints` as physical corridor evidence for schematic-v2 shared corridor diagnostics and route-guide hints.

Reason: Stop-sequence and shared-adjacent-edge detection cannot fully describe skip-stop / express services. In the primary real city, `10号线` and `2号线` show physical corridor sharing in path geometry even when the stop patterns are not enough by themselves. This remains renderer-only and does not change exporter logic, JSON schema, geographic rendering, `line.stops`, or `line.pathPoints`.

## 2026-05-31 - Render schematic-v2 parallel corridors only from materialized route guides

Decision: Phase 5B.3b renders schematic-v2 parallel corridor overlays only after a shared corridor has been materialized into final schematic-v2 route geometry.

Reason: The old schematic-lite patch pipeline offset arbitrary snapped overlap segments and caused local junction artifacts. Schematic-v2 should first establish topology and route guides, then draw continuous shared runs. This keeps `10号线` / `2号线` visible on their shared corridor without reviving segment-by-segment overlap patches or changing exporter/schema/geographic behavior.

## 2026-06-01 - Reconstruct schematic-v2 render route chains for guide materialization

Decision: Phase 5B.3c treats corridor materialization as a final render route chain reconstruction step, not as a separate short overlay. The follower family route may include render-only pass-through guide nodes from the host corridor, while raw `line.stops`, `line.pathPoints`, exporter output, and JSON schema remain unchanged.

Reason: The real `2号线` / `10号线` case already had detection and parallel debug attributes, but the rendered shared section could still be only a two-point overlay. A schematic map needs the follower polyline itself to travel through the corridor nodes before divergence, especially for skip-stop / express services.

## 2026-06-01 - Treat express variants as schematic-v2 service attributes

Decision: Phase 5B.4 stops using complex skip-stop route-chain reconstruction as the main schematic-v2 expression for express / rapid / skip-stop services. Schematic-v2 now renders one canonical route per service family and marks families with express variants using a white center stripe.

Reason: The 5B.3 route-guide materialization path was becoming too complex for alpha and remained hard to stabilize visually on real `2号线` / `10号线` cases. A canonical all-stop / longest route plus service marker is more stable, easier to explain to alpha users, and keeps exporter data, JSON schema, geographic rendering, raw `line.stops`, and raw `line.pathPoints` untouched.
## 2026-06-02 - Add opt-in transit-map cartography style

Decision: Phase 5C adds `--style transit-map` as a renderer-only style preset instead of changing the default SVG appearance.

Reason: The project needs a path toward official transit-map presentation: title band, bottom key, consistent station tokens, and map-like framing. Keeping it opt-in preserves the accepted geographic alpha baseline and avoids changing exporter data, JSON schema, raw stops, raw path points, Viewer defaults, or schematic-v2 topology work.

## 2026-06-02 - Relax schematic-v2 sharp detours after route guides

Decision: Phase 5D adds a renderer-only sharp-angle relaxation pass for schematic-v2 after final route guides are built.

Reason: The transit-map style schematic-v2 output exposed a very sharp `3号线` detour that did not match the smoother geographic/pathPoints direction. Running the fix after route-guide construction addresses the actual rendered route chain while leaving exporter data, JSON schema, geographic rendering, raw stops, and raw path points unchanged.

## 2026-06-02 - Add route number badges to transit-map style

Decision: Transit-map style now renders route number badges on the map for visible display families.

Reason: Official transit maps commonly identify lines directly on the route, not only in the legend. Keeping badges inside the opt-in `--style transit-map` preset improves map-like readability without changing the default standard style, exporter data, JSON schema, or layout modes.

## 2026-06-02 - Keep schematic-v2 shared-edge guides local to the actual shared edge

Decision: Exact topology shared-edge guides in schematic-v2 are no longer materialized into route chains.

Reason: In the primary city transit-map schematic-v2 output, `3号线` was incorrectly routed through a neighboring `4号线` station because a true shared edge was expanded into a longer guide interval. A follow-up check showed `2号线` / `8号线` could regress even more severely when repeated loop stops made a shared-edge slice replace a long route interval. Exact shared edges already exist in raw stop order, so they should be preserved by final route-chain rendering and shared-run detection, not by route-guide materialization. Only stronger geometry corridor guides should be allowed to add pass-through nodes.

## 2026-06-02 - Make transit-map route badges collision-aware and optional at cluttered endpoints

Decision: Route badges now choose among multiple endpoint-side candidate positions and skip a second endpoint badge when every candidate is too crowded.

Reason: Fixed endpoint placement made badges collide with station labels and with other badges. A map can remain readable with one badge per route, but overlapping badges are visually noisy; conservative omission at crowded endpoints is preferable to forcing a collision.

## 2026-06-04 - Reuse final label placement for transit-map badge avoidance

Decision: Phase 5E makes route badge collision scoring consume the same placed station-label boxes used by the final label renderer, and skips even the first endpoint badge when every candidate has a severe collision score.

Reason: The primary-city transit-map output showed route badges could overlap station names and other route badges. Approximate label boxes were not strict enough once label placement had evolved. Reusing final placed-label geometry keeps badge avoidance aligned with the visible output while preserving exporter data, JSON schema, geographic rendering, and schematic-v2 topology.

## 2026-06-04 - Explain express center stripes in the transit-map key

Decision: The transit-map bottom key includes an `Express service marker` sample when any display family uses the white center stripe marker.

Reason: The white center stripe is now part of schematic-v2 service-variant simplification. Without a legend sample, users can mistake it for an accidental rendering artifact. Keeping the explanation in the key avoids adding new Viewer UI or changing the JSON schema.

## 2026-06-04 - Render exact shared schematic-v2 segments as family overlays

Decision: Schematic-v2 now renders exact shared final-route-chain segments through centered `exact-shared-platform` overlays for any two-or-more display families, including single-edge runs and express/service families.

Reason: The primary transit-map schematic-v2 output showed that a Line 7 shared segment could still depend on route draw order. Exact final-route-chain sharing is strong topology evidence, so every family on that segment should be drawn explicitly instead of allowing the last normal route stroke to hide earlier colors. This remains renderer-only and does not change exporter data, JSON schema, geographic rendering, raw stops, or raw path points.

## 2026-06-07 - Straighten only high-detour schematic-v2 terminal tails

Decision: Schematic-v2 now has a narrow terminal-tail straightening pass after route-guide materialization. It only applies to short tails from a terminal endpoint to an interchange/high-degree anchor, and only when the rendered tail has a high detour ratio.

Reason: A new Zhaoqing export showed the 8号线 southern terminal tail as a visually wrong zigzag even though geographic/pathPoints output reads as a simple terminal corridor. This is a schematic rendering artifact from snapped station positions, not an exporter/schema problem. The fix keeps endpoints fixed, moves only internal ordinary tail stations, and leaves geographic output, schematic-lite, 2号线/10号线 geometry sharing, and 3号线/4号线 exact shared platform overlays untouched.

## 2026-06-11 - Retire schematic-lite from Viewer

Decision: Remove `schematic-lite` from the Viewer layout dropdown and migrate saved Viewer `schematic-lite` settings to `schematic-v2`.

Reason: `schematic-lite` was useful as an early grid-snapping experiment, but recent work shows the target schematic-map experience needs a topology/corridor-first architecture. Keeping the old mode in the Viewer makes it look like a supported user-facing layout even though it is no longer the design direction.

Consequence: CLI `--layout schematic-lite` remains available for historical comparison and regression scripts. Viewer users now choose between the stable geographic baseline and experimental topology-first `schematic-v2`.

## 2026-06-11 - Add render-only canonical schematic network model

Decision: Add `CanonicalSchematicNetworkBuilder` in the Rendering project as the S2 graph layer for future schematic-map work.

Reason: Schematic-v2 needs a stable intermediate model before another layout solver rewrite. Raw JSON is service/export data, not a direct schematic graph. The new model turns display families, variants, stop adjacency, interchange membership, exact shared edges, and pathPoints corridor hints into one render-only structure without mutating `MetroExportDocument`.

Consequence: Current rendering behavior is unchanged. S3 should consume this canonical model instead of adding more direct `MetroSvgRenderer` patch logic.

## 2026-06-11 - Wire schematic-v2 skeleton and corridor output to the canonical model

Decision: Schematic-v2 now consumes `CanonicalSchematicNetwork` for its initial station adjacency graph, family route skeletons, and route-guide starting chains. Shared corridor overlays are explicitly marked as canonical corridor output.

Reason: S2 should not remain a passive data model. The next step toward a real schematic-map system is to make the renderer's topology skeleton start from canonical service families and adjacency instead of repeatedly deriving graph meaning from raw lines inside `MetroSvgRenderer`.

Consequence: This is an incremental S3/S4 bridge, not a complete new solver. Existing spacing, route-guide, and shared-corridor rendering rules remain in place. Exporter output, JSON schema, geographic rendering, raw `line.stops`, raw `line.pathPoints`, and Viewer defaults remain unchanged.

## 2026-06-13 - Make Viewer default-station label control positive

Decision: Expose the Viewer generic-label setting as `Show default / non-important station labels`.

Reason: Alpha review needs a clear way to temporarily show unrenamed/default station names. A positive checkbox matches the user's intent better than the previous `Hide generic station labels` wording.

Consequence: The UI checkbox is inverted when mapped to the existing renderer and settings field: checked means `HideGenericStationLabels = false`. Existing `viewer-settings.json` files remain compatible.
