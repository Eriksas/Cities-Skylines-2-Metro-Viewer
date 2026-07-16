# Decision Log

This file contains current high-level decisions only. Full historical decisions before the 2026-06-18 cleanup are archived at:

```text
docs\archive\2026-06-18-doc-consolidation\DECISION_LOG.full.md
```

## Promote The Validated Toolchain To Beta.1

Decision: use `v0.1.0-beta.1` for the next package while preserving the
`0.1.0` compatibility line.

Reason: real-city export, automatic schematic layout, manual Viewer refinement,
and SVG/PNG/PDF output now form a complete user workflow. The project still
needs broader multi-city validation, so it is beta rather than stable.

Consequence: existing Alpha.7 artifacts remain untouched. Paradox Mods is
updated only after a separate in-game smoke test of the Beta.1 package.

## Localize The Mod Page Without Changing The Game Locale

Decision: the in-game settings page exposes `Auto / English / Simplified
Chinese`. Auto follows the active CS2 locale; explicit choices only change this
mod's localization source and then reload the active locale.

Reason: users need a predictable language for export controls, but a utility
mod must not change the language of the entire game.

Consequence: every supported locale receives a dynamic mod dictionary. Adding a
new mod-page string requires both English and Chinese entries; changing the
dropdown must never assign the game's global locale.

## Preserve Explicit Render Options

Decision: product layout defaults apply only to options that remain `Auto` or
otherwise unspecified. An explicit `MapStyle=Standard` and an explicit disabled
service-family merge must be honored.

Reason: silently overriding CLI/Viewer settings makes diagnostics misleading
and prevents callers from constructing stable comparison renders.

Consequence: `SvgMapStyle.Auto` is the default convenience value. Geometry
cache keys include geometry-affecting inputs only; presentation-only changes
reuse the solved layout.

## Validate The Default Product Layout In Automation

Decision: product candidate and regression scripts default to
`schematic-anneal`, while alpha bundles retain geographic, schematic-map, and
schematic-v2 outputs as evidence.

Reason: the prior scripts could pass while never rendering the actual Viewer
default. A release workflow must exercise the layout users receive.

Consequence: CI additionally publishes the Viewer in Release mode, and shared
PowerShell helpers live in `MetroScriptCommon.psm1` instead of drifting between
validation scripts.

## Default To Schematic-anneal (Supersedes Geographic Default)

Decision: `schematic-anneal` is the default product mode; the Viewer opens on it (2026-07-06). `geographic` remains available and unchanged as the faithful-geometry render.

Reason: on the current corpus (9 samples + Sheffield/Zhaoqing real exports) schematic-anneal wins every layout metric on both median and worst case, and the same-line clearance fix plus canvas recentering removed the last obvious artifacts. A schematic is the product goal; geographic is the reference render, not the product.

Consequence: `geographic` behavior must stay byte-identical (it is the reference). This decision is provisional pending broader multi-city validation; if anneal regresses on more cities, the default can revert. The earlier "keep geographic as default" decision is superseded for the product mode only.

## Treat Schematic Work As Render-only

Decision: schematic-v2 and schematic-map work must remain in renderer/viewer/CLI layers.

Reason: the exporter and `metro-export.json` schema are already useful and should not churn while visual layout evolves.

Consequence: do not modify `RealMetroJsonExporter` ECS interpretation, raw
`line.stops`, raw `line.pathPoints`, or schema for layout polish. Export-folder
UX, localization, logging, and atomic file publication may evolve without
changing the exported data contract.

## Remove Schematic-lite Entirely

Decision: delete legacy `schematic-lite` from the renderer, CLI, tests, and scripts (2026-07-06). It was previously retired from the Viewer.

Reason: schematic-lite became a patch-based dead end; keeping it "for historical comparison" cost ~700 lines of renderer code plus 21 tests while git history already preserves it.

Consequence: `--layout schematic-lite` is no longer accepted. Old Viewer settings that referenced it are migrated to `schematic-v2` on load.

## Judge Layout Changes By Corpus Scores, Not Single Maps

Decision: layout quality changes are accepted or rejected based on the shared layout metrics (octilinearity, bends, crossings, spacing, clearance, weighted cost) measured across the whole sample corpus via `scripts\compare-schematic-layouts.ps1` - corpus medians must improve without making the worst case worse (2026-07-06).

Reason: schematic-map polish had degraded into per-map coefficient tuning: ~40 interacting thresholds adjusted against whichever city was being looked at, with no protection against regressing other cities.

Consequence: the metrics live in one place (`MetroSvgRenderer.LayoutMetrics.cs`) and drive both the CLI `--emit-layout-score` output and the experimental `schematic-anneal` mode, which minimizes exactly this cost globally with deterministic simulated annealing. `schematic-anneal` is the challenger to the schematic-map pass stack; whichever wins on corpus evidence becomes the product direction.

