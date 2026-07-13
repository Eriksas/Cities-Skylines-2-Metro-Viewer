# Phase 7 Release Candidate Manual Test

This checklist validates the exact private/local candidate before any version
bump, GitHub release, or Paradox Mods update. Geographic remains the reliable
default; schematic preview is useful but still recommends the desktop Viewer
for the most complete result.

## Candidate Identity

- Candidate folder:
  `artifacts\release-candidates\CS2MetroDiagram-phase7-rc1`
- Candidate zip:
  `artifacts\release-candidates\CS2MetroDiagram-phase7-rc1-win-x64.zip`
- Confirm `build-info.txt` records candidate name, embedded version, commit,
  dirty state, build time, and UI-module hash.
- Confirm `manifest.sha256` lists every candidate payload file except the
  manifest itself.

Automated candidate verification completed on 2026-07-13: solution build,
tests, Viewer publish, CS2 post-process, source/staged MJS identity, manifest,
ZIP contents, and a six-second Viewer launch smoke all passed. The remaining
checkboxes require owner-visible game/Viewer behavior and release judgment.

## Mod Lifecycle

- [ ] Start CS2 with the staged candidate and confirm the mod loads.
- [ ] Confirm Options/localization load normally in Chinese and English.
- [ ] Confirm the existing JSON export remains available even if preview UI
      initialization is intentionally unavailable or fails.
- [ ] Load no city, an empty/no-metro city, and both available real cities.
- [ ] Open, close, and reopen the preview repeatedly without duplicate toolbar
      buttons, stale state, or errors.

## Preview

- [ ] First open defaults to Geographic and displays the current city network.
- [ ] Geographic station names, line colors, path geometry, title, and legend
      are readable, including Chinese glyphs.
- [ ] Switch to Schematic and back; both layouts render and the selection
      persists. The schematic Viewer recommendation stays subtle.
- [ ] Toggle default station names and crowded labels; the latest option wins
      after rapid changes or an immediate Refresh.
- [ ] Wheel zoom, buttons, Fit, and mouse drag work without backend rerender.
- [ ] Edge stations and labels remain inside the canvas at supported display
      resolutions.

## Refresh, Cache, And Telemetry

- [ ] Refresh after changing the network updates revision, line/station counts,
      and SVG without a stale second render.
- [ ] Closing during pending refresh/render/save does not alter the next panel
      session; an explicit JSON export still completes.
- [ ] Returning to a previously rendered option combination reports a cache hit
      and the cache never exceeds four entries.
- [ ] `CS2_Metro.Mod.log` contains categorized Lifecycle, Capture, Render,
      Export, Save, and Settings entries.
- [ ] Record `captureMs`, `requestMs`, and `rendererMs` for the largest city.

## Existing Export Workflow

- [ ] Export Test Metro JSON still succeeds.
- [ ] Export Transport Debug Dump still succeeds.
- [ ] Export Real Metro JSON writes latest and timestamped JSON/diagnostics.
- [ ] A second export does not overwrite the earlier snapshot.
- [ ] Save current SVG writes latest and timestamped SVG files.

## Viewer

- [ ] Launch `Viewer\MetroDiagram.Viewer.exe` from the candidate package.
- [ ] Open latest export and a timestamped snapshot.
- [ ] Preview geographic and product schematic layouts.
- [ ] Save SVG, PNG, and PDF.
- [ ] Confirm Chinese/English switching and saved settings still work.

## Release Decision

- Exact candidate accepted: **yes**, owner-confirmed on 2026-07-13
- Blocking issues: none reported
- Non-blocking issues: none reported during final acceptance
- Largest-city capture/render timings: not recorded in the final owner message
- Approved for Beta.4 version bump and publication: **yes**

The release gate is open. Rebuild from the final Beta.4 commit before publishing
so public binaries do not retain the private candidate's Beta.3 identity.
