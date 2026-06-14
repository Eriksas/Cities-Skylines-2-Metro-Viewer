# Scripts Index

PowerShell scripts are grouped by workflow. Run scripts from the repository root unless a script says otherwise.

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

- `generate-alpha-validation-bundle.ps1` - creates a per-city alpha validation bundle with export JSON, diagnostics, geographic/schematic outputs, screenshots, notes, and feedback template.
- `generate-primary-city-baseline.ps1` - refreshes the primary city regression baseline.
- `generate-product-candidate-map.ps1` - creates a focused product-candidate SVG/PNG, usually `schematic-v2 + transit-map + ultra`, for human review of the current best map output.

## Route Geometry And Schematic Diagnostics

- `analyze-metro-export-json.ps1` - summarizes an exported metro JSON and pathPoints coverage.
- `generate-path-geometry-comparison.ps1` - renders stops/pathPoints/schematic comparison SVGs.
- `generate-schematic-v2-diagnostics.ps1` - generates topology, shared corridor, route-guide, and schematic-v2 comparison diagnostics.
- `analyze-svg-render-debug.ps1` - summarizes SVG route/debug attributes.
- `analyze-visual-continuity.ps1` - reports route fragment and visual continuity risks.
- `capture-svg-screenshot.ps1` - renders an SVG to PNG using installed Microsoft Edge in headless mode.

## Notes

- Scripts intentionally do not modify exporter data or JSON schema.
- Generated outputs should go under `artifacts\`.
- If PowerShell blocks script execution locally, use `powershell -NoProfile -ExecutionPolicy Bypass -File <script>`.