Update 2026-07-06: on the corpus of 9 samples plus 2 real cities (Sheffield 59 stations, Zhaoqing 51), `schematic-anneal` wins every metric on both median and worst case (octilinear ratio, bends, crossings, spacing/clearance violations, weighted cost). It is now selectable in the Viewer. schematic-map is retained as the previous product mode until anneal has broader multi-city validation.

## Use Schematic-v2 As Diagnostic Base

Decision: keep `schematic-v2` as an experimental topology/corridor diagnostic layout.

Reason: it contains useful topology-first ideas, but it is not yet product-facing enough.

Consequence: schematic-v2 should stay available for regression and debugging but should not be promoted as the recommended map.

## Build Product-facing Work In Schematic-map

Decision: use `schematic-map` for the official metro map candidate.

Reason: a separate product-facing mode allows more abstraction, map framing, labels, badges, station tokens, and fit behavior without destabilizing schematic-v2.

Consequence: future visual polish should target schematic-map first unless the underlying topology model is wrong.

## Express Variants Are Service Metadata In Schematic Modes

Decision: express/rapid/skip-stop variants should not become independent schematic geometry by default.

Reason: independent skip-stop geometry caused unstable route chains and visual regressions.

Consequence: render a canonical family route and express marker/legend metadata instead of drawing every service variant separately.

## Preserve Export Snapshots

Decision: keep latest export files and write timestamped snapshots for every real export.

Reason: alpha validation needs reproducible inputs and issue attachments.

Consequence: Viewer `Open Default Export` continues to use latest files; snapshots are opened manually.

## Use Validation Bundles As Review Unit

Decision: every real city review should use an alpha validation bundle.

Reason: bundles keep JSON, diagnostics, screenshots, notes, feedback template, and settings together.

Consequence: use `generate-alpha-validation-bundle.ps1`, then refresh `artifacts\alpha-validation\index.md`.

## Render Schematic-map Crossings Directly Until Elevation Is Known

Decision: schematic-map non-station crossings should render as direct pass-through intersections for now.

Reason: both bridge blobs and underpass-gap symbols looked visually noisy when the renderer could not know real CS2 over/under order.

Consequence: the renderer keeps crossing audit warnings but does not draw extra crossing elements. Real over/under styling should wait until exporter data includes a stable elevation or track-layer signal.

## Mask Duplicate Base Strokes On Exact Shared Platforms

Decision: exact shared platform corridors in schematic-v2/schematic-map should mask the underlying normal route strokes before drawing parallel corridor overlays, and should assign overlays through `VisibleLaneResolver` plus stable lane ordering rather than raw display family count or city-specific line ids.

Reason: drawing normal route strokes and narrower parallel overlays on the same segment made close parallel tracks look uneven or accidentally thicker. Separately, same-number branch families such as `7号线` / `7号线支线` can share one physical track and should not be split just because their raw family keys differ. When a collapsed branch lane and a single-family lane share the same platform segment, the collapsed lane should be visually prioritized; otherwise continuation-side ordering keeps each line on the side implied by its approach/departure geometry.

Consequence: the visible shared-platform segment is now produced by one consistent parallel-corridor layer with debug attributes, while raw route geometry and export data remain unchanged. Different visible lines still separate, but same-number same-color branches collapse into a single visible lane and are ordered above adjacent single-family lanes on exact shared platforms. Future schematic shared-platform logic should use the resolver and lane-ordering helper instead of local family-count heuristics.

## Make Schematic-map Polish Audit-driven

Decision: product-facing schematic-map changes should ship with an SVG audit report for route direction, shared/parallel corridor overlays, route badge/label conflicts, and stroke-width consistency.

Reason: recent visual issues were subtle and could regress across cities when judged only by one screenshot. A lightweight SVG audit gives repeatable evidence without changing exporter data or the JSON schema.

Consequence: `scripts\generate-product-candidate-map.ps1` now runs `scripts\analyze-schematic-map-svg.ps1`. Schematic-map octilinear snapping is more assertive for official-map style, but remaining non-octilinear segments are tracked in audit output and should be fixed only when the correction preserves topology and real-world route direction.

## Keep Route-only Synthetic Bends Opt-in (Superseded For Current Schematic-map)

Decision: route-only synthetic bends for schematic-map were initially available
as an explicit experiment, but were not enabled by default.

Reason: moving station nodes can damage topology, interchange relationships, and shared-platform alignment, and a route-only bend can sometimes help. However, the first Sheffield experiment reduced non-octilinear warnings while making the whole map feel less natural than the previous candidate.

