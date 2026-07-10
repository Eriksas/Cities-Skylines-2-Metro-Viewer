[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json',
    [string] $DiagnosticsPath,
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force -DisableNameChecking

function Invoke-CliRender {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CliProject,
        [Parameter(Mandatory = $true)]
        [string] $InputPath,
        [Parameter(Mandatory = $true)]
        [string] $OutputPath
    )

    $arguments = @(
        'run',
        '--project',
        $CliProject,
        '--no-restore',
        '--',
        $InputPath,
        $OutputPath,
        '--layout',
        'geographic',
        '--size',
        'poster',
        '--use-path-points',
        '--simplify-path-points'
    )

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "CLI baseline render failed with exit code $LASTEXITCODE."
    }
}

function Read-ExportMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string] $JsonPath
    )

    try {
        $document = Get-Content -LiteralPath $JsonPath -Encoding UTF8 -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            CityName = $document.city.name
            ExportedAtUtc = $document.city.exportedAtUtc
            GeneratorVersion = $document.generator.version
        }
    }
    catch {
        return [pscustomobject]@{
            CityName = 'unreadable'
            ExportedAtUtc = 'unreadable'
            GeneratorVersion = 'unreadable'
        }
    }
}

function Write-BaselineNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [string] $InputPath,
        [Parameter(Mandatory = $true)]
        [string] $DiagnosticsInputPath,
        [Parameter(Mandatory = $true)]
        [string] $SnapshotPath,
        [Parameter(Mandatory = $true)]
        [string] $GeneratedAt,
        [Parameter(Mandatory = $true)]
        $Metadata
    )

    @"
# Primary City Baseline

## Source
- Input JSON: $InputPath
- Diagnostics: $DiagnosticsInputPath
- Export timestamp: $($Metadata.ExportedAtUtc)
- Snapshot path: $SnapshotPath
- City name: $($Metadata.CityName)
- Generator version: $($Metadata.GeneratorVersion)
- Baseline generated at: $GeneratedAt

## Render Settings
- Layout: geographic
- UsePathPoints: true
- Service family merge: enabled
- Shared corridor: disabled
- Express stripe: disabled
- Size preset: poster
- Station markers: white-filled circles with dark outlines
- Station route anchoring: enabled for geographic pathPoints rendering
- Label settings: default renderer label font size, halo, and station offset
- Legend settings: default renderer legend width, font size, and row spacing
- Framing: default renderer padding with reserved right-side legend lane
- Path simplification: enabled
- Title fallback: `CS2 Metro Diagram` when the export contains the CS2 placeholder city name

## Visual Review
- Overall readability:
- Route continuity:
- Stroke width consistency:
- Station marker readability:
- Label readability:
- Known issues:

## Decision
- Accept as current baseline: yes/no
- Needs 5A.9 Route Run Stitcher: yes/no
- Notes:

## Generated Files
- metro-export.json
- metro-export-diagnostics.txt
- baseline-geographic.svg
- baseline-geographic.full.png
- visual-continuity-summary.txt
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$inputPath = Get-FullPath $InputJson

if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    Write-Host "ERROR: Input metro export JSON was not found: $inputPath. Export Real Metro JSON in-game first, or pass -InputJson <path>." -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($DiagnosticsPath)) {
    $DiagnosticsPath = Get-DefaultDiagnosticsPath -JsonPath $inputPath
}

$diagnosticsInputPath = Get-FullPath $DiagnosticsPath
if (-not (Test-Path -LiteralPath $diagnosticsInputPath -PathType Leaf)) {
    Write-Host "WARNING: Diagnostics file was not found: $diagnosticsInputPath. The baseline will still be generated." -ForegroundColor Yellow
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\primary-city-baseline'
}

$outputRootPath = Get-FullPath $OutputRoot
$latestPath = Join-Path $outputRootPath 'latest'
$historyRootPath = Join-Path $outputRootPath 'history'
$runTimestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$historyPath = Join-Path $historyRootPath $runTimestamp
$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'
$captureScript = Join-Path $repoRoot 'scripts\capture-svg-screenshot.ps1'
$visualScript = Join-Path $repoRoot 'scripts\analyze-visual-continuity.ps1'
$metadata = Read-ExportMetadata -JsonPath $inputPath

New-Item -ItemType Directory -Force -Path $latestPath | Out-Null

$latestJson = Join-Path $latestPath 'metro-export.json'
$latestDiagnostics = Join-Path $latestPath 'metro-export-diagnostics.txt'
$latestSvg = Join-Path $latestPath 'baseline-geographic.svg'
$latestPng = Join-Path $latestPath 'baseline-geographic.full.png'
$latestReport = Join-Path $latestPath 'visual-continuity-summary.txt'
$latestDebugSvg = Join-Path $latestPath 'visual-continuity-debug.svg'
$latestNotes = Join-Path $latestPath 'notes.md'

Write-Host "Input JSON: $inputPath"
Write-Host "Diagnostics: $diagnosticsInputPath"
Write-Host "Baseline latest directory: $latestPath"
Write-Host "Baseline history directory: $historyPath"

Copy-Item -LiteralPath $inputPath -Destination $latestJson -Force
if (Test-Path -LiteralPath $diagnosticsInputPath -PathType Leaf) {
    Copy-Item -LiteralPath $diagnosticsInputPath -Destination $latestDiagnostics -Force
}

Invoke-CliRender -CliProject $cliProject -InputPath $latestJson -OutputPath $latestSvg

& powershell -NoProfile -ExecutionPolicy Bypass -File $captureScript -InputSvg $latestSvg -OutputPng $latestPng -Width 3200 -Height 2000
if ($LASTEXITCODE -ne 0) {
    throw "Baseline PNG screenshot failed with exit code $LASTEXITCODE."
}

& powershell -NoProfile -ExecutionPolicy Bypass -File $visualScript -InputSvg $latestSvg -OutputReport $latestReport -OutputDebugSvg $latestDebugSvg
if ($LASTEXITCODE -ne 0) {
    throw "Visual continuity analysis failed with exit code $LASTEXITCODE."
}

$snapshotPath = if ($inputPath -like '*\exports\*') { $inputPath } else { 'latest export file; no explicit snapshot path was provided' }
Write-BaselineNotes -Path $latestNotes -InputPath $inputPath -DiagnosticsInputPath $diagnosticsInputPath -SnapshotPath $snapshotPath -GeneratedAt (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -Metadata $metadata

New-Item -ItemType Directory -Force -Path $historyPath | Out-Null
Copy-Item -Path (Join-Path $latestPath '*') -Destination $historyPath -Recurse -Force

Write-Host "Generated baseline SVG: $latestSvg"
Write-Host "Generated baseline PNG: $latestPng"
Write-Host "Generated visual continuity summary: $latestReport"
Write-Host "Generated notes: $latestNotes"
Write-Host "Archived this baseline run under: $historyPath"
