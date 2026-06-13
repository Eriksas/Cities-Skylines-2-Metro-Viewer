param(
    [switch]$SkipSolutionBuild,
    [switch]$SkipTests,
    [switch]$IncludeModBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

function Invoke-ValidationStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Name"
    $startedAt = Get-Date
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }

    $elapsed = (Get-Date) - $startedAt
    Write-Host "OK: $Name ($([Math]::Round($elapsed.TotalSeconds, 1))s)"
}

Push-Location $repoRoot
try {
    if (-not $SkipSolutionBuild) {
        Invoke-ValidationStep -Name 'Build offline solution' -Command {
            dotnet build CS2MetroDiagram.slnx --no-restore
        }
    }

    if (-not $SkipTests) {
        Invoke-ValidationStep -Name 'Run offline test suite' -Command {
            dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
        }
    }

    if ($IncludeModBuild) {
        Invoke-ValidationStep -Name 'Build CS2 mod project' -Command {
            dotnet build "CS2 Metro\CS2 Metro.csproj" --no-restore
        }
    }

    Write-Host ""
    Write-Host 'Local validation passed.'
}
finally {
    Pop-Location
}
