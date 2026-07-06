# Scripts Index

PowerShell scripts are grouped by workflow. Run scripts from the repository root unless a script says otherwise.

## Shared Helpers

- `MetroScriptCommon.psm1` - shared helpers imported by the diagnostic/validation scripts, so the same logic is not copy-pasted. Contains the SVG-geometry scoring primitives (`Parse-Points`, `Get-Distance`, `Get-AngleDegrees`, `Get-OctilinearDelta`, `Get-SegmentIntersection`) plus small filesystem/naming utilities (`Get-FullPath`, `Convert-ToDouble`, `Convert-ToSafeName`). The authoritative layout score is produced by the C# CLI (`MetroDiagram.Cli --emit-layout-score`); these SVG-level primitives remain only for auditing that the CLI does not emit (debug overlays, stroke-width checks).

## Build, Publish, Release

- `validate-local.ps1` - runs the normal local validation flow: offline solution build plus offline tests. Add `-IncludeModBuild` to also build the CS2 mod project when the CS2 modding toolchain environment is configured.
- `publish-viewer-self-contained.ps1` - publishes the Windows Viewer as a self-contained win-x64 package.
- `publish-viewer-framework-dependent.ps1` - publishes the Windows Viewer as a framework-dependent package.
- `publish-mod.ps1` - publishes the existing Paradox Mods listing only. Use `-Mode NewVersion` for a binary/version upload or `-Mode UpdateConfiguration` for metadata-only updates. It refuses to run without a configured `ModId` and intentionally does not support `PublishNewMod`. Use `-WhatIf` to verify the target/profile without uploading.
- `generate-viewer-icon.ps1` - regenerates the Viewer app icon PNG/ICO assets from the deterministic icon drawing script.
- `package-alpha-release.ps1` - builds/tests, publishes Viewer, copies docs/samples/mod artifacts when available, and creates an alpha release folder/zip.

## Local CS2 Mod Workflow

- `deploy-local-mod.ps1` - copies the latest generated local mod output into the Cities: Skylines II local ModsData folder. Restart CS2 after running it.

## Alpha Validation

- `generate-alpha-validation-bundle.ps1` - creates a per-city alpha validation bundle with export JSON, diagnostics, geographic/schematic outputs, `schematic-map` product candidate output, optional screenshots, `manifest.json`, notes, and feedback template. Use `-SkipPng` for a fast SVG/diagnostics-only bundle.
- `generate-alpha-validation-set.ps1` - batch wrapper for multi-city alpha validation. It can scan recent timestamped exports, include the latest export, generate one bundle per input, and refresh the alpha validation index. Use `-SkipPng` for daily triage; omit it for full screenshot bundles.
- `summarize-alpha-validation-bundles.ps1` - scans generated alpha validation bundles and writes `artifacts\alpha-validation\index.md` plus `index.csv` so multi-city review status is visible in one place.
- `generate-schematic-regression-gate.ps1` - runs the current schematic-map regression gate across latest exports and regression samples, writing per-case geographic/schematic-map outputs, audit files, screenshots unless `-SkipPng` is used, and a `regression-summary.csv`.
- `generate-primary-city-baseline.ps1` - refreshes the primary city regression baseline.
- `generate-product-candidate-map.ps1` - creates a focused product-candidate SVG/PNG, usually `schematic-map + ultra`, for human review of the current best map output.
- `compare-product-candidates.ps1` - creates an HTML/Markdown/CSV side-by-side comparison of recent product candidate PNGs plus schematic-map score/audit metrics.
- `compare-schematic-layouts.ps1` - renders the sample corpus (plus optional real exports via `-ExtraInputJson`) in `schematic-map` and `schematic-anneal`, collects the shared CLI layout scores, and writes corpus median/worst comparison tables to `artifacts\layout-comparison\<timestamp>`. This is the acceptance evidence for layout changes.

## Route Geometry And Schematic Diagnostics

- `analyze-metro-export-json.ps1` - summarizes an exported metro JSON and pathPoints coverage.
- `generate-path-geometry-comparison.ps1` - renders stops/pathPoints/schematic comparison SVGs.
- `generate-schematic-v2-diagnostics.ps1` - generates topology, shared corridor, route-guide, and schematic-v2 comparison diagnostics.
- `analyze-schematic-map-svg.ps1` - audits schematic-map SVGs, writes score/route/crossing CSVs, and creates `schematic-map-debug.svg` with visual overlays for non-octilinear segments and interior crossings.
- `analyze-svg-render-debug.ps1` - summarizes SVG route/debug attributes.
- `analyze-visual-continuity.ps1` - reports route fragment and visual continuity risks.
- `capture-svg-screenshot.ps1` - renders an SVG to PNG using installed Microsoft Edge in headless mode.

## Notes

- Scripts intentionally do not modify exporter data or JSON schema.
- Generated outputs should go under `artifacts\`.
- If PowerShell blocks script execution locally, use `powershell -NoProfile -ExecutionPolicy Bypass -File <script>`.
