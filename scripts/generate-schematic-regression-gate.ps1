[CmdletBinding()]
param(
    [string[]] $InputJson = @(),
    [string] $OutputRoot = 'artifacts\schematic-regression',
    [int] $LatestExports = 4,
    [switch] $NoSamples,
    [switch] $SkipPng
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force -DisableNameChecking

function Get-PowerShellRunner {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -ne $pwsh -and -not [string]::IsNullOrWhiteSpace($pwsh.Source)) {
        return $pwsh.Source
    }

    $powershell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($null -ne $powershell -and -not [string]::IsNullOrWhiteSpace($powershell.Source)) {
        return $powershell.Source
    }

    throw 'Neither pwsh nor powershell was found.'
}

function Convert-ToSafeName {
    param([string] $Value, [string] $Fallback = 'case')

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Fallback
    }

    $safe = $Value.Trim()
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string] $invalid, '-')
    }

    $safe = [regex]::Replace($safe, '\s+', '-')
    $safe = [regex]::Replace($safe, '-{2,}', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return $Fallback
    }

    if ($safe.Length -gt 80) {
        $safe = $safe.Substring(0, 80).Trim('-')
    }

    return $safe
}

function Get-DefaultDiagnosticsPath {
    param([Parameter(Mandatory = $true)][string] $JsonPath)

    $directory = [System.IO.Path]::GetDirectoryName($JsonPath)
    $fileName = [System.IO.Path]::GetFileName($JsonPath)

    if ($fileName -eq 'metro-export.json') {
        return Join-Path $directory 'metro-export-diagnostics.txt'
    }

    if ($fileName -like 'metro-export-*.json') {
        $diagnosticsName = $fileName -replace '^metro-export-', 'metro-export-diagnostics-'
        $diagnosticsName = [System.IO.Path]::ChangeExtension($diagnosticsName, '.txt')
        return Join-Path $directory $diagnosticsName
    }

    return Join-Path $directory 'metro-export-diagnostics.txt'
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
            SchemaVersion = $document.schemaVersion
            LineCount = $lines.Count
            StationCount = $stations.Count
        }
    }
    catch {
        return [pscustomobject]@{
            CityName = 'unreadable'
            ExportedAtUtc = 'unreadable'
            GeneratorVersion = 'unreadable'
            SchemaVersion = 'unreadable'
            LineCount = 0
            StationCount = 0
        }
    }
}

function Add-CasePath {
    param(
        [System.Collections.Generic.List[string]] $List,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $fullPath = Get-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Write-Host "Skipping missing input: $fullPath" -ForegroundColor Yellow
        return
    }

    if (-not ($List.Contains($fullPath))) {
        $List.Add($fullPath)
    }
}

function Invoke-CliRender {
    param(
        [Parameter(Mandatory = $true)][string] $CliProject,
        [Parameter(Mandatory = $true)][string] $InputPath,
        [Parameter(Mandatory = $true)][string] $OutputPath,
        [Parameter(Mandatory = $true)][string] $Layout,
        [Parameter(Mandatory = $true)][string] $Style
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
        '--style',
        $Style,
        '--hide-generic-labels',
        '--hide-crowded-labels',
        '--use-path-points'
    )

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

    $runner = Get-PowerShellRunner
    & $runner -NoProfile -ExecutionPolicy Bypass -File $CaptureScript -InputSvg $InputSvg -OutputPng $OutputPng -Width 3200 -Height 2000
    if ($LASTEXITCODE -ne 0) {
        throw "Screenshot generation failed for '$InputSvg' with exit code $LASTEXITCODE."
    }
}

