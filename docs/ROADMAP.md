# Roadmap

This roadmap is intentionally practical. The near-term goal is to make the
current alpha useful for real testers before adding larger product features.

## Short Term: Alpha Public Stabilization

Goal: make the current build safe, repeatable, and easy to test on more cities.

- Keep `geographic + UsePathPoints + service family merge` as the recommended
  alpha baseline.
- Keep `schematic-map` experimental, but continue using it for product-facing
  visual review.
- Generate one alpha validation bundle for every real city export.
- Use bundle notes to record whether the city is a regression case.
- Keep Viewer startup, preview framing, Open Default Export, Open JSON, and Save
  SVG reliable.
- Fix only clear regressions in `schematic-map`; avoid broad layout rewrites
  until the same problem appears in multiple cities.
- Keep docs short and current; archive historical phase notes.
- Keep `v0.1.0-alpha.3` public on Paradox Mods and use real tester feedback to
  decide the next alpha package.

## Medium Term: Official-map Schematic Direction

Goal: move from "debug schematic" toward a readable metro-map product.

- Continue `schematic-map` as the main visual design lane.
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

## Long Term: Product Packaging And Power-user Tools

Goal: make the tool easier to distribute and more flexible without risking the
export/schema contract.

- Reduce Viewer package size after alpha behavior stabilizes. Candidates:
  framework-dependent package, trimmed publish, split portable package, or a
  smaller preview technology if WPF self-contained size remains too high.
- Add PNG export only after SVG output is stable.
- Add style presets after the core schematic behavior is stable across multiple
  cities.
- Add optional manual overrides for station/label positions if automatic layout
  cannot cover advanced cases.
- Investigate broader transit support only after metro/subway remains stable.
- Consider richer installer/release flow after the mod and Viewer packaging are
  no longer changing frequently.

## Not Now

- Do not optimize Viewer package size before alpha.2 validation is usable.
- Do not replace `geographic` as the alpha default.
- Do not change `metro-export.json` schema for renderer polish.
- Do not modify exporter ECS logic for schematic layout problems.
- Do not revive old `schematic-lite` patch work.
