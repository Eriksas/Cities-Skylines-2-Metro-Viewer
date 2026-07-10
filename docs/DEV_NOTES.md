# Development Notes

Living operational notes only. History:

- Journal 2026-06-19 .. 2026-07-07: `docs/archive/2026-07-10-dev-notes-journal/DEV_NOTES-journal-2026-06-19-to-2026-07-07.md`
- Everything before the 2026-06-18 cleanup: `docs/archive/2026-06-18-doc-consolidation/DEV_NOTES.full.md`

## Beta.1 Packaging - 2026-07-10

- Version sources are unified at `v0.1.0-beta.1`; Windows file version is
  `0.1.0.8`.
- The first beta packages SVG, PNG, and PDF output in the Viewer and CLI while
  retaining `schematic-anneal` as the default and `geographic` as the faithful
  fallback.
- `scripts\package-alpha-release.ps1` keeps its historical filename for
  compatibility, but reads the version dynamically and emits neutral release
  messages. Its Beta.1 output does not overwrite Alpha.7 artifacts.
- The Paradox Mods thumbnail source remains editable in Figma; its release badge
  and output-format copy were updated from Alpha.7 to Beta.1.
- This packaging pass does not publish to GitHub or Paradox Mods. Publish only
  after the generated Viewer and mod are manually smoke-tested.
- Routine mod delivery now uses the subscribed Paradox Mods listing. Do not run
  `scripts\deploy-local-mod.ps1` or copy builds into the game directory during
  normal release work; publish the new PDX version and let the subscription
  update it. Keep the local deploy script only as an explicit emergency/dev
  fallback.

## Alpha.7 Release - 2026-07-10

- Unified repository, Viewer, generator, and Paradox Mods version:
  `v0.1.0-alpha.7` / `0.1.0-alpha.7`.
- Paradox Mods listing: ModId `146643`, access level `Public`.
- Editable main promotional artwork:
  `https://www.figma.com/design/4BGPoAEwzZ2DMTJT8RLLLH`, frame `4:2`.
- Published thumbnail source in the mod project:
  `CS2 Metro\Properties\Thumbnail.png` (`950 x 500`).
- Use `scripts\publish-mod.ps1 -Mode NewVersion -SkipRestore` only after the
  game has been launched and the intended PDX account has signed in.

## Post-alpha.6 Hardening - 2026-07-10

- The CS2 options page now registers one dynamic localization source per
  supported locale. `InterfaceLanguage=auto` follows the game; `en` and
  `zh-HANS` force this mod's options text only. After a change,
  `LocalizationManager.ReloadActiveLocale()` refreshes the page.
- `SvgMapStyle.Auto` distinguishes convenience defaults from an explicit
  `Standard` choice. Product-layout defaults no longer overwrite explicit map
  style or disabled service-family merge.
- Renderer geometry cache keys contain geometry-affecting inputs, not label and
  presentation switches. This keeps manual/viewer presentation changes from
  rerunning the layout solver.
- Real export content is first written to same-directory temporary files. Each
  file is committed with `File.Replace`/`File.Move`, and latest JSON is committed
  last. ECS extraction and the JSON schema are unchanged.
- Product candidate and schematic regression scripts now default to
  `schematic-anneal`. Alpha bundles generate anneal plus geographic,
  schematic-map, and schematic-v2 comparisons.
- Shared script helpers (`Convert-ToSafeName`, `Get-PowerShellRunner`, and
  `Get-DefaultDiagnosticsPath`) live in `scripts\MetroScriptCommon.psm1`.
- Build and tests must still run sequentially. The CS2 mod build performs its
  post-process and local Mods deployment; restart CS2 before manual validation.

## Repo And Runtime

Workspace:

```text
E:\CS2\CS2 Metro
```

Common real export directory:

```text
D:\CS2MetroDiagram
```

The CS2 mod settings page also allows a custom export folder. Recommended
presets are `Documents\CS2MetroDiagram`, `Desktop\CS2MetroDiagram`, and
`D:\CS2MetroDiagram`.

Use PowerShell 7 (`pwsh`) for scripts when available. Older Windows PowerShell can still run many scripts, but screenshot/capture helpers are more reliable through `pwsh`.

## Build And Test

Run from repo root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1
```

The preflight script restores `CS2MetroDiagram.slnx`, builds the offline
solution, and runs the lightweight renderer test project. Use this before
committing or pushing. To keep the working folder light after verification:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1 -CleanBuildOutput
```

Manual equivalent:

```powershell
dotnet restore CS2MetroDiagram.slnx
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Run sequentially to avoid DLL locking.

GitHub Actions runs the same `scripts\preflight.ps1` workflow on pushes and
pull requests to `master`.

## CS2 Mod Build And Deploy

Build command template:

```powershell
$tool = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain'
$managed = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$userData = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II'
$unityProject = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\.cache\Modding\UnityModsProject'
$post = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe'
$mscorlib = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\mscorlib.dll'
$localMods = 'E:\CS2\CS2 Metro\artifacts\cs2-local-mods'
dotnet build "CS2 Metro.slnx" --no-restore /p:CsiiToolPath="$tool" /p:ManagedPath="$managed" /p:UserDataPath="$userData" /p:UnityModProjectPath="$unityProject" /p:ModPostProcessorPath="$post" /p:EntitiesVersion="1.3.10" /p:MSCORLIBPath="$mscorlib" /p:LocalModsPath="$localMods"
```

Deploy latest built mod before in-game testing:

```powershell
scripts\deploy-local-mod.ps1
```

Current deploy destination:

```text
E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods\CS2 Metro
```

Restart Cities: Skylines II after deploying.

## Real Export Behavior

The selected export folder is used by:

- `Export Real Metro JSON`
- `Export Test Metro JSON`
- `Export Transport Debug Dump`
- `Metro Track Geometry Debug`

Latest files:

```text
<export folder>\metro-export.json
<export folder>\metro-export-diagnostics.txt
```

Snapshot files:

```text
<export folder>\exports\metro-export-{citySlug}-{yyyyMMdd-HHmmss}.json
<export folder>\exports\metro-export-diagnostics-{citySlug}-{yyyyMMdd-HHmmss}.txt
```

Snapshot timestamps use local time. City slugs are sanitized for Windows filenames. If city name is unavailable, `UnnamedCity` is used.

Viewer default export detection checks `D:\CS2MetroDiagram`,
`Documents\CS2MetroDiagram`, and `Desktop\CS2MetroDiagram`. Other custom folders
can be opened manually with `Open JSON`.

## Validation Bundle Workflow

Generate a bundle for the latest export:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName primary-city
```

