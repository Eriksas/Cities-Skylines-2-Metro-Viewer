[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json',
    [string] $OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MetroScriptCommon.psm1') -Force -DisableNameChecking

function Get-FamilyKey {
    param([string] $Name, [string] $Id)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $Id
    }

    $value = $Name.Trim()
    $value = [regex]::Replace($value, '\s*[\uFF08(][^\uFF09)]*[\uFF09)]\s*$', '')
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Id
    }

    return $value.Trim()
}

function Get-StationName {
    param($StationsById, [string] $StationId)
    if ($StationsById.ContainsKey($StationId) -and -not [string]::IsNullOrWhiteSpace($StationsById[$StationId].name)) {
        return $StationsById[$StationId].name
    }

    return $StationId
}

function Get-ObjectPropertyValue {
    param($Object, [string] $Name)

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-ValidStops {
    param($Line, $StationsById)
    return @(Get-ObjectPropertyValue $Line 'stops') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $StationsById.ContainsKey($_) }
}

function Get-StationEdgeKey {
    param([string] $A, [string] $B)
    if ([string]::CompareOrdinal($A, $B) -le 0) {
        return "$A|$B"
    }

    return "$B|$A"
}

function Get-DistinctEdgeCount {
    param([object[]] $Stops)
    $edges = New-Object 'System.Collections.Generic.HashSet[string]'
    for ($i = 1; $i -lt $Stops.Count; $i++) {
        $a = [string] $Stops[$i - 1]
        $b = [string] $Stops[$i]
        if ($a -eq $b) {
            continue
        }

        [void] $edges.Add((Get-StationEdgeKey $a $b))
    }

    return $edges.Count
}

function Get-LinePathPoints {
    param($Line)

    $points = New-Object System.Collections.ArrayList
    foreach ($point in @(Get-ObjectPropertyValue $Line 'pathPoints')) {
        if ($null -eq $point) {
            continue
        }

        [void] $points.Add([pscustomobject]@{
            X = [double] $point.x
            Z = [double] $point.z
        })
    }

    return @($points)
}

function Limit-GeometryPathPoints {
    param([object[]] $Points, [int] $MaxPoints = 90)

    if ($Points.Count -le $MaxPoints) {
        return @($Points)
    }

    $sampled = New-Object System.Collections.ArrayList
    $step = ($Points.Count - 1) / [double] ($MaxPoints - 1)
    for ($i = 0; $i -lt $MaxPoints; $i++) {
        $index = [int] [Math]::Round($i * $step)
        $index = [Math]::Max(0, [Math]::Min($Points.Count - 1, $index))
        if ($sampled.Count -eq 0 -or (Get-Distance $sampled[$sampled.Count - 1] $Points[$index]) -gt 0.001) {
            [void] $sampled.Add($Points[$index])
        }
    }

    if ((Get-Distance $sampled[$sampled.Count - 1] $Points[$Points.Count - 1]) -gt 0.001) {
        $sampled[$sampled.Count - 1] = $Points[$Points.Count - 1]
    }

    return @($sampled)
}

function Get-LineLength {
    param([object[]] $Points)

    $length = 0.0
    for ($i = 1; $i -lt $Points.Count; $i++) {
        $dx = $Points[$i].X - $Points[$i - 1].X
        $dz = $Points[$i].Z - $Points[$i - 1].Z
        $length += [Math]::Sqrt(($dx * $dx) + ($dz * $dz))
    }

    return $length
}

function Select-GeometryLine {
    param($Family)

    $best = $null
    $bestPointCount = -1
    $bestLength = -1.0
    foreach ($line in $Family.Lines) {
        $points = @(Get-LinePathPoints $line)
        $length = Get-LineLength $points
        if ($points.Count -gt $bestPointCount -or ($points.Count -eq $bestPointCount -and $length -gt $bestLength)) {
            $best = $line
            $bestPointCount = $points.Count
            $bestLength = $length
        }
    }

    return $best
}

function Get-Distance {
    param($A, $B)
    $dx = $A.X - $B.X
    $dz = $A.Z - $B.Z
    return [Math]::Sqrt(($dx * $dx) + ($dz * $dz))
}

function Get-NormalizedVector {
    param($Start, $End)
    $dx = $End.X - $Start.X
    $dz = $End.Z - $Start.Z
    $length = [Math]::Sqrt(($dx * $dx) + ($dz * $dz))
    if ($length -le 0.000001) {
        return [pscustomobject]@{ X = 0.0; Z = 0.0 }
    }

    return [pscustomobject]@{ X = $dx / $length; Z = $dz / $length }
}

function Get-Dot {
    param($A, $B)
    return ($A.X * $B.X) + ($A.Z * $B.Z)
}

function Get-ProjectFraction {
    param($Point, $Start, $End)
    $vx = $End.X - $Start.X
    $vz = $End.Z - $Start.Z
    $denominator = ($vx * $vx) + ($vz * $vz)
    if ($denominator -le 0.000001) {
        return 0.0
    }

    return ((($Point.X - $Start.X) * $vx) + (($Point.Z - $Start.Z) * $vz)) / $denominator
}

function Get-PointToSegmentDistance {
    param($Point, $Start, $End)
    $t = [Math]::Max(0.0, [Math]::Min(1.0, (Get-ProjectFraction $Point $Start $End)))
    $projected = [pscustomobject]@{
        X = $Start.X + (($End.X - $Start.X) * $t)
        Z = $Start.Z + (($End.Z - $Start.Z) * $t)
    }
    return Get-Distance $Point $projected
}

function Test-GeometrySegmentMatch {
    param(
        $A0,
        $A1,
        $B0,
        $B1,
        [double] $DistanceThreshold,
        [double] $MinimumOverlapLength,
        [double] $MinimumAngleCosine)

    $directionA = Get-NormalizedVector $A0 $A1
    $directionB = Get-NormalizedVector $B0 $B1
    $cosine = Get-Dot $directionA $directionB
    if ([Math]::Abs($cosine) -lt $MinimumAngleCosine) {
        return $null
    }

    $directionSign = if ($cosine -ge 0) { 1 } else { -1 }
    $axis = if ((Get-Distance $A0 $A1) -ge (Get-Distance $B0 $B1)) {
        $directionA
    }
    elseif ($directionSign -ge 0) {
        $directionB
    }
    else {
        [pscustomobject]@{ X = -$directionB.X; Z = -$directionB.Z }
    }
    $normal = [pscustomobject]@{ X = -$axis.Z; Z = $axis.X }
    $aStart = Get-Dot $A0 $axis
    $aEnd = Get-Dot $A1 $axis
    $bStart = Get-Dot $B0 $axis
    $bEnd = Get-Dot $B1 $axis
    $overlapLength = ([Math]::Min([Math]::Max($aStart, $aEnd), [Math]::Max($bStart, $bEnd)) - [Math]::Max([Math]::Min($aStart, $aEnd), [Math]::Min($bStart, $bEnd)))
    if ($overlapLength -lt $MinimumOverlapLength) {
        return $null
    }

    $midA = [pscustomobject]@{ X = ($A0.X + $A1.X) / 2.0; Z = ($A0.Z + $A1.Z) / 2.0 }
    $midB = [pscustomobject]@{ X = ($B0.X + $B1.X) / 2.0; Z = ($B0.Z + $B1.Z) / 2.0 }
    $centerDelta = [pscustomobject]@{ X = $midA.X - $midB.X; Z = $midA.Z - $midB.Z }
    $centerDistance = [Math]::Abs((Get-Dot $centerDelta $normal))
    if ($centerDistance -gt $DistanceThreshold) {
        return $null
    }

    $averageDistance = ((Get-PointToSegmentDistance $midA $B0 $B1) + (Get-PointToSegmentDistance $midB $A0 $A1)) / 2.0
    $maxDistance = @(
        (Get-PointToSegmentDistance $A0 $B0 $B1),
        (Get-PointToSegmentDistance $A1 $B0 $B1),
        (Get-PointToSegmentDistance $B0 $A0 $A1),
        (Get-PointToSegmentDistance $B1 $A0 $A1)
    ) | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum
    if ($averageDistance -gt $DistanceThreshold -or $maxDistance -gt ($DistanceThreshold * 1.8)) {
        return $null
    }

    return [pscustomobject]@{
        directionSign = $directionSign
        overlapLength = $overlapLength
        averageDistance = $averageDistance
        maxDistance = $maxDistance
    }
}

