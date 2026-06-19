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

## Keep Route-only Synthetic Bends Opt-in

Decision: route-only synthetic bends for schematic-map remain available as an explicit experiment, but are not enabled by default.

Reason: moving station nodes can damage topology, interchange relationships, and shared-platform alignment, and a route-only bend can sometimes help. However, the first Sheffield experiment reduced non-octilinear warnings while making the whole map feel less natural than the previous candidate.

Consequence: product candidates keep synthetic bends off unless explicitly requested. The audit still tracks `data-schematic-map-synthetic-bends` so future experiments can be compared, but visual review takes precedence over warning-count reduction.

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
