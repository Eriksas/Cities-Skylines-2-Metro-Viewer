Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = 'v0.1.0-alpha.2-candidate'
$packageName = "CS2MetroDiagram-$version"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$releaseRoot = Join-Path $repoRoot 'artifacts\releases'
$releasePath = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName-win-x64.zip"
$viewerSource = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained'
$modSource = Join-Path $repoRoot 'artifacts\cs2-local-mods'
$modSourceDescription = 'artifacts\cs2-local-mods'
$localModsDataSource = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods'

if (-not (Test-Path $modSource) -and (Test-Path $localModsDataSource)) {
    $modSource = $localModsDataSource
    $modSourceDescription = $localModsDataSource
}

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

function Get-GitCommit {
    $commit = 'unknown'
    try {
        $result = git -C $repoRoot rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and ![string]::IsNullOrWhiteSpace($result)) {
            $commit = $result.Trim()
        }
    }
    catch {
    }

    return $commit
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

New-Item -ItemType Directory -Force $releasePath | Out-Null
$viewerTarget = Join-Path $releasePath 'Viewer'
$modTarget = Join-Path $releasePath 'Mod'
$docsTarget = Join-Path $releasePath 'docs'
$samplesTarget = Join-Path $releasePath 'samples'
New-Item -ItemType Directory -Force $viewerTarget, $modTarget, $docsTarget, $samplesTarget | Out-Null

Copy-Item -Path (Join-Path $viewerSource '*') -Destination $viewerTarget -Recurse -Force

if (Test-Path $modSource) {
    Copy-Item -Path (Join-Path $modSource '*') -Destination $modTarget -Recurse -Force
}
else {
    @(
        'CS2 mod artifacts were not found when this package was created.'
        'Expected local path: artifacts\cs2-local-mods'
        'Build the CS2 mod with the local modding toolchain, then rerun scripts\package-alpha-release.ps1.'
    ) | Set-Content -LiteralPath (Join-Path $modTarget 'README_MOD_ARTIFACTS_MISSING.txt') -Encoding UTF8
}

Copy-Item -Path (Join-Path $repoRoot 'docs\*') -Destination $docsTarget -Recurse -Force
Copy-Item -Path (Join-Path $repoRoot 'samples\*') -Destination $samplesTarget -Recurse -Force

Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $releasePath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\ALPHA_QUICK_START.md') -Destination (Join-Path $releasePath 'QUICK_START.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\KNOWN_ISSUES.md') -Destination (Join-Path $releasePath 'KNOWN_ISSUES.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\CHANGELOG.md') -Destination (Join-Path $releasePath 'CHANGELOG.md') -Force

$commit = Get-GitCommit
$builtAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
@(
    'CS2 Metro Diagram'
    "Version: $version"
    'Package: alpha release win-x64'
    "BuiltAtUtc: $builtAt"
    "Commit: $commit"
    "ReleaseFolder: $releasePath"
    "Zip: $zipPath"
    'Includes: Mod, Viewer, docs, samples'
    "ModSource: $modSourceDescription"
    'Stability: alpha, not a stable release'
) | Set-Content -LiteralPath (Join-Path $releasePath 'build-info.txt') -Encoding UTF8

Compress-Archive -LiteralPath $releasePath -DestinationPath $zipPath -Force

Write-Host "Alpha release folder written to $releasePath"
Write-Host "Alpha release zip written to $zipPath"
