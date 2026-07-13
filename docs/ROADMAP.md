# Roadmap

This roadmap is intentionally practical. The near-term goal is to make the
current alpha useful for real testers before adding larger product features.

## Short Term: Alpha Public Stabilization

Goal: make the current build safe, repeatable, and easy to test on more cities.

- Keep `schematic-anneal` as the recommended product layout and `geographic` as
  the faithful-geometry fallback.
- Keep `schematic-map` and `schematic-v2` as comparison/diagnostic modes rather
  than separate product directions.
- Generate one alpha validation bundle for every real city export.
- Use bundle notes to record whether the city is a regression case.
- Keep Viewer startup, preview framing, Open Default Export, Open JSON, and Save
  SVG reliable.
- Fix only clear corpus-backed schematic regressions; avoid broad layout rewrites
  until the same problem appears in multiple cities.
- Keep docs short and current; archive historical phase notes.
- Keep GitHub/Viewer and Paradox Mods aligned from `v0.1.0-alpha.7` onward when
  code-mod behavior changes.

## Medium Term: Official-map Schematic Direction

Goal: move from "debug schematic" toward a readable metro-map product.

- Continue `schematic-anneal` as the main visual design lane.
- Preserve topology first: stop order, interchanges, branches, shared visible
  lanes, and branch/service semantics must remain believable.
- Make route geometry more octilinear where safe: horizontal, vertical, and
  45-degree directions should be preferred without distorting topology.
- Improve station label placement and route badge placement with collision
  avoidance.
- Build a multi-city regression set: simple network, dense network, branch
  network, airport/express network, and parallel-platform cases.
- Add renderer diagnostics that explain why a route was straightened, offset,
  merged, or left alone.
- Decide which schematic-map behavior is mature enough to expose as a
  recommended Viewer preset.

## Long Term Active Track: In-Game Metro Preview

The next major product track is documented in `INGAME_PREVIEW_PLAN.md`.

- Add a supported universal mod-menu entry and a polished map workspace.
- Reuse one in-memory CS2 metro snapshot for export and preview.
- Port/extract the existing C# render engine instead of duplicating layout in
  the UI frontend.
- Support schematic/geographic preview, refresh, pan/zoom/fit, label controls,
  JSON export, and SVG save.
- Keep all Phase 7 development off the public PDX listing until the owner has
  tested and explicitly approved the release candidate.

## Later: Product Packaging And Power-user Tools

Goal: make the tool easier to distribute and more flexible without risking the
export/schema contract.

- Reduce Viewer package size after alpha behavior stabilizes. Candidates:
  framework-dependent package, trimmed publish, split portable package, or a
  smaller preview technology if WPF self-contained size remains too high.
- Keep the complete manual override workflow in the desktop Viewer until the
  in-game preview is stable.
- Investigate broader transit support only after metro/subway remains stable.
- Consider richer installer/release flow after the mod and Viewer packaging are
  no longer changing frequently.

## Not Now

- Do not optimize Viewer package size before alpha.2 validation is usable.
- Do not replace `geographic` as the alpha default.
- Do not change `metro-export.json` schema for renderer polish.
- Do not modify exporter ECS logic for schematic layout problems.
- Do not revive old `schematic-lite` patch work.
