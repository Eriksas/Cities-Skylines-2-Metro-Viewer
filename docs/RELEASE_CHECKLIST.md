# Release Checklist

Use this checklist for future releases.

## Build And Test

- [ ] Update version number.
- [ ] Update `docs/CHANGELOG.md`.
- [ ] Update `docs/KNOWN_ISSUES.md`.
- [ ] Freeze the accepted primary city baseline under `artifacts\primary-city-baseline\history\<timestamp>`.
- [ ] Confirm the recommended baseline settings are geographic, `UsePathPoints`, service family merge enabled, shared corridor disabled, and express stripe disabled.
- [ ] Run schematic regression gate:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1
```

- [ ] Review `artifacts\schematic-regression\<timestamp>\index.md` and confirm no `needs-fix` cases.
- [ ] Build offline solution:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
```

- [ ] Run tests:

```text
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

- [ ] Build CS2 mod with the local CS2 modding toolchain.
- [ ] If `Mod.OnLoad`, `Mod.OnDispose`, settings, Options UI, or localization
  changed, run an in-game smoke test before updating the public PDX version.
  Offline build/tests and post-processing do not exercise the CS2 lifecycle.
- [ ] Inspect `CS2_Metro.Mod.log`: initialization must reach the export-directory
  log without an outer `failed to initialize` error. Locale warnings may be
  non-blocking only when the Options page and exporter remain usable.
- [ ] Run Viewer publish script:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
```

- [ ] Run release package script:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1
```

## Package Verification

- [ ] Verify release folder exists.
- [ ] Verify release zip exists.
- [ ] Verify the release folder and zip use the intended version and do not overwrite a previous package.
- [ ] Verify zip contents include:
  - [ ] `MetroDiagram.Viewer.exe`
  - [ ] `README.txt`
  - [ ] `build-info.txt`
- [ ] Launch Viewer exe from the release package.
- [ ] Open sample JSON.
- [ ] Open real `metro-export.json`.
- [ ] Use `Open Default Export` to open the latest export.
- [ ] Use `Open JSON` to manually open a timestamped snapshot.
- [ ] Save SVG, PNG, and PDF.

## In-Game Verification

- [ ] For lifecycle-sensitive changes, complete the staged/private in-game smoke
  before public publication. Do not use the public subscriber base as the first
  runtime test.
- [ ] Publish the validated new version to the existing Paradox Mods listing.
- [ ] Allow the subscribed mod to update; do not copy a local build into the
  game Mods directory during the normal release workflow.
- [ ] Start Cities: Skylines II.
- [ ] Enable/load the mod.
- [ ] Load a city.
- [ ] Verify `Export Test Metro JSON`.
- [ ] Verify `Export Transport Debug Dump`.
- [ ] Verify `Export Real Metro JSON` in-game.
- [ ] Verify the mod `Export Folder` setting can use at least one preset and one custom folder.
- [ ] Confirm latest export and latest diagnostics exist.
- [ ] Confirm timestamped export and diagnostics snapshots exist.
- [ ] Export twice and confirm old snapshots are not overwritten.
- [ ] Confirm generated `metro-export.json` opens in Viewer.

## Baseline Verification

- [ ] Run primary city baseline generation:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\generate-primary-city-baseline.ps1
```

- [ ] Confirm baseline SVG, full PNG, visual continuity summary, and `notes.md` exist.
- [ ] Confirm the baseline has no obvious regression from the accepted alpha candidate image.

## Final Review

- [ ] Confirm README states the correct prerelease stage and does not call it stable.
- [ ] Confirm known issues are current.
- [ ] Confirm feedback template is included.
- [ ] Confirm build-info version, build time, and commit are present.
