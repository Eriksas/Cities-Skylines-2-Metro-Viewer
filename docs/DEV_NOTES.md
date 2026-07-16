# Development Notes

Living operational notes only. History:

- Journal 2026-06-19 .. 2026-07-07: `docs/archive/2026-07-10-dev-notes-journal/DEV_NOTES-journal-2026-06-19-to-2026-07-07.md`
- Everything before the 2026-06-18 cleanup: `docs/archive/2026-06-18-doc-consolidation/DEV_NOTES.full.md`

## In-game Schematic Product Parity - 2026-07-16

- `PortableMetroSvgRenderer` now runs the desktop schematic-anneal math:
  product cost weights (octilinear/short/long/bend/crossing/clearance/anchor),
  same-line clearance exemption via station/edge line masks, minimum-spacing
  hard gate, adaptive temperature with best-state tracking, range-2 polish
  (12 sweeps), fixed xorshift seed (layouts stable across refreshes and
  runtimes), post-anneal fit-to-frame scaling, canvas height adaptation, and
  parallel shared corridors (same-color lane collapsing, symmetric lane
  offsets, miter-joined corners).
- The solver works on int-indexed arrays; string-dictionary lookups dominated
  cost evaluation before (Release timings for 谢菲尔德 59 stations:
  1151 ms -> 321 ms; 肇庆 51 stations: 813 ms -> 309 ms; geographic ~30 ms).
- Offline verification: `scripts\generate-in-game-preview-audit.ps1` on both
  real-city exports; free-angle diagonals eliminated (octilinear core), shared
  trunks render as parallel lanes (e.g. 5号线/7号线 corridor, 1号线/10号线
  肇庆 corridor), Codex's route-chain normalization and collision-aware labels
  retained on top. 153 offline tests pass, including the portable mirror-chain
  and label-candidate tests.
- IMPORTANT: audits must measure Release assemblies; Debug is ~3x slower and
  does not represent the in-game build.
- Remaining desktop-only refinements (candidates for a later pass): label
  route-penalty parity and softer crowded-hide threshold, middle/end text
  anchors, route badges, express stripes, marker hierarchy.
- Owner in-game validation still required before publication.

## Beta.2/Beta.3 Mod Loading Incident - 2026-07-10

- CS2 `1.6.0f1` logs showed `OnLoad` immediately followed by `OnDispose`, raw
  options locale keys, and a secondary null reference at `Mod.cs:88`.
- Beta.2 incorrectly moved localization-source registration before
  `RegisterInOptionsUI()`. `LocalizationManager.AddSource()` eagerly enumerates
  `ModLocaleSource.ReadEntries()`, whose generated locale IDs require the
  Options registration to exist. All locale registrations therefore threw and
  `OnLoad` aborted before the exporter became available.
- Beta.3 restores the official template order: load settings, register Options
  UI, then register localization sources. It retains Beta.2's supported-locale
  filtering, initialization rollback, and null-safe cleanup.
- Localization is a presentation feature, not an exporter prerequisite.
  Beta.3 catches localization failure outside the core initialization path so
  the exporter remains usable. Current game logs still show locale-source
  registration failures, so the fallback is raw locale keys, not translated or
  guaranteed English labels; this remains follow-up work.
- The successful Beta.3 log contains `Real metro JSON export directory` after
  the localization warnings and reaches a normal `OnDispose`, confirming that
  localization failure no longer aborts the mod.
- Paradox Mods `0.1.0-beta.3` was published successfully to public ModId
  `146643`; `ModPublisher.exe` ended with `New mod version published`.
- Offline solution build, renderer tests, CS2 Release build, and mod
  post-process pass. The user also confirmed in game that Beta.3 restored mod
  loading. Offline green tests alone must never be treated as proof that a CS2
  lifecycle change works in game.

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
- The published Beta.1 PNG uses a thumbnail-friendly abstract network instead
  of a dense real-city screenshot. Its right-side illustration was compacted to
  about 86% while retaining the required `950 x 500` canvas. The matching Figma
  source still needs the final compact-width adjustment once the Starter MCP
  call quota resets.
- Paradox Mods `0.1.0-beta.1` was published successfully to public ModId
  `146643` after the generated Viewer and mod passed smoke testing.
- Routine mod delivery now uses the subscribed Paradox Mods listing. Do not
  copy builds into the game directory during normal release work; publish the
  new PDX version and let the subscription update it. The former local deploy
  script has been removed from the active workflow.
