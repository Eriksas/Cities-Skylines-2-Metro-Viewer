<#
.SYNOPSIS
Generates desktop and in-game portable geographic passenger-rail evidence.

.DESCRIPTION
This is a development-only audit. It enables the hidden intercity rendering
switch directly on renderer options and does not expose that switch through
the Viewer, CLI, or CS2 settings.

The local player exports listed by the fixture manifest are read in place.
They are not copied into the repository or the audit output.
#>
[CmdletBinding()]
param(
    [string] $ManifestPath = 'samples\intercity-geographic\real-fixture-manifest.json',

    [string] $OutputDir = 'artifacts\intercity-geographic-v2\renderer-audit',

    [switch] $NoBuild,

    [switch] $SkipPng
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function ConvertTo-SafeSlug {
    param(
        [AllowEmptyString()]
        [string] $Value
    )

    $slug = if ([string]::IsNullOrWhiteSpace($Value)) { 'UnnamedCity' } else { $Value.Trim() }
    foreach ($character in [System.IO.Path]::GetInvalidFileNameChars()) {
        $slug = $slug.Replace([string] $character, '-')
    }

    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '\s+', '-').Trim('-', '.', ' ')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return 'UnnamedCity'
    }

    if ($slug.Length -gt 48) {
        return $slug.Substring(0, 48).TrimEnd('-', '.', ' ')
    }

    return $slug
}

function Get-SvgAudit {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SvgPath
    )

    $xml = [xml](Get-Content -LiteralPath $SvgPath -Raw -Encoding UTF8)
    $manager = [System.Xml.XmlNamespaceManager]::new($xml.NameTable)
    $manager.AddNamespace('svg', 'http://www.w3.org/2000/svg')
    $sleepers = @($xml.SelectNodes('//*[@class and contains(concat(" ", normalize-space(@class), " "), " intercity-rail-sleepers ")]', $manager))
    $explicitPatterns = @($sleepers | Where-Object {
        $_.GetAttribute('data-intercity-pattern') -eq 'explicit-equal-spacing'
    })
    $legacyDashPatterns = @($sleepers | Where-Object {
        $_.HasAttribute('stroke-dasharray')
    })
    $explicitMarkCount = 0
    foreach ($pattern in $explicitPatterns) {
        $count = 0
        if ([int]::TryParse($pattern.GetAttribute('data-intercity-mark-count'), [ref] $count)) {
            $explicitMarkCount += $count
        }
    }

    return [pscustomobject]@{
        IntercityBases = @($xml.SelectNodes('//*[@class and contains(concat(" ", normalize-space(@class), " "), " intercity-rail-base ")]', $manager)).Count
        IntercitySleepers = $sleepers.Count
        ExplicitPatternLayers = $explicitPatterns.Count
        ExplicitMarkCount = $explicitMarkCount
        LegacyDashPatternLayers = $legacyDashPatterns.Count
        RenderedContinuationArrows = @($xml.SelectNodes('//*[@data-intercity-continuation="outside-city"]', $manager)).Count
        TrainLegendSamples = @($xml.SelectNodes('//*[@data-legend-mode="train"]', $manager)).Count
        ParallelLegendLayouts = @($xml.SelectNodes('//*[@data-legend-layout="parallel-mode-groups"]', $manager)).Count
        MetroLegendGroups = @($xml.SelectNodes('//*[@data-legend-group="metro"]', $manager)).Count
        IntercityLegendGroups = @($xml.SelectNodes('//*[@data-legend-group="intercity"]', $manager)).Count
        TrainSourceRoutes = @($xml.SelectNodes('//*[@data-route-source="intercity-pathPoints"]', $manager)).Count
        Stations = @($xml.SelectNodes('//*[@data-station-id]', $manager)).Count
        ValidXml = $true
    }
}

