<#
.SYNOPSIS
Shared helpers for the CS2 Metro diagnostic/validation scripts.

Two groups live here so the same logic is not copy-pasted across scripts:

- SVG-geometry scoring primitives (Parse-Points, Get-Distance,
  Get-AngleDegrees, Get-OctilinearDelta, Get-SegmentIntersection). These
  measure a RENDERED SVG. The authoritative layout score is now produced by
  the C# CLI (`MetroDiagram.Cli --emit-layout-score`); these remain only for
  SVG-level auditing (debug overlays, stroke-width checks) that the CLI does
  not emit.
- Small filesystem/naming utilities reused by many scripts.

Import with:
    Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force
#>

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Convert-ToDouble {
    param($Value, [double] $Default = 0.0)

    if ($null -eq $Value) {
        return $Default
    }

    $text = ([string] $Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $Default
    }

    $result = 0.0
    if ([double]::TryParse($text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $result)) {
        return $result
    }

    return $Default
}

function Convert-ToSafeName {
    param([string] $Value, [string] $Fallback = 'case')

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Fallback
    }

    $safe = $Value.Trim()
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string] $invalid, '-')
    }

    $safe = [regex]::Replace($safe, '\s+', '-')
    $safe = [regex]::Replace($safe, '-+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return $Fallback
    }

    return $safe
}

function Parse-Points {
    param([string] $Text)

    $points = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    foreach ($pair in ($Text.Trim() -split '\s+')) {
        $parts = $pair -split ','
        if ($parts.Count -ne 2) {
            continue
        }

        $points.Add([pscustomobject]@{ X = (Convert-ToDouble $parts[0]); Y = (Convert-ToDouble $parts[1]) }) | Out-Null
    }

    return $points.ToArray()
}

function Get-Distance {
    param($A, $B)
    $dx = $B.X - $A.X
    $dy = $B.Y - $A.Y
    return [Math]::Sqrt(($dx * $dx) + ($dy * $dy))
}

function Get-AngleDegrees {
    param($A, $B)
    $dx = $B.X - $A.X
    $dy = $B.Y - $A.Y
    if ([Math]::Abs($dx) -lt 0.000001 -and [Math]::Abs($dy) -lt 0.000001) {
        return 0.0
    }

    $angle = [Math]::Atan2($dy, $dx) * 180.0 / [Math]::PI
    while ($angle -lt 0.0) {
        $angle += 180.0
    }

    while ($angle -ge 180.0) {
        $angle -= 180.0
    }

    return $angle
}

function Get-AngleDelta {
    param([double] $A, [double] $B)
    $delta = [Math]::Abs($A - $B)
    while ($delta -gt 180.0) {
        $delta -= 180.0
    }

    if ($delta -gt 90.0) {
        $delta = 180.0 - $delta
    }

    return [Math]::Abs($delta)
}

function Get-OctilinearDelta {
    param([double] $Angle)

    $best = 180.0
    foreach ($target in @(0.0, 45.0, 90.0, 135.0, 180.0)) {
        $delta = Get-AngleDelta $Angle $target
        if ($delta -lt $best) {
            $best = $delta
        }
    }

    return $best
}

function Get-SegmentIntersection {
    param($A, $B, $C, $D)

    $rx = $B.X - $A.X
    $ry = $B.Y - $A.Y
    $sx = $D.X - $C.X
    $sy = $D.Y - $C.Y
    $denominator = ($rx * $sy) - ($ry * $sx)
    if ([Math]::Abs($denominator) -lt 0.000001) {
        return $null
    }

    $qx = $C.X - $A.X
    $qy = $C.Y - $A.Y
    $t = (($qx * $sy) - ($qy * $sx)) / $denominator
    $u = (($qx * $ry) - ($qy * $rx)) / $denominator
    if ($t -le 0.02 -or $t -ge 0.98 -or $u -le 0.02 -or $u -ge 0.98) {
        return $null
    }

    return [pscustomobject]@{
        X = $A.X + ($t * $rx)
        Y = $A.Y + ($t * $ry)
        T = $t
        U = $u
    }
}

Export-ModuleMember -Function Get-FullPath, Convert-ToDouble, Convert-ToSafeName, Parse-Points, Get-Distance, Get-AngleDegrees, Get-AngleDelta, Get-OctilinearDelta, Get-SegmentIntersection
