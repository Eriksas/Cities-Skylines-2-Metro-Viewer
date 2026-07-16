# Alpha Test Plan

Use this plan for `v0.1.0-beta.6`.

The testing unit is an alpha validation bundle, not a loose screenshot.

## Recommended Baseline

Review in this order:

1. `schematic-anneal`
2. `geographic`
3. `schematic-map`
4. `schematic-v2`

`schematic-anneal` is the alpha recommended product output.

`geographic` is the faithful-geometry fallback and diagnosis reference.

`schematic-map` is the previous pass-stack product candidate retained for
comparison.

`schematic-v2` is the topology/diagnostic schematic base.

## Generate A Validation Bundle

For latest export:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName <city-or-case-name>
```

For a snapshot:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson 'D:\CS2MetroDiagram\exports\metro-export-<city>-<timestamp>.json' `
  -CaseName <city-or-case-name>
```

Refresh the index:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-alpha-validation-bundles.ps1
```

For a batch pass over recent exported snapshots:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-set.ps1 `
  -IncludeLatest `
  -LatestCount 5 `
  -SkipPng `
  -SkipZip
```

This is the recommended daily triage mode. It generates SVGs, diagnostics,
notes, manifests, and the validation index without spending time on every PNG
screenshot.

For a full screenshot batch, omit `-SkipPng`:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-set.ps1 `
  -IncludeLatest `
  -LatestCount 5 `
  -SkipZip
```

Use explicit inputs when comparing a known set of cities:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-set.ps1 `
  -InputJson @(
    'D:\CS2MetroDiagram\exports\metro-export-city-a-yyyymmdd-hhmmss.json',
    'D:\CS2MetroDiagram\exports\metro-export-city-b-yyyymmdd-hhmmss.json'
  ) `
  -LatestCount 0 `
  -SkipPng `
  -SkipZip
```

Bundle index:

```text
artifacts\alpha-validation\index.md
artifacts\alpha-validation\index.csv
```

## Run The Schematic Regression Gate

Use this before accepting renderer changes that affect schematic output. The
default candidate is `schematic-anneal`; pass `-CandidateLayout schematic-map`
only when intentionally auditing the older pass-stack mode:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1
```

Useful faster smoke mode:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1 -LatestExports 0 -SkipPng
```

The gate writes:

```text
artifacts\schematic-regression\<timestamp>\index.md
artifacts\schematic-regression\<timestamp>\regression-summary.csv
artifacts\schematic-regression\<timestamp>\cases\...\geographic-baseline.svg
artifacts\schematic-regression\<timestamp>\cases\...\schematic-anneal.svg
artifacts\schematic-regression\<timestamp>\cases\...\schematic-anneal-audit\schematic-map-score.csv
```

When PNG generation is enabled, each case also includes:

```text
geographic-baseline.full.png
schematic-anneal.full.png
schematic-anneal-debug.full.png
```

Treat `pass` as "cleared current automated safety checks", not as a substitute
for visual review. Treat `needs-review` as a prompt to inspect the generated PNGs
and debug overlay.

## Bundle Contents To Check

Each current bundle should contain:

- `metro-export.json`
- `metro-export-diagnostics.txt`, when available
- `baseline-geographic.svg`
- `baseline-geographic.full.png`, when PNG screenshots were generated
- `schematic-anneal.svg`
- `schematic-anneal.full.png`, when PNG screenshots were generated
- `schematic-map.svg`
- `schematic-map.full.png`, when PNG screenshots were generated
- `schematic-v2.svg`
- `schematic-v2.full.png`, when PNG screenshots were generated
- `visual-continuity-summary.txt`
- `schematic-v2-diagnostics\`
- `manifest.json`
- `notes.md`
- `feedback-template-filled.md`

Older bundles may not have every file. The index script marks missing manifests or PNGs.

## City Scenarios

Collect cases across these shapes:

- No metro city.
- Simple one-line city.
- Small three-line city.
- Multi-line ordinary city.
- Loop line city.
- Branch line city.
- Airport/express/skip-stop service city.
- Dense interchange city.
- City with mostly default CS2 station asset names.
- City with deliberate custom station names.
- City with virtual-transfer-like nearby same-name stations.

## Geographic Review Checklist

- Routes are continuous.
- Route width is consistent.
- Station markers sit on routes.
- Interchanges are visible.
- Important labels are readable.
- Default/non-important station labels can be hidden.
- Legend does not obscure the main network.
- City name and title look reasonable.

## Schematic Product Review Checklist

- Lines mostly use horizontal, vertical, and 45-degree directions.
- Obvious straight corridors do not become awkward zigzags.
- Product-map header reads as a clear official-style title bar.
- Bottom legend is readable, separated into key/line/symbol areas, and does not
  dominate the network.
- Station hierarchy is clear: ordinary stops, terminals, and transfers are
  visually distinct without cutting routes apart.
- Important station labels are more prominent, while default/non-important
  station labels can still be hidden.
- Size presets should keep
  the same route shape, long non-octilinear spans should be bent into clean
  0/45/90-degree legs, and shallow ordinary kinks should be straightened when
  topology permits.
- Check whether the full map looks better, not only whether audit warning counts
  or anneal cost decrease.
- Intentional schematic doglegs may be reported as informational
  source-direction skips. This is expected when the route stays octilinear and
  the visual result is cleaner than the raw geographic direction.
- True route crossings are marked or visually understandable.
- Interchanges are preserved.
- Shared/parallel corridors do not hide one line behind another.
- Route badges do not collide badly with labels.
- Small cities are not buried in excessive blank space.
- The output feels closer to an official metro map than raw geography.

When reviewing a product candidate folder, also check:

- `schematic-map-score.csv`
- `schematic-map-crossings.csv`
- `schematic-map-turns.csv`

Use these files to find recurring issues and compare versions. Do not accept a worse-looking map only because the numeric score is higher.

To compare recent product candidates side by side:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-product-candidates.ps1 -LatestCount 4
```

Open the generated `comparison.html` or `comparison.full.png` before deciding whether a layout change is visually better.

## Schematic-v2 Review Checklist

- Stop order is preserved.
- Adjacent stops remain connected.
- Interchange stations remain shared nodes.
- Express/rapid variants do not create misleading independent route geometry.
- Diagnostics explain any shared corridor or route-guide behavior.

## Feedback Package

For each issue, attach:

- The validation bundle path.
- The JSON snapshot path.
- `metro-export-diagnostics.txt`.
- The relevant PNG/SVG.
- Viewer settings if changed.
- A short description of what looks wrong and what the expected map should show.

Use:

```text
docs\FEEDBACK_TEMPLATE.md
```

## Current Do-not-start List

Do not start these unless explicitly requested:

- Exporter schema change.
- Geographic redesign.
- PNG/PDF product export.
- Manual drag editor.
- New style preset.
- Reopening old schematic-lite patch work.
- Changing the game-wide language from this utility mod.

## Mod Options Localization Smoke

After deploying a new mod build and restarting CS2:

1. Open `Options > CS2 Metro Diagram > Main`.
2. Select `Auto`; verify Chinese game locale shows Chinese mod controls and an
   English game locale shows English controls.
3. Select `English`; verify only this mod page changes to English.
4. Select `简体中文`; verify only this mod page changes to Chinese.
5. Export real metro JSON in both explicit language modes and confirm the output
   path and JSON content are unaffected.

## Verification For Code Changes