- `ModPublisher.exe` may emit a cross-volume `IOERR_101` warning while preparing
  its local content. For Beta.1 it continued through upload and ended with
  `New mod version published`; treat the final publisher status as authoritative.

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

## Phase 7 In-Game Preview Track - 2026-07-13

- Product and architecture plan: `docs\INGAME_PREVIEW_PLAN.md`.
- The installed game/mod set confirms that current code mods can ship UI
  JavaScript/CSS bundles, and the current game line includes a universal mod
  button/menu. Exact extension APIs must still be validated against the local
  toolchain during Phase 7A rather than copied from an older mod.
- Current compatibility constraint: the CS2 project uses the game/.NET
  Framework toolchain, while Core/Rendering target .NET 8. Do not duplicate the
  schematic algorithm in the frontend. Audit multi-targeting or extract a
  dependency-light portable render engine in Phase 7C.
- ECS capture stays on the game thread and produces an immutable snapshot.
  Rendering/UI must never keep live ECS handles.
- Do not publish any Phase 7 build to PDX before the owner's in-game acceptance.

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

## Phase 7A UI And Binding Spike (2026-07-13)

Implementation branch:

```text
feature/ingame-preview
```

The installed CS2 assemblies and a current installed code mod were inspected
with `ilspycmd` installed under `E:\Tools\ilspycmd`. The supported pattern used
by the corrected spike is:

- backend `UISystemBase` with `ValueBinding<T>` and `TriggerBinding`;
- frontend `bindValue`, `useValue`, and `trigger` from `cs2/api`;
- entry appended to `GameTopRight`;
- binding-driven panel root appended to `Game`.

The first build registered `gamePanelComponents` and called only the game's
`togglePanel` trigger. Its top-right icon appeared, but the panel did not mount
and the C# log never recorded an open transition. The corrected build toggles
the C# `panelOpen` binding directly and renders `PreviewRoot` from that binding.

Phase 7A files:

```text
CS2 Metro\InGamePreviewUISystem.cs
CS2 Metro\CS2 Metro.mjs
CS2 Metro\Mod.cs
CS2 Metro\CS2 Metro.csproj
```

The backend currently exposes only panel-open state, health JSON, and refresh
commands. The frontend renders a responsive static sample map. There are no ECS
queries and no portable renderer integration in this phase.

Verification completed:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore
node --check "CS2 Metro\CS2 Metro.mjs"
```

All commands passed, including CS2 IL post-process and Burst outputs. The CS2
toolchain stages the development build at:

```text
E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods\CS2 Metro
```

The user-level `CSII_LOCALMODSPATH` was moved to that E-drive directory on
2026-07-13. Future CS2 builds should not use the C-drive local Mods directory.
The first build left one loaded native DLL in the old C-drive folder while the
game was running; remove that final file after Cities: Skylines II exits.

The owner completed the in-game checklist on 2026-07-13. Phase 7A is closed;
the accepted shell is the base for Phase 7D.

## Phase 7B/7C Snapshot And Portable Runtime (2026-07-13)

The shared runtime is implemented as a new `src\MetroDiagram.Engine` project
targeting `netstandard2.0`. It deliberately does not reference the net8 desktop
renderer or JSON/file APIs that are unsuitable for the CS2 net48 runtime.

Key boundaries:

- `MetroNetworkSnapshotService` performs capture through the existing exporter
  path on the game thread and retains the latest immutable snapshot.
- `RealMetroJsonExporter` writes JSON from that snapshot through
  `MetroSnapshotJsonWriter`; the schema and ECS extraction logic are unchanged.
- `MetroSnapshotRevision` excludes `exportedAt`, so identical network content
  can reuse a cached render after refresh.
- `PortableMetroSvgRenderer` owns the small runtime geographic and
  schematic-anneal profiles. It is not a second JavaScript layout engine.
- `InGamePreviewRenderService` keeps at most four revision/options results. A
  `List<string>` is used for FIFO order because the CS2 net48 references expose
  an ambiguous `Queue<T>` through both `System` and `mscorlib`.

Validation completed sequentially:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore -p:LocalModsPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods"
node --check "CS2 Metro\CS2 Metro.mjs"
```

The Release build passed IL post-process and Burst generation for Windows,
macOS, and Linux with zero warnings/errors. The E-drive staged package includes
`CS2 Metro.dll`, `CS2 Metro.mjs`, and `MetroDiagram.Engine.dll`.

Phase 7C was delivered as a runtime foundation; Phase 7D subsequently replaced
the static sample panel with the real capture/render state pipeline.