function Find-GeometrySharedCorridor {
    param($FamilyA, $FamilyB)

    $lineA = Select-GeometryLine $FamilyA
    $lineB = Select-GeometryLine $FamilyB
    if ($null -eq $lineA -or $null -eq $lineB) {
        return [pscustomobject]@{ Accepted = $false; RejectionReason = 'missing-geometry-line'; lineA = $lineA; lineB = $lineB; pathPointCountA = 0; pathPointCountB = 0 }
    }

    $rawPointsA = @(Get-LinePathPoints $lineA)
    $rawPointsB = @(Get-LinePathPoints $lineB)
    $pointsA = @(Limit-GeometryPathPoints $rawPointsA)
    $pointsB = @(Limit-GeometryPathPoints $rawPointsB)
    if ($pointsA.Count -lt 2 -or $pointsB.Count -lt 2) {
        return [pscustomobject]@{ Accepted = $false; RejectionReason = 'missing-pathPoints'; lineA = $lineA; lineB = $lineB; pathPointCountA = $rawPointsA.Count; pathPointCountB = $rawPointsB.Count }
    }

    $matches = New-Object System.Collections.ArrayList
    $distanceThreshold = 44.0
    $minimumOverlapLength = 20.0
    $minimumSharedLength = 72.0
    $minimumSegmentLength = 10.0
    $minimumAngleCosine = [Math]::Cos([Math]::PI / 8.0)
    for ($i = 0; $i -lt ($pointsA.Count - 1); $i++) {
        if ((Get-Distance $pointsA[$i] $pointsA[$i + 1]) -lt $minimumSegmentLength) {
            continue
        }

        for ($j = 0; $j -lt ($pointsB.Count - 1); $j++) {
            if ((Get-Distance $pointsB[$j] $pointsB[$j + 1]) -lt $minimumSegmentLength) {
                continue
            }

            $match = Test-GeometrySegmentMatch $pointsA[$i] $pointsA[$i + 1] $pointsB[$j] $pointsB[$j + 1] $distanceThreshold $minimumOverlapLength $minimumAngleCosine
            if ($null -ne $match) {
                $match | Add-Member -NotePropertyName segmentIndexA -NotePropertyValue $i
                $match | Add-Member -NotePropertyName segmentIndexB -NotePropertyValue $j
                [void] $matches.Add($match)
            }
        }
    }

    if ($matches.Count -eq 0) {
        return [pscustomobject]@{ Accepted = $false; RejectionReason = 'no-compatible-segments'; lineA = $lineA; lineB = $lineB; pathPointCountA = $rawPointsA.Count; pathPointCountB = $rawPointsB.Count }
    }

    $best = $null
    $used = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($seed in @($matches | Sort-Object segmentIndexA, segmentIndexB)) {
        $seedKey = "$($seed.segmentIndexA)|$($seed.segmentIndexB)"
        if (-not $used.Add($seedKey)) {
            continue
        }

        $sharedLength = [double] $seed.overlapLength
        $weightedDistance = [double] $seed.averageDistance * [double] $seed.overlapLength
        $maxDistance = [double] $seed.maxDistance
        $lastA = [int] $seed.segmentIndexA
        $lastB = [int] $seed.segmentIndexB
        $directionSign = [int] $seed.directionSign
        while ($true) {
            $nextA = $lastA + 1
            $nextB = $lastB + $directionSign
            $next = @($matches | Where-Object { $_.segmentIndexA -eq $nextA -and $_.segmentIndexB -eq $nextB -and $_.directionSign -eq $directionSign } | Select-Object -First 1)
            if ($next.Count -eq 0) {
                break
            }

            $nextMatch = $next[0]
            [void] $used.Add("$($nextMatch.segmentIndexA)|$($nextMatch.segmentIndexB)")
            $sharedLength += [double] $nextMatch.overlapLength
            $weightedDistance += [double] $nextMatch.averageDistance * [double] $nextMatch.overlapLength
            $maxDistance = [Math]::Max($maxDistance, [double] $nextMatch.maxDistance)
            $lastA = [int] $nextMatch.segmentIndexA
            $lastB = [int] $nextMatch.segmentIndexB
        }

        if ($sharedLength -lt $minimumSharedLength) {
            continue
        }

        $candidate = [pscustomobject]@{
            Accepted = $true
            RejectionReason = ''
            lineA = $lineA
            lineB = $lineB
            pathPointCountA = $rawPointsA.Count
            pathPointCountB = $rawPointsB.Count
            sampledPathPointCountA = $pointsA.Count
            sampledPathPointCountB = $pointsB.Count
            startIndexA = [int] $seed.segmentIndexA
            endIndexA = $lastA + 1
            startIndexB = [int] $seed.segmentIndexB
            endIndexB = $lastB + 1
            directionSign = $directionSign
            sharedLength = $sharedLength
            averageDistance = $weightedDistance / [Math]::Max($sharedLength, 0.001)
            maxDistance = $maxDistance
        }
        if ($null -eq $best -or $candidate.sharedLength -gt $best.sharedLength) {
            $best = $candidate
        }
    }

    if ($null -eq $best) {
        return [pscustomobject]@{ Accepted = $false; RejectionReason = 'shared-length-below-threshold'; lineA = $lineA; lineB = $lineB; pathPointCountA = $rawPointsA.Count; pathPointCountB = $rawPointsB.Count }
    }

    return $best
}

function Get-StationNearestToPoint {
    param($StationsById, [object[]] $Stops, $Point)

    $best = $null
    $bestDistance = [double]::MaxValue
    foreach ($stopId in $Stops) {
        if (-not $StationsById.ContainsKey($stopId) -or $null -eq $StationsById[$stopId].position) {
            continue
        }

        $stationPoint = [pscustomobject]@{
            X = [double] $StationsById[$stopId].position.x
            Z = [double] $StationsById[$stopId].position.z
        }
        $distance = Get-Distance $stationPoint $Point
        if ($distance -lt $bestDistance) {
            $best = [string] $stopId
            $bestDistance = $distance
        }
    }

    return [pscustomobject]@{ StationId = $best; Distance = $bestDistance }
}

