[CmdletBinding()]
param(
    [string] $OutputDir = 'artifacts\intercity-geographic-v2\baseline',
    [string] $RealFixtureManifest = 'samples\intercity-geographic\real-fixture-manifest.json'
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

function Get-SvgDomCounts {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SvgPath
    )

    $xml = [xml](Get-Content -LiteralPath $SvgPath -Raw -Encoding UTF8)
    return [pscustomobject]@{
        Paths = @($xml.SelectNodes("//*[local-name()='path']")).Count
        Polylines = @($xml.SelectNodes("//*[local-name()='polyline']")).Count
        Circles = @($xml.SelectNodes("//*[local-name()='circle']")).Count
        TextNodes = @($xml.SelectNodes("//*[local-name()='text']")).Count
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedOutputDir = Resolve-RepoPath -Path $OutputDir
$resolvedFixtureManifest = Resolve-RepoPath -Path $RealFixtureManifest
$svgOutputDir = Join-Path $resolvedOutputDir 'metro-only-svg'
$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'

if (-not (Test-Path -LiteralPath $resolvedFixtureManifest -PathType Leaf)) {
    throw "Real fixture manifest was not found: $resolvedFixtureManifest"
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $svgOutputDir | Out-Null

$sampleFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'samples') -Filter 'sample-metro-*.json' -File |
    Sort-Object Name

if ($sampleFiles.Count -eq 0) {
    throw 'No metro sample JSON files were found.'
}

$metroRows = foreach ($sample in $sampleFiles) {
    $outputName = '{0}.geographic.svg' -f [System.IO.Path]::GetFileNameWithoutExtension($sample.Name)
    $outputPath = Join-Path $svgOutputDir $outputName
    $arguments = @(
        'run',
        '--project',
        $cliProject,
        '--no-restore',
        '--',
        $sample.FullName,
        $outputPath,
        '--layout',
        'geographic',
        '--size',
        'poster',
        '--use-path-points',
        '--simplify-path-points'
    )

    $renderOutput = & dotnet @arguments 2>&1
    foreach ($outputLine in $renderOutput) {
        Write-Host $outputLine
    }

    if ($LASTEXITCODE -ne 0) {
        throw "CLI baseline render failed for $($sample.FullName) with exit code $LASTEXITCODE."
    }

    $dom = Get-SvgDomCounts -SvgPath $outputPath
    [pscustomobject]@{
        Sample = $sample.Name
        InputSha256 = (Get-FileHash -LiteralPath $sample.FullName -Algorithm SHA256).Hash
        SvgSha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
        Paths = $dom.Paths
        Polylines = $dom.Polylines
        Circles = $dom.Circles
        TextNodes = $dom.TextNodes
    }
}

$metroManifestPath = Join-Path $resolvedOutputDir 'metro-only-baseline.csv'
$metroRows | Export-Csv -LiteralPath $metroManifestPath -NoTypeInformation -Encoding UTF8

$realManifest = Get-Content -LiteralPath $resolvedFixtureManifest -Raw -Encoding UTF8 | ConvertFrom-Json
$realRows = foreach ($fixture in $realManifest.fixtures) {
    $sourceExists = Test-Path -LiteralPath $fixture.source -PathType Leaf
    [pscustomobject]@{
        City = $fixture.city
        Source = $fixture.source
        Exists = $sourceExists
        Sha256 = if ($sourceExists) {
            (Get-FileHash -LiteralPath $fixture.source -Algorithm SHA256).Hash
        }
        else {
            ''
        }
        Lines = $fixture.lines
        Stations = $fixture.stations
        MetroLines = $fixture.modes.metro
        TrainLines = $fixture.modes.train
        TramLines = $fixture.modes.tram
    }
}

$realManifestPath = Join-Path $resolvedOutputDir 'real-fixture-hashes.csv'
$realRows | Export-Csv -LiteralPath $realManifestPath -NoTypeInformation -Encoding UTF8

$notesPath = Join-Path $resolvedOutputDir 'notes.md'
$notes = @"
# Intercity Geographic v2 Baseline

Generated at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')

## Metro-only regression contract

- Layout: geographic
- Size: poster
- UsePathPoints: true
- Path simplification: enabled
- Intercity rendering: not connected
- Tram rendering: not connected
- Exporter and JSON schema: unchanged

metro-only-baseline.csv stores the input hash, rendered SVG hash, and compact
DOM counts for every repository `sample-metro-*.json` fixture. Re-running this
script after a future intercity change provides a byte-level and structure-level
check that the established metro-only output did not drift.

## Real fixture inventory

real-fixture-hashes.csv records local source paths and hashes for the available
奥斯特, 肇庆, and 谢菲尔德 exports. Player exports remain local and are not copied
into the repository.
"@
[System.IO.File]::WriteAllText($notesPath, $notes, [System.Text.UTF8Encoding]::new($false))

Write-Host "Metro-only baseline manifest: $metroManifestPath"
Write-Host "Real fixture hash manifest: $realManifestPath"
Write-Host "Baseline notes: $notesPath"
Write-Host "Rendered metro-only SVG directory: $svgOutputDir"
