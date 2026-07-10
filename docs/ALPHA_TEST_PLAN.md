# Alpha Test Plan

Use this plan for `v0.1.0-beta.1`.

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
