[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot "samples\intercity-geographic\real-fixture-manifest.json"
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\intercity-geographic-v2\classification-audit"
}

$ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Intercity fixture manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $manifest.fixtures -or @($manifest.fixtures).Count -eq 0) {
    throw "Intercity fixture manifest contains no fixtures: $ManifestPath"
}

$missingSources = @(
    $manifest.fixtures |
        Where-Object { -not (Test-Path -LiteralPath ([string]$_.source) -PathType Leaf) } |
        ForEach-Object { [string]$_.source }
)
if ($missingSources.Count -gt 0) {
    throw "Real fixture source file(s) not found:`n$($missingSources -join [Environment]::NewLine)"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$lineRows = [System.Collections.Generic.List[object]]::new()
$cityRows = [System.Collections.Generic.List[object]]::new()
$cargoNamePattern = "(?i)(cargo|freight|货运|货物|货场|物流)"
$outsideNamePattern = "(?i)(outside|external|出口|外部连接|边界)"

function Test-OptionalBooleanProperty {
    param(
        [AllowNull()]
        [object]$InputObject,
        [Parameter(Mandatory)]
        [string]$PropertyName
    )

    if ($null -eq $InputObject) {
        return $false
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    return $null -ne $property -and [bool]$property.Value
}

foreach ($fixture in $manifest.fixtures) {
    $sourcePath = [System.IO.Path]::GetFullPath([string]$fixture.source)
    $document = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $lines = @($document.network.lines)
    $stations = @($document.network.stations)
    $stationById = @{}
    foreach ($station in $stations) {
        $stationById[[string]$station.id] = $station
    }

    $trainLines = @($lines | Where-Object { [string]$_.mode -eq "train" })
    $cityPathPointCount = 0
    $cityCargoNameWarnings = 0
    $cityOutsideTerminals = 0

    foreach ($line in $trainLines) {
        $stops = @($line.stops)
        $pathPoints = @($line.pathPoints)
        $cityPathPointCount += $pathPoints.Count

        $sourceCounts = @(
            $pathPoints |
                Group-Object -Property {
                    if ([string]::IsNullOrWhiteSpace([string]$_.source)) {
                        "(missing)"
                    }
                    else {
                        [string]$_.source
                    }
                } |
                Sort-Object -Property Name |
                ForEach-Object { "$($_.Name)=$($_.Count)" }
        )

        $firstStation = if ($stops.Count -gt 0 -and $stationById.ContainsKey([string]$stops[0])) {
            $stationById[[string]$stops[0]]
        }
        else {
            $null
        }
        $lastStation = if ($stops.Count -gt 0 -and $stationById.ContainsKey([string]$stops[-1])) {
            $stationById[[string]$stops[-1]]
        }
        else {
            $null
        }

        $firstName = if ($null -ne $firstStation) { [string]$firstStation.name } else { "(missing)" }
        $lastName = if ($null -ne $lastStation) { [string]$lastStation.name } else { "(missing)" }
        $firstOutside = Test-OptionalBooleanProperty -InputObject $firstStation -PropertyName "isOutsideConnection"
        $lastOutside = Test-OptionalBooleanProperty -InputObject $lastStation -PropertyName "isOutsideConnection"
        $outsideTerminalCount = @($firstOutside, $lastOutside | Where-Object { $_ }).Count
        $cityOutsideTerminals += $outsideTerminalCount

        $cargoNameWarning = ([string]$line.name -match $cargoNamePattern) -or
            ($firstName -match $cargoNamePattern) -or
            ($lastName -match $cargoNamePattern)
        if ($cargoNameWarning) {
            $cityCargoNameWarnings++
        }

        $outsideNameHint = ($firstName -match $outsideNamePattern) -or ($lastName -match $outsideNamePattern)
        $lineRows.Add([pscustomobject]@{
            City = [string]$document.city.name
            SourcePath = $sourcePath
            SchemaVersion = [string]$document.schemaVersion
            GeneratorVersion = [string]$document.generator.version
            LineId = [string]$line.id
            LineName = [string]$line.name
            StopCount = $stops.Count
            PathPointCount = $pathPoints.Count
            PathPointsGreaterThanStops = $pathPoints.Count -gt $stops.Count
            PathSourceSummary = $sourceCounts -join "; "
            FirstStation = $firstName
            LastStation = $lastName
            OutsideTerminalCount = $outsideTerminalCount
            OutsideNameHint = $outsideNameHint
            CargoNameHeuristicWarning = $cargoNameWarning
        })
    }

    $cityRows.Add([pscustomobject]@{
        City = [string]$document.city.name
        SourcePath = $sourcePath
        SchemaVersion = [string]$document.schemaVersion
        GeneratorVersion = [string]$document.generator.version
        TotalLines = $lines.Count
        TotalStations = $stations.Count
        MetroLines = @($lines | Where-Object { [string]$_.mode -eq "metro" }).Count
        PassengerTrainLinesInArchivedExport = $trainLines.Count
        TramLines = @($lines | Where-Object { [string]$_.mode -eq "tram" }).Count
        TrainPathPoints = $cityPathPointCount
        TrainOutsideTerminalReferences = $cityOutsideTerminals
        CargoNameHeuristicWarnings = $cityCargoNameWarnings
    })
}

$lineCsvPath = Join-Path $OutputDir "passenger-train-lines.csv"
$cityCsvPath = Join-Path $OutputDir "city-summary.csv"
$notesPath = Join-Path $OutputDir "notes.md"

$lineRows | Export-Csv -LiteralPath $lineCsvPath -NoTypeInformation -Encoding UTF8
$cityRows | Export-Csv -LiteralPath $cityCsvPath -NoTypeInformation -Encoding UTF8

$totalTrainLines = ($cityRows | Measure-Object -Property PassengerTrainLinesInArchivedExport -Sum).Sum
$totalPathPoints = ($cityRows | Measure-Object -Property TrainPathPoints -Sum).Sum
$totalCargoWarnings = ($cityRows | Measure-Object -Property CargoNameHeuristicWarnings -Sum).Sum
$allDenseEnough = @($lineRows | Where-Object { -not $_.PathPointsGreaterThanStops }).Count -eq 0

$cityTable = @(
    "| City | Lines | Stations | Archived train lines | Train path points | Cargo-name warnings |",
    "| --- | ---: | ---: | ---: | ---: | ---: |"
)
foreach ($row in $cityRows) {
    $cityTable += "| $($row.City) | $($row.TotalLines) | $($row.TotalStations) | $($row.PassengerTrainLinesInArchivedExport) | $($row.TrainPathPoints) | $($row.CargoNameHeuristicWarnings) |"
}

$notes = @'
# Intercity Real-fixture Classification Audit

Generated: {{GENERATED_AT}}

## Scope

This audit reads the local archived exports listed in:

```text
{{MANIFEST_PATH}}
```

It does not copy player exports into the repository and does not modify any
export. These files predate the new isolated ECS collector, so they prove that
the available real fixtures contain usable `mode=train` route/station/path data.
They do not prove that the new collector excludes every cargo line in game.

## Summary

{{CITY_TABLE}}

- Archived passenger-train candidates: {{TOTAL_TRAIN_LINES}}
- Train path points: {{TOTAL_PATH_POINTS}}
- Every archived train line has more path points than stops: {{ALL_DENSE_ENOUGH}}
- Cargo-name heuristic warnings: {{TOTAL_CARGO_WARNINGS}}

## Cargo boundary

`CargoNameHeuristicWarning` only searches line and terminal names for obvious
words such as `cargo`, `freight`, `货运`, and `货场`. A clean result is not
authoritative. The new `PassengerIntercityRailCollector` performs the actual
ECS exclusion by checking cargo vehicle/station prefab components, and that
behavior still requires owner validation in CS2.

## Outputs

- `city-summary.csv` - per-city mode and geometry totals.
- `passenger-train-lines.csv` - per-line stop/path/source/terminal evidence.

## Release gate

Intercity export remains hidden and default-off. Do not expose or publish it
until a newly built mod has been validated in CS2 with passenger and cargo rail
lines.
'@

$notes = $notes.Replace("{{GENERATED_AT}}", (Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"))
$notes = $notes.Replace("{{MANIFEST_PATH}}", $ManifestPath)
$notes = $notes.Replace("{{CITY_TABLE}}", ($cityTable -join [Environment]::NewLine))
$notes = $notes.Replace("{{TOTAL_TRAIN_LINES}}", [string]$totalTrainLines)
$notes = $notes.Replace("{{TOTAL_PATH_POINTS}}", [string]$totalPathPoints)
$notes = $notes.Replace("{{ALL_DENSE_ENOUGH}}", [string]$allDenseEnough)
$notes = $notes.Replace("{{TOTAL_CARGO_WARNINGS}}", [string]$totalCargoWarnings)

Set-Content -LiteralPath $notesPath -Value $notes -Encoding UTF8

Write-Host "Intercity real-fixture audit complete."
Write-Host "City summary: $cityCsvPath"
Write-Host "Line summary: $lineCsvPath"
Write-Host "Notes: $notesPath"
