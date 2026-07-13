param(
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$CandidateName = 'phase7-rc1',

    [string]$LocalModsPath = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$candidateRoot = Join-Path $repoRoot 'artifacts\release-candidates'
$packageName = "CS2MetroDiagram-$CandidateName"
$candidatePath = Join-Path $candidateRoot $packageName
$zipPath = Join-Path $candidateRoot "$packageName-win-x64.zip"
$viewerSource = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained'
$modSource = Join-Path $LocalModsPath 'CS2 Metro'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$embeddedVersion = ([xml](Get-Content -LiteralPath $propsPath -Raw)).Project.PropertyGroup.InformationalVersion

function Assert-UnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    if (!$candidateFull.StartsWith($baseFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside release-candidates. Base: $baseFull Candidate: $candidateFull"
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Force -Path $candidateRoot | Out-Null
Assert-UnderPath -BasePath $candidateRoot -Candidate $candidatePath
Assert-UnderPath -BasePath $candidateRoot -Candidate $zipPath

if (Test-Path -LiteralPath $candidatePath) {
    Remove-Item -LiteralPath $candidatePath -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Push-Location $repoRoot
try {
    Invoke-Step -Name 'Validate in-game UI JavaScript' -Command {
        node --check (Join-Path $repoRoot 'CS2 Metro\CS2 Metro.mjs')
    }

    Invoke-Step -Name 'Build offline solution' -Command {
        dotnet build (Join-Path $repoRoot 'CS2MetroDiagram.slnx') --no-restore
    }

    Invoke-Step -Name 'Run complete test suite' -Command {
        dotnet run --project (Join-Path $repoRoot 'src\MetroDiagram.Tests\MetroDiagram.Tests.csproj') --no-restore
    }

    Write-Host ""
    Write-Host '==> Publish self-contained Viewer' -ForegroundColor Cyan
    & (Join-Path $repoRoot 'scripts\publish-viewer-self-contained.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Viewer publish failed with exit code $LASTEXITCODE."
    }

    Invoke-Step -Name 'Build and stage CS2 mod' -Command {
        dotnet build (Join-Path $repoRoot 'CS2 Metro\CS2 Metro.csproj') `
            -c Release `
            --no-restore `
            "-p:LocalModsPath=$LocalModsPath"
    }
}
finally {
    Pop-Location
}

if (!(Test-Path -LiteralPath (Join-Path $viewerSource 'MetroDiagram.Viewer.exe'))) {
    throw "Viewer output is missing: $viewerSource"
}

if (!(Test-Path -LiteralPath (Join-Path $modSource 'CS2 Metro.dll'))) {
    throw "Staged CS2 mod output is missing: $modSource"
}

$sourceMjs = Join-Path $repoRoot 'CS2 Metro\CS2 Metro.mjs'
$stagedMjs = Join-Path $modSource 'CS2 Metro.mjs'
if (!(Test-Path -LiteralPath $stagedMjs)) {
    throw "Staged UI module is missing: $stagedMjs"
}

$sourceMjsHash = (Get-FileHash -LiteralPath $sourceMjs -Algorithm SHA256).Hash
$stagedMjsHash = (Get-FileHash -LiteralPath $stagedMjs -Algorithm SHA256).Hash
if ($sourceMjsHash -ne $stagedMjsHash) {
    throw "Staged UI module does not match source. Source=$sourceMjsHash Staged=$stagedMjsHash"
}

$viewerPath = Join-Path $candidatePath 'Viewer'
$modPath = Join-Path $candidatePath 'Mod'
$docsPath = Join-Path $candidatePath 'docs'
New-Item -ItemType Directory -Force -Path $viewerPath, $modPath, $docsPath | Out-Null
Copy-Item -Path (Join-Path $viewerSource '*') -Destination $viewerPath -Recurse -Force
Copy-Item -Path (Join-Path $modSource '*') -Destination $modPath -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $candidatePath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\PHASE7_RC_MANUAL_TEST.md') -Destination (Join-Path $docsPath 'PHASE7_RC_MANUAL_TEST.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\KNOWN_ISSUES.md') -Destination (Join-Path $docsPath 'KNOWN_ISSUES.md') -Force

$commit = 'unknown'
$workingTreeDirty = $true
try {
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
    $workingTreeDirty = @((git -C $repoRoot status --porcelain)).Count -gt 0
}
catch {
}

@(
    'CS2 Metro Diagram - Phase 7 release candidate'
    "Candidate: $CandidateName"
    "Embedded public-baseline version: $embeddedVersion"
    "BuiltAtUtc: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    "Commit: $commit"
    "WorkingTreeDirty: $workingTreeDirty"
    "ModSource: $modSource"
    "SourceMjsSha256: $sourceMjsHash"
    'Publication: private/local validation only; do not upload to PDX before owner approval.'
) | Set-Content -LiteralPath (Join-Path $candidatePath 'build-info.txt') -Encoding UTF8

$manifestPath = Join-Path $candidatePath 'manifest.sha256'
Get-ChildItem -LiteralPath $candidatePath -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($candidatePath, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    } | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Compress-Archive -Path (Join-Path $candidatePath '*') -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Phase 7 release candidate folder: $candidatePath" -ForegroundColor Green
Write-Host "Phase 7 release candidate zip:    $zipPath" -ForegroundColor Green
Write-Host "Embedded version remains $embeddedVersion until owner approval." -ForegroundColor Yellow
