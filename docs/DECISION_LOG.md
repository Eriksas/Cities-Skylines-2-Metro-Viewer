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

Decision: exact shared platform corridors in schematic-v2/schematic-map should mask the underlying normal route strokes before drawing parallel corridor overlays, and should assign overlays through `VisibleLaneResolver` rather than raw display family count.

Reason: drawing normal route strokes and narrower parallel overlays on the same segment made close parallel tracks look uneven or accidentally thicker. Separately, same-number branch families such as `7号线` / `7号线支线` can share one physical track and should not be split just because their raw family keys differ.

Consequence: the visible shared-platform segment is now produced by one consistent parallel-corridor layer with debug attributes, while raw route geometry and export data remain unchanged. Different visible lines still separate, but same-number same-color branches collapse into a single visible lane. Future schematic shared-platform logic should use the resolver instead of local family-count heuristics.

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
