[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('NewVersion', 'UpdateConfiguration')]
    [string]$Mode = 'NewVersion',

    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'CS2 Metro\CS2 Metro.csproj'
$publishConfigurationPath = Join-Path $repoRoot 'CS2 Metro\Properties\PublishConfiguration.xml'
$profileName = if ($Mode -eq 'NewVersion') { 'PublishNewVersion' } else { 'UpdatePublishedConfiguration' }

if (-not (Test-Path -LiteralPath $publishConfigurationPath)) {
    throw "Publish configuration not found: $publishConfigurationPath"
}

[xml]$publishConfiguration = Get-Content -LiteralPath $publishConfigurationPath
$modId = $publishConfiguration.Publish.ModId.Value
if ([string]::IsNullOrWhiteSpace($modId)) {
    throw 'PublishConfiguration.xml has no ModId. Refusing to publish because this script is only for updating the existing Paradox Mods listing.'
}

Write-Host "Paradox Mods publish mode: $Mode"
Write-Host "Publish profile: $profileName"
Write-Host "ModId: $modId"
Write-Host "Project: $projectPath"
Write-Host ""
Write-Host 'This script intentionally does not support PublishNewMod.'
Write-Host 'Make sure Cities: Skylines II has been launched and signed into the target PDX account before publishing.'
Write-Host ""

$publishArgs = @(
    'publish',
    $projectPath,
    "/p:PublishProfile=$profileName"
)

if ($SkipRestore) {
    $publishArgs += '--no-restore'
}

if ($PSCmdlet.ShouldProcess("Paradox Mods listing $modId", "dotnet publish using $profileName")) {
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "Paradox Mods publish command completed for ModId $modId."
}
