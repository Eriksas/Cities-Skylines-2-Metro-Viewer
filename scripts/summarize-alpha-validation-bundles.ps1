[CmdletBinding()]
param(
    [string] $InputRoot = 'artifacts\alpha-validation',
    [string] $OutputMarkdown,
    [string] $OutputCsv,
    [int] $Latest = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Convert-ToDisplayValue {
    param($Value, [string] $Fallback = '')

    if ($null -eq $Value) {
        return $Fallback
    }

    $text = [string] $Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $Fallback
    }

    return $text
}

function Test-BundleFile {
    param(
        [Parameter(Mandatory = $true)][string] $BundlePath,
        [string] $RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return $false
    }

    return Test-Path -LiteralPath (Join-Path $BundlePath $RelativePath) -PathType Leaf
}

function Get-BundleSummary {
    param([Parameter(Mandatory = $true)] [System.IO.DirectoryInfo] $Directory)

    $manifestPath = Join-Path $Directory.FullName 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return [pscustomobject]@{
            BundleName = $Directory.Name
            BundlePath = $Directory.FullName
            GeneratedAt = ''
            CityName = ''
            LineCount = ''
            StationCount = ''
            GeneratorVersion = ''
            ToolVersion = ''
            SourceJson = ''
            ValidationWarningCount = ''
            ValidationWarnings = 'missing manifest.json'
            HasGeographicPng = Test-BundleFile -BundlePath $Directory.FullName -RelativePath 'baseline-geographic.full.png'
            HasSchematicMapPng = Test-BundleFile -BundlePath $Directory.FullName -RelativePath 'schematic-map.full.png'
            HasSchematicV2Png = Test-BundleFile -BundlePath $Directory.FullName -RelativePath 'schematic-v2.full.png'
            HasNotes = Test-BundleFile -BundlePath $Directory.FullName -RelativePath 'notes.md'
            HasFeedbackTemplate = Test-BundleFile -BundlePath $Directory.FullName -RelativePath 'feedback-template-filled.md'
            HasTopologySummary = Test-BundleFile -BundlePath $Directory.FullName -RelativePath 'schematic-v2-diagnostics\topology-summary.txt'
            ManifestPath = ''
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $warnings = @($manifest.validationWarnings)
    $files = $manifest.files

    return [pscustomobject]@{
        BundleName = Convert-ToDisplayValue $manifest.bundleName $Directory.Name
        BundlePath = $Directory.FullName
        GeneratedAt = Convert-ToDisplayValue $manifest.generatedAt
        CityName = Convert-ToDisplayValue $manifest.export.cityName
        LineCount = $manifest.export.lineCount
        StationCount = $manifest.export.stationCount
        GeneratorVersion = Convert-ToDisplayValue $manifest.export.generatorVersion
        ToolVersion = Convert-ToDisplayValue $manifest.tool.version
        SourceJson = Convert-ToDisplayValue $manifest.source.inputJson
        ValidationWarningCount = $warnings.Count
        ValidationWarnings = if ($warnings.Count -eq 0) { '' } else { $warnings -join ' | ' }
        HasGeographicPng = Test-BundleFile -BundlePath $Directory.FullName -RelativePath $files.geographicPng
        HasSchematicMapPng = Test-BundleFile -BundlePath $Directory.FullName -RelativePath $files.schematicMapPng
        HasSchematicV2Png = Test-BundleFile -BundlePath $Directory.FullName -RelativePath $files.schematicV2Png
        HasNotes = Test-BundleFile -BundlePath $Directory.FullName -RelativePath $files.notes
        HasFeedbackTemplate = Test-BundleFile -BundlePath $Directory.FullName -RelativePath $files.feedbackTemplate
        HasTopologySummary = Test-BundleFile -BundlePath $Directory.FullName -RelativePath (Join-Path $files.schematicV2Diagnostics 'topology-summary.txt')
        ManifestPath = $manifestPath
    }
}

function Convert-ToMarkdownBool {
    param([bool] $Value)
    if ($Value) {
        return 'yes'
    }

    return 'no'
}

$inputRootPath = Get-FullPath $InputRoot
if (-not (Test-Path -LiteralPath $inputRootPath -PathType Container)) {
    throw "Alpha validation root does not exist: $inputRootPath"
}

if ([string]::IsNullOrWhiteSpace($OutputMarkdown)) {
    $OutputMarkdown = Join-Path $inputRootPath 'index.md'
}

if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
    $OutputCsv = Join-Path $inputRootPath 'index.csv'
}

$directories = Get-ChildItem -LiteralPath $inputRootPath -Directory |
    Where-Object { $_.Name -notlike '*.tmp' } |
    Sort-Object LastWriteTime -Descending

if ($Latest -gt 0) {
    $directories = $directories | Select-Object -First $Latest
}

$summaries = @($directories | ForEach-Object { Get-BundleSummary -Directory $_ })

$outputMarkdownPath = Get-FullPath $OutputMarkdown
$outputCsvPath = Get-FullPath $OutputCsv
New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($outputMarkdownPath)) | Out-Null
New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($outputCsvPath)) | Out-Null

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add('# Alpha Validation Bundle Index')
$markdown.Add('')
$markdown.Add("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$markdown.Add("- Input root: $inputRootPath")
$markdown.Add("- Bundle count: $($summaries.Count)")
$markdown.Add('')
$markdown.Add('| Bundle | City | Lines | Stations | Generated | Geo PNG | Schematic-map PNG | Schematic-v2 PNG | Warnings |')
$markdown.Add('| --- | --- | ---: | ---: | --- | --- | --- | --- | --- |')
foreach ($summary in $summaries) {
    $warningText = if ([string]::IsNullOrWhiteSpace([string] $summary.ValidationWarnings)) { '' } else { [string] $summary.ValidationWarnings }
    $warningText = $warningText.Replace('|', '\|')
    $markdown.Add("| $($summary.BundleName) | $($summary.CityName) | $($summary.LineCount) | $($summary.StationCount) | $($summary.GeneratedAt) | $(Convert-ToMarkdownBool $summary.HasGeographicPng) | $(Convert-ToMarkdownBool $summary.HasSchematicMapPng) | $(Convert-ToMarkdownBool $summary.HasSchematicV2Png) | $warningText |")
}

$markdown.Add('')
$markdown.Add('## Review Notes')
$markdown.Add('')
$markdown.Add('- Open `baseline-geographic.full.png` first for alpha baseline acceptance.')
$markdown.Add('- Open `schematic-map.full.png` next for product-facing schematic review.')
$markdown.Add('- Attach `manifest.json`, `notes.md`, and `feedback-template-filled.md` when reporting a city-specific issue.')
$markdown.Add('- Bundles marked `missing manifest.json` are older outputs and should be regenerated before formal alpha review.')

$markdown | Set-Content -LiteralPath $outputMarkdownPath -Encoding UTF8
$summaries | Export-Csv -LiteralPath $outputCsvPath -NoTypeInformation -Encoding UTF8

Write-Host "Alpha validation index written to: $outputMarkdownPath"
Write-Host "Alpha validation CSV written to: $outputCsvPath"