Consequence: this was superseded on 2026-06-20 by the narrower
schematic-map route grammar protection decision below. The audit still tracks
`data-schematic-map-synthetic-bends` so future changes can be compared, but the
current product-facing schematic-map default now enables conservative
route-grammar bends.

## Use Scoring Before Large Schematic-map Rewrites

Decision: before starting another large schematic-map optimization pass, product candidates should be evaluated with a score-oriented SVG audit that records octilinear grammar, interior crossings, sharp turns, route badge conflicts, and stroke-width consistency.

Reason: recent layout work improved the map, but screenshot-only review made it easy to regress one area while fixing another. A repeatable score report gives a baseline for comparing candidates across cities.

Consequence: `scripts\generate-product-candidate-map.ps1` writes `schematic-map-score.csv`, `schematic-map-crossings.csv`, and `schematic-map-turns.csv` through `scripts\analyze-schematic-map-svg.ps1`. Scores guide prioritization, but a higher score is not automatically accepted if the real map looks worse.

## Use A Regression Gate Before Accepting Schematic-map Changes

Decision: schematic-map renderer changes should be checked across real exports
and regression samples before they are accepted.

Reason: recent fixes improved one visible area while sometimes regressing another
area. A multi-case gate makes these tradeoffs visible before pushing or packaging
an alpha candidate.

Consequence: `scripts\generate-schematic-regression-gate.ps1` generates
geographic and schematic-map outputs, PNGs, audit CSVs, and a summary table under
`artifacts\schematic-regression`. `pass` means the current automated safety
checks cleared; human visual review is still required.

## Compare Product Candidates Side By Side

Decision: screenshot review for schematic-map changes should use a generated comparison bundle rather than isolated screenshots.

Reason: subtle layout changes often improve one local area while hurting another. Side-by-side PNGs with score summaries make regressions easier to spot.

Consequence: use `scripts\compare-product-candidates.ps1` after generating product candidates. The script writes `comparison.html`, `comparison.md`, `comparison.csv`, and `comparison.full.png` under `artifacts\product-candidate-comparison`.

## Batch Alpha Validation Before Broad Schematic Tuning

Decision: medium-term schematic-map work should collect and review multiple alpha validation bundles before broad layout changes.

Reason: two hand-tested cities are not enough to prove a schematic rule is general. A batch wrapper makes it cheap to process recent exported snapshots and refresh the review index.

Consequence: use `scripts\generate-alpha-validation-set.ps1` to generate one validation bundle per latest/snapshot export, then use `artifacts\alpha-validation\index.md` as the review queue. Continue using product-candidate comparison and schematic regression gates for code changes.

## Add Visual Debug Overlays Before More Schematic-map Tuning

Decision: schematic-map audits should produce a visual debug overlay that marks
non-octilinear segments and interior route crossings directly on the candidate
SVG.

Reason: after shared-platform and badge fixes, remaining issues are local
geometry problems that are hard to judge from CSVs alone. Marking them on the
map makes the next layout changes evidence-driven without changing renderer
output.

Consequence: `scripts\analyze-schematic-map-svg.ps1` writes
`schematic-map-debug.svg`, and product candidate generation can capture
`schematic-map-debug.full.png`. Future large schematic-map changes should first
check this overlay and avoid broad tuning that improves a score while making the
map feel worse.

## Keep Docs Short And Archive History

Decision: root docs should be current operational references, not full chronological transcripts.

Reason: huge docs made context compaction fragile.

Consequence: long pre-cleanup docs are archived under `docs\archive\2026-06-18-doc-consolidation`; old phase docs are under `docs\archive\historical`.

## Defer Viewer Size Optimization

Decision: Viewer package-size reduction is a long-term packaging task, not a
short-term alpha.2 blocker.

Reason: the current priority is reliable export, preview, validation bundles,
and schematic-map behavior. Changing publish strategy while UI/layout behavior is
still moving risks confusing testers and release artifacts.

Consequence: keep the self-contained Viewer package for alpha candidate testing.
Investigate framework-dependent, trimmed, or alternative packaging after alpha
validation is repeatable.

## Clamp Shared Platform Knockout To Visible Lane Envelope

Decision: exact shared-platform corridor knockout strokes must stay inside the
visible colored lane envelope.

Reason: the knockout exists only to hide duplicate base route strokes before the
parallel colored lanes are drawn. When it is wider than the colored envelope, it
leaks out as white fringes near stations and tight parallel segments.

Consequence: shared-platform SVG now records both
`data-schematic-v2-knockout-width` and
`data-schematic-v2-visible-envelope-width`, and tests assert the knockout does
not exceed the visible lane envelope.

