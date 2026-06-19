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
- [ ] Verify the release folder and zip use the intended version and do not overwrite the previous alpha package.
- [ ] Verify zip contents include:
  - [ ] `Mod`
  - [ ] `Viewer`
  - [ ] `docs`
  - [ ] `samples`
  - [ ] `README.md`
  - [ ] `QUICK_START.md`
  - [ ] `KNOWN_ISSUES.md`
  - [ ] `CHANGELOG.md`
  - [ ] `build-info.txt`
- [ ] Launch Viewer exe from the release package.
- [ ] Open sample JSON.
- [ ] Open real `metro-export.json`.
- [ ] Use `Open Default Export` to open the latest export.
- [ ] Use `Open JSON` to manually open a timestamped snapshot.
- [ ] Save SVG.

## In-Game Verification

- [ ] Copy mod to CS2 local Mods.
- [ ] Start Cities: Skylines II.
- [ ] Enable/load the mod.
- [ ] Load a city.
- [ ] Verify `Export Test Metro JSON`.
- [ ] Verify `Export Transport Debug Dump`.
- [ ] Verify `Export Real Metro JSON` in-game.
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

- [ ] Confirm README states the release is alpha, not stable.
- [ ] Confirm known issues are current.
- [ ] Confirm feedback template is included.
- [ ] Confirm build-info version, build time, and commit are present.
