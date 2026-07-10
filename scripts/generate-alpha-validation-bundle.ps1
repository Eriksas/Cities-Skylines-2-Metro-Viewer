[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json',
    [string] $CaseName = 'primary-city',
    [string] $OutputRoot,
    [switch] $SkipZip,
    [switch] $SkipPng
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force -DisableNameChecking

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

function Get-CurrentAppVersion {
    param([Parameter(Mandatory = $true)][string] $RepoRoot)

    $appInfoPath = Join-Path $RepoRoot 'src\MetroDiagram.Core\MetroDiagramAppInfo.cs'
    if (-not (Test-Path -LiteralPath $appInfoPath -PathType Leaf)) {
        return 'unknown'
    }

    $content = Get-Content -LiteralPath $appInfoPath -Raw -Encoding UTF8
    $match = [regex]::Match($content, 'Version\s*=\s*"([^"]+)"')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return 'unknown'
}

function Get-ValidationWarnings {
    param(
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)][string] $CurrentAppVersion
    )

    $warnings = New-Object System.Collections.Generic.List[string]

    if ($Metadata.GeneratorVersion -eq 'unreadable') {
        $warnings.Add('Export generator version could not be read. Treat this bundle as diagnostic-only until the JSON is inspected.')
    }
    elseif ($CurrentAppVersion -ne 'unknown' -and $Metadata.GeneratorVersion -ne $CurrentAppVersion) {
        $warnings.Add("Export generator version '$($Metadata.GeneratorVersion)' differs from current tool version '$CurrentAppVersion'. Re-export in CS2 before final alpha validation if exporter behavior matters.")
    }

    if ($Metadata.CityName -eq 'CS2 Metro Export') {
        $warnings.Add("City name is the exporter placeholder 'CS2 Metro Export'. This is expected for older exports, but feedback should identify the real city manually.")
    }

    return $warnings
}

function Format-MarkdownList {
    param([string[]] $Items = @())

    if ($Items.Count -eq 0) {
        return '- none'
    }

    return ($Items | ForEach-Object { "- $_" }) -join "`r`n"
}

function Invoke-CliRender {
    param(
        [Parameter(Mandatory = $true)][string] $CliProject,
        [Parameter(Mandatory = $true)][string] $InputPath,
        [Parameter(Mandatory = $true)][string] $OutputPath,
        [Parameter(Mandatory = $true)][string] $Layout,
        [switch] $UsePathPoints
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
        $Layout,
        '--size',
        'poster',
        '--hide-generic-labels',
        '--hide-crowded-labels'
    )

    if ($UsePathPoints) {
        $arguments += '--use-path-points'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "CLI render failed for layout '$Layout' with exit code $LASTEXITCODE."
    }
}

function Invoke-Capture {
    param(
        [Parameter(Mandatory = $true)][string] $CaptureScript,
        [Parameter(Mandatory = $true)][string] $InputSvg,
        [Parameter(Mandatory = $true)][string] $OutputPng
    )

    $powerShellRunner = Get-PowerShellRunner
    & $powerShellRunner -NoProfile -ExecutionPolicy Bypass -File $CaptureScript -InputSvg $InputSvg -OutputPng $OutputPng -Width 3200 -Height 2000
    if ($LASTEXITCODE -ne 0) {
        throw "Screenshot generation failed for '$InputSvg' with exit code $LASTEXITCODE."
    }
}

function Write-ValidationNotes {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $InputPath,
        [Parameter(Mandatory = $true)][string] $DiagnosticsPath,
        [Parameter(Mandatory = $true)][string] $GeneratedAt,
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)][string] $BundlePath,
        [Parameter(Mandatory = $true)][string] $CurrentAppVersion,
        [string[]] $ValidationWarnings = @()
    )

    $warningText = Format-MarkdownList -Items $ValidationWarnings

    @"
# Alpha Validation Bundle

## Source
- Input JSON: $InputPath
- Diagnostics: $DiagnosticsPath
- Bundle path: $BundlePath
- Bundle generated at: $GeneratedAt
- City name: $($Metadata.CityName)
- Export timestamp: $($Metadata.ExportedAtUtc)
- Generator version: $($Metadata.GeneratorVersion)
- Current tool version: $CurrentAppVersion
- Schema version: $($Metadata.SchemaVersion)
- Lines: $($Metadata.LineCount)
- Stations: $($Metadata.StationCount)

## Validation Warnings
$warningText

