[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json',
    [string] $CaseName = 'product-candidate',
    [string] $OutputRoot,
    [ValidateSet('geographic', 'schematic-v2', 'schematic-map')]
    [string] $Layout = 'schematic-map',
    [ValidateSet('compact', 'standard', 'poster', 'ultra')]
    [string] $Size = 'ultra',
    [ValidateSet('standard', 'transit-map')]
    [string] $Style = 'transit-map',
    [switch] $SkipPng
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Convert-ToSafeName {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 'product-candidate'
    }

    $safe = $Value.Trim()
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string] $invalid, '-')
    }

    $safe = [regex]::Replace($safe, '\s+', '-')
    $safe = [regex]::Replace($safe, '-{2,}', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return 'product-candidate'
    }

    if ($safe.Length -gt 80) {
        $safe = $safe.Substring(0, 80).Trim('-')
    }

    return $safe
}

function Read-ExportMetadata {
    param([Parameter(Mandatory = $true)][string] $JsonPath)

    try {
        $document = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $stations = @($document.network.stations)
        $lines = @($document.network.lines)
        return [pscustomobject]@{
            CityName = if ([string]::IsNullOrWhiteSpace($document.city.name)) { 'Unnamed City' } else { $document.city.name }
            ExportedAtUtc = $document.city.exportedAtUtc
            GeneratorVersion = $document.generator.version
            LineCount = $lines.Count
            StationCount = $stations.Count
            SchemaVersion = $document.schemaVersion
        }
    }
    catch {
        return [pscustomobject]@{
            CityName = 'unreadable'
            ExportedAtUtc = 'unreadable'
            GeneratorVersion = 'unreadable'
            LineCount = 0
            StationCount = 0
            SchemaVersion = 'unreadable'
        }
    }
}

function Get-CaptureSize {
    param([Parameter(Mandatory = $true)][string] $Preset)

    switch ($Preset) {
        'compact' { return [pscustomobject]@{ Width = 1600; Height = 1000 } }
        'standard' { return [pscustomobject]@{ Width = 2200; Height = 1400 } }
        'poster' { return [pscustomobject]@{ Width = 3200; Height = 2000 } }
        'ultra' { return [pscustomobject]@{ Width = 4200; Height = 2600 } }
        default { throw "Unknown size preset: $Preset" }
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$inputPath = Get-FullPath $InputJson

if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    Write-Host "ERROR: Input JSON not found: $inputPath" -ForegroundColor Red
    Write-Host "Export Real Metro JSON in-game first, or pass -InputJson <path>." -ForegroundColor Yellow
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\product-candidate'
}

$outputRootPath = Get-FullPath $OutputRoot
$timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$safeCaseName = Convert-ToSafeName $CaseName
$outputPath = Join-Path $outputRootPath "$timestamp-$safeCaseName"
$tempOutputPath = "$outputPath.tmp"

if (Test-Path -LiteralPath $tempOutputPath) {
    Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
}

if (Test-Path -LiteralPath $outputPath) {
    throw "Product candidate output already exists: $outputPath"
}

New-Item -ItemType Directory -Force -Path $tempOutputPath | Out-Null

$metadata = Read-ExportMetadata -JsonPath $inputPath
$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'
$captureScript = Join-Path $repoRoot 'scripts\capture-svg-screenshot.ps1'
$auditScript = Join-Path $repoRoot 'scripts\analyze-schematic-map-svg.ps1'
$candidateSvg = Join-Path $tempOutputPath 'product-candidate.svg'
$candidatePng = Join-Path $tempOutputPath 'product-candidate.full.png'
$renderLog = Join-Path $tempOutputPath 'render-log.txt'
$notesPath = Join-Path $tempOutputPath 'notes.md'
$captureSize = Get-CaptureSize -Preset $Size

Copy-Item -LiteralPath $inputPath -Destination (Join-Path $tempOutputPath 'metro-export.json') -Force

$renderArguments = @(
    'run',
    '--project',
    $cliProject,
    '--no-restore',
    '--',
    $inputPath,
    $candidateSvg,
    '--layout',
    $Layout,
    '--size',
    $Size,
    '--style',
    $Style,
    '--hide-generic-labels',
    '--hide-crowded-labels',
    '--use-path-points'
)

Write-Host "Input JSON: $inputPath"
Write-Host "Output directory: $outputPath"
Write-Host "Rendering product candidate: layout=$Layout size=$Size style=$Style"

$renderOutput = & dotnet @renderArguments 2>&1
$renderOutput | Set-Content -LiteralPath $renderLog -Encoding UTF8
$renderOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "CLI product candidate render failed with exit code $LASTEXITCODE. See $renderLog"
}

