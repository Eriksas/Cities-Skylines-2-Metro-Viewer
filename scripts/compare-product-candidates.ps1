[CmdletBinding()]
param(
    [string[]] $CandidateDirs,

    [string] $OutputDir,

    [int] $LatestCount = 4,

    [switch] $SkipScreenshot,

    [int] $ScreenshotWidth = 2400,

    [int] $ScreenshotHeight = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Convert-ToFileUri {
    param([Parameter(Mandatory = $true)][string] $Path)
    $fullPath = Get-FullPath $Path
    return (New-Object System.Uri($fullPath)).AbsoluteUri
}

function Convert-ToHtmlText {
    param([string] $Text)
    if ($null -eq $Text) {
        return ''
    }

    return [System.Net.WebUtility]::HtmlEncode($Text)
}

function Convert-ToSafeName {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 'product-candidate-comparison'
    }

    $safe = $Value.Trim()
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string] $invalid, '-')
    }

    $safe = [regex]::Replace($safe, '\s+', '-')
    $safe = [regex]::Replace($safe, '-{2,}', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return 'product-candidate-comparison'
    }

    return $safe
}

function Get-ScoreValue {
    param(
        [object[]] $Rows,
        [string] $Category,
        [string] $Property = 'Score',
        [string] $Fallback = ''
    )

    $row = $Rows | Where-Object { $_.Category -eq $Category } | Select-Object -First 1
    if ($null -eq $row) {
        return $Fallback
    }

    return [string] $row.$Property
}

function Get-AuditValue {
    param(
        [string[]] $Lines,
        [string] $Label,
        [string] $Fallback = ''
    )

    $pattern = "^- $([regex]::Escape($Label)):\s*(.+)$"
    foreach ($line in $Lines) {
        if ($line -match $pattern) {
            return $Matches[1].Trim()
        }
    }

    return $Fallback
}

function Read-Candidate {
    param([Parameter(Mandatory = $true)][string] $Directory)

    $dirPath = Get-FullPath $Directory
    if (-not (Test-Path -LiteralPath $dirPath -PathType Container)) {
        throw "Candidate directory not found: $dirPath"
    }

    $pngPath = Join-Path $dirPath 'product-candidate.full.png'
    $svgPath = Join-Path $dirPath 'product-candidate.svg'
    $scorePath = Join-Path $dirPath 'schematic-map-score.csv'
    $auditPath = Join-Path $dirPath 'schematic-map-audit.txt'
    $notesPath = Join-Path $dirPath 'notes.md'

    if (-not (Test-Path -LiteralPath $pngPath -PathType Leaf)) {
        throw "Candidate PNG not found: $pngPath"
    }

    $scoreRows = @()
    if (Test-Path -LiteralPath $scorePath -PathType Leaf) {
        $scoreRows = @(Import-Csv -LiteralPath $scorePath)
    }

    $auditLines = @()
    if (Test-Path -LiteralPath $auditPath -PathType Leaf) {
        $auditLines = @(Get-Content -LiteralPath $auditPath -Encoding UTF8)
    }

    $notesLines = @()
    if (Test-Path -LiteralPath $notesPath -PathType Leaf) {
        $notesLines = @(Get-Content -LiteralPath $notesPath -Encoding UTF8)
    }

    $cityName = ''
    foreach ($line in $notesLines) {
        if ($line -match '^- City name:\s*(.+)$') {
            $cityName = $Matches[1].Trim()
            break
        }
    }

    return [pscustomobject]@{
        Name = Split-Path -Leaf $dirPath
        Path = $dirPath
        PngPath = $pngPath
        SvgPath = $svgPath
        ScorePath = $scorePath
        AuditPath = $auditPath
        CityName = $cityName
        OverallScore = Get-ScoreValue $scoreRows 'overall'
        OverallPenalty = Get-ScoreValue $scoreRows 'overall' 'Penalty'
        OctilinearScore = Get-ScoreValue $scoreRows 'octilinear-grammar'
        CrossingScore = Get-ScoreValue $scoreRows 'route-crossings'
        BadgeScore = Get-ScoreValue $scoreRows 'badge-layout'
        WidthScore = Get-ScoreValue $scoreRows 'stroke-width-consistency'
        RouteWarnings = Get-AuditValue $auditLines 'Route warnings'
        NonOctilinearSegments = Get-AuditValue $auditLines 'Non-octilinear segments'
        InteriorCrossings = Get-AuditValue $auditLines 'Interior route crossings'
        SharpTurns = Get-AuditValue $auditLines 'Sharp turn candidates'
        BadgeBadgeConflicts = Get-AuditValue $auditLines 'Badge-badge conflicts'
        BadgeLabelConflicts = Get-AuditValue $auditLines 'Badge-label conflicts'
        LayoutScoreText = Get-AuditValue $auditLines 'Layout score'
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $OutputDir = Join-Path $repoRoot "artifacts\product-candidate-comparison\$timestamp"
}

$outputPath = Get-FullPath $OutputDir
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

if ($null -eq $CandidateDirs -or $CandidateDirs.Count -eq 0) {
    $candidateRoot = Join-Path $repoRoot 'artifacts\product-candidate'
    if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) {
        throw "Product candidate root not found: $candidateRoot"
    }

    $CandidateDirs = @(Get-ChildItem -LiteralPath $candidateRoot -Directory |
        Where-Object {
            $_.Name -notlike '*.tmp' -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'product-candidate.full.png') -PathType Leaf)
        } |
        Sort-Object Name -Descending |
        Select-Object -First $LatestCount |
        ForEach-Object { $_.FullName })
}

