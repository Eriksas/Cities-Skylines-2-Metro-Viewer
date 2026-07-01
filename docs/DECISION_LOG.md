# Decision Log

This file contains current high-level decisions only. Full historical decisions before the 2026-06-18 cleanup are archived at:

```text
docs\archive\2026-06-18-doc-consolidation\DECISION_LOG.full.md
```

## Keep Geographic As Alpha Default

Decision: `geographic + UsePathPoints + service family merge` remains the alpha recommended output.

Reason: it is the most faithful to exported CS2 geometry and has passed the broadest manual validation.

Consequence: schematic modes must not break or replace geographic behavior.

## Treat Schematic Work As Render-only

Decision: schematic-v2 and schematic-map work must remain in renderer/viewer/CLI layers.

Reason: the exporter and `metro-export.json` schema are already useful and should not churn while visual layout evolves.

Consequence: do not modify `RealMetroJsonExporter`, raw `line.stops`, raw `line.pathPoints`, or schema for layout polish.

## Retire Schematic-lite From Viewer

Decision: remove legacy `schematic-lite` from the Viewer layout choices.

Reason: schematic-lite became a patch-based dead end and is less useful than schematic-v2/schematic-map.

Consequence: schematic-lite remains available through CLI/scripts for historical comparison only.

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