## Render Settings
- Baseline layout: geographic
- UsePathPoints: true
- Service family merge: enabled
- Shared corridor: disabled
- Express stripe: disabled
- Size preset: poster
- Label filters: hide generic labels, hide crowded labels
- Product candidate layout: schematic-anneal
- Comparison layouts: schematic-map, schematic-v2
- Schematic-anneal status: current default product candidate
- Schematic-v2 status: experimental
- Schematic-map status: retained comparison layout

## Visual Review
- Geographic baseline acceptable: yes/no
- Schematic-v2 topology issues:
- Label readability issues:
- Legend readability issues:
- Station marker issues:
- Route continuity issues:

## Regression Decision
- Suitable as regression case: yes/no
- Priority: low/medium/high
- Notes:

## Generated Files
- metro-export.json
- metro-export-diagnostics.txt, if available
- baseline-geographic.svg
- baseline-geographic.full.png, unless -SkipPng was used
- visual-continuity-summary.txt
- visual-continuity-debug.svg
- schematic-v2.svg
- schematic-v2.full.png, unless -SkipPng was used
- schematic-map.svg
- schematic-map.full.png, unless -SkipPng was used
- schematic-anneal.svg
- schematic-anneal.full.png, unless -SkipPng was used
- schematic-v2-diagnostics\
- manifest.json
- feedback-template-filled.md
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-FilledFeedbackTemplate {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $BundlePath,
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)][string] $CurrentAppVersion,
        [string[]] $ValidationWarnings = @()
    )

    $warningText = Format-MarkdownList -Items $ValidationWarnings

    @"
# Feedback Template - v0.1.0-beta.1

## Environment

- CS2 Metro Diagram validation tool version: $CurrentAppVersion
- Export generator version: $($Metadata.GeneratorVersion)
- Cities: Skylines II game version:
- Operating system:
- Are other transport-related mods enabled? If yes, list them:
- Alpha validation bundle path: $BundlePath

## Validation Warnings
$warningText

## City / Network

- City name: $($Metadata.CityName)
- Approximate number of metro lines: $($Metadata.LineCount)
- Approximate number of metro stations: $($Metadata.StationCount)
- Does the city contain loops, branches, or many interchanges?

## Files To Attach

- metro-export.json
- metro-export-diagnostics.txt, if available
- baseline-geographic.svg
- baseline-geographic.full.png, if generated
- schematic-v2.svg
- schematic-v2.full.png, if generated
- schematic-map.svg
- schematic-map.full.png, if generated
- schematic-anneal.svg
- schematic-anneal.full.png, if generated
- schematic-v2-diagnostics\shared-corridors.txt
- manifest.json
- Viewer settings from Documents\CS2MetroDiagram\viewer-settings.json, if available
- Relevant game/mod log excerpt if export failed

## Viewer Settings

- Layout mode: schematic-anneal / geographic / schematic-map / schematic-v2
- Width:
- Height:
- Label font size:
- Grid size:
- Hide generic station labels: yes/no
- Hide crowded labels: yes/no
- Always show interchanges: yes/no
- Always show terminals: yes/no

## Issue

