Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
# Single source of truth for the desktop Viewer release train.
$version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.MetroDiagramViewerInformationalVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read MetroDiagramViewerInformationalVersion from Directory.Build.props.'
}

$projectPath = Join-Path $repoRoot 'src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj'
$outputPath = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained'
$tempOutputPath = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained.tmp'

if (Test-Path $tempOutputPath) {
    Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
}

dotnet restore $projectPath `
    -r win-x64 `
    --source 'https://api.nuget.org/v3/index.json'
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

# Player-facing binary: one compressed self-contained exe, no debug symbols,
# no NuGet documentation files. Compression halves the on-disk size for a
# small one-time cost on first start.
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:CopyDebugSymbolFilesFromPackages=false `
    -p:CopyDocumentationFilesFromPackages=false `
    --no-restore `
    -o $tempOutputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Belt and braces: nothing but the exe ships from this folder.
Get-ChildItem $tempOutputPath -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force

$commit = 'unknown'
try {
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
}
catch {
}

@(
    'CS2 Metro Diagram Viewer'
    "Version: $version"
    'Package: win-x64 self-contained single-file (compressed)'
    "BuiltAtUtc: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    "Commit: $commit"
    'Requires: Windows x64'
) | Set-Content -LiteralPath (Join-Path $tempOutputPath 'build-info.txt') -Encoding UTF8

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

Move-Item -LiteralPath $tempOutputPath -Destination $outputPath

Write-Host "Viewer self-contained package written to $outputPath"
