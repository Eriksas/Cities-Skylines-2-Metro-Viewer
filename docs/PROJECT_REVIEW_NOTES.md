# Project Review Notes

## 2026-06-11 Lightweight Review

The current offline solution and test suite pass. The review focused on low-risk engineering improvements because the renderer has several accepted visual fixes that should not be disturbed before more alpha validation.

## Findings

- `src\MetroDiagram.Rendering\MetroSvgRenderer.cs` is the largest maintenance risk. It contains most renderer, layout, schematic-v2, diagnostics, and style behavior. Future feature work should extract focused helpers instead of adding more large private methods.
- `src\MetroDiagram.Tests\Program.cs` is also large. The console-runner approach is still useful because it avoids package dependencies, but future test additions should consider splitting fixtures/assertion helpers into separate files.
- The script set is useful but was missing a single local validation entrypoint. `scripts\validate-local.ps1` now wraps the normal build/test flow and can optionally include the CS2 mod project build.
- Paradox Mods publishing had manual instructions but no command-line guard against accidentally using `PublishNewMod`. `scripts\publish-mod.ps1` now only supports existing-listing updates and requires a configured `ModId`.
- `CS2 Metro\Library\ilpp.pid` is currently modified in git status and looks like a generated CS2/modding artifact. It was not changed by this review. If it is intentionally not source material, handle it in a separate cleanup commit after confirming whether it is tracked by design.

## Recommended Next Refactors

1. Extract schematic-v2 route-chain, shared-platform overlay, and terminal-tail logic from `MetroSvgRenderer.cs` into dedicated rendering helpers.
2. Extract test fixtures and SVG assertion helpers out of `Program.cs`.
3. Keep new rendering behavior behind explicit layout/style options until alpha validation confirms it across more city exports.
4. Keep `geographic + UsePathPoints + service family merge` as the alpha-safe recommendation while `schematic-v2` matures.

## Validation

Use:

```powershell
scripts\validate-local.ps1
```

For the CS2 mod project as well:

```powershell
scripts\validate-local.ps1 -IncludeModBuild
```
