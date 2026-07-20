Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
# Keep package metadata aligned with the desktop Viewer release train.
$version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.MetroDiagramViewerInformationalVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read MetroDiagramViewerInformationalVersion from Directory.Build.props.'
}
$projectPath = Join-Path $repoRoot 'src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj'
$outputPath = Join-Path $repoRoot 'artifacts\viewer-win-x64-framework-dependent'
$tempOutputPath = Join-Path $repoRoot 'artifacts\viewer-win-x64-framework-dependent.tmp'
$sampleOutputPath = Join-Path $tempOutputPath 'samples'

if (Test-Path $tempOutputPath) {
    Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $tempOutputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force $sampleOutputPath | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\VIEWER_QUICK_START.md') -Destination (Join-Path $tempOutputPath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'samples\sample-metro-small.json') -Destination (Join-Path $sampleOutputPath 'sample-metro-small.json') -Force

$commit = 'unknown'
try {
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
}
catch {
}

@(
    'CS2 Metro Diagram Viewer'
    "Version: $version"
    'Package: win-x64 framework-dependent'
    "BuiltAtUtc: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    "Commit: $commit"
    'Requires: .NET Desktop Runtime 8 for Windows x64'
) | Set-Content -LiteralPath (Join-Path $tempOutputPath 'build-info.txt') -Encoding UTF8

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

Move-Item -LiteralPath $tempOutputPath -Destination $outputPath

Write-Host "Viewer framework-dependent package written to $outputPath"