if (-not $SkipPng) {
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $captureScript -InputSvg $candidateSvg -OutputPng $candidatePng -Width $captureSize.Width -Height $captureSize.Height
    if ($LASTEXITCODE -ne 0) {
        throw "Product candidate screenshot failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $auditScript -PathType Leaf) {
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $auditScript -InputSvg $candidateSvg -InputJson $inputPath -OutputDir $tempOutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Product candidate audit failed with exit code $LASTEXITCODE."
    }
}

$scoreSummaryMarkdown = '- Layout score: not available'
$scoreCsvPath = Join-Path $tempOutputPath 'schematic-map-score.csv'
if (Test-Path -LiteralPath $scoreCsvPath -PathType Leaf) {
    $scoreRows = @(Import-Csv -LiteralPath $scoreCsvPath)
    if ($scoreRows.Count -gt 0) {
        $scoreSummaryMarkdown = ($scoreRows | ForEach-Object {
            "- $($_.Category): score=$($_.Score), penalty=$($_.Penalty), count=$($_.Count)"
        }) -join "`n"
    }
}

@"
# Product Candidate Map

## Source
- Input JSON: $inputPath
- City name: $($metadata.CityName)
- Exported at UTC: $($metadata.ExportedAtUtc)
- Generator version: $($metadata.GeneratorVersion)
- Schema version: $($metadata.SchemaVersion)
- Lines: $($metadata.LineCount)
- Stations: $($metadata.StationCount)

## Render Settings
- Layout: $Layout
- Size: $Size
- Style: $Style
- UsePathPoints: true
- Hide generic station labels: true
- Hide crowded labels: true

## Generated Files
- product-candidate.svg
- product-candidate.full.png
- render-log.txt
- metro-export.json
- schematic-map-audit.txt
- schematic-map-route-segments.csv
- schematic-map-layout-conflicts.csv
- schematic-map-style-widths.csv
- schematic-map-parallel-corridors.csv
- schematic-map-crossings.csv
- schematic-map-turns.csv
- schematic-map-score.csv
- schematic-map-debug.svg
- schematic-map-debug.full.png

## Audit Score
$scoreSummaryMarkdown

## Review Notes
- Overall visual quality:
- Topology/corridor readability:
- Direction fidelity:
- Interior crossings:
- Octilinear grammar:
- Label readability:
- Route badge conflicts:
- Shared/parallel corridor readability:
- Legend/key readability:
- Known issues:
- Accept as current product candidate: yes/no
"@ | Set-Content -LiteralPath $notesPath -Encoding UTF8

Move-Item -LiteralPath $tempOutputPath -Destination $outputPath

if (Test-Path -LiteralPath $auditScript -PathType Leaf) {
    $finalSvg = Join-Path $outputPath 'product-candidate.svg'
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $auditScript -InputSvg $finalSvg -InputJson $inputPath -OutputDir $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Final product candidate audit refresh failed with exit code $LASTEXITCODE."
    }

    $debugSvg = Join-Path $outputPath 'schematic-map-debug.svg'
    $debugPng = Join-Path $outputPath 'schematic-map-debug.full.png'
    if (-not $SkipPng -and (Test-Path -LiteralPath $debugSvg -PathType Leaf)) {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $captureScript -InputSvg $debugSvg -OutputPng $debugPng -Width $captureSize.Width -Height $captureSize.Height
        if ($LASTEXITCODE -ne 0) {
            throw "Schematic map debug screenshot failed with exit code $LASTEXITCODE."
        }
    }
}

Write-Host "Product candidate written to: $outputPath"
Write-Host "SVG: $(Join-Path $outputPath 'product-candidate.svg')"
if (-not $SkipPng) {
    Write-Host "PNG: $(Join-Path $outputPath 'product-candidate.full.png')"
    Write-Host "Debug PNG: $(Join-Path $outputPath 'schematic-map-debug.full.png')"
}
