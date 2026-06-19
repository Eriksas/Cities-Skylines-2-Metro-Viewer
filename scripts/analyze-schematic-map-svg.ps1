[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputSvg,

    [string] $InputJson,

    [string] $OutputDir,

    [double] $OctilinearToleranceDegrees = 6.0,

    [double] $DirectionWarningDegrees = 50.0,

    [double] $MinimumSegmentLength = 12.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-Attr {
    param($Node, [string] $Name)

    if ($null -eq $Node -or $null -eq $Node.Attributes) {
        return ''
    }

    $attr = $Node.Attributes[$Name]
    if ($null -eq $attr) {
        return ''
    }

    return [string] $attr.Value
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

function Format-Number {
    param([double] $Value, [int] $Digits = 2)
    return $Value.ToString("F$Digits", [System.Globalization.CultureInfo]::InvariantCulture)
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

        $x = Convert-ToDouble $parts[0]
        $y = Convert-ToDouble $parts[1]
        $points.Add([pscustomobject]@{ X = $x; Y = $y }) | Out-Null
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

function Get-TextWidthEstimate {
    param([string] $Text, [double] $FontSize)

    if ([string]::IsNullOrEmpty($Text)) {
        return 0.0
    }

    $width = 0.0
    foreach ($ch in $Text.ToCharArray()) {
        if ([int] $ch -gt 255) {
            $width += $FontSize * 0.95
        }
        else {
            $width += $FontSize * 0.58
        }
    }

    return $width
}

function Test-BoxOverlap {
    param($A, $B, [double] $Padding = 0.0)
    return (($A.Left - $Padding) -lt ($B.Right + $Padding) -and
        ($A.Right + $Padding) -gt ($B.Left - $Padding) -and
        ($A.Top - $Padding) -lt ($B.Bottom + $Padding) -and
        ($A.Bottom + $Padding) -gt ($B.Top - $Padding))
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

function Get-ScoreRow {
    param(
        [string] $Category,
        [double] $Penalty,
        [int] $Count,
        [string] $Details
    )

    $score = [Math]::Max(0.0, 100.0 - $Penalty)
    return [pscustomobject]@{
        Category = $Category
        Score = Format-Number $score
        Penalty = Format-Number $Penalty
        Count = $Count
        Details = $Details
    }
}

function Get-StyleStrokeWidth {
    param([string] $Style)

    if ([string]::IsNullOrWhiteSpace($Style)) {
        return $null
    }

    $match = [regex]::Match($Style, 'stroke-width\s*:\s*([0-9.]+)')
    if (-not $match.Success) {
        return $null
    }

    return Convert-ToDouble $match.Groups[1].Value
}

function Get-CssRouteWidth {
    param([string] $SvgText)

    $match = [regex]::Match($SvgText, '\.route\s*\{[^}]*stroke-width\s*:\s*([0-9.]+)', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($match.Success) {
        return Convert-ToDouble $match.Groups[1].Value 14.0
    }

    return 14.0
}

function Escape-XmlText {
    param([string] $Text)

    if ($null -eq $Text) {
        return ''
    }

    return [System.Security.SecurityElement]::Escape($Text)
}

function Convert-PointText {
    param([string] $Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $parts = $Text -split ','
    if ($parts.Count -ne 2) {
        return $null
    }

    return [pscustomobject]@{
        X = Convert-ToDouble $parts[0]
        Y = Convert-ToDouble $parts[1]
    }
}

function Write-DebugOverlaySvg {
    param(
        [Parameter(Mandatory = $true)][string] $SvgText,
        [Parameter(Mandatory = $true)][string] $OutputPath,
        [object[]] $NonOctilinearRows,
        [object[]] $CrossingRows,
        [object[]] $SharpTurnRows
    )

    $overlay = New-Object System.Collections.Generic.List[string]
    $overlay.Add('<g id="schematic-map-debug-overlay" pointer-events="none" font-family="Arial, sans-serif">') | Out-Null
    $overlay.Add('<rect x="16" y="16" width="760" height="104" rx="10" fill="#fff7ed" stroke="#f97316" stroke-width="2" opacity="0.92" />') | Out-Null
    $overlay.Add('<text x="34" y="48" fill="#7c2d12" font-size="20" font-weight="700">Schematic map debug overlay</text>') | Out-Null
    $overlay.Add("<text x=`"34`" y=`"76`" fill=`"#7c2d12`" font-size=`"15`">Orange = non-octilinear route segment; red target = interior route crossing.</text>") | Out-Null
    $overlay.Add("<text x=`"34`" y=`"102`" fill=`"#7c2d12`" font-size=`"15`">Counts: non-octilinear=$($NonOctilinearRows.Count), crossings=$($CrossingRows.Count), sharp-turns=$($SharpTurnRows.Count).</text>") | Out-Null

    foreach ($row in @($NonOctilinearRows | Select-Object -First 120)) {
        $a = Convert-PointText $row.Start
        $b = Convert-PointText $row.End
        if ($null -eq $a -or $null -eq $b) {
            continue
        }

        $midX = ($a.X + $b.X) / 2.0
        $midY = ($a.Y + $b.Y) / 2.0
        $label = Escape-XmlText "$($row.Family) s$($row.SegmentIndex) d=$($row.OctilinearDelta)"
        $overlay.Add("<line x1=`"$(Format-Number $a.X)`" y1=`"$(Format-Number $a.Y)`" x2=`"$(Format-Number $b.X)`" y2=`"$(Format-Number $b.Y)`" stroke=`"#f97316`" stroke-width=`"9`" stroke-linecap=`"round`" opacity=`"0.38`" data-debug-type=`"non-octilinear`" data-debug-family=`"$(Escape-XmlText $row.Family)`" data-debug-segment=`"$($row.SegmentIndex)`" />") | Out-Null
        $overlay.Add("<circle cx=`"$(Format-Number $midX)`" cy=`"$(Format-Number $midY)`" r=`"9`" fill=`"#f97316`" opacity=`"0.75`" />") | Out-Null
        $overlay.Add("<text x=`"$(Format-Number ($midX + 12.0))`" y=`"$(Format-Number ($midY - 10.0))`" fill=`"#9a3412`" font-size=`"13`" font-weight=`"700`">$label</text>") | Out-Null
    }

    foreach ($row in @($CrossingRows | Select-Object -First 80)) {
        $x = Convert-ToDouble $row.X
        $y = Convert-ToDouble $row.Y
        $label = Escape-XmlText "$($row.FamilyA) s$($row.SegmentA) x $($row.FamilyB) s$($row.SegmentB)"
        $overlay.Add("<circle cx=`"$(Format-Number $x)`" cy=`"$(Format-Number $y)`" r=`"24`" fill=`"none`" stroke=`"#dc2626`" stroke-width=`"5`" opacity=`"0.92`" data-debug-type=`"interior-crossing`" />") | Out-Null
        $overlay.Add("<line x1=`"$(Format-Number ($x - 22.0))`" y1=`"$(Format-Number ($y - 22.0))`" x2=`"$(Format-Number ($x + 22.0))`" y2=`"$(Format-Number ($y + 22.0))`" stroke=`"#dc2626`" stroke-width=`"4`" opacity=`"0.92`" />") | Out-Null
        $overlay.Add("<line x1=`"$(Format-Number ($x - 22.0))`" y1=`"$(Format-Number ($y + 22.0))`" x2=`"$(Format-Number ($x + 22.0))`" y2=`"$(Format-Number ($y - 22.0))`" stroke=`"#dc2626`" stroke-width=`"4`" opacity=`"0.92`" />") | Out-Null
        $overlay.Add("<text x=`"$(Format-Number ($x + 30.0))`" y=`"$(Format-Number ($y - 28.0))`" fill=`"#991b1b`" font-size=`"14`" font-weight=`"700`">$label</text>") | Out-Null
    }

    $overlay.Add('</g>') | Out-Null

    $overlayText = $overlay -join "`n"
    $debugSvg = [regex]::Replace(
        $SvgText,
        '</svg>\s*$',
        "$overlayText`n</svg>",
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    $debugSvg | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}

function Get-JsonProperty {
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

function Load-ExportLookup {
    param([string] $JsonPath)

    $result = [pscustomobject]@{
        LinesById = @{}
        StationsById = @{}
        CityName = ''
    }

    if ([string]::IsNullOrWhiteSpace($JsonPath) -or -not (Test-Path -LiteralPath $JsonPath -PathType Leaf)) {
        return $result
    }

    $document = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $result.CityName = [string] (Get-JsonProperty (Get-JsonProperty $document 'city') 'name')

    foreach ($station in @(Get-JsonProperty (Get-JsonProperty $document 'network') 'stations')) {
        $id = [string] (Get-JsonProperty $station 'id')
        $position = Get-JsonProperty $station 'position'
        if ([string]::IsNullOrWhiteSpace($id) -or $null -eq $position) {
            continue
        }

        $result.StationsById[$id] = [pscustomobject]@{
            Id = $id
            Name = [string] (Get-JsonProperty $station 'name')
            X = Convert-ToDouble (Get-JsonProperty $position 'x')
            Y = Convert-ToDouble (Get-JsonProperty $position 'z')
        }
    }

    foreach ($line in @(Get-JsonProperty (Get-JsonProperty $document 'network') 'lines')) {
        $id = [string] (Get-JsonProperty $line 'id')
        if ([string]::IsNullOrWhiteSpace($id)) {
            continue
        }

        $result.LinesById[$id] = $line
    }

    return $result
}

$inputSvgPath = Get-FullPath $InputSvg
if (-not (Test-Path -LiteralPath $inputSvgPath -PathType Leaf)) {
    Write-Host "ERROR: SVG not found: $inputSvgPath" -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Split-Path -Parent $inputSvgPath
}

$outputPath = Get-FullPath $OutputDir
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$svgText = Get-Content -LiteralPath $inputSvgPath -Raw -Encoding UTF8
$svgDoc = New-Object System.Xml.XmlDocument
$svgDoc.PreserveWhitespace = $false
$svgDoc.LoadXml($svgText)

$ns = New-Object System.Xml.XmlNamespaceManager($svgDoc.NameTable)
$ns.AddNamespace('svg', 'http://www.w3.org/2000/svg')

$lookup = Load-ExportLookup $InputJson
$defaultRouteWidth = Get-CssRouteWidth $svgText

$routeRows = New-Object System.Collections.Generic.List[object]
$routeSegmentGeometryRows = New-Object System.Collections.Generic.List[object]
$styleRows = New-Object System.Collections.Generic.List[object]
$corridorRows = New-Object System.Collections.Generic.List[object]
$labelBoxes = New-Object System.Collections.Generic.List[object]
$badgeBoxes = New-Object System.Collections.Generic.List[object]
$conflictRows = New-Object System.Collections.Generic.List[object]
$crossingRows = New-Object System.Collections.Generic.List[object]
$turnRows = New-Object System.Collections.Generic.List[object]

$routeNodes = $svgDoc.SelectNodes('//svg:polyline[@points]', $ns)
foreach ($node in $routeNodes) {
    $class = Get-Attr $node 'class'
    $family = Get-Attr $node 'data-display-family-key'
    $lineId = Get-Attr $node 'data-line-id'
    $syntheticBends = Get-Attr $node 'data-schematic-map-synthetic-bends'
    $points = @(Parse-Points (Get-Attr $node 'points'))
    $styleWidth = Get-StyleStrokeWidth (Get-Attr $node 'style')
    $width = if ($null -ne $styleWidth) { [double] $styleWidth } elseif ($class -eq 'route') { $defaultRouteWidth } else { 0.0 }

    if ($class -match 'route|parallel-corridor|express') {
        $styleRows.Add([pscustomobject]@{
            Layer = $class
            Family = $family
            LineId = $lineId
            Stroke = Get-Attr $node 'stroke'
            StrokeWidth = Format-Number $width
            PointCount = $points.Count
            SyntheticBends = $syntheticBends
        }) | Out-Null
    }

    if ($class -eq 'schematic-v2-parallel-corridor') {
        $corridorRows.Add([pscustomobject]@{
            RunId = Get-Attr $node 'data-schematic-v2-shared-corridor-run-id'
            Source = Get-Attr $node 'data-schematic-v2-parallel-corridor-source'
            Family = $family
            Families = Get-Attr $node 'data-schematic-v2-shared-corridor-families'
            VisibleLaneKey = Get-Attr $node 'data-schematic-v2-visible-lane-key'
            VisibleLaneFamilyCount = Get-Attr $node 'data-schematic-v2-visible-lane-family-count'
            Offset = Get-Attr $node 'data-schematic-v2-parallel-offset'
            StrokeWidth = Format-Number $width
            PointCount = $points.Count
        }) | Out-Null
    }

    if ($class -ne 'route') {
        continue
    }

    $line = $null
    $stops = @()
    if (-not [string]::IsNullOrWhiteSpace($lineId) -and $lookup.LinesById.ContainsKey($lineId)) {
        $line = $lookup.LinesById[$lineId]
        $stops = @(Get-JsonProperty $line 'stops')
    }

    for ($i = 1; $i -lt $points.Count; $i++) {
        $a = $points[$i - 1]
        $b = $points[$i]
        $length = Get-Distance $a $b
        if ($length -lt 0.0001) {
            continue
        }

        $angle = Get-AngleDegrees $a $b
        $octDelta = Get-OctilinearDelta $angle
        $sourceAngle = ''
        $sourceDelta = ''
        $sourceWarning = ''
        $sourceNote = ''

        if ($stops.Count -eq $points.Count -and $i -lt $stops.Count) {
            $stopA = [string] $stops[$i - 1]
            $stopB = [string] $stops[$i]
            if ($lookup.StationsById.ContainsKey($stopA) -and $lookup.StationsById.ContainsKey($stopB)) {
                $rawA = $lookup.StationsById[$stopA]
                $rawB = $lookup.StationsById[$stopB]
                $rawAngle = Get-AngleDegrees $rawA $rawB
                $delta = Get-AngleDelta $angle $rawAngle
                $sourceAngle = Format-Number $rawAngle
                $sourceDelta = Format-Number $delta
                if ($delta -gt $DirectionWarningDegrees) {
                    $sourceWarning = 'direction-diverges-from-export'
                }
            }
        }
        elseif ($points.Count -ne $stops.Count -and $stops.Count -gt 0) {
            $sourceNote = "render-point-count-differs-from-stop-count:$($points.Count)-vs-$($stops.Count)"
        }

        $warning = @()
        if ($length -lt $MinimumSegmentLength) {
            $warning += 'short-segment'
        }

        if ($octDelta -gt $OctilinearToleranceDegrees) {
            $warning += 'non-octilinear'
        }

        if (-not [string]::IsNullOrWhiteSpace($sourceWarning)) {
            $warning += $sourceWarning
        }

        $routeSegmentGeometryRows.Add([pscustomobject]@{
            Family = $family
            LineId = $lineId
            SegmentIndex = $i - 1
            A = $a
            B = $b
            Length = $length
            Angle = $angle
            OctilinearDelta = $octDelta
        }) | Out-Null

        $routeRows.Add([pscustomobject]@{
            Family = $family
            LineId = $lineId
            SegmentIndex = $i - 1
            Length = Format-Number $length
            Angle = Format-Number $angle
            OctilinearDelta = Format-Number $octDelta
            SourceAngle = $sourceAngle
            SourceAngleDelta = $sourceDelta
            Start = "$(Format-Number $a.X),$(Format-Number $a.Y)"
            End = "$(Format-Number $b.X),$(Format-Number $b.Y)"
            Warning = ($warning -join ';')
            Note = $sourceNote
            SyntheticBends = $syntheticBends
        }) | Out-Null
    }
}

for ($i = 0; $i -lt $routeSegmentGeometryRows.Count; $i++) {
    for ($j = $i + 1; $j -lt $routeSegmentGeometryRows.Count; $j++) {
        $a = $routeSegmentGeometryRows[$i]
        $b = $routeSegmentGeometryRows[$j]
        if ($a.Family -eq $b.Family) {
            continue
        }

        $intersection = Get-SegmentIntersection $a.A $a.B $b.A $b.B
        if ($null -eq $intersection) {
            continue
        }

        $crossingRows.Add([pscustomobject]@{
            FamilyA = $a.Family
            SegmentA = $a.SegmentIndex
            FamilyB = $b.Family
            SegmentB = $b.SegmentIndex
            X = Format-Number $intersection.X
            Y = Format-Number $intersection.Y
            Detail = 'interior-route-crossing'
        }) | Out-Null
    }
}

$segmentsByLine = @($routeSegmentGeometryRows | Group-Object LineId, Family)
foreach ($group in $segmentsByLine) {
    $segments = @($group.Group | Sort-Object SegmentIndex)
    for ($i = 1; $i -lt $segments.Count; $i++) {
        $previous = $segments[$i - 1]
        $current = $segments[$i]
        $turn = Get-AngleDelta $previous.Angle $current.Angle
        if ($turn -gt 100.0) {
            $turnRows.Add([pscustomobject]@{
                Family = $current.Family
                LineId = $current.LineId
                AtSegment = $current.SegmentIndex
                TurnDegrees = Format-Number $turn
                PreviousAngle = Format-Number $previous.Angle
                CurrentAngle = Format-Number $current.Angle
                Detail = 'sharp-turn-candidate'
            }) | Out-Null
        }
    }
}

$badgeNodes = $svgDoc.SelectNodes('//svg:g[contains(concat(" ", normalize-space(@class), " "), " route-badge ")]', $ns)
foreach ($node in $badgeNodes) {
    $rect = $node.SelectSingleNode('svg:rect', $ns)
    if ($null -eq $rect) {
        continue
    }

    $x = Convert-ToDouble (Get-Attr $rect 'x')
    $y = Convert-ToDouble (Get-Attr $rect 'y')
    $w = Convert-ToDouble (Get-Attr $rect 'width')
    $h = Convert-ToDouble (Get-Attr $rect 'height')
    $badgeBoxes.Add([pscustomobject]@{
        Type = 'badge'
        Id = "$(Get-Attr $node 'data-display-family-key')[$(Get-Attr $node 'data-route-badge-index')]"
        Family = Get-Attr $node 'data-display-family-key'
        Left = $x
        Top = $y
        Right = $x + $w
        Bottom = $y + $h
    }) | Out-Null
}

$labelNodes = $svgDoc.SelectNodes('//svg:text[contains(concat(" ", normalize-space(@class), " "), " station-label ") and not(contains(concat(" ", normalize-space(@class), " "), " station-label-halo "))]', $ns)
foreach ($node in $labelNodes) {
    $text = [string] $node.InnerText
    $x = Convert-ToDouble (Get-Attr $node 'x')
    $y = Convert-ToDouble (Get-Attr $node 'y')
    $fontSize = 14.0
    $width = Get-TextWidthEstimate $text $fontSize
    $height = $fontSize * 1.25
    $labelBoxes.Add([pscustomobject]@{
        Type = 'label'
        Id = Get-Attr $node 'data-station-id'
        Text = $text
        Left = $x
        Top = $y - $height
        Right = $x + $width
        Bottom = $y + ($height * 0.25)
    }) | Out-Null
}

for ($i = 0; $i -lt $badgeBoxes.Count; $i++) {
    for ($j = $i + 1; $j -lt $badgeBoxes.Count; $j++) {
        if (Test-BoxOverlap $badgeBoxes[$i] $badgeBoxes[$j] 4.0) {
            $conflictRows.Add([pscustomobject]@{
                Type = 'badge-badge'
                A = $badgeBoxes[$i].Id
                B = $badgeBoxes[$j].Id
                Detail = 'estimated bbox overlap'
            }) | Out-Null
        }
    }
}

foreach ($badge in $badgeBoxes) {
    foreach ($label in $labelBoxes) {
        if (Test-BoxOverlap $badge $label 3.0) {
            $conflictRows.Add([pscustomobject]@{
                Type = 'badge-label'
                A = $badge.Id
                B = $label.Text
                Detail = $label.Id
            }) | Out-Null
        }
    }
}

$segmentCsv = Join-Path $outputPath 'schematic-map-route-segments.csv'
$conflictCsv = Join-Path $outputPath 'schematic-map-layout-conflicts.csv'
$styleCsv = Join-Path $outputPath 'schematic-map-style-widths.csv'
$corridorCsv = Join-Path $outputPath 'schematic-map-parallel-corridors.csv'
$crossingCsv = Join-Path $outputPath 'schematic-map-crossings.csv'
$turnCsv = Join-Path $outputPath 'schematic-map-turns.csv'
$scoreCsv = Join-Path $outputPath 'schematic-map-score.csv'
$debugSvgPath = Join-Path $outputPath 'schematic-map-debug.svg'
$summaryPath = Join-Path $outputPath 'schematic-map-audit.txt'

$routeRows | Export-Csv -LiteralPath $segmentCsv -NoTypeInformation -Encoding UTF8
$conflictRows | Export-Csv -LiteralPath $conflictCsv -NoTypeInformation -Encoding UTF8
$styleRows | Export-Csv -LiteralPath $styleCsv -NoTypeInformation -Encoding UTF8
$corridorRows | Export-Csv -LiteralPath $corridorCsv -NoTypeInformation -Encoding UTF8
$crossingRows | Export-Csv -LiteralPath $crossingCsv -NoTypeInformation -Encoding UTF8
$turnRows | Export-Csv -LiteralPath $turnCsv -NoTypeInformation -Encoding UTF8

$routeWarnings = @($routeRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Warning) })
$nonOctilinear = @($routeRows | Where-Object { $_.Warning -match 'non-octilinear' })
$directionWarnings = @($routeRows | Where-Object { $_.Warning -match 'direction-diverges-from-export' })
$shortSegments = @($routeRows | Where-Object { $_.Warning -match 'short-segment' })
$routeNotes = @($routeRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Note) })
$badgeConflicts = @($conflictRows | Where-Object { $_.Type -eq 'badge-badge' })
$badgeLabelConflicts = @($conflictRows | Where-Object { $_.Type -eq 'badge-label' })
$widthGroups = @($styleRows | Where-Object { $_.StrokeWidth -ne '0.00' } | Group-Object Layer, StrokeWidth)
$routeWidthGroups = @($styleRows | Where-Object { $_.Layer -eq 'route' -and $_.StrokeWidth -ne '0.00' } | Group-Object StrokeWidth)
$syntheticBendTotal = 0
foreach ($styleRow in $styleRows) {
    $parsedBends = 0
    if ([int]::TryParse([string] $styleRow.SyntheticBends, [ref] $parsedBends)) {
        $syntheticBendTotal += $parsedBends
    }
}

$octilinearPenalty = 0.0
foreach ($row in $nonOctilinear) {
    $delta = Convert-ToDouble $row.OctilinearDelta
    $length = Convert-ToDouble $row.Length
    $excess = [Math]::Max(0.0, $delta - $OctilinearToleranceDegrees)
    $octilinearPenalty += [Math]::Min(3.0, ($excess * 0.12) + ($length / 2500.0))
}
$octilinearPenalty = [Math]::Min(40.0, $octilinearPenalty)

$directionPenalty = [Math]::Min(25.0, $directionWarnings.Count * 10.0)
$shortPenalty = [Math]::Min(20.0, $shortSegments.Count * 5.0)
$badgePenalty = [Math]::Min(25.0, ($badgeConflicts.Count * 8.0) + ($badgeLabelConflicts.Count * 10.0))
$crossingPenalty = [Math]::Min(30.0, $crossingRows.Count * 6.0)
$turnPenalty = [Math]::Min(20.0, $turnRows.Count * 4.0)
$widthPenalty = [Math]::Min(20.0, [Math]::Max(0, $routeWidthGroups.Count - 1) * 8.0)
$totalPenalty = [Math]::Min(100.0, $octilinearPenalty + $directionPenalty + $shortPenalty + $badgePenalty + $crossingPenalty + $turnPenalty + $widthPenalty)
$layoutScore = [Math]::Max(0.0, 100.0 - $totalPenalty)

$scoreRows = @(
    (Get-ScoreRow 'overall' $totalPenalty $routeRows.Count 'Weighted score; visual review still takes precedence.'),
    (Get-ScoreRow 'octilinear-grammar' $octilinearPenalty $nonOctilinear.Count 'Penalizes route segments far from horizontal, vertical, or 45-degree directions.'),
    (Get-ScoreRow 'direction-fidelity' $directionPenalty $directionWarnings.Count 'Penalizes route directions that diverge from source station order when comparable.'),
    (Get-ScoreRow 'short-segments' $shortPenalty $shortSegments.Count 'Penalizes tiny route fragments that can create jitter or visual noise.'),
    (Get-ScoreRow 'badge-layout' $badgePenalty ($badgeConflicts.Count + $badgeLabelConflicts.Count) 'Penalizes route badge overlaps with badges or station labels.'),
    (Get-ScoreRow 'route-crossings' $crossingPenalty $crossingRows.Count 'Penalizes interior route crossings; station crossings are excluded by endpoint filtering.'),
    (Get-ScoreRow 'sharp-turns' $turnPenalty $turnRows.Count 'Penalizes very sharp route turns likely to look less official-map-like.'),
    (Get-ScoreRow 'stroke-width-consistency' $widthPenalty $routeWidthGroups.Count 'Penalizes inconsistent normal route stroke widths.')
)
$scoreRows | Export-Csv -LiteralPath $scoreCsv -NoTypeInformation -Encoding UTF8

Write-DebugOverlaySvg `
    -SvgText $svgText `
    -OutputPath $debugSvgPath `
    -NonOctilinearRows $nonOctilinear `
    -CrossingRows $crossingRows `
    -SharpTurnRows $turnRows

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add('# Schematic Map Audit') | Out-Null
$summary.Add('') | Out-Null
$summary.Add("- Input SVG: $inputSvgPath") | Out-Null
if (-not [string]::IsNullOrWhiteSpace($InputJson)) {
    $summary.Add("- Input JSON: $(Get-FullPath $InputJson)") | Out-Null
}
$summary.Add("- City: $($lookup.CityName)") | Out-Null
$summary.Add("- Route segments checked: $($routeRows.Count)") | Out-Null
$summary.Add("- Route warnings: $($routeWarnings.Count)") | Out-Null
$summary.Add("- Non-octilinear segments: $($nonOctilinear.Count)") | Out-Null
$summary.Add("- Direction divergence warnings: $($directionWarnings.Count)") | Out-Null
$summary.Add("- Short segments: $($shortSegments.Count)") | Out-Null
$summary.Add("- Informational route notes: $($routeNotes.Count)") | Out-Null
$summary.Add("- Parallel corridor elements: $($corridorRows.Count)") | Out-Null
$summary.Add("- Schematic-map synthetic bends: $syntheticBendTotal") | Out-Null
$summary.Add("- Badge-badge conflicts: $($badgeConflicts.Count)") | Out-Null
$summary.Add("- Badge-label conflicts: $($badgeLabelConflicts.Count)") | Out-Null
$summary.Add("- Interior route crossings: $($crossingRows.Count)") | Out-Null
$summary.Add("- Sharp turn candidates: $($turnRows.Count)") | Out-Null
$summary.Add("- Layout score: $(Format-Number $layoutScore) / 100") | Out-Null
$summary.Add("- Debug overlay SVG: $debugSvgPath") | Out-Null
$summary.Add('') | Out-Null
$summary.Add('## Score Breakdown') | Out-Null
foreach ($row in $scoreRows) {
    $summary.Add("- $($row.Category): score=$($row.Score), penalty=$($row.Penalty), count=$($row.Count)") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Width Tokens') | Out-Null
foreach ($group in $widthGroups) {
    $summary.Add("- $($group.Name): $($group.Count) elements") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Highest Priority Route Warnings') | Out-Null
foreach ($row in @($routeWarnings | Select-Object -First 20)) {
    $summary.Add("- $($row.Family) segment $($row.SegmentIndex): length=$($row.Length), angle=$($row.Angle), octDelta=$($row.OctilinearDelta), sourceDelta=$($row.SourceAngleDelta), warning=$($row.Warning)") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Informational Route Notes') | Out-Null
foreach ($row in @($routeNotes | Select-Object -First 12)) {
    $summary.Add("- $($row.Family): $($row.Note)") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Layout Conflicts') | Out-Null
foreach ($row in @($conflictRows | Select-Object -First 30)) {
    $summary.Add("- $($row.Type): $($row.A) vs $($row.B) ($($row.Detail))") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Route Crossings') | Out-Null
foreach ($row in @($crossingRows | Select-Object -First 20)) {
    $summary.Add("- $($row.FamilyA) segment $($row.SegmentA) crosses $($row.FamilyB) segment $($row.SegmentB) at $($row.X),$($row.Y)") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Sharp Turn Candidates') | Out-Null
foreach ($row in @($turnRows | Select-Object -First 20)) {
    $summary.Add("- $($row.Family) segment $($row.AtSegment): turn=$($row.TurnDegrees), previous=$($row.PreviousAngle), current=$($row.CurrentAngle)") | Out-Null
}
$summary.Add('') | Out-Null
$summary.Add('## Parallel Corridors') | Out-Null
foreach ($row in @($corridorRows | Select-Object -First 30)) {
    $summary.Add("- $($row.RunId): family=$($row.Family), families=$($row.Families), offset=$($row.Offset), points=$($row.PointCount), source=$($row.Source)") | Out-Null
}

$summary | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "Schematic map audit written to: $summaryPath"
Write-Host "Route segment CSV: $segmentCsv"
Write-Host "Layout conflict CSV: $conflictCsv"
Write-Host "Style width CSV: $styleCsv"
Write-Host "Parallel corridor CSV: $corridorCsv"
Write-Host "Crossing CSV: $crossingCsv"
Write-Host "Turn CSV: $turnCsv"
Write-Host "Score CSV: $scoreCsv"
Write-Host "Debug overlay SVG: $debugSvgPath"
