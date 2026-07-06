[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json',
    [string] $OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Invoke-CliRender {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CliProject,
        [Parameter(Mandatory = $true)]
        [string] $InputPath,
        [Parameter(Mandatory = $true)]
        [string] $OutputPath,
        [Parameter(Mandatory = $true)]
        [string[]] $RenderArgs
    )

    $arguments = @(
        'run',
        '--project',
        $CliProject,
        '--no-restore',
        '--',
        $InputPath,
        $OutputPath
    ) + $RenderArgs

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "CLI render failed for '$OutputPath' with exit code $LASTEXITCODE."
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$inputPath = Get-FullPath $InputJson

if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    Write-Host "ERROR: Input metro export JSON was not found: $inputPath. Export Real Metro JSON in-game first, or pass -InputJson <path>." -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'artifacts\path-geometry-comparison'
}

$outputPath = Get-FullPath $OutputDir
$cliProject = Join-Path $repoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$jobs = @(
    @{
        Name = '01-geographic-stops.svg'
        Args = @('--layout', 'geographic', '--size', 'poster')
    },
    @{
        Name = '02-geographic-pathpoints.svg'
        Args = @('--layout', 'geographic', '--size', 'poster', '--use-path-points', '--no-simplify-path-points')
    },
    @{
        Name = '03-geographic-pathpoints-simplified.svg'
        Args = @('--layout', 'geographic', '--size', 'poster', '--use-path-points', '--simplify-path-points')
    }
)

Write-Host "Input: $inputPath"
Write-Host "Output directory: $outputPath"

foreach ($job in $jobs) {
    $svgPath = Join-Path $outputPath $job.Name
    Invoke-CliRender -CliProject $cliProject -InputPath $inputPath -OutputPath $svgPath -RenderArgs ([string[]] $job.Args)
    Write-Host "Generated: $svgPath"
}

Write-Host 'Path geometry comparison SVG generation completed.'
