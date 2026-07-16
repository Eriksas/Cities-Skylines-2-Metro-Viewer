<#
.SYNOPSIS
Reproduces the two CS2 in-game preview render profiles from a real export.

.DESCRIPTION
The in-game preview intentionally uses MetroDiagram.Engine's portable renderer,
not the desktop Viewer renderer. This script keeps visual QA honest by loading
the same snapshot, applying the same 1800x1100 profiles, and producing SVG,
PNG, route-chain diagnostics, and the shared SVG audit report.

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-in-game-preview-audit.ps1

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\generate-in-game-preview-audit.ps1 `
  -InputJson D:\CS2MetroDiagram\exports\metro-export-MyCity-20260715-120000.json
#>
[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json',

    [string] $OutputDir,

    [switch] $ShowGenericStationNames,

    [switch] $ShowCrowdedLabels,

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force

$repoRoot = Split-Path -Parent $PSScriptRoot
$inputPath = [System.IO.Path]::GetFullPath($InputJson)
if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    throw "Metro export JSON was not found: $inputPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot ('artifacts\ingame-schematic-audit\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$outputPath = [System.IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

if (-not $NoBuild) {
    & dotnet build (Join-Path $repoRoot 'src\MetroDiagram.Core\MetroDiagram.Core.csproj') --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'MetroDiagram.Core/Engine build failed; audit output was not generated.'
    }
}

$engineAssembly = Join-Path $repoRoot 'src\MetroDiagram.Engine\bin\Debug\netstandard2.0\MetroDiagram.Engine.dll'
$coreAssembly = Join-Path $repoRoot 'src\MetroDiagram.Core\bin\Debug\net8.0\MetroDiagram.Core.dll'
foreach ($assembly in @($engineAssembly, $coreAssembly)) {
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw "Required audit assembly was not found: $assembly"
    }
}

Add-Type -Path $engineAssembly
Add-Type -Path $coreAssembly

$loadResult = [MetroDiagram.Core.Loading.MetroJsonLoader]::LoadFromFile($inputPath)
if (-not $loadResult.IsValid -or $null -eq $loadResult.Document) {
    $issues = @($loadResult.Issues | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
    throw "Metro export JSON is invalid: $inputPath`n$issues"
}

$snapshot = [MetroDiagram.Core.Models.MetroNetworkSnapshotAdapter]::FromDocument($loadResult.Document)
$hideCrowdedLabels = -not $ShowCrowdedLabels
$schematicOptions = [MetroDiagram.Engine.PortableRenderProfiles]::CreateInGameSchematic(
    [bool] $ShowGenericStationNames,
    [bool] $hideCrowdedLabels)
$geographicOptions = [MetroDiagram.Engine.PortableRenderProfiles]::CreateInGameGeographic(
    [bool] $ShowGenericStationNames,
    [bool] $hideCrowdedLabels)

$renderer = [MetroDiagram.Engine.PortableMetroSvgRenderer]::new()
$schematic = $renderer.Render($snapshot, $schematicOptions)
$geographic = $renderer.Render($snapshot, $geographicOptions)