- What did you expect to happen?
- What happened instead?
- Steps to reproduce:
- Any error text shown in the Viewer or game log:
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-BundleManifest {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $BundleName,
        [Parameter(Mandatory = $true)][string] $InputPath,
        [Parameter(Mandatory = $true)][string] $DiagnosticsPath,
        [Parameter(Mandatory = $true)][string] $GeneratedAt,
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)][string] $CurrentAppVersion,
        [Parameter(Mandatory = $true)][bool] $ScreenshotsGenerated,
        [string[]] $ValidationWarnings = @()
    )

    $manifest = [ordered]@{
        bundleName = $BundleName
        generatedAt = $GeneratedAt
        source = [ordered]@{
            inputJson = $InputPath
            diagnostics = $DiagnosticsPath
        }
        export = [ordered]@{
            cityName = $Metadata.CityName
            exportedAtUtc = $Metadata.ExportedAtUtc
            generatorVersion = $Metadata.GeneratorVersion
            schemaVersion = $Metadata.SchemaVersion
            lineCount = $Metadata.LineCount
            stationCount = $Metadata.StationCount
        }
        tool = [ordered]@{
            version = $CurrentAppVersion
        }
        recommendedReviewOrder = @(
            'schematic-anneal.full.png',
            'baseline-geographic.full.png',
            'schematic-map.full.png',
            'schematic-v2.full.png',
            'visual-continuity-summary.txt',
            'schematic-v2-diagnostics\topology-summary.txt',
            'notes.md'
        )
        files = [ordered]@{
            exportJson = 'metro-export.json'
            exportDiagnostics = 'metro-export-diagnostics.txt'
            geographicSvg = 'baseline-geographic.svg'
            geographicPng = 'baseline-geographic.full.png'
            schematicV2Svg = 'schematic-v2.svg'
            schematicV2Png = 'schematic-v2.full.png'
            schematicMapSvg = 'schematic-map.svg'
            schematicMapPng = 'schematic-map.full.png'
            schematicAnnealSvg = 'schematic-anneal.svg'
            schematicAnnealPng = 'schematic-anneal.full.png'
            visualContinuityReport = 'visual-continuity-summary.txt'
            visualContinuityDebugSvg = 'visual-continuity-debug.svg'
            schematicV2Diagnostics = 'schematic-v2-diagnostics'
            notes = 'notes.md'
            feedbackTemplate = 'feedback-template-filled.md'
        }
        renderSettings = [ordered]@{
            alphaBaseline = [ordered]@{
                layout = 'geographic'
                size = 'poster'
                usePathPoints = $true
                hideGenericLabels = $true
                hideCrowdedLabels = $true
            }
            productCandidate = [ordered]@{
                layout = 'schematic-anneal'
                size = 'poster'
                status = 'current default product candidate'
            }
            comparisons = @('schematic-map', 'schematic-v2')
            screenshotsGenerated = $ScreenshotsGenerated
        }
        validationWarnings = $ValidationWarnings
    }

    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
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
    $OutputRoot = Join-Path $repoRoot 'artifacts\alpha-validation'
}

$outputRootPath = Get-FullPath $OutputRoot
$timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$safeCaseName = Convert-ToSafeName $CaseName
$bundleName = "$timestamp-$safeCaseName"
$bundlePath = Join-Path $outputRootPath $bundleName
$tempBundlePath = "$bundlePath.tmp"
$zipPath = Join-Path $outputRootPath "alpha-validation-$bundleName.zip"
$diagnosticsInputPath = Get-FullPath (Get-DefaultDiagnosticsPath -JsonPath $inputPath)
$metadata = Read-ExportMetadata -JsonPath $inputPath
$currentAppVersion = Get-CurrentAppVersion -RepoRoot $repoRoot
$validationWarnings = @(Get-ValidationWarnings -Metadata $metadata -CurrentAppVersion $currentAppVersion)

if (Test-Path -LiteralPath $tempBundlePath) {
    Remove-Item -LiteralPath $tempBundlePath -Recurse -Force
}

if (Test-Path -LiteralPath $bundlePath) {
    throw "Validation bundle already exists: $bundlePath"
}

New-Item -ItemType Directory -Force -Path $tempBundlePath | Out-Null

$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'
$captureScript = Join-Path $repoRoot 'scripts\capture-svg-screenshot.ps1'
$visualScript = Join-Path $repoRoot 'scripts\analyze-visual-continuity.ps1'
$schematicDiagnosticsScript = Join-Path $repoRoot 'scripts\generate-schematic-v2-diagnostics.ps1'