## Split Alpha Validation Into Fast And Full Modes

Decision: batch alpha validation should default to fast SVG/diagnostics triage
when reviewing many exports, with PNG screenshots reserved for selected full
review bundles.

Reason: a recent six-case validation batch completed the actual bundle work but
hit the command timeout because screenshot capture dominates runtime. We need
multi-city evidence without making every scan wait on browser screenshots.

Consequence: `generate-alpha-validation-bundle.ps1` and
`generate-alpha-validation-set.ps1` support `-SkipPng`. Use `-SkipPng` for daily
triage and omit it for the cases that need full screenshot feedback packages.

## Promote Schematic-map Route Grammar Protection To Product Defaults

Decision: `schematic-map` now enables route grammar safeguards by default:
uniform output-size scaling, route-only octilinear synthetic bends for long
non-octilinear spans, and conservative shallow-kink straightening for ordinary
non-anchor stations.

Reason: the Zhaoqing 1号线 west-side route showed that a map can be topologically
correct but still feel unlike a metro diagram when a mostly straight corridor is
drawn as a small zigzag. This is a general schematic grammar issue, not a
city-specific issue.

Consequence: generated schematic-map candidates should keep the same route shape
across size presets and avoid avoidable shallow zigzags. Interchanges and
high-degree stations remain protected anchors. Exporter data, JSON schema,
`line.stops`, and `line.pathPoints` are unchanged.

## Separate Non-adjacent Route Node Collapses In Schematic-map

Decision: `schematic-map` should separate non-adjacent stations in the same final
route chain when layout transforms collapse them onto the same visual route node.

Reason: a product-candidate review showed a real case where two non-adjacent
stations were geographically separate but became nearly coincident after
schematic-map layout passes. That made the route look like an unnatural loop or
self-overlap even though the exported data was valid.

Consequence: this is a renderer-only post-layout guard. It never separates true
adjacent station pairs, prefers moving less-connected stations, and uses
original geography only to choose a stable separation direction. Exporter logic,
JSON schema, `line.stops`, and `line.pathPoints` are unchanged.

## Prefer Compact Octilinear Doglegs For Product-map Grammar

Decision: `schematic-map` may use compact route-only synthetic bends for shorter
non-octilinear spans when station movement would be too risky.

Reason: official metro maps often preserve topology and station anchors while
absorbing small geographic offsets into short 0/45/90-degree doglegs. Keeping a
single slightly off-grid segment makes the map feel less like a system diagram,
but moving interchange or shared-platform stations can damage topology.

Consequence: the product-facing `schematic-map` can produce more route points
and may trigger source-direction audit warnings because intentional dogleg legs
diverge from the original geographic segment. This is still renderer-only and
does not change exporter logic, JSON schema, `line.stops`, or
`line.pathPoints`.

## Treat Synthetic Doglegs As Route Grammar In Audit

Decision: `analyze-schematic-map-svg.ps1` skips source-direction comparison for
route polylines that declare `data-schematic-map-synthetic-bends`, and reports
those segments as informational dogleg notes instead of direction-divergence
warnings.

Reason: compact schematic doglegs intentionally add route points that no longer
map one-to-one to raw stop positions. Comparing those generated dogleg legs to
the original station-to-station direction by index creates false positives and
can make a more official-map-like candidate look worse than it is.

Consequence: candidate scoring still penalizes non-octilinear segments, short
fragments, badge collisions, width inconsistencies, and interior route
crossings, but intentional dogleg route grammar is reviewed through PNG/SVG
inspection and debug attributes. This is an audit-script change only; renderer
behavior, exporter data, and JSON schema are unchanged.

## Add Configurable Mod Export Folder

Decision: the CS2 mod exposes an `Export Folder` setting with an editable folder
path and preset buttons for Documents, Desktop, and `D:\CS2MetroDiagram`.

Reason: alpha testing now depends on repeated exports and snapshots from
multiple cities. Users should not have to accept one hard-coded folder, and
common destinations should be one click away.

Consequence: real exports, test exports, transport debug dumps, and metro track
geometry debug dumps resolve through one shared export-directory helper. Latest
files and timestamped snapshots keep the same file names under the selected
folder. Viewer `Open Default Export` checks the common folders; arbitrary custom
folders are opened with `Open JSON`.

## Treat Product-map Polish As Cartographic Hierarchy, Not Topology Work

Decision: Phase 5C.2 improves `schematic-map` through header/footer/legend,
station hierarchy, label hierarchy, and framing polish only.

Reason: the current product-style map is ready for external visual audit, but
still looks more like an automatic engineering output than an official system
map. The safest next step is to improve information hierarchy without changing
exporter data, JSON schema, geographic output, or schematic route topology.

