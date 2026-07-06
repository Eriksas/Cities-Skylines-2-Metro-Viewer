<#
.SYNOPSIS
Renders the sample corpus (plus optional real exports) in schematic-map and
schematic-anneal, collects the shared layout scores, and writes a comparison
summary. This is the acceptance evidence for layout work: a change is only
"better" when it improves corpus medians without making the worst case worse.

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-schematic-layouts.ps1

.EXAMPLE
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\compare-schematic-layouts.ps1 `
  -ExtraInputJson D:\CS2MetroDiagram\metro-export.json
#>
param(
    [string[]] $ExtraInputJson = @(),
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot ('artifacts\layout-comparison\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force $OutputRoot | Out-Null

$cases = @(Get-ChildItem (Join-Path $repoRoot 'samples') -Filter '*.json' -File) +
         @(Get-ChildItem (Join-Path $repoRoot 'samples\regression') -Filter '*.json' -File)
foreach ($extra in $ExtraInputJson) {
    $cases += Get-Item $extra
}

$modes = @('schematic-map', 'schematic-anneal')
$rows = @()
foreach ($case in $cases) {
    foreach ($mode in $modes) {
        $svgPath = Join-Path $OutputRoot ("{0}--{1}.svg" -f $case.BaseName, $mode)
        $csvPath = Join-Path $OutputRoot ("{0}--{1}.score.csv" -f $case.BaseName, $mode)
        dotnet run --project $cliProject --no-restore -- $case.FullName $svgPath `
            --layout $mode --hide-generic-labels --hide-crowded-labels `
            --emit-layout-score $csvPath *> $null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $csvPath)) {
            Write-Host "SKIP $($case.BaseName) [$mode]: render or score failed" -ForegroundColor Yellow
            continue
        }

        $score = Import-Csv $csvPath | Select-Object -First 1
        $rows += [pscustomobject]@{
            case = $case.BaseName
            mode = $mode
            stations = [int]$score.stations
            octilinearEdgeRatio = [double]$score.octilinearEdgeRatio
            bendCount = [int]$score.bendCount
            crossings = [int]$score.crossings
            minSpacingViolations = [int]$score.minSpacingViolations
            clearanceViolations = [int]$score.clearanceViolations
            weightedCost = [double]$score.weightedCost
        }
    }
}

$rows | Export-Csv (Join-Path $OutputRoot 'scores.csv') -NoTypeInformation

function Get-Median([double[]] $values) {
    if ($values.Count -eq 0) { return 0 }
    $sorted = $values | Sort-Object
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$metrics = @(
    @{ Name = 'octilinearEdgeRatio'; HigherIsBetter = $true },
    @{ Name = 'bendCount'; HigherIsBetter = $false },
    @{ Name = 'crossings'; HigherIsBetter = $false },
    @{ Name = 'minSpacingViolations'; HigherIsBetter = $false },
    @{ Name = 'clearanceViolations'; HigherIsBetter = $false },
    @{ Name = 'weightedCost'; HigherIsBetter = $false }
)

$lines = New-Object System.Collections.ArrayList
[void]$lines.Add('# Schematic Layout Comparison')
[void]$lines.Add('')
[void]$lines.Add("Cases: $(@($cases).Count); generated $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss').")
[void]$lines.Add('')
[void]$lines.Add('A layout change is acceptable when it improves corpus medians without')
[void]$lines.Add('worsening the worst case. Single-map screenshots are not acceptance evidence.')
[void]$lines.Add('')
[void]$lines.Add('| metric | schematic-map median | schematic-anneal median | schematic-map worst | schematic-anneal worst | median winner |')
[void]$lines.Add('| --- | --- | --- | --- | --- | --- |')

foreach ($metric in $metrics) {
    $byMode = @{}
    foreach ($mode in $modes) {
        $values = @($rows | Where-Object mode -eq $mode | ForEach-Object { [double]$_.($metric.Name) })
        $median = Get-Median $values
        $worst = if ($values.Count -eq 0) { 0 }
                 elseif ($metric.HigherIsBetter) { ($values | Measure-Object -Minimum).Minimum }
                 else { ($values | Measure-Object -Maximum).Maximum }
        $byMode[$mode] = @{ Median = $median; Worst = $worst }
    }

    $mapMedian = $byMode['schematic-map'].Median
    $annealMedian = $byMode['schematic-anneal'].Median
    $winner = if ($mapMedian -eq $annealMedian) { 'tie' }
              elseif (($metric.HigherIsBetter -and $annealMedian -gt $mapMedian) -or (-not $metric.HigherIsBetter -and $annealMedian -lt $mapMedian)) { 'schematic-anneal' }
              else { 'schematic-map' }
    [void]$lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f `
        $metric.Name, `
        [math]::Round($mapMedian, 3), [math]::Round($annealMedian, 3), `
        [math]::Round($byMode['schematic-map'].Worst, 3), [math]::Round($byMode['schematic-anneal'].Worst, 3), `
        $winner))
}

[void]$lines.Add('')
[void]$lines.Add('## Per-case scores')
[void]$lines.Add('')
[void]$lines.Add('| case | mode | stations | octilinear | bends | crossings | spacing- | clearance- | cost |')
[void]$lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($row in $rows | Sort-Object case, mode) {
    [void]$lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
        $row.case, $row.mode, $row.stations, $row.octilinearEdgeRatio, $row.bendCount, `
        $row.crossings, $row.minSpacingViolations, $row.clearanceViolations, $row.weightedCost))
}

$lines | Set-Content (Join-Path $OutputRoot 'index.md') -Encoding UTF8
Write-Host "Comparison written to $OutputRoot"
Get-Content (Join-Path $OutputRoot 'index.md') | Select-Object -First 16 | Write-Host