function Get-ContiguousStopSlice {
    param([object[]] $Stops, [string] $StartId, [string] $EndId)

    $list = @($Stops | ForEach-Object { [string] $_ })
    $start = [Array]::IndexOf($list, $StartId)
    $end = [Array]::IndexOf($list, $EndId)
    if ($start -lt 0 -or $end -lt 0) {
        return @()
    }

    if ($start -le $end) {
        return @($list[$start..$end])
    }

    $slice = @($list[$end..$start])
    [array]::Reverse($slice)
    return @($slice)
}

function Expand-GuideSlice {
    param([object[]] $TopologyStops, [object[]] $GuideSlice, [int] $ExpansionSteps = 1)

    $topology = @($TopologyStops | ForEach-Object { [string] $_ })
    $slice = @($GuideSlice | ForEach-Object { [string] $_ })
    if ($slice.Count -lt 2 -or $ExpansionSteps -le 0) {
        return @($slice)
    }

    $firstIndex = [Array]::IndexOf($topology, $slice[0])
    $lastIndex = [Array]::IndexOf($topology, $slice[$slice.Count - 1])
    if ($firstIndex -lt 0 -or $lastIndex -lt 0) {
        return @($slice)
    }

    $reversed = $lastIndex -lt $firstIndex
    $min = [Math]::Max(0, [Math]::Min($firstIndex, $lastIndex) - $ExpansionSteps)
    $max = [Math]::Min($topology.Count - 1, [Math]::Max($firstIndex, $lastIndex) + $ExpansionSteps)
    $expanded = @($topology[$min..$max])
    if ($reversed) {
        [array]::Reverse($expanded)
    }

    return @($expanded)
}

function Count-PassThroughStops {
    param([object[]] $GuideStops, [object[]] $FollowerStops)

    $raw = @{}
    foreach ($stop in $FollowerStops) {
        $raw[[string] $stop] = $true
    }

    $count = 0
    foreach ($stop in $GuideStops) {
        if (-not $raw.ContainsKey([string] $stop)) {
            $count++
        }
    }

    return $count
}

function Get-Confidence {
    param([double] $SharedLength, [double] $AverageDistance, [double] $MaxDistance)
    $lengthScore = [Math]::Max(0, [Math]::Min(1, $SharedLength / 240.0))
    $averageScore = 1.0 - [Math]::Max(0, [Math]::Min(1, $AverageDistance / 48.0))
    $maxScore = 1.0 - [Math]::Max(0, [Math]::Min(1, $MaxDistance / 96.0))
    return [Math]::Max(0, [Math]::Min(1, ($lengthScore * 0.45) + ($averageScore * 0.35) + ($maxScore * 0.20)))
}