Consequence: `schematic-map` now emits clearer transit-map header and bottom
legend styling plus station/terminal/transfer symbol metadata. Important station
labels are marked with data attributes while keeping the existing
`station-label` class for test/tool compatibility. Future official-map polish
should continue through candidate bundles and audits instead of reopening
schematic-v2 route-chain experiments or legacy schematic-lite patching.

## Prefer Context-aware Doglegs Over Pure Octilinear Score

Decision: `schematic-map` synthetic bends should be context-aware instead of
blindly adding hard elbows to every short non-octilinear span.

Reason: the Sheffield product candidate showed several visually unnecessary
doglegs where a short direct segment or a nearly octilinear segment read better
than a forced L-shaped route. A higher octilinear score can still make the map
feel less official if it creates cramped or backtracking elbows.

Consequence: the renderer now suppresses synthetic bends when a contextual
segment is close to 0/45/90 degrees, too short to benefit from a bend, or when
the proposed bend would fight the incoming/outgoing route direction. Candidate
audits must be reviewed visually as well as numerically. Exporter logic, JSON
schema, geographic output, `line.stops`, and `line.pathPoints` are unchanged.

## Center Dominant Same-service Lanes On Exact Shared Platforms

Decision: when an exact shared-platform corridor contains a collapsed
same-number/same-color branch lane plus another visible line, the collapsed
same-service lane should remain centered and the adjacent different line should
move to the side.

Reason: same-service branches such as `7号线` and `7号线支线` can represent the
same physical track/service family through a platform segment. Splitting that
collapsed lane away from the station center makes it look like a false separate
track, while offsetting the adjacent different-color line keeps both visible.

Consequence: parallel corridor offsets are assigned by visible-lane semantics,
not only by lane count. SVG output records
`data-schematic-v2-parallel-offset-mode` for these cases, and tests assert the
dominant same-service lane stays centered. This remains renderer-only.

## Use Visible-lane Semantics For Schematic-map Anchors

Decision: schematic-map station anchoring and local-clearance passes should use
visible-lane groups, not raw service/display-family counts.

Reason: same-number/same-color branches and service variants can share one
physical or visual lane. Treating every raw family at a shared station as a true
interchange froze false anchors and preserved avoidable kinks around branch
sections. True transfer behavior is better represented by multiple distinct
visible lanes or high route degree.

Consequence: same-visible-lane branch stations can be straightened and cleaned
up by the product-map passes, while true multi-line interchanges and high-degree
junctions remain protected. Local clearance also ignores paths belonging to the
same visible lane, preventing a route family from pushing itself apart. This is
renderer-only and does not alter exporter data or `metro-export.json`.

## Keep Manual Adjustments As A Separate Render Override Layer

Decision: future player/manual layout adjustment should be stored outside
`metro-export.json` as render-time overrides.

Reason: exported JSON is the factual game-data snapshot. Manual station nudges,
label moves, bend locks, and lane-order preferences are cartographic choices
that should be editable, reversible, and portable without corrupting the source
export.

Consequence: the likely next implementation should introduce a separate Viewer
override file or sidecar model and apply it after automatic schematic-map layout
passes. Do not write manual edits back into raw `station.position`, `line.stops`,
or `line.pathPoints`.

Update 2026-06-24: the first sidecar model is implemented as
`*.layout-overrides.json`. It supports station render-position nudges and label
nudges/hiding, can be loaded explicitly by the CLI with `--overrides`, and is
auto-loaded by the Viewer when a sibling sidecar exists. This confirms the
manual-adjustment direction while keeping the exported JSON/schema unchanged.

Update 2026-06-28: the Viewer manual editor now uses the same sidecar for the
complete lightweight edit loop: station dragging, label dragging, selected label
hide/show, selected reset, clear-all, and open-sidecar. This remains a
renderer/UI override only; it does not change exporter data or source geometry.

## Use WebView2 For Viewer Preview

Decision: replace the legacy WPF `WebBrowser` preview with Microsoft WebView2.

Reason: the old control uses Internet Explorer behavior, shows local-file
active-content security prompts, and makes SVG preview/manual drag interaction
feel dated and slower.

Consequence: preview HTML is loaded in-memory with WebView2 `NavigateToString`,
manual edit messages use `window.chrome.webview.postMessage`, and the Viewer
depends on the Microsoft Edge WebView2 Runtime. This is Viewer-only and does
not alter exporter data, renderer output, or `metro-export.json`.

## Keep CS2 Options Registration Before Dynamic Localization

Decision: preserve the official CS2 mod-template lifecycle order: load the
setting, call `RegisterInOptionsUI()`, and only then add dynamic localization
sources. Localization failures must be caught outside the exporter-critical
initialization path.