function Get-ScoreValue {
    param(
        [object[]] $Rows,
        [string] $Category,
        [string] $Column = 'Score',
        [double] $Fallback = 0
    )

    $row = $Rows | Where-Object { $_.Category -eq $Category } | Select-Object -First 1
    if ($null -eq $row) {
        return $Fallback
    }

    $value = $row.$Column
    if ([string]::IsNullOrWhiteSpace([string] $value)) {
        return $Fallback
    }

    return [double]::Parse([string] $value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-GateStatus {
    param(
        [double] $OverallScore,
        [double] $BadgeScore,
        [double] $StrokeScore,
        [double] $CrossingCount,
        [double] $ShortSegmentCount
    )

    if ($StrokeScore -lt 95 -or $BadgeScore -lt 90) {
        return 'needs-fix'
    }

    if ($ShortSegmentCount -gt 0) {
        return 'needs-review'
    }

    if ($CrossingCount -gt 5 -or $OverallScore -lt 60) {
        return 'needs-review'
    }

    return 'pass'
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$outputRootPath = Get-FullPath $OutputRoot
$timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$runPath = Join-Path $outputRootPath $timestamp
$tempRunPath = "$runPath.tmp"
$casesPath = Join-Path $tempRunPath 'cases'

if (Test-Path -LiteralPath $tempRunPath) {
    Remove-Item -LiteralPath $tempRunPath -Recurse -Force
}

if (Test-Path -LiteralPath $runPath) {
    throw "Regression output already exists: $runPath"
}

New-Item -ItemType Directory -Force -Path $casesPath | Out-Null

$casePaths = [System.Collections.Generic.List[string]]::new()
foreach ($path in $InputJson) {
    Add-CasePath -List $casePaths -Path $path
}

Add-CasePath -List $casePaths -Path 'D:\CS2MetroDiagram\metro-export.json'

$exportsPath = 'D:\CS2MetroDiagram\exports'
if ($LatestExports -gt 0 -and (Test-Path -LiteralPath $exportsPath -PathType Container)) {
    Get-ChildItem -LiteralPath $exportsPath -Filter 'metro-export-*.json' -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First $LatestExports |
        ForEach-Object { Add-CasePath -List $casePaths -Path $_.FullName }
}

if (-not $NoSamples) {
    $sampleRegressionPath = Join-Path $repoRoot 'samples\regression'
    if (Test-Path -LiteralPath $sampleRegressionPath -PathType Container) {
        Get-ChildItem -LiteralPath $sampleRegressionPath -Filter '*.json' -File |
            Sort-Object Name |
            ForEach-Object { Add-CasePath -List $casePaths -Path $_.FullName }
    }

    $sampleFallbacks = @(
        'samples\sample-metro-small.json',
        'samples\sample-metro-branch.json',
        'samples\sample-metro-loop.json',
        'samples\sample-metro-large-network.json',
        'samples\sample-metro-pathpoints.json'
    )

    foreach ($sample in $sampleFallbacks) {
        Add-CasePath -List $casePaths -Path (Join-Path $repoRoot $sample)
    }
}

if ($casePaths.Count -eq 0) {
    throw 'No regression inputs found. Pass -InputJson <path> or export a city to D:\CS2MetroDiagram.'
}

$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'
$captureScript = Join-Path $repoRoot 'scripts\capture-svg-screenshot.ps1'
$auditScript = Join-Path $repoRoot 'scripts\analyze-schematic-map-svg.ps1'
$summaryRows = [System.Collections.Generic.List[object]]::new()

Write-Host "Regression gate output: $runPath"
Write-Host "Case count: $($casePaths.Count)"

$caseNumber = 0
foreach ($inputPath in $casePaths) {
    $caseNumber++
    $metadata = Read-ExportMetadata -JsonPath $inputPath
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($inputPath)
    $caseSlug = Convert-ToSafeName -Value "$('{0:D2}' -f $caseNumber)-$($metadata.CityName)-$baseName" -Fallback "case-$caseNumber"
    $casePath = Join-Path $casesPath $caseSlug
    New-Item -ItemType Directory -Force -Path $casePath | Out-Null

    Write-Host "[$caseNumber/$($casePaths.Count)] $($metadata.CityName): $inputPath"

    $caseJson = Join-Path $casePath 'metro-export.json'
    Copy-Item -LiteralPath $inputPath -Destination $caseJson -Force

    $diagnosticsPath = Get-DefaultDiagnosticsPath -JsonPath $inputPath
    $hasDiagnostics = Test-Path -LiteralPath $diagnosticsPath -PathType Leaf
    if ($hasDiagnostics) {
        Copy-Item -LiteralPath $diagnosticsPath -Destination (Join-Path $casePath 'metro-export-diagnostics.txt') -Force
    }

    $geographicSvg = Join-Path $casePath 'geographic-baseline.svg'
    $schematicMapSvg = Join-Path $casePath 'schematic-map.svg'
    $renderLogPath = Join-Path $casePath 'render-log.txt'

    $caseRenderOutput = New-Object System.Collections.Generic.List[string]
    try {
        Invoke-CliRender -CliProject $cliProject -InputPath $inputPath -OutputPath $geographicSvg -Layout 'geographic' -Style 'transit-map' 2>&1 | ForEach-Object { $caseRenderOutput.Add([string] $_) }
        Invoke-CliRender -CliProject $cliProject -InputPath $inputPath -OutputPath $schematicMapSvg -Layout 'schematic-map' -Style 'transit-map' 2>&1 | ForEach-Object { $caseRenderOutput.Add([string] $_) }

        if (-not $SkipPng) {
            Invoke-Capture -CaptureScript $captureScript -InputSvg $geographicSvg -OutputPng (Join-Path $casePath 'geographic-baseline.full.png') 2>&1 | ForEach-Object { $caseRenderOutput.Add([string] $_) }
            Invoke-Capture -CaptureScript $captureScript -InputSvg $schematicMapSvg -OutputPng (Join-Path $casePath 'schematic-map.full.png') 2>&1 | ForEach-Object { $caseRenderOutput.Add([string] $_) }
        }

        if (Test-Path -LiteralPath $auditScript -PathType Leaf) {
            & (Get-PowerShellRunner) -NoProfile -ExecutionPolicy Bypass -File $auditScript -InputSvg $schematicMapSvg -InputJson $inputPath -OutputDir $casePath 2>&1 | ForEach-Object { $caseRenderOutput.Add([string] $_) }
            if ($LASTEXITCODE -ne 0) {
                throw "Schematic-map audit failed with exit code $LASTEXITCODE."
            }

            $debugSvg = Join-Path $casePath 'schematic-map-debug.svg'
            if (-not $SkipPng -and (Test-Path -LiteralPath $debugSvg -PathType Leaf)) {
                Invoke-Capture -CaptureScript $captureScript -InputSvg $debugSvg -OutputPng (Join-Path $casePath 'schematic-map-debug.full.png') 2>&1 | ForEach-Object { $caseRenderOutput.Add([string] $_) }
            }
        }

        $scoreRows = @()
        $scorePath = Join-Path $casePath 'schematic-map-score.csv'
        if (Test-Path -LiteralPath $scorePath -PathType Leaf) {
            $scoreRows = @(Import-Csv -LiteralPath $scorePath)
        }

        $overallScore = Get-ScoreValue -Rows $scoreRows -Category 'overall'
        $badgeScore = Get-ScoreValue -Rows $scoreRows -Category 'badge-layout'
        $strokeScore = Get-ScoreValue -Rows $scoreRows -Category 'stroke-width-consistency'
        $crossingCount = Get-ScoreValue -Rows $scoreRows -Category 'route-crossings' -Column 'Count'
        $shortSegmentCount = Get-ScoreValue -Rows $scoreRows -Category 'short-segments' -Column 'Count'
        $status = Get-GateStatus -OverallScore $overallScore -BadgeScore $badgeScore -StrokeScore $strokeScore -CrossingCount $crossingCount -ShortSegmentCount $shortSegmentCount

        $notes = @"
# Schematic Regression Case

## Source
- Input JSON: $inputPath
- Diagnostics copied: $hasDiagnostics
- City name: $($metadata.CityName)
- Lines: $($metadata.LineCount)
- Stations: $($metadata.StationCount)
- Generator version: $($metadata.GeneratorVersion)
- Schema version: $($metadata.SchemaVersion)

## Generated Files
- metro-export.json
- metro-export-diagnostics.txt, when available
- geographic-baseline.svg
- geographic-baseline.full.png
- schematic-map.svg
- schematic-map.full.png
- schematic-map-debug.svg
- schematic-map-debug.full.png
- schematic-map-score.csv
- schematic-map-crossings.csv
- schematic-map-turns.csv

## Gate Summary
- Status: $status
- Overall score: $overallScore
- Badge score: $badgeScore
- Stroke score: $strokeScore
- Interior crossing count: $crossingCount
- Short segment count: $shortSegmentCount

## Human Review
- Geographic acceptable:
- Schematic-map acceptable:
- Route continuity:
- Shared/parallel corridor readability:
- Label/badge readability:
- Framing / blank space:
- Regression notes:
"@
        $notes | Set-Content -LiteralPath (Join-Path $casePath 'notes.md') -Encoding UTF8

        $summaryRows.Add([pscustomobject]@{
            Case = $caseSlug
            Status = $status
            CityName = $metadata.CityName
            Lines = $metadata.LineCount
            Stations = $metadata.StationCount
            OverallScore = ('{0:F2}' -f $overallScore)
            BadgeScore = ('{0:F2}' -f $badgeScore)
            StrokeScore = ('{0:F2}' -f $strokeScore)
            CrossingCount = $crossingCount
            ShortSegmentCount = $shortSegmentCount
            HasPng = -not $SkipPng
            InputJson = $inputPath
            CasePath = $casePath
        })
    }
    catch {
        $summaryRows.Add([pscustomobject]@{
            Case = $caseSlug
            Status = 'render-failed'
            CityName = $metadata.CityName
            Lines = $metadata.LineCount
            Stations = $metadata.StationCount
            OverallScore = ''
            BadgeScore = ''
            StrokeScore = ''
            CrossingCount = ''
            ShortSegmentCount = ''
            HasPng = -not $SkipPng
            InputJson = $inputPath
            CasePath = $casePath
        })
        "ERROR: $($_.Exception.Message)" | Add-Content -LiteralPath (Join-Path $casePath 'notes.md') -Encoding UTF8
        Write-Host "Case failed: $caseSlug - $($_.Exception.Message)" -ForegroundColor Red
    }
    finally {
        $caseRenderOutput | Set-Content -LiteralPath $renderLogPath -Encoding UTF8
    }
}

$summaryCsv = Join-Path $tempRunPath 'regression-summary.csv'
$summaryMarkdown = Join-Path $tempRunPath 'index.md'
$manifestPath = Join-Path $tempRunPath 'manifest.json'

$summaryRows | Export-Csv -LiteralPath $summaryCsv -NoTypeInformation -Encoding UTF8

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add('# Schematic Regression Gate')
$markdown.Add('')
$markdown.Add("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$markdown.Add("- Case count: $($summaryRows.Count)")
$markdown.Add("- PNG generated: $(-not $SkipPng)")
$markdown.Add('')
$markdown.Add('| Status | Case | City | Lines | Stations | Overall | Badge | Stroke | Crossings | Short segments |')
$markdown.Add('| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($row in $summaryRows) {
    $markdown.Add("| $($row.Status) | $($row.Case) | $($row.CityName) | $($row.Lines) | $($row.Stations) | $($row.OverallScore) | $($row.BadgeScore) | $($row.StrokeScore) | $($row.CrossingCount) | $($row.ShortSegmentCount) |")
}

$markdown.Add('')
$markdown.Add('## Review Order')
$markdown.Add('')
$markdown.Add('1. Open each `geographic-baseline.full.png` first to confirm the export itself is plausible.')
$markdown.Add('2. Open each `schematic-map.full.png` for product-map review.')
$markdown.Add('3. Open `schematic-map-debug.full.png` when score rows show crossings or octilinear warnings.')
$markdown.Add('4. Treat `needs-review` as a human-review flag, not an automatic rejection.')
$markdown.Add('')
$markdown.Add('## Gate Criteria')
$markdown.Add('')
$markdown.Add('- `needs-fix`: badge or stroke-width consistency is below the current safety threshold.')
$markdown.Add('- `needs-review`: the render completed but crossings, short segments, or low overall score need human review.')
$markdown.Add('- `pass`: the case cleared the current automated safety checks.')

$markdown | Set-Content -LiteralPath $summaryMarkdown -Encoding UTF8

$manifest = [pscustomobject]@{
    generatedAt = (Get-Date).ToString('o')
    caseCount = $summaryRows.Count
    skipPng = [bool] $SkipPng
    latestExports = $LatestExports
    cases = $summaryRows
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Move-Item -LiteralPath $tempRunPath -Destination $runPath

$latestPath = Join-Path $outputRootPath 'latest'
if (Test-Path -LiteralPath $latestPath) {
    Remove-Item -LiteralPath $latestPath -Recurse -Force
}

$latestTextPath = Join-Path $outputRootPath 'latest.txt'
try {
    New-Item -ItemType SymbolicLink -Path $latestPath -Target $runPath | Out-Null
}
catch {
    Write-Host "Could not create latest symbolic link. Writing latest.txt instead: $($_.Exception.Message)" -ForegroundColor Yellow
}

"Latest regression gate: $runPath" | Set-Content -LiteralPath $latestTextPath -Encoding UTF8

Write-Host "Schematic regression gate written to: $runPath"
Write-Host "Summary: $(Join-Path $runPath 'index.md')"
Write-Host "CSV: $(Join-Path $runPath 'regression-summary.csv')"