```powershell
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

## Phase 7D/7E In-Game Preview Checklist

Use the E-drive development build and restart CS2 before testing:

```text
E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods\CS2 Metro
```

1. At the main menu/no-city state, open the panel and confirm a clear no-city
   message appears without disabling Options or export.
2. Load a city with no metro and confirm the no-metro state does not crash.
3. Load a real metro city. Opening the panel should automatically show the real
   map, city name, line count, station count, and render time.
   If counts/render time appear but the map surface is blank, inspect
   `CS2_Metro.Mod.log` for `svgChars` and `Player.log` for `[UI] [ERROR]`.
   This indicates a UI sizing/payload failure rather than an ECS capture
   failure. In particular, no mixed percentage/rem `calc()` errors should be
   present after the 2026-07-13 sizing hotfix.
4. Close/reopen repeatedly. The unchanged map should appear from cache without
   another visible capture delay or duplicate top-right buttons.
5. Change the network, press Refresh, and confirm the revision/map/counts update.
6. On the first open after this build, confirm Geographic is selected and the
   map uses true route geometry. Switch to Schematic and back; both must remain
   valid, and the later explicit choice must persist after close/reopen.
7. Test wheel zoom, pointer drag, zoom buttons, and Fit. None should trigger a
   backend rerender.
8. Toggle default station names and crowded labels, close/reopen, and confirm
   the preferences persist.
9. Press Export JSON and verify existing latest plus timestamped JSON and
   diagnostics outputs still work.
10. Press Save current SVG and verify `metro-diagram.svg` plus a timestamped
    SVG under `exports`; open the file and confirm it matches the visible layout.
11. Trigger or simulate an unavailable/error state and confirm details are
    visible/copyable and the panel remains closable.
12. Inspect the game log/UI console for uncaught errors. Do not publish to PDX
    until this checklist passes and the owner explicitly approves the build.

## Phase 7F Visual And Localization Checklist

1. With the mod language set to Auto and the game in Simplified Chinese, confirm
   city name, station labels, line names, title, summary, and legend contain
   readable Chinese glyphs with no tofu boxes.
2. Select English in the mod options and confirm panel controls/status text use
   English without changing exported station names. Select Simplified Chinese
   and confirm controls/status text switch back.
3. Confirm localization/font failure cannot prevent opening/closing the panel,
   refreshing the city snapshot, exporting JSON, or saving SVG.
4. Review the panel at 1920x1080, 2560x1440, 3840x2160, and one ultrawide
   resolution where available. The map remains dominant; controls wrap without
   clipping or covering the map; title, summary, filters, zoom controls, and
   notifications remain readable.
5. Verify keyboard/controller focus order where supported: close, layout,
   refresh/export/save, filters, then map controls. Mouse hover/focus states
   should remain visible.
6. Open a saved standalone SVG outside CS2 and confirm Chinese glyphs render
   with the Noto CJK fallback stack on the test machine.
7. Confirm `Show default station names` and `Hide crowded labels` appear as
   switch-style controls rather than text inputs. Clicking anywhere on each
   control must toggle it, show a clear selected state, rerender the map, and
   persist after close/reopen. Layout and command buttons should use the same
   dark CS2 visual language with no white browser-style buttons.
8. At 100% zoom, drag the map in all directions. Repeat after zooming in, move
   the cursor outside the canvas while holding the button, then release. The map
   must follow smoothly and stop moving immediately after release.
9. In geographic and schematic views, inspect terminal/interchange stations at
   every edge. No station circle may be clipped, and edge labels must remain
   inside the map area rather than entering the legend or leaving the canvas.
10. Confirm geographic mode shows only the normal pan/zoom hint. Switch to
    schematic mode and confirm a small, subdued note recommends the desktop
    Viewer without covering the map or behaving like a warning banner.
11. Rapidly change a label option, then press Refresh or Export JSON. The panel
    should settle on the latest selected options without a second visible stale
    rerender.
12. Close the panel immediately after pressing Refresh or Save SVG, reopen it,
    and confirm no stale visual operation changes the reopened panel. Repeat
    with Export JSON and confirm the explicit export still completes.
13. Reopen the same unchanged network and toggle back to a previously rendered
    option combination. Confirm `renderCacheHit` becomes true, cache entries do
    not exceed four, and the map appears without a full renderer delay.
14. In `CS2_Metro.Mod.log`, confirm Capture and Render entries include revision,
    counts, timings, and cache status. A slow test report should quote
    `captureMs`, `requestMs`, and `rendererMs` rather than only saying the panel
    felt slow.