function New-DesktopOptions {
    param(
        [bool] $ShowIntercity
    )

    $options = [MetroDiagram.Rendering.SvgRenderOptions]::new()
    $options.LayoutMode = [MetroDiagram.Rendering.SvgLayoutMode]::Geographic
    $options.Width = 1800
    $options.Height = 1100
    $options.Padding = 64
    $options.LegendWidth = 250
    $options.LineWidth = 9
    $options.StationRadius = 5
    $options.LabelFontSize = 11
    $options.HideGenericStationLabels = $true
    $options.HideCrowdedLabels = $true
    $options.AlwaysShowInterchanges = $true
    $options.AlwaysShowTerminals = $true
    $options.UsePathPoints = $true
    $options.PathPointSimplificationEnabled = $true
    $options.EnableServiceFamilyMerge = $true
    $options.EnableIntercityRail = $ShowIntercity
    return $options
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Read-IntercityAuditDocument {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourcePath
    )

    $rawJson = Get-Content -LiteralPath $SourcePath -Raw -Encoding UTF8
    $jsonObject = $rawJson | ConvertFrom-Json
    $allLines = @($jsonObject.network.lines)
    $legacyTramLines = @($allLines | Where-Object {
        [string]::Equals([string] $_.mode, 'tram', [System.StringComparison]::OrdinalIgnoreCase)
    })

    if ($legacyTramLines.Count -eq 0) {
        return [pscustomobject]@{
            LoadResult = [MetroDiagram.Core.Loading.MetroJsonLoader]::LoadFromJson($rawJson)
            LegacyTramLinesExcluded = 0
        }
    }

    $keptLines = @($allLines | Where-Object {
        -not [string]::Equals([string] $_.mode, 'tram', [System.StringComparison]::OrdinalIgnoreCase)
    })
    $keptLineIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $referencedStationIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in $keptLines) {
        [void] $keptLineIds.Add([string] $line.id)
        foreach ($stationId in @($line.stops)) {
            if (-not [string]::IsNullOrWhiteSpace([string] $stationId)) {
                [void] $referencedStationIds.Add([string] $stationId)
            }
        }
    }

    $keptStations = @($jsonObject.network.stations | Where-Object {
        $referencedStationIds.Contains([string] $_.id)
    })
    foreach ($station in $keptStations) {
        if ($null -ne $station.lines) {
            $station.lines = @($station.lines | Where-Object {
                $keptLineIds.Contains([string] $_)
            })
        }
    }

    $jsonObject.network.lines = $keptLines
    $jsonObject.network.stations = $keptStations
    $auditJson = $jsonObject | ConvertTo-Json -Depth 100 -Compress
    return [pscustomobject]@{
        LoadResult = [MetroDiagram.Core.Loading.MetroJsonLoader]::LoadFromJson($auditJson)
        LegacyTramLinesExcluded = $legacyTramLines.Count
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestFile = Resolve-RepoPath -Path $ManifestPath
$outputPath = Resolve-RepoPath -Path $OutputDir
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) {
    throw "Intercity fixture manifest was not found: $manifestFile"
}

$manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
$fixtures = @($manifest.fixtures)
if ($fixtures.Count -eq 0) {
    throw "Intercity fixture manifest contains no fixtures: $manifestFile"
}

$missing = @($fixtures | Where-Object { -not (Test-Path -LiteralPath ([string] $_.source) -PathType Leaf) })
if ($missing.Count -gt 0) {
    $paths = @($missing | ForEach-Object { [string] $_.source }) -join [Environment]::NewLine
    throw "Intercity fixture source file(s) were not found:`n$paths"
}

if (-not $NoBuild) {
    & dotnet build (Join-Path $repoRoot 'CS2MetroDiagram.slnx') --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Solution build failed; intercity renderer audit was not generated.'
    }
}

$engineAssembly = Join-Path $repoRoot 'src\MetroDiagram.Engine\bin\Debug\netstandard2.0\MetroDiagram.Engine.dll'
$coreAssembly = Join-Path $repoRoot 'src\MetroDiagram.Core\bin\Debug\net8.0\MetroDiagram.Core.dll'
$renderingAssembly = Join-Path $repoRoot 'src\MetroDiagram.Rendering\bin\Debug\net8.0\MetroDiagram.Rendering.dll'
foreach ($assembly in @($engineAssembly, $coreAssembly, $renderingAssembly)) {
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw "Required renderer audit assembly was not found: $assembly"
    }
}

Add-Type -Path $engineAssembly
Add-Type -Path $coreAssembly
Add-Type -Path $renderingAssembly

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$captureScript = Join-Path $PSScriptRoot 'capture-svg-screenshot.ps1'
$rows = [System.Collections.Generic.List[object]]::new()