function Test-ExpressServiceText {
    param([string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $lower = $Value.ToLowerInvariant()
    return $Value -match '\u5FEB|\u7279\u5FEB|\u5927\u7AD9\u5FEB\u8F66|\u673A\u573A\u5FEB\u7EBF' `
        -or $lower.Contains('express') `
        -or $lower.Contains('rapid')
}

function Select-CanonicalServiceLine {
    param($Family, $StationsById)

    $best = $null
    $bestStopCount = -1
    $bestLength = -1.0
    $bestCanonicalName = $false
    $bestExpressRank = 1
    foreach ($line in $Family.Lines) {
        $stops = @(Get-ValidStops $line $StationsById)
        $pathLength = Get-LineLength @(Get-LinePathPoints $line)
        $isCanonicalName = ([string] $line.name) -eq $Family.FamilyKey -or ([string] $line.name) -match '\(Local\)|\uFF08\u666E\u901A\uFF09|\uFF08\u7AD9\u7AD9\u505C\uFF09'
        $expressRank = if (Test-ExpressServiceText ([string] $line.name)) { 1 } else { 0 }
        if ($stops.Count -gt $bestStopCount `
            -or ($stops.Count -eq $bestStopCount -and $pathLength -gt $bestLength) `
            -or ($stops.Count -eq $bestStopCount -and $pathLength -eq $bestLength -and $isCanonicalName -and -not $bestCanonicalName) `
            -or ($stops.Count -eq $bestStopCount -and $pathLength -eq $bestLength -and $isCanonicalName -eq $bestCanonicalName -and $expressRank -lt $bestExpressRank)) {
            $best = $line
            $bestStopCount = $stops.Count
            $bestLength = $pathLength
            $bestCanonicalName = $isCanonicalName
            $bestExpressRank = $expressRank
        }
    }

    return $best
}

function Select-TopologyLine {
    param($Family, $StationsById)
    return Select-CanonicalServiceLine $Family $StationsById
}

function Find-SharedRuns {
    param(
        $FamilyA,
        [object[]] $StopsA,
        $FamilyB,
        [object[]] $StopsB,
        $StationsById)

    $rows = New-Object System.Collections.ArrayList
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    for ($i = 0; $i -lt $StopsA.Count; $i++) {
        for ($j = 0; $j -lt $StopsB.Count; $j++) {
            if ([string] $StopsA[$i] -ne [string] $StopsB[$j]) {
                continue
            }

            foreach ($direction in @(1, -1)) {
                $run = New-Object System.Collections.ArrayList
                $ia = $i
                $jb = $j
                while ($ia -lt $StopsA.Count -and $jb -ge 0 -and $jb -lt $StopsB.Count -and ([string] $StopsA[$ia]) -eq ([string] $StopsB[$jb])) {
                    [void] $run.Add([string] $StopsA[$ia])
                    $ia++
                    $jb += $direction
                }

                if ($run.Count -lt 2) {
                    continue
                }

                $runKey = "$($FamilyA.FamilyKey)|$($FamilyB.FamilyKey)|$([string]::Join('>', @($run)))"
                if (-not $seen.Add($runKey)) {
                    continue
                }

                $startId = [string] $run[0]
                $endId = [string] $run[$run.Count - 1]
                [void] $rows.Add([pscustomobject]@{
                    familyA = $FamilyA.FamilyKey
                    familyB = $FamilyB.FamilyKey
                    startStationId = $startId
                    startStationName = Get-StationName $StationsById $startId
                    endStationId = $endId
                    endStationName = Get-StationName $StationsById $endId
                    divergenceStationId = $endId
                    divergenceStationName = Get-StationName $StationsById $endId
                    stationCount = $run.Count
                    edgeCount = $run.Count - 1
                    orientation = if ($direction -eq 1) { 'same' } else { 'reversed' }
                    stationIds = [string]::Join('>', @($run))
                    stationNames = [string]::Join(' -> ', @($run | ForEach-Object { Get-StationName $StationsById $_ }))
                })
            }
        }
    }

    return @($rows)
}

function Invoke-CliRender {
    param(
        [string] $RepoRoot,
        [string] $InputPath,
        [string] $OutputPath,
        [string] $Layout
    )

    $cliProject = Join-Path $RepoRoot 'src\MetroDiagram.Cli\MetroDiagram.Cli.csproj'
    & dotnet run --project $cliProject --no-restore -- $InputPath $OutputPath --layout $Layout --size poster --hide-generic-labels --hide-crowded-labels
    if ($LASTEXITCODE -ne 0) {
        throw "CLI render failed for layout '$Layout'."
    }
}

function Get-SvgStations {
    param([string] $SvgPath)

    [xml] $xml = Get-Content -LiteralPath $SvgPath -Raw -Encoding UTF8
    $stations = @{}
    $xml.SelectNodes("//*[local-name()='circle' and @data-station-id]") | ForEach-Object {
        $stations[$_.GetAttribute('data-station-id')] = [pscustomobject]@{
            X = [double]::Parse($_.GetAttribute('cx'), [System.Globalization.CultureInfo]::InvariantCulture)
            Y = [double]::Parse($_.GetAttribute('cy'), [System.Globalization.CultureInfo]::InvariantCulture)
            Adjusted = $_.GetAttribute('data-schematic-station-adjusted')
            AdjustmentDistance = $_.GetAttribute('data-schematic-station-adjustment-distance')
        }
    }

    return $stations
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$inputPath = Get-FullPath $InputJson
if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    Write-Host "ERROR: Input JSON not found: $inputPath" -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'artifacts\schematic-v2-diagnostics'
}

$outputPath = Get-FullPath $OutputDir
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$document = Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stations = @($document.network.stations)
$lines = @($document.network.lines)
$stationsById = @{}
foreach ($station in $stations) {
    if (-not [string]::IsNullOrWhiteSpace($station.id) -and -not $stationsById.ContainsKey($station.id)) {
        $stationsById[$station.id] = $station
    }
}

$families = @{}
foreach ($line in $lines) {
    $familyKey = Get-FamilyKey -Name $line.name -Id $line.id
    if (-not $families.ContainsKey($familyKey)) {
        $families[$familyKey] = [pscustomobject]@{
            FamilyKey = $familyKey
            Lines = New-Object System.Collections.ArrayList
        }
    }

    [void] $families[$familyKey].Lines.Add($line)
}

$familyTopologyRows = New-Object System.Collections.ArrayList
$serviceSimplificationRows = New-Object System.Collections.ArrayList
$familyTopologyStops = @{}
foreach ($family in $families.Values) {
    $topologyLine = Select-TopologyLine $family $stationsById
    if ($null -eq $topologyLine) {
        continue
    }

    $stops = @(Get-ValidStops $topologyLine $stationsById)
    $familyTopologyStops[$family.FamilyKey] = $stops
    [void] $familyTopologyRows.Add([pscustomobject]@{
        family = $family.FamilyKey
        topologyLineId = $topologyLine.id
        topologyLineName = $topologyLine.name
        stopCount = $stops.Count
        distinctStopCount = @($stops | Select-Object -Unique).Count
        distinctEdgeCount = Get-DistinctEdgeCount $stops
    })

    $hiddenLines = @($family.Lines | Where-Object { ([string] $_.id) -ne ([string] $topologyLine.id) })
    $hiddenNames = @($hiddenLines | ForEach-Object { [string] $_.name })
    $hiddenStopCounts = @($hiddenLines | ForEach-Object { @(Get-ValidStops $_ $stationsById).Count })
    $hiddenPathLengths = @($hiddenLines | ForEach-Object { [Math]::Round((Get-LineLength @(Get-LinePathPoints $_)), 3) })
    $hasExpressVariant = @(
        $family.Lines | Where-Object { Test-ExpressServiceText ([string] $_.name) }
    ).Count -gt 0
    [void] $serviceSimplificationRows.Add([pscustomobject]@{
        family = $family.FamilyKey
        canonicalRoute = $topologyLine.id
        canonicalName = $topologyLine.name
        canonicalReason = 'max-stop-count-then-path-length-then-ordinary-name'
        canonicalStopCount = $stops.Count
        canonicalPathLength = [Math]::Round((Get-LineLength @(Get-LinePathPoints $topologyLine)), 3)
        hiddenVariants = [string]::Join('|', $hiddenNames)
        hiddenVariantStopCounts = [string]::Join('|', $hiddenStopCounts)
        hiddenVariantPathLengths = [string]::Join('|', $hiddenPathLengths)
        expressMarkerApplied = $hasExpressVariant
        fallbackReason = if ($family.Lines.Count -eq 1) { 'single-variant-family' } else { '' }
    })
}

$sharedCorridorRows = New-Object System.Collections.ArrayList
$geometrySharedCorridorRows = New-Object System.Collections.ArrayList
$geometryRejectedRows = New-Object System.Collections.ArrayList
$routeGuideRows = New-Object System.Collections.ArrayList
$parallelCorridorRows = New-Object System.Collections.ArrayList
$familyList = @($families.Values | Sort-Object FamilyKey)
for ($i = 0; $i -lt $familyList.Count; $i++) {
    for ($j = $i + 1; $j -lt $familyList.Count; $j++) {
        $familyA = $familyList[$i]
        $familyB = $familyList[$j]
        if (-not $familyTopologyStops.ContainsKey($familyA.FamilyKey) -or -not $familyTopologyStops.ContainsKey($familyB.FamilyKey)) {
            continue
        }

        $runs = Find-SharedRuns $familyA @($familyTopologyStops[$familyA.FamilyKey]) $familyB @($familyTopologyStops[$familyB.FamilyKey]) $stationsById
        foreach ($run in $runs) {
            [void] $sharedCorridorRows.Add($run)
        }

        $geometry = Find-GeometrySharedCorridor $familyA $familyB
        $stopSequenceMatched = @($runs).Count -gt 0
        if ($geometry.Accepted -eq $true) {
            $pointsA = @(Limit-GeometryPathPoints @(Get-LinePathPoints $geometry.lineA))
            $pointsB = @(Limit-GeometryPathPoints @(Get-LinePathPoints $geometry.lineB))
            $stopsA = @($familyTopologyStops[$familyA.FamilyKey])
            $stopsB = @($familyTopologyStops[$familyB.FamilyKey])
            $startA = Get-StationNearestToPoint $stationsById $stopsA $pointsA[$geometry.startIndexA]
            $endA = Get-StationNearestToPoint $stationsById $stopsA $pointsA[$geometry.endIndexA]
            $startB = Get-StationNearestToPoint $stationsById $stopsB $pointsB[$geometry.startIndexB]
            $endB = Get-StationNearestToPoint $stationsById $stopsB $pointsB[$geometry.endIndexB]
            $guideFamily = if ($stopsA.Count -ge $stopsB.Count) { $familyA.FamilyKey } else { $familyB.FamilyKey }
            $guideStops = if ($guideFamily -eq $familyA.FamilyKey) {
                @(Get-ContiguousStopSlice $stopsA $startA.StationId $endA.StationId)
            }
            else {
                @(Get-ContiguousStopSlice $stopsB $startB.StationId $endB.StationId)
            }
            $guideStops = @($guideStops)
            if ($guideStops.Count -le 2) {
                $hostStopsForExpansion = if ($guideFamily -eq $familyA.FamilyKey) { $stopsA } else { $stopsB }
                $guideStops = @(Expand-GuideSlice $hostStopsForExpansion $guideStops 1)
            }

            $followerFamily = if ($guideFamily -eq $familyA.FamilyKey) { $familyB.FamilyKey } else { $familyA.FamilyKey }
            $followerStops = if ($followerFamily -eq $familyA.FamilyKey) { $stopsA } else { $stopsB }
            $hostIntervalNodeCount = $guideStops.Count
            $passThroughNodeCount = Count-PassThroughStops $guideStops $followerStops
            $renderRouteChainBeforeCount = $followerStops.Count
            $renderRouteChainAfterCount = $renderRouteChainBeforeCount + $passThroughNodeCount
            $confidence = Get-Confidence $geometry.sharedLength $geometry.averageDistance $geometry.maxDistance
            $geometryOnly = -not $stopSequenceMatched
            $routeGuideInjected = $hostIntervalNodeCount -gt 2 -and $passThroughNodeCount -gt 0
            $parallelRendered = $routeGuideInjected -and $hostIntervalNodeCount -gt 2
            $skippedReason = if ($routeGuideInjected) {
                ''
            }
            elseif ($hostIntervalNodeCount -le 2) {
                'host-interval-too-short'
            }
            elseif ($passThroughNodeCount -le 0) {
                'no-pass-through-nodes'
            }
            else {
                'not-materialized'
            }
            $row = [pscustomobject]@{
                familyA = $familyA.FamilyKey
                familyB = $familyB.FamilyKey
                detectionSource = if ($geometryOnly) { 'geometry pathPoints corridor' } else { 'shared stop sequence + geometry pathPoints corridor' }
                approximateSharedLength = [Math]::Round($geometry.sharedLength, 3)
                averageDistance = [Math]::Round($geometry.averageDistance, 3)
                maxDistance = [Math]::Round($geometry.maxDistance, 3)
                confidence = [Math]::Round($confidence, 4)
                startNearestStationA = $startA.StationId
                startNearestStationAName = Get-StationName $stationsById $startA.StationId
                endNearestStationA = $endA.StationId
                endNearestStationAName = Get-StationName $stationsById $endA.StationId
                startNearestStationB = $startB.StationId
                startNearestStationBName = Get-StationName $stationsById $startB.StationId
                endNearestStationB = $endB.StationId
                endNearestStationBName = Get-StationName $stationsById $endB.StationId
                divergenceStation = $endA.StationId
                divergenceStationName = Get-StationName $stationsById $endA.StationId
                stopSequenceDetectionMatched = $stopSequenceMatched
                geometryOnly = $geometryOnly
                routeGuideInjected = $routeGuideInjected
                hostIntervalNodeCount = $hostIntervalNodeCount
                passThroughNodeCount = $passThroughNodeCount
                renderRouteChainBeforeCount = $renderRouteChainBeforeCount
                renderRouteChainAfterCount = $renderRouteChainAfterCount
                materialized = $routeGuideInjected
                parallelRendered = $parallelRendered
                skippedReason = $skippedReason
                guideFamily = $guideFamily
                followerFamily = $followerFamily
                guideStationIds = [string]::Join('>', @($guideStops))
                guideStationNames = [string]::Join(' -> ', @($guideStops | ForEach-Object { Get-StationName $stationsById $_ }))
                lineA = $geometry.lineA.id
                lineB = $geometry.lineB.id
                pathPointCountA = $geometry.pathPointCountA
                pathPointCountB = $geometry.pathPointCountB
            }
            [void] $geometrySharedCorridorRows.Add($row)
            [void] $parallelCorridorRows.Add([pscustomobject]@{
                familyA = $familyA.FamilyKey
                familyB = $familyB.FamilyKey
                detected = $true
                materialized = $routeGuideInjected
                parallelRendered = $parallelRendered
                hostFamily = $guideFamily
                followerFamily = $followerFamily
                hostIntervalNodeCount = $hostIntervalNodeCount
                passThroughNodeCount = $passThroughNodeCount
                renderRouteChainBeforeCount = $renderRouteChainBeforeCount
                renderRouteChainAfterCount = $renderRouteChainAfterCount
                passThroughStations = [string]::Join('>', @($guideStops))
                passThroughStationNames = [string]::Join(' -> ', @($guideStops | ForEach-Object { Get-StationName $stationsById $_ }))
                sharedCorridorRunLength = [Math]::Round($geometry.sharedLength, 3)
                divergenceStation = $endA.StationId
                divergenceStationName = Get-StationName $stationsById $endA.StationId
                skippedReason = $skippedReason
            })

            foreach ($familyKey in @($familyA.FamilyKey, $familyB.FamilyKey)) {
                [void] $routeGuideRows.Add([pscustomobject]@{
                    family = $familyKey
                    source = 'geometry-shared-corridor'
                    corridorFamilyA = $familyA.FamilyKey
                    corridorFamilyB = $familyB.FamilyKey
                    confidence = [Math]::Round($confidence, 4)
                    sharedLength = [Math]::Round($geometry.sharedLength, 3)
                    guideFamily = $guideFamily
                    followerFamily = $followerFamily
                    hostIntervalNodeCount = $hostIntervalNodeCount
                    passThroughNodeCount = $passThroughNodeCount
                    renderRouteChainBeforeCount = $renderRouteChainBeforeCount
                    renderRouteChainAfterCount = $renderRouteChainAfterCount
                    materialized = $routeGuideInjected
                    guideStationIds = [string]::Join('>', @($guideStops))
                    guideStationNames = [string]::Join(' -> ', @($guideStops | ForEach-Object { Get-StationName $stationsById $_ }))
                })
            }
        }
        else {
            [void] $geometryRejectedRows.Add([pscustomobject]@{
                familyA = $familyA.FamilyKey
                familyB = $familyB.FamilyKey
                rejectionReason = $geometry.RejectionReason
                lineA = if ($null -ne $geometry.lineA) { $geometry.lineA.id } else { '' }
                lineB = if ($null -ne $geometry.lineB) { $geometry.lineB.id } else { '' }
                pathPointCountA = if ($null -ne $geometry.pathPointCountA) { $geometry.pathPointCountA } else { 0 }
                pathPointCountB = if ($null -ne $geometry.pathPointCountB) { $geometry.pathPointCountB } else { 0 }
            })
        }
    }
}

$edgeRows = New-Object System.Collections.ArrayList
$degree = @{}
foreach ($station in $stations) {
    if (-not [string]::IsNullOrWhiteSpace($station.id)) {
        $degree[$station.id] = New-Object 'System.Collections.Generic.HashSet[string]'
    }
}

foreach ($family in $families.Values) {
    foreach ($line in $family.Lines) {
        $stops = @($line.stops) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $stationsById.ContainsKey($_) }
        for ($i = 1; $i -lt $stops.Count; $i++) {
            $from = [string] $stops[$i - 1]
            $to = [string] $stops[$i]
            if ($from -eq $to) {
                continue
            }

            [void] $degree[$from].Add($to)
            [void] $degree[$to].Add($from)
            [void] $edgeRows.Add([pscustomobject]@{
                family = $family.FamilyKey
                lineId = $line.id
                lineName = $line.name
                fromId = $from
                fromName = Get-StationName $stationsById $from
                toId = $to
                toName = Get-StationName $stationsById $to
            })
        }
    }
}

$stationDegreeRows = foreach ($station in $stations) {
    if ([string]::IsNullOrWhiteSpace($station.id)) {
        continue
    }

    [pscustomobject]@{
        stationId = $station.id
        stationName = Get-StationName $stationsById $station.id
        degree = if ($degree.ContainsKey($station.id)) { $degree[$station.id].Count } else { 0 }
        lineCount = @($station.lines).Count
        isInterchange = ($station.isInterchange -eq $true) -or (@($station.lines).Count -gt 1)
    }
}

$schematicV2Svg = Join-Path $outputPath 'schematic-topology-debug.svg'
Invoke-CliRender -RepoRoot $repoRoot -InputPath $inputPath -OutputPath $schematicV2Svg -Layout 'schematic-v2'
Copy-Item -LiteralPath $schematicV2Svg -Destination (Join-Path $outputPath 'schematic-v2-service-simplified.svg') -Force
Copy-Item -LiteralPath $schematicV2Svg -Destination (Join-Path $outputPath 'schematic-v2-service-simplified-debug.svg') -Force

$layoutStations = Get-SvgStations -SvgPath $schematicV2Svg
$v2Stations = Get-SvgStations -SvgPath $schematicV2Svg
$minSpacing = 32
$denseRows = New-Object System.Collections.ArrayList
$ids = @($layoutStations.Keys)
for ($i = 0; $i -lt $ids.Count; $i++) {
    for ($j = $i + 1; $j -lt $ids.Count; $j++) {
        $a = $layoutStations[$ids[$i]]
        $b = $layoutStations[$ids[$j]]
        $dx = $a.X - $b.X
        $dy = $a.Y - $b.Y
        $distance = [Math]::Sqrt($dx * $dx + $dy * $dy)
        if ($distance -lt $minSpacing) {
            [void] $denseRows.Add([pscustomobject]@{
                stationA = $ids[$i]
                stationAName = Get-StationName $stationsById $ids[$i]
                stationB = $ids[$j]
                stationBName = Get-StationName $stationsById $ids[$j]
                layoutDistance = [Math]::Round($distance, 3)
            })
        }
    }
}

$edgeRows | Export-Csv -LiteralPath (Join-Path $outputPath 'adjacency-edges.csv') -NoTypeInformation -Encoding UTF8
$stationDegreeRows | Export-Csv -LiteralPath (Join-Path $outputPath 'station-degree.csv') -NoTypeInformation -Encoding UTF8
$denseRows | Export-Csv -LiteralPath (Join-Path $outputPath 'dense-junctions.csv') -NoTypeInformation -Encoding UTF8
$familyTopologyRows | Export-Csv -LiteralPath (Join-Path $outputPath 'schematic-v2-family-topology-lines.csv') -NoTypeInformation -Encoding UTF8
$serviceSimplificationRows | Export-Csv -LiteralPath (Join-Path $outputPath 'service-family-simplification.csv') -NoTypeInformation -Encoding UTF8
$sharedCorridorRows | Export-Csv -LiteralPath (Join-Path $outputPath 'shared-corridors.csv') -NoTypeInformation -Encoding UTF8
$geometrySharedCorridorRows | Export-Csv -LiteralPath (Join-Path $outputPath 'geometry-shared-corridors.csv') -NoTypeInformation -Encoding UTF8
$routeGuideRows | Export-Csv -LiteralPath (Join-Path $outputPath 'schematic-v2-route-guides.csv') -NoTypeInformation -Encoding UTF8
$parallelCorridorRows | Export-Csv -LiteralPath (Join-Path $outputPath 'schematic-v2-parallel-corridors.csv') -NoTypeInformation -Encoding UTF8

$sharedText = New-Object System.Collections.ArrayList
[void] $sharedText.Add('# Schematic-v2 Shared Corridor Diagnostics')
[void] $sharedText.Add('')
[void] $sharedText.Add("Input JSON: $inputPath")
[void] $sharedText.Add("Shared corridor runs: $($sharedCorridorRows.Count)")
[void] $sharedText.Add('')
if ($sharedCorridorRows.Count -eq 0) {
    [void] $sharedText.Add('No exact topology shared corridor runs were found from display family stop sequences.')
}
else {
    foreach ($row in $sharedCorridorRows | Sort-Object familyA, familyB, edgeCount -Descending) {
        [void] $sharedText.Add("$($row.familyA) + $($row.familyB): $($row.stationNames) [$($row.orientation)], edges=$($row.edgeCount), divergence=$($row.divergenceStationName)")
    }
}
$line2And10 = @($sharedCorridorRows | Where-Object {
    (($_.familyA -like '*2*' -and $_.familyB -like '*10*') -or ($_.familyA -like '*10*' -and $_.familyB -like '*2*'))
})
[void] $sharedText.Add('')
[void] $sharedText.Add("2/10 shared corridor candidates: $($line2And10.Count)")
foreach ($row in $line2And10) {
    [void] $sharedText.Add("- $($row.familyA) + $($row.familyB): $($row.stationNames)")
}
$sharedText | Set-Content -LiteralPath (Join-Path $outputPath 'shared-corridors.txt') -Encoding UTF8

$geometryText = New-Object System.Collections.ArrayList
[void] $geometryText.Add('# Schematic-v2 Geometry Shared Corridor Diagnostics')
[void] $geometryText.Add('')
[void] $geometryText.Add("Input JSON: $inputPath")
[void] $geometryText.Add("Accepted geometry corridors: $($geometrySharedCorridorRows.Count)")
[void] $geometryText.Add("Rejected family pairs: $($geometryRejectedRows.Count)")
[void] $geometryText.Add('')
if ($geometrySharedCorridorRows.Count -eq 0) {
    [void] $geometryText.Add('No geometry-based shared corridor candidates were accepted from pathPoints.')
}
else {
    foreach ($row in $geometrySharedCorridorRows | Sort-Object familyA, familyB) {
        [void] $geometryText.Add("$($row.familyA) + $($row.familyB): source=$($row.detectionSource), length=$($row.approximateSharedLength), avgDistance=$($row.averageDistance), maxDistance=$($row.maxDistance), confidence=$($row.confidence)")
        [void] $geometryText.Add("  start: $($row.startNearestStationAName) / $($row.startNearestStationBName)")
        [void] $geometryText.Add("  end/divergence: $($row.endNearestStationAName) / $($row.endNearestStationBName)")
        [void] $geometryText.Add("  geometryOnly=$($row.geometryOnly), routeGuideInjected=$($row.routeGuideInjected), materialized=$($row.materialized), parallelRendered=$($row.parallelRendered)")
        [void] $geometryText.Add("  hostIntervalNodeCount=$($row.hostIntervalNodeCount), passThroughNodeCount=$($row.passThroughNodeCount), renderRouteChainBeforeCount=$($row.renderRouteChainBeforeCount), renderRouteChainAfterCount=$($row.renderRouteChainAfterCount)")
        [void] $geometryText.Add("  guide: $($row.guideStationNames)")
        if (-not [string]::IsNullOrWhiteSpace($row.skippedReason)) {
            [void] $geometryText.Add("  skipped: $($row.skippedReason)")
        }
    }
}
$geometryLine2And10 = @($geometrySharedCorridorRows | Where-Object {
    (($_.familyA -like '*2*' -and $_.familyB -like '*10*') -or ($_.familyA -like '*10*' -and $_.familyB -like '*2*'))
})
[void] $geometryText.Add('')
[void] $geometryText.Add("2/10 geometry shared corridor candidates: $($geometryLine2And10.Count)")
foreach ($row in $geometryLine2And10) {
    [void] $geometryText.Add("- $($row.familyA) + $($row.familyB): length=$($row.approximateSharedLength), confidence=$($row.confidence), guide=$($row.guideStationNames), injected=$($row.routeGuideInjected), hostIntervalNodeCount=$($row.hostIntervalNodeCount), passThroughNodeCount=$($row.passThroughNodeCount), parallelRendered=$($row.parallelRendered)")
}
[void] $geometryText.Add('')
[void] $geometryText.Add('Rejected candidates:')
foreach ($row in $geometryRejectedRows | Select-Object -First 30) {
    [void] $geometryText.Add("- $($row.familyA) + $($row.familyB): $($row.rejectionReason)")
}
$geometryText | Set-Content -LiteralPath (Join-Path $outputPath 'geometry-shared-corridors.txt') -Encoding UTF8

$routeGuideText = New-Object System.Collections.ArrayList
[void] $routeGuideText.Add('# Schematic-v2 Route Guides')
[void] $routeGuideText.Add('')
[void] $routeGuideText.Add("Route guide rows: $($routeGuideRows.Count)")
foreach ($row in $routeGuideRows | Sort-Object family) {
    [void] $routeGuideText.Add("$($row.family): source=$($row.source), corridor=$($row.corridorFamilyA)+$($row.corridorFamilyB), confidence=$($row.confidence), length=$($row.sharedLength), materialized=$($row.materialized)")
    [void] $routeGuideText.Add("  hostIntervalNodeCount=$($row.hostIntervalNodeCount), passThroughNodeCount=$($row.passThroughNodeCount), renderRouteChainBeforeCount=$($row.renderRouteChainBeforeCount), renderRouteChainAfterCount=$($row.renderRouteChainAfterCount)")
    [void] $routeGuideText.Add("  guide: $($row.guideStationNames)")
}
$routeGuideText | Set-Content -LiteralPath (Join-Path $outputPath 'schematic-v2-route-guides.txt') -Encoding UTF8

$serviceText = New-Object System.Collections.ArrayList
[void] $serviceText.Add('# Schematic-v2 Service Family Simplification')
[void] $serviceText.Add('')
[void] $serviceText.Add("Service family rows: $($serviceSimplificationRows.Count)")
foreach ($row in $serviceSimplificationRows | Sort-Object family) {
    [void] $serviceText.Add("$($row.family): canonical=$($row.canonicalRoute) ($($row.canonicalName)), reason=$($row.canonicalReason), expressMarkerApplied=$($row.expressMarkerApplied)")
    if (-not [string]::IsNullOrWhiteSpace($row.hiddenVariants)) {
        [void] $serviceText.Add("  hidden variants: $($row.hiddenVariants)")
        [void] $serviceText.Add("  hidden stop counts: $($row.hiddenVariantStopCounts)")
        [void] $serviceText.Add("  hidden path lengths: $($row.hiddenVariantPathLengths)")
    }

    if (-not [string]::IsNullOrWhiteSpace($row.fallbackReason)) {
        [void] $serviceText.Add("  fallback: $($row.fallbackReason)")
    }
}
$serviceText | Set-Content -LiteralPath (Join-Path $outputPath 'service-family-simplification.txt') -Encoding UTF8

$parallelText = New-Object System.Collections.ArrayList
[void] $parallelText.Add('# Schematic-v2 Parallel Corridors')
[void] $parallelText.Add('')
[void] $parallelText.Add("Parallel corridor rows: $($parallelCorridorRows.Count)")
foreach ($row in $parallelCorridorRows | Sort-Object familyA, familyB) {
    [void] $parallelText.Add("$($row.familyA) + $($row.familyB): detected=$($row.detected), materialized=$($row.materialized), parallelRendered=$($row.parallelRendered), host=$($row.hostFamily), follower=$($row.followerFamily), hostIntervalNodeCount=$($row.hostIntervalNodeCount), passThroughNodeCount=$($row.passThroughNodeCount), before=$($row.renderRouteChainBeforeCount), after=$($row.renderRouteChainAfterCount), length=$($row.sharedCorridorRunLength)")
    [void] $parallelText.Add("  pass-through: $($row.passThroughStationNames)")
    if (-not [string]::IsNullOrWhiteSpace($row.skippedReason)) {
        [void] $parallelText.Add("  skipped: $($row.skippedReason)")
    }
}
$parallelText | Set-Content -LiteralPath (Join-Path $outputPath 'schematic-v2-parallel-corridors.txt') -Encoding UTF8

$debugSvg = New-Object System.Collections.Generic.List[string]
$debugSvg.Add('<?xml version="1.0" encoding="UTF-8"?>')
$debugSvg.Add('<svg xmlns="http://www.w3.org/2000/svg" width="3200" height="2000" viewBox="0 0 3200 2000">')
$debugSvg.Add('<rect x="0" y="0" width="3200" height="2000" fill="#ffffff" />')
$debugSvg.Add('<g id="shared-corridor-diagnostic-runs" fill="none" stroke-linecap="round" stroke-linejoin="round">')
$runIndex = 0
foreach ($row in $sharedCorridorRows) {
    $stationIds = @(([string] $row.stationIds).Split('>', [System.StringSplitOptions]::RemoveEmptyEntries))
    $points = New-Object System.Collections.ArrayList
    foreach ($stationId in $stationIds) {
        if ($v2Stations.ContainsKey($stationId)) {
            [void] $points.Add("$([Math]::Round($v2Stations[$stationId].X, 3)),$([Math]::Round($v2Stations[$stationId].Y, 3))")
        }
    }

    if ($points.Count -ge 2) {
        $pointList = [string]::Join(' ', @($points))
        $debugSvg.Add("<polyline points=""$pointList"" stroke=""#e11d48"" stroke-width=""16"" opacity=""0.72"" data-shared-corridor-diagnostic=""true"" data-shared-corridor-run-id=""shared-$runIndex"" data-shared-corridor-family-a=""$([System.Security.SecurityElement]::Escape($row.familyA))"" data-shared-corridor-family-b=""$([System.Security.SecurityElement]::Escape($row.familyB))"" />")
        $runIndex++
    }
}
$debugSvg.Add('</g>')
$debugSvg.Add('<g id="stations" font-family="Arial, sans-serif" font-size="11" fill="#1f2933">')
foreach ($stationId in $v2Stations.Keys) {
    $station = $v2Stations[$stationId]
    $stationName = Get-StationName $stationsById $stationId
    $debugSvg.Add("<circle cx=""$([Math]::Round($station.X, 3))"" cy=""$([Math]::Round($station.Y, 3))"" r=""5"" fill=""#ffffff"" stroke=""#111827"" stroke-width=""1.5"" data-station-id=""$([System.Security.SecurityElement]::Escape($stationId))"" />")
    $debugSvg.Add("<text x=""$([Math]::Round($station.X + 7, 3))"" y=""$([Math]::Round($station.Y - 7, 3))"">$([System.Security.SecurityElement]::Escape($stationName))</text>")
}
$debugSvg.Add('</g>')
$debugSvg.Add('</svg>')
$debugSvg | Set-Content -LiteralPath (Join-Path $outputPath 'schematic-v2-shared-corridor-debug.svg') -Encoding UTF8

$geometryDebugSvg = New-Object System.Collections.Generic.List[string]
$geometryDebugSvg.Add('<?xml version="1.0" encoding="UTF-8"?>')
$geometryDebugSvg.Add('<svg xmlns="http://www.w3.org/2000/svg" width="3200" height="2000" viewBox="0 0 3200 2000">')
$geometryDebugSvg.Add('<rect x="0" y="0" width="3200" height="2000" fill="#ffffff" />')
$geometryDebugSvg.Add('<g id="geometry-shared-corridor-diagnostic-runs" fill="none" stroke-linecap="round" stroke-linejoin="round">')
$geometryRunIndex = 0
foreach ($row in $geometrySharedCorridorRows) {
    $stationIds = @(([string] $row.guideStationIds).Split('>', [System.StringSplitOptions]::RemoveEmptyEntries))
    $points = New-Object System.Collections.ArrayList
    foreach ($stationId in $stationIds) {
        if ($v2Stations.ContainsKey($stationId)) {
            [void] $points.Add("$([Math]::Round($v2Stations[$stationId].X, 3)),$([Math]::Round($v2Stations[$stationId].Y, 3))")
        }
    }

    if ($points.Count -ge 2) {
        $pointList = [string]::Join(' ', @($points))
        $isLine2And10 = (($row.familyA -like '*2*' -and $row.familyB -like '*10*') -or ($row.familyA -like '*10*' -and $row.familyB -like '*2*'))
        $stroke = if ($isLine2And10) { '#f97316' } else { '#2563eb' }
        $width = if ($isLine2And10) { 20 } else { 12 }
        $geometryDebugSvg.Add("<polyline points=""$pointList"" stroke=""$stroke"" stroke-width=""$width"" opacity=""0.72"" data-schematic-v2-geometry-corridor=""true"" data-schematic-v2-corridor-id=""geometry-$geometryRunIndex"" data-schematic-v2-corridor-family-a=""$([System.Security.SecurityElement]::Escape($row.familyA))"" data-schematic-v2-corridor-family-b=""$([System.Security.SecurityElement]::Escape($row.familyB))"" data-schematic-v2-corridor-source=""pathPoints"" data-schematic-v2-corridor-confidence=""$($row.confidence)"" data-schematic-v2-corridor-shared-length=""$($row.approximateSharedLength)"" data-schematic-v2-route-guide=""$($row.routeGuideInjected)"" />")
        $geometryRunIndex++
    }
}
$geometryDebugSvg.Add('</g>')
$geometryDebugSvg.Add('<g id="stations" font-family="Arial, sans-serif" font-size="11" fill="#1f2933">')
foreach ($stationId in $v2Stations.Keys) {
    $station = $v2Stations[$stationId]
    $stationName = Get-StationName $stationsById $stationId
    $geometryDebugSvg.Add("<circle cx=""$([Math]::Round($station.X, 3))"" cy=""$([Math]::Round($station.Y, 3))"" r=""5"" fill=""#ffffff"" stroke=""#111827"" stroke-width=""1.5"" data-station-id=""$([System.Security.SecurityElement]::Escape($stationId))"" />")
    $geometryDebugSvg.Add("<text x=""$([Math]::Round($station.X + 7, 3))"" y=""$([Math]::Round($station.Y - 7, 3))"">$([System.Security.SecurityElement]::Escape($stationName))</text>")
}
$geometryDebugSvg.Add('</g>')
$geometryDebugSvg.Add('</svg>')
$geometryDebugSvg | Set-Content -LiteralPath (Join-Path $outputPath 'geometry-shared-corridor-debug.svg') -Encoding UTF8

$shortEdgeLines = New-Object System.Collections.ArrayList
foreach ($edge in $edgeRows) {
    if (-not $layoutStations.ContainsKey($edge.fromId) -or -not $layoutStations.ContainsKey($edge.toId)) {
        continue
    }

    $a = $layoutStations[$edge.fromId]
    $b = $layoutStations[$edge.toId]
    $dx = $a.X - $b.X
    $dy = $a.Y - $b.Y
    $distance = [Math]::Sqrt($dx * $dx + $dy * $dy)
    if ($distance -lt $minSpacing) {
        [void] $shortEdgeLines.Add("$($edge.family): $($edge.fromName) -> $($edge.toName), distance=$([Math]::Round($distance, 3))")
    }
}

if ($shortEdgeLines.Count -eq 0) {
    [void] $shortEdgeLines.Add('No adjacency edges below the diagnostic threshold.')
}
$shortEdgeLines | Set-Content -LiteralPath (Join-Path $outputPath 'schematic-layout-edge-check.txt') -Encoding UTF8

$summary = @(
    '# Schematic v2 Topology Summary',
    '',
    "Input JSON: $inputPath",
    "Stations: $(@($stations).Count)",
    "Raw lines: $(@($lines).Count)",
    "Display families: $($families.Count)",
    "Adjacency edges: $($edgeRows.Count)",
    "Shared corridor runs: $($sharedCorridorRows.Count)",
    "Geometry shared corridor runs: $($geometrySharedCorridorRows.Count)",
    "Schematic-v2 route guides: $($routeGuideRows.Count)",
    "Schematic-v2 parallel corridors: $($parallelCorridorRows.Count)",
    "Dense station pairs under ${minSpacing}px: $($denseRows.Count)",
    "Schematic-v2 adjusted stations in debug SVG: $(@($v2Stations.Values | Where-Object { $_.Adjusted -eq 'true' }).Count)",
    '',
    'Principle: A schematic map may distort geography, but it must not distort topology.',
    'Phase 5B.2 principle: A schematic map may distort geography, but it should preserve corridor topology.',
    'Phase 5B.3 principle: Stops define service order; pathPoints reveal physical corridor sharing.',
    '',
    'Generated files:',
    '- adjacency-edges.csv',
    '- station-degree.csv',
    '- dense-junctions.csv',
    '- schematic-layout-edge-check.txt',
    '- schematic-v2-family-topology-lines.csv',
    '- service-family-simplification.txt',
    '- service-family-simplification.csv',
    '- shared-corridors.txt',
    '- shared-corridors.csv',
    '- geometry-shared-corridors.txt',
    '- geometry-shared-corridors.csv',
    '- schematic-v2-route-guides.txt',
    '- schematic-v2-route-guides.csv',
    '- schematic-v2-parallel-corridors.txt',
    '- schematic-v2-parallel-corridors.csv',
    '- schematic-topology-debug.svg',
    '- schematic-v2-shared-corridor-debug.svg',
    '- geometry-shared-corridor-debug.svg'
)
$summary | Set-Content -LiteralPath (Join-Path $outputPath 'topology-summary.txt') -Encoding UTF8

Write-Host "Schematic v2 diagnostics written to: $outputPath"