if ($CandidateDirs.Count -eq 0) {
    throw 'No product candidate directories were found.'
}

$candidates = @($CandidateDirs | ForEach-Object { Read-Candidate $_ })

$csvPath = Join-Path $outputPath 'comparison.csv'
$mdPath = Join-Path $outputPath 'comparison.md'
$htmlPath = Join-Path $outputPath 'comparison.html'
$pngPath = Join-Path $outputPath 'comparison.full.png'

$candidates | Select-Object `
    Name,
    CityName,
    OverallScore,
    OctilinearScore,
    CrossingScore,
    BadgeScore,
    WidthScore,
    RouteWarnings,
    NonOctilinearSegments,
    InteriorCrossings,
    SharpTurns,
    BadgeBadgeConflicts,
    BadgeLabelConflicts,
    Path,
    PngPath,
    SvgPath |
    Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add('# Product Candidate Comparison') | Out-Null
$markdown.Add('') | Out-Null
$markdown.Add("- Generated: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))") | Out-Null
$markdown.Add("- Candidate count: $($candidates.Count)") | Out-Null
$markdown.Add('') | Out-Null
$markdown.Add('| Candidate | Overall | Octilinear | Crossings | Badges | Width | Warnings | Image |') | Out-Null
$markdown.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |') | Out-Null
foreach ($candidate in $candidates) {
    $imageLink = $candidate.PngPath.Replace('\', '/')
    $markdown.Add("| $($candidate.Name) | $($candidate.OverallScore) | $($candidate.OctilinearScore) | $($candidate.CrossingScore) | $($candidate.BadgeScore) | $($candidate.WidthScore) | $($candidate.RouteWarnings) | [$([System.IO.Path]::GetFileName($candidate.PngPath))]($imageLink) |") | Out-Null
}
$markdown.Add('') | Out-Null
$markdown.Add('Use this as a comparison aid only. Manual visual review still wins over numeric score changes.') | Out-Null
$markdown | Set-Content -LiteralPath $mdPath -Encoding UTF8

$cards = New-Object System.Collections.Generic.List[string]
foreach ($candidate in $candidates) {
    $imageUri = Convert-ToFileUri $candidate.PngPath
    $folderUri = Convert-ToFileUri $candidate.Path
    $auditUri = if (Test-Path -LiteralPath $candidate.AuditPath -PathType Leaf) { Convert-ToFileUri $candidate.AuditPath } else { '' }
    $scoreUri = if (Test-Path -LiteralPath $candidate.ScorePath -PathType Leaf) { Convert-ToFileUri $candidate.ScorePath } else { '' }
    $name = Convert-ToHtmlText $candidate.Name
    $city = Convert-ToHtmlText $candidate.CityName

    $cards.Add(@"
<section class="candidate-card">
  <h2>$name</h2>
  <p class="meta">$city</p>
  <table>
    <tr><th>Overall</th><td>$($candidate.OverallScore)</td><th>Octilinear</th><td>$($candidate.OctilinearScore)</td></tr>
    <tr><th>Crossings</th><td>$($candidate.CrossingScore)</td><th>Badges</th><td>$($candidate.BadgeScore)</td></tr>
    <tr><th>Warnings</th><td>$($candidate.RouteWarnings)</td><th>Non-oct</th><td>$($candidate.NonOctilinearSegments)</td></tr>
    <tr><th>Interior crossings</th><td>$($candidate.InteriorCrossings)</td><th>Sharp turns</th><td>$($candidate.SharpTurns)</td></tr>
  </table>
  <div class="links">
    <a href="$folderUri">folder</a>
    <a href="$auditUri">audit</a>
    <a href="$scoreUri">score</a>
  </div>
  <a href="$imageUri"><img src="$imageUri" alt="$name"></a>
</section>
"@) | Out-Null
}

$html = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Product Candidate Comparison</title>
  <style>
    body {
      margin: 24px;
      background: #f4f6f8;
      color: #17212b;
      font-family: "Segoe UI", Arial, sans-serif;
    }

    h1 {
      margin: 0 0 6px;
      font-size: 24px;
    }

    .hint {
      margin: 0 0 18px;
      color: #56616d;
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(520px, 1fr));
      gap: 18px;
      align-items: start;
    }

    .candidate-card {
      background: #fff;
      border: 1px solid #d8dee5;
      border-radius: 8px;
      padding: 14px;
      box-shadow: 0 1px 3px rgba(16, 24, 40, 0.08);
    }

    .candidate-card h2 {
      margin: 0;
      font-size: 16px;
    }

    .meta {
      margin: 4px 0 10px;
      color: #6b7480;
      font-size: 12px;
    }

    table {
      border-collapse: collapse;
      width: 100%;
      margin-bottom: 10px;
      font-size: 12px;
    }

    th, td {
      border-bottom: 1px solid #e8edf2;
      padding: 4px 6px;
      text-align: left;
    }

    th {
      color: #56616d;
      font-weight: 600;
      width: 24%;
    }

    .links {
      display: flex;
      gap: 12px;
      margin-bottom: 10px;
      font-size: 12px;
    }

    .links a {
      color: #175cd3;
      text-decoration: none;
    }

    img {
      display: block;
      width: 100%;
      max-height: 720px;
      object-fit: contain;
      background: white;
      border: 1px solid #eef2f6;
    }
  </style>
</head>
<body>
  <h1>Product Candidate Comparison</h1>
  <p class="hint">Generated $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')). Scores guide review; manual visual judgment still wins.</p>
  <main class="grid">
$($cards -join "`n")
  </main>