try {
    Write-Host "Input JSON: $inputPath"
    Write-Host "Case name: $safeCaseName"
    Write-Host "Temporary bundle: $tempBundlePath"

    $bundleJson = Join-Path $tempBundlePath 'metro-export.json'
    $bundleDiagnostics = Join-Path $tempBundlePath 'metro-export-diagnostics.txt'
    Copy-Item -LiteralPath $inputPath -Destination $bundleJson -Force

    if (Test-Path -LiteralPath $diagnosticsInputPath -PathType Leaf) {
        Copy-Item -LiteralPath $diagnosticsInputPath -Destination $bundleDiagnostics -Force
    }
    else {
        Write-Host "WARNING: Diagnostics file not found: $diagnosticsInputPath" -ForegroundColor Yellow
    }

    $viewerSettings = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CS2MetroDiagram\viewer-settings.json'
    if (Test-Path -LiteralPath $viewerSettings -PathType Leaf) {
        Copy-Item -LiteralPath $viewerSettings -Destination (Join-Path $tempBundlePath 'viewer-settings.json') -Force
    }

    $baselineSvg = Join-Path $tempBundlePath 'baseline-geographic.svg'
    $baselinePng = Join-Path $tempBundlePath 'baseline-geographic.full.png'
    $schematicV2Svg = Join-Path $tempBundlePath 'schematic-v2.svg'
    $schematicV2Png = Join-Path $tempBundlePath 'schematic-v2.full.png'
    $schematicMapSvg = Join-Path $tempBundlePath 'schematic-map.svg'
    $schematicMapPng = Join-Path $tempBundlePath 'schematic-map.full.png'
    $schematicAnnealSvg = Join-Path $tempBundlePath 'schematic-anneal.svg'
    $schematicAnnealPng = Join-Path $tempBundlePath 'schematic-anneal.full.png'
    $visualReport = Join-Path $tempBundlePath 'visual-continuity-summary.txt'
    $visualDebugSvg = Join-Path $tempBundlePath 'visual-continuity-debug.svg'

    Invoke-CliRender -CliProject $cliProject -InputPath $bundleJson -OutputPath $baselineSvg -Layout 'geographic' -UsePathPoints
    Invoke-CliRender -CliProject $cliProject -InputPath $bundleJson -OutputPath $schematicV2Svg -Layout 'schematic-v2'
    Invoke-CliRender -CliProject $cliProject -InputPath $bundleJson -OutputPath $schematicMapSvg -Layout 'schematic-map' -UsePathPoints
    Invoke-CliRender -CliProject $cliProject -InputPath $bundleJson -OutputPath $schematicAnnealSvg -Layout 'schematic-anneal' -UsePathPoints

    if ($SkipPng) {
        Write-Host 'Skipping PNG screenshot generation because -SkipPng was specified.'
    }
    else {
        Invoke-Capture -CaptureScript $captureScript -InputSvg $baselineSvg -OutputPng $baselinePng
        Invoke-Capture -CaptureScript $captureScript -InputSvg $schematicV2Svg -OutputPng $schematicV2Png
        Invoke-Capture -CaptureScript $captureScript -InputSvg $schematicMapSvg -OutputPng $schematicMapPng
        Invoke-Capture -CaptureScript $captureScript -InputSvg $schematicAnnealSvg -OutputPng $schematicAnnealPng
    }

    $powerShellRunner = Get-PowerShellRunner
    & $powerShellRunner -NoProfile -ExecutionPolicy Bypass -File $visualScript -InputSvg $baselineSvg -OutputReport $visualReport -OutputDebugSvg $visualDebugSvg
    if ($LASTEXITCODE -ne 0) {
        throw "Visual continuity analysis failed with exit code $LASTEXITCODE."
    }

    Write-ValidationNotes -Path (Join-Path $tempBundlePath 'notes.md') -InputPath $inputPath -DiagnosticsPath $diagnosticsInputPath -GeneratedAt (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -Metadata $metadata -BundlePath $bundlePath -CurrentAppVersion $currentAppVersion -ValidationWarnings $validationWarnings
    Write-FilledFeedbackTemplate -Path (Join-Path $tempBundlePath 'feedback-template-filled.md') -BundlePath $bundlePath -Metadata $metadata -CurrentAppVersion $currentAppVersion -ValidationWarnings $validationWarnings

    Move-Item -LiteralPath $tempBundlePath -Destination $bundlePath

    $finalBundleJson = Join-Path $bundlePath 'metro-export.json'
    $finalDiagnosticsOutput = Join-Path $bundlePath 'schematic-v2-diagnostics'
    & $powerShellRunner -NoProfile -ExecutionPolicy Bypass -File $schematicDiagnosticsScript -InputJson $finalBundleJson -OutputDir $finalDiagnosticsOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Schematic-v2 diagnostics failed with exit code $LASTEXITCODE."
    }

    Write-BundleManifest `
        -Path (Join-Path $bundlePath 'manifest.json') `
        -BundleName $bundleName `
        -InputPath $inputPath `
        -DiagnosticsPath $diagnosticsInputPath `
        -GeneratedAt (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') `
        -Metadata $metadata `
        -CurrentAppVersion $currentAppVersion `
        -ScreenshotsGenerated (-not $SkipPng) `
        -ValidationWarnings $validationWarnings

    if (-not $SkipZip) {
        if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -LiteralPath $bundlePath -DestinationPath $zipPath -Force
    }

    Write-Host "Alpha validation bundle written to: $bundlePath"
    if (-not $SkipZip) {
        Write-Host "Alpha validation zip written to: $zipPath"
    }
}
catch {
    if (Test-Path -LiteralPath $tempBundlePath) {
        Remove-Item -LiteralPath $tempBundlePath -Recurse -Force
    }

    if (Test-Path -LiteralPath $bundlePath) {
        Remove-Item -LiteralPath $bundlePath -Recurse -Force
    }

    if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    throw
}