Generate from a snapshot:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-alpha-validation-bundle.ps1 `
  -InputJson 'D:\CS2MetroDiagram\exports\metro-export-城市名-yyyymmdd-hhmmss.json' `
  -CaseName city-review
```

Refresh the bundle index:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-alpha-validation-bundles.ps1
```

Outputs:

```text
artifacts\alpha-validation\index.md
artifacts\alpha-validation\index.csv
```

## Product Candidate Map Workflow

Generate a product-facing schematic-map candidate:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-product-candidate-map.ps1 `
  -InputJson D:\CS2MetroDiagram\metro-export.json `
  -CaseName product-candidate
```

Use this for visual experiments around `schematic-map`. The script now also runs
the schematic-map SVG audit and writes these files beside the candidate SVG/PNG:

```text
schematic-map-audit.txt
schematic-map-route-segments.csv
schematic-map-layout-conflicts.csv
schematic-map-style-widths.csv
schematic-map-parallel-corridors.csv
```

Run the audit directly on an existing SVG:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\analyze-schematic-map-svg.ps1 `
  -InputSvg artifacts\product-candidate\<case>\product-candidate.svg `
  -InputJson D:\CS2MetroDiagram\exports\metro-export-城市名-yyyymmdd-hhmmss.json `
  -OutputDir artifacts\product-candidate\<case>
```

Use the audit to review:

- route segment angle and octilinear drift;
- synthetic bend count and which routes needed render-only bend points;
- direction divergence against exported station positions when comparable;
- exact shared-platform/parallel corridor overlay consistency;
- route badge and station-label conflicts;
- route badge to route badge conflicts;
- route and corridor stroke-width token consistency.

Recent Sheffield schematic-map candidate after disabling default synthetic bends and tightening route-badge spacing:

```text
artifacts\product-candidate\20260619-143053-sheffield-badge-spacing
```

The audit for this candidate reported 0 synthetic bends, 16 remaining non-octilinear segment warnings, 0 direction-divergence warnings, 0 short-segment warnings, and 0 badge conflicts. Manual review preferred this visual family over the synthetic-bend experiment, so synthetic bends should stay opt-in until a better visual scoring rule exists.

## Schematic Regression Gate

Run this before accepting schematic-map renderer changes:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1
```

Fast smoke mode:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-schematic-regression-gate.ps1 -LatestExports 0 -SkipPng
```

The script discovers the latest real export, recent snapshot exports,
`samples\regression\*.json`, and selected legacy samples. It writes:

```text
artifacts\schematic-regression\<timestamp>\index.md
artifacts\schematic-regression\<timestamp>\regression-summary.csv
artifacts\schematic-regression\<timestamp>\cases\...
```

Recent real-export gate:

```text
artifacts\schematic-regression\20260619-214528
```

Recent sample smoke gate:

```text
artifacts\schematic-regression\20260619-215440
```

## Roadmap Operating Rule

Short-term work should improve alpha validation reliability before adding larger
features. Prefer this order:

1. keep build/test green,
2. generate validation bundles for new real exports,
3. fix obvious Viewer or schematic-map regressions,
4. update docs and bundle notes,
5. only then consider new renderer behavior.

Viewer package-size reduction is intentionally deferred until the alpha.2
candidate is stable. The current self-contained package is large, but changing
publish strategy too early would add packaging noise while layout behavior is
still moving.

## Layout Modes

`geographic`

- Alpha recommended baseline.
- Uses exported path geometry when enabled.
- Keep stable.

`schematic-v2`

- Experimental topology/diagnostic schematic.
- Used as a base for schematic-map work.

`schematic-map`

- Product-facing official-map candidate.
- More abstract and map-like than schematic-v2.
- Still experimental.
- Uses a more assertive octilinear snap tolerance than schematic-v2 so product
  candidates better follow horizontal, vertical, and 45-degree metro map grammar.
- Has an opt-in render-only synthetic bend experiment for long locked route
  segments. This does not move station markers, labels, stops, pathPoints, or
  exported data.
- Synthetic bends are disabled by default for product candidates after manual
  review found the initial experiment less visually natural despite fewer audit
  warnings.
- Direction polish should be evidence-led through `schematic-map-audit.txt`, not
  one-off screenshot memory.

`schematic-lite`

- Legacy mode.
- Fully removed from the toolchain (renderer, CLI, tests) in 2026-07.

## Current Guardrails

- Do not modify exporter or JSON schema for renderer/layout polish.
- Do not mutate `line.stops` or `line.pathPoints`.
- Keep new schematic work render-only.
- Prefer validation bundles over ad hoc screenshots.
- Keep docs short; archive long historical notes.