</body>
</html>
"@

$html | Set-Content -LiteralPath $htmlPath -Encoding UTF8

if (-not $SkipScreenshot) {
    $edgeCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )

    $edgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($edgePath)) {
        Write-Warning "Microsoft Edge was not found; skipping comparison screenshot."
    }
    else {
        if (Test-Path -LiteralPath $pngPath -PathType Leaf) {
            Remove-Item -LiteralPath $pngPath -Force
        }

        $htmlUri = Convert-ToFileUri $htmlPath
        $edgeOutput = & $edgePath `
            --headless `
            --disable-gpu `
            --no-first-run `
            --disable-extensions `
            "--window-size=$ScreenshotWidth,$ScreenshotHeight" `
            "--screenshot=$pngPath" `
            $htmlUri 2>&1

        if ($edgeOutput) {
            $edgeOutput | ForEach-Object { Write-Host $_ }
        }

        for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path -LiteralPath $pngPath); $attempt++) {
            Start-Sleep -Milliseconds 250
        }

        if (-not (Test-Path -LiteralPath $pngPath)) {
            Write-Warning "Comparison screenshot was not created: $pngPath"
        }
    }
}

Write-Host "Comparison written to: $outputPath"
Write-Host "HTML: $htmlPath"
Write-Host "Markdown: $mdPath"
Write-Host "CSV: $csvPath"
if (Test-Path -LiteralPath $pngPath -PathType Leaf) {
    Write-Host "Screenshot: $pngPath"
}