Reason: `LocalizationManager.AddSource()` eagerly enumerates the source.
`ModLocaleSource.ReadEntries()` asks the setting for generated Options locale
IDs, which are not valid before Options registration. Reversing the order in
Beta.2 caused every locale to throw and aborted `OnLoad`, disabling the entire
exporter for a cosmetic feature.

Consequence: lifecycle order must not be changed from intuition alone. Any
change to `Mod.OnLoad`, `Mod.OnDispose`, settings, Options UI, or localization
requires a real in-game smoke test before public PDX publication. Build,
post-process, and offline tests are necessary but do not validate this runtime
contract. Localization may degrade to raw keys; it may never take down export.

## Use Snapshot-To-Renderer-To-UI For In-Game Preview

Decision: the Phase 7 in-game preview will use a C# backend pipeline: capture an
immutable metro snapshot on the game side, render it through a portable form of
the existing C# engine, and send sanitized SVG plus small state DTOs to the
game UI. The frontend will display and interact with the map, not reimplement
the schematic layout or read ECS.

Reason: duplicating layout in JavaScript would split product behavior between
the Viewer and game, while letting UI callbacks access ECS would introduce
threading and lifecycle hazards. A shared snapshot and renderer preserve map
parity, isolate game data access, and allow explicit caching/cancellation.

Consequence: Phase 7 begins with an official UI/binding spike, then separates
export data capture from file writing, then resolves .NET runtime compatibility
for the renderer. The desktop Viewer remains the advanced editor. The public
PDX listing stays on the accepted Beta.3 until the owner approves the exact
in-game release candidate.

## Use Current CS2 Game-Panel Extensions For The Preview Shell

Decision: Phase 7A registers its entry through `GameTopRight`, appends its panel
root to `Game`, and uses `Colossal.UI.Binding` value/trigger bindings for C#/UI
communication. It does not use the legacy HookUI path.

Reason: these extension points and binding classes were verified against the
installed game assemblies and a current working code mod. Using the current
game-panel lifecycle lowers API-drift risk and keeps the spike representative
of the panel that later phases will ship.

Consequence: `InGamePreviewUISystem` owns panel visibility and small health
state during Phase 7A, while the `.mjs` frontend owns presentation. The button
changes the binding directly and the `Game` root mounts the panel when it is
true. This replaced an initial `game.togglePanel`-only attempt whose button
appeared but whose panel did not mount in the owner's game. Registration is
wrapped so preview failure cannot prevent exporter/settings initialization.
Any future entry or binding change still requires an in-game lifecycle smoke
test before Phase 7B or publication.

## Use A Small Netstandard Engine Instead Of Porting The Desktop Renderer

Decision: Phase 7B/7C introduce `MetroDiagram.Engine` as a dependency-light
`netstandard2.0` runtime boundary. It owns immutable snapshot DTOs, stable
revision calculation, exporter-compatible JSON writing, and compact geographic
and schematic-anneal SVG rendering. The CS2 mod references this assembly; the
existing net8 Viewer/CLI renderer remains unchanged.

Reason: the desktop rendering assembly depends on a broad net8 and desktop API
surface and cannot safely be referenced by the CS2 net48 mod. Linking or
mass-copying its implementation would make game compatibility fragile. A small
engine keeps the captured data contract shared, preserves stops/pathPoints
semantics, and avoids implementing layout in the frontend.

Consequence: portable output is tested for schema, route/station semantics,
determinism, valid XML, empty networks, and a 200-station budget. It is not
claimed to be byte-identical to the advanced desktop renderer. Phase 7D now
consumes the portable engine through a bounded cache; no game UI callback may
read ECS directly. The public PDX build remains Beta.3 until owner acceptance.

## Queue Preview Operations And Keep Viewport Interaction Client-side

Decision: Phase 7D/7E UI triggers only queue capture/render/export/save work for
`InGamePreviewUISystem.OnUpdate`. Pan, zoom, and fit change only the frontend
image transform; layout and label changes rerender the cached immutable
snapshot.

Reason: ECS access from browser callbacks would make game-thread ownership
unclear, while rerendering for every viewport gesture would introduce avoidable
latency. The snapshot revision and option-keyed cache already provide the
correct invalidation boundary.

Consequence: opening without a cached map queues one capture; reopening an
unchanged map restores the current SVG immediately. JSON export continues to
use `RealMetroJsonExporter`, while SVG save uses the configured folder and a
separate latest/snapshot writer. The frontend never implements layout logic,
and exporter ECS/schema behavior remains unchanged.

## Mount In-Game Preview SVG Inline Instead Of Using A Data URI