foreach ($fixture in $fixtures) {
    $sourcePath = [System.IO.Path]::GetFullPath([string] $fixture.source)
    $auditInput = Read-IntercityAuditDocument -SourcePath $sourcePath
    $loadResult = $auditInput.LoadResult
    if (-not $loadResult.IsValid -or $null -eq $loadResult.Document) {
        $issues = @($loadResult.Issues | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
        throw "Metro export JSON is invalid: $sourcePath`n$issues"
    }

    $document = $loadResult.Document
    $snapshot = [MetroDiagram.Core.Models.MetroNetworkSnapshotAdapter]::FromDocument($document)
    $city = if ([string]::IsNullOrWhiteSpace($snapshot.CityName)) { [string] $fixture.city } else { $snapshot.CityName }
    $cityDir = Join-Path $outputPath (ConvertTo-SafeSlug -Value $city)
    New-Item -ItemType Directory -Force -Path $cityDir | Out-Null

    $desktopDefault = [MetroDiagram.Rendering.MetroSvgRenderer]::new().Render(
        $document,
        (New-DesktopOptions -ShowIntercity $false))
    $desktopIntercity = [MetroDiagram.Rendering.MetroSvgRenderer]::new().Render(
        $document,
        (New-DesktopOptions -ShowIntercity $true))

    $portableOptions = [MetroDiagram.Engine.PortableRenderProfiles]::CreateInGameGeographic($false, $true)
    $portableOptions.ShowIntercityRail = $true
    $portableIntercity = [MetroDiagram.Engine.PortableMetroSvgRenderer]::new().Render(
        $snapshot,
        $portableOptions)

    $desktopDefaultSvg = Join-Path $cityDir 'desktop-geographic-default-off.svg'
    $desktopIntercitySvg = Join-Path $cityDir 'desktop-geographic-intercity.svg'
    $portableIntercitySvg = Join-Path $cityDir 'portable-geographic-intercity.svg'
    Write-Utf8NoBom -Path $desktopDefaultSvg -Content $desktopDefault.Svg
    Write-Utf8NoBom -Path $desktopIntercitySvg -Content $desktopIntercity.Svg
    Write-Utf8NoBom -Path $portableIntercitySvg -Content $portableIntercity.Svg

    $desktopDefaultAudit = Get-SvgAudit -SvgPath $desktopDefaultSvg
    $desktopAudit = Get-SvgAudit -SvgPath $desktopIntercitySvg
    $portableAudit = Get-SvgAudit -SvgPath $portableIntercitySvg
    $trainLines = @($document.Network.Lines | Where-Object { [string] $_.Mode -eq 'train' }).Count

    if ($desktopDefaultAudit.IntercityBases -ne 0 -or $desktopDefaultAudit.TrainSourceRoutes -ne 0) {
        throw "Default-off contract failed for ${city}: train route elements were emitted."
    }
    if ($trainLines -gt 0 -and ($desktopAudit.IntercityBases -eq 0 -or $portableAudit.IntercityBases -eq 0)) {
        throw "Intercity rendering was not emitted for $city in both renderer surfaces."
    }
    if ($desktopAudit.IntercityBases -ne $desktopAudit.IntercitySleepers) {
        throw "Desktop intercity base/sleeper layer count mismatch for $city."
    }
    if ($portableAudit.IntercityBases -ne $portableAudit.IntercitySleepers) {
        throw "Portable intercity base/sleeper layer count mismatch for $city."
    }
    if ($desktopAudit.ExplicitPatternLayers -ne $desktopAudit.IntercitySleepers -or
        $portableAudit.ExplicitPatternLayers -ne $portableAudit.IntercitySleepers) {
        throw "Explicit intercity pattern layer contract failed for $city."
    }
    if ($desktopAudit.LegacyDashPatternLayers -ne 0 -or $portableAudit.LegacyDashPatternLayers -ne 0) {
        throw "Legacy stroke-dasharray intercity pattern detected for $city."
    }
    if ($trainLines -gt 0 -and
        ($desktopAudit.ExplicitMarkCount -eq 0 -or $portableAudit.ExplicitMarkCount -eq 0)) {
        throw "Explicit intercity marks were not emitted for $city."
    }
    if ($trainLines -gt 0 -and
        ($desktopAudit.ParallelLegendLayouts -ne 1 -or
         $desktopAudit.MetroLegendGroups -ne 1 -or
         $desktopAudit.IntercityLegendGroups -ne 1 -or
         $portableAudit.ParallelLegendLayouts -ne 1 -or
         $portableAudit.MetroLegendGroups -ne 1 -or
         $portableAudit.IntercityLegendGroups -ne 1)) {
        throw "Parallel metro/intercity legend group contract failed for $city."
    }

    if (-not $SkipPng) {
        & $captureScript -InputSvg $desktopIntercitySvg `
            -OutputPng (Join-Path $cityDir 'desktop-geographic-intercity.full.png') `
            -Width 1800 -Height 1100
        & $captureScript -InputSvg $portableIntercitySvg `
            -OutputPng (Join-Path $cityDir 'portable-geographic-intercity.full.png') `
            -Width $portableOptions.Width -Height $portableOptions.Height
    }

    $rows.Add([pscustomobject]@{
        City = $city
        SourcePath = $sourcePath
        SourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        LegacyTramLinesExcluded = $auditInput.LegacyTramLinesExcluded
        TrainLines = $trainLines
        DesktopDefaultIntercityBases = $desktopDefaultAudit.IntercityBases
        DesktopIntercityBases = $desktopAudit.IntercityBases
        DesktopSleeperLayers = $desktopAudit.IntercitySleepers
        DesktopExplicitPatternLayers = $desktopAudit.ExplicitPatternLayers
        DesktopExplicitMarks = $desktopAudit.ExplicitMarkCount
        DesktopLegacyDashLayers = $desktopAudit.LegacyDashPatternLayers
        DesktopContinuationArrows = $desktopAudit.RenderedContinuationArrows
        DesktopTrainLegendSamples = $desktopAudit.TrainLegendSamples
        DesktopParallelLegendLayouts = $desktopAudit.ParallelLegendLayouts
        DesktopMetroLegendGroups = $desktopAudit.MetroLegendGroups
        DesktopIntercityLegendGroups = $desktopAudit.IntercityLegendGroups
        PortableIntercityBases = $portableAudit.IntercityBases
        PortableSleeperLayers = $portableAudit.IntercitySleepers
        PortableExplicitPatternLayers = $portableAudit.ExplicitPatternLayers
        PortableExplicitMarks = $portableAudit.ExplicitMarkCount
        PortableLegacyDashLayers = $portableAudit.LegacyDashPatternLayers
        PortableContinuationArrows = $portableAudit.RenderedContinuationArrows
        PortableTrainLegendSamples = $portableAudit.TrainLegendSamples
        PortableParallelLegendLayouts = $portableAudit.ParallelLegendLayouts
        PortableMetroLegendGroups = $portableAudit.MetroLegendGroups
        PortableIntercityLegendGroups = $portableAudit.IntercityLegendGroups
        DesktopWarnings = @($desktopIntercity.Warnings).Count
        PortableElapsedMilliseconds = $portableIntercity.ElapsedMilliseconds
    })
}

$csvPath = Join-Path $outputPath 'renderer-audit.csv'
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$table = @(
    '| City | Train lines | Desktop rail parts | Portable rail parts | Desktop arrows | Portable arrows |',
    '| --- | ---: | ---: | ---: | ---: | ---: |'
)
foreach ($row in $rows) {
    $table += "| $($row.City) | $($row.TrainLines) | $($row.DesktopIntercityBases) | $($row.PortableIntercityBases) | $($row.DesktopContinuationArrows) | $($row.PortableContinuationArrows) |"
}

$notes = @"
# Intercity Geographic Renderer Audit

Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')

## Contract

- Development-only passenger intercity rendering is enabled directly in this audit.
- The Viewer, CLI, and CS2 settings remain unchanged.
- Default desktop rendering emits no intercity route elements.
- Intercity rendering is geographic-only.
- Route geometry and stations beyond the city envelope are clipped.
- A dark railway bed plus explicit equal-length, equal-spaced white marks follows the approved reference direction.
- Desktop and portable rendering share the same arc-length pattern generator.
- Metro and passenger-intercity legend entries use adjacent transport-mode groups instead of one interspersed list.
- The audit rejects legacy route-level stroke-dasharray patterns.
- The audit requires exactly one metro group and one intercity group on both desktop and portable mixed-mode outputs.
- Legacy tram lines in archived experiment fixtures are removed from the in-memory audit copy.

## Real-city results

$($table -join [Environment]::NewLine)

## Files

- renderer-audit.csv contains DOM-level counts and source hashes.
- Each city directory contains desktop and exact portable in-game geographic SVGs.
- PNGs are generated unless -SkipPng is supplied.
- Local player exports are read in place and are not copied here.
- Source JSON files are not modified; legacy tram filtering exists only in the audit process.

## Release gate

Offline renderer evidence is not CS2 approval. Keep intercity export and rendering
hidden/default-off until the owner validates a newly built mod in CS2/Coherent.
"@
Write-Utf8NoBom -Path (Join-Path $outputPath 'notes.md') -Content $notes

Write-Host ''
Write-Host "Intercity geographic renderer audit written to: $outputPath" -ForegroundColor Green
Write-Host "Summary: $csvPath"
$rows | Format-Table City, TrainLines, DesktopIntercityBases, PortableIntercityBases, DesktopContinuationArrows, PortableContinuationArrows