## Phase 7D/7E Real In-Game Preview And Export Controls (2026-07-13)

`InGamePreviewUISystem` is now a small game-thread controller rather than a
static health spike. Trigger bindings only set pending flags; `OnUpdate`
performs snapshot capture, render, JSON export, or SVG save. This preserves the
rule that browser callbacks never read ECS.

Bindings:

```text
CS2MetroPreview.panelOpen
CS2MetroPreview.stateJson
CS2MetroPreview.svg
CS2MetroPreview.setPanelOpen(bool)
CS2MetroPreview.refresh
CS2MetroPreview.setLayout(string)
CS2MetroPreview.setShowGenericStationNames(bool)
CS2MetroPreview.setHideCrowdedLabels(bool)
CS2MetroPreview.exportJson
CS2MetroPreview.saveSvg
```

The frontend displays renderer-owned SVG as responsive inline SVG. Pan, zoom,
and fit are client-side transforms and therefore do not rerun layout.
Changing layout or labels rerenders from the cached immutable snapshot. The
bounded C# render cache is keyed by revision, layout, and label options.

The first real D/E game pass showed a white map surface even though the log
confirmed successful captures/renders. Moving from a data URI to inline SVG was
a useful transport hardening step but did not fix the blank surface. The next
pass recorded complete payloads (schematic `24,588` chars, geographic
`307,344` chars), while `Player.log` repeatedly reported:

```text
[UI] [ERROR] Combining percents in calc() expressions with other types is not supported!
```

The actual failure was `width/height: calc(100% - 28rem)` on the map layer.
CS2 Coherent UI could not compute its size. Use `top/right/bottom/left: 14rem`
for absolute fill instead; do not introduce percentage-plus-rem `calc()` in
the game UI. A source regression test now rejects `calc(100%` in the MJS.
Inline SVG and `stateJson.svgLength` remain because they provide a simpler,
diagnosable payload path.

SVG save paths:

```text
<configured export folder>\metro-diagram.svg
<configured export folder>\exports\metro-diagram-{citySlug}-{yyyyMMdd-HHmmss}.svg
```

Same-second saves receive `-2`, `-3`, and later suffixes. Writes are UTF-8
without BOM and publish the stable latest file atomically after the snapshot.

Validation completed sequentially:

```text
node --check "CS2 Metro\CS2 Metro.mjs"
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore -p:LocalModsPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods"
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

All passed with zero build warnings/errors. The mod post-process and Burst
steps completed, and the E-drive package includes the mod DLL, MJS, and
`MetroDiagram.Engine.dll`. Runtime/manual validation is still required; do not
publish this Phase 7 build to PDX yet.

## Phase 7F Geographic Default And CJK Typography (2026-07-13)

The owner confirmed that the Coherent sizing hotfix makes the real SVG visible.
The visible portable schematic is not yet as readable as geographic in the game
panel, so geographic is now the in-game-only default. This does not change the
desktop Viewer or CLI product defaults. `InGamePreviewGeographicDefaultApplied`
provides a one-time migration for existing development settings; after that,
the user's selected in-game layout persists normally.

Chinese tofu boxes were not corrupt JSON or missing station names. The portable
renderer explicitly emitted `font-family="Arial, sans-serif"`, overriding CS2's
locale-aware font stack. CS2 ships Noto Sans SC and exposes it through
`--fontFamily`. Inline preview preparation now removes descendant SVG
`font-family` attributes and sets the root to `var(--fontFamily)`. Standalone
portable SVG declares Overpass plus Noto Sans SC/TC/JP/KR fallbacks so saved
files remain readable outside the game.

Phase 7F also separates the close action from a wrapping layout/command toolbar
and publishes the mod interface-language setting to the panel. Code-side
validation passed sequentially:

```text
node --check "CS2 Metro\CS2 Metro.mjs"
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore -p:LocalModsPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods"
```

The source, E-drive development package, and CS2 staged MJS hashes matched after
post-process. Owner validation is still required for Chinese glyphs, language
override behavior, toolbar wrapping, and supported display resolutions. Do not
publish this branch to PDX yet.

The first visible Phase 7F pass also showed that raw HTML checkbox inputs are
not suitable in Coherent: they appeared as empty text-entry boxes. Use
`ui.Button` with `aria-pressed` for binary preview controls. The filter controls
now use a compact track/knob indicator and the full control surface toggles the
setting. Command buttons use the built-in `flat` theme and selected layout
buttons use `primary`; this retains CS2 focus and sound behavior while avoiding
the white default-button treatment. A source regression test rejects native
checkboxes and requires the game button variants.

## Phase 7F Pan And Canvas Safety Follow-up

CS2 Coherent did not reliably dispatch the Pointer Events/pointer-capture path
used by the first map viewport implementation. The viewport now begins a drag
with `onMouseDown` and tracks `mousemove`/`mouseup` on `window`, so panning
continues when the cursor leaves the map and always terminates on release.

The portable renderer previously projected station centers exactly to the map
bounds. That ignored interchange radius, route half-width, and label extent.
`CreateMapFrame` now supplies one shared safe frame to geographic projection,
schematic snapping/annealing, station markers, and label placement. Edge labels
prefer the right side but flip left when needed and clamp vertically.

Validation passed sequentially:

```text
node --check "CS2 Metro\CS2 Metro.mjs"
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore -p:LocalModsPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods"
```

The CS2 build completed IL post-processing and Burst generation for all target
platforms with zero warnings/errors. The owner subsequently accepted drag and
edge-station behavior after in-game review.

## Phase 7G First Hardening Tranche

- Schematic mode now appends a low-contrast Viewer recommendation to the
  existing pan/zoom hint. Geographic mode does not show the recommendation.
- Refresh captures and renders the latest options itself, so it clears any
  earlier queued option render. A successful JSON export likewise clears a
  redundant queued refresh/render before rendering its authoritative snapshot.
- Clearing an unavailable/error preview also clears pending render/save work.
  This is request coalescing for the current synchronous controller, not an
  asynchronous renderer rewrite.
- Validation passed sequentially: MJS syntax, solution build, complete offline
  tests, CS2 Release build/post-process, and E-drive staging. The source and
  staged MJS SHA256 both equal
  `E85D2BB8777E92564AF084749AE66A8A16E531A2C3A22F867B2F8D3A6C1015D8`.

## Phase 7G Runtime Telemetry And Lifecycle Hardening

- `MetroNetworkSnapshotService` measures capture time at the single game-thread
  ECS boundary and records UTC capture time with the immutable snapshot.
- `InGamePreviewRenderService` returns request timing/cache metadata and keeps
  at most four entries using LRU access order.
- The controller publishes capture/request/renderer timings separately. On a
  cache hit, `renderMs` is the user-facing request duration while `rendererMs`
  remains the original renderer measurement for that cached SVG.
- Panel close cancels queued refresh/render/save work. JSON export is not
  cancelled because it is an explicit data action rather than disposable
  preview work.
- The controller remains synchronous and processes one operation per update;
  counters measure coalescing and lifecycle behavior without adding thread
  access to ECS.
- Owner game testing passed on 2026-07-13 with no blocking issue. Treat the
  current Phase 7G behavior as frozen while preparing Phase 7H; do not publish
  to PDX until the owner approves the exact release candidate.

## Phase 7H Candidate Packaging

`scripts\package-phase7-release-candidate.ps1` is intentionally separate from
the public Viewer release script. It builds/tests, publishes the Viewer, runs
the CS2 Release post-process to the E-drive `cs2-local-mods` path, verifies the
staged MJS hash, and packages Mod/Viewer/docs plus a SHA-256 manifest under
`artifacts\release-candidates`. Candidate identity is separate from the
embedded Beta.3 baseline version until owner approval, preventing an accidental
PDX version/configuration update during private validation.

The first candidate run completed successfully on 2026-07-13:

```text
Candidate: phase7-rc1
Folder: artifacts\release-candidates\CS2MetroDiagram-phase7-rc1
ZIP: artifacts\release-candidates\CS2MetroDiagram-phase7-rc1-win-x64.zip
Files: 16
Unpacked: 76.23 MiB
ZIP: 70.86 MiB
Source/staged MJS SHA256: E85D2BB8777E92564AF084749AE66A8A16E531A2C3A22F867B2F8D3A6C1015D8
```

Manifest and ZIP required-entry verification passed. The packaged
`MetroDiagram.Viewer.exe` remained running after a six-second hidden launch
smoke and was then stopped deliberately. `manifest.sha256` covers all package
payload files except itself, avoiding a recursive self-hash.

## Beta.4 Publication

The owner accepted the exact Phase 7 candidate on 2026-07-13. All release-facing
version sources moved to `v0.1.0-beta.4`; `FileVersion` is `0.1.0.11`.

Final verification rebuilt from commit `99a4259`:

```text
Offline build/tests: passed, zero warnings/errors
Viewer self-contained publish and launch smoke: passed
CS2 Release post-process and Burst targets: passed, zero warnings/errors
Viewer/Mod ProductVersion: v0.1.0-beta.4+99a4259...
Viewer ZIP SHA256: BA526BCD203733DEE7E8E0F611DF6251BF0EEA8FFE4B587F5A2F9EC010A51EC1
```

`scripts\publish-mod.ps1 -Mode NewVersion -SkipRestore` updated existing public
ModId `146643`; `ModPublisher.exe` ended with `New mod version published`. The
known cross-volume `IOERR_101` warning appeared before upload preparation but
did not prevent publication. GitHub Release `v0.1.0-beta.4` is public with the
self-contained Viewer ZIP and checksum asset; its tag points to `99a4259`.

## In-game Schematic Readability Audit - 2026-07-15

The desktop Viewer and the CS2 panel do not share the same renderer. The game
uses `MetroDiagram.Engine.PortableMetroSvgRenderer` with a bounded 1800x1100
profile so it can run safely under the mod's `netstandard2.0` runtime. Desktop
`schematic-anneal` screenshots therefore cannot prove game-panel quality.

The real Sheffield export exposed two portable-renderer problems:

- mirrored out-and-back stop sequences were drawn as route geometry, producing
  false terminals and duplicate return legs;
- fixed right/left labels ignored route and station obstacles, creating a dense
  central text pile.

The fix is schematic-only and render-only. It collapses mirrored route chains
without mutating the snapshot, performs a deterministic final grid polish, and
places labels using eight candidate positions with label/station/route overlap
scoring. Important interchange and terminal labels are preserved; low-priority
crowded labels may hide according to the existing option.

Generate evidence with the actual in-game renderer profile:

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-in-game-preview-audit.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -OutputDir artifacts\ingame-schematic-audit\verification -NoBuild
```