Decision: the game UI mounts the trusted SVG produced by
`MetroDiagram.Engine` directly as responsive inline SVG. It removes XML/doctype
headers for HTML embedding but does not rewrite route geometry or saved SVG.

Reason: the first real Phase 7D/E validation proved capture and rendering were
successful while the map area stayed blank. Inline SVG removes URL-size/CSP
ambiguity and preserves the existing `viewBox`, pan, zoom, and fit transforms.
Follow-up validation showed that data transport was not the actual blank-canvas
root cause; the complete payload reached the UI.

Consequence: only renderer-owned, XML-escaped SVG is mounted; external JSON or
arbitrary HTML is never inserted. Preview state includes `svgLength`, and the
frontend shows an explicit payload-missing message if C# reports an SVG but the
binding is empty. The exporter, schema, snapshot, and portable renderer are
unchanged.

## Avoid Mixed Percentage And Rem Calculations In CS2 UI

Decision: size the in-game SVG layer with absolute `top/right/bottom/left`
offsets. Do not use expressions such as `calc(100% - 28rem)` in CS2 UI code.

Reason: Coherent UI logs `Combining percents in calc() expressions with other
types is not supported!` and leaves the affected layer without usable
dimensions. This produced a white canvas even though C# generated and bound a
complete SVG.

Consequence: game UI source tests reject `calc(100%`, and layout code should
prefer flex sizing or same-unit edge positioning. This is frontend-only; no
exporter, schema, snapshot, or route-rendering behavior changes.

## Default The In-Game Preview To Geographic

Decision: open the Phase 7 game panel on geographic by default, with a one-time
migration for older development preferences. Keep schematic available and keep
the desktop Viewer/CLI product default unchanged.

Reason: after the blank-canvas fix, the owner confirmed that the geographic
portable render is currently the clearer and more trustworthy in-game view.
The game panel should lead with the best current experience without weakening
the longer-term schematic work or changing exported data.

Consequence: this is a presentation preference only. Exporter ECS logic, JSON
schema, snapshot contents, route geometry, and desktop defaults are unchanged.
Once migrated, an explicit in-game user layout choice persists normally.

## Inherit The CS2 Locale-Aware Font In Inline SVG

Decision: remove explicit descendant font-family attributes when mounting the
trusted renderer SVG in the game panel and set the SVG root to CS2's
`var(--fontFamily)`. Standalone portable SVG uses an explicit Overpass/Noto CJK
fallback stack.

Reason: the game already loads Noto Sans SC/TC/JP/KR according to locale, but
the renderer's explicit Arial declaration bypassed that stack and produced tofu
boxes for Chinese names. Font inheritance fixes presentation without touching
the source strings.

Consequence: inline preview typography follows the active game/mod locale and
saved SVG remains portable. Localization or font failure remains isolated from
capture, export, and panel startup.

## Use Game Buttons For In-Game Preview Toggles And Commands

Decision: represent binary preview filters as pressed `ui.Button` controls with
a switch indicator, and use CS2 `flat`/`primary` button variants for panel
commands and selected layouts. Do not use native HTML checkbox inputs in the
Coherent panel.

Reason: Coherent rendered the native checkboxes as white input boxes, and the
unspecified button theme produced conspicuous white command buttons. The game
button component provides the expected focus, input sounds, disabled state, and
visual language.

Consequence: bindings and persisted settings are unchanged. Tests protect the
control semantics and theme variants; future visual adjustment should change
only spacing/colors, not reintroduce native form controls.

## Use Mouse Capture Semantics And A Marker-aware Portable Map Frame

Decision: use mouse-down plus window-level mouse-move/mouse-up listeners for
the in-game map viewport, and calculate one renderer safe frame that includes
the largest station marker and route stroke allowance.

Reason: CS2 Coherent did not reliably support the original Pointer Events
capture path. Separately, projecting station centers to the mathematical map
edge allowed the visible marker or its label to leave the SVG canvas.

Consequence: pan/zoom state remains UI-only, while both portable layouts share
the same visible-content bounds. Exporter data, schema, route geometry, and
desktop renderer behavior remain unchanged.

## Keep The In-game Schematic Recommendation Contextual And Subdued

Decision: show a small desktop Viewer recommendation beside the existing
pan/zoom hint only while the in-game schematic layout is selected. Do not show
it in geographic mode and do not use a banner, modal, or warning color.

Reason: geographic is the reliable in-game default, while the portable
schematic remains useful but less mature than the desktop renderer. The user
should receive the expectation without losing map space or feeling blocked.

Consequence: the hint is presentation-only and bilingual. Phase 7G also
coalesces redundant synchronous refresh/export render requests, with no changes
to ECS capture, JSON schema, or rendering semantics.

