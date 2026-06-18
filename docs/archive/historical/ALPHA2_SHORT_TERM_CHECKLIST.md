# Alpha.2 Short-Term Checklist

This checklist captures the current short-term alpha.2 validation flow. The goal is to keep the project in a testable state before adding more renderer features.

## Current Recommended Default

- Layout: `geographic`
- Use exported path geometry: enabled
- Service family merge / normalized family rendering: enabled
- Shared corridor: disabled by default
- Express stripe: disabled by default for geographic baseline
- Viewer schematic layout: `schematic-v2` remains experimental
- Viewer `schematic-lite`: retired from the UI; CLI-only for historical comparison

## Latest Validation Inputs

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
D:\CS2MetroDiagram\exports\metro-export-肇庆-20260607-112942.json
```

## Latest Generated Bundles

Alpha validation bundle:

```text
artifacts\alpha-validation\20260614-002122-zhaoqing-alpha2-short-term
artifacts\alpha-validation\alpha-validation-20260614-002122-zhaoqing-alpha2-short-term.zip
```

Product candidate map:

```text
artifacts\product-candidate\20260614-002504-zhaoqing-alpha2-short-term
artifacts\product-candidate\20260614-002504-zhaoqing-alpha2-short-term\product-candidate.svg
artifacts\product-candidate\20260614-002504-zhaoqing-alpha2-short-term\product-candidate.full.png
```

Release package:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.2-candidate-win-x64.zip
```

Viewer package:

```text
artifacts\viewer-win-x64-self-contained\MetroDiagram.Viewer.exe
```

## Manual Smoke Checklist

- [ ] Launch `MetroDiagram.Viewer.exe` from the refreshed self-contained package.
- [ ] Open default export from `D:\CS2MetroDiagram\metro-export.json`.
- [ ] Confirm the map preview renders inside the Viewer.
- [ ] Confirm `Show default / non-important station labels` can be enabled and disabled.
- [ ] Confirm `geographic` remains the recommended stable baseline.
- [ ] Switch to `schematic-v2` and confirm it renders without crashing.
- [ ] Save SVG from the Viewer and open it in a browser.
- [ ] Open the snapshot JSON manually from `D:\CS2MetroDiagram\exports`.
- [ ] Review the product candidate PNG for obvious route hiding, bad scroll capture, or missing legend/key content.
- [ ] Attach the alpha validation bundle zip when reporting a new city issue.

## Stop Conditions Before New Feature Work

Do not start new schematic styling or renderer feature work if any of these fail:

- Viewer cannot open latest export.
- Viewer cannot open a snapshot export.
- Viewer preview is blank.
- Release zip is missing Viewer, docs, samples, or build info.
- `scripts\validate-local.ps1` fails.
- Product candidate screenshot is cropped, blank, or captured with scrollbars.

## Notes

- Current schematic-v2 warnings are acceptable for alpha diagnostics as long as the SVG/PNG render and topology is not visibly broken.
- Future renderer decisions should be based on alpha validation bundles from multiple cities rather than single-city visual tuning.
