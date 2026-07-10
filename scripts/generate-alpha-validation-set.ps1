[CmdletBinding()]
param(
    [string[]] $InputJson,
    [string] $InputDir = 'D:\CS2MetroDiagram\exports',
    [switch] $IncludeLatest,
    [string] $LatestJson = 'D:\CS2MetroDiagram\metro-export.json',
    [int] $LatestCount = 5,
    [string] $OutputRoot,
    [switch] $SkipZip,
    [switch] $SkipPng,
    [switch] $WhatIfOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force -DisableNameChecking

function Read-CaseMetadata {
    param([Parameter(Mandatory = $true)][string] $JsonPath)

    try {
        $document = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $cityName = if ([string]::IsNullOrWhiteSpace($document.city.name)) { '' } else { [string] $document.city.name }
        $lines = @($document.network.lines)
        $stations = @($document.network.stations)
        return [pscustomobject]@{
            CityName = $cityName
            LineCount = $lines.Count
            StationCount = $stations.Count
        }
    }
    catch {
        return [pscustomobject]@{
            CityName = ''
            LineCount = 0
            StationCount = 0
        }
    }
}

function Get-CaseName {
    param([Parameter(Mandatory = $true)][string] $JsonPath)

    $metadata = Read-CaseMetadata -JsonPath $JsonPath
    $fileBase = [System.IO.Path]::GetFileNameWithoutExtension($JsonPath)
    $fileBase = $fileBase -replace '^metro-export-', ''

    if (-not [string]::IsNullOrWhiteSpace($metadata.CityName)) {
        $caseName = $metadata.CityName
        if (-not [string]::IsNullOrWhiteSpace($fileBase) -and $fileBase -match '\d{8}-\d{6}$') {
            $caseName = "$caseName-$($Matches[0])"
        }
    }
    else {
        $caseName = $fileBase
    }

    return Convert-ToSafeName $caseName
}

function Add-InputCandidate {
    param(
        [System.Collections.Generic.List[string]] $Candidates,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $fullPath = Get-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Write-Host "WARNING: input JSON not found, skipping: $fullPath" -ForegroundColor Yellow
        return
    }

    if (-not $Candidates.Contains($fullPath)) {
        $Candidates.Add($fullPath)
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$bundleScript = Join-Path $scriptRoot 'generate-alpha-validation-bundle.ps1'
$summaryScript = Join-Path $scriptRoot 'summarize-alpha-validation-bundles.ps1'

if (-not (Test-Path -LiteralPath $bundleScript -PathType Leaf)) {
    throw "Bundle script not found: $bundleScript"
}

if (-not (Test-Path -LiteralPath $summaryScript -PathType Leaf)) {
    throw "Summary script not found: $summaryScript"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\alpha-validation'
}

$outputRootPath = Get-FullPath $OutputRoot
$candidates = [System.Collections.Generic.List[string]]::new()

foreach ($input in @($InputJson)) {
    Add-InputCandidate -Candidates $candidates -Path $input
}

if ($IncludeLatest) {
    Add-InputCandidate -Candidates $candidates -Path $LatestJson
}

if ((Test-Path -LiteralPath $InputDir -PathType Container) -and $LatestCount -ne 0) {
    $snapshots = Get-ChildItem -LiteralPath $InputDir -Filter 'metro-export-*.json' -File |
        Where-Object { $_.Name -notlike 'metro-export-diagnostics-*' } |
        Sort-Object LastWriteTime -Descending

    if ($LatestCount -gt 0) {
        $snapshots = $snapshots | Select-Object -First $LatestCount
    }

    foreach ($snapshot in @($snapshots)) {
        Add-InputCandidate -Candidates $candidates -Path $snapshot.FullName
    }
}
elseif (-not (Test-Path -LiteralPath $InputDir -PathType Container) -and $InputJson.Count -eq 0 -and -not $IncludeLatest) {
    Write-Host "WARNING: input directory not found: $(Get-FullPath $InputDir)" -ForegroundColor Yellow
}

if ($candidates.Count -eq 0) {
    Write-Host 'ERROR: no alpha validation inputs were found.' -ForegroundColor Red
    Write-Host 'Pass -InputJson <path>, use -IncludeLatest, or export snapshots into D:\CS2MetroDiagram\exports.' -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

$runTimestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$batchCsv = Join-Path $outputRootPath "batch-$runTimestamp.csv"
$results = [System.Collections.Generic.List[object]]::new()

Write-Host "Alpha validation set"
Write-Host "Output root: $outputRootPath"
Write-Host "Input count: $($candidates.Count)"
Write-Host ""

foreach ($candidate in $candidates) {
    $caseName = Get-CaseName -JsonPath $candidate
    $metadata = Read-CaseMetadata -JsonPath $candidate

    Write-Host "Case: $caseName"
    Write-Host "  JSON: $candidate"
    Write-Host "  City: $($metadata.CityName)"
    Write-Host "  Lines: $($metadata.LineCount), Stations: $($metadata.StationCount)"

    if ($WhatIfOnly) {
        $results.Add([pscustomobject]@{
            inputJson = $candidate
            caseName = $caseName
            cityName = $metadata.CityName
            lineCount = $metadata.LineCount
            stationCount = $metadata.StationCount
            status = 'what-if'
            bundlePath = ''
            error = ''
        })
        continue
    }

    $before = Get-Date
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $bundleScript,
        '-InputJson',
        $candidate,
        '-CaseName',
        $caseName,
        '-OutputRoot',
        $outputRootPath
    )

    if ($SkipZip) {
        $arguments += '-SkipZip'
    }

    if ($SkipPng) {
        $arguments += '-SkipPng'
    }

    try {
        & pwsh @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "generate-alpha-validation-bundle.ps1 exited with code $LASTEXITCODE"
        }

        $bundle = Get-ChildItem -LiteralPath $outputRootPath -Directory |
            Where-Object { $_.Name -like "*-$caseName" -and $_.LastWriteTime -ge $before.AddSeconds(-2) } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        $results.Add([pscustomobject]@{
            inputJson = $candidate
            caseName = $caseName
            cityName = $metadata.CityName
            lineCount = $metadata.LineCount
            stationCount = $metadata.StationCount
            status = 'generated'
            bundlePath = if ($null -eq $bundle) { '' } else { $bundle.FullName }
            error = ''
        })
    }
    catch {
        $results.Add([pscustomobject]@{
            inputJson = $candidate
            caseName = $caseName
            cityName = $metadata.CityName
            lineCount = $metadata.LineCount
            stationCount = $metadata.StationCount
            status = 'failed'
            bundlePath = ''
            error = $_.Exception.Message
        })
        Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
    }

    Write-Host ""
}

$results | Export-Csv -LiteralPath $batchCsv -NoTypeInformation -Encoding UTF8

if (-not $WhatIfOnly) {
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $summaryScript -InputRoot $outputRootPath
    if ($LASTEXITCODE -ne 0) {
        throw "summarize-alpha-validation-bundles.ps1 exited with code $LASTEXITCODE"
    }
}

Write-Host "Batch CSV written to: $batchCsv"
Write-Host "Alpha validation index: $(Join-Path $outputRootPath 'index.md')"