$schematicSvg = Join-Path $outputPath 'in-game-portable-schematic.svg'
$geographicSvg = Join-Path $outputPath 'in-game-portable-geographic.svg'
[System.IO.File]::WriteAllText($schematicSvg, $schematic.Svg, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($geographicSvg, $geographic.Svg, [System.Text.UTF8Encoding]::new($false))

Copy-Item -LiteralPath $inputPath -Destination (Join-Path $outputPath 'input-metro-export.json') -Force
$diagnosticsPath = Get-DefaultDiagnosticsPath -JsonPath $inputPath
if (Test-Path -LiteralPath $diagnosticsPath -PathType Leaf) {
    Copy-Item -LiteralPath $diagnosticsPath -Destination (Join-Path $outputPath 'input-metro-export-diagnostics.txt') -Force
}

$captureScript = Join-Path $PSScriptRoot 'capture-svg-screenshot.ps1'
& $captureScript -InputSvg $schematicSvg -OutputPng (Join-Path $outputPath 'in-game-portable-schematic.full.png') -Width $schematicOptions.Width -Height $schematicOptions.Height
& $captureScript -InputSvg $geographicSvg -OutputPng (Join-Path $outputPath 'in-game-portable-geographic.full.png') -Width $geographicOptions.Width -Height $geographicOptions.Height

$analysisDir = Join-Path $outputPath 'schematic-analysis'
& (Join-Path $PSScriptRoot 'analyze-schematic-map-svg.ps1') `
    -InputSvg $schematicSvg `
    -InputJson $inputPath `
    -OutputDir $analysisDir

[xml] $schematicXml = Get-Content -LiteralPath $schematicSvg -Raw -Encoding UTF8
$namespace = [System.Xml.XmlNamespaceManager]::new($schematicXml.NameTable)
$namespace.AddNamespace('svg', 'http://www.w3.org/2000/svg')
$routes = @($schematicXml.SelectNodes('//svg:polyline[@class="route"]', $namespace))
$labels = @($schematicXml.SelectNodes('//svg:text[@class="station-label"]', $namespace))
$normalizedRoutes = @($routes | Where-Object { $_.GetAttribute('data-route-chain-normalized') -eq 'true' })
$routeRows = foreach ($route in $routes) {
    [pscustomobject]@{
        family = $route.GetAttribute('data-display-family')
        lineId = $route.GetAttribute('data-line-id')
        rawStops = $route.GetAttribute('data-raw-stop-count')
        renderedStops = $route.GetAttribute('data-render-stop-count')
        normalized = $route.GetAttribute('data-route-chain-normalized')
    }
}
$routeRows | Export-Csv -LiteralPath (Join-Path $outputPath 'route-chain-summary.csv') -NoTypeInformation -Encoding UTF8

$labelPositionRows = $labels |
    Group-Object { $_.GetAttribute('data-label-position') } |
    Sort-Object Name |
    ForEach-Object { [pscustomobject]@{ position = $_.Name; count = $_.Count } }
$labelPositionRows | Export-Csv -LiteralPath (Join-Path $outputPath 'label-position-summary.csv') -NoTypeInformation -Encoding UTF8

$summary = @(
    'CS2 Metro Diagram - In-game Preview Audit'
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
    "Input JSON: $inputPath"
    "City: $($snapshot.CityName)"
    "Stations: $($snapshot.Stations.Count)"
    "Lines: $($snapshot.Lines.Count)"
    ''
    'In-game profiles:'
    "- canvas: $($schematicOptions.Width)x$($schematicOptions.Height)"
    "- generic station names: $([bool] $ShowGenericStationNames)"
    "- hide crowded labels: $hideCrowdedLabels"
    "- schematic elapsed: $($schematic.ElapsedMilliseconds) ms"
    "- geographic elapsed: $($geographic.ElapsedMilliseconds) ms"
    ''
    'Schematic output:'
    "- rendered route families: $($routes.Count)"
    "- normalized mirrored route chains: $($normalizedRoutes.Count)"
    "- visible station labels: $($labels.Count)"
    "- SVG: $schematicSvg"
    "- PNG: $(Join-Path $outputPath 'in-game-portable-schematic.full.png')"
    "- SVG audit: $(Join-Path $analysisDir 'schematic-map-audit.txt')"
    ''
    'Geographic safety comparison:'
    "- SVG: $geographicSvg"
    "- PNG: $(Join-Path $outputPath 'in-game-portable-geographic.full.png')"
)
$summary | Set-Content -LiteralPath (Join-Path $outputPath 'audit-summary.txt') -Encoding UTF8

Write-Host ''
Write-Host "In-game preview audit written to: $outputPath" -ForegroundColor Green
$summary | ForEach-Object { Write-Host $_ }