## Measure The Synchronous Preview Pipeline Before Adding Concurrency

Decision: keep the game-thread capture/controller synchronous, add categorized
timings and lifecycle counters, use a four-entry LRU render cache, and cancel
only disposable visual work when the panel closes.

Reason: ECS capture is intentionally constrained to the game thread, and the
controller already executes at most one operation per update. Adding worker
threads before measuring capture versus render cost would increase lifecycle
risk without identifying the real bottleneck.

Consequence: logs and UI state can distinguish capture cost, renderer cost,
cache reuse, and duplicate requests. Explicit JSON exports survive panel close;
map output, exporter contracts, and schema remain unchanged. Async work should
be considered only if owner stress evidence shows a measured blocker.

## Separate The Phase 7 Candidate From Public Versioning

Decision: package Phase 7H into `artifacts\release-candidates` with a candidate
label and complete hashes while leaving all embedded/public version sources at
Beta.3 until the owner accepts the exact candidate.

Reason: the PDX listing is public and Beta.3 is the known-good fallback. Reusing
the normal release directory or editing `PublishConfiguration.xml` before game
acceptance creates avoidable overwrite or accidental-publication risk.

Consequence: the private bundle records both candidate identity and embedded
baseline version. After acceptance, version sources move together to Beta.4,
the full preflight is rerun, and only then are GitHub and PDX updated.

## Publish The Accepted Phase 7 Candidate As Beta.4

Decision: after owner acceptance of `phase7-rc1` on 2026-07-13, move all code,
Viewer, generator, documentation, and PDX version sources together to
`v0.1.0-beta.4`, rebuild from the final committed source, and update the
existing public ModId `146643` rather than creating another listing.

Reason: Phase 7 adds a substantial read-only in-game preview but preserves the
exporter schema and established desktop workflow. The private candidate passed
mechanical verification and owner-visible game testing, so keeping it hidden
behind the Beta.3 version would make support and diagnostics ambiguous.

Consequence: Beta.4 is the first public release with the game-native preview.
Geographic remains the reliable in-game default; schematic preview stays
secondary and recommends the desktop Viewer. GitHub and PDX publication must
use binaries rebuilt after the Beta.4 commit so product versions and build
hashes remain traceable.

## Harden The Portable Schematic Instead Of Porting The Desktop Renderer

Decision: keep the in-game schematic on the small `MetroDiagram.Engine`
renderer and fix its route-chain normalization, deterministic polish, and
collision-aware labels. Do not copy the desktop cartographic renderer into the
CS2 mod.

Reason: the game renderer must remain dependency-light, bounded, and compatible
with `netstandard2.0`. The reported poor result came from mirrored service stop
chains and simplistic label placement, not from missing exporter data. Porting
the full desktop renderer would increase runtime and packaging risk before
addressing those concrete causes.

Consequence: geographic remains the reliable in-game default and the portable
schematic remains secondary. A dedicated audit script renders the exact game
profiles offline. Exporter ECS logic, JSON schema, and snapshot semantics remain
unchanged, and post-Beta.4 publication requires owner game validation.

## Port The Desktop Layout Math Into The Portable Engine

Decision: keep the dependency-light `MetroDiagram.Engine` assembly (no reference
to the net8 desktop renderer), but port the desktop schematic-anneal MATH into
it: the exact cost terms and weights (octilinear 4.5, short-edge 2.0, long-edge
0.5, bend 1.5, crossing 8.0, clearance 4.0 with same-line mask exemption,
geographic anchor 0.05), the adaptive temperature schedule with best-state
tracking, a range-2 greedy polish, the minimum-spacing hard gate, post-anneal
fit-to-frame scaling with canvas height adaptation, and parallel shared
corridors with same-color lane collapsing and miter-joined corners.

Reason: "cannot reference the assembly" never meant "cannot own the same
algorithms" - every missing piece is dependency-free geometry that fits
netstandard2.0. The audited quality gap between the game panel and the desktop
Viewer (free-angle diagonals, stacked shared corridors, under-filled canvas,
refresh-dependent layouts) came precisely from the simplified cost model, not
from the panel or the snapshot.

Consequence: the solver runs on int-indexed arrays (string-dictionary lookups
dominated the runtime; 1.15s -> 0.32s for the 59-station reference city in
Release), uses the fixed xorshift seed from the desktop renderer so layouts are
identical across refreshes AND across runtimes (mono vs .NET), and stays within
a bounded in-game budget (~320 ms schematic, ~30 ms geographic). Output is not
byte-identical to the desktop renderer but optimizes the same objective.
Remaining desktop-only refinements: label side-scoring parity, route badges,
express stripes, marker hierarchy.
