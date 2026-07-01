param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipRestore,

    [switch]$CleanBuildOutput
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'CS2MetroDiagram.slnx'
$testProjectPath = Join-Path $repoRoot 'src\MetroDiagram.Tests\MetroDiagram.Tests.csproj'

function Invoke-PreflightStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Remove-BuildOutput {
    $roots = @(
        (Join-Path $repoRoot 'src'),
        (Join-Path $repoRoot 'CS2 Metro')
    )

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        Get-ChildItem -LiteralPath $root -Include bin,obj -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                Write-Host "Removing $($_.FullName)"
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            }
    }
}

Push-Location $repoRoot
try {
    if (-not $SkipRestore) {
        Invoke-PreflightStep -Name 'Restore offline solution' -Command {
            dotnet restore $solutionPath
        }
    }

    Invoke-PreflightStep -Name 'Build offline solution' -Command {
        dotnet build $solutionPath --configuration $Configuration --no-restore
    }

    Invoke-PreflightStep -Name 'Run renderer tests' -Command {
        dotnet run --project $testProjectPath --configuration $Configuration --no-restore
    }

    if ($CleanBuildOutput) {
        Write-Host ""
        Write-Host '==> Clean generated bin/obj output' -ForegroundColor Cyan
        Remove-BuildOutput
    }

    Write-Host ""
    Write-Host 'Preflight passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
