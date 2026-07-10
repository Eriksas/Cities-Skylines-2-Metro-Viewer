Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
# Single source of truth for the version string.
$version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.InformationalVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read InformationalVersion from Directory.Build.props.'
}

$packageName = "CS2MetroDiagram-$version"
$releaseRoot = Join-Path $repoRoot 'artifacts\releases'
$releasePath = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName-win-x64.zip"
$viewerSource = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained'

function Assert-UnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$CandidatePath
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath)
    $candidateFull = [System.IO.Path]::GetFullPath($CandidatePath)
    if (!$candidateFull.StartsWith($baseFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected directory. Base: $baseFull Candidate: $candidateFull"
    }
}

New-Item -ItemType Directory -Force $releaseRoot | Out-Null
Assert-UnderPath -BasePath $releaseRoot -CandidatePath $releasePath
Assert-UnderPath -BasePath $releaseRoot -CandidatePath $zipPath

if (Test-Path $releasePath) {
    Remove-Item -LiteralPath $releasePath -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet build (Join-Path $repoRoot 'CS2MetroDiagram.slnx') --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

dotnet run --project (Join-Path $repoRoot 'src\MetroDiagram.Tests\MetroDiagram.Tests.csproj') --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "MetroDiagram.Tests failed with exit code $LASTEXITCODE"
}

& (Join-Path $repoRoot 'scripts\publish-viewer-self-contained.ps1')

# Players want to unzip and run. The package is the exe plus a short bilingual
# README and the build stamp - nothing else. Docs, changelog and samples live
# on GitHub; the mod is distributed through Paradox Mods.
New-Item -ItemType Directory -Force $releasePath | Out-Null
Copy-Item -LiteralPath (Join-Path $viewerSource 'MetroDiagram.Viewer.exe') -Destination (Join-Path $releasePath 'MetroDiagram.Viewer.exe') -Force
Copy-Item -LiteralPath (Join-Path $viewerSource 'build-info.txt') -Destination (Join-Path $releasePath 'build-info.txt') -Force

@(
    "CS2 Metro Diagram Viewer $version"
    ''
    'Run MetroDiagram.Viewer.exe - no install needed (Windows x64).'
    '1. Install the "CS2 Metro Diagram" mod from Paradox Mods.'
    '2. In game: Options -> CS2 Metro Diagram -> Export Real Metro JSON.'
    '3. In the Viewer: click "Open Default Export", tidy the map, then save as SVG, PNG, or PDF.'
    ''
    "双击 MetroDiagram.Viewer.exe 直接运行，无需安装（Windows x64）。"
    "1. 在 Paradox Mods 安装 CS2 Metro Diagram 模组。"
    "2. 游戏内：选项 -> CS2 Metro Diagram -> 导出真实地铁 JSON。"
    "3. 打开 Viewer 点「打开默认导出」，整理地图后保存为 SVG、PNG 或 PDF。"
    ''
    'Docs / 文档: https://github.com/Eriksas/Cities-Skylines-2-Metro-Viewer'
) | Set-Content -LiteralPath (Join-Path $releasePath 'README.txt') -Encoding UTF8

Compress-Archive -LiteralPath $releasePath -DestinationPath $zipPath -Force

Write-Host "Release folder written to $releasePath"
Write-Host "Release zip written to $zipPath"
