# External Audit Summary

Generated: 2026-06-23

This document summarizes the current CS2 Metro Diagram state for external review. It intentionally separates product capability, visual quality, and engineering risk so reviewers can give focused feedback.

## Audit Package

Current audit bundle:

`artifacts/external-audit/20260623-144955-external-audit-current/`

Key files:

- `product-candidate.full.png` - current product-style schematic map output.
- `product-candidate.svg` - current SVG source.
- `official-reference-map-20260506.webp` - official transit-map style reference image provided by the user.
- `schematic-map-audit.txt` - automated visual/topology audit summary.
- `schematic-map-debug.full.png` - debug overlay image.
- `schematic-map-*.csv` - machine-readable diagnostics for route segments, crossings, widths, parallel corridors, turns, and score.
- `metro-export.json` - input export used for this audit bundle.

## Current Capability

- CS2 code mod exports real subway/metro data.
- Export folder can be configured in the mod settings, with shortcuts for Documents, Desktop, and `D:\CS2MetroDiagram`.
- Latest export and timestamped snapshots are both supported.
- CLI can render geographic, schematic-v2, and product-style `schematic-map` SVGs.
- WPF Viewer can open exported JSON, preview SVG, switch layouts, adjust render settings, and save SVG.
- Product-style `schematic-map` is the current direction for official-map-like output.
- Geographic rendering remains the safest baseline when schematic output needs comparison.

## Current Recommended Output

For product-facing map review:

```text
layout = schematic-map
style = transit-map
size = ultra
UsePathPoints = true
HideGenericStationLabels = true
HideCrowdedLabels = true
```

For alpha-safe fallback:

```text
layout = geographic
UsePathPoints = true
service family merge = enabled
shared corridor = disabled
express stripe = disabled
```

## Visual QA Notes

Verdict: accept with caveats for external visual audit.

What is working well:

- Route strokes are visually consistent.
- Station markers are readable and remain attached to route geometry.
- Route badges no longer collide in the current audit case.
- Shared/parallel corridors are visible for the known 7/7-branch and platform-adjacent cases.
- The current map is much closer to an official schematic than earlier geographic or schematic-lite outputs.

Known caveats:

- Automated audit still reports 6 interior route crossings. These are currently rendered as direct pass-through crossings.
- A few route segments remain non-octilinear, though the route grammar score is high.
- Some labels are still small at full-network scale.
- Product-style framing works, but compact/small networks still need more auto-framing polish.
- The official reference map has much richer manual cartographic refinement than the current automatic renderer.

Automated audit snapshot:

```text
Route segments checked: 109
Route warnings: 5
Non-octilinear segments: 5
Short segments: 0
Badge-badge conflicts: 0
Badge-label conflicts: 0
Interior route crossings: 6
Sharp turn candidates: 0
Stroke-width consistency: 100
Overall automated score: 64.57 / 100
```

Interpretation: the low overall score is mostly driven by interior crossings. Manual review is still required because direct pass-through crossings are currently a deliberate simpler rendering choice.

## Engineering Review

Low-risk optimization completed in this pass:

- `scripts/generate-product-candidate-map.ps1` no longer runs schematic-map audit twice. It now renders and screenshots into a temp folder, moves the candidate to the final folder, runs the audit once there, captures the debug overlay, and writes `notes.md` using final paths.

Main technical debt:

- `src/MetroDiagram.Rendering/MetroSvgRenderer.cs` is too large and now mixes geographic rendering, schematic-lite legacy code, schematic-v2 topology work, product-style schematic-map logic, diagnostics, labels, stations, and style rules.
- `src/MetroDiagram.Tests/Program.cs` has grown into a very large single-file test harness.
- Legacy schematic-lite code is still present for compatibility/regression, but it should not be the future product direction.
- Some diagnostics scripts overlap in purpose and should eventually be consolidated around product-style schematic-map regression bundles.

Recommended refactor sequence:

1. Split renderer internals into focused files or helper classes:
   - geographic rendering
   - station and label rendering
   - schematic-v2 topology helpers
   - schematic-map product layout
   - SVG diagnostics/debug attributes
2. Split tests by topic:
   - loader/schema tests
   - geographic renderer tests
   - schematic-v2 tests
   - schematic-map product-style tests
   - viewer/settings tests
3. Keep schematic-lite available only as legacy/diagnostic until removal is intentionally scheduled.
4. Preserve exporter and JSON schema contracts while visual work continues in rendering and Viewer.

## Suggested External Audit Questions

Ask reviewers to focus on:

1. Does `product-candidate.full.png` preserve believable line topology?
2. Are shared or parallel corridors readable, especially around 7/7-branch and platform-adjacent stations?
3. Are direct pass-through crossings acceptable for alpha, or should crossings get a specific visual convention?
4. Are station markers and labels readable enough for a first public alpha?
5. Does the official-map header/legend direction feel appropriate, or should it be simplified?
6. Which output should be the default public preview: geographic, schematic-v2, or schematic-map?

## Files For Reviewers

Current generated project image:

`E:\CS2\CS2 Metro\artifacts\external-audit\20260623-144955-external-audit-current\product-candidate.full.png`

Official reference image:

`E:\CS2\CS2 Metro\artifacts\external-audit\20260623-144955-external-audit-current\official-reference-map-20260506.webp`

Debug overlay:

`E:\CS2\CS2 Metro\artifacts\external-audit\20260623-144955-external-audit-current\schematic-map-debug.full.png`