Current real-city evidence:

```text
Before: 161 route segments, 54 warnings, 10 non-octilinear, 49 direction divergence
After:  104 route segments, 21 warnings, 8 non-octilinear, 16 direction divergence
Portable schematic render time: approximately 352 ms
Geographic before/after PNG SHA-256: identical
```

Sequential verification passed:

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
node --check "CS2 Metro\CS2 Metro.mjs"
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore -p:LocalModsPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods"
```

The source and staged `MetroDiagram.Engine.dll` hashes match. Owner in-game
validation is still required before any GitHub/PDX follow-up release.

## 2026-07-16: Single-line in-game framing and vector zoom

Root cause of the one-line preview escaping its white sheet:

- `PortableMetroSvgRenderer.CloneWithAdaptedHeight` derived schematic canvas
  height from the geographic station bounding box;
- a nearly vertical single family has almost no source width, so this produced
  an extreme page ratio even though a single route has no useful 2D network
  aspect.

Fix: all in-game profiles explicitly disable per-network canvas-height
adaptation and use the stable `1800x1100` panel sheet. The existing schematic
fit/geographic projector centers and scales the network inside the map frame.
The SVG root also declares `overflow="hidden"` as a final Coherent safety
boundary. The portable option remains available outside the game profile.

Root cause of blurry text after zoom: `CS2 Metro.mjs` scaled the complete SVG
host with CSS `transform`. Coherent could cache that host as a texture, so 219%
zoom enlarged rasterized glyphs. Zoom/pan now updates the inline SVG `viewBox`;
mouse deltas are converted from screen pixels to SVG user units. The SVG remains
vector-rendered at every zoom level.

Regression fixture and visual evidence:

```text
samples\sample-metro-single-line-vertical.json
artifacts\ingame-schematic-audit\single-line-framing-fix
artifacts\ingame-schematic-audit\universal-framing-real-city
artifacts\ingame-schematic-audit\universal-framing-large-sample
```

The test matrix covers horizontal/vertical/diagonal one-line networks, two
nearly coincident vertical families, a crossing interchange, very large source
coordinates, the real 59-station city, and the 24-station large sample. It
checks both in-game layouts and asserts fixed root dimensions plus contained
route, station, and label coordinates.

Verification passed sequentially:

```text
node --check "CS2 Metro\CS2 Metro.mjs"
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build "CS2 Metro\CS2 Metro.csproj" -c Release --no-restore -p:LocalModsPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods"
```

Owner game validation remains required for fit-to-window, zoom sharpness, and
drag behavior under the real CS2 Coherent runtime.

