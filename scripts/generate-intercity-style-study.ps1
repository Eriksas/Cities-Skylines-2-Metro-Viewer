param(
    [string]$OutputDir = "artifacts\intercity-geographic-v2\style-study",
    [switch]$SkipPng
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
$svgPath = Join-Path $resolvedOutputDir "intercity-rail-style-study.svg"
$pngPath = Join-Path $resolvedOutputDir "intercity-rail-style-study.full.png"
$notesPath = Join-Path $resolvedOutputDir "notes.md"

$svg = @'
<?xml version="1.0" encoding="utf-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="1800" height="1050" viewBox="0 0 1800 1050">
  <title>Intercity Geographic v2 railway style study</title>
  <rect width="1800" height="1050" fill="#f5f7f8"/>
  <rect x="54" y="42" width="1692" height="966" rx="8" fill="#ffffff" stroke="#d7dde1"/>
  <text x="100" y="105" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="34" font-weight="700" fill="#1d2730">Intercity Geographic v2 - railway style study</text>
  <text x="100" y="145" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="18" fill="#5a6873">Geometry is identical in all candidates. Only the cartographic rail grammar changes.</text>

  <g transform="translate(0,40)">
    <text x="110" y="205" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="25" font-weight="700" fill="#202a31">A. Black/white sleeper rail - recommended</text>
    <text x="110" y="237" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="17" fill="#62717c">Official-map reference: continuous dark railway with equally spaced white sleepers.</text>
    <path d="M150 330 C340 250 470 410 650 315 S980 245 1170 335 S1440 420 1650 300" fill="none" stroke="#555b60" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M150 330 C340 250 470 410 650 315 S980 245 1170 335 S1440 420 1650 300" fill="none" stroke="#ffffff" stroke-width="4.5" stroke-dasharray="10 13" stroke-linecap="butt"/>
    <g fill="#ffffff" stroke="#41484d" stroke-width="3">
      <circle cx="150" cy="330" r="10"/><circle cx="650" cy="315" r="10"/><circle cx="1170" cy="335" r="10"/><circle cx="1650" cy="300" r="10"/>
    </g>
  </g>

  <g transform="translate(0,330)">
    <text x="110" y="205" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="25" font-weight="700" fill="#202a31">B. Double-rail outline - retained alternative</text>
    <text x="110" y="237" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="17" fill="#62717c">Quieter at small scale, but visually less recognizable as intercity railway.</text>
    <path d="M150 330 C340 250 470 410 650 315 S980 245 1170 335 S1440 420 1650 300" fill="none" stroke="#535a60" stroke-width="13" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M150 330 C340 250 470 410 650 315 S980 245 1170 335 S1440 420 1650 300" fill="none" stroke="#ffffff" stroke-width="5.5" stroke-linecap="round" stroke-linejoin="round"/>
    <g fill="#ffffff" stroke="#41484d" stroke-width="3">
      <circle cx="150" cy="330" r="10"/><circle cx="650" cy="315" r="10"/><circle cx="1170" cy="335" r="10"/><circle cx="1650" cy="300" r="10"/>
    </g>
  </g>

  <g transform="translate(0,620)">
    <text x="110" y="205" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="25" font-weight="700" fill="#202a31">C. Light sleeper rail - low-emphasis alternative</text>
    <text x="110" y="237" font-family="Segoe UI, Microsoft YaHei, sans-serif" font-size="17" fill="#62717c">Useful when metro must dominate, but contrast may be weak over a detailed background.</text>
    <path d="M150 330 C340 250 470 410 650 315 S980 245 1170 335 S1440 420 1650 300" fill="none" stroke="#7a8085" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M150 330 C340 250 470 410 650 315 S980 245 1170 335 S1440 420 1650 300" fill="none" stroke="#f7f8f9" stroke-width="3.5" stroke-dasharray="8 12" stroke-linecap="butt"/>
    <g fill="#ffffff" stroke="#5d6469" stroke-width="3">
      <circle cx="150" cy="330" r="9"/><circle cx="650" cy="315" r="9"/><circle cx="1170" cy="335" r="9"/><circle cx="1650" cy="300" r="9"/>
    </g>
  </g>
</svg>
'@

[System.IO.File]::WriteAllText($svgPath, $svg, [System.Text.UTF8Encoding]::new($false))

$notes = @"
# Intercity Railway Style Study

## Recommendation

Candidate A is the current preferred grammar:

- continuous dark-grey base rail;
- equally spaced white sleeper marks;
- white station centers with dark outlines;
- independent of individual service colors.

This follows the supplied official-map reference without copying its branding
or page decoration. Candidate B and C remain comparison options until real-city
geographic output is available.

## Guardrail

No style in this study changes route geometry, station order, clipping, map
bounds, exporter data, or JSON schema.

Generated SVG: $svgPath
"@
[System.IO.File]::WriteAllText($notesPath, $notes, [System.Text.UTF8Encoding]::new($false))

if (-not $SkipPng) {
    & (Join-Path $PSScriptRoot "capture-svg-screenshot.ps1") `
        -InputSvg $svgPath `
        -OutputPng $pngPath `
        -Width 1800 `
        -Height 1050
}

Write-Host "Intercity style study SVG: $svgPath"
if (-not $SkipPng) {
    Write-Host "Intercity style study PNG: $pngPath"
}
Write-Host "Notes: $notesPath"
