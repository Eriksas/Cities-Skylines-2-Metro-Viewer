using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed class MetroSvgRenderer
{
    private const double PathPointDuplicateEpsilon = 0.001;

    public SvgRenderResult Render(MetroExportDocument document, SvgRenderOptions? options = null)
    {
        options ??= new SvgRenderOptions();
        options = ApplyLayoutPresentationDefaults(options);
        List<string> warnings = [];

        MetroNetwork network = document.Network ?? new MetroNetwork();
        List<MetroStation> stations = network.Stations ?? [];
        List<MetroLine> lines = network.Lines ?? [];
        Dictionary<string, MetroStation> stationsById = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .GroupBy(station => station.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        List<DisplayLineFamily> displayFamilies = DisplayLineFamilyResolver.Resolve(lines, stationsById, options.EnableServiceFamilyMerge);
        options = ApplyNetworkPresentationDefaults(options, displayFamilies, stations.Count);
        CanonicalSchematicNetwork? canonicalNetwork = IsSchematicV2FamilyLayout(options.LayoutMode)
            ? CanonicalSchematicNetworkBuilder.Build(document, options.EnableServiceFamilyMerge)
            : null;

        bool hasLegend = displayFamilies.Count > 0;
        RenderGeometry geometry = CreateRenderGeometry(stations, lines, displayFamilies, canonicalNetwork, stationsById, options, hasLegend, warnings);
        geometry = ApplyLayoutOverridesToGeometry(geometry, options, warnings);
        StationRouteAnchorMap stationAnchors = ResolveStationRouteAnchors(stations, displayFamilies, stationsById, geometry, options, warnings);
        Dictionary<string, SvgPoint> stationPoints = stationAnchors.Points;
        HashSet<string> terminalStationIds = GetTerminalStationIds(lines, stationsById);
        StringBuilder svg = new();

        AppendHeader(svg, document, options);
        AppendEmptyNotice(svg, stations, lines, options);
        AppendRoutes(svg, stations, displayFamilies, stationsById, stationPoints, terminalStationIds, geometry, options, hasLegend, warnings);
        AppendVirtualTransferHints(svg, stations, stationPoints, options);
        AppendStations(svg, stations, stationPoints, stationAnchors.Anchors, geometry.SchematicStationAdjustments, geometry.SchematicV2DenseStationPairs, terminalStationIds, options);
        AppendLabels(svg, stations, stationPoints, stationAnchors.Anchors, geometry.SchematicStationAdjustments, terminalStationIds, options, hasLegend);
        AppendLegend(svg, SortFamiliesForLegend(displayFamilies), options, hasLegend);
        AppendFooter(svg, options);

        return new SvgRenderResult(svg.ToString(), warnings);
    }

    private static RenderGeometry CreateRenderGeometry(
        List<MetroStation> stations,
        List<MetroLine> lines,
        List<DisplayLineFamily> displayFamilies,
        CanonicalSchematicNetwork? canonicalNetwork,
        Dictionary<string, MetroStation> stationsById,
        SvgRenderOptions options,
        bool reserveLegendSpace,
        List<string> warnings)
    {
        List<SourceStationPoint> sourcePoints = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && station.Position is not null)
            .Select(station => new SourceStationPoint(station.Id!, station.Position!.X, station.Position.Z))
            .ToList();

        List<SourceCoordinate> sourceCoordinates = sourcePoints
            .Select(point => new SourceCoordinate(point.X, point.Z))
            .ToList();

        if (options.LayoutMode == SvgLayoutMode.Geographic && options.UsePathPoints)
        {
            sourceCoordinates.AddRange(lines
                .SelectMany(line => GetRenderablePathPoints(line, options))
                .Select(point => new SourceCoordinate(point.X, point.Z)));
        }

        CoordinateProjector? projector = CoordinateProjector.Create(sourceCoordinates, options, reserveLegendSpace);
        if (projector is null)
        {
            return new RenderGeometry([], null, [], [], null, null);
        }

        Dictionary<string, SvgPoint> points = new(StringComparer.Ordinal);
        foreach (SourceStationPoint station in sourcePoints)
        {
            if (points.ContainsKey(station.Id))
            {
                continue;
            }

            points[station.Id] = projector.Project(station.X, station.Z);
        }

        if (options.LayoutMode == SvgLayoutMode.SchematicLite)
        {
            SchematicLayoutResult schematicLayout = ApplySchematicLiteLayout(points, lines, stationsById, options, reserveLegendSpace, warnings);
            return new RenderGeometry(schematicLayout.Points, projector, schematicLayout.Adjustments, [], null, null);
        }

        if (IsSchematicV2FamilyLayout(options.LayoutMode))
        {
            SvgRenderOptions canonicalOptions = CreateSchematicV2CanonicalOptions(options);
            CoordinateProjector canonicalProjector = CoordinateProjector.Create(sourceCoordinates, canonicalOptions, reserveLegendSpace) ?? projector;
            Dictionary<string, SvgPoint> canonicalPoints = new(StringComparer.Ordinal);
            foreach (SourceStationPoint station in sourcePoints)
            {
                if (canonicalPoints.ContainsKey(station.Id))
                {
                    continue;
                }

                canonicalPoints[station.Id] = canonicalProjector.Project(station.X, station.Z);
            }

            SchematicLayoutResult canonicalLayout = ApplySchematicV2Layout(
                canonicalPoints,
                canonicalProjector,
                canonicalPoints,
                canonicalProjector,
                displayFamilies,
                canonicalNetwork,
                stationsById,
                canonicalOptions,
                reserveLegendSpace,
                warnings);
            SchematicLayoutResult targetLayout = ScaleSchematicV2LayoutToTarget(canonicalLayout, canonicalOptions, options, reserveLegendSpace);
            return new RenderGeometry(targetLayout.Points, projector, targetLayout.Adjustments, targetLayout.DenseStationPairs, targetLayout.RouteGuideByFamily, targetLayout.RouteGuideMetadataByFamily);
        }

        return new RenderGeometry(points, projector, [], [], null, null);
    }

    private static RenderGeometry ApplyLayoutOverridesToGeometry(
        RenderGeometry geometry,
        SvgRenderOptions options,
        List<string> warnings)
    {
        LayoutOverrideDocument? overrides = options.LayoutOverrides;
        if (overrides is null || overrides.Stations.Count == 0)
        {
            return geometry;
        }

        Dictionary<string, SvgPoint> points = new(geometry.StationPoints, StringComparer.Ordinal);
        Dictionary<string, SchematicStationAdjustment> adjustments = new(geometry.SchematicStationAdjustments, StringComparer.Ordinal);
        int appliedCount = 0;
        int skippedCount = 0;

        foreach ((string stationId, StationLayoutOverride stationOverride) in overrides.Stations)
        {
            if (string.IsNullOrWhiteSpace(stationId) || !stationOverride.Enabled)
            {
                continue;
            }

            if (!points.TryGetValue(stationId, out SvgPoint current))
            {
                skippedCount++;
                continue;
            }

            SvgPoint adjusted = ApplyPointOverride(current, stationOverride.X, stationOverride.Y, stationOverride.Dx, stationOverride.Dy);
            if (Distance(current, adjusted) <= 0.001)
            {
                continue;
            }

            SvgPoint original = adjustments.TryGetValue(stationId, out SchematicStationAdjustment existingAdjustment)
                ? existingAdjustment.OriginalPoint
                : current;
            points[stationId] = adjusted;
            adjustments[stationId] = new SchematicStationAdjustment(
                stationId,
                original,
                adjusted,
                Distance(original, adjusted),
                "layout-override");
            appliedCount++;
        }

        if (appliedCount > 0 || skippedCount > 0)
        {
            warnings.Add($"Layout overrides: applied station overrides: {appliedCount}; skipped station overrides: {skippedCount}.");
        }

        return geometry with
        {
            StationPoints = points,
            SchematicStationAdjustments = adjustments
        };
    }

    private static SvgPoint ApplyPointOverride(
        SvgPoint current,
        double? x,
        double? y,
        double? dx,
        double? dy)
    {
        double adjustedX = x ?? current.X;
        double adjustedY = y ?? current.Y;
        adjustedX += dx ?? 0;
        adjustedY += dy ?? 0;
        return new SvgPoint(adjustedX, adjustedY);
    }

    private static SvgRenderOptions ApplyLayoutPresentationDefaults(SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap)
        {
            return options;
        }

        return new SvgRenderOptions
        {
            LayoutMode = SvgLayoutMode.SchematicMap,
            MapStyle = SvgMapStyle.TransitMap,
            Width = options.Width,
            Height = options.Height,
            Padding = options.Padding,
            Margin = options.Margin,
            LegendWidth = options.LegendWidth,
            LegendGap = options.LegendGap,
            LineWidth = options.LineWidth,
            StationRadius = Math.Max(options.StationRadius, 6.6),
            InterchangeStationRadius = Math.Max(options.InterchangeStationRadius, 11.0),
            LabelFontSize = Math.Max(options.LabelFontSize, 14),
            LegendLabelFontSize = Math.Max(options.LegendLabelFontSize, 18),
            LabelGap = Math.Max(options.LabelGap, 15),
            EnableCenterExpansion = options.EnableCenterExpansion,
            CenterExpansionStrength = options.CenterExpansionStrength,
            GridSize = options.GridSize,
            HideGenericStationLabels = options.HideGenericStationLabels,
            EnableVirtualTransferHints = options.EnableVirtualTransferHints,
            HideCrowdedLabels = options.HideCrowdedLabels,
            AlwaysShowInterchanges = options.AlwaysShowInterchanges,
            AlwaysShowTerminals = options.AlwaysShowTerminals,
            UsePathPoints = true,
            PathPointSimplificationEnabled = options.PathPointSimplificationEnabled,
            PathPointSimplificationTolerance = options.PathPointSimplificationTolerance,
            MinPathSegmentLength = options.MinPathSegmentLength,
            AdaptivePathPointSimplificationEnabled = options.AdaptivePathPointSimplificationEnabled,
            EnableParallelCorridorOffset = options.EnableParallelCorridorOffset,
            EnableServiceFamilyMerge = true,
            EnableSharedCorridorCompositeStroke = options.EnableSharedCorridorCompositeStroke,
            EnableExpressCenterStripe = true,
            LayoutOverrides = options.LayoutOverrides,
            EnableStationRouteAnchoring = options.EnableStationRouteAnchoring,
            StationRouteAnchorMaxDistance = options.StationRouteAnchorMaxDistance,
            StationRouteAnchorMultiFamilyMaxSpread = options.StationRouteAnchorMultiFamilyMaxSpread,
            EnableSchematicSegmentOverlapResolver = options.EnableSchematicSegmentOverlapResolver,
            SchematicSegmentOverlapOffsetDistance = options.SchematicSegmentOverlapOffsetDistance,
            SchematicOverlapEndpointTrim = options.SchematicOverlapEndpointTrim,
            SchematicShortOverlapSegmentThreshold = options.SchematicShortOverlapSegmentThreshold,
            SchematicMinimumStationSpacing = options.SchematicMinimumStationSpacing,
            CompactTransitMapFrame = options.CompactTransitMapFrame,
            EnableSchematicMapOctilinearNormalization = true,
            SchematicMapOctilinearSnapAngleDegrees = options.SchematicMapOctilinearSnapAngleDegrees,
            EnableSchematicMapSimpleRunLinearization = true,
            SchematicMapPreferredStationSpacing = options.SchematicMapPreferredStationSpacing,
            EnableSchematicMapLocalClearance = true,
            SchematicMapLocalClearanceDistance = options.SchematicMapLocalClearanceDistance,
            EnableSchematicMapSyntheticBends = true,
            SchematicMapSyntheticBendMinimumLength = Math.Min(options.SchematicMapSyntheticBendMinimumLength, 140)
        };
    }

    private static SvgRenderOptions ApplyNetworkPresentationDefaults(
        SvgRenderOptions options,
        IReadOnlyList<DisplayLineFamily> displayFamilies,
        int stationCount)
    {
        if (!IsSchematicMapLayout(options))
        {
            return options;
        }

        options.CompactTransitMapFrame = ShouldUseCompactTransitMapFrame(displayFamilies, stationCount);
        return options;
    }

    private static bool ShouldUseCompactTransitMapFrame(IReadOnlyList<DisplayLineFamily> displayFamilies, int stationCount)
    {
        return displayFamilies.Count is > 0 and <= 4
            && stationCount <= 40
            && !displayFamilies.Any(family => family.Variants.Count > 1);
    }

    private static bool IsSchematicV2FamilyLayout(SvgLayoutMode layoutMode)
    {
        return layoutMode is SvgLayoutMode.SchematicV2 or SvgLayoutMode.SchematicMap;
    }

    private static StationRouteAnchorMap ResolveStationRouteAnchors(
        List<MetroStation> stations,
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options,
        List<string> warnings)
    {
        Dictionary<string, StationRenderAnchor> anchors = [];
        foreach (MetroStation station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id) || !geometry.StationPoints.TryGetValue(station.Id!, out SvgPoint rawPoint))
            {
                continue;
            }

            anchors[station.Id!] = new StationRenderAnchor(
                station.Id!,
                rawPoint,
                rawPoint,
                false,
                "raw",
                0,
                [],
                options.LayoutMode == SvgLayoutMode.SchematicLite ? "schematic-lite" : null);
        }

        if (!options.EnableStationRouteAnchoring
            || options.LayoutMode != SvgLayoutMode.Geographic
            || !options.UsePathPoints
            || geometry.Projector is null
            || anchors.Count == 0)
        {
            return CreateStationRouteAnchorMap(anchors);
        }

        Dictionary<string, List<DisplayLineFamily>> familiesByStationId = BuildStationFamilies(stations, displayFamilies);
        Dictionary<string, RoutePointSet> routePointsByFamilyKey = [];

        foreach (MetroStation station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id)
                || !anchors.TryGetValue(station.Id!, out StationRenderAnchor rawAnchor)
                || !familiesByStationId.TryGetValue(station.Id!, out List<DisplayLineFamily>? relatedFamilies)
                || relatedFamilies.Count == 0)
            {
                continue;
            }

            List<StationRouteAnchorCandidate> candidates = [];
            foreach (DisplayLineFamily family in relatedFamilies)
            {
                if (!routePointsByFamilyKey.TryGetValue(family.FamilyKey, out RoutePointSet routePointSet))
                {
                    routePointSet = CreateRoutePoints(family, family.PrimaryLine, stationsById, geometry, options, warnings);
                    routePointsByFamilyKey[family.FamilyKey] = routePointSet;
                }

                if (routePointSet.Source != "pathPoints")
                {
                    continue;
                }

                if (TryFindNearestPointOnPolylines(rawAnchor.RawPoint, routePointSet.Polylines, out SvgPoint anchorPoint, out double distance))
                {
                    candidates.Add(new StationRouteAnchorCandidate(family.FamilyKey, anchorPoint, distance));
                }
            }

            anchors[station.Id!] = ResolveStationAnchor(rawAnchor, candidates, options);
        }

        return CreateStationRouteAnchorMap(anchors);
    }

    private static Dictionary<string, List<DisplayLineFamily>> BuildStationFamilies(List<MetroStation> stations, List<DisplayLineFamily> displayFamilies)
    {
        Dictionary<string, List<DisplayLineFamily>> familiesByStationId = [];
        Dictionary<string, List<DisplayLineFamily>> familiesByLineId = [];
        foreach (DisplayLineFamily family in displayFamilies)
        {
            foreach (DisplayServiceVariant variant in family.Variants)
            {
                if (!string.IsNullOrWhiteSpace(variant.LineId))
                {
                    AddFamily(familiesByLineId, variant.LineId, family);
                }

                foreach (string stationId in variant.Stops)
                {
                    AddFamily(familiesByStationId, stationId, family);
                }
            }
        }

        foreach (MetroStation station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id))
            {
                continue;
            }

            foreach (string lineId in station.Lines ?? [])
            {
                if (familiesByLineId.TryGetValue(lineId, out List<DisplayLineFamily>? lineFamilies))
                {
                    foreach (DisplayLineFamily family in lineFamilies)
                    {
                        AddFamily(familiesByStationId, station.Id!, family);
                    }
                }
            }
        }

        return familiesByStationId;
    }

    private static void AddFamily(Dictionary<string, List<DisplayLineFamily>> lookup, string key, DisplayLineFamily family)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!lookup.TryGetValue(key, out List<DisplayLineFamily>? families))
        {
            families = [];
            lookup[key] = families;
        }

        if (!families.Any(existing => string.Equals(existing.FamilyKey, family.FamilyKey, StringComparison.Ordinal)))
        {
            families.Add(family);
        }
    }

    private static StationRenderAnchor ResolveStationAnchor(
        StationRenderAnchor rawAnchor,
        List<StationRouteAnchorCandidate> candidates,
        SvgRenderOptions options)
    {
        if (candidates.Count == 0)
        {
            return rawAnchor with { FallbackReason = "no-related-pathPoints-route" };
        }

        double maxDistance = Math.Max(0, options.StationRouteAnchorMaxDistance);
        List<StationRouteAnchorCandidate> tooFar = candidates
            .Where(candidate => candidate.Distance > maxDistance)
            .ToList();
        if (tooFar.Count > 0)
        {
            return rawAnchor with
            {
                FamilyKeys = candidates.Select(candidate => candidate.FamilyKey).ToList(),
                FallbackReason = "too-far",
                Distance = candidates.Min(candidate => candidate.Distance)
            };
        }

        if (candidates.Count == 1)
        {
            StationRouteAnchorCandidate candidate = candidates[0];
            return rawAnchor with
            {
                Point = candidate.Point,
                Applied = true,
                Source = "route-projection",
                Distance = candidate.Distance,
                FamilyKeys = [candidate.FamilyKey],
                FallbackReason = null
            };
        }

        double spread = MeasureAnchorSpread(candidates.Select(candidate => candidate.Point).ToList());
        if (spread > Math.Max(0, options.StationRouteAnchorMultiFamilyMaxSpread))
        {
            return rawAnchor with
            {
                FamilyKeys = candidates.Select(candidate => candidate.FamilyKey).ToList(),
                FallbackReason = "multi-family-anchor-spread-too-large",
                Distance = candidates.Min(candidate => candidate.Distance)
            };
        }

        SvgPoint average = new(
            candidates.Average(candidate => candidate.Point.X),
            candidates.Average(candidate => candidate.Point.Y));
        return rawAnchor with
        {
            Point = average,
            Applied = true,
            Source = "multi-family-route-projection",
            Distance = Distance(rawAnchor.RawPoint, average),
            FamilyKeys = candidates.Select(candidate => candidate.FamilyKey).ToList(),
            FallbackReason = null
        };
    }

    private static double MeasureAnchorSpread(List<SvgPoint> points)
    {
        double spread = 0;
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                spread = Math.Max(spread, Distance(points[i], points[j]));
            }
        }

        return spread;
    }

    private static bool TryFindNearestPointOnPolylines(
        SvgPoint rawPoint,
        List<RoutePolyline> polylines,
        out SvgPoint nearestPoint,
        out double nearestDistance)
    {
        nearestPoint = rawPoint;
        nearestDistance = double.MaxValue;
        bool found = false;

        foreach (RoutePolyline polyline in polylines)
        {
            List<SvgPoint> points = polyline.Points;
            for (int i = 1; i < points.Count; i++)
            {
                SvgPoint candidate = ProjectPointToSegment(rawPoint, points[i - 1], points[i]);
                double distance = Distance(rawPoint, candidate);
                if (distance < nearestDistance)
                {
                    nearestPoint = candidate;
                    nearestDistance = distance;
                    found = true;
                }
            }
        }

        return found;
    }

    private static SvgPoint ProjectPointToSegment(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.000001)
        {
            return start;
        }

        double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return new SvgPoint(start.X + dx * t, start.Y + dy * t);
    }

    private static StationRouteAnchorMap CreateStationRouteAnchorMap(Dictionary<string, StationRenderAnchor> anchors)
    {
        return new StationRouteAnchorMap(
            anchors.ToDictionary(pair => pair.Key, pair => pair.Value.Point, StringComparer.Ordinal),
            anchors);
    }

    private static SchematicLayoutResult ApplySchematicLiteLayout(
        Dictionary<string, SvgPoint> geographicPoints,
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        SvgRenderOptions options,
        bool reserveLegendSpace,
        List<string> warnings)
    {
        double gridSize = Math.Max(4, options.GridSize);
        SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);
        Dictionary<string, SvgPoint> points = geographicPoints
            .ToDictionary(
                pair => pair.Key,
                pair => SnapPointToGrid(pair.Value, gridSize, bounds),
                StringComparer.Ordinal);
        HashSet<string> placed = new(StringComparer.Ordinal);

        foreach (MetroLine line in lines)
        {
            List<string> validStops = (line.Stops ?? [])
                .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId) && geographicPoints.ContainsKey(stopId))
                .ToList();

            if (validStops.Count == 0)
            {
                continue;
            }

            string firstStopId = validStops[0];
            if (placed.Add(firstStopId))
            {
                points[firstStopId] = SnapPointToGrid(geographicPoints[firstStopId], gridSize, bounds);
            }

            for (int i = 1; i < validStops.Count; i++)
            {
                string previousStopId = validStops[i - 1];
                string currentStopId = validStops[i];
                if (string.Equals(previousStopId, currentStopId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!points.TryGetValue(previousStopId, out SvgPoint previousPoint))
                {
                    previousPoint = SnapPointToGrid(geographicPoints[previousStopId], gridSize, bounds);
                    points[previousStopId] = previousPoint;
                }

                if (placed.Contains(currentStopId))
                {
                    continue;
                }

                SvgPoint desiredPoint = SnapPointToGrid(geographicPoints[currentStopId], gridSize, bounds);
                points[currentStopId] = SnapSegmentEndpoint(previousPoint, desiredPoint, gridSize, bounds);
                placed.Add(currentStopId);
            }
        }

        return RelaxSchematicStationSpacing(points, lines, stationsById, bounds, options, warnings);
    }

    private static SchematicLayoutResult RelaxSchematicStationSpacing(
        Dictionary<string, SvgPoint> snappedPoints,
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        SvgRect bounds,
        SvgRenderOptions options,
        List<string> warnings)
    {
        double gridSize = Math.Max(4, options.GridSize);
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        double minimumSpacing = options.SchematicMinimumStationSpacing > 0
            ? options.SchematicMinimumStationSpacing
            : Math.Max(3.0 * visualStyle.StationMarkerOuterRadius, gridSize);

        Dictionary<string, SvgPoint> adjusted = snappedPoints.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Dictionary<string, SvgPoint> original = snappedPoints.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> adjacency = BuildSchematicStationAdjacency(lines, stationsById, snappedPoints);
        Dictionary<string, int> degreeByStation = adjacency.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
        HashSet<string> interchangeStationIds = stationsById.Values
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && IsInterchange(station))
            .Select(station => station.Id!)
            .ToHashSet(StringComparer.Ordinal);

        int initialConflicts = CountSchematicSpacingConflicts(adjusted, minimumSpacing);
        if (initialConflicts == 0)
        {
            return new SchematicLayoutResult(
                adjusted,
                [],
                [],
                new Dictionary<string, List<string>>(StringComparer.Ordinal),
                new Dictionary<string, SchematicV2RouteGuideMetadata>(StringComparer.Ordinal));
        }

        List<string> stationIds = adjusted.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        double maxStep = Math.Max(2, gridSize * 0.18);
        int iterations = 14;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            bool moved = false;
            for (int i = 0; i < stationIds.Count; i++)
            {
                string firstId = stationIds[i];
                for (int j = i + 1; j < stationIds.Count; j++)
                {
                    string secondId = stationIds[j];
                    SvgPoint first = adjusted[firstId];
                    SvgPoint second = adjusted[secondId];
                    double distance = Distance(first, second);
                    if (distance >= minimumSpacing)
                    {
                        continue;
                    }

                    if (ShouldKeepSchematicStationsTogether(firstId, secondId, stationsById, interchangeStationIds, distance))
                    {
                        continue;
                    }

                    SvgPoint direction = GetSchematicSeparationDirection(first, second, firstId, secondId);
                    double push = Math.Min(maxStep, (minimumSpacing - distance) * 0.5);
                    double firstWeight = GetSchematicStationAnchorWeight(firstId, degreeByStation, interchangeStationIds);
                    double secondWeight = GetSchematicStationAnchorWeight(secondId, degreeByStation, interchangeStationIds);
                    double firstShare = secondWeight / (firstWeight + secondWeight);
                    double secondShare = firstWeight / (firstWeight + secondWeight);

                    SvgPoint movedFirst = new(first.X - direction.X * push * firstShare, first.Y - direction.Y * push * firstShare);
                    SvgPoint movedSecond = new(second.X + direction.X * push * secondShare, second.Y + direction.Y * push * secondShare);

                    adjusted[firstId] = ClampSchematicStationMovement(movedFirst, original[firstId], bounds, gridSize, firstId, degreeByStation, interchangeStationIds);
                    adjusted[secondId] = ClampSchematicStationMovement(movedSecond, original[secondId], bounds, gridSize, secondId, degreeByStation, interchangeStationIds);
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        double halfGrid = Math.Max(2, gridSize / 2);
        foreach (string stationId in stationIds)
        {
            SvgPoint snapped = SnapPointToGrid(adjusted[stationId], halfGrid, bounds);
            adjusted[stationId] = ClampSchematicStationMovement(snapped, original[stationId], bounds, gridSize, stationId, degreeByStation, interchangeStationIds);
        }

        Dictionary<string, SchematicStationAdjustment> adjustments = [];
        foreach (string stationId in stationIds)
        {
            double distance = Distance(original[stationId], adjusted[stationId]);
            if (distance <= 0.001)
            {
                continue;
            }

            adjustments[stationId] = new SchematicStationAdjustment(stationId, original[stationId], adjusted[stationId], distance, "min-spacing");
        }

        int remainingConflicts = CountSchematicSpacingConflicts(adjusted, minimumSpacing);
        double maxAdjustment = adjustments.Values.Select(adjustment => adjustment.Distance).DefaultIfEmpty(0).Max();
        warnings.Add($"Schematic station spacing conflicts: {initialConflicts}; adjusted stations: {adjustments.Count}; max adjustment distance: {Format(maxAdjustment)}; remaining spacing conflicts: {remainingConflicts}.");
        return new SchematicLayoutResult(
            adjusted,
            adjustments,
            [],
            new Dictionary<string, List<string>>(StringComparer.Ordinal),
            new Dictionary<string, SchematicV2RouteGuideMetadata>(StringComparer.Ordinal));
    }

    private static Dictionary<string, HashSet<string>> BuildSchematicStationAdjacency(
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        Dictionary<string, HashSet<string>> adjacency = stationPoints.Keys
            .ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (MetroLine line in lines)
        {
            List<string> validStops = (line.Stops ?? [])
                .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId) && stationPoints.ContainsKey(stopId))
                .ToList();
            for (int i = 1; i < validStops.Count; i++)
            {
                string a = validStops[i - 1];
                string b = validStops[i];
                if (string.Equals(a, b, StringComparison.Ordinal))
                {
                    continue;
                }

                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }
        }

        return adjacency;
    }

    private static bool ShouldKeepSchematicStationsTogether(
        string firstId,
        string secondId,
        Dictionary<string, MetroStation> stationsById,
        HashSet<string> interchangeStationIds,
        double distance)
    {
        if (distance > 0.001)
        {
            return false;
        }

        bool bothInterchangeLike = interchangeStationIds.Contains(firstId) && interchangeStationIds.Contains(secondId);
        if (!bothInterchangeLike)
        {
            return false;
        }

        string firstName = stationsById.TryGetValue(firstId, out MetroStation? first) ? first.Name ?? string.Empty : string.Empty;
        string secondName = stationsById.TryGetValue(secondId, out MetroStation? second) ? second.Name ?? string.Empty : string.Empty;
        return string.Equals(firstName, secondName, StringComparison.CurrentCulture);
    }

    private static SvgPoint GetSchematicSeparationDirection(SvgPoint first, SvgPoint second, string firstId, string secondId)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 0.001)
        {
            return new SvgPoint(dx / distance, dy / distance);
        }

        int hash = StringComparer.Ordinal.GetHashCode(firstId + "|" + secondId);
        double angle = (Math.Abs(hash) % 8) * Math.PI / 4.0;
        return new SvgPoint(Math.Cos(angle), Math.Sin(angle));
    }

    private static double GetSchematicStationAnchorWeight(string stationId, Dictionary<string, int> degreeByStation, HashSet<string> interchangeStationIds)
    {
        degreeByStation.TryGetValue(stationId, out int degree);
        double weight = 1 + degree * 0.85;
        if (interchangeStationIds.Contains(stationId) || degree >= 3)
        {
            weight += 2.5;
        }

        return weight;
    }

    private static SvgPoint ClampSchematicStationMovement(
        SvgPoint point,
        SvgPoint original,
        SvgRect bounds,
        double gridSize,
        string stationId,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds)
    {
        degreeByStation.TryGetValue(stationId, out int degree);
        double maxMovement = (interchangeStationIds.Contains(stationId) || degree >= 3)
            ? gridSize * 0.75
            : gridSize * 1.5;
        double distance = Distance(original, point);
        SvgPoint clamped = point;
        if (distance > maxMovement && distance > 0.001)
        {
            double ratio = maxMovement / distance;
            clamped = new SvgPoint(
                original.X + (point.X - original.X) * ratio,
                original.Y + (point.Y - original.Y) * ratio);
        }

        return new SvgPoint(
            Math.Clamp(clamped.X, bounds.Left, bounds.Right),
            Math.Clamp(clamped.Y, bounds.Top, bounds.Bottom));
    }

    private static int CountSchematicSpacingConflicts(Dictionary<string, SvgPoint> stationPoints, double minimumSpacing)
    {
        List<string> stationIds = stationPoints.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        int conflicts = 0;
        for (int i = 0; i < stationIds.Count; i++)
        {
            for (int j = i + 1; j < stationIds.Count; j++)
            {
                double distance = Distance(stationPoints[stationIds[i]], stationPoints[stationIds[j]]);
                if (distance < minimumSpacing)
                {
                    conflicts++;
                }
            }
        }

        return conflicts;
    }

    private static List<SchematicV2DenseStationPair> FindSchematicV2DenseStationPairs(
        Dictionary<string, SvgPoint> stationPoints,
        Dictionary<string, HashSet<string>> adjacency,
        HashSet<string> interchangeStationIds,
        Dictionary<string, MetroStation> stationsById,
        double minimumSpacing)
    {
        List<SchematicV2DenseStationPair> pairs = [];
        List<string> stationIds = stationPoints.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        for (int i = 0; i < stationIds.Count; i++)
        {
            string firstId = stationIds[i];
            for (int j = i + 1; j < stationIds.Count; j++)
            {
                string secondId = stationIds[j];
                double distance = Distance(stationPoints[firstId], stationPoints[secondId]);
                if (distance >= minimumSpacing)
                {
                    continue;
                }

                bool adjacent = adjacency.TryGetValue(firstId, out HashSet<string>? neighbors) && neighbors.Contains(secondId);
                bool sameNameCluster = HaveSameStationDisplayName(firstId, secondId, stationsById);
                bool sameNameAssetDefaultCluster = sameNameCluster && HaveSameStationNameKind(firstId, secondId, stationsById, StationNameKind.KnownAssetDefault);
                bool sameNameLikelyUserCluster = sameNameCluster && HaveSameStationNameKind(firstId, secondId, stationsById, StationNameKind.UserNamed);
                pairs.Add(new SchematicV2DenseStationPair(
                    firstId,
                    secondId,
                    distance,
                    minimumSpacing,
                    adjacent,
                    sameNameCluster,
                    sameNameAssetDefaultCluster,
                    sameNameLikelyUserCluster,
                    interchangeStationIds.Contains(firstId),
                    interchangeStationIds.Contains(secondId)));
            }
        }

        return pairs
            .OrderBy(pair => pair.Distance)
            .ThenBy(pair => pair.FirstStationId, StringComparer.Ordinal)
            .ThenBy(pair => pair.SecondStationId, StringComparer.Ordinal)
            .ToList();
    }

    private static string FormatSchematicV2DensePairDetails(
        List<SchematicV2DenseStationPair> pairs,
        Dictionary<string, MetroStation> stationsById)
    {
        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> details = pairs.Take(6).Select(pair =>
        {
            string firstName = GetStationDebugName(pair.FirstStationId, stationsById);
            string secondName = GetStationDebugName(pair.SecondStationId, stationsById);
            string adjacent = pair.Adjacent ? "adjacent" : "non-adjacent";
            string sameName = pair.SameNameCluster ? ", same-name cluster" : string.Empty;
            string sameNameKind = pair.SameNameAssetDefaultCluster
                ? ", asset-default name"
                : pair.SameNameLikelyUserCluster
                    ? ", likely user-named"
                    : string.Empty;
            return $"{firstName} <-> {secondName} ({Format(pair.Distance)}/{Format(pair.MinimumSpacing)}, {adjacent}{sameName}{sameNameKind})";
        });
        string suffix = pairs.Count > 6 ? $"; +{pairs.Count - 6} more" : string.Empty;
        return $"; remaining dense station pair details: {string.Join("; ", details)}{suffix}";
    }

    private static string GetStationDebugName(string stationId, Dictionary<string, MetroStation> stationsById)
    {
        if (stationsById.TryGetValue(stationId, out MetroStation? station)
            && !string.IsNullOrWhiteSpace(station.Name))
        {
            return $"{station.Name} [{stationId}]";
        }

        return stationId;
    }

    private static bool HaveSameStationDisplayName(
        string firstStationId,
        string secondStationId,
        Dictionary<string, MetroStation> stationsById)
    {
        if (!stationsById.TryGetValue(firstStationId, out MetroStation? first)
            || !stationsById.TryGetValue(secondStationId, out MetroStation? second)
            || string.IsNullOrWhiteSpace(first.Name)
            || string.IsNullOrWhiteSpace(second.Name))
        {
            return false;
        }

        return string.Equals(first.Name.Trim(), second.Name.Trim(), StringComparison.CurrentCulture);
    }

    private static bool HaveSameStationNameKind(
        string firstStationId,
        string secondStationId,
        Dictionary<string, MetroStation> stationsById,
        StationNameKind kind)
    {
        if (!stationsById.TryGetValue(firstStationId, out MetroStation? first)
            || !stationsById.TryGetValue(secondStationId, out MetroStation? second)
            || string.IsNullOrWhiteSpace(first.Name)
            || string.IsNullOrWhiteSpace(second.Name))
        {
            return false;
        }

        return StationLabelClassifier.Classify(first.Name, first.Id) == kind
            && StationLabelClassifier.Classify(second.Name, second.Id) == kind;
    }

    private static SchematicLayoutResult ApplySchematicV2Layout(
        Dictionary<string, SvgPoint> geographicPoints,
        CoordinateProjector projector,
        Dictionary<string, SvgPoint> corridorDetectionPoints,
        CoordinateProjector corridorDetectionProjector,
        List<DisplayLineFamily> displayFamilies,
        CanonicalSchematicNetwork? canonicalNetwork,
        Dictionary<string, MetroStation> stationsById,
        SvgRenderOptions options,
        bool reserveLegendSpace,
        List<string> warnings)
    {
        double gridSize = Math.Max(4, options.GridSize);
        SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);
        Dictionary<string, SvgPoint> points = geographicPoints
            .ToDictionary(
                pair => pair.Key,
                pair => SnapPointToGrid(pair.Value, gridSize, bounds),
                StringComparer.Ordinal);
        Dictionary<string, SvgPoint> original = points.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> adjacency = canonicalNetwork is null
            ? BuildSchematicV2Adjacency(displayFamilies, stationsById, points)
            : BuildSchematicV2Adjacency(canonicalNetwork, points);
        Dictionary<string, int> degreeByStation = adjacency.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
        HashSet<string> interchangeStationIds = stationsById.Values
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && IsInterchange(station))
            .Select(station => station.Id!)
            .ToHashSet(StringComparer.Ordinal);
        double minimumSpacing = options.SchematicMinimumStationSpacing > 0
            ? options.SchematicMinimumStationSpacing
            : Math.Max(gridSize * 1.35, SvgVisualStyle.From(options).StationMarkerOuterRadius * 4.0);

        List<SchematicV2FamilyPath> familyPaths = canonicalNetwork is null
            ? BuildSchematicV2FamilyPaths(displayFamilies, stationsById, points)
            : BuildSchematicV2FamilyPaths(canonicalNetwork, points);
        int initialDensePairs = CountSchematicSpacingConflicts(points, minimumSpacing);
        int initialShortEdges = CountShortSchematicEdges(points, adjacency, minimumSpacing);
        double maxStep = Math.Max(3, gridSize * 0.22);

        for (int iteration = 0; iteration < 18; iteration++)
        {
            bool moved = false;
            foreach (SchematicV2FamilyPath familyPath in familyPaths)
            {
                for (int i = 1; i < familyPath.Stops.Count; i++)
                {
                    string previousId = familyPath.Stops[i - 1];
                    string currentId = familyPath.Stops[i];
                    if (!points.TryGetValue(previousId, out SvgPoint previous)
                        || !points.TryGetValue(currentId, out SvgPoint current)
                        || string.Equals(previousId, currentId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    double distance = Distance(previous, current);
                    if (distance >= minimumSpacing)
                    {
                        continue;
                    }

                    SvgPoint direction = distance <= 0.001
                        ? familyPath.Direction
                        : QuantizeSchematicDirection(new SvgPoint(current.X - previous.X, current.Y - previous.Y), familyPath.Direction);
                    ApplySchematicV2PairPush(
                        points,
                        original,
                        previousId,
                        currentId,
                        direction,
                        Math.Min(maxStep, (minimumSpacing - distance) * 0.55),
                        bounds,
                        gridSize,
                        degreeByStation,
                        interchangeStationIds);
                    moved = true;
                }
            }

            moved |= RelaxSchematicV2DensePairs(points, original, bounds, gridSize, minimumSpacing, maxStep, degreeByStation, interchangeStationIds);
            if (!moved)
            {
                break;
            }
        }

        double halfGrid = Math.Max(2, gridSize / 2);
        foreach (string stationId in points.Keys.ToList())
        {
            points[stationId] = ClampSchematicStationMovement(
                SnapPointToGrid(points[stationId], halfGrid, bounds),
                original[stationId],
                bounds,
                gridSize,
                stationId,
                degreeByStation,
                interchangeStationIds);
        }

        List<SchematicV2GeometryCorridorConstraint> geometryCorridors = DetectSchematicV2GeometryCorridors(displayFamilies, stationsById, corridorDetectionPoints, corridorDetectionProjector, options);
        SchematicV2RouteGuideResult routeGuideResult = BuildSchematicV2RouteGuideByFamily(displayFamilies, canonicalNetwork, stationsById, points, geometryCorridors);
        Dictionary<string, string> schematicMapVisibleLaneKeyByFamilyKey = BuildSchematicMapVisibleLaneKeyByFamilyKey(displayFamilies);
        List<SchematicV2FamilyPath> schematicMapAllPaths = BuildSchematicMapAllFamilyPaths(familyPaths, routeGuideResult.RouteGuideByFamily);
        Dictionary<string, HashSet<string>> schematicMapStationVisibleLaneGroups = BuildSchematicMapStationFamilies(
            schematicMapAllPaths,
            schematicMapVisibleLaneKeyByFamilyKey);
        int abstractLinearizedStations = ApplySchematicMapSimpleRunLinearization(
            points,
            original,
            schematicMapAllPaths,
            schematicMapStationVisibleLaneGroups,
            bounds,
            halfGrid,
            degreeByStation,
            interchangeStationIds,
            options);
        int straightenedTerminalTails = StraightenSchematicV2TerminalTails(
            points,
            routeGuideResult.RouteGuideByFamily.Values,
            degreeByStation,
            interchangeStationIds,
            gridSize);
        int relaxedSharpAngles = RelaxSchematicV2SharpAngles(
            points,
            familyPaths.Select(path => (IReadOnlyList<string>)path.Stops).Concat(routeGuideResult.RouteGuideByFamily.Values),
            bounds,
            halfGrid);
        int octilinearNormalizedStations = NormalizeSchematicMapOctilinearSegments(
            points,
            original,
            familyPaths.Select(path => (IReadOnlyList<string>)path.Stops).Concat(routeGuideResult.RouteGuideByFamily.Values),
            bounds,
            halfGrid,
            degreeByStation,
            interchangeStationIds,
            schematicMapStationVisibleLaneGroups,
            options);
        int localClearanceStations = ApplySchematicMapLocalClearance(
            points,
            original,
            schematicMapAllPaths,
            schematicMapStationVisibleLaneGroups,
            schematicMapVisibleLaneKeyByFamilyKey,
            bounds,
            halfGrid,
            degreeByStation,
            interchangeStationIds,
            options);
        int shallowKinkStations = StraightenSchematicMapShallowKinks(
            points,
            original,
            familyPaths.Select(path => (IReadOnlyList<string>)path.Stops).Concat(routeGuideResult.RouteGuideByFamily.Values),
            bounds,
            halfGrid,
            degreeByStation,
            interchangeStationIds,
            schematicMapStationVisibleLaneGroups,
            options);
        int separatedRouteOverlapStations = SeparateSchematicMapNonAdjacentRouteOverlaps(
            points,
            original,
            routeGuideResult.RouteGuideByFamily.Values,
            bounds,
            minimumSpacing,
            degreeByStation,
            interchangeStationIds,
            options);
        string adjustmentReason = straightenedTerminalTails > 0
            ? "topology-spacing-terminal-tail-straightening"
            : relaxedSharpAngles > 0
                ? "topology-spacing-sharp-angle-relaxation"
                : separatedRouteOverlapStations > 0
                    ? "schematic-map-route-overlap-separation"
                    : shallowKinkStations > 0
                        ? "schematic-map-shallow-kink-straightening"
                        : localClearanceStations > 0
                            ? "schematic-map-local-clearance"
                            : octilinearNormalizedStations > 0
                                ? "schematic-map-octilinear-normalization"
                                : abstractLinearizedStations > 0
                                    ? "schematic-map-simple-run-linearization"
                                    : "topology-spacing";
        Dictionary<string, SchematicStationAdjustment> adjustments = BuildSchematicStationAdjustments(original, points, adjustmentReason);
        List<SchematicV2DenseStationPair> remainingDensePairs = FindSchematicV2DenseStationPairs(points, adjacency, interchangeStationIds, stationsById, minimumSpacing);
        int remainingShortEdges = CountShortSchematicEdges(points, adjacency, minimumSpacing);
        double maxAdjustment = adjustments.Values.Select(adjustment => adjustment.Distance).DefaultIfEmpty(0).Max();
        string canonicalNetworkText = canonicalNetwork is null
            ? "none"
            : $"stations={canonicalNetwork.Stations.Count}, families={canonicalNetwork.Families.Count}, edges={canonicalNetwork.AdjacencyEdges.Count}, corridor hints={canonicalNetwork.CorridorHints.Count}";
        string densePairDetails = FormatSchematicV2DensePairDetails(remainingDensePairs, stationsById);
        int sameNameDenseClusters = remainingDensePairs.Count(pair => pair.SameNameCluster);
        int assetDefaultSameNameDenseClusters = remainingDensePairs.Count(pair => pair.SameNameAssetDefaultCluster);
        int likelyUserSameNameDenseClusters = remainingDensePairs.Count(pair => pair.SameNameLikelyUserCluster);
        warnings.Add($"Schematic-v2 topology diagnostics: initial dense station pairs: {initialDensePairs}; remaining dense station pairs: {remainingDensePairs.Count}; same-name dense clusters: {sameNameDenseClusters}; same-name asset-default clusters: {assetDefaultSameNameDenseClusters}; same-name likely-user clusters: {likelyUserSameNameDenseClusters}; initial short adjacency edges: {initialShortEdges}; remaining short adjacency edges: {remainingShortEdges}; adjusted stations: {adjustments.Count}; max adjustment distance: {Format(maxAdjustment)}; sharp angle relaxations: {relaxedSharpAngles}; terminal tail straightening: {straightenedTerminalTails}; schematic-map linearized stations: {abstractLinearizedStations}; schematic-map octilinear stations: {octilinearNormalizedStations}; schematic-map clearance stations: {localClearanceStations}; schematic-map shallow-kink stations: {shallowKinkStations}; schematic-map route-overlap stations: {separatedRouteOverlapStations}; geometry shared corridors: {geometryCorridors.Count}; canonical network: {canonicalNetworkText}{densePairDetails}.");
        return new SchematicLayoutResult(points, adjustments, remainingDensePairs, routeGuideResult.RouteGuideByFamily, routeGuideResult.MetadataByFamily);
    }

    private static SvgRenderOptions CreateSchematicV2CanonicalOptions(SvgRenderOptions options)
    {
        SvgRenderSize poster = SvgRenderSizePresets.Get(SvgRenderSizePreset.Poster);
        return new SvgRenderOptions
        {
            LayoutMode = options.LayoutMode,
            MapStyle = options.MapStyle,
            Width = poster.Width,
            Height = poster.Height,
            Padding = 80,
            Margin = 80,
            LegendWidth = 240,
            LegendGap = options.LegendGap,
            LineWidth = options.LineWidth,
            StationRadius = options.StationRadius,
            InterchangeStationRadius = options.InterchangeStationRadius,
            LabelFontSize = options.LabelFontSize,
            LegendLabelFontSize = options.LegendLabelFontSize,
            LabelGap = options.LabelGap,
            EnableCenterExpansion = options.EnableCenterExpansion,
            CenterExpansionStrength = options.CenterExpansionStrength,
            GridSize = options.GridSize,
            HideGenericStationLabels = options.HideGenericStationLabels,
            EnableVirtualTransferHints = options.EnableVirtualTransferHints,
            HideCrowdedLabels = options.HideCrowdedLabels,
            AlwaysShowInterchanges = options.AlwaysShowInterchanges,
            AlwaysShowTerminals = options.AlwaysShowTerminals,
            UsePathPoints = options.UsePathPoints,
            PathPointSimplificationEnabled = options.PathPointSimplificationEnabled,
            PathPointSimplificationTolerance = options.PathPointSimplificationTolerance,
            MinPathSegmentLength = options.MinPathSegmentLength,
            AdaptivePathPointSimplificationEnabled = options.AdaptivePathPointSimplificationEnabled,
            EnableParallelCorridorOffset = options.EnableParallelCorridorOffset,
            EnableServiceFamilyMerge = options.EnableServiceFamilyMerge,
            EnableSharedCorridorCompositeStroke = options.EnableSharedCorridorCompositeStroke,
            EnableExpressCenterStripe = options.EnableExpressCenterStripe,
            EnableStationRouteAnchoring = options.EnableStationRouteAnchoring,
            StationRouteAnchorMaxDistance = options.StationRouteAnchorMaxDistance,
            StationRouteAnchorMultiFamilyMaxSpread = options.StationRouteAnchorMultiFamilyMaxSpread,
            EnableSchematicSegmentOverlapResolver = options.EnableSchematicSegmentOverlapResolver,
            SchematicSegmentOverlapOffsetDistance = options.SchematicSegmentOverlapOffsetDistance,
            SchematicOverlapEndpointTrim = options.SchematicOverlapEndpointTrim,
            SchematicShortOverlapSegmentThreshold = options.SchematicShortOverlapSegmentThreshold,
            SchematicMinimumStationSpacing = options.SchematicMinimumStationSpacing,
            CompactTransitMapFrame = options.CompactTransitMapFrame,
            EnableSchematicMapOctilinearNormalization = options.EnableSchematicMapOctilinearNormalization,
            SchematicMapOctilinearSnapAngleDegrees = options.SchematicMapOctilinearSnapAngleDegrees,
            EnableSchematicMapSimpleRunLinearization = options.EnableSchematicMapSimpleRunLinearization,
            SchematicMapPreferredStationSpacing = options.SchematicMapPreferredStationSpacing,
            EnableSchematicMapLocalClearance = options.EnableSchematicMapLocalClearance,
            SchematicMapLocalClearanceDistance = options.SchematicMapLocalClearanceDistance,
            EnableSchematicMapSyntheticBends = options.EnableSchematicMapSyntheticBends,
            SchematicMapSyntheticBendMinimumLength = options.SchematicMapSyntheticBendMinimumLength
        };
    }

    private static SchematicLayoutResult ScaleSchematicV2LayoutToTarget(
        SchematicLayoutResult layout,
        SvgRenderOptions sourceOptions,
        SvgRenderOptions targetOptions,
        bool reserveLegendSpace)
    {
        SvgRect sourceBounds = CreateGeometryBounds(sourceOptions, reserveLegendSpace);
        SvgRect targetBounds = CreateGeometryBounds(targetOptions, reserveLegendSpace);
        double sourceWidth = Math.Max(1, sourceBounds.Right - sourceBounds.Left);
        double sourceHeight = Math.Max(1, sourceBounds.Bottom - sourceBounds.Top);
        double targetWidth = Math.Max(1, targetBounds.Right - targetBounds.Left);
        double targetHeight = Math.Max(1, targetBounds.Bottom - targetBounds.Top);

        double scale = Math.Min(targetWidth / sourceWidth, targetHeight / sourceHeight);
        double scaledWidth = sourceWidth * scale;
        double scaledHeight = sourceHeight * scale;
        double offsetX = targetBounds.Left + (targetWidth - scaledWidth) / 2.0;
        double offsetY = targetBounds.Top + (targetHeight - scaledHeight) / 2.0;

        SvgPoint Transform(SvgPoint point)
        {
            return new SvgPoint(
                offsetX + (point.X - sourceBounds.Left) * scale,
                offsetY + (point.Y - sourceBounds.Top) * scale);
        }

        Dictionary<string, SvgPoint> scaledPoints = layout.Points.ToDictionary(
            pair => pair.Key,
            pair => Transform(pair.Value),
            StringComparer.Ordinal);
        Dictionary<string, SchematicStationAdjustment> scaledAdjustments = layout.Adjustments.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                SvgPoint originalPoint = Transform(pair.Value.OriginalPoint);
                SvgPoint adjustedPoint = Transform(pair.Value.AdjustedPoint);
                return pair.Value with
                {
                    OriginalPoint = originalPoint,
                    AdjustedPoint = adjustedPoint,
                    Distance = Distance(originalPoint, adjustedPoint)
                };
            },
            StringComparer.Ordinal);

        List<SchematicV2DenseStationPair> scaledDensePairs = layout.DenseStationPairs
            .Select(pair =>
            {
                double distance = scaledPoints.TryGetValue(pair.FirstStationId, out SvgPoint first)
                    && scaledPoints.TryGetValue(pair.SecondStationId, out SvgPoint second)
                        ? Distance(first, second)
                        : pair.Distance * scale;
                return pair with
                {
                    Distance = distance,
                    MinimumSpacing = pair.MinimumSpacing * scale
                };
            })
            .ToList();

        return new SchematicLayoutResult(scaledPoints, scaledAdjustments, scaledDensePairs, layout.RouteGuideByFamily, layout.RouteGuideMetadataByFamily);
    }

    private static int ApplySchematicMapSimpleRunLinearization(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        List<SchematicV2FamilyPath> allPaths,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups,
        SvgRect bounds,
        double gridSize,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap || !options.EnableSchematicMapSimpleRunLinearization)
        {
            return 0;
        }

        if (allPaths.Count == 0)
        {
            return 0;
        }

        Dictionary<string, List<SvgPoint>> proposals = new(StringComparer.Ordinal);
        double preferredSpacing = options.SchematicMapPreferredStationSpacing > 0
            ? options.SchematicMapPreferredStationSpacing
            : Math.Max(options.GridSize * 5.0, options.LineWidth * 9.0);
        double minSpacing = Math.Max(options.GridSize * 3.0, options.LineWidth * 6.0);
        double maxOrdinarySpacing = Math.Max(preferredSpacing * 1.45, options.GridSize * 7.5);

        foreach (SchematicV2FamilyPath path in allPaths)
        {
            List<string> stops = RemoveConsecutiveDuplicateStops(path.Stops);
            if (stops.Count < 3)
            {
                continue;
            }

            List<int> boundaries = [0];
            for (int i = 1; i < stops.Count - 1; i++)
            {
                if (IsSchematicMapSimpleRunAnchor(stops[i], degreeByStation, interchangeStationIds, stationVisibleLaneGroups))
                {
                    boundaries.Add(i);
                }
            }

            boundaries.Add(stops.Count - 1);

            for (int boundaryIndex = 1; boundaryIndex < boundaries.Count; boundaryIndex++)
            {
                int startIndex = boundaries[boundaryIndex - 1];
                int endIndex = boundaries[boundaryIndex];
                if (endIndex - startIndex < 2)
                {
                    continue;
                }

                AddSchematicMapLinearRunProposals(
                    points,
                    original,
                    stops,
                    startIndex,
                    endIndex,
                    proposals,
                    bounds,
                    gridSize,
                    preferredSpacing,
                    minSpacing,
                    maxOrdinarySpacing,
                    degreeByStation,
                    interchangeStationIds,
                    stationVisibleLaneGroups);
            }
        }

        HashSet<string> adjustedStations = new(StringComparer.Ordinal);
        foreach ((string stationId, List<SvgPoint> stationProposals) in proposals)
        {
            if (stationProposals.Count == 0)
            {
                continue;
            }

            SvgPoint average = new(
                stationProposals.Average(point => point.X),
                stationProposals.Average(point => point.Y));
            SvgPoint snapped = SnapPointToGrid(average, gridSize, bounds);
            snapped = new SvgPoint(
                Math.Clamp(snapped.X, bounds.Left, bounds.Right),
                Math.Clamp(snapped.Y, bounds.Top, bounds.Bottom));

            if (!points.TryGetValue(stationId, out SvgPoint current) || Distance(current, snapped) <= 0.001)
            {
                continue;
            }

            points[stationId] = snapped;
            adjustedStations.Add(stationId);
        }

        return adjustedStations.Count;
    }

    private static void AddSchematicMapLinearRunProposals(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        List<string> stops,
        int startIndex,
        int endIndex,
        Dictionary<string, List<SvgPoint>> proposals,
        SvgRect bounds,
        double gridSize,
        double preferredSpacing,
        double minSpacing,
        double maxOrdinarySpacing,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        Dictionary<string, HashSet<string>> stationFamilies)
    {
        string startId = stops[startIndex];
        string endId = stops[endIndex];
        if (!points.TryGetValue(startId, out SvgPoint start)
            || !points.TryGetValue(endId, out SvgPoint end))
        {
            return;
        }

        int count = endIndex - startIndex + 1;
        if (count < 3)
        {
            return;
        }

        bool startAnchor = IsSchematicMapSimpleRunAnchor(startId, degreeByStation, interchangeStationIds, stationFamilies);
        bool endAnchor = IsSchematicMapSimpleRunAnchor(endId, degreeByStation, interchangeStationIds, stationFamilies);
        double pathLength = 0;
        for (int i = startIndex + 1; i <= endIndex; i++)
        {
            if (points.TryGetValue(stops[i - 1], out SvgPoint previous) && points.TryGetValue(stops[i], out SvgPoint current))
            {
                pathLength += Distance(previous, current);
            }
        }

        if (pathLength <= 0.001)
        {
            return;
        }

        SvgPoint endpointVector = new(end.X - start.X, end.Y - start.Y);
        SvgPoint direction = QuantizeSchematicDirection(endpointVector, new SvgPoint(1, 0));
        double spacing = ResolveSchematicMapLinearSpacing(pathLength / Math.Max(1, count - 1), preferredSpacing, minSpacing, maxOrdinarySpacing);
        double[] segmentSpacings = ResolveSchematicMapSegmentSpacings(
            points,
            stops,
            startIndex,
            endIndex,
            spacing,
            preferredSpacing,
            minSpacing,
            maxOrdinarySpacing);

        if (startAnchor && endAnchor)
        {
            AddSchematicMapFixedEndpointRunProposals(
                points,
                original,
                stops,
                startIndex,
                endIndex,
                segmentSpacings,
                proposals,
                bounds,
                gridSize);
            return;
        }

        int anchorIndex = startAnchor || !endAnchor ? startIndex : endIndex;
        string anchorId = stops[anchorIndex];
        if (!points.TryGetValue(anchorId, out SvgPoint anchor))
        {
            return;
        }

        SvgPoint anchorDirection = anchorIndex == startIndex
            ? direction
            : new SvgPoint(-direction.X, -direction.Y);
        double maxMove = Math.Max(Math.Max(preferredSpacing * 6.0, gridSize * 18), pathLength * 0.9);

        for (int i = startIndex; i <= endIndex; i++)
        {
            string stationId = stops[i];
            if (i == anchorIndex || IsSchematicMapSimpleRunAnchor(stationId, degreeByStation, interchangeStationIds, stationFamilies))
            {
                continue;
            }

            if (!original.TryGetValue(stationId, out SvgPoint originalPoint))
            {
                continue;
            }

            int offset = Math.Abs(i - anchorIndex);
            double distanceFromAnchor = anchorIndex == startIndex
                ? SumSchematicMapSegmentSpacings(segmentSpacings, 0, offset)
                : SumSchematicMapSegmentSpacings(segmentSpacings, offset, segmentSpacings.Length);
            SvgPoint proposed = new(
                anchor.X + anchorDirection.X * distanceFromAnchor,
                anchor.Y + anchorDirection.Y * distanceFromAnchor);
            proposed = SnapPointToGrid(proposed, gridSize, bounds);
            proposed = new SvgPoint(
                Math.Clamp(proposed.X, bounds.Left, bounds.Right),
                Math.Clamp(proposed.Y, bounds.Top, bounds.Bottom));

            if (Distance(originalPoint, proposed) > maxMove)
            {
                continue;
            }

            AddSchematicMapProposal(proposals, stationId, proposed);
        }
    }

    private static void AddSchematicMapFixedEndpointRunProposals(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        List<string> stops,
        int startIndex,
        int endIndex,
        double[] segmentSpacings,
        Dictionary<string, List<SvgPoint>> proposals,
        SvgRect bounds,
        double gridSize)
    {
        SvgPoint start = points[stops[startIndex]];
        SvgPoint end = points[stops[endIndex]];
        int count = endIndex - startIndex + 1;
        double totalSpacing = Math.Max(0.001, segmentSpacings.Sum());
        SvgPoint direct = new(end.X - start.X, end.Y - start.Y);
        double directLength = Math.Max(0.001, Distance(start, end));
        SvgPoint spineDirection = QuantizeSchematicDirection(direct, new SvgPoint(1, 0));
        if (Dot(direct, spineDirection) < 0)
        {
            spineDirection = new SvgPoint(-spineDirection.X, -spineDirection.Y);
        }

        double spineLength = Math.Max(0.001, Dot(direct, spineDirection));
        SvgPoint projectedEnd = new(start.X + spineDirection.X * spineLength, start.Y + spineDirection.Y * spineLength);
        double endpointLateralOffset = Distance(end, projectedEnd);
        bool useOctilinearSpine = count >= 4
            && spineLength >= gridSize * 4.0
            && endpointLateralOffset <= Math.Max(gridSize * 1.9, directLength * 0.20);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            string stationId = stops[i];
            if (!original.TryGetValue(stationId, out SvgPoint originalPoint))
            {
                continue;
            }

            double distanceAlongRun = SumSchematicMapSegmentSpacings(segmentSpacings, 0, i - startIndex);
            double t = distanceAlongRun / totalSpacing;
            SvgPoint proposed = useOctilinearSpine
                ? new SvgPoint(
                    start.X + spineDirection.X * spineLength * t,
                    start.Y + spineDirection.Y * spineLength * t)
                : new SvgPoint(
                    start.X + (end.X - start.X) * t,
                    start.Y + (end.Y - start.Y) * t);
            proposed = SnapPointToGrid(proposed, gridSize, bounds);
            proposed = new SvgPoint(
                Math.Clamp(proposed.X, bounds.Left, bounds.Right),
                Math.Clamp(proposed.Y, bounds.Top, bounds.Bottom));

            double maxMove = useOctilinearSpine
                ? Math.Max(gridSize * 10.0, directLength * 0.55)
                : Math.Max(gridSize * 8.0, directLength * 0.45);
            if (Distance(originalPoint, proposed) > maxMove)
            {
                continue;
            }

            AddSchematicMapProposal(proposals, stationId, proposed);
        }
    }

    private static double ResolveSchematicMapLinearSpacing(
        double averageSpacing,
        double preferredSpacing,
        double minSpacing,
        double maxOrdinarySpacing)
    {
        if (averageSpacing > preferredSpacing * 2.6)
        {
            return Math.Clamp(averageSpacing, preferredSpacing, maxOrdinarySpacing * 1.55);
        }

        return Math.Clamp(preferredSpacing, minSpacing, maxOrdinarySpacing);
    }

    private static double[] ResolveSchematicMapSegmentSpacings(
        Dictionary<string, SvgPoint> points,
        List<string> stops,
        int startIndex,
        int endIndex,
        double ordinarySpacing,
        double preferredSpacing,
        double minSpacing,
        double maxOrdinarySpacing)
    {
        int segmentCount = Math.Max(0, endIndex - startIndex);
        if (segmentCount == 0)
        {
            return [];
        }

        double[] sourceLengths = new double[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            string firstId = stops[startIndex + i];
            string secondId = stops[startIndex + i + 1];
            sourceLengths[i] = points.TryGetValue(firstId, out SvgPoint first)
                && points.TryGetValue(secondId, out SvgPoint second)
                    ? Distance(first, second)
                    : ordinarySpacing;
        }

        double medianLength = MedianPositive(sourceLengths);
        if (medianLength <= 0.001)
        {
            return Enumerable.Repeat(ordinarySpacing, segmentCount).ToArray();
        }

        double longSegmentThreshold = Math.Max(medianLength * 2.15, preferredSpacing * 1.9);
        double longestSpacing = Math.Max(maxOrdinarySpacing * 1.65, ordinarySpacing * 1.9);
        double[] spacings = new double[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            double sourceLength = sourceLengths[i];
            if (sourceLength >= longSegmentThreshold)
            {
                double longFactor = Math.Clamp(sourceLength / medianLength, 1.0, 2.15);
                spacings[i] = Math.Clamp(ordinarySpacing * longFactor, minSpacing, longestSpacing);
            }
            else
            {
                spacings[i] = ordinarySpacing;
            }
        }

        return spacings;
    }

    private static double MedianPositive(IEnumerable<double> values)
    {
        List<double> positives = values
            .Where(value => value > 0.001)
            .OrderBy(value => value)
            .ToList();
        if (positives.Count == 0)
        {
            return 0;
        }

        int midpoint = positives.Count / 2;
        return positives.Count % 2 == 1
            ? positives[midpoint]
            : (positives[midpoint - 1] + positives[midpoint]) / 2.0;
    }

    private static double SumSchematicMapSegmentSpacings(double[] segmentSpacings, int start, int end)
    {
        double total = 0;
        int clampedStart = Math.Clamp(start, 0, segmentSpacings.Length);
        int clampedEnd = Math.Clamp(end, clampedStart, segmentSpacings.Length);
        for (int i = clampedStart; i < clampedEnd; i++)
        {
            total += segmentSpacings[i];
        }

        return total;
    }

    private static void AddSchematicMapProposal(
        Dictionary<string, List<SvgPoint>> proposals,
        string stationId,
        SvgPoint proposed)
    {
        if (!proposals.TryGetValue(stationId, out List<SvgPoint>? stationProposals))
        {
            stationProposals = [];
            proposals[stationId] = stationProposals;
        }

        stationProposals.Add(proposed);
    }

    private static List<SchematicV2FamilyPath> BuildSchematicMapAllFamilyPaths(
        List<SchematicV2FamilyPath> familyPaths,
        Dictionary<string, List<string>> routeGuideByFamily)
    {
        List<SchematicV2FamilyPath> allPaths = familyPaths.ToList();
        foreach ((string familyKey, List<string> guideStops) in routeGuideByFamily)
        {
            List<string> stops = RemoveConsecutiveDuplicateStops(guideStops);
            if (stops.Count >= 2)
            {
                allPaths.Add(new SchematicV2FamilyPath(familyKey, stops, new SvgPoint(1, 0)));
            }
        }

        return allPaths;
    }

    private static Dictionary<string, string> BuildSchematicMapVisibleLaneKeyByFamilyKey(IEnumerable<DisplayLineFamily> families)
    {
        return families
            .GroupBy(family => family.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => VisibleLaneResolver.CreateKey(group.First()).Key,
                StringComparer.Ordinal);
    }

    private static Dictionary<string, HashSet<string>> BuildSchematicMapStationFamilies(
        List<SchematicV2FamilyPath> allPaths,
        Dictionary<string, string> visibleLaneKeyByFamilyKey)
    {
        Dictionary<string, HashSet<string>> stationFamilies = new(StringComparer.Ordinal);
        foreach (SchematicV2FamilyPath path in allPaths)
        {
            string visibleLaneKey = GetSchematicMapVisibleLaneKey(path.FamilyKey, visibleLaneKeyByFamilyKey);
            foreach (string stationId in path.Stops)
            {
                if (!stationFamilies.TryGetValue(stationId, out HashSet<string>? families))
                {
                    families = new HashSet<string>(StringComparer.Ordinal);
                    stationFamilies[stationId] = families;
                }

                families.Add(visibleLaneKey);
            }
        }

        return stationFamilies;
    }

    private static string GetSchematicMapVisibleLaneKey(string familyKey, Dictionary<string, string> visibleLaneKeyByFamilyKey)
    {
        return visibleLaneKeyByFamilyKey.TryGetValue(familyKey, out string? visibleLaneKey)
            ? visibleLaneKey
            : familyKey;
    }

    private static bool IsSchematicMapSimpleRunAnchor(
        string stationId,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups)
    {
        int degree = GetSchematicV2NodeDegree(stationId, degreeByStation);
        bool hasVisibleLaneInfo = stationVisibleLaneGroups.TryGetValue(stationId, out HashSet<string>? groups) && groups.Count > 0;
        return degree >= 3
            || (hasVisibleLaneInfo && groups!.Count > 1)
            || (interchangeStationIds.Contains(stationId) && !hasVisibleLaneInfo);
    }

    private static int ApplySchematicMapLocalClearance(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        List<SchematicV2FamilyPath> allPaths,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups,
        Dictionary<string, string> visibleLaneKeyByFamilyKey,
        SvgRect bounds,
        double gridSize,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap || !options.EnableSchematicMapLocalClearance)
        {
            return 0;
        }

        if (allPaths.Count < 2)
        {
            return 0;
        }

        double clearance = options.SchematicMapLocalClearanceDistance > 0
            ? options.SchematicMapLocalClearanceDistance
            : Math.Max(options.GridSize * 2.25, options.LineWidth * 5.0);
        double maxStep = Math.Max(gridSize * 1.5, options.LineWidth * 3.0);
        double maxTotalMove = Math.Max(clearance * 1.5, gridSize * 5.0);
        HashSet<string> adjustedStations = new(StringComparer.Ordinal);

        for (int pass = 0; pass < 3; pass++)
        {
            Dictionary<string, List<SvgPoint>> proposals = new(StringComparer.Ordinal);
            foreach ((string stationId, SvgPoint point) in points.ToList())
            {
                if (IsSchematicMapLocalClearanceAnchor(stationId, degreeByStation, interchangeStationIds, stationVisibleLaneGroups)
                    || !original.TryGetValue(stationId, out SvgPoint originalPoint)
                    || !stationVisibleLaneGroups.TryGetValue(stationId, out HashSet<string>? ownVisibleLaneGroups))
                {
                    continue;
                }

                foreach (SchematicV2FamilyPath path in allPaths)
                {
                    string pathVisibleLaneKey = GetSchematicMapVisibleLaneKey(path.FamilyKey, visibleLaneKeyByFamilyKey);
                    if (ownVisibleLaneGroups.Contains(pathVisibleLaneKey))
                    {
                        continue;
                    }

                    for (int i = 1; i < path.Stops.Count; i++)
                    {
                        string startId = path.Stops[i - 1];
                        string endId = path.Stops[i];
                        if (string.Equals(startId, endId, StringComparison.Ordinal)
                            || string.Equals(startId, stationId, StringComparison.Ordinal)
                            || string.Equals(endId, stationId, StringComparison.Ordinal)
                            || !points.TryGetValue(startId, out SvgPoint start)
                            || !points.TryGetValue(endId, out SvgPoint end))
                        {
                            continue;
                        }

                        double segmentLength = Distance(start, end);
                        if (segmentLength <= 0.001)
                        {
                            continue;
                        }

                        SvgPoint projected = ProjectPointToSegment(point, start, end);
                        double distance = Distance(point, projected);
                        if (distance >= clearance)
                        {
                            continue;
                        }

                        SvgPoint pushDirection;
                        if (distance > 0.001)
                        {
                            pushDirection = Normalize(new SvgPoint(point.X - projected.X, point.Y - projected.Y));
                        }
                        else
                        {
                            SvgPoint segmentDirection = Normalize(new SvgPoint(end.X - start.X, end.Y - start.Y));
                            pushDirection = new SvgPoint(-segmentDirection.Y, segmentDirection.X);
                            if (GetStableSign(stationId, path.FamilyKey) < 0)
                            {
                                pushDirection = new SvgPoint(-pushDirection.X, -pushDirection.Y);
                            }
                        }

                        double step = Math.Min(maxStep, (clearance - distance) * 0.55);
                        if (step <= 0.001)
                        {
                            continue;
                        }

                        SvgPoint proposed = new(point.X + pushDirection.X * step, point.Y + pushDirection.Y * step);
                        proposed = SnapPointToGrid(proposed, gridSize, bounds);
                        proposed = new SvgPoint(
                            Math.Clamp(proposed.X, bounds.Left, bounds.Right),
                            Math.Clamp(proposed.Y, bounds.Top, bounds.Bottom));

                        if (Distance(originalPoint, proposed) > maxTotalMove || Distance(point, proposed) <= 0.001)
                        {
                            continue;
                        }

                        if (!proposals.TryGetValue(stationId, out List<SvgPoint>? stationProposals))
                        {
                            stationProposals = [];
                            proposals[stationId] = stationProposals;
                        }

                        stationProposals.Add(proposed);
                    }
                }
            }

            if (proposals.Count == 0)
            {
                break;
            }

            foreach ((string stationId, List<SvgPoint> stationProposals) in proposals)
            {
                SvgPoint average = new(
                    stationProposals.Average(item => item.X),
                    stationProposals.Average(item => item.Y));
                SvgPoint snapped = SnapPointToGrid(average, gridSize, bounds);
                snapped = new SvgPoint(
                    Math.Clamp(snapped.X, bounds.Left, bounds.Right),
                    Math.Clamp(snapped.Y, bounds.Top, bounds.Bottom));

                if (Distance(points[stationId], snapped) <= 0.001)
                {
                    continue;
                }

                points[stationId] = snapped;
                adjustedStations.Add(stationId);
            }
        }

        return adjustedStations.Count;
    }

    private static int NormalizeSchematicMapOctilinearSegments(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        IEnumerable<IReadOnlyList<string>> routeChains,
        SvgRect bounds,
        double gridSize,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups,
        SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap || !options.EnableSchematicMapOctilinearNormalization)
        {
            return 0;
        }

        List<IReadOnlyList<string>> chains = routeChains
            .Select(chain => RemoveConsecutiveDuplicateStops(chain.ToList()))
            .Where(chain => chain.Count >= 2)
            .Cast<IReadOnlyList<string>>()
            .ToList();
        if (chains.Count == 0)
        {
            return 0;
        }

        double thresholdRadians = Math.Clamp(options.SchematicMapOctilinearSnapAngleDegrees, 1, 30) * Math.PI / 180.0;
        HashSet<string> adjustedStations = new(StringComparer.Ordinal);

        for (int pass = 0; pass < 2; pass++)
        {
            Dictionary<string, List<SvgPoint>> proposals = new(StringComparer.Ordinal);
            foreach (IReadOnlyList<string> chain in chains)
            {
                for (int i = 1; i < chain.Count; i++)
                {
                    string firstId = chain[i - 1];
                    string secondId = chain[i];
                    if (string.Equals(firstId, secondId, StringComparison.Ordinal)
                        || !points.TryGetValue(firstId, out SvgPoint first)
                        || !points.TryGetValue(secondId, out SvgPoint second)
                        || !TryGetNearbyOctilinearDirection(first, second, thresholdRadians, out SvgPoint direction))
                    {
                        continue;
                    }

                    bool firstLocked = IsSchematicMapOctilinearAnchor(firstId, degreeByStation, interchangeStationIds, stationVisibleLaneGroups);
                    bool secondLocked = IsSchematicMapOctilinearAnchor(secondId, degreeByStation, interchangeStationIds, stationVisibleLaneGroups);
                    if (firstLocked && secondLocked)
                    {
                        continue;
                    }

                    string movableId;
                    SvgPoint anchor;
                    SvgPoint movable;
                    SvgPoint vectorDirection;
                    if (firstLocked || !secondLocked)
                    {
                        movableId = secondId;
                        anchor = first;
                        movable = second;
                        vectorDirection = direction;
                    }
                    else
                    {
                        movableId = firstId;
                        anchor = second;
                        movable = first;
                        vectorDirection = new SvgPoint(-direction.X, -direction.Y);
                    }

                    if (IsSchematicMapOctilinearAnchor(movableId, degreeByStation, interchangeStationIds, stationVisibleLaneGroups))
                    {
                        continue;
                    }

                    double length = Distance(anchor, movable);
                    if (length <= 0.001 || !original.ContainsKey(movableId))
                    {
                        continue;
                    }

                    SvgPoint proposed = new(anchor.X + vectorDirection.X * length, anchor.Y + vectorDirection.Y * length);
                    double proposalMove = Distance(movable, proposed);
                    double maxProposalMove = Math.Max(gridSize * 8.0, length * 0.28);
                    if (proposalMove <= 0.001 || proposalMove > maxProposalMove)
                    {
                        continue;
                    }

                    proposed = SnapPointToGrid(proposed, gridSize, bounds);
                    proposed = new SvgPoint(
                        Math.Clamp(proposed.X, bounds.Left, bounds.Right),
                        Math.Clamp(proposed.Y, bounds.Top, bounds.Bottom));

                    if (Distance(points[movableId], proposed) <= 0.001)
                    {
                        continue;
                    }

                    if (!proposals.TryGetValue(movableId, out List<SvgPoint>? stationProposals))
                    {
                        stationProposals = [];
                        proposals[movableId] = stationProposals;
                    }

                    stationProposals.Add(proposed);
                }
            }

            if (proposals.Count == 0)
            {
                break;
            }

            foreach ((string stationId, List<SvgPoint> stationProposals) in proposals)
            {
                SvgPoint average = new(
                    stationProposals.Average(point => point.X),
                    stationProposals.Average(point => point.Y));
                SvgPoint snapped = SnapPointToGrid(average, gridSize, bounds);
                snapped = new SvgPoint(
                    Math.Clamp(snapped.X, bounds.Left, bounds.Right),
                    Math.Clamp(snapped.Y, bounds.Top, bounds.Bottom));
                if (Distance(points[stationId], snapped) <= 0.001)
                {
                    continue;
                }

                points[stationId] = snapped;
                adjustedStations.Add(stationId);
            }
        }

        return adjustedStations.Count;
    }

    private static int StraightenSchematicMapShallowKinks(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        IEnumerable<IReadOnlyList<string>> routeChains,
        SvgRect bounds,
        double gridSize,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups,
        SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap)
        {
            return 0;
        }

        List<IReadOnlyList<string>> chains = routeChains
            .Select(chain => RemoveConsecutiveDuplicateStops(chain.ToList()))
            .Where(chain => chain.Count >= 3)
            .Cast<IReadOnlyList<string>>()
            .ToList();
        if (chains.Count == 0)
        {
            return 0;
        }

        double thresholdRadians = Math.Clamp(options.SchematicMapOctilinearSnapAngleDegrees, 1, 30) * Math.PI / 180.0;
        double maxMove = Math.Max(options.GridSize * 2.0, options.LineWidth * 5.0);
        double minNeighborLength = Math.Max(options.LineWidth * 5.0, gridSize * 2.5);
        HashSet<string> adjustedStations = new(StringComparer.Ordinal);

        for (int pass = 0; pass < 2; pass++)
        {
            Dictionary<string, List<SvgPoint>> proposals = new(StringComparer.Ordinal);
            foreach (IReadOnlyList<string> chain in chains)
            {
                for (int i = 1; i < chain.Count - 1; i++)
                {
                    string previousId = chain[i - 1];
                    string stationId = chain[i];
                    string nextId = chain[i + 1];
                    if (string.Equals(previousId, stationId, StringComparison.Ordinal)
                        || string.Equals(stationId, nextId, StringComparison.Ordinal)
                        || IsSchematicMapOctilinearAnchor(stationId, degreeByStation, interchangeStationIds, stationVisibleLaneGroups)
                        || !original.ContainsKey(stationId)
                        || !points.TryGetValue(previousId, out SvgPoint previous)
                        || !points.TryGetValue(stationId, out SvgPoint current)
                        || !points.TryGetValue(nextId, out SvgPoint next))
                    {
                        continue;
                    }

                    double previousLength = Distance(previous, current);
                    double nextLength = Distance(current, next);
                    if (previousLength < minNeighborLength || nextLength < minNeighborLength)
                    {
                        continue;
                    }

                    SvgPoint corridor = new(next.X - previous.X, next.Y - previous.Y);
                    if (Distance(previous, next) < minNeighborLength
                        || !TryGetNearbyOctilinearDirection(previous, next, thresholdRadians, out SvgPoint direction))
                    {
                        continue;
                    }

                    if (Dot(corridor, direction) < 0)
                    {
                        direction = new SvgPoint(-direction.X, -direction.Y);
                    }

                    double corridorLength = Dot(corridor, direction);
                    if (corridorLength < minNeighborLength * 2.0)
                    {
                        continue;
                    }

                    double projectionDistance = Dot(new SvgPoint(current.X - previous.X, current.Y - previous.Y), direction);
                    double endpointGuard = Math.Min(minNeighborLength * 0.7, corridorLength * 0.25);
                    if (projectionDistance <= endpointGuard || projectionDistance >= corridorLength - endpointGuard)
                    {
                        continue;
                    }

                    SvgPoint projected = new(
                        previous.X + direction.X * projectionDistance,
                        previous.Y + direction.Y * projectionDistance);
                    projected = new SvgPoint(
                        Math.Clamp(projected.X, bounds.Left, bounds.Right),
                        Math.Clamp(projected.Y, bounds.Top, bounds.Bottom));

                    double move = Distance(current, projected);
                    if (move <= 0.001 || move > maxMove)
                    {
                        continue;
                    }

                    if (!proposals.TryGetValue(stationId, out List<SvgPoint>? stationProposals))
                    {
                        stationProposals = [];
                        proposals[stationId] = stationProposals;
                    }

                    stationProposals.Add(projected);
                }
            }

            if (proposals.Count == 0)
            {
                break;
            }

            foreach ((string stationId, List<SvgPoint> stationProposals) in proposals)
            {
                SvgPoint average = new(
                    stationProposals.Average(point => point.X),
                    stationProposals.Average(point => point.Y));
                SvgPoint current = points[stationId];
                if (Distance(current, average) <= 0.001 || Distance(current, average) > maxMove)
                {
                    continue;
                }

                points[stationId] = average;
                adjustedStations.Add(stationId);
            }
        }

        return adjustedStations.Count;
    }

    private static int SeparateSchematicMapNonAdjacentRouteOverlaps(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        IEnumerable<IReadOnlyList<string>> routeChains,
        SvgRect bounds,
        double minimumSpacing,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap)
        {
            return 0;
        }

        List<IReadOnlyList<string>> chains = routeChains
            .Select(chain => NormalizeSchematicMapRenderRouteChain(chain.ToList()))
            .Where(chain => chain.Count >= 4)
            .Cast<IReadOnlyList<string>>()
            .ToList();
        if (chains.Count == 0)
        {
            return 0;
        }

        HashSet<string> adjacentPairs = [];
        foreach (IReadOnlyList<string> chain in chains)
        {
            for (int i = 1; i < chain.Count; i++)
            {
                if (!string.Equals(chain[i - 1], chain[i], StringComparison.Ordinal))
                {
                    adjacentPairs.Add(CreateStationPairKey(chain[i - 1], chain[i]));
                }
            }
        }

        double targetDistance = Math.Max(minimumSpacing, Math.Max(options.GridSize * 1.35, options.LineWidth * 3.2));
        double conflictDistance = Math.Max(minimumSpacing, Math.Max(options.LineWidth * 2.5, targetDistance * 0.9));
        double maxPushPerPass = Math.Max(options.GridSize * 1.2, options.LineWidth * 5.0);
        HashSet<string> adjustedStations = new(StringComparer.Ordinal);

        for (int pass = 0; pass < 5; pass++)
        {
            bool moved = false;
            HashSet<string> processedPairs = new(StringComparer.Ordinal);

            foreach (IReadOnlyList<string> chain in chains)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    string firstId = chain[i];
                    for (int j = i + 2; j < chain.Count; j++)
                    {
                        string secondId = chain[j];
                        if (string.Equals(firstId, secondId, StringComparison.Ordinal)
                            || !points.TryGetValue(firstId, out SvgPoint first)
                            || !points.TryGetValue(secondId, out SvgPoint second))
                        {
                            continue;
                        }

                        string pairKey = CreateStationPairKey(firstId, secondId);
                        if (adjacentPairs.Contains(pairKey) || !processedPairs.Add(pairKey))
                        {
                            continue;
                        }

                        double distance = Distance(first, second);
                        if (distance >= conflictDistance)
                        {
                            continue;
                        }

                        SvgPoint direction = ResolveSchematicMapRouteOverlapDirection(
                            first,
                            second,
                            firstId,
                            secondId,
                            original,
                            targetDistance);
                        if (Distance(direction, new SvgPoint(0, 0)) <= 0.001)
                        {
                            continue;
                        }

                        double push = Math.Min(maxPushPerPass, (targetDistance - distance) * 0.58);
                        if (push <= 0.001)
                        {
                            continue;
                        }

                        double firstWeight = GetSchematicStationAnchorWeight(firstId, degreeByStation, interchangeStationIds);
                        double secondWeight = GetSchematicStationAnchorWeight(secondId, degreeByStation, interchangeStationIds);
                        double firstShare = secondWeight / (firstWeight + secondWeight);
                        double secondShare = firstWeight / (firstWeight + secondWeight);

                        SvgPoint movedFirst = ClampToBounds(new SvgPoint(
                            first.X - direction.X * push * firstShare,
                            first.Y - direction.Y * push * firstShare), bounds);
                        SvgPoint movedSecond = ClampToBounds(new SvgPoint(
                            second.X + direction.X * push * secondShare,
                            second.Y + direction.Y * push * secondShare), bounds);

                        if (Distance(first, movedFirst) <= 0.001 && Distance(second, movedSecond) <= 0.001)
                        {
                            continue;
                        }

                        points[firstId] = movedFirst;
                        points[secondId] = movedSecond;
                        adjustedStations.Add(firstId);
                        adjustedStations.Add(secondId);
                        moved = true;
                    }
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return adjustedStations.Count;
    }

    private static SvgPoint ResolveSchematicMapRouteOverlapDirection(
        SvgPoint first,
        SvgPoint second,
        string firstId,
        string secondId,
        Dictionary<string, SvgPoint> original,
        double targetDistance)
    {
        SvgPoint currentVector = new(second.X - first.X, second.Y - first.Y);
        if (Distance(new SvgPoint(0, 0), currentVector) >= targetDistance * 0.25)
        {
            return Normalize(currentVector);
        }

        if (original.TryGetValue(firstId, out SvgPoint originalFirst)
            && original.TryGetValue(secondId, out SvgPoint originalSecond))
        {
            SvgPoint originalVector = new(originalSecond.X - originalFirst.X, originalSecond.Y - originalFirst.Y);
            if (Distance(new SvgPoint(0, 0), originalVector) > 0.001)
            {
                return QuantizeSchematicDirection(originalVector, GetSchematicSeparationDirection(first, second, firstId, secondId));
            }
        }

        return GetSchematicSeparationDirection(first, second, firstId, secondId);
    }

    private static SvgPoint ClampToBounds(SvgPoint point, SvgRect bounds)
    {
        return new SvgPoint(
            Math.Clamp(point.X, bounds.Left, bounds.Right),
            Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
    }

    private static bool IsSchematicMapOctilinearAnchor(
        string stationId,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups)
    {
        int degree = GetSchematicV2NodeDegree(stationId, degreeByStation);
        bool hasVisibleLaneInfo = stationVisibleLaneGroups.TryGetValue(stationId, out HashSet<string>? groups) && groups.Count > 0;
        return degree >= 3
            || (hasVisibleLaneInfo && groups!.Count > 1)
            || (interchangeStationIds.Contains(stationId) && !hasVisibleLaneInfo);
    }

    private static bool IsSchematicMapLocalClearanceAnchor(
        string stationId,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        Dictionary<string, HashSet<string>> stationVisibleLaneGroups)
    {
        int degree = GetSchematicV2NodeDegree(stationId, degreeByStation);
        bool hasVisibleLaneInfo = stationVisibleLaneGroups.TryGetValue(stationId, out HashSet<string>? groups) && groups.Count > 0;
        return degree >= 3
            || (hasVisibleLaneInfo && groups!.Count > 1)
            || (interchangeStationIds.Contains(stationId) && !hasVisibleLaneInfo);
    }

    private static int GetStableSign(string first, string second)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in first)
            {
                hash = (hash * 31) + character;
            }

            foreach (char character in second)
            {
                hash = (hash * 31) + character;
            }

            return (hash & 1) == 0 ? 1 : -1;
        }
    }

    private static bool TryGetNearbyOctilinearDirection(
        SvgPoint first,
        SvgPoint second,
        double thresholdRadians,
        out SvgPoint direction)
    {
        SvgPoint vector = new(second.X - first.X, second.Y - first.Y);
        double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length <= 0.001)
        {
            direction = new SvgPoint(1, 0);
            return false;
        }

        direction = QuantizeSchematicDirection(vector, new SvgPoint(1, 0));
        double dot = (vector.X / length * direction.X) + (vector.Y / length * direction.Y);
        dot = Math.Clamp(dot, -1, 1);
        double angle = Math.Acos(dot);
        return angle <= thresholdRadians;
    }

    private static int RelaxSchematicV2SharpAngles(
        Dictionary<string, SvgPoint> points,
        IEnumerable<IReadOnlyList<string>> routeChains,
        SvgRect bounds,
        double gridSize)
    {
        Dictionary<string, List<SvgPoint>> proposals = new(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> routeChain in routeChains)
        {
            for (int i = 1; i < routeChain.Count - 1; i++)
            {
                string previousId = routeChain[i - 1];
                string currentId = routeChain[i];
                string nextId = routeChain[i + 1];
                if (string.Equals(previousId, currentId, StringComparison.Ordinal)
                    || string.Equals(currentId, nextId, StringComparison.Ordinal)
                    || string.Equals(previousId, nextId, StringComparison.Ordinal)
                    || !points.TryGetValue(previousId, out SvgPoint previous)
                    || !points.TryGetValue(currentId, out SvgPoint current)
                    || !points.TryGetValue(nextId, out SvgPoint next))
                {
                    continue;
                }

                if (!IsSchematicV2SharpDetour(previous, current, next, gridSize))
                {
                    continue;
                }

                SvgPoint midpoint = new((previous.X + next.X) / 2, (previous.Y + next.Y) / 2);
                SvgPoint relaxed = new(
                    current.X + (midpoint.X - current.X) * 0.88,
                    current.Y + (midpoint.Y - current.Y) * 0.88);
                relaxed = SnapPointToGrid(relaxed, Math.Max(2, gridSize), bounds);
                if (Distance(relaxed, current) <= 0.001)
                {
                    continue;
                }

                if (!proposals.TryGetValue(currentId, out List<SvgPoint>? stationProposals))
                {
                    stationProposals = [];
                    proposals[currentId] = stationProposals;
                }

                stationProposals.Add(relaxed);
            }
        }

        foreach ((string stationId, List<SvgPoint> stationProposals) in proposals)
        {
            if (stationProposals.Count == 0)
            {
                continue;
            }

            SvgPoint average = new(
                stationProposals.Average(point => point.X),
                stationProposals.Average(point => point.Y));
            points[stationId] = SnapPointToGrid(average, Math.Max(2, gridSize), bounds);
        }

        return proposals.Count;
    }

    private static bool IsSchematicV2SharpDetour(SvgPoint previous, SvgPoint current, SvgPoint next, double gridSize)
    {
        double previousDistance = Distance(previous, current);
        double nextDistance = Distance(current, next);
        double directDistance = Distance(previous, next);
        if (previousDistance <= 0.001 || nextDistance <= 0.001 || directDistance < Math.Max(1, gridSize * 2))
        {
            return false;
        }

        double detourRatio = (previousDistance + nextDistance) / directDistance;
        if (detourRatio < 1.55)
        {
            return false;
        }

        double cosine = Dot(
            new SvgPoint(previous.X - current.X, previous.Y - current.Y),
            new SvgPoint(next.X - current.X, next.Y - current.Y)) / (previousDistance * nextDistance);
        cosine = Math.Clamp(cosine, -1, 1);
        double angleDegrees = Math.Acos(cosine) * 180 / Math.PI;
        return angleDegrees < 58;
    }

    private static int StraightenSchematicV2TerminalTails(
        Dictionary<string, SvgPoint> points,
        IEnumerable<List<string>> routeChains,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        double gridSize)
    {
        int straightened = 0;
        foreach (List<string> routeChain in routeChains)
        {
            List<string> stops = RemoveConsecutiveDuplicateStops(routeChain);
            if (stops.Count < 4)
            {
                continue;
            }

            if (TryStraightenSchematicV2TerminalTail(points, stops, degreeByStation, interchangeStationIds, gridSize, fromStart: true))
            {
                straightened++;
            }

            if (TryStraightenSchematicV2TerminalTail(points, stops, degreeByStation, interchangeStationIds, gridSize, fromStart: false))
            {
                straightened++;
            }
        }

        return straightened;
    }

    private static bool TryStraightenSchematicV2TerminalTail(
        Dictionary<string, SvgPoint> points,
        List<string> routeChain,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds,
        double gridSize,
        bool fromStart)
    {
        int endpointIndex = fromStart ? 0 : routeChain.Count - 1;
        string endpointId = routeChain[endpointIndex];
        if (GetSchematicV2NodeDegree(endpointId, degreeByStation) > 1 || interchangeStationIds.Contains(endpointId))
        {
            return false;
        }

        List<string> tail = [];
        for (int offset = 0; offset < routeChain.Count; offset++)
        {
            int index = fromStart ? offset : routeChain.Count - 1 - offset;
            string stationId = routeChain[index];
            tail.Add(stationId);

            if (offset == 0)
            {
                continue;
            }

            bool isAnchor = interchangeStationIds.Contains(stationId) || GetSchematicV2NodeDegree(stationId, degreeByStation) >= 3;
            if (isAnchor)
            {
                break;
            }

            if (tail.Count >= 6)
            {
                break;
            }
        }

        if (tail.Count < 4)
        {
            return false;
        }

        string anchorId = tail[^1];
        if (!interchangeStationIds.Contains(anchorId) && GetSchematicV2NodeDegree(anchorId, degreeByStation) < 3)
        {
            return false;
        }

        tail.Reverse();
        if (!tail.All(points.ContainsKey))
        {
            return false;
        }

        SvgPoint anchor = points[tail[0]];
        SvgPoint endpoint = points[tail[^1]];
        double directDistance = Distance(anchor, endpoint);
        if (directDistance < Math.Max(gridSize * 4.0, 1))
        {
            return false;
        }

        double polylineDistance = 0;
        for (int i = 1; i < tail.Count; i++)
        {
            polylineDistance += Distance(points[tail[i - 1]], points[tail[i]]);
        }

        if (polylineDistance / directDistance < 1.22)
        {
            return false;
        }

        double maxPerpendicularDistance = 0;
        for (int i = 1; i < tail.Count - 1; i++)
        {
            maxPerpendicularDistance = Math.Max(maxPerpendicularDistance, DistancePointToLine(points[tail[i]], anchor, endpoint));
        }

        if (maxPerpendicularDistance < gridSize * 1.5)
        {
            return false;
        }

        for (int i = 1; i < tail.Count - 1; i++)
        {
            double t = i / (double)(tail.Count - 1);
            points[tail[i]] = new SvgPoint(
                anchor.X + (endpoint.X - anchor.X) * t,
                anchor.Y + (endpoint.Y - anchor.Y) * t);
        }

        return true;
    }

    private static int GetSchematicV2NodeDegree(string stationId, Dictionary<string, int> degreeByStation)
    {
        return degreeByStation.TryGetValue(stationId, out int degree) ? degree : 0;
    }

    private static double DistancePointToLine(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double denominator = Math.Sqrt(dx * dx + dy * dy);
        if (denominator <= 0.001)
        {
            return Distance(point, start);
        }

        return Math.Abs(((end.X - start.X) * (start.Y - point.Y)) - ((start.X - point.X) * (end.Y - start.Y))) / denominator;
    }

    private static List<SchematicV2FamilyPath> BuildSchematicV2FamilyPaths(
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        List<SchematicV2FamilyPath> paths = [];
        foreach (DisplayLineFamily family in SortFamiliesForLegend(displayFamilies))
        {
            List<string> stops = GetSchematicV2TopologyStops(family, stationsById, stationPoints);
            if (stops.Count < 2)
            {
                continue;
            }

            SvgPoint first = stationPoints[stops[0]];
            SvgPoint last = stationPoints[stops[^1]];
            SvgPoint fallback = new(1, 0);
            paths.Add(new SchematicV2FamilyPath(family.FamilyKey, stops, QuantizeSchematicDirection(new SvgPoint(last.X - first.X, last.Y - first.Y), fallback)));
        }

        return paths;
    }

    private static List<SchematicV2FamilyPath> BuildSchematicV2FamilyPaths(
        CanonicalSchematicNetwork canonicalNetwork,
        Dictionary<string, SvgPoint> stationPoints)
    {
        List<SchematicV2FamilyPath> paths = [];
        foreach (CanonicalServiceFamily family in canonicalNetwork.Families.OrderBy(item => item.DisplayName, StringComparer.CurrentCulture))
        {
            List<string> stops = RemoveConsecutiveDuplicateStops(
                family.CanonicalStops
                    .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationPoints.ContainsKey(stopId))
                    .ToList());
            if (stops.Count < 2)
            {
                continue;
            }

            SvgPoint first = stationPoints[stops[0]];
            SvgPoint last = stationPoints[stops[^1]];
            SvgPoint fallback = new(1, 0);
            paths.Add(new SchematicV2FamilyPath(
                family.FamilyKey,
                stops,
                QuantizeSchematicDirection(new SvgPoint(last.X - first.X, last.Y - first.Y), fallback)));
        }

        return paths;
    }

    private static SchematicV2RouteGuideResult BuildSchematicV2CanonicalRouteGuideByFamily(
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        Dictionary<string, List<string>> routeGuideByFamily = displayFamilies
            .ToDictionary(
                family => family.FamilyKey,
                family => GetSchematicV2TopologyStops(family, stationsById, stationPoints),
                StringComparer.Ordinal);

        return new SchematicV2RouteGuideResult(routeGuideByFamily, []);
    }

    private static SchematicV2RouteGuideResult BuildSchematicV2RouteGuideByFamily(
        List<DisplayLineFamily> displayFamilies,
        CanonicalSchematicNetwork? canonicalNetwork,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints,
        List<SchematicV2GeometryCorridorConstraint> geometryCorridors)
    {
        Dictionary<string, List<string>> routeGuideByFamily = canonicalNetwork is null
            ? displayFamilies
                .ToDictionary(
                    family => family.FamilyKey,
                    family => GetSchematicV2TopologyStops(family, stationsById, stationPoints),
                    StringComparer.Ordinal)
            : canonicalNetwork.Families
                .ToDictionary(
                    family => family.FamilyKey,
                    family => RemoveConsecutiveDuplicateStops(
                        family.CanonicalStops
                            .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationPoints.ContainsKey(stopId))
                            .ToList()),
                    StringComparer.Ordinal);
        Dictionary<string, DisplayLineFamily> familyByKey = displayFamilies.ToDictionary(family => family.FamilyKey, StringComparer.Ordinal);
        Dictionary<string, SchematicV2RouteGuideMetadata> metadataByFamily = new(StringComparer.Ordinal);
        List<SchematicV2GeometryCorridorConstraint> corridorConstraints = [..geometryCorridors];
        HashSet<string> materializedFamilies = new(StringComparer.Ordinal);

        foreach (SchematicV2GeometryCorridorConstraint corridor in corridorConstraints
                     .OrderByDescending(item => CalculateSchematicV2CorridorConfidence(item.SharedLength, item.AverageDistance, item.MaxDistance))
                     .ThenByDescending(item => item.SharedLength)
                     .ThenBy(item => item.FamilyAKey, StringComparer.Ordinal)
                     .ThenBy(item => item.FamilyBKey, StringComparer.Ordinal))
        {
            if (!ShouldMaterializeSchematicV2GeometryCorridor(corridor, routeGuideByFamily, familyByKey))
            {
                continue;
            }

            if (materializedFamilies.Contains(corridor.FamilyAKey) || materializedFamilies.Contains(corridor.FamilyBKey))
            {
                continue;
            }

            bool materialized = false;
            if (routeGuideByFamily.TryGetValue(corridor.FamilyAKey, out List<string>? guideA))
            {
                List<string> updatedGuideA = ApplySchematicV2CorridorGuide(
                    guideA,
                    corridor.FamilyAStartStationId,
                    corridor.FamilyAEndStationId,
                    corridor.GuideStationIds,
                    corridor.UseFullGuideInterval);
                if (!SequencesEqual(guideA, updatedGuideA))
                {
                    routeGuideByFamily[corridor.FamilyAKey] = updatedGuideA;
                    metadataByFamily[corridor.FamilyAKey] = CreateSchematicV2RouteGuideMetadata(corridor);
                    materialized = true;
                }
            }

            if (routeGuideByFamily.TryGetValue(corridor.FamilyBKey, out List<string>? guideB))
            {
                List<string> updatedGuideB = ApplySchematicV2CorridorGuide(
                    guideB,
                    corridor.FamilyBStartStationId,
                    corridor.FamilyBEndStationId,
                    corridor.GuideStationIds,
                    corridor.UseFullGuideInterval);
                if (!SequencesEqual(guideB, updatedGuideB))
                {
                    routeGuideByFamily[corridor.FamilyBKey] = updatedGuideB;
                    metadataByFamily[corridor.FamilyBKey] = CreateSchematicV2RouteGuideMetadata(corridor);
                    materialized = true;
                }
            }

            if (materialized)
            {
                materializedFamilies.Add(corridor.FamilyAKey);
                materializedFamilies.Add(corridor.FamilyBKey);
            }
        }

        foreach (string familyKey in routeGuideByFamily.Keys.ToList())
        {
            routeGuideByFamily[familyKey] = NormalizeSchematicV2RenderRouteChain(routeGuideByFamily[familyKey]);
        }

        return new SchematicV2RouteGuideResult(routeGuideByFamily, metadataByFamily);
    }

    private static bool ShouldMaterializeSchematicV2GeometryCorridor(
        SchematicV2GeometryCorridorConstraint corridor,
        Dictionary<string, List<string>> routeGuideByFamily,
        Dictionary<string, DisplayLineFamily> familyByKey)
    {
        bool hasExpressFamily =
            familyByKey.TryGetValue(corridor.FamilyAKey, out DisplayLineFamily? familyA) && HasExpressServiceVariant(familyA)
            || familyByKey.TryGetValue(corridor.FamilyBKey, out DisplayLineFamily? familyB) && HasExpressServiceVariant(familyB);
        if (!hasExpressFamily)
        {
            return false;
        }

        if (!routeGuideByFamily.TryGetValue(corridor.FamilyAKey, out List<string>? familyAGuide)
            || !routeGuideByFamily.TryGetValue(corridor.FamilyBKey, out List<string>? familyBGuide)
            || !TryFindSharedAdjacentEdge(familyAGuide, familyBGuide, out _))
        {
            return false;
        }

        if (corridor.GuideStationIds.Distinct(StringComparer.Ordinal).Count() < 3)
        {
            return false;
        }

        if (string.Equals(corridor.FamilyAStartStationId, corridor.FamilyAEndStationId, StringComparison.Ordinal)
            || string.Equals(corridor.FamilyBStartStationId, corridor.FamilyBEndStationId, StringComparison.Ordinal))
        {
            return false;
        }

        double confidence = CalculateSchematicV2CorridorConfidence(corridor.SharedLength, corridor.AverageDistance, corridor.MaxDistance);
        return confidence >= 0.65 && corridor.SharedLength >= 100;
    }

    private static bool SequencesEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> NormalizeSchematicV2RenderRouteChain(List<string> routeGuide)
    {
        List<string> stops = RemoveConsecutiveDuplicateStops(routeGuide);
        if (stops.Count < 5)
        {
            return stops;
        }

        List<string> collapsedOutAndBack = CollapseMirroredOutAndBackStops(stops);
        if (collapsedOutAndBack.Count < stops.Count)
        {
            return collapsedOutAndBack;
        }

        int bestRepeatIndex = -1;
        int bestBacktrackCount = 0;
        for (int i = 2; i < stops.Count; i++)
        {
            if (!string.Equals(stops[i], stops[i - 2], StringComparison.Ordinal))
            {
                continue;
            }

            int previous = i - 2;
            int current = i;
            int backtrackCount = 0;
            while (previous >= 0
                   && current < stops.Count
                   && string.Equals(stops[previous], stops[current], StringComparison.Ordinal))
            {
                backtrackCount++;
                previous--;
                current++;
            }

            if (backtrackCount > bestBacktrackCount)
            {
                bestBacktrackCount = backtrackCount;
                bestRepeatIndex = i;
            }
        }

        if (bestBacktrackCount < 2 || bestRepeatIndex < 0)
        {
            return stops;
        }

        return stops.Take(bestRepeatIndex).ToList();
    }

    private static List<string> NormalizeSchematicMapRenderRouteChain(List<string> routeStops)
    {
        List<string> stops = RemoveConsecutiveDuplicateStops(routeStops);
        if (stops.Count < 5)
        {
            return stops;
        }

        List<string> collapsedOutAndBack = CollapseMirroredOutAndBackStops(stops);
        if (collapsedOutAndBack.Count < stops.Count)
        {
            return collapsedOutAndBack;
        }

        return stops;
    }

    private static List<string> CollapseMirroredOutAndBackStops(List<string> stops)
    {
        if (stops.Count < 5)
        {
            return stops;
        }

        int bestPivot = -1;
        int bestMirrorCount = 0;
        for (int pivot = 1; pivot < stops.Count - 1; pivot++)
        {
            int left = pivot - 1;
            int right = pivot + 1;
            int mirrorCount = 0;
            while (left >= 0
                   && right < stops.Count
                   && string.Equals(stops[left], stops[right], StringComparison.Ordinal))
            {
                mirrorCount++;
                left--;
                right++;
            }

            bool reachesRouteEnd = right >= stops.Count;
            if (!reachesRouteEnd || mirrorCount < 2)
            {
                continue;
            }

            if (mirrorCount > bestMirrorCount)
            {
                bestMirrorCount = mirrorCount;
                bestPivot = pivot;
            }
        }

        if (bestPivot < 0)
        {
            return stops;
        }

        return stops.Take(bestPivot + 1).ToList();
    }

    private static List<SchematicV2GeometryCorridorConstraint> BuildSchematicV2TopologySharedEdgeGuideConstraints(
        Dictionary<string, List<string>> routeGuideByFamily,
        List<SchematicV2GeometryCorridorConstraint> existingConstraints)
    {
        HashSet<string> constrainedPairs = existingConstraints
            .Select(constraint => CreateFamilyPairKey(constraint.FamilyAKey, constraint.FamilyBKey))
            .ToHashSet(StringComparer.Ordinal);
        List<string> familyKeys = routeGuideByFamily.Keys.OrderBy(key => key, StringComparer.CurrentCulture).ToList();
        List<SchematicV2GeometryCorridorConstraint> constraints = [];
        for (int i = 0; i < familyKeys.Count; i++)
        {
            for (int j = i + 1; j < familyKeys.Count; j++)
            {
                string familyAKey = familyKeys[i];
                string familyBKey = familyKeys[j];
                string pairKey = CreateFamilyPairKey(familyAKey, familyBKey);
                if (constrainedPairs.Contains(pairKey))
                {
                    continue;
                }

                List<string> stopsA = routeGuideByFamily[familyAKey];
                List<string> stopsB = routeGuideByFamily[familyBKey];
                if (!TryFindSharedAdjacentEdge(stopsA, stopsB, out SharedAdjacentEdge edge))
                {
                    continue;
                }

                bool hostIsA = stopsA.Count >= stopsB.Count;
                List<string> hostStops = hostIsA ? stopsA : stopsB;
                string guideFamilyKey = hostIsA ? familyAKey : familyBKey;
                List<string> guideSlice = hostIsA
                    ? [edge.FamilyAStartStationId, edge.FamilyAEndStationId]
                    : [edge.FamilyBStartStationId, edge.FamilyBEndStationId];

                constraints.Add(new SchematicV2GeometryCorridorConstraint(
                    familyAKey,
                    familyBKey,
                    guideFamilyKey,
                    edge.FamilyAStartStationId,
                    edge.FamilyAEndStationId,
                    edge.FamilyBStartStationId,
                    edge.FamilyBEndStationId,
                    guideSlice,
                    1,
                    0,
                    0,
                    true,
                    false));
                constrainedPairs.Add(pairKey);
            }
        }

        return constraints;
    }

    private static SchematicV2RouteGuideMetadata CreateSchematicV2RouteGuideMetadata(SchematicV2GeometryCorridorConstraint corridor)
    {
        double confidence = CalculateSchematicV2CorridorConfidence(corridor.SharedLength, corridor.AverageDistance, corridor.MaxDistance);
        string corridorId = $"geometry-{StableId(corridor.FamilyAKey)}-{StableId(corridor.FamilyBKey)}";
        return new SchematicV2RouteGuideMetadata(
            corridorId,
            corridor.FamilyAKey,
            corridor.FamilyBKey,
            confidence,
            corridor.SharedLength,
            corridor.AverageDistance,
            corridor.MaxDistance,
            corridor.GuideStationIds);
    }

    private static List<string> ApplySchematicV2CorridorGuide(
        List<string> routeGuide,
        string startStationId,
        string endStationId,
        List<string> guideStationIds,
        bool useFullGuideInterval)
    {
        if (routeGuide.Count < 2
            || guideStationIds.Count < 2
            || string.IsNullOrWhiteSpace(startStationId)
            || string.IsNullOrWhiteSpace(endStationId))
        {
            return routeGuide;
        }

        int startIndex = routeGuide.IndexOf(startStationId);
        int endIndex = routeGuide.IndexOf(endStationId);
        if (startIndex < 0 || endIndex < 0 || startIndex == endIndex)
        {
            return routeGuide;
        }

        bool reversed = endIndex < startIndex;
        int replaceStart = reversed ? endIndex : startIndex;
        int replaceEnd = reversed ? startIndex : endIndex;
        List<string> orientedGuide = OrientSchematicV2GuideStations(guideStationIds, routeGuide[startIndex], routeGuide[endIndex], useFullGuideInterval);
        if (orientedGuide.Count < 2)
        {
            return routeGuide;
        }

        List<string> updated = [];
        updated.AddRange(routeGuide.Take(replaceStart));
        updated.AddRange(orientedGuide);
        updated.AddRange(routeGuide.Skip(replaceEnd + 1));
        return RemoveConsecutiveDuplicateStops(updated);
    }

    private static List<string> OrientSchematicV2GuideStations(
        List<string> guideStationIds,
        string startStationId,
        string endStationId,
        bool useFullGuideInterval)
    {
        int forwardStart = guideStationIds.IndexOf(startStationId);
        int forwardEnd = guideStationIds.IndexOf(endStationId);
        if (forwardStart >= 0 && forwardEnd >= 0 && forwardStart <= forwardEnd)
        {
            if (useFullGuideInterval)
            {
                return guideStationIds.ToList();
            }

            return guideStationIds.Skip(forwardStart).Take(forwardEnd - forwardStart + 1).ToList();
        }

        List<string> reversed = guideStationIds.AsEnumerable().Reverse().ToList();
        int reverseStart = reversed.IndexOf(startStationId);
        int reverseEnd = reversed.IndexOf(endStationId);
        if (reverseStart >= 0 && reverseEnd >= 0 && reverseStart <= reverseEnd)
        {
            if (useFullGuideInterval)
            {
                return reversed;
            }

            return reversed.Skip(reverseStart).Take(reverseEnd - reverseStart + 1).ToList();
        }

        return [];
    }

    private static List<string> RemoveConsecutiveDuplicateStops(List<string> stops)
    {
        List<string> cleaned = [];
        foreach (string stopId in stops)
        {
            if (string.IsNullOrWhiteSpace(stopId))
            {
                continue;
            }

            if (cleaned.Count == 0 || !string.Equals(cleaned[^1], stopId, StringComparison.Ordinal))
            {
                cleaned.Add(stopId);
            }
        }

        return cleaned;
    }

    private static List<SchematicV2GeometryCorridorConstraint> DetectSchematicV2GeometryCorridors(
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> geographicPoints,
        CoordinateProjector projector,
        SvgRenderOptions options)
    {
        List<SchematicV2ProjectedLine> projectedLines = displayFamilies
            .Select(family => CreateSchematicV2ProjectedLine(family, stationsById, geographicPoints, projector, options))
            .Where(line => line.HasValue)
            .Select(line => line!.Value)
            .ToList();

        List<SchematicV2GeometryCorridorConstraint> constraints = [];
        for (int i = 0; i < projectedLines.Count; i++)
        {
            for (int j = i + 1; j < projectedLines.Count; j++)
            {
                SchematicV2ProjectedLine familyA = projectedLines[i];
                SchematicV2ProjectedLine familyB = projectedLines[j];
                foreach (GeometrySharedCorridorRun run in FindGeometrySharedCorridorRuns(familyA.PathPoints, familyB.PathPoints, options)
                             .OrderByDescending(item => item.SharedLength)
                             .ThenBy(item => item.AverageDistance)
                             .ThenBy(item => item.MaxDistance))
                {
                    if (TryBuildSchematicV2GeometryCorridorConstraint(familyA, familyB, run, options, out SchematicV2GeometryCorridorConstraint constraint))
                    {
                        constraints.Add(constraint);
                    }
                }
            }
        }

        return constraints;
    }

    private static SchematicV2ProjectedLine? CreateSchematicV2ProjectedLine(
        DisplayLineFamily family,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> geographicPoints,
        CoordinateProjector projector,
        SvgRenderOptions options)
    {
        List<string> topologyStops = GetSchematicV2TopologyStops(family, stationsById, geographicPoints);
        if (topologyStops.Count < 2)
        {
            return null;
        }

        List<SvgPoint> stopPoints = topologyStops
            .Where(geographicPoints.ContainsKey)
            .Select(stopId => geographicPoints[stopId])
            .ToList();
        if (stopPoints.Count < 2)
        {
            return null;
        }

        List<MetroPathPoint> rawPathPoints = RemoveConsecutiveDuplicatePathPoints((family.PrimaryLine.PathPoints ?? []).ToList(), PathPointDuplicateEpsilon);
        if (rawPathPoints.Count < 2)
        {
            return null;
        }

        List<SvgPoint> pathPoints = [];
        foreach (MetroPathPoint pathPoint in rawPathPoints)
        {
            AddPointIfNotDuplicate(pathPoints, projector.Project(pathPoint.X, pathPoint.Z));
        }

        if (pathPoints.Count < 2)
        {
            return null;
        }

        return new SchematicV2ProjectedLine(family, family.PrimaryLine, topologyStops, LimitSchematicV2GeometryPathPoints(pathPoints), stopPoints);
    }

    private static List<SvgPoint> LimitSchematicV2GeometryPathPoints(List<SvgPoint> pathPoints)
    {
        const int maxDetectionPoints = 90;
        if (pathPoints.Count <= maxDetectionPoints)
        {
            return pathPoints;
        }

        List<SvgPoint> sampled = [];
        double step = (pathPoints.Count - 1) / (double)(maxDetectionPoints - 1);
        for (int i = 0; i < maxDetectionPoints; i++)
        {
            int index = (int)Math.Round(i * step);
            index = Math.Clamp(index, 0, pathPoints.Count - 1);
            AddPointIfNotDuplicate(sampled, pathPoints[index]);
        }

        if (Distance(sampled[^1], pathPoints[^1]) > 0.001)
        {
            sampled[^1] = pathPoints[^1];
        }

        return sampled.Count >= 2 ? sampled : pathPoints;
    }

    private static GeometrySharedCorridorRun? FindBestGeometrySharedCorridorRun(
        List<SvgPoint> pathA,
        List<SvgPoint> pathB,
        SvgRenderOptions options)
    {
        List<GeometrySharedCorridorRun> runs = FindGeometrySharedCorridorRuns(pathA, pathB, options);
        if (runs.Count == 0)
        {
            return null;
        }

        return runs
            .OrderByDescending(run => run.SharedLength)
            .ThenBy(run => run.AverageDistance)
            .ThenBy(run => run.MaxDistance)
            .FirstOrDefault();
    }

    private static List<GeometrySharedCorridorRun> FindGeometrySharedCorridorRuns(
        List<SvgPoint> pathA,
        List<SvgPoint> pathB,
        SvgRenderOptions options)
    {
        List<GeometrySharedSegmentMatch> matches = [];
        double distanceThreshold = Math.Max(options.LineWidth * 3.0 + 26, 48);
        double minimumSharedLength = Math.Max(options.GridSize * 1.0, 36);
        double minimumOverlapLength = Math.Max(options.GridSize * 0.35, 8);
        double minimumSegmentLength = Math.Max(options.GridSize * 0.15, 4);
        double minimumAngleCosine = Math.Cos(Math.PI / 8.0);

        for (int i = 0; i < pathA.Count - 1; i++)
        {
            SvgPoint a0 = pathA[i];
            SvgPoint a1 = pathA[i + 1];
            if (Distance(a0, a1) < minimumSegmentLength)
            {
                continue;
            }

            for (int j = 0; j < pathB.Count - 1; j++)
            {
                SvgPoint b0 = pathB[j];
                SvgPoint b1 = pathB[j + 1];
                if (Distance(b0, b1) < minimumSegmentLength)
                {
                    continue;
                }

                if (!TryMatchGeometrySharedSegments(a0, a1, b0, b1, distanceThreshold, minimumOverlapLength, minimumAngleCosine, out GeometrySharedSegmentMatch match))
                {
                    continue;
                }

                matches.Add(match with { SegmentIndexA = i, SegmentIndexB = j });
            }
        }

        if (matches.Count == 0)
        {
            GeometrySharedCorridorRun? projectedRun = FindBestGeometrySharedCorridorRunByPointProjection(pathA, pathB, options);
            return projectedRun is null ? [] : [projectedRun.Value];
        }

        List<GeometrySharedCorridorRun> runs = [];
        HashSet<(int, int)> used = [];
        foreach (GeometrySharedSegmentMatch seed in matches
                     .OrderBy(match => match.SegmentIndexA)
                     .ThenBy(match => match.SegmentIndexB))
        {
            if (!used.Add((seed.SegmentIndexA, seed.SegmentIndexB)))
            {
                continue;
            }

            int directionSign = seed.DirectionSign;
            double sharedLength = seed.OverlapLength;
            double weightedDistance = seed.AverageDistance * seed.OverlapLength;
            double maxDistance = seed.MaxDistance;
            int lastA = seed.SegmentIndexA;
            int lastB = seed.SegmentIndexB;
            int endA = seed.SegmentIndexA + 1;
            int endB = seed.SegmentIndexB + 1;

            while (true)
            {
                int nextA = lastA + 1;
                int nextB = lastB + directionSign;
                GeometrySharedSegmentMatch? next = matches
                    .Where(match =>
                        match.SegmentIndexA == nextA
                        && match.SegmentIndexB == nextB
                        && match.DirectionSign == directionSign)
                    .Select(match => (GeometrySharedSegmentMatch?)match)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                used.Add((next.Value.SegmentIndexA, next.Value.SegmentIndexB));
                sharedLength += next.Value.OverlapLength;
                weightedDistance += next.Value.AverageDistance * next.Value.OverlapLength;
                maxDistance = Math.Max(maxDistance, next.Value.MaxDistance);
                lastA = next.Value.SegmentIndexA;
                lastB = next.Value.SegmentIndexB;
                endA = next.Value.SegmentIndexA + 1;
                endB = next.Value.SegmentIndexB + 1;
            }

            if (sharedLength < minimumSharedLength)
            {
                continue;
            }

            runs.Add(new GeometrySharedCorridorRun(
                seed.SegmentIndexA,
                endA,
                seed.SegmentIndexB,
                endB,
                directionSign,
                sharedLength,
                weightedDistance / Math.Max(sharedLength, 0.001),
                maxDistance));
        }

        return runs;
    }

    private static GeometrySharedCorridorRun? FindBestGeometrySharedCorridorRunByPointProjection(
        List<SvgPoint> pathA,
        List<SvgPoint> pathB,
        SvgRenderOptions options)
    {
        if (pathA.Count < 2 || pathB.Count < 2)
        {
            return null;
        }

        double distanceThreshold = Math.Max(options.LineWidth * 3.0 + 26, 48);
        double minimumSharedLength = Math.Max(options.GridSize * 1.0, 36);
        double[] cumulativeA = BuildPolylineCumulativeLengths(pathA);

        List<GeometryPointProjectionMatch> closePoints = [];
        for (int i = 0; i < pathA.Count; i++)
        {
            if (TryProjectPointOntoPolyline(pathA[i], pathB, out PolylinePointProjection projection)
                && projection.Distance <= distanceThreshold)
            {
                closePoints.Add(new GeometryPointProjectionMatch(i, projection.SegmentIndex, cumulativeA[i], projection.PolylineProgress, projection.Distance));
            }
        }

        if (closePoints.Count < 2)
        {
            return null;
        }

        List<GeometrySharedCorridorRun> runs = [];
        int groupStart = 0;
        for (int i = 1; i <= closePoints.Count; i++)
        {
            bool split = i == closePoints.Count
                || closePoints[i].PathIndexA - closePoints[i - 1].PathIndexA > 2;
            if (!split)
            {
                continue;
            }

            List<GeometryPointProjectionMatch> group = closePoints.Skip(groupStart).Take(i - groupStart).ToList();
            if (group.Count >= 2)
            {
                GeometryPointProjectionMatch first = group[0];
                GeometryPointProjectionMatch last = group[^1];
                double sharedLength = Math.Abs(last.ProgressA - first.ProgressA);
                if (sharedLength >= minimumSharedLength)
                {
                    double averageDistance = group.Average(match => match.Distance);
                    double maxDistance = group.Max(match => match.Distance);
                    int directionSign = last.ProgressB >= first.ProgressB ? 1 : -1;
                    int pathIndexBStart = directionSign >= 0 ? first.SegmentIndexB : last.SegmentIndexB;
                    int pathIndexBEnd = directionSign >= 0 ? last.SegmentIndexB + 1 : first.SegmentIndexB + 1;
                    runs.Add(new GeometrySharedCorridorRun(
                        first.PathIndexA,
                        last.PathIndexA,
                        pathIndexBStart,
                        pathIndexBEnd,
                        directionSign,
                        sharedLength,
                        averageDistance,
                        maxDistance));
                }
            }

            groupStart = i;
        }

        return runs
            .OrderByDescending(run => run.SharedLength)
            .ThenBy(run => run.AverageDistance)
            .ThenBy(run => run.MaxDistance)
            .FirstOrDefault();
    }

    private static bool TryMatchGeometrySharedSegments(
        SvgPoint a0,
        SvgPoint a1,
        SvgPoint b0,
        SvgPoint b1,
        double distanceThreshold,
        double minimumOverlapLength,
        double minimumAngleCosine,
        out GeometrySharedSegmentMatch match)
    {
        match = default;
        SvgPoint directionA = Normalize(new SvgPoint(a1.X - a0.X, a1.Y - a0.Y));
        SvgPoint directionB = Normalize(new SvgPoint(b1.X - b0.X, b1.Y - b0.Y));
        double cosine = Dot(directionA, directionB);
        if (Math.Abs(cosine) < minimumAngleCosine)
        {
            return false;
        }

        int directionSign = cosine >= 0 ? 1 : -1;
        SvgPoint axis = Distance(a0, a1) >= Distance(b0, b1) ? directionA : directionSign >= 0 ? directionB : new SvgPoint(-directionB.X, -directionB.Y);
        SvgPoint normal = new(-axis.Y, axis.X);
        double aStart = Dot(a0, axis);
        double aEnd = Dot(a1, axis);
        double bStart = Dot(b0, axis);
        double bEnd = Dot(b1, axis);
        double aMin = Math.Min(aStart, aEnd);
        double aMax = Math.Max(aStart, aEnd);
        double bMin = Math.Min(bStart, bEnd);
        double bMax = Math.Max(bStart, bEnd);
        double overlapLength = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
        if (overlapLength < minimumOverlapLength)
        {
            return false;
        }

        SvgPoint midpointA = Midpoint(a0, a1);
        SvgPoint midpointB = Midpoint(b0, b1);
        double centerDistance = Math.Abs(Dot(new SvgPoint(midpointA.X - midpointB.X, midpointA.Y - midpointB.Y), normal));
        if (centerDistance > distanceThreshold)
        {
            return false;
        }

        double averageDistance = (
            DistancePointToSegment(midpointA, b0, b1)
            + DistancePointToSegment(midpointB, a0, a1)) / 2.0;
        double maxDistance = new[]
        {
            DistancePointToSegment(a0, b0, b1),
            DistancePointToSegment(a1, b0, b1),
            DistancePointToSegment(b0, a0, a1),
            DistancePointToSegment(b1, a0, a1)
        }.Max();

        if (averageDistance > distanceThreshold || maxDistance > distanceThreshold * 1.8)
        {
            return false;
        }

        match = new GeometrySharedSegmentMatch(0, 0, directionSign, overlapLength, averageDistance, maxDistance);
        return true;
    }

    private static bool TryBuildSchematicV2GeometryCorridorConstraint(
        SchematicV2ProjectedLine familyA,
        SchematicV2ProjectedLine familyB,
        GeometrySharedCorridorRun run,
        SvgRenderOptions options,
        out SchematicV2GeometryCorridorConstraint constraint)
    {
        constraint = null!;
        List<string> sharedStopsA = GetSharedStationsForGeometryRun(familyA, run.PathIndexAStart, run.PathIndexAEnd, options);
        List<string> sharedStopsB = GetSharedStationsForGeometryRun(familyB, run.PathIndexBStart, run.PathIndexBEnd, options);
        if (sharedStopsA.Count < 2 || sharedStopsB.Count < 2)
        {
            return false;
        }

        if ((HasExpressServiceVariant(familyA.Family) || HasExpressServiceVariant(familyB.Family))
            && TryFindSharedAdjacentEdge(familyA.TopologyStops, familyB.TopologyStops, out SharedAdjacentEdge sharedEdge))
        {
            bool hostIsA = familyA.TopologyStops.Count >= familyB.TopologyStops.Count;
            List<string> hostStops = hostIsA ? familyA.TopologyStops : familyB.TopologyStops;
            string sharedEdgeGuideFamilyKey = hostIsA ? familyA.Family.FamilyKey : familyB.Family.FamilyKey;
            string hostStart = hostIsA ? sharedEdge.FamilyAStartStationId : sharedEdge.FamilyBStartStationId;
            string hostEnd = hostIsA ? sharedEdge.FamilyAEndStationId : sharedEdge.FamilyBEndStationId;
            List<string> sharedEdgeGuideSlice = GetContiguousStationSlice(hostStops, hostStart, hostEnd);
            if (sharedEdgeGuideSlice.Count >= 2)
            {
                List<string> sharedEdgeMaterializedGuideSlice = ExpandSchematicV2GuideSlice(hostStops, sharedEdgeGuideSlice, 1);
                if (sharedEdgeMaterializedGuideSlice.Distinct(StringComparer.Ordinal).Count() >= 3)
                {
                    constraint = new SchematicV2GeometryCorridorConstraint(
                        familyA.Family.FamilyKey,
                        familyB.Family.FamilyKey,
                        sharedEdgeGuideFamilyKey,
                        sharedEdge.FamilyAStartStationId,
                        sharedEdge.FamilyAEndStationId,
                        sharedEdge.FamilyBStartStationId,
                        sharedEdge.FamilyBEndStationId,
                        sharedEdgeMaterializedGuideSlice,
                        run.SharedLength,
                        run.AverageDistance,
                        run.MaxDistance,
                        true,
                        true);
                    return true;
                }
            }
        }

        string guideFamilyKey = sharedStopsA.Count >= sharedStopsB.Count ? familyA.Family.FamilyKey : familyB.Family.FamilyKey;
        List<string> guideStations = guideFamilyKey == familyA.Family.FamilyKey ? sharedStopsA : sharedStopsB;
        if (guideStations.Count < 2)
        {
            return false;
        }

        List<string> guideTopologyStops = guideFamilyKey == familyA.Family.FamilyKey ? familyA.TopologyStops : familyB.TopologyStops;
        List<string> guideSlice = GetContiguousStationSlice(
            guideTopologyStops,
            guideStations[0],
            guideStations[^1]);
        if (guideSlice.Count < 2)
        {
            return false;
        }

        List<string> materializedGuideSlice = guideSlice.Count <= 2
            ? ExpandSchematicV2GuideSlice(guideTopologyStops, guideSlice, 1)
            : guideSlice;
        bool useFullGuideInterval = materializedGuideSlice.Count > guideSlice.Count;
        bool stopSequenceMatched = sharedStopsA.Count == guideSlice.Count && sharedStopsB.Count == guideSlice.Count;
        constraint = new SchematicV2GeometryCorridorConstraint(
            familyA.Family.FamilyKey,
            familyB.Family.FamilyKey,
            guideFamilyKey,
            sharedStopsA[0],
            sharedStopsA[^1],
            sharedStopsB[0],
            sharedStopsB[^1],
            materializedGuideSlice,
            run.SharedLength,
            run.AverageDistance,
            run.MaxDistance,
            stopSequenceMatched,
            useFullGuideInterval);
        return true;
    }

    private static List<string> ExpandSchematicV2GuideSlice(List<string> topologyStops, List<string> guideSlice, int expansionSteps)
    {
        if (guideSlice.Count < 2 || expansionSteps <= 0)
        {
            return guideSlice;
        }

        int firstIndex = topologyStops.IndexOf(guideSlice[0]);
        int lastIndex = topologyStops.IndexOf(guideSlice[^1]);
        if (firstIndex < 0 || lastIndex < 0)
        {
            return guideSlice;
        }

        bool reversed = lastIndex < firstIndex;
        int min = Math.Min(firstIndex, lastIndex);
        int max = Math.Max(firstIndex, lastIndex);
        min = Math.Max(0, min - expansionSteps);
        max = Math.Min(topologyStops.Count - 1, max + expansionSteps);
        List<string> expanded = topologyStops.Skip(min).Take(max - min + 1).ToList();
        if (reversed)
        {
            expanded.Reverse();
        }

        return RemoveConsecutiveDuplicateStops(expanded);
    }

    private static bool TryFindSharedAdjacentEdge(List<string> stopsA, List<string> stopsB, out SharedAdjacentEdge edge)
    {
        edge = default;
        Dictionary<string, (string Start, string End)> edgesA = [];
        for (int i = 1; i < stopsA.Count; i++)
        {
            string start = stopsA[i - 1];
            string end = stopsA[i];
            if (string.Equals(start, end, StringComparison.Ordinal))
            {
                continue;
            }

            edgesA.TryAdd(CreateStationPairKey(start, end), (start, end));
        }

        for (int i = 1; i < stopsB.Count; i++)
        {
            string startB = stopsB[i - 1];
            string endB = stopsB[i];
            if (string.Equals(startB, endB, StringComparison.Ordinal))
            {
                continue;
            }

            if (edgesA.TryGetValue(CreateStationPairKey(startB, endB), out (string Start, string End) edgeA))
            {
                edge = new SharedAdjacentEdge(edgeA.Start, edgeA.End, startB, endB);
                return true;
            }
        }

        return false;
    }

    private static string CreateStationPairKey(string stationA, string stationB)
    {
        return string.CompareOrdinal(stationA, stationB) <= 0
            ? $"{stationA}|{stationB}"
            : $"{stationB}|{stationA}";
    }

    private static string CreateFamilyPairKey(string familyA, string familyB)
    {
        return string.CompareOrdinal(familyA, familyB) <= 0
            ? $"{familyA}|{familyB}"
            : $"{familyB}|{familyA}";
    }

    private static List<string> GetSharedStationsForGeometryRun(
        SchematicV2ProjectedLine line,
        int pathStartIndex,
        int pathEndIndex,
        SvgRenderOptions options)
    {
        double[] cumulative = BuildPolylineCumulativeLengths(line.PathPoints);
        double startProgress = cumulative[Math.Max(0, Math.Min(pathStartIndex, cumulative.Length - 1))];
        double endProgress = cumulative[Math.Max(0, Math.Min(pathEndIndex, cumulative.Length - 1))];
        double minProgress = Math.Min(startProgress, endProgress);
        double maxProgress = Math.Max(startProgress, endProgress);
        double stationDistanceThreshold = Math.Max(options.GridSize * 2.25, options.LineWidth * 4.0 + 20);

        List<PolylineStationProjection> projections = [];
        foreach (string stopId in line.TopologyStops)
        {
            if (!TryProjectStationOntoPolyline(stopId, line.PathPoints, line.StopPoints, line.TopologyStops, out PolylineStationProjection projection))
            {
                continue;
            }

            if (projection.Distance <= stationDistanceThreshold
                && projection.PolylineProgress >= minProgress - stationDistanceThreshold
                && projection.PolylineProgress <= maxProgress + stationDistanceThreshold)
            {
                projections.Add(projection);
            }
        }

        if (projections.Count >= 2)
        {
            return projections
                .OrderBy(projection => projection.PolylineProgress)
                .Select(projection => projection.StationId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        List<PolylineStationProjection> allProjections = [];
        foreach (string stopId in line.TopologyStops)
        {
            if (TryProjectStationOntoPolyline(stopId, line.PathPoints, line.StopPoints, line.TopologyStops, out PolylineStationProjection projection))
            {
                allProjections.Add(projection);
            }
        }

        if (allProjections.Count < 2)
        {
            return [];
        }

        PolylineStationProjection nearestStart = allProjections
            .OrderBy(projection => Math.Abs(projection.PolylineProgress - minProgress))
            .First();
        PolylineStationProjection nearestEnd = allProjections
            .OrderBy(projection => Math.Abs(projection.PolylineProgress - maxProgress))
            .First();
        if (string.Equals(nearestStart.StationId, nearestEnd.StationId, StringComparison.Ordinal))
        {
            return [];
        }

        return GetContiguousStationSlice(line.TopologyStops, nearestStart.StationId, nearestEnd.StationId);
    }

    private static bool TryProjectStationOntoPolyline(
        string stopId,
        List<SvgPoint> polyline,
        List<SvgPoint> stopPoints,
        List<string> topologyStops,
        out PolylineStationProjection projection)
    {
        projection = default;
        int stopIndex = topologyStops.IndexOf(stopId);
        if (stopIndex < 0 || stopIndex >= stopPoints.Count)
        {
            return false;
        }

        SvgPoint stationPoint = stopPoints[stopIndex];
        double[] cumulative = BuildPolylineCumulativeLengths(polyline);
        double bestDistance = double.MaxValue;
        int bestSegment = -1;
        double bestProgress = 0;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            SvgPoint start = polyline[i];
            SvgPoint end = polyline[i + 1];
            double segmentLength = Distance(start, end);
            if (segmentLength <= 0.001)
            {
                continue;
            }

            double t = Math.Clamp(ProjectFraction(stationPoint, start, end), 0, 1);
            SvgPoint projected = new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            double distance = Distance(stationPoint, projected);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSegment = i;
                bestProgress = cumulative[i] + (segmentLength * t);
            }
        }

        if (bestSegment < 0)
        {
            return false;
        }

        projection = new PolylineStationProjection(stopId, bestSegment, bestDistance, bestProgress);
        return true;
    }

    private static bool TryProjectPointOntoPolyline(
        SvgPoint point,
        List<SvgPoint> polyline,
        out PolylinePointProjection projection)
    {
        projection = default;
        if (polyline.Count < 2)
        {
            return false;
        }

        double[] cumulative = BuildPolylineCumulativeLengths(polyline);
        double bestDistance = double.MaxValue;
        int bestSegment = -1;
        double bestProgress = 0;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            SvgPoint start = polyline[i];
            SvgPoint end = polyline[i + 1];
            double segmentLength = Distance(start, end);
            if (segmentLength <= 0.001)
            {
                continue;
            }

            double t = Math.Clamp(ProjectFraction(point, start, end), 0, 1);
            SvgPoint projected = new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            double distance = Distance(point, projected);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSegment = i;
                bestProgress = cumulative[i] + (segmentLength * t);
            }
        }

        if (bestSegment < 0)
        {
            return false;
        }

        projection = new PolylinePointProjection(bestSegment, bestDistance, bestProgress);
        return true;
    }

    private static List<string> GetContiguousStationSlice(List<string> stops, string startStationId, string endStationId)
    {
        int startIndex = stops.IndexOf(startStationId);
        int endIndex = stops.IndexOf(endStationId);
        if (startIndex < 0 || endIndex < 0)
        {
            return [];
        }

        if (startIndex <= endIndex)
        {
            return stops.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
        }

        return stops.Skip(endIndex).Take(startIndex - endIndex + 1).Reverse().ToList();
    }

    private static double[] BuildPolylineCumulativeLengths(List<SvgPoint> points)
    {
        double[] cumulative = new double[points.Count];
        double total = 0;
        cumulative[0] = 0;
        for (int i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
            cumulative[i] = total;
        }

        return cumulative;
    }

    private static double ProjectFraction(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        SvgPoint vector = new(end.X - start.X, end.Y - start.Y);
        double denominator = (vector.X * vector.X) + (vector.Y * vector.Y);
        if (denominator <= 0.001)
        {
            return 0;
        }

        return (((point.X - start.X) * vector.X) + ((point.Y - start.Y) * vector.Y)) / denominator;
    }

    private static MetroLine CreateSchematicV2TopologyLine(
        DisplayLineFamily family,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        DisplayServiceVariant variant = SelectSchematicV2TopologyVariant(family, stationsById, stationPoints);
        return new MetroLine
        {
            Id = string.IsNullOrWhiteSpace(variant.LineId) ? family.PrimaryLine.Id : variant.LineId,
            Name = family.DisplayName,
            Color = family.Color,
            Mode = family.PrimaryLine.Mode,
            Stops = variant.Stops.ToList(),
            PathPoints = []
        };
    }

    private static List<string> GetSchematicV2TopologyStops(
        DisplayLineFamily family,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        DisplayServiceVariant variant = SelectSchematicV2TopologyVariant(family, stationsById, stationPoints);
        return GetValidSchematicV2Stops(variant, stationsById, stationPoints);
    }

    private static DisplayServiceVariant SelectSchematicV2TopologyVariant(
        DisplayLineFamily family,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        DisplayServiceVariant? best = family.Variants
            .OrderByDescending(variant => GetValidSchematicV2Stops(variant, stationsById, stationPoints).Count)
            .ThenByDescending(variant => variant.PathLength)
            .ThenByDescending(variant => IsCanonicalNameMatch(family, variant))
            .ThenBy(variant => IsExpressServiceVariant(variant) ? 1 : 0)
            .ThenBy(variant => variant.OriginalName, StringComparer.CurrentCulture)
            .ThenBy(variant => variant.LineId, StringComparer.Ordinal)
            .FirstOrDefault();

        return best ?? new DisplayServiceVariant(
            family.PrimaryLine.Id ?? string.Empty,
            family.PrimaryLine.Name ?? family.DisplayName,
            family.DisplayName,
            family.PrimaryLine.Stops ?? [],
            family.PrimaryLine.Stops?.Count ?? 0,
            family.PrimaryLine.PathPoints?.Count ?? 0,
            0,
            null,
            null);
    }

    private static bool IsCanonicalNameMatch(DisplayLineFamily family, DisplayServiceVariant variant)
    {
        return string.Equals(variant.OriginalName, family.DisplayName, StringComparison.CurrentCulture)
            || string.Equals(variant.VariantName, family.DisplayName, StringComparison.CurrentCulture)
            || string.Equals(variant.VariantName, "Local", StringComparison.OrdinalIgnoreCase)
            || string.Equals(variant.VariantName, "普通", StringComparison.CurrentCulture)
            || string.Equals(variant.VariantName, "站站停", StringComparison.CurrentCulture);
    }

    private static List<string> GetValidSchematicV2Stops(
        DisplayServiceVariant variant,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        return variant.Stops
            .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId) && stationPoints.ContainsKey(stopId))
            .ToList();
    }

    private static int CountDistinctAdjacentEdges(List<string> stops)
    {
        HashSet<string> edges = new(StringComparer.Ordinal);
        for (int i = 1; i < stops.Count; i++)
        {
            string a = stops[i - 1];
            string b = stops[i];
            if (string.Equals(a, b, StringComparison.Ordinal))
            {
                continue;
            }

            edges.Add(CreateUndirectedStationEdgeKey(a, b));
        }

        return edges.Count;
    }

    private static string CreateUndirectedStationEdgeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0
            ? $"{a}|{b}"
            : $"{b}|{a}";
    }

    private static double CalculateSchematicV2CorridorConfidence(double sharedLength, double averageDistance, double maxDistance)
    {
        double lengthScore = Math.Clamp(sharedLength / 240.0, 0, 1);
        double averageDistanceScore = 1.0 - Math.Clamp(averageDistance / 48.0, 0, 1);
        double maxDistanceScore = 1.0 - Math.Clamp(maxDistance / 96.0, 0, 1);
        return Math.Clamp((lengthScore * 0.45) + (averageDistanceScore * 0.35) + (maxDistanceScore * 0.20), 0, 1);
    }

    private static string StableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        StringBuilder builder = new();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static Dictionary<string, HashSet<string>> BuildSchematicV2Adjacency(
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints)
    {
        Dictionary<string, HashSet<string>> adjacency = stationPoints.Keys
            .ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (DisplayLineFamily family in displayFamilies)
        {
            foreach (DisplayServiceVariant variant in family.Variants)
            {
                List<string> validStops = variant.Stops
                    .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId) && stationPoints.ContainsKey(stopId))
                    .ToList();
                for (int i = 1; i < validStops.Count; i++)
                {
                    string a = validStops[i - 1];
                    string b = validStops[i];
                    if (string.Equals(a, b, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    adjacency[a].Add(b);
                    adjacency[b].Add(a);
                }
            }
        }

        return adjacency;
    }

    private static Dictionary<string, HashSet<string>> BuildSchematicV2Adjacency(
        CanonicalSchematicNetwork canonicalNetwork,
        Dictionary<string, SvgPoint> stationPoints)
    {
        Dictionary<string, HashSet<string>> adjacency = stationPoints.Keys
            .ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (CanonicalAdjacencyEdge edge in canonicalNetwork.AdjacencyEdges)
        {
            if (!stationPoints.ContainsKey(edge.StartStationId)
                || !stationPoints.ContainsKey(edge.EndStationId)
                || string.Equals(edge.StartStationId, edge.EndStationId, StringComparison.Ordinal))
            {
                continue;
            }

            adjacency[edge.StartStationId].Add(edge.EndStationId);
            adjacency[edge.EndStationId].Add(edge.StartStationId);
        }

        return adjacency;
    }

    private static bool RelaxSchematicV2DensePairs(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        SvgRect bounds,
        double gridSize,
        double minimumSpacing,
        double maxStep,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds)
    {
        bool moved = false;
        List<string> stationIds = points.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        for (int i = 0; i < stationIds.Count; i++)
        {
            for (int j = i + 1; j < stationIds.Count; j++)
            {
                string firstId = stationIds[i];
                string secondId = stationIds[j];
                SvgPoint first = points[firstId];
                SvgPoint second = points[secondId];
                double distance = Distance(first, second);
                if (distance >= minimumSpacing)
                {
                    continue;
                }

                SvgPoint direction = GetSchematicSeparationDirection(first, second, firstId, secondId);
                ApplySchematicV2PairPush(
                    points,
                    original,
                    firstId,
                    secondId,
                    direction,
                    Math.Min(maxStep, (minimumSpacing - distance) * 0.45),
                    bounds,
                    gridSize,
                    degreeByStation,
                    interchangeStationIds);
                moved = true;
            }
        }

        return moved;
    }

    private static void ApplySchematicV2PairPush(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, SvgPoint> original,
        string firstId,
        string secondId,
        SvgPoint direction,
        double push,
        SvgRect bounds,
        double gridSize,
        Dictionary<string, int> degreeByStation,
        HashSet<string> interchangeStationIds)
    {
        if (push <= 0)
        {
            return;
        }

        double firstWeight = GetSchematicStationAnchorWeight(firstId, degreeByStation, interchangeStationIds);
        double secondWeight = GetSchematicStationAnchorWeight(secondId, degreeByStation, interchangeStationIds);
        double firstShare = secondWeight / (firstWeight + secondWeight);
        double secondShare = firstWeight / (firstWeight + secondWeight);
        SvgPoint first = points[firstId];
        SvgPoint second = points[secondId];

        points[firstId] = ClampSchematicStationMovement(
            new SvgPoint(first.X - direction.X * push * firstShare, first.Y - direction.Y * push * firstShare),
            original[firstId],
            bounds,
            gridSize,
            firstId,
            degreeByStation,
            interchangeStationIds);
        points[secondId] = ClampSchematicStationMovement(
            new SvgPoint(second.X + direction.X * push * secondShare, second.Y + direction.Y * push * secondShare),
            original[secondId],
            bounds,
            gridSize,
            secondId,
            degreeByStation,
            interchangeStationIds);
    }

    private static SvgPoint QuantizeSchematicDirection(SvgPoint vector, SvgPoint fallback)
    {
        double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length <= 0.001)
        {
            vector = fallback;
            length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        }

        if (length <= 0.001)
        {
            return new SvgPoint(1, 0);
        }

        double angle = Math.Atan2(vector.Y, vector.X);
        double octant = Math.Round(angle / (Math.PI / 4.0));
        double snappedAngle = octant * Math.PI / 4.0;
        return new SvgPoint(Math.Cos(snappedAngle), Math.Sin(snappedAngle));
    }

    private static Dictionary<string, SchematicStationAdjustment> BuildSchematicStationAdjustments(
        Dictionary<string, SvgPoint> original,
        Dictionary<string, SvgPoint> adjusted,
        string reason)
    {
        Dictionary<string, SchematicStationAdjustment> adjustments = [];
        foreach ((string stationId, SvgPoint originalPoint) in original)
        {
            if (!adjusted.TryGetValue(stationId, out SvgPoint adjustedPoint))
            {
                continue;
            }

            double distance = Distance(originalPoint, adjustedPoint);
            if (distance > 0.001)
            {
                adjustments[stationId] = new SchematicStationAdjustment(stationId, originalPoint, adjustedPoint, distance, reason);
            }
        }

        return adjustments;
    }

    private static int CountShortSchematicEdges(
        Dictionary<string, SvgPoint> points,
        Dictionary<string, HashSet<string>> adjacency,
        double minimumSpacing)
    {
        int count = 0;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string stationId, HashSet<string> neighbors) in adjacency)
        {
            if (!points.TryGetValue(stationId, out SvgPoint first))
            {
                continue;
            }

            foreach (string neighborId in neighbors)
            {
                string key = string.CompareOrdinal(stationId, neighborId) <= 0 ? $"{stationId}|{neighborId}" : $"{neighborId}|{stationId}";
                if (!seen.Add(key) || !points.TryGetValue(neighborId, out SvgPoint second))
                {
                    continue;
                }

                if (Distance(first, second) < minimumSpacing)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static SvgPoint SnapSegmentEndpoint(SvgPoint from, SvgPoint desired, double gridSize, SvgRect bounds)
    {
        double dx = desired.X - from.X;
        double dy = desired.Y - from.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return SnapPointToGrid(desired, gridSize, bounds);
        }

        double signX = dx < 0 ? -1 : 1;
        double signY = dy < 0 ? -1 : 1;
        double horizontalLength = SnapLength(Math.Abs(dx), gridSize);
        double verticalLength = SnapLength(Math.Abs(dy), gridSize);
        double diagonalLength = SnapLength(Math.Max(Math.Abs(dx), Math.Abs(dy)), gridSize);

        List<SvgPoint> candidates =
        [
            new(from.X + signX * horizontalLength, from.Y),
            new(from.X, from.Y + signY * verticalLength),
            new(from.X + signX * diagonalLength, from.Y + signY * diagonalLength)
        ];

        SvgPoint best = desired;
        double bestDistance = double.MaxValue;
        foreach (SvgPoint candidate in candidates)
        {
            SvgPoint snappedCandidate = SnapPointToGrid(candidate, gridSize, bounds);
            double distance = DistanceSquared(snappedCandidate, desired);
            if (distance < bestDistance)
            {
                best = snappedCandidate;
                bestDistance = distance;
            }
        }

        if (DistanceSquared(best, from) < 0.001)
        {
            return SnapPointToGrid(desired, gridSize, bounds);
        }

        return best;
    }

    private static double SnapLength(double length, double gridSize)
    {
        return Math.Max(gridSize, Math.Round(length / gridSize) * gridSize);
    }

    private static SvgPoint SnapPointToGrid(SvgPoint point, double gridSize, SvgRect bounds)
    {
        double x = Math.Round(point.X / gridSize) * gridSize;
        double y = Math.Round(point.Y / gridSize) * gridSize;
        double left = Math.Ceiling(bounds.Left / gridSize) * gridSize;
        double right = Math.Floor(bounds.Right / gridSize) * gridSize;
        double top = Math.Ceiling(bounds.Top / gridSize) * gridSize;
        double bottom = Math.Floor(bounds.Bottom / gridSize) * gridSize;

        if (right < left)
        {
            left = bounds.Left;
            right = bounds.Right;
        }

        if (bottom < top)
        {
            top = bounds.Top;
            bottom = bounds.Bottom;
        }

        return new SvgPoint(
            Math.Clamp(x, left, right),
            Math.Clamp(y, top, bottom));
    }

    private static SvgRect CreateGeometryBounds(SvgRenderOptions options, bool reserveLegendSpace)
    {
        int padding = options.EffectivePadding;
        bool transitMapStyle = IsTransitMapStyle(options);
        double topReserve = transitMapStyle ? GetTransitMapHeaderHeight(options) + padding * (options.CompactTransitMapFrame ? 0.28 : 0.55) : padding;
        double bottomReserve = transitMapStyle ? GetTransitMapFooterHeight(options) + padding * (options.CompactTransitMapFrame ? 0.22 : 0.45) : padding;
        double rightReserve = padding + (!transitMapStyle && reserveLegendSpace ? options.LegendWidth + options.LegendGap : 0);
        return new SvgRect(
            padding,
            topReserve,
            Math.Max(padding, options.Width - rightReserve),
            Math.Max(topReserve, options.Height - bottomReserve));
    }

    private static double DistanceSquared(SvgPoint a, SvgPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static List<SourceStationPoint> ExpandCenter(List<SourceStationPoint> sourcePoints, double strength)
    {
        strength = Math.Clamp(strength, 0, 0.45);
        if (strength <= 0 || sourcePoints.Count < 3)
        {
            return sourcePoints;
        }

        double minX = sourcePoints.Min(point => point.X);
        double maxX = sourcePoints.Max(point => point.X);
        double minZ = sourcePoints.Min(point => point.Z);
        double maxZ = sourcePoints.Max(point => point.Z);
        double centerX = (minX + maxX) / 2;
        double centerZ = (minZ + maxZ) / 2;
        double halfWidth = Math.Max((maxX - minX) / 2, 1);
        double halfHeight = Math.Max((maxZ - minZ) / 2, 1);

        return sourcePoints
            .Select(point =>
            {
                double normalizedX = (point.X - centerX) / halfWidth;
                double normalizedZ = (point.Z - centerZ) / halfHeight;
                double normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
                double centerWeight = Math.Max(0, 1 - Math.Min(1, normalizedDistance));
                double factor = 1 + strength * centerWeight;
                return new SourceStationPoint(
                    point.Id,
                    centerX + (point.X - centerX) * factor,
                    centerZ + (point.Z - centerZ) * factor);
            })
            .ToList();
    }

    private static void AppendHeader(StringBuilder svg, MetroExportDocument document, SvgRenderOptions options)
    {
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        string title = BuildDiagramTitle(document.City?.Name);
        bool transitMapStyle = IsTransitMapStyle(options);

        int padding = options.EffectivePadding;
        double titleY = transitMapStyle ? GetTransitMapHeaderHeight(options) * 0.56 : Math.Max(36, padding - 36);
        double labelHaloWidth = visualStyle.LabelHaloWidth;
        double legendVariantFontSize = Math.Max(12, options.LegendLabelFontSize * 0.84);
        double legendTitleFontSize = Math.Max(16, options.LegendLabelFontSize + 1);

        svg.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{options.Width}" height="{options.Height}" viewBox="0 0 {options.Width} {options.Height}" role="img" aria-label="{Escape(title)}" data-map-style="{GetMapStyleName(options.MapStyle)}" data-transit-map-frame="{GetTransitMapFrameName(options)}">""");
        svg.AppendLine($"""<title>{Escape(title)}</title>""");
        svg.AppendLine("<defs>");
        svg.AppendLine("<style>");
        svg.AppendLine("            .background { fill: #ffffff; }");
        svg.AppendLine("            .title { font: 700 28px Arial, sans-serif; fill: #1f2933; }");
        svg.AppendLine("            .transit-title-cn { font: 700 54px Arial, 'Microsoft YaHei', sans-serif; fill: #143a78; letter-spacing: 1.2px; }");
        svg.AppendLine("            .transit-title-en { font: 600 24px Arial, sans-serif; fill: #143a78; }");
        svg.AppendLine("            .transit-city { font: 600 15px Arial, 'Microsoft YaHei', sans-serif; fill: #4b5563; }");
        svg.AppendLine("            .transit-info-icon { font: 800 52px Arial, sans-serif; fill: #ffffff; }");
        svg.AppendLine($"            .route {{ fill: none; stroke-width: {Format(visualStyle.BaseRouteWidth)}; stroke-linecap: round; stroke-linejoin: round; }}");
        svg.AppendLine("            .express-decoration { fill: none; stroke-linecap: round; stroke-linejoin: round; }");
        svg.AppendLine($"            .station {{ fill: #ffffff; stroke: #1f2933; stroke-width: {Format(visualStyle.StationMarkerStrokeWidth)}; }}");
        svg.AppendLine($"            .station.terminal {{ stroke-width: {Format(visualStyle.StationMarkerStrokeWidth + 0.35)}; }}");
        svg.AppendLine($"            .station.interchange {{ stroke-width: {Format(visualStyle.InterchangeMarkerStrokeWidth)}; }}");
        svg.AppendLine("            .station-interchange-inner { fill: none; stroke: #1f2933; stroke-width: 1.45; pointer-events: none; }");
        svg.AppendLine($"            .station-label {{ font: 600 {Format(options.LabelFontSize)}px Arial, sans-serif; fill: #1f2933; }}");
        svg.AppendLine("            .station-label[data-label-important=\"true\"] { font-weight: 700; fill: #111827; }");
        svg.AppendLine($"            .station-label-halo {{ stroke: #ffffff; stroke-width: {Format(labelHaloWidth)}; paint-order: stroke; stroke-linejoin: round; }}");
        svg.AppendLine("            .virtual-transfer-hint { fill: none; stroke: #6b7280; stroke-width: 2.2; stroke-linecap: round; stroke-dasharray: 8 6; opacity: 0.72; }");
        svg.AppendLine("            .empty-notice { font: 600 16px Arial, sans-serif; fill: #52616f; }");
        svg.AppendLine($"            .legend-label {{ font: 600 {Format(options.LegendLabelFontSize)}px Arial, sans-serif; fill: #1f2933; }}");
        svg.AppendLine($"            .legend-variant {{ font: 500 {Format(legendVariantFontSize)}px Arial, sans-serif; fill: #52616f; }}");
        svg.AppendLine($"            .legend-title {{ font: 700 {Format(legendTitleFontSize)}px Arial, sans-serif; fill: #1f2933; }}");
        svg.AppendLine("            .transit-footer-label { font: 700 13px Arial, sans-serif; fill: #374151; }");
        svg.AppendLine("            .transit-footer-note { font: 500 12px Arial, sans-serif; fill: #7b8491; }");
        svg.AppendLine($"            .route-badge-label {{ font: 800 {Format(GetRouteBadgeFontSize(options))}px Arial, sans-serif; fill: #ffffff; }}");
        svg.AppendLine("</style>");
        svg.AppendLine("</defs>");
        svg.AppendLine($"""<rect class="background" x="0" y="0" width="{options.Width}" height="{options.Height}" />""");
        if (transitMapStyle)
        {
            AppendTransitMapHeader(svg, document.City?.Name, title, options);
        }
        else
        {
            svg.AppendLine($"""<text class="title" x="{padding}" y="{Format(titleY)}">{Escape(title)}</text>""");
        }
    }

    private static void AppendTransitMapHeader(StringBuilder svg, string? rawCityName, string title, SvgRenderOptions options)
    {
        double headerHeight = GetTransitMapHeaderHeight(options);
        double bandHeight = Math.Max(options.CompactTransitMapFrame ? 60 : 52, headerHeight * (options.CompactTransitMapFrame ? 0.66 : 0.64));
        double capsuleX = Math.Max(44, options.Width * 0.035);
        double capsuleY = Math.Max(options.CompactTransitMapFrame ? 18 : 20, headerHeight * (options.CompactTransitMapFrame ? 0.16 : 0.18));
        double capsuleWidth = Math.Max(200, options.Width - capsuleX * 2);
        double capsuleHeight = Math.Max(options.CompactTransitMapFrame ? 66 : 62, headerHeight * (options.CompactTransitMapFrame ? 0.62 : 0.58));
        double centerX = options.Width / 2.0;
        double infoX = Math.Min(options.Width - capsuleX - 78, centerX + capsuleWidth * 0.32);
        double infoY = capsuleY + capsuleHeight / 2.0;
        string mainTitle = BuildTransitMapMainTitle(rawCityName);

        svg.AppendLine($"""<g id="transit-map-header" data-map-style="transit-map">""");
        svg.AppendLine($"""<rect x="0" y="0" width="{options.Width}" height="{Format(bandHeight)}" fill="#dfe83a" />""");
        svg.AppendLine($"""<polygon points="0,0 {Format(options.Width * 0.08)},0 {Format(options.Width * 0.03)},{Format(bandHeight)} 0,{Format(bandHeight)}" fill="#176ba8" />""");
        svg.AppendLine($"""<polygon points="{Format(options.Width * 0.08)},0 {Format(options.Width * 0.38)},0 {Format(options.Width * 0.43)},{Format(bandHeight)} {Format(options.Width * 0.03)},{Format(bandHeight)}" fill="#80bf1f" />""");
        svg.AppendLine($"""<polygon points="{Format(options.Width * 0.37)},0 {Format(options.Width * 0.68)},0 {Format(options.Width * 0.74)},{Format(bandHeight)} {Format(options.Width * 0.43)},{Format(bandHeight)}" fill="#16a889" />""");
        svg.AppendLine($"""<polygon points="{Format(options.Width * 0.68)},0 {Format(options.Width * 0.88)},0 {Format(options.Width * 0.91)},{Format(bandHeight)} {Format(options.Width * 0.74)},{Format(bandHeight)}" fill="#1f7db3" />""");
        svg.AppendLine($"""<polygon points="{Format(options.Width * 0.88)},0 {options.Width},0 {options.Width},{Format(bandHeight)} {Format(options.Width * 0.91)},{Format(bandHeight)}" fill="#18aa88" />""");
        svg.AppendLine($"""<rect x="{Format(capsuleX)}" y="{Format(capsuleY)}" width="{Format(capsuleWidth)}" height="{Format(capsuleHeight)}" rx="{Format(capsuleHeight * 0.34)}" fill="#ffffff" stroke="#d8eef0" stroke-width="1.2" />""");
        svg.AppendLine($"""<text class="transit-title-cn" x="{Format(centerX)}" y="{Format(capsuleY + capsuleHeight * 0.46)}" text-anchor="middle">{Escape(mainTitle)}</text>""");
        svg.AppendLine($"""<text class="transit-title-en" x="{Format(centerX)}" y="{Format(capsuleY + capsuleHeight * 0.73)}" text-anchor="middle">Transport System Map</text>""");
        svg.AppendLine($"""<circle cx="{Format(infoX)}" cy="{Format(infoY)}" r="{Format(capsuleHeight * 0.33)}" fill="#17a979" />""");
        svg.AppendLine($"""<text class="transit-info-icon" x="{Format(infoX)}" y="{Format(infoY + capsuleHeight * 0.22)}" text-anchor="middle">i</text>""");
        svg.AppendLine($"""<line x1="0" y1="{Format(headerHeight - 1.2)}" x2="{options.Width}" y2="{Format(headerHeight - 1.2)}" stroke="#d1d5db" stroke-width="1.1" opacity="0.65" />""");
        if (!IsSchematicMapLayout(options))
        {
            svg.AppendLine($"""<text class="transit-city" x="{Format(capsuleX + 26)}" y="{Format(capsuleY + capsuleHeight + 28)}">{Escape(title)}</text>""");
        }
        svg.AppendLine("</g>");
    }

    private static string BuildTransitMapMainTitle(string? rawCityName)
    {
        if (string.IsNullOrWhiteSpace(rawCityName))
        {
            return "\u7ebf\u7f51\u793a\u610f\u56fe";
        }

        string cityName = rawCityName.Trim();
        if (IsExportPlaceholderCityName(cityName))
        {
            return "\u7ebf\u7f51\u793a\u610f\u56fe";
        }

        return ContainsCjk(cityName)
            ? $"{cityName}\u7ebf\u7f51\u793a\u610f\u56fe"
            : $"{cityName} Metro Diagram";
    }

    private static bool ContainsCjk(string value)
    {
        return value.Any(character => character is >= '\u3400' and <= '\u9fff');
    }

    private static string BuildDiagramTitle(string? rawCityName)
    {
        string cityName = string.IsNullOrWhiteSpace(rawCityName)
            ? "Unnamed City"
            : rawCityName.Trim();

        if (IsExportPlaceholderCityName(cityName))
        {
            return "CS2 Metro Diagram";
        }

        return $"{cityName} Metro Diagram";
    }

    private static bool IsExportPlaceholderCityName(string cityName)
    {
        return string.Equals(cityName, "CS2 Metro Export", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cityName, "Metro Export", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendEmptyNotice(StringBuilder svg, List<MetroStation> stations, List<MetroLine> lines, SvgRenderOptions options)
    {
        if (stations.Count > 0 || lines.Count > 0)
        {
            return;
        }

        int padding = options.EffectivePadding;
        svg.AppendLine($"""<text class="empty-notice" x="{padding}" y="{padding + 40}">No metro stations or lines in this file.</text>""");
    }

    private static void AppendRoutes(
        StringBuilder svg,
        List<MetroStation> stations,
        List<DisplayLineFamily> families,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints,
        HashSet<string> terminalStationIds,
        RenderGeometry geometry,
        SvgRenderOptions options,
        bool hasLegend,
        List<string> warnings)
    {
        List<RenderRoute> renderRoutes = [];
        foreach (DisplayLineFamily family in families)
        {
            MetroLine line = IsSchematicV2FamilyLayout(options.LayoutMode)
                ? CreateSchematicV2TopologyLine(family, stationsById, geometry.StationPoints)
                : family.PrimaryLine;
            RoutePointSet routePointSet = CreateRoutePoints(family, line, stationsById, geometry, options, warnings);
            List<RoutePolyline> routePolylines = routePointSet.Polylines
                .Where(polyline => polyline.Points.Count >= 2)
                .ToList();

            if (routePolylines.Count == 0)
            {
                warnings.Add($"Line '{line.Id}' did not have enough positioned stops to render.");
                continue;
            }

            renderRoutes.Add(new RenderRoute(family, line, routePointSet with { Polylines = routePolylines }));
        }

        bool useCorridorRunPipeline = options.LayoutMode == SvgLayoutMode.Geographic
            && options.UsePathPoints
            && !options.EnableParallelCorridorOffset;

        if (options.EnableParallelCorridorOffset
            && options.LayoutMode == SvgLayoutMode.Geographic
            && options.UsePathPoints)
        {
            renderRoutes = ApplyParallelCorridorOffset(renderRoutes, stationsById, geometry, options);
        }

        if (!useCorridorRunPipeline
            && options.EnableSharedCorridorCompositeStroke
            && options.LayoutMode == SvgLayoutMode.Geographic
            && options.UsePathPoints)
        {
            renderRoutes = ApplySharedCorridorCompositeStroke(renderRoutes, stationsById, geometry, options);
        }

        Dictionary<string, SchematicSegmentOccupancy>? schematicSegmentOccupancy = null;
        if (options.LayoutMode == SvgLayoutMode.SchematicLite
            && options.EnableSchematicSegmentOverlapResolver)
        {
            schematicSegmentOccupancy = BuildSchematicSegmentOccupancy(renderRoutes);
        }

        svg.AppendLine($"""<g id="routes" data-layout="{GetLayoutModeName(options.LayoutMode)}">""");
        if (useCorridorRunPipeline)
        {
            AppendCorridorRenderPlan(
                svg,
                BuildGeographicCorridorRenderPlan(renderRoutes, stationsById, geometry, options),
                options);
            AppendTransitMapRouteBadges(svg, renderRoutes, stations, stationPoints, terminalStationIds, options, hasLegend);
            svg.AppendLine("</g>");
            return;
        }

        foreach (RenderRoute renderRoute in renderRoutes)
        {
            MetroLine line = renderRoute.Line;
            DisplayLineFamily family = renderRoute.Family;
            RoutePointSet routePointSet = renderRoute.RoutePointSet;
            List<RoutePolyline> routePolylines = routePointSet.Polylines;

            if (schematicSegmentOccupancy is not null)
            {
                AppendSchematicRoutePolylines(
                    svg,
                    renderRoute,
                    schematicSegmentOccupancy,
                    options);
                continue;
            }

            for (int i = 0; i < routePolylines.Count; i++)
            {
                RoutePolyline polyline = routePolylines[i];
                string pointList = string.Join(" ", polyline.Points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
                string pathPointAttributes = routePointSet.Source == "pathPoints"
                    ? $" data-path-point-count=\"{routePointSet.OriginalPathPointCount}\" data-cleaned-path-point-count=\"{routePointSet.CleanedPathPointCount}\" data-path-reduction-ratio=\"{Format(routePointSet.ReductionRatio)}\" data-max-path-segment-length=\"{Format(routePointSet.MaxPathSegmentLength)}\" data-suspicious-jump-count=\"{routePointSet.SuspiciousJumpCount}\" data-path-simplification-tolerance=\"{Format(routePointSet.EffectiveSimplificationTolerance)}\""
                    : string.Empty;
                string routePartAttributes = routePolylines.Count > 1
                    ? $" data-route-part-index=\"{i}\" data-route-part-count=\"{routePolylines.Count}\""
                    : string.Empty;
                string corridorAttributes = polyline.CorridorId is null
                    ? string.Empty
                    : $" data-parallel-corridor-offset=\"true\" data-corridor-id=\"{Escape(polyline.CorridorId)}\" data-corridor-member-count=\"{polyline.CorridorMemberCount}\" data-corridor-offset-index=\"{Format(polyline.CorridorOffsetIndex)}\" data-corridor-offset-px=\"{Format(polyline.CorridorOffsetPx)}\"";
                string familyAttributes = $" data-display-family-key=\"{Escape(family.FamilyKey)}\" data-display-family-member-count=\"{family.Variants.Count}\" data-display-family-primary-line-id=\"{Escape(family.PrimaryLine.Id)}\" data-display-family-merged=\"{(family.Variants.Count > 1 ? "true" : "false")}\"";
                string colorMismatchAttribute = family.HasColorMismatch
                    ? " data-display-family-color-mismatch=\"true\""
                    : string.Empty;
                string sharedSkipAttribute = polyline.SharedCorridorSkipped is null
                    ? string.Empty
                    : $" data-shared-corridor-skipped=\"{Escape(polyline.SharedCorridorSkipped)}\"";
                string schematicMapBendAttributes = polyline.SyntheticBendCount > 0
                    ? $" data-schematic-map-synthetic-bends=\"{polyline.SyntheticBendCount}\""
                    : string.Empty;
                string schematicV2GuideAttributes = BuildSchematicV2RouteGuideAttributes(family, geometry);
                string schematicV2ServiceAttributes = BuildSchematicV2ServiceFamilyAttributes(family, line, options);
                string commonAttributes = $"class=\"route\" data-line-id=\"{Escape(line.Id)}\" data-route-source=\"{routePointSet.Source}\"{familyAttributes}{colorMismatchAttribute}{pathPointAttributes}{routePartAttributes}{corridorAttributes}{sharedSkipAttribute}{schematicMapBendAttributes}{schematicV2GuideAttributes}{schematicV2ServiceAttributes} points=\"{pointList}\"";
                if (polyline.SharedCorridorStroke is not null)
                {
                    AppendSharedCorridorRoutePolylines(svg, commonAttributes, polyline.SharedCorridorStroke, options);
                }
                else
                {
                    AppendStandardRoutePolyline(svg, commonAttributes, family, options);
                }
            }
        }

        if (IsSchematicV2FamilyLayout(options.LayoutMode))
        {
            AppendSchematicV2ParallelCorridorOverlays(svg, renderRoutes, geometry, options);
        }

        if (IsSchematicMapLayout(options))
        {
            List<SchematicMapRouteCrossing> crossings = DetectSchematicMapRouteCrossings(renderRoutes, stationPoints, options);
            if (crossings.Count > 0)
            {
                warnings.Add($"Schematic-map crossing audit: non-station crossings: {crossings.Count}; rendered as direct pass-through.");
            }
        }

        AppendTransitMapRouteBadges(svg, renderRoutes, stations, stationPoints, terminalStationIds, options, hasLegend);
        svg.AppendLine("</g>");
    }

    private static List<SchematicMapRouteCrossing> DetectSchematicMapRouteCrossings(
        List<RenderRoute> renderRoutes,
        Dictionary<string, SvgPoint> stationPoints,
        SvgRenderOptions options)
    {
        List<SchematicMapRouteSegment> segments = [];
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            for (int polylineIndex = 0; polylineIndex < renderRoute.RoutePointSet.Polylines.Count; polylineIndex++)
            {
                List<SvgPoint> points = renderRoute.RoutePointSet.Polylines[polylineIndex].Points;
                for (int segmentIndex = 1; segmentIndex < points.Count; segmentIndex++)
                {
                    SvgPoint start = points[segmentIndex - 1];
                    SvgPoint end = points[segmentIndex];
                    double length = Distance(start, end);
                    double minimumSegmentLength = Math.Max(options.LineWidth * 2.5, 18);
                    if (length <= minimumSegmentLength)
                    {
                        continue;
                    }

                    SvgPoint direction = new((end.X - start.X) / length, (end.Y - start.Y) / length);
                    string segmentColor = renderRoute.Family.Color
                        ?? renderRoute.Line.Color
                        ?? "#4b5563";
                    segments.Add(new SchematicMapRouteSegment(
                        routeIndex,
                        polylineIndex,
                        segmentIndex - 1,
                        renderRoute.Family.FamilyKey,
                        renderRoute.Family.DisplayName,
                        segmentColor,
                        start,
                        end,
                        length,
                        direction));
                }
            }
        }

        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        double stationTolerance = Math.Max(visualStyle.InterchangeMarkerRadius * 1.7, visualStyle.BaseRouteWidth * 2.0);
        double endpointTolerance = Math.Max(visualStyle.BaseRouteWidth * 0.95, 10);
        double crossingMergeTolerance = Math.Max(visualStyle.BaseRouteWidth * 0.45, 6);
        List<SvgPoint> stationPointList = stationPoints.Values.ToList();
        List<SchematicMapRouteCrossing> crossings = [];

        for (int i = 0; i < segments.Count; i++)
        {
            for (int j = i + 1; j < segments.Count; j++)
            {
                SchematicMapRouteSegment first = segments[i];
                SchematicMapRouteSegment second = segments[j];
                if (string.Equals(first.FamilyKey, second.FamilyKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryFindSegmentIntersection(first.Start, first.End, second.Start, second.End, out SvgPoint point, out double firstFraction, out double secondFraction))
                {
                    continue;
                }

                if (IsNearSegmentEndpoint(firstFraction, first.Length, endpointTolerance)
                    || IsNearSegmentEndpoint(secondFraction, second.Length, endpointTolerance))
                {
                    continue;
                }

                if (stationPointList.Any(stationPoint => Distance(stationPoint, point) <= stationTolerance))
                {
                    continue;
                }

                double angleDegrees = SegmentAngleDegrees(first.Direction, second.Direction);
                if (angleDegrees < 25)
                {
                    continue;
                }

                string pairKey = CreateFamilyPairKey(first.FamilyKey, second.FamilyKey);
                if (crossings.Any(crossing => crossing.FamilyPairKey == pairKey && Distance(crossing.Point, point) <= crossingMergeTolerance))
                {
                    continue;
                }

                SchematicMapRouteSegment topSegment = first.RouteIndex >= second.RouteIndex ? first : second;
                SchematicMapRouteSegment bottomSegment = first.RouteIndex >= second.RouteIndex ? second : first;
                crossings.Add(new SchematicMapRouteCrossing(
                    crossings.Count,
                    point,
                    topSegment,
                    bottomSegment,
                    angleDegrees,
                    pairKey));
            }
        }

        return crossings;
    }

    private static bool TryFindSegmentIntersection(
        SvgPoint a,
        SvgPoint b,
        SvgPoint c,
        SvgPoint d,
        out SvgPoint point,
        out double firstFraction,
        out double secondFraction)
    {
        point = new SvgPoint(0, 0);
        firstFraction = 0;
        secondFraction = 0;
        double rx = b.X - a.X;
        double ry = b.Y - a.Y;
        double sx = d.X - c.X;
        double sy = d.Y - c.Y;
        double denominator = Cross(rx, ry, sx, sy);
        if (Math.Abs(denominator) < 0.000001)
        {
            return false;
        }

        double qpx = c.X - a.X;
        double qpy = c.Y - a.Y;
        firstFraction = Cross(qpx, qpy, sx, sy) / denominator;
        secondFraction = Cross(qpx, qpy, rx, ry) / denominator;
        if (firstFraction <= 0 || firstFraction >= 1 || secondFraction <= 0 || secondFraction >= 1)
        {
            return false;
        }

        point = new SvgPoint(a.X + rx * firstFraction, a.Y + ry * firstFraction);
        return true;
    }

    private static double Cross(double ax, double ay, double bx, double by)
    {
        return ax * by - ay * bx;
    }

    private static bool IsNearSegmentEndpoint(double fraction, double segmentLength, double endpointTolerance)
    {
        return fraction * segmentLength <= endpointTolerance
            || (1 - fraction) * segmentLength <= endpointTolerance;
    }

    private static double SegmentAngleDegrees(SvgPoint firstDirection, SvgPoint secondDirection)
    {
        double dot = Math.Abs(firstDirection.X * secondDirection.X + firstDirection.Y * secondDirection.Y);
        dot = Math.Clamp(dot, 0, 1);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    private static string BuildSchematicV2RouteGuideAttributes(DisplayLineFamily family, RenderGeometry geometry)
    {
        if (geometry.SchematicV2RouteGuideMetadataByFamily is null
            || !geometry.SchematicV2RouteGuideMetadataByFamily.TryGetValue(family.FamilyKey, out SchematicV2RouteGuideMetadata? metadata))
        {
            return string.Empty;
        }

        return $" data-schematic-v2-geometry-corridor=\"true\" data-schematic-v2-corridor-id=\"{Escape(metadata.CorridorId)}\" data-schematic-v2-corridor-family-a=\"{Escape(metadata.FamilyAKey)}\" data-schematic-v2-corridor-family-b=\"{Escape(metadata.FamilyBKey)}\" data-schematic-v2-corridor-source=\"pathPoints\" data-schematic-v2-corridor-confidence=\"{Format(metadata.Confidence)}\" data-schematic-v2-corridor-shared-length=\"{Format(metadata.SharedLength)}\" data-schematic-v2-corridor-average-distance=\"{Format(metadata.AverageDistance)}\" data-schematic-v2-corridor-max-distance=\"{Format(metadata.MaxDistance)}\" data-schematic-v2-route-guide=\"true\" data-schematic-v2-route-guide-source=\"geometry-shared-corridor\" data-schematic-v2-route-guide-stations=\"{Escape(string.Join(">", metadata.GuideStationIds))}\"";
    }

    private static string BuildSchematicV2ServiceFamilyAttributes(DisplayLineFamily family, MetroLine line, SvgRenderOptions options)
    {
        if (!IsSchematicV2FamilyLayout(options.LayoutMode))
        {
            return string.Empty;
        }

        List<DisplayServiceVariant> hiddenVariants = family.Variants
            .Where(variant => !string.Equals(variant.LineId, line.Id, StringComparison.Ordinal))
            .ToList();
        string hiddenVariantNames = string.Join("|", hiddenVariants.Select(variant => variant.OriginalName));
        string attributes = $" data-schematic-v2-canonical-route=\"{Escape(line.Id)}\" data-schematic-v2-hidden-service-variants=\"{Escape(hiddenVariantNames)}\"";
        if (HasExpressServiceVariant(family))
        {
            attributes += " data-schematic-v2-express-service-family=\"true\"";
        }

        return attributes;
    }

    private static void AppendTransitMapRouteBadges(
        StringBuilder svg,
        List<RenderRoute> renderRoutes,
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options,
        bool hasLegend)
    {
        if (!IsTransitMapStyle(options) || renderRoutes.Count == 0)
        {
            return;
        }

        List<SvgRect> occupiedBoxes = BuildTransitMapBadgeOccupiedBoxes(stations, stationPoints, terminalStationIds, options, hasLegend);
        SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace: false);
        svg.AppendLine("""<g id="route-badges" data-map-style="transit-map">""");
        foreach (RenderRoute renderRoute in renderRoutes)
        {
            string badgeText = GetRouteBadgeText(renderRoute.Family.DisplayName);
            if (string.IsNullOrWhiteSpace(badgeText))
            {
                continue;
            }

            List<RoutePolyline> candidatePolylines = renderRoute.RoutePointSet.Polylines
                .Where(polyline => polyline.Points.Count >= 2)
                .OrderByDescending(polyline => MeasurePolylineLength(polyline.Points))
                .ToList();
            if (candidatePolylines.Count == 0)
            {
                continue;
            }

            RoutePolyline primaryPolyline = candidatePolylines[0];
            List<RouteBadgePlacement> placements = CreateRouteBadgePlacements(primaryPolyline.Points, badgeText, occupiedBoxes, bounds, options);
            for (int i = 0; i < placements.Count; i++)
            {
                RouteBadgePlacement placement = placements[i];
                AppendRouteBadge(svg, placement, renderRoute.Family, i, placements.Count, options);
                occupiedBoxes.Add(placement.Box);
            }
        }

        svg.AppendLine("</g>");
    }

    private static List<SvgRect> BuildTransitMapBadgeOccupiedBoxes(
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options,
        bool hasLegend)
    {
        List<SvgRect> occupied = [];
        double markerRadius = SvgVisualStyle.From(options).StationMarkerOuterRadius;
        double markerBufferMultiplier = IsSchematicMapLayout(options) ? 6.2 : 5.0;
        foreach ((string _, SvgPoint point) in stationPoints)
        {
            occupied.Add(SvgRect.FromCenter(point.X, point.Y, markerRadius * markerBufferMultiplier, markerRadius * markerBufferMultiplier));
        }

        foreach (PlacedLabel label in BuildPlacedLabels(stations, stationPoints, terminalStationIds, options, hasLegend))
        {
            double inflateX = IsSchematicMapLayout(options) ? 12 : 8;
            double inflateY = IsSchematicMapLayout(options) ? 8 : 6;
            occupied.Add(label.Box.Inflate(inflateX, inflateY));
        }

        return occupied;
    }

    private static string GetRouteBadgeText(string? displayName)
    {
        return VisibleLaneResolver.GetBadgeText(displayName);
    }

    private static List<RouteBadgePlacement> CreateRouteBadgePlacements(
        List<SvgPoint> points,
        string text,
        List<SvgRect> occupiedBoxes,
        SvgRect bounds,
        SvgRenderOptions options)
    {
        if (points.Count < 2)
        {
            return [];
        }

        double badgeFontSize = GetRouteBadgeFontSize(options);
        double badgeWidth = Math.Max(28, EstimateTextWidth(text, badgeFontSize) + (IsSchematicMapLayout(options) ? 18 : 14));
        double badgeHeight = IsSchematicMapLayout(options) ? 25 : 22;
        double severeCollisionScore = IsSchematicMapLayout(options) ? 1200 : 5000;
        List<RouteBadgePlacement> placements = [];
        RouteBadgePlacement start = CreateRouteBadgePlacement(points[0], points[1], text, badgeWidth, badgeHeight, occupiedBoxes, bounds, options);
        if (start.Score < severeCollisionScore)
        {
            placements.Add(start);
            occupiedBoxes.Add(start.Box);
        }

        RouteBadgePlacement end = CreateRouteBadgePlacement(points[^1], points[^2], text, badgeWidth, badgeHeight, occupiedBoxes, bounds, options);
        bool farEnoughFromExisting = placements.Count == 0
            || placements.All(placement => Distance(new SvgPoint(placement.X, placement.Y), new SvgPoint(end.X, end.Y)) > 180);
        if (farEnoughFromExisting && end.Score < severeCollisionScore)
        {
            placements.Add(end);
        }

        return placements;
    }

    private static RouteBadgePlacement CreateRouteBadgePlacement(
        SvgPoint endpoint,
        SvgPoint neighbor,
        string text,
        double width,
        double height,
        List<SvgRect> occupiedBoxes,
        SvgRect bounds,
        SvgRenderOptions options)
    {
        SvgPoint direction = Normalize(new SvgPoint(endpoint.X - neighbor.X, endpoint.Y - neighbor.Y));
        if (Distance(direction, new SvgPoint(0, 0)) <= 0.001)
        {
            direction = new SvgPoint(1, 0);
        }

        SvgPoint normal = new(-direction.Y, direction.X);
        (double Forward, double Lateral)[] candidates = IsSchematicMapLayout(options)
            ? [
                (44, 0),
                (58, 30),
                (58, -30),
                (76, 0),
                (36, 42),
                (36, -42),
                (94, 34),
                (94, -34),
                (116, 0),
                (116, 48),
                (116, -48),
                (142, 0)
            ]
            : [
                (34, 0),
                (46, 24),
                (46, -24),
                (62, 0),
                (24, 34),
                (24, -34),
                (74, 28),
                (74, -28)
            ];

        RouteBadgePlacement? best = null;
        double bestScore = double.MaxValue;
        foreach ((double forward, double lateral) in candidates)
        {
            double x = endpoint.X + direction.X * forward + normal.X * lateral;
            double y = endpoint.Y + direction.Y * forward + normal.Y * lateral;
            double collisionPaddingX = IsSchematicMapLayout(options) ? 18 : 6;
            double collisionPaddingY = IsSchematicMapLayout(options) ? 14 : 6;
            SvgRect box = SvgRect.FromCenter(x, y, width + collisionPaddingX, height + collisionPaddingY);
            double overlap = occupiedBoxes.Sum(occupied => box.OverlapArea(occupied));
            double score = overlap * (IsSchematicMapLayout(options) ? 260 : 120)
                + (overlap > 0 && IsSchematicMapLayout(options) ? 1500 : 0)
                + box.OutsideArea(bounds) * 20
                + Math.Abs(lateral) * 0.8
                + forward * 0.05;
            if (score < bestScore)
            {
                bestScore = score;
                best = new RouteBadgePlacement(x, y, width, height, text, box, score);
            }
        }

        return best ?? new RouteBadgePlacement(endpoint.X, endpoint.Y, width, height, text, SvgRect.FromCenter(endpoint.X, endpoint.Y, width + 6, height + 6), 0);
    }

    private static void AppendRouteBadge(
        StringBuilder svg,
        RouteBadgePlacement placement,
        DisplayLineFamily family,
        int index,
        int count,
        SvgRenderOptions options)
    {
        double x = placement.X - placement.Width / 2;
        double y = placement.Y - placement.Height / 2;
        double textBaselineOffset = GetRouteBadgeFontSize(options) * 0.37;
        svg.AppendLine($"""<g class="route-badge" data-display-family-key="{Escape(family.FamilyKey)}" data-route-badge-index="{index}" data-route-badge-count="{count}" data-route-badge-placement-score="{Format(placement.Score)}">""");
        svg.AppendLine($"""<rect x="{Format(x)}" y="{Format(y)}" width="{Format(placement.Width)}" height="{Format(placement.Height)}" rx="{Format(IsSchematicMapLayout(options) ? 6 : 5)}" fill="{Escape(family.Color)}" stroke="#ffffff" stroke-width="2" />""");
        svg.AppendLine($"""<text class="route-badge-label" x="{Format(placement.X)}" y="{Format(placement.Y + textBaselineOffset)}" text-anchor="middle">{Escape(placement.Text)}</text>""");
        svg.AppendLine("</g>");
    }

    private static double MeasurePolylineLength(List<SvgPoint> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
        {
            length += Distance(points[i - 1], points[i]);
        }

        return length;
    }

    private static void AppendSchematicV2ParallelCorridorOverlays(
        StringBuilder svg,
        List<RenderRoute> renderRoutes,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<SchematicV2SharedCorridorRun> runs = BuildSchematicV2SharedCorridorRuns(renderRoutes, geometry);
        if (runs.Count == 0)
        {
            return;
        }

        Dictionary<string, RenderRoute> routesByFamily = renderRoutes
            .GroupBy(route => route.Family.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        svg.AppendLine("<g id=\"schematic-v2-parallel-corridors\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");

        foreach (SchematicV2SharedCorridorRun run in runs)
        {
            List<RenderRoute> runRoutes = run.FamilyKeys
                .Where(routesByFamily.ContainsKey)
                .Select(familyKey => routesByFamily[familyKey])
                .ToList();
            if (runRoutes.Count < 2 || run.Points.Count < 2)
            {
                continue;
            }

            List<SchematicV2ParallelCorridorLane> lanes = CreateSchematicV2ParallelCorridorLanes(run, runRoutes);
            if (lanes.Count == 0)
            {
                continue;
            }

            double spacing = GetSchematicV2ParallelCorridorSpacing(run, options);
            double strokeWidth = GetSchematicV2ParallelCorridorStrokeWidth(run, options, spacing);
            if (run.Source == "exact-shared-platform")
            {
                AppendSchematicV2ParallelCorridorKnockout(svg, run, lanes.Count, spacing, strokeWidth, options);
            }

            List<double> offsets = CreateSchematicV2ParallelCorridorOffsets(run, lanes, spacing);
            for (int i = 0; i < lanes.Count; i++)
            {
                string? offsetMode = IsDominantExactSharedPlatformLane(run, lanes, lanes[i])
                    ? "dominant-visible-lane-centered"
                    : offsets.Count == lanes.Count && offsets.Any(offset => Math.Abs(offset) <= 0.001)
                        ? "dominant-visible-lane-adjacent"
                        : null;
                AppendSchematicV2ParallelCorridorOverlay(svg, run, lanes[i], offsets[i], strokeWidth, options, offsetMode);
            }
        }

        svg.AppendLine("</g>");
    }

    private static List<double> CreateSchematicV2ParallelCorridorOffsets(
        SchematicV2SharedCorridorRun run,
        List<SchematicV2ParallelCorridorLane> lanes,
        double spacing)
    {
        if (!ShouldCenterDominantExactSharedPlatformLane(run, lanes))
        {
            double center = (lanes.Count - 1) / 2.0;
            return lanes
                .Select((_, index) => (index - center) * spacing)
                .ToList();
        }

        int dominantIndex = lanes.FindIndex(lane => lane.Routes.Count > 1);
        List<double> offsets = Enumerable.Repeat(0.0, lanes.Count).ToList();
        List<int> remaining = Enumerable
            .Range(0, lanes.Count)
            .Where(index => index != dominantIndex)
            .ToList();
        double fallbackSign = -1;
        foreach (int laneIndex in remaining)
        {
            double sideScore = CalculateSchematicV2ParallelLaneSideScore(run, lanes[laneIndex]);
            double sign;
            if (Math.Abs(sideScore) >= 0.08)
            {
                sign = sideScore > 0 ? 1 : -1;
            }
            else
            {
                sign = fallbackSign;
                fallbackSign *= -1;
            }

            int sameSideCount = offsets.Count(offset => Math.Sign(offset) == Math.Sign(sign));
            double magnitude = spacing * (0.5 + sameSideCount);
            offsets[laneIndex] = sign * magnitude;
        }

        return offsets;
    }

    private static bool ShouldCenterDominantExactSharedPlatformLane(
        SchematicV2SharedCorridorRun run,
        List<SchematicV2ParallelCorridorLane> lanes)
    {
        return run.Source == "exact-shared-platform"
            && lanes.Count >= 2
            && lanes.Any(lane => lane.Routes.Count > 1);
    }

    private static bool IsDominantExactSharedPlatformLane(
        SchematicV2SharedCorridorRun run,
        List<SchematicV2ParallelCorridorLane> lanes,
        SchematicV2ParallelCorridorLane lane)
    {
        return ShouldCenterDominantExactSharedPlatformLane(run, lanes)
            && lane.Routes.Count == lanes.Max(candidate => candidate.Routes.Count);
    }

    private static List<SchematicV2ParallelCorridorLane> CreateSchematicV2ParallelCorridorLanes(
        SchematicV2SharedCorridorRun run,
        List<RenderRoute> runRoutes)
    {
        if (run.Source != "exact-shared-platform")
        {
            return runRoutes
                .Select(route => new SchematicV2ParallelCorridorLane(
                    VisibleLaneResolver.CreateKey(route.Family),
                    [route],
                    route))
                .ToList();
        }

        return runRoutes
            .Select(route => new { Route = route, LaneKey = VisibleLaneResolver.CreateKey(route.Family) })
            .GroupBy(item => item.LaneKey.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                List<RenderRoute> routes = group.Select(item => item.Route).ToList();
                RenderRoute primary = routes
                    .OrderBy(route => route.Family, Comparer<DisplayLineFamily>.Create(VisibleLaneResolver.ComparePrimaryFamily))
                    .First();
                return new SchematicV2ParallelCorridorLane(group.First().LaneKey, routes, primary);
            })
            .OrderByDescending(lane => lane.Routes.Count)
            .ThenByDescending(lane => CalculateSchematicV2ParallelLaneSideScore(run, lane))
            .ThenBy(lane => run.FamilyKeys.FindIndex(familyKey => lane.Routes.Any(route => string.Equals(route.Family.FamilyKey, familyKey, StringComparison.Ordinal))))
            .ToList();
    }

    private static double CalculateSchematicV2ParallelLaneSideScore(
        SchematicV2SharedCorridorRun run,
        SchematicV2ParallelCorridorLane lane)
    {
        List<double> scores = [];
        foreach (RenderRoute route in lane.Routes)
        {
            foreach (RoutePolyline polyline in route.RoutePointSet.Polylines)
            {
                List<SvgPoint> points = polyline.Points;
                for (int i = 1; i < points.Count; i++)
                {
                    SvgPoint start = points[i - 1];
                    SvgPoint end = points[i];
                    if (!TryGetRunSegmentDirection(run, start, end, out SvgPoint direction))
                    {
                        continue;
                    }

                    if (i >= 2)
                    {
                        AddLaneSideScore(scores, direction, start, points[i - 2]);
                    }

                    if (i + 1 < points.Count)
                    {
                        AddLaneSideScore(scores, direction, end, points[i + 1]);
                    }
                }
            }
        }

        if (scores.Count == 0)
        {
            return 0;
        }

        return scores.Average();
    }

    private static bool TryGetRunSegmentDirection(
        SchematicV2SharedCorridorRun run,
        SvgPoint start,
        SvgPoint end,
        out SvgPoint direction)
    {
        for (int i = 1; i < run.Points.Count; i++)
        {
            SvgPoint runStart = run.Points[i - 1];
            SvgPoint runEnd = run.Points[i];
            bool sameDirection = PointsNearlyEqual(start, runStart) && PointsNearlyEqual(end, runEnd);
            bool reverseDirection = PointsNearlyEqual(start, runEnd) && PointsNearlyEqual(end, runStart);
            if (!sameDirection && !reverseDirection)
            {
                continue;
            }

            direction = Normalize(new SvgPoint(runEnd.X - runStart.X, runEnd.Y - runStart.Y));
            return Math.Abs(direction.X) > 0.001 || Math.Abs(direction.Y) > 0.001;
        }

        direction = new SvgPoint(0, 0);
        return false;
    }

    private static void AddLaneSideScore(List<double> scores, SvgPoint direction, SvgPoint sharedPoint, SvgPoint externalPoint)
    {
        SvgPoint vector = new(externalPoint.X - sharedPoint.X, externalPoint.Y - sharedPoint.Y);
        double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length < 0.001)
        {
            return;
        }

        SvgPoint normalized = new(vector.X / length, vector.Y / length);
        double side = Cross(direction.X, direction.Y, normalized.X, normalized.Y);
        if (Math.Abs(side) < 0.08)
        {
            return;
        }

        scores.Add(side);
    }

    private static bool PointsNearlyEqual(SvgPoint first, SvgPoint second)
    {
        return Math.Abs(first.X - second.X) < 0.001 && Math.Abs(first.Y - second.Y) < 0.001;
    }

    private static double GetSchematicV2ParallelCorridorSpacing(
        SchematicV2SharedCorridorRun run,
        SvgRenderOptions options)
    {
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        if (run.Source == "exact-shared-platform")
        {
            return Math.Max(visualStyle.BaseRouteWidth * 0.95, options.GridSize * 0.14);
        }

        return Math.Max(options.LineWidth * 0.75, options.GridSize * 0.10);
    }

    private static double GetSchematicV2ParallelCorridorStrokeWidth(
        SchematicV2SharedCorridorRun run,
        SvgRenderOptions options,
        double spacing)
    {
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        if (run.Source == "exact-shared-platform")
        {
            return Math.Clamp(visualStyle.BaseRouteWidth * 0.78, 7.5, Math.Min(visualStyle.BaseRouteWidth, spacing - 2));
        }

        return Math.Max(options.LineWidth * 0.62, options.LineWidth - spacing);
    }

    private static void AppendSchematicV2ParallelCorridorKnockout(
        StringBuilder svg,
        SchematicV2SharedCorridorRun run,
        int familyCount,
        double spacing,
        double strokeWidth,
        SvgRenderOptions options)
    {
        if (run.Points.Count < 2)
        {
            return;
        }

        if (familyCount < 2)
        {
            return;
        }

        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        double center = (familyCount - 1) / 2.0;
        double maxOffset = center * spacing;
        double visibleEnvelopeWidth = strokeWidth + maxOffset * 2;
        double knockoutWidth = Math.Max(
            visualStyle.BaseRouteWidth + 1,
            visibleEnvelopeWidth - 0.5);
        knockoutWidth = Math.Min(knockoutWidth, visibleEnvelopeWidth);
        string pointList = string.Join(" ", run.Points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
        string familyKeys = string.Join("|", run.FamilyKeys);
        svg.AppendLine($"""<polyline class="schematic-v2-parallel-corridor-knockout" data-schematic-v2-parallel-corridor-knockout="true" data-schematic-v2-shared-corridor-run-id="{Escape(run.RunId)}" data-schematic-v2-parallel-corridor-source="{Escape(run.Source)}" data-schematic-v2-shared-corridor-family-count="{run.FamilyKeys.Count}" data-schematic-v2-shared-corridor-families="{Escape(familyKeys)}" data-schematic-v2-knockout-width="{Format(knockoutWidth)}" data-schematic-v2-visible-envelope-width="{Format(visibleEnvelopeWidth)}" points="{pointList}" stroke="#ffffff" stroke-linecap="butt" stroke-linejoin="round" style="stroke-width: {Format(knockoutWidth)};" />""");
    }

    private static void AppendSchematicV2ParallelCorridorOverlay(
        StringBuilder svg,
        SchematicV2SharedCorridorRun run,
        SchematicV2ParallelCorridorLane lane,
        double offset,
        double strokeWidth,
        SvgRenderOptions options,
        string? offsetMode = null)
    {
        RenderRoute renderRoute = lane.PrimaryRoute;
        List<SvgPoint> points = OffsetPolyline(run.Points, offset);
        if (points.Count < 2)
        {
            return;
        }

        string pointList = string.Join(" ", points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
        string stationIds = string.Join(">", run.StationIds);
        string routeGuideAttribute = run.Source == "geometry-route-guide"
            ? " data-schematic-v2-route-guide-materialized=\"true\""
            : " data-schematic-v2-parallel-platform=\"true\"";
        string offsetModeAttribute = string.IsNullOrWhiteSpace(offsetMode)
            ? string.Empty
            : $" data-schematic-v2-parallel-offset-mode=\"{Escape(offsetMode)}\"";
        string familyKeys = string.Join("|", run.FamilyKeys);
        string laneFamilies = string.Join("|", lane.Routes.Select(route => route.Family.FamilyKey));
        string attributes = $"class=\"schematic-v2-parallel-corridor\" data-line-id=\"{Escape(renderRoute.Line.Id)}\" data-display-family-key=\"{Escape(renderRoute.Family.FamilyKey)}\" data-schematic-v2-canonical-corridor=\"true\" data-schematic-v2-shared-corridor-run=\"true\" data-schematic-v2-shared-corridor-run-id=\"{Escape(run.RunId)}\" data-schematic-v2-shared-corridor-family-a=\"{Escape(run.FamilyAKey)}\" data-schematic-v2-shared-corridor-family-b=\"{Escape(run.FamilyBKey)}\" data-schematic-v2-shared-corridor-family-count=\"{run.FamilyKeys.Count}\" data-schematic-v2-shared-corridor-families=\"{Escape(familyKeys)}\" data-schematic-v2-visible-lane-key=\"{Escape(lane.LaneKey.Key)}\" data-schematic-v2-visible-lane-token=\"{Escape(lane.LaneKey.DisplayToken)}\" data-schematic-v2-visible-lane-reason=\"{Escape(lane.LaneKey.Reason)}\" data-schematic-v2-visible-lane-family-count=\"{lane.Routes.Count}\" data-schematic-v2-visible-lane-families=\"{Escape(laneFamilies)}\" data-schematic-v2-parallel-corridor=\"true\" data-schematic-v2-parallel-corridor-source=\"{Escape(run.Source)}\" data-schematic-v2-parallel-offset=\"{Format(offset)}\" data-schematic-v2-parallel-stroke-width=\"{Format(strokeWidth)}\"{offsetModeAttribute}{routeGuideAttribute} data-schematic-v2-pass-through-stations=\"{Escape(stationIds)}\" data-schematic-v2-shared-corridor-point-count=\"{run.Points.Count}\" points=\"{pointList}\"";
        svg.AppendLine($"""<polyline {attributes} stroke="{Escape(renderRoute.Family.Color)}" style="stroke-width: {Format(strokeWidth)};" />""");

        if (!lane.Routes.Any(route => ShouldRenderExpressCenterStripe(options, route.Family)))
        {
            return;
        }

        string stripeAttributes = attributes.Replace(
            "class=\"schematic-v2-parallel-corridor\"",
            "class=\"express-decoration schematic-v2-parallel-corridor-express\"",
            StringComparison.Ordinal);
        svg.AppendLine($"""<polyline {stripeAttributes} data-express-marker="white-center-stripe" data-express-family="{Escape(renderRoute.Family.FamilyKey)}" data-schematic-v2-express-marker="white-center-stripe" data-schematic-v2-parallel-corridor-express-marker="true" stroke="#ffffff" style="stroke-width: {Format(GetExpressCenterStripeWidth(options))};" />""");
    }

    private static List<SchematicV2SharedCorridorRun> BuildSchematicV2SharedCorridorRuns(
        List<RenderRoute> renderRoutes,
        RenderGeometry geometry)
    {
        HashSet<string> materializedPairKeys = geometry.SchematicV2RouteGuideMetadataByFamily is null
            ? []
            : geometry.SchematicV2RouteGuideMetadataByFamily.Values
                .Select(metadata => CreateFamilyPairKey(metadata.FamilyAKey, metadata.FamilyBKey))
                .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, DisplayLineFamily> familyByKey = renderRoutes
            .GroupBy(route => route.Family.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Family, StringComparer.Ordinal);

        Dictionary<string, SchematicV2SharedSegmentBuilder> segmentBuilders = [];
        HashSet<string> seenFamilySegments = new(StringComparer.Ordinal);
        foreach (RenderRoute route in renderRoutes)
        {
            string familyKey = route.Family.FamilyKey;
            foreach (RoutePolyline polyline in route.RoutePointSet.Polylines)
            {
                for (int i = 1; i < polyline.Points.Count; i++)
                {
                    SvgPoint start = polyline.Points[i - 1];
                    SvgPoint end = polyline.Points[i];
                    if (Distance(start, end) <= 0.001)
                    {
                        continue;
                    }

                    SchematicSegmentKey segmentKey = CreateSchematicSegmentKey(start, end);
                    if (!seenFamilySegments.Add($"{familyKey}|{segmentKey.Key}"))
                    {
                        continue;
                    }

                    if (!segmentBuilders.TryGetValue(segmentKey.Key, out SchematicV2SharedSegmentBuilder? builder))
                    {
                        builder = new SchematicV2SharedSegmentBuilder(segmentKey.Start, segmentKey.End);
                        segmentBuilders[segmentKey.Key] = builder;
                    }

                    builder.FamilyKeys.Add(familyKey);
                }
            }
        }

        Dictionary<string, List<SchematicV2SharedSegment>> byGroup = [];
        foreach ((string key, SchematicV2SharedSegmentBuilder builder) in segmentBuilders)
        {
            if (builder.FamilyKeys.Count < 2)
            {
                continue;
            }

            string[] familyKeys = builder.FamilyKeys.OrderBy(value => value, StringComparer.CurrentCulture).ToArray();
            string groupKey = string.Join("|", familyKeys);
            bool isMaterializedGeometryPair = familyKeys.Length == 2
                && materializedPairKeys.Contains(CreateFamilyPairKey(familyKeys[0], familyKeys[1]));
            bool isExactParallelPlatformPair = !isMaterializedGeometryPair
                && familyKeys.All(familyByKey.ContainsKey);
            if (!isMaterializedGeometryPair && !isExactParallelPlatformPair)
            {
                continue;
            }

            string source = isMaterializedGeometryPair ? "geometry-route-guide" : "exact-shared-platform";
            string sharedGroupKey = $"{source}|{groupKey}";
            if (!byGroup.TryGetValue(sharedGroupKey, out List<SchematicV2SharedSegment>? segments))
            {
                segments = [];
                byGroup[sharedGroupKey] = segments;
            }

            segments.Add(new SchematicV2SharedSegment(key, builder.Start, builder.End, familyKeys.ToList(), source));
        }

        Dictionary<string, string> stationIdByPointKey = geometry.StationPoints
            .GroupBy(pair => CreateSchematicPointKey(pair.Value), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

        List<SchematicV2SharedCorridorRun> runs = [];
        int runIndex = 1;
        foreach ((_, List<SchematicV2SharedSegment> segments) in byGroup)
        {
            foreach (List<SvgPoint> points in BuildSchematicV2SharedSegmentChains(segments))
            {
                SchematicV2SharedSegment firstSegment = segments[0];
                if (points.Count < 2)
                {
                    continue;
                }

                List<string> stationIds = points
                    .Select(point => stationIdByPointKey.TryGetValue(CreateSchematicPointKey(point), out string? stationId) ? stationId : null)
                    .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                    .Select(stationId => stationId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                runs.Add(new SchematicV2SharedCorridorRun(
                    $"schematic-v2-shared-{runIndex++}",
                    firstSegment.FamilyKeys,
                    firstSegment.Source,
                    points,
                    stationIds));
            }
        }

        return runs;
    }

    private static List<List<SvgPoint>> BuildSchematicV2SharedSegmentChains(List<SchematicV2SharedSegment> segments)
    {
        Dictionary<string, SvgPoint> pointsByKey = [];
        Dictionary<string, List<SchematicV2SharedSegment>> adjacency = [];
        foreach (SchematicV2SharedSegment segment in segments)
        {
            string startKey = CreateSchematicPointKey(segment.Start);
            string endKey = CreateSchematicPointKey(segment.End);
            pointsByKey[startKey] = segment.Start;
            pointsByKey[endKey] = segment.End;
            if (!adjacency.TryGetValue(startKey, out List<SchematicV2SharedSegment>? startSegments))
            {
                startSegments = [];
                adjacency[startKey] = startSegments;
            }

            if (!adjacency.TryGetValue(endKey, out List<SchematicV2SharedSegment>? endSegments))
            {
                endSegments = [];
                adjacency[endKey] = endSegments;
            }

            startSegments.Add(segment);
            endSegments.Add(segment);
        }

        HashSet<string> used = new(StringComparer.Ordinal);
        List<List<SvgPoint>> chains = [];
        foreach (SchematicV2SharedSegment seed in segments)
        {
            if (used.Contains(seed.Key))
            {
                continue;
            }

            string startKey = FindSchematicV2ChainEndpoint(seed, adjacency, used);
            List<SvgPoint> chain = [];
            string currentKey = startKey;
            string? previousKey = null;
            while (true)
            {
                chain.Add(pointsByKey[currentKey]);
                SchematicV2SharedSegment? next = adjacency[currentKey]
                    .Where(segment => !used.Contains(segment.Key))
                    .Select(segment => (SchematicV2SharedSegment?)segment)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                used.Add(next.Value.Key);
                string nextKey = CreateSchematicPointKey(next.Value.Start) == currentKey
                    ? CreateSchematicPointKey(next.Value.End)
                    : CreateSchematicPointKey(next.Value.Start);
                if (previousKey is not null && nextKey == previousKey)
                {
                    break;
                }

                previousKey = currentKey;
                currentKey = nextKey;
            }

            if (chain.Count >= 2)
            {
                chains.Add(chain);
            }
        }

        return chains;
    }

    private static string FindSchematicV2ChainEndpoint(
        SchematicV2SharedSegment seed,
        Dictionary<string, List<SchematicV2SharedSegment>> adjacency,
        HashSet<string> used)
    {
        HashSet<string> reachable = [];
        Stack<string> stack = new();
        stack.Push(CreateSchematicPointKey(seed.Start));
        stack.Push(CreateSchematicPointKey(seed.End));
        while (stack.Count > 0)
        {
            string key = stack.Pop();
            if (!reachable.Add(key))
            {
                continue;
            }

            foreach (SchematicV2SharedSegment segment in adjacency[key])
            {
                if (used.Contains(segment.Key))
                {
                    continue;
                }

                string startKey = CreateSchematicPointKey(segment.Start);
                string endKey = CreateSchematicPointKey(segment.End);
                stack.Push(startKey == key ? endKey : startKey);
            }
        }

        return reachable
            .Where(key => adjacency[key].Count(segment => !used.Contains(segment.Key)) <= 1)
            .OrderBy(key => key, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? CreateSchematicPointKey(seed.Start);
    }

    private static List<SvgPoint> OffsetPolyline(List<SvgPoint> points, double offset)
    {
        if (points.Count < 2 || Math.Abs(offset) <= 0.001)
        {
            return points.ToList();
        }

        List<SvgPoint> result = [];
        for (int i = 0; i < points.Count; i++)
        {
            SvgPoint normal;
            if (i == 0)
            {
                normal = GetSegmentNormal(points[0], points[1]);
            }
            else if (i == points.Count - 1)
            {
                normal = GetSegmentNormal(points[^2], points[^1]);
            }
            else
            {
                SvgPoint before = GetSegmentNormal(points[i - 1], points[i]);
                SvgPoint after = GetSegmentNormal(points[i], points[i + 1]);
                normal = Normalize(new SvgPoint(before.X + after.X, before.Y + after.Y));
                if (Distance(new SvgPoint(0, 0), normal) <= 0.001)
                {
                    normal = after;
                }
            }

            result.Add(new SvgPoint(points[i].X + normal.X * offset, points[i].Y + normal.Y * offset));
        }

        return result;
    }

    private static string CreateSchematicPointKey(SvgPoint point)
    {
        return $"{Format(point.X)},{Format(point.Y)}";
    }

    private static Dictionary<string, SchematicSegmentOccupancy> BuildSchematicSegmentOccupancy(List<RenderRoute> renderRoutes)
    {
        Dictionary<string, SchematicSegmentOccupancyBuilder> builders = [];
        HashSet<string> seenFamilySegments = new(StringComparer.Ordinal);

        foreach (RenderRoute renderRoute in renderRoutes)
        {
            string familyKey = renderRoute.Family.FamilyKey;
            foreach (RoutePolyline polyline in renderRoute.RoutePointSet.Polylines)
            {
                for (int i = 1; i < polyline.Points.Count; i++)
                {
                    SvgPoint start = polyline.Points[i - 1];
                    SvgPoint end = polyline.Points[i];
                    if (Distance(start, end) <= 0.001)
                    {
                        continue;
                    }

                    SchematicSegmentKey segmentKey = CreateSchematicSegmentKey(start, end);
                    string familySegmentKey = $"{segmentKey.Key}|{familyKey}";
                    if (!seenFamilySegments.Add(familySegmentKey))
                    {
                        continue;
                    }

                    if (!builders.TryGetValue(segmentKey.Key, out SchematicSegmentOccupancyBuilder? builder))
                    {
                        builder = new SchematicSegmentOccupancyBuilder(segmentKey.Start, segmentKey.End);
                        builders[segmentKey.Key] = builder;
                    }

                    builder.Families[familyKey] = renderRoute.Family.DisplayName;
                }
            }
        }

        Dictionary<string, SchematicSegmentOccupancy> result = [];
        foreach ((string key, SchematicSegmentOccupancyBuilder builder) in builders)
        {
            List<string> familyKeys = builder.Families
                .Select(pair => new
                {
                    FamilyKey = pair.Key,
                    DisplayName = pair.Value,
                    LineNumber = ExtractLineNumber(pair.Value)
                })
                .OrderBy(item => item.LineNumber.HasValue ? 0 : 1)
                .ThenBy(item => item.LineNumber ?? int.MaxValue)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
                .ThenBy(item => item.FamilyKey, StringComparer.Ordinal)
                .Select(item => item.FamilyKey)
                .ToList();

            result[key] = new SchematicSegmentOccupancy(key, builder.Start, builder.End, familyKeys);
        }

        return result;
    }

    private static void AppendSchematicRoutePolylines(
        StringBuilder svg,
        RenderRoute renderRoute,
        Dictionary<string, SchematicSegmentOccupancy> occupancyBySegment,
        SvgRenderOptions options)
    {
        MetroLine line = renderRoute.Line;
        DisplayLineFamily family = renderRoute.Family;
        RoutePointSet routePointSet = renderRoute.RoutePointSet;
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        double overlapOffsetDistance = visualStyle.SchematicOverlapOffsetDistance;
        double overlapEndpointTrim = visualStyle.SchematicOverlapEndpointTrim;
        Dictionary<string, int> schematicRenderKeyCounts = CountSchematicOverlapRenderKeys(
            renderRoute,
            occupancyBySegment);
        int segmentRoutePartIndex = 0;
        int segmentRoutePartCount = renderRoute.RoutePointSet.Polylines
            .Sum(polyline => Math.Max(0, polyline.Points.Count - 1));

        foreach (RoutePolyline polyline in routePointSet.Polylines)
        {
            for (int i = 1; i < polyline.Points.Count; i++)
            {
                SvgPoint start = polyline.Points[i - 1];
                SvgPoint end = polyline.Points[i];
                if (Distance(start, end) <= 0.001)
                {
                    continue;
                }

                segmentRoutePartIndex++;
                List<SvgPoint> segmentPoints = [start, end];
                SchematicSegmentKey segmentKey = CreateSchematicSegmentKey(start, end);
                string overlapAttributes = string.Empty;
                string routeStyle = string.Empty;

                if (occupancyBySegment.TryGetValue(segmentKey.Key, out SchematicSegmentOccupancy? occupancy)
                    && occupancy.FamilyKeys.Count > 1)
                {
                    int overlapIndex = occupancy.FamilyKeys.IndexOf(family.FamilyKey);
                    if (overlapIndex < 0)
                    {
                        overlapAttributes = " data-schematic-overlap-fallback=\"family-not-found\"";
                    }
                    else
                    {
                        double offsetIndex = overlapIndex - (occupancy.FamilyKeys.Count - 1) / 2.0;
                        double originalSegmentLength = Distance(start, end);
                        SchematicOverlapSafety safety = GetSchematicOverlapSafety(
                            start,
                            end,
                            occupancyBySegment,
                            visualStyle);
                        string renderMode = safety.SafeToOffset ? "offset" : "centered";
                        string renderDedupeKey = CreateSchematicRenderDedupeKey(family.FamilyKey, segmentKey.Key);
                        int renderDedupeCount = schematicRenderKeyCounts.TryGetValue(renderDedupeKey, out int count)
                            ? count
                            : 1;

                        double offset = safety.SafeToOffset ? offsetIndex * overlapOffsetDistance : 0;
                        string trimAttributes = string.Empty;
                        if (safety.SafeToOffset)
                        {
                            SvgPoint normal = GetSegmentNormal(start, end);
                            SchematicOverlapSegment overlapSegment = BuildSchematicOverlapSegment(
                                start,
                                end,
                                normal,
                                offset,
                                overlapEndpointTrim);
                            segmentPoints = overlapSegment.Points;
                            trimAttributes = overlapSegment.TrimApplied
                                ? $" data-schematic-overlap-trim=\"true\" data-schematic-overlap-trim-distance=\"{Format(overlapSegment.TrimDistance)}\""
                                : string.Empty;
                            if (overlapSegment.TrimFallback is not null)
                            {
                                trimAttributes += $" data-schematic-overlap-trim-fallback=\"{Escape(overlapSegment.TrimFallback)}\"";
                            }
                            routeStyle = "stroke-linecap: butt;";
                        }

                        string fallbackAttributes = safety.SafeToOffset
                            ? string.Empty
                            : $" data-schematic-overlap-fallback=\"unsafe-short-or-junction\" data-schematic-overlap-safe-offset=\"false\" data-schematic-overlap-safe-offset-reason=\"{Escape(safety.Reason)}\"";
                        string safeOffsetAttributes = safety.SafeToOffset
                            ? " data-schematic-overlap-safe-offset=\"true\""
                            : string.Empty;
                        string dedupeAttributes = renderDedupeCount > 1
                            ? $" data-schematic-render-duplicate-count=\"{renderDedupeCount}\" data-schematic-render-dedupe-skipped=\"continuity-priority\""
                            : string.Empty;
                        overlapAttributes = $" data-schematic-overlap=\"true\" data-schematic-overlap-family-count=\"{occupancy.FamilyKeys.Count}\" data-schematic-overlap-index=\"{overlapIndex}\" data-schematic-overlap-offset=\"{Format(offset)}\" data-schematic-overlap-original-length=\"{Format(originalSegmentLength)}\" data-schematic-overlap-render-mode=\"{renderMode}\" data-schematic-segment-key=\"{Escape(segmentKey.Key)}\"{safeOffsetAttributes}{fallbackAttributes}{trimAttributes}{dedupeAttributes}";
                    }
                }

                string pointList = string.Join(" ", segmentPoints.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
                string routePartAttributes = segmentRoutePartCount > 1
                    ? $" data-route-part-index=\"{segmentRoutePartIndex - 1}\" data-route-part-count=\"{segmentRoutePartCount}\""
                    : string.Empty;
                string familyAttributes = $" data-display-family-key=\"{Escape(family.FamilyKey)}\" data-display-family-member-count=\"{family.Variants.Count}\" data-display-family-primary-line-id=\"{Escape(family.PrimaryLine.Id)}\" data-display-family-merged=\"{(family.Variants.Count > 1 ? "true" : "false")}\"";
                string colorMismatchAttribute = family.HasColorMismatch
                    ? " data-display-family-color-mismatch=\"true\""
                    : string.Empty;
                string commonAttributes = $"class=\"route\" data-line-id=\"{Escape(line.Id)}\" data-route-source=\"{routePointSet.Source}\"{familyAttributes}{colorMismatchAttribute}{routePartAttributes}{overlapAttributes} points=\"{pointList}\"";
                AppendStandardRoutePolyline(svg, commonAttributes, family, options, routeStyle);
            }
        }
    }

    private static Dictionary<string, int> CountSchematicOverlapRenderKeys(
        RenderRoute renderRoute,
        Dictionary<string, SchematicSegmentOccupancy> occupancyBySegment)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        string familyKey = renderRoute.Family.FamilyKey;

        foreach (RoutePolyline polyline in renderRoute.RoutePointSet.Polylines)
        {
            for (int i = 1; i < polyline.Points.Count; i++)
            {
                SvgPoint start = polyline.Points[i - 1];
                SvgPoint end = polyline.Points[i];
                if (Distance(start, end) <= 0.001)
                {
                    continue;
                }

                SchematicSegmentKey segmentKey = CreateSchematicSegmentKey(start, end);
                if (!occupancyBySegment.TryGetValue(segmentKey.Key, out SchematicSegmentOccupancy? occupancy)
                    || occupancy.FamilyKeys.Count <= 1
                    || !occupancy.FamilyKeys.Contains(familyKey, StringComparer.Ordinal))
                {
                    continue;
                }

                string key = CreateSchematicRenderDedupeKey(familyKey, segmentKey.Key);
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    private static string CreateSchematicRenderDedupeKey(string familyKey, string segmentKey)
    {
        return $"{familyKey}|{segmentKey}";
    }

    private static SchematicOverlapSafety GetSchematicOverlapSafety(
        SvgPoint start,
        SvgPoint end,
        Dictionary<string, SchematicSegmentOccupancy> occupancyBySegment,
        SvgVisualStyle visualStyle)
    {
        double length = Distance(start, end);
        if (length <= visualStyle.SchematicShortOverlapSegmentThreshold)
        {
            return new SchematicOverlapSafety(false, "short-segment");
        }

        double remainingLengthAfterTrim = length - visualStyle.SchematicOverlapEndpointTrim * 2;
        if (remainingLengthAfterTrim < visualStyle.BaseRouteWidth * 2)
        {
            return new SchematicOverlapSafety(false, "trim-too-short");
        }

        if (IsSchematicEndpointComplex(start, occupancyBySegment)
            || IsSchematicEndpointComplex(end, occupancyBySegment))
        {
            return new SchematicOverlapSafety(false, "high-degree-junction");
        }

        return new SchematicOverlapSafety(true, "safe-long-simple-segment");
    }

    private static bool IsSchematicEndpointComplex(
        SvgPoint point,
        Dictionary<string, SchematicSegmentOccupancy> occupancyBySegment)
    {
        HashSet<string> touchingSegments = new(StringComparer.Ordinal);
        HashSet<string> touchingFamilies = new(StringComparer.Ordinal);

        foreach (SchematicSegmentOccupancy occupancy in occupancyBySegment.Values)
        {
            if (Distance(point, occupancy.Start) > 0.001
                && Distance(point, occupancy.End) > 0.001)
            {
                continue;
            }

            touchingSegments.Add(occupancy.Key);
            foreach (string familyKey in occupancy.FamilyKeys)
            {
                touchingFamilies.Add(familyKey);
            }
        }

        return touchingSegments.Count >= 3
            || (touchingSegments.Count >= 2 && touchingFamilies.Count >= 3);
    }

    private static SchematicOverlapSegment BuildSchematicOverlapSegment(
        SvgPoint start,
        SvgPoint end,
        SvgPoint normal,
        double offset,
        double requestedTrim)
    {
        double length = Distance(start, end);
        SvgPoint direction = length <= 0.001
            ? new SvgPoint(0, 0)
            : new SvgPoint((end.X - start.X) / length, (end.Y - start.Y) / length);
        SvgPoint offsetStart = new(start.X + normal.X * offset, start.Y + normal.Y * offset);
        SvgPoint offsetEnd = new(end.X + normal.X * offset, end.Y + normal.Y * offset);

        if (length <= 0.001)
        {
            return new SchematicOverlapSegment([offsetStart, offsetEnd], false, 0, "zero-length");
        }

        double trim = Math.Max(0, requestedTrim);
        if (trim <= 0)
        {
            return new SchematicOverlapSegment([offsetStart, offsetEnd], false, 0, "disabled");
        }

        double maxTrim = Math.Max(0, (length - 1) / 2);
        if (maxTrim <= 0)
        {
            return new SchematicOverlapSegment([offsetStart, offsetEnd], false, 0, "segment-too-short");
        }

        bool shortened = trim > maxTrim;
        trim = Math.Min(trim, maxTrim);
        SvgPoint trimmedStart = new(offsetStart.X + direction.X * trim, offsetStart.Y + direction.Y * trim);
        SvgPoint trimmedEnd = new(offsetEnd.X - direction.X * trim, offsetEnd.Y - direction.Y * trim);
        return new SchematicOverlapSegment(
            [trimmedStart, trimmedEnd],
            true,
            trim,
            shortened ? "segment-too-short" : null);
    }

    private static SchematicSegmentKey CreateSchematicSegmentKey(SvgPoint a, SvgPoint b)
    {
        SvgPoint first = ComparePoints(a, b) <= 0 ? a : b;
        SvgPoint second = ComparePoints(a, b) <= 0 ? b : a;
        string key = $"{Format(first.X)},{Format(first.Y)}|{Format(second.X)},{Format(second.Y)}";
        return new SchematicSegmentKey(key, first, second);
    }

    private static int ComparePoints(SvgPoint a, SvgPoint b)
    {
        int x = a.X.CompareTo(b.X);
        return x != 0 ? x : a.Y.CompareTo(b.Y);
    }

    private static SvgPoint GetSegmentNormal(SvgPoint start, SvgPoint end)
    {
        double length = Distance(start, end);
        if (length <= 0.001)
        {
            return new SvgPoint(0, 0);
        }

        return new SvgPoint(-(end.Y - start.Y) / length, (end.X - start.X) / length);
    }

    private static CorridorRenderPlan BuildGeographicCorridorRenderPlan(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = CreateCorridorSegmentFragments(renderRoutes, stationsById, geometry, options);
        Dictionary<int, HashSet<string>> sharedFamiliesByFragment = [];
        List<SharedCorridorRun> sharedRuns = [];
        HashSet<int> hiddenSharedFragments = [];
        HashSet<int> tooManyFamilyFragments = [];

        if (options.EnableSharedCorridorCompositeStroke && fragments.Count > 1)
        {
            List<CorridorMatch> matches = FindSharedCorridorMatches(fragments, options);
            sharedFamiliesByFragment = GetSharedFamiliesByFragment(fragments, matches);
            hiddenSharedFragments = sharedFamiliesByFragment
                .Where(pair => pair.Value.Count == 2)
                .Select(pair => pair.Key)
                .ToHashSet();
            tooManyFamilyFragments = sharedFamiliesByFragment
                .Where(pair => pair.Value.Count > 2)
                .Select(pair => pair.Key)
                .ToHashSet();
            sharedRuns = BuildSharedCorridorRuns(fragments, sharedFamiliesByFragment, options);
        }

        Dictionary<int, List<CorridorSegmentFragment>> fragmentsByRoute = fragments
            .GroupBy(fragment => fragment.RouteIndex)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .ToList());

        List<CorridorDrawCommand> normal = [];
        List<CorridorDrawCommand> sharedBase = [];
        List<CorridorDrawCommand> sharedInner = [];
        List<CorridorDrawCommand> expressDecorations = [];

        foreach (SharedCorridorRun run in sharedRuns)
        {
            RenderRoute renderRoute = renderRoutes[run.RouteIndex];
            bool expressConflict = options.EnableExpressCenterStripe
                && (HasExpressServiceVariant(run.FamilyA) || HasExpressServiceVariant(run.FamilyB));
            string sharedAttributes = $" data-shared-corridor=\"true\" data-shared-corridor-run-id=\"{Escape(run.RunId)}\" data-shared-corridor-family-a=\"{Escape(run.FamilyA.FamilyKey)}\" data-shared-corridor-family-b=\"{Escape(run.FamilyB.FamilyKey)}\" data-shared-corridor-style=\"shanghai-like-continuous\" data-shared-corridor-point-count=\"{run.Points.Count}\" data-shared-corridor-fragment-count=\"{run.FragmentCount}\"";
            if (expressConflict)
            {
                sharedAttributes += " data-express-marker-skipped=\"shared-corridor-style-conflict\"";
            }

            sharedBase.Add(new CorridorDrawCommand(
                renderRoute,
                run.Points,
                CorridorDrawLayer.SharedBase,
                run.FamilyA.Color,
                GetSharedCorridorStrokeWidth(options),
                sharedAttributes + " data-shared-corridor-layer=\"corridor-base\""));
            sharedInner.Add(new CorridorDrawCommand(
                renderRoute,
                run.Points,
                CorridorDrawLayer.SharedInner,
                run.FamilyB.Color,
                GetSharedCorridorInnerStrokeWidth(options),
                sharedAttributes + " data-shared-corridor-layer=\"inner-band\""));
        }

        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints"
                || !fragmentsByRoute.TryGetValue(routeIndex, out List<CorridorSegmentFragment>? routeFragments))
            {
                AddWholeRoutePolylines(renderRoute, normal, expressDecorations, options);
                continue;
            }

            List<SvgPoint> normalPoints = [];
            int normalPartIndex = -1;
            foreach (CorridorSegmentFragment fragment in routeFragments)
            {
                if (hiddenSharedFragments.Contains(fragment.Index))
                {
                    FlushNormalRun();
                    continue;
                }

                if (tooManyFamilyFragments.Contains(fragment.Index))
                {
                    FlushNormalRun();
                    List<SvgPoint> fallbackPoints = [fragment.Start, fragment.End];
                    CorridorDrawCommand fallback = new(
                        renderRoute,
                        fallbackPoints,
                        CorridorDrawLayer.NormalBase,
                        renderRoute.Family.Color,
                        GetNormalRouteStrokeWidth(options),
                        HasExpressServiceVariant(renderRoute.Family)
                            ? " data-shared-corridor-skipped=\"too-many-families\" data-express-marker-skipped=\"shared-corridor-style-conflict\""
                            : " data-shared-corridor-skipped=\"too-many-families\"",
                        fragment.PolylineIndex,
                        renderRoute.RoutePointSet.Polylines.Count);
                    normal.Add(fallback);
                    AddExpressDecorationIfNeeded(fallback, expressDecorations, options);
                    continue;
                }

                AddNormalSegment(fragment);
            }

            FlushNormalRun();

            void AddNormalSegment(CorridorSegmentFragment fragment)
            {
                if (normalPoints.Count == 0)
                {
                    normalPartIndex = fragment.PolylineIndex;
                    normalPoints.Add(fragment.Start);
                    normalPoints.Add(fragment.End);
                    return;
                }

                if (normalPartIndex == fragment.PolylineIndex && Distance(normalPoints[^1], fragment.Start) <= 0.001)
                {
                    AddPointIfNotDuplicate(normalPoints, fragment.End);
                    return;
                }

                FlushNormalRun();
                normalPoints.Add(fragment.Start);
                normalPoints.Add(fragment.End);
            }

            void FlushNormalRun()
            {
                if (normalPoints.Count >= 2)
                {
                    CorridorDrawCommand command = new(
                        renderRoute,
                        normalPoints.ToList(),
                        CorridorDrawLayer.NormalBase,
                        renderRoute.Family.Color,
                        GetNormalRouteStrokeWidth(options),
                        string.Empty,
                        normalPartIndex,
                        renderRoute.RoutePointSet.Polylines.Count);
                    normal.Add(command);
                    AddExpressDecorationIfNeeded(command, expressDecorations, options);
                }

                normalPoints.Clear();
                normalPartIndex = -1;
            }
        }

        return new CorridorRenderPlan(normal, sharedBase, sharedInner, expressDecorations);
    }

    private static void AddWholeRoutePolylines(
        RenderRoute renderRoute,
        List<CorridorDrawCommand> normal,
        List<CorridorDrawCommand> expressDecorations,
        SvgRenderOptions options)
    {
        for (int index = 0; index < renderRoute.RoutePointSet.Polylines.Count; index++)
        {
            RoutePolyline polyline = renderRoute.RoutePointSet.Polylines[index];
            if (polyline.Points.Count < 2)
            {
                continue;
            }

            CorridorDrawCommand command = new(
                renderRoute,
                polyline.Points,
                CorridorDrawLayer.NormalBase,
                renderRoute.Family.Color,
                GetNormalRouteStrokeWidth(options),
                string.Empty,
                index,
                renderRoute.RoutePointSet.Polylines.Count);
            normal.Add(command);
            AddExpressDecorationIfNeeded(command, expressDecorations, options);
        }
    }

    private static void AddExpressDecorationIfNeeded(
        CorridorDrawCommand baseCommand,
        List<CorridorDrawCommand> expressDecorations,
        SvgRenderOptions options)
    {
        if (!ShouldRenderExpressCenterStripe(options, baseCommand.RenderRoute.Family))
        {
            return;
        }

        if (baseCommand.ExtraAttributes.Contains("data-shared-corridor-skipped=", StringComparison.Ordinal)
            || baseCommand.ExtraAttributes.Contains("data-shared-corridor=\"true\"", StringComparison.Ordinal))
        {
            return;
        }

        expressDecorations.Add(baseCommand with
        {
            Layer = CorridorDrawLayer.ExpressDecoration,
            Stroke = "#ffffff",
            StrokeWidth = GetExpressCenterStripeWidth(options),
            ExtraAttributes = baseCommand.ExtraAttributes
                + $" data-express-marker=\"white-center-stripe\" data-express-family=\"{Escape(baseCommand.RenderRoute.Family.FamilyKey)}\""
                + (IsSchematicV2FamilyLayout(options.LayoutMode) ? " data-schematic-v2-express-marker=\"white-center-stripe\"" : string.Empty)
        });
    }

    private static void AppendCorridorRenderPlan(StringBuilder svg, CorridorRenderPlan plan, SvgRenderOptions options)
    {
        foreach (CorridorDrawCommand command in plan.NormalBase)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }

        foreach (CorridorDrawCommand command in plan.SharedBase)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }

        foreach (CorridorDrawCommand command in plan.SharedInner)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }

        foreach (CorridorDrawCommand command in plan.ExpressDecorations)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }
    }

    private static void AppendCorridorDrawCommand(
        StringBuilder svg,
        CorridorDrawCommand command,
        SvgRenderOptions options)
    {
        RenderRoute renderRoute = command.RenderRoute;
        DisplayLineFamily family = renderRoute.Family;
        MetroLine line = renderRoute.Line;
        RoutePointSet routePointSet = renderRoute.RoutePointSet;
        string pointList = string.Join(" ", command.Points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
        string pathPointAttributes = routePointSet.Source == "pathPoints"
            ? $" data-path-point-count=\"{routePointSet.OriginalPathPointCount}\" data-cleaned-path-point-count=\"{routePointSet.CleanedPathPointCount}\" data-path-reduction-ratio=\"{Format(routePointSet.ReductionRatio)}\" data-max-path-segment-length=\"{Format(routePointSet.MaxPathSegmentLength)}\" data-suspicious-jump-count=\"{routePointSet.SuspiciousJumpCount}\" data-path-simplification-tolerance=\"{Format(routePointSet.EffectiveSimplificationTolerance)}\""
            : string.Empty;
        string routePartAttributes = command.RoutePartCount > 1
            ? $" data-route-part-index=\"{command.RoutePartIndex}\" data-route-part-count=\"{command.RoutePartCount}\""
            : string.Empty;
        string familyAttributes = $" data-display-family-key=\"{Escape(family.FamilyKey)}\" data-display-family-member-count=\"{family.Variants.Count}\" data-display-family-primary-line-id=\"{Escape(family.PrimaryLine.Id)}\" data-display-family-merged=\"{(family.Variants.Count > 1 ? "true" : "false")}\"";
        string colorMismatchAttribute = family.HasColorMismatch
            ? " data-display-family-color-mismatch=\"true\""
            : string.Empty;
        string layerAttributes = $" data-corridor-run-layer=\"{GetCorridorDrawLayerName(command.Layer)}\"";
        svg.AppendLine($"""<polyline class="route" data-line-id="{Escape(line.Id)}" data-route-source="{routePointSet.Source}"{familyAttributes}{colorMismatchAttribute}{pathPointAttributes}{routePartAttributes}{layerAttributes}{command.ExtraAttributes} points="{pointList}" stroke="{Escape(command.Stroke)}" style="stroke-width: {Format(command.StrokeWidth)};" />""");
    }

    private static double GetNormalRouteStrokeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).BaseRouteWidth;
    }

    private static double GetSharedCorridorStrokeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).SharedCorridorOuterWidth;
    }

    private static double GetSharedCorridorInnerStrokeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).SharedCorridorInnerWidth;
    }

    private static double GetExpressCenterStripeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).ExpressStripeWidth;
    }

    private static bool ShouldRenderExpressCenterStripe(SvgRenderOptions options, DisplayLineFamily family)
    {
        return HasExpressServiceVariant(family)
            && (options.EnableExpressCenterStripe || IsSchematicV2FamilyLayout(options.LayoutMode));
    }

    private static string GetCorridorDrawLayerName(CorridorDrawLayer layer)
    {
        return layer switch
        {
            CorridorDrawLayer.NormalBase => "normal-base",
            CorridorDrawLayer.SharedBase => "shared-corridor-base",
            CorridorDrawLayer.SharedInner => "shared-corridor-inner",
            CorridorDrawLayer.ExpressDecoration => "express-decoration",
            _ => "unknown"
        };
    }

    private static void AppendStandardRoutePolyline(
        StringBuilder svg,
        string commonAttributes,
        DisplayLineFamily family,
        SvgRenderOptions options,
        string routeStyle = "")
    {
        string styleAttribute = string.IsNullOrWhiteSpace(routeStyle)
            ? string.Empty
            : $" style=\"{Escape(routeStyle)}\"";
        svg.AppendLine($"""<polyline {commonAttributes} stroke="{Escape(family.Color)}"{styleAttribute} />""");
        if (!ShouldRenderExpressCenterStripe(options, family))
        {
            return;
        }

        double stripeWidth = GetExpressCenterStripeWidth(options);
        string stripeStyle = $"stroke-width: {Format(stripeWidth)};";
        if (!string.IsNullOrWhiteSpace(routeStyle))
        {
            stripeStyle += $" {routeStyle}";
        }

        string schematicV2MarkerAttribute = IsSchematicV2FamilyLayout(options.LayoutMode)
            ? " data-schematic-v2-express-marker=\"white-center-stripe\""
            : string.Empty;
        string stripeAttributes = commonAttributes.Replace("class=\"route\"", "class=\"express-decoration\"", StringComparison.Ordinal);
        svg.AppendLine($"""<polyline {stripeAttributes} data-express-marker="white-center-stripe" data-express-family="{Escape(family.FamilyKey)}"{schematicV2MarkerAttribute} stroke="#ffffff" style="{Escape(stripeStyle)}" />""");
    }

    private static void AppendSharedCorridorRoutePolylines(
        StringBuilder svg,
        string commonAttributes,
        SharedCorridorStroke sharedStroke,
        SvgRenderOptions options)
    {
        string sharedAttributes = $" data-shared-corridor=\"true\" data-shared-corridor-run-id=\"{Escape(sharedStroke.RunId)}\" data-shared-corridor-family-a=\"{Escape(sharedStroke.FamilyAKey)}\" data-shared-corridor-family-b=\"{Escape(sharedStroke.FamilyBKey)}\" data-shared-corridor-style=\"shanghai-like\" data-shared-corridor-point-count=\"{sharedStroke.PointCount}\"";
        double corridorWidth = GetSharedCorridorStrokeWidth(options);
        double bandWidth = GetSharedCorridorInnerStrokeWidth(options);

        svg.AppendLine($"""<polyline {commonAttributes}{sharedAttributes} data-shared-corridor-layer="corridor-base" stroke="{Escape(sharedStroke.OuterColor)}" style="stroke-width: {Format(corridorWidth)};" />""");
        svg.AppendLine($"""<polyline {commonAttributes}{sharedAttributes} data-shared-corridor-layer="inner-band" stroke="{Escape(sharedStroke.InnerColor)}" style="stroke-width: {Format(bandWidth)};" />""");
    }

    private static List<RenderRoute> ApplyParallelCorridorOffset(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = CreateCorridorSegmentFragments(renderRoutes, stationsById, geometry, options);

        if (fragments.Count < 2)
        {
            return renderRoutes;
        }

        Dictionary<int, CorridorAssignment> assignments = DetectCorridors(fragments, options);
        if (assignments.Count == 0)
        {
            return SegmentizePathPointRoutes(renderRoutes, fragments, []);
        }

        return SegmentizePathPointRoutes(renderRoutes, fragments, assignments);
    }

    private static List<RenderRoute> ApplySharedCorridorCompositeStroke(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = CreateCorridorSegmentFragments(renderRoutes, stationsById, geometry, options);
        if (fragments.Count < 2)
        {
            return renderRoutes;
        }

        List<CorridorMatch> matches = FindSharedCorridorMatches(fragments, options);
        if (matches.Count == 0)
        {
            return renderRoutes;
        }

        Dictionary<int, HashSet<string>> sharedFamiliesByFragment = GetSharedFamiliesByFragment(fragments, matches);
        HashSet<int> tooManyFamilyFragments = sharedFamiliesByFragment
            .Where(pair => pair.Value.Count > 2)
            .Select(pair => pair.Key)
            .ToHashSet();
        HashSet<int> twoFamilySharedFragments = sharedFamiliesByFragment
            .Where(pair => pair.Value.Count == 2)
            .Select(pair => pair.Key)
            .ToHashSet();
        List<SharedCorridorRun> runs = BuildSharedCorridorRuns(fragments, sharedFamiliesByFragment, options);
        if (runs.Count == 0 && tooManyFamilyFragments.Count == 0)
        {
            return renderRoutes;
        }

        return BuildSharedCorridorRenderRoutes(renderRoutes, fragments, runs, twoFamilySharedFragments, tooManyFamilyFragments);
    }

    private static List<CorridorSegmentFragment> CreateCorridorSegmentFragments(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = [];
        double minEligibleLength = Math.Max(20, options.LineWidth * 1.5);
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints")
            {
                continue;
            }

            string lineId = renderRoute.Line.Id ?? $"line_{routeIndex + 1}";
            List<SvgPoint> stationPoints = GetLineStationPoints(renderRoute.Line, stationsById, geometry);
            for (int polylineIndex = 0; polylineIndex < renderRoute.RoutePointSet.Polylines.Count; polylineIndex++)
            {
                List<SvgPoint> points = renderRoute.RoutePointSet.Polylines[polylineIndex].Points;
                for (int segmentIndex = 1; segmentIndex < points.Count; segmentIndex++)
                {
                    SvgPoint start = points[segmentIndex - 1];
                    SvgPoint end = points[segmentIndex];
                    double length = Distance(start, end);
                    if (length <= 0.001)
                    {
                        continue;
                    }

                    SvgPoint direction = new((end.X - start.X) / length, (end.Y - start.Y) / length);
                    SvgPoint normal = new(-direction.Y, direction.X);
                    fragments.Add(new CorridorSegmentFragment(
                        fragments.Count,
                        routeIndex,
                        polylineIndex,
                        segmentIndex - 1,
                        lineId,
                        renderRoute.Family,
                        renderRoute.Family.FamilyKey,
                        renderRoute.Family.DisplayName,
                        renderRoute.Family.Color,
                        start,
                        end,
                        direction,
                        normal,
                        length,
                        length >= minEligibleLength,
                        stationPoints));
                }
            }
        }

        return fragments;
    }

    private static Dictionary<int, CorridorAssignment> DetectCorridors(
        List<CorridorSegmentFragment> fragments,
        SvgRenderOptions options)
    {
        Dictionary<int, CorridorAssignment> assignments = [];
        int corridorNumber = 1;
        foreach (List<CorridorSegmentFragment> group in FindSharedCorridorGroups(fragments, options)
            .Where(group => group.Select(fragment => fragment.LineId).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(group => group.Min(fragment => fragment.RouteIndex))
            .ThenBy(group => group.Min(fragment => fragment.PolylineIndex))
            .ThenBy(group => group.Min(fragment => fragment.SegmentIndex)))
        {
            List<string> lineOrder = group
                .Select(fragment => new { fragment.LineId, fragment.RouteIndex })
                .GroupBy(item => item.LineId, StringComparer.Ordinal)
                .Select(grouping => new { LineId = grouping.Key, RouteIndex = grouping.Min(item => item.RouteIndex) })
                .OrderBy(item => item.RouteIndex)
                .ThenBy(item => item.LineId, StringComparer.Ordinal)
                .Select(item => item.LineId)
                .ToList();

            if (lineOrder.Count < 2)
            {
                continue;
            }

            string corridorId = $"corridor-{corridorNumber++}";
            double spacing = options.LineWidth + 4;
            SvgPoint baseDirection = group
                .OrderBy(fragment => fragment.RouteIndex)
                .ThenBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .First()
                .Direction;

            foreach (CorridorSegmentFragment fragment in group)
            {
                int lineIndex = lineOrder.IndexOf(fragment.LineId);
                if (lineIndex < 0)
                {
                    continue;
                }

                double offsetIndex = lineIndex - (lineOrder.Count - 1) / 2.0;
                assignments[fragment.Index] = new CorridorAssignment(
                    corridorId,
                    lineOrder.Count,
                    offsetIndex,
                    offsetIndex * spacing,
                    baseDirection);
            }
        }

        return assignments;
    }

    private static List<List<CorridorSegmentFragment>> FindSharedCorridorGroups(
        List<CorridorSegmentFragment> fragments,
        SvgRenderOptions options)
    {
        double distanceThreshold = options.LineWidth * 1.5 + 6;
        double cellSize = Math.Max(48, distanceThreshold * 3);
        int[] parent = Enumerable.Range(0, fragments.Count).ToArray();

        int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }

            return value;
        }

        void Union(int left, int right)
        {
            int leftRoot = Find(left);
            int rightRoot = Find(right);
            if (leftRoot != rightRoot)
            {
                parent[rightRoot] = leftRoot;
            }
        }

        Dictionary<(int X, int Y), List<int>> grid = [];
        HashSet<(int A, int B)> checkedPairs = [];
        for (int index = 0; index < fragments.Count; index++)
        {
            CorridorSegmentFragment fragment = fragments[index];
            if (!fragment.IsEligible)
            {
                continue;
            }

            (int minX, int maxX, int minY, int maxY) = GetGridBounds(fragment, cellSize, distanceThreshold);
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    (int X, int Y) cell = (x, y);
                    if (!grid.TryGetValue(cell, out List<int>? candidates))
                    {
                        candidates = [];
                        grid[cell] = candidates;
                    }

                    foreach (int candidateIndex in candidates)
                    {
                        int a = Math.Min(candidateIndex, index);
                        int b = Math.Max(candidateIndex, index);
                        if (!checkedPairs.Add((a, b)))
                        {
                            continue;
                        }

                        if (AreSharedCorridorCandidates(fragments[a], fragments[b], distanceThreshold))
                        {
                            Union(a, b);
                        }
                    }

                    candidates.Add(index);
                }
            }
        }

        Dictionary<int, List<CorridorSegmentFragment>> groups = [];
        for (int index = 0; index < fragments.Count; index++)
        {
            if (!fragments[index].IsEligible)
            {
                continue;
            }

            int root = Find(index);
            if (!groups.TryGetValue(root, out List<CorridorSegmentFragment>? group))
            {
                group = [];
                groups[root] = group;
            }

            group.Add(fragments[index]);
        }

        return groups.Values
            .Where(group => group.Select(fragment => fragment.FamilyKey).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToList();
    }

    private static List<CorridorMatch> FindSharedCorridorMatches(
        List<CorridorSegmentFragment> fragments,
        SvgRenderOptions options)
    {
        List<CorridorMatch> matches = [];
        double distanceThreshold = options.LineWidth * 1.5 + 6;
        double cellSize = Math.Max(48, distanceThreshold * 3);
        Dictionary<(int X, int Y), List<int>> grid = [];
        HashSet<(int A, int B)> checkedPairs = [];

        for (int index = 0; index < fragments.Count; index++)
        {
            CorridorSegmentFragment fragment = fragments[index];
            if (!fragment.IsEligible)
            {
                continue;
            }

            (int minX, int maxX, int minY, int maxY) = GetGridBounds(fragment, cellSize, distanceThreshold);
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    (int X, int Y) cell = (x, y);
                    if (!grid.TryGetValue(cell, out List<int>? candidates))
                    {
                        candidates = [];
                        grid[cell] = candidates;
                    }

                    foreach (int candidateIndex in candidates)
                    {
                        int a = Math.Min(candidateIndex, index);
                        int b = Math.Max(candidateIndex, index);
                        if (!checkedPairs.Add((a, b)))
                        {
                            continue;
                        }

                        if (string.Equals(fragments[a].FamilyKey, fragments[b].FamilyKey, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (AreSharedCorridorCandidates(fragments[a], fragments[b], distanceThreshold))
                        {
                            matches.Add(new CorridorMatch(a, b));
                        }
                    }

                    candidates.Add(index);
                }
            }
        }

        return matches;
    }

    private static Dictionary<int, HashSet<string>> GetSharedFamiliesByFragment(
        List<CorridorSegmentFragment> fragments,
        List<CorridorMatch> matches)
    {
        Dictionary<int, HashSet<string>> familiesByFragment = [];
        foreach (CorridorMatch match in matches)
        {
            CorridorSegmentFragment left = fragments[match.LeftFragmentIndex];
            CorridorSegmentFragment right = fragments[match.RightFragmentIndex];
            AddSharedFamily(left.Index, left.FamilyKey);
            AddSharedFamily(left.Index, right.FamilyKey);
            AddSharedFamily(right.Index, right.FamilyKey);
            AddSharedFamily(right.Index, left.FamilyKey);
        }

        return familiesByFragment;

        void AddSharedFamily(int fragmentIndex, string familyKey)
        {
            if (!familiesByFragment.TryGetValue(fragmentIndex, out HashSet<string>? families))
            {
                families = new HashSet<string>(StringComparer.Ordinal);
                familiesByFragment[fragmentIndex] = families;
            }

            families.Add(familyKey);
        }
    }

    private static List<SharedCorridorRun> BuildSharedCorridorRuns(
        List<CorridorSegmentFragment> fragments,
        Dictionary<int, HashSet<string>> sharedFamiliesByFragment,
        SvgRenderOptions options)
    {
        Dictionary<string, CorridorFamilyInfo> familyInfoByKey = fragments
            .GroupBy(fragment => fragment.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new CorridorFamilyInfo(
                    group.Key,
                    group.First().Family,
                    group.First().FamilyDisplayName,
                    group.First().FamilyColor,
                    group.Min(fragment => fragment.RouteIndex)),
                StringComparer.Ordinal);

        Dictionary<SharedCorridorPairKey, List<CorridorSegmentFragment>> outerFragmentsByPair = [];
        foreach ((int fragmentIndex, HashSet<string> familySet) in sharedFamiliesByFragment)
        {
            if (familySet.Count != 2)
            {
                continue;
            }

            List<CorridorFamilyInfo> orderedFamilies = familySet
                .Where(familyInfoByKey.ContainsKey)
                .Select(familyKey => familyInfoByKey[familyKey])
                .OrderBy(info => info.DisplayName, StringComparer.CurrentCulture)
                .ThenBy(info => info.RouteIndex)
                .ThenBy(info => info.FamilyKey, StringComparer.Ordinal)
                .ToList();

            if (orderedFamilies.Count != 2)
            {
                continue;
            }

            CorridorFamilyInfo outer = orderedFamilies[0];
            CorridorFamilyInfo inner = orderedFamilies[1];
            CorridorSegmentFragment fragment = fragments[fragmentIndex];
            if (!string.Equals(fragment.FamilyKey, outer.FamilyKey, StringComparison.Ordinal))
            {
                continue;
            }

            SharedCorridorPairKey pairKey = new(outer.FamilyKey, inner.FamilyKey);
            if (!outerFragmentsByPair.TryGetValue(pairKey, out List<CorridorSegmentFragment>? pairFragments))
            {
                pairFragments = [];
                outerFragmentsByPair[pairKey] = pairFragments;
            }

            pairFragments.Add(fragment);
        }

        List<SharedCorridorRun> runs = [];
        int runNumber = 1;
        foreach ((SharedCorridorPairKey pairKey, List<CorridorSegmentFragment> pairFragments) in outerFragmentsByPair
            .OrderBy(pair => familyInfoByKey[pair.Key.OuterFamilyKey].RouteIndex)
            .ThenBy(pair => familyInfoByKey[pair.Key.InnerFamilyKey].RouteIndex))
        {
            CorridorFamilyInfo outer = familyInfoByKey[pairKey.OuterFamilyKey];
            CorridorFamilyInfo inner = familyInfoByKey[pairKey.InnerFamilyKey];
            foreach (List<CorridorSegmentFragment> runFragments in SplitContiguousSharedRuns(pairFragments))
            {
                List<SvgPoint> points = BuildRunPoints(runFragments);
                if (points.Count < 2)
                {
                    continue;
                }

                runs.Add(new SharedCorridorRun(
                    $"shared-corridor-run-{runNumber++}",
                    runFragments.Min(fragment => fragment.RouteIndex),
                    outer.Family,
                    inner.Family,
                    outer.Color,
                    inner.Color,
                    points,
                    runFragments.Count,
                    runFragments.Select(fragment => fragment.Index).ToHashSet()));
            }
        }

        return MergeAdjacentSharedCorridorRuns(runs, options);
    }

    private static List<SharedCorridorRun> MergeAdjacentSharedCorridorRuns(List<SharedCorridorRun> runs, SvgRenderOptions options)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        double endpointTolerance = Math.Max(18, GetNormalRouteStrokeWidth(options) * 2.6);
        List<SharedCorridorRun> mergedRuns = [];
        foreach (IGrouping<(string FamilyA, string FamilyB), SharedCorridorRun> group in runs
            .GroupBy(run => (run.FamilyA.FamilyKey, run.FamilyB.FamilyKey))
            .OrderBy(group => group.Min(run => run.FragmentIndices.DefaultIfEmpty(0).Min())))
        {
            SharedCorridorRun? current = null;
            foreach (SharedCorridorRun run in group
                .OrderBy(run => run.FragmentIndices.DefaultIfEmpty(0).Min()))
            {
                if (current is null)
                {
                    current = run;
                    continue;
                }

                if (TryMergeSharedCorridorRuns(current, run, endpointTolerance, out SharedCorridorRun merged))
                {
                    current = merged;
                }
                else
                {
                    mergedRuns.Add(current);
                    current = run;
                }
            }

            if (current is not null)
            {
                mergedRuns.Add(current);
            }
        }

        return mergedRuns
            .OrderBy(run => run.RouteIndex)
            .ThenBy(run => run.FragmentIndices.DefaultIfEmpty(0).Min())
            .ToList();
    }

    private static bool TryMergeSharedCorridorRuns(
        SharedCorridorRun current,
        SharedCorridorRun next,
        double endpointTolerance,
        out SharedCorridorRun merged)
    {
        merged = current;
        if (current.Points.Count < 2 || next.Points.Count < 2)
        {
            return false;
        }

        List<(double Distance, int Mode)> candidates =
        [
            (Distance(current.Points[^1], next.Points[0]), 0),
            (Distance(current.Points[^1], next.Points[^1]), 1),
            (Distance(current.Points[0], next.Points[^1]), 2),
            (Distance(current.Points[0], next.Points[0]), 3)
        ];
        (double distance, int mode) = candidates.OrderBy(candidate => candidate.Distance).First();
        if (distance > endpointTolerance)
        {
            return false;
        }

        merged = MergeSharedCorridorRun(current, next, mode);
        return true;
    }

    private static SharedCorridorRun MergeSharedCorridorRun(SharedCorridorRun current, SharedCorridorRun next, int mode)
    {
        List<SvgPoint> points = mode switch
        {
            1 => AppendPoints(current.Points, next.Points.AsEnumerable().Reverse()),
            2 => AppendPoints(next.Points, current.Points),
            3 => AppendPoints(next.Points.AsEnumerable().Reverse(), current.Points),
            _ => AppendPoints(current.Points, next.Points)
        };

        HashSet<int> fragmentIndices = current.FragmentIndices.ToHashSet();
        foreach (int fragmentIndex in next.FragmentIndices)
        {
            fragmentIndices.Add(fragmentIndex);
        }

        return current with
        {
            Points = points,
            FragmentCount = current.FragmentCount + next.FragmentCount,
            FragmentIndices = fragmentIndices
        };

        static List<SvgPoint> AppendPoints(IEnumerable<SvgPoint> first, IEnumerable<SvgPoint> second)
        {
            List<SvgPoint> points = [];
            foreach (SvgPoint point in first)
            {
                AddPointIfNotDuplicate(points, point);
            }

            foreach (SvgPoint point in second)
            {
                AddPointIfNotDuplicate(points, point);
            }

            return points;
        }
    }

    private static IEnumerable<List<CorridorSegmentFragment>> SplitContiguousSharedRuns(List<CorridorSegmentFragment> fragments)
    {
        foreach (IGrouping<(int RouteIndex, int PolylineIndex), CorridorSegmentFragment> group in fragments
            .GroupBy(fragment => (fragment.RouteIndex, fragment.PolylineIndex))
            .OrderBy(group => group.Key.RouteIndex)
            .ThenBy(group => group.Key.PolylineIndex))
        {
            List<CorridorSegmentFragment> ordered = group
                .OrderBy(fragment => fragment.SegmentIndex)
                .ToList();
            List<CorridorSegmentFragment> current = [];
            foreach (CorridorSegmentFragment fragment in ordered)
            {
                if (current.Count == 0)
                {
                    current.Add(fragment);
                    continue;
                }

                CorridorSegmentFragment previous = current[^1];
                bool contiguous = fragment.SegmentIndex == previous.SegmentIndex + 1
                    && Distance(previous.End, fragment.Start) <= 0.001;
                if (!contiguous)
                {
                    yield return current;
                    current = [];
                }

                current.Add(fragment);
            }

            if (current.Count > 0)
            {
                yield return current;
            }
        }
    }

    private static List<SvgPoint> BuildRunPoints(List<CorridorSegmentFragment> runFragments)
    {
        List<SvgPoint> points = [];
        foreach (CorridorSegmentFragment fragment in runFragments
            .OrderBy(fragment => fragment.RouteIndex)
            .ThenBy(fragment => fragment.PolylineIndex)
            .ThenBy(fragment => fragment.SegmentIndex))
        {
            if (points.Count == 0)
            {
                points.Add(fragment.Start);
            }

            AddPointIfNotDuplicate(points, fragment.End);
        }

        return points;
    }

    private static List<RenderRoute> SegmentizePathPointRoutes(
        List<RenderRoute> renderRoutes,
        List<CorridorSegmentFragment> fragments,
        Dictionary<int, CorridorAssignment> assignments)
    {
        Dictionary<int, List<CorridorSegmentFragment>> fragmentsByRoute = fragments
            .GroupBy(fragment => fragment.RouteIndex)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .ToList());

        List<RenderRoute> adjustedRoutes = [];
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints" || !fragmentsByRoute.TryGetValue(routeIndex, out List<CorridorSegmentFragment>? routeFragments))
            {
                adjustedRoutes.Add(renderRoute);
                continue;
            }

            List<RoutePolyline> polylines = [];
            foreach (CorridorSegmentFragment fragment in routeFragments)
            {
                if (assignments.TryGetValue(fragment.Index, out CorridorAssignment assignment))
                {
                    polylines.Add(CreateOffsetCorridorPolyline(fragment, assignment));
                }
                else
                {
                    polylines.Add(new RoutePolyline([fragment.Start, fragment.End]));
                }
            }

            adjustedRoutes.Add(renderRoute with
            {
                RoutePointSet = renderRoute.RoutePointSet with { Polylines = polylines }
            });
        }

        return adjustedRoutes;
    }

    private static List<RenderRoute> BuildSharedCorridorRenderRoutes(
        List<RenderRoute> renderRoutes,
        List<CorridorSegmentFragment> fragments,
        List<SharedCorridorRun> runs,
        HashSet<int> twoFamilySharedFragments,
        HashSet<int> tooManyFamilyFragments)
    {
        Dictionary<int, List<CorridorSegmentFragment>> fragmentsByRoute = fragments
            .GroupBy(fragment => fragment.RouteIndex)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .ToList());
        Dictionary<int, SharedCorridorRun> runByStartFragment = runs
            .Where(run => run.FragmentIndices.Count > 0)
            .ToDictionary(run => run.FragmentIndices.Min(), run => run);
        HashSet<int> hiddenSharedFragmentIndices = twoFamilySharedFragments;

        List<RenderRoute> adjustedRoutes = [];
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints" || !fragmentsByRoute.TryGetValue(routeIndex, out List<CorridorSegmentFragment>? routeFragments))
            {
                adjustedRoutes.Add(renderRoute);
                continue;
            }

            List<RoutePolyline> polylines = [];
            List<SvgPoint> normalPoints = [];
            foreach (CorridorSegmentFragment fragment in routeFragments)
            {
                if (runByStartFragment.TryGetValue(fragment.Index, out SharedCorridorRun? run))
                {
                    FlushNormalPolyline();
                    polylines.Add(new RoutePolyline(
                        run.Points,
                        SharedCorridorStroke: new SharedCorridorStroke(
                            run.RunId,
                            run.FamilyA.FamilyKey,
                            run.FamilyB.FamilyKey,
                            run.OuterColor,
                            run.InnerColor,
                            run.Points.Count)));
                }
                else if (hiddenSharedFragmentIndices.Contains(fragment.Index))
                {
                    FlushNormalPolyline();
                }
                else if (tooManyFamilyFragments.Contains(fragment.Index))
                {
                    FlushNormalPolyline();
                    polylines.Add(new RoutePolyline([fragment.Start, fragment.End], SharedCorridorSkipped: "too-many-families"));
                }
                else
                {
                    AddNormalSegment(fragment);
                }
            }

            FlushNormalPolyline();
            adjustedRoutes.Add(renderRoute with
            {
                RoutePointSet = renderRoute.RoutePointSet with { Polylines = polylines }
            });

            void AddNormalSegment(CorridorSegmentFragment fragment)
            {
                if (normalPoints.Count == 0)
                {
                    normalPoints.Add(fragment.Start);
                    normalPoints.Add(fragment.End);
                    return;
                }

                if (Distance(normalPoints[^1], fragment.Start) <= 0.001)
                {
                    AddPointIfNotDuplicate(normalPoints, fragment.End);
                    return;
                }

                FlushNormalPolyline();
                normalPoints.Add(fragment.Start);
                normalPoints.Add(fragment.End);
            }

            void FlushNormalPolyline()
            {
                if (normalPoints.Count >= 2)
                {
                    polylines.Add(new RoutePolyline(normalPoints.ToList()));
                }

                normalPoints.Clear();
            }
        }

        return adjustedRoutes;
    }

    private static RoutePolyline CreateOffsetCorridorPolyline(CorridorSegmentFragment fragment, CorridorAssignment assignment)
    {
        SvgPoint normal = fragment.Normal;
        if (Dot(fragment.Direction, assignment.BaseDirection) < 0)
        {
            normal = new SvgPoint(-normal.X, -normal.Y);
        }

        double startWeight = GetCorridorOffsetWeight(fragment.Start, fragment.StationPoints);
        double endWeight = GetCorridorOffsetWeight(fragment.End, fragment.StationPoints);
        SvgPoint offsetStart = OffsetPoint(fragment.Start, normal, assignment.OffsetPx * startWeight);
        SvgPoint offsetEnd = OffsetPoint(fragment.End, normal, assignment.OffsetPx * endWeight);

        if (!IsFinite(offsetStart) || !IsFinite(offsetEnd) || Distance(offsetStart, offsetEnd) <= 0.001)
        {
            return new RoutePolyline([fragment.Start, fragment.End]);
        }

        return new RoutePolyline(
            [offsetStart, offsetEnd],
            assignment.CorridorId,
            assignment.MemberCount,
            assignment.OffsetIndex,
            assignment.OffsetPx);
    }

    private static bool AreSharedCorridorCandidates(
        CorridorSegmentFragment left,
        CorridorSegmentFragment right,
        double distanceThreshold)
    {
        if (!left.IsEligible || !right.IsEligible || string.Equals(left.LineId, right.LineId, StringComparison.Ordinal))
        {
            return false;
        }

        double directionSimilarity = Math.Abs(Dot(left.Direction, right.Direction));
        if (directionSimilarity < Math.Cos(15 * Math.PI / 180))
        {
            return false;
        }

        double distance = SegmentDistance(left.Start, left.End, right.Start, right.End);
        if (distance > distanceThreshold)
        {
            return false;
        }

        double overlapLength = ProjectedOverlapLength(left, right);
        double overlapRatio = overlapLength / Math.Max(1, Math.Min(left.Length, right.Length));
        return overlapRatio > 0.35 || overlapLength > 40;
    }

    private static (int MinX, int MaxX, int MinY, int MaxY) GetGridBounds(
        CorridorSegmentFragment fragment,
        double cellSize,
        double expansion)
    {
        double minX = Math.Min(fragment.Start.X, fragment.End.X) - expansion;
        double maxX = Math.Max(fragment.Start.X, fragment.End.X) + expansion;
        double minY = Math.Min(fragment.Start.Y, fragment.End.Y) - expansion;
        double maxY = Math.Max(fragment.Start.Y, fragment.End.Y) + expansion;
        return (
            (int)Math.Floor(minX / cellSize),
            (int)Math.Floor(maxX / cellSize),
            (int)Math.Floor(minY / cellSize),
            (int)Math.Floor(maxY / cellSize));
    }

    private static double ProjectedOverlapLength(CorridorSegmentFragment left, CorridorSegmentFragment right)
    {
        double leftStart = Dot(left.Start, left.Direction);
        double leftEnd = Dot(left.End, left.Direction);
        double rightStart = Dot(right.Start, left.Direction);
        double rightEnd = Dot(right.End, left.Direction);
        double minLeft = Math.Min(leftStart, leftEnd);
        double maxLeft = Math.Max(leftStart, leftEnd);
        double minRight = Math.Min(rightStart, rightEnd);
        double maxRight = Math.Max(rightStart, rightEnd);
        return Math.Max(0, Math.Min(maxLeft, maxRight) - Math.Max(minLeft, minRight));
    }

    private static double SegmentDistance(SvgPoint aStart, SvgPoint aEnd, SvgPoint bStart, SvgPoint bEnd)
    {
        return Math.Min(
            Math.Min(PointToSegmentDistance(aStart, bStart, bEnd), PointToSegmentDistance(aEnd, bStart, bEnd)),
            Math.Min(PointToSegmentDistance(bStart, aStart, aEnd), PointToSegmentDistance(bEnd, aStart, aEnd)));
    }

    private static double PointToSegmentDistance(SvgPoint point, SvgPoint segmentStart, SvgPoint segmentEnd)
    {
        double dx = segmentEnd.X - segmentStart.X;
        double dy = segmentEnd.Y - segmentStart.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.000001)
        {
            return Distance(point, segmentStart);
        }

        double t = ((point.X - segmentStart.X) * dx + (point.Y - segmentStart.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        SvgPoint projected = new(segmentStart.X + t * dx, segmentStart.Y + t * dy);
        return Distance(point, projected);
    }

    private static double GetCorridorOffsetWeight(SvgPoint point, List<SvgPoint> stationPoints)
    {
        if (stationPoints.Count == 0)
        {
            return 1;
        }

        double distance = stationPoints.Min(stationPoint => Distance(point, stationPoint));
        const double taperRadius = 44;
        if (distance >= taperRadius)
        {
            return 1;
        }

        double t = Math.Clamp(distance / taperRadius, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static SvgPoint OffsetPoint(SvgPoint point, SvgPoint normal, double offset)
    {
        return new SvgPoint(point.X + normal.X * offset, point.Y + normal.Y * offset);
    }

    private static SvgPoint Normalize(SvgPoint point)
    {
        double length = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        if (length <= 0.000001)
        {
            return new SvgPoint(0, 0);
        }

        return new SvgPoint(point.X / length, point.Y / length);
    }

    private static double Dot(SvgPoint a, SvgPoint b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    private static SvgPoint Midpoint(SvgPoint a, SvgPoint b)
    {
        return new SvgPoint((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }

    private static double DistancePointToSegment(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        double t = Math.Clamp(ProjectFraction(point, start, end), 0, 1);
        SvgPoint projected = new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        return Distance(point, projected);
    }

    private static bool IsFinite(SvgPoint point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private static RoutePointSet CreateRoutePoints(
        DisplayLineFamily family,
        MetroLine line,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options,
        List<string> warnings)
    {
        if (options.LayoutMode == SvgLayoutMode.Geographic
            && options.UsePathPoints
            && geometry.Projector is not null
            && (line.PathPoints?.Count ?? 0) >= 2)
        {
            List<MetroPathPoint> renderablePathPoints = (line.PathPoints ?? []).ToList();
            List<SvgPoint> projectedPathPoints = [];
            foreach (MetroPathPoint pathPoint in renderablePathPoints)
            {
                AddPointIfNotDuplicate(projectedPathPoints, geometry.Projector.Project(pathPoint.X, pathPoint.Z));
            }

            if (projectedPathPoints.Count >= 2)
            {
                List<SvgPoint> stationPoints = GetLineStationPoints(line, stationsById, geometry);
                PathPointRenderDiagnostics diagnostics = CleanProjectedPathPoints(projectedPathPoints, stationPoints, options);
                if (diagnostics.Points.Count >= 2)
                {
                    return new RoutePointSet(
                        SplitAtSuspiciousJumps(diagnostics.Points, diagnostics.SuspiciousJumpThreshold),
                        "pathPoints",
                        line.PathPoints!.Count,
                        diagnostics.Points.Count,
                        diagnostics.ReductionRatio,
                        diagnostics.MaxSegmentLength,
                        diagnostics.SuspiciousJumpCount,
                        diagnostics.EffectiveSimplificationTolerance);
                }

                if (projectedPathPoints.Count >= 2)
                {
                    PathPointMetrics fallbackMetrics = MeasurePathPoints(projectedPathPoints, options);
                    return new RoutePointSet(
                        SplitAtSuspiciousJumps(projectedPathPoints, fallbackMetrics.SuspiciousJumpThreshold),
                        "pathPoints",
                        line.PathPoints!.Count,
                        projectedPathPoints.Count,
                        0,
                        fallbackMetrics.MaxSegmentLength,
                        fallbackMetrics.SuspiciousJumpCount,
                        0);
                }
            }

            warnings.Add($"Line '{line.Id}' had pathPoints, but fewer than two distinct projected path points were usable.");
        }

        List<string> routeStopIds = (line.Stops ?? []).ToList();
        if (IsSchematicV2FamilyLayout(options.LayoutMode)
            && geometry.SchematicV2RouteGuideByFamily is not null
            && geometry.SchematicV2RouteGuideByFamily.TryGetValue(family.FamilyKey, out List<string>? routeGuide)
            && routeGuide.Count >= 2)
        {
            routeStopIds = routeGuide.ToList();
        }

        if (options.LayoutMode == SvgLayoutMode.SchematicMap)
        {
            routeStopIds = NormalizeSchematicMapRenderRouteChain(routeStopIds);
        }

        List<SvgPoint> stopPoints = [];
        foreach (string stopId in routeStopIds)
        {
            if (stationsById.ContainsKey(stopId) && geometry.StationPoints.TryGetValue(stopId, out SvgPoint point))
            {
                stopPoints.Add(point);
            }
        }

        return new RoutePointSet(
            [CreateSchematicMapRoutePolyline(stopPoints, options)],
            "stops",
            line.PathPoints?.Count ?? 0,
            0,
            0,
            0,
            0,
            0);
    }

    private static RoutePolyline CreateSchematicMapRoutePolyline(List<SvgPoint> stopPoints, SvgRenderOptions options)
    {
        if (options.LayoutMode != SvgLayoutMode.SchematicMap
            || !options.EnableSchematicMapSyntheticBends
            || stopPoints.Count < 2)
        {
            return new RoutePolyline(stopPoints);
        }

        List<SvgPoint> routePoints = [];
        int syntheticBendCount = 0;
        for (int i = 1; i < stopPoints.Count; i++)
        {
            SvgPoint start = stopPoints[i - 1];
            SvgPoint end = stopPoints[i];
            if (routePoints.Count == 0)
            {
                AddPointIfNotDuplicate(routePoints, start);
            }

            SvgPoint? previous = i >= 2 ? stopPoints[i - 2] : null;
            SvgPoint? next = i + 1 < stopPoints.Count ? stopPoints[i + 1] : null;
            if (TryCreateSchematicMapSyntheticBend(start, end, previous, next, options, out SvgPoint bend))
            {
                AddPointIfNotDuplicate(routePoints, bend);
                syntheticBendCount++;
            }

            AddPointIfNotDuplicate(routePoints, end);
        }

        return syntheticBendCount > 0
            ? new RoutePolyline(routePoints, SyntheticBendCount: syntheticBendCount)
            : new RoutePolyline(stopPoints);
    }

    private static bool TryCreateSchematicMapSyntheticBend(
        SvgPoint start,
        SvgPoint end,
        SvgPoint? previous,
        SvgPoint? next,
        SvgRenderOptions options,
        out SvgPoint bend)
    {
        bend = default;
        double directLength = Distance(start, end);
        bool hasRouteContext = previous.HasValue || next.HasValue;
        double minimumDirectLength = Math.Max(
            Math.Min(options.SchematicMapSyntheticBendMinimumLength, options.LineWidth * 7.0),
            options.LineWidth * 6.5);
        if (directLength < minimumDirectLength)
        {
            return false;
        }

        double octilinearThresholdRadians = Math.PI / 180.0 * 5.0;
        if (TryGetNearbyOctilinearDirection(start, end, octilinearThresholdRadians, out _))
        {
            return false;
        }

        if (hasRouteContext)
        {
            double relaxedOctilinearThresholdRadians = Math.PI / 180.0 * 12.0;
            if (TryGetNearbyOctilinearDirection(start, end, relaxedOctilinearThresholdRadians, out _))
            {
                return false;
            }

            double contextualMinimumLength = Math.Max(options.GridSize * 3.6, options.LineWidth * 9.0);
            if (directLength < contextualMinimumLength)
            {
                return false;
            }
        }

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        if (Math.Abs(dx) <= 0.001 || Math.Abs(dy) <= 0.001)
        {
            return false;
        }

        double signX = Math.Sign(dx);
        double signY = Math.Sign(dy);
        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);
        double minimumLegLength = Math.Max(options.LineWidth * 2.0, 28);
        List<SvgPoint> candidates =
        [
            new(end.X, start.Y),
            new(start.X, end.Y)
        ];

        if (absDx > absDy + 0.001)
        {
            candidates.Add(new SvgPoint(start.X + signX * absDy, end.Y));
            candidates.Add(new SvgPoint(end.X - signX * absDy, start.Y));
        }
        else if (absDy > absDx + 0.001)
        {
            candidates.Add(new SvgPoint(end.X, start.Y + signY * absDx));
            candidates.Add(new SvgPoint(start.X, end.Y - signY * absDx));
        }

        SvgPoint originalDirection = Normalize(new SvgPoint(dx, dy));
        double bestScore = double.PositiveInfinity;
        SvgPoint best = default;
        foreach (SvgPoint candidate in candidates.Distinct())
        {
            double firstLength = Distance(start, candidate);
            double secondLength = Distance(candidate, end);
            if (firstLength < minimumLegLength || secondLength < minimumLegLength)
            {
                continue;
            }

            double routeLength = firstLength + secondLength;
            double maxRouteLengthRatio = hasRouteContext ? 1.32 : 1.55;
            if (routeLength > directLength * maxRouteLengthRatio)
            {
                continue;
            }

            if (!TryGetNearbyOctilinearDirection(start, candidate, octilinearThresholdRadians, out _)
                || !TryGetNearbyOctilinearDirection(candidate, end, octilinearThresholdRadians, out _))
            {
                continue;
            }

            SvgPoint firstDirection = Normalize(new SvgPoint(candidate.X - start.X, candidate.Y - start.Y));
            SvgPoint secondDirection = Normalize(new SvgPoint(end.X - candidate.X, end.Y - candidate.Y));
            if (Dot(firstDirection, originalDirection) <= 0.05 || Dot(secondDirection, originalDirection) <= 0.05)
            {
                continue;
            }

            if (!IsSchematicMapSyntheticBendContextAcceptable(previous, start, firstDirection)
                || !IsSchematicMapSyntheticBendContextAcceptable(next, end, new SvgPoint(-secondDirection.X, -secondDirection.Y)))
            {
                continue;
            }

            double balancePenalty = Math.Abs(firstLength - secondLength) / routeLength;
            double score = (routeLength / directLength) + balancePenalty * 0.12;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (double.IsPositiveInfinity(bestScore))
        {
            return false;
        }

        bend = best;
        return true;
    }

    private static bool IsSchematicMapSyntheticBendContextAcceptable(
        SvgPoint? neighbor,
        SvgPoint anchor,
        SvgPoint proposedDirectionAwayFromAnchor)
    {
        if (!neighbor.HasValue)
        {
            return true;
        }

        SvgPoint directionFromNeighbor = Normalize(new SvgPoint(anchor.X - neighbor.Value.X, anchor.Y - neighbor.Value.Y));
        if (Math.Abs(directionFromNeighbor.X) <= 0.001 && Math.Abs(directionFromNeighbor.Y) <= 0.001)
        {
            return true;
        }

        return Dot(directionFromNeighbor, proposedDirectionAwayFromAnchor) >= -0.08;
    }

    private static List<SvgPoint> GetLineStationPoints(
        MetroLine line,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry)
    {
        List<SvgPoint> stationPoints = [];
        foreach (string stopId in line.Stops ?? [])
        {
            if (stationsById.ContainsKey(stopId) && geometry.StationPoints.TryGetValue(stopId, out SvgPoint point))
            {
                AddPointIfNotDuplicate(stationPoints, point);
            }
        }

        return stationPoints;
    }

    private static List<MetroPathPoint> GetRenderablePathPoints(MetroLine line, SvgRenderOptions options)
    {
        List<MetroPathPoint> original = (line.PathPoints ?? []).ToList();
        if (original.Count < 2)
        {
            return original;
        }

        if (!options.PathPointSimplificationEnabled)
        {
            return original;
        }

        List<MetroPathPoint> cleaned = RemoveConsecutiveDuplicatePathPoints(original, PathPointDuplicateEpsilon);
        cleaned = RemoveShortPathSegments(cleaned, Math.Max(0, options.MinPathSegmentLength));
        cleaned = SimplifyNearlyCollinearPathPoints(cleaned, Math.Max(0, options.PathPointSimplificationTolerance));

        return cleaned.Count >= 2 ? cleaned : original;
    }

    private static PathPointRenderDiagnostics CleanProjectedPathPoints(
        List<SvgPoint> originalPoints,
        List<SvgPoint> stationPoints,
        SvgRenderOptions options)
    {
        if (originalPoints.Count < 2)
        {
            return new PathPointRenderDiagnostics(originalPoints.ToList(), 0, 0, 0, 0, 0);
        }

        List<SvgPoint> cleaned = RemoveConsecutiveDuplicateSvgPoints(originalPoints, 0.001);
        PathPointMetrics metrics = MeasurePathPoints(cleaned, options);
        double minSegmentLength = ResolveMinPathSegmentLength(metrics, options);
        double effectiveTolerance = ResolvePathSimplificationTolerance(metrics, options);

        if (options.PathPointSimplificationEnabled)
        {
            cleaned = RemoveShortSvgSegments(cleaned, minSegmentLength, stationPoints, options);
            metrics = MeasurePathPoints(cleaned, options);
            effectiveTolerance = ResolvePathSimplificationTolerance(metrics, options);
            cleaned = SimplifyProjectedPathPoints(cleaned, stationPoints, metrics.SuspiciousJumpStartIndices, effectiveTolerance, options);
            metrics = MeasurePathPoints(cleaned, options, detectSparseJumps: metrics.SuspiciousJumpCount > 0);
        }

        double reductionRatio = originalPoints.Count == 0
            ? 0
            : Math.Max(0, (originalPoints.Count - cleaned.Count) / (double)originalPoints.Count);
        return new PathPointRenderDiagnostics(
            cleaned.Count >= 2 ? cleaned : originalPoints.ToList(),
            reductionRatio,
            metrics.MaxSegmentLength,
            metrics.SuspiciousJumpCount,
            metrics.SuspiciousJumpThreshold,
            effectiveTolerance);
    }

    private static List<SvgPoint> RemoveConsecutiveDuplicateSvgPoints(List<SvgPoint> points, double epsilon)
    {
        if (points.Count <= 1)
        {
            return points.ToList();
        }

        double epsilonSquared = epsilon * epsilon;
        List<SvgPoint> cleaned = [points[0]];
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (DistanceSquared(points[i], cleaned[^1]) > epsilonSquared)
            {
                cleaned.Add(points[i]);
            }
        }

        SvgPoint last = points[^1];
        if (DistanceSquared(last, cleaned[^1]) <= epsilonSquared)
        {
            if (cleaned.Count == 1)
            {
                cleaned.Add(last);
            }
            else
            {
                cleaned[^1] = last;
            }
        }
        else
        {
            cleaned.Add(last);
        }

        return cleaned;
    }

    private static List<SvgPoint> RemoveShortSvgSegments(
        List<SvgPoint> points,
        double minSegmentLength,
        List<SvgPoint> stationPoints,
        SvgRenderOptions options)
    {
        if (points.Count <= 2 || minSegmentLength <= 0)
        {
            return points.ToList();
        }

        List<SvgPoint> cleaned = [points[0]];
        for (int i = 1; i < points.Count - 1; i++)
        {
            SvgPoint point = points[i];
            if (Distance(point, cleaned[^1]) >= minSegmentLength)
            {
                cleaned.Add(point);
            }
        }

        SvgPoint last = points[^1];
        if (Distance(last, cleaned[^1]) < minSegmentLength && cleaned.Count > 1)
        {
            cleaned[^1] = last;
        }
        else
        {
            AddPointIfNotDuplicate(cleaned, last);
        }

        return cleaned;
    }

    private static List<SvgPoint> SimplifyProjectedPathPoints(
        List<SvgPoint> points,
        List<SvgPoint> stationPoints,
        List<int> suspiciousJumpStartIndices,
        double tolerance,
        SvgRenderOptions options)
    {
        if (points.Count <= 2 || tolerance <= 0)
        {
            return points.ToList();
        }

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        double stationProtectionRadius = GetStationProtectionRadius(options);
        foreach (int index in GetStationProtectedPathPointIndices(points, stationPoints, stationProtectionRadius))
        {
            if (index > 0 && index < points.Count - 1)
            {
                keep[index] = true;
            }
        }

        foreach (int jumpStartIndex in suspiciousJumpStartIndices)
        {
            if (jumpStartIndex >= 0 && jumpStartIndex < points.Count)
            {
                keep[jumpStartIndex] = true;
            }

            if (jumpStartIndex + 1 >= 0 && jumpStartIndex + 1 < points.Count)
            {
                keep[jumpStartIndex + 1] = true;
            }
        }

        List<int> anchors = [];
        for (int i = 0; i < keep.Length; i++)
        {
            if (keep[i])
            {
                anchors.Add(i);
            }
        }

        for (int i = 1; i < anchors.Count; i++)
        {
            MarkProjectedRamerDouglasPeucker(points, keep, anchors[i - 1], anchors[i], tolerance);
        }

        List<SvgPoint> simplified = [];
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified;
    }

    private static HashSet<int> GetStationProtectedPathPointIndices(
        List<SvgPoint> points,
        List<SvgPoint> stationPoints,
        double radius)
    {
        HashSet<int> protectedIndices = [];
        if (points.Count == 0 || stationPoints.Count == 0)
        {
            return protectedIndices;
        }

        double radiusSquared = radius * radius;
        foreach (SvgPoint stationPoint in stationPoints)
        {
            int bestIndex = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                double distance = DistanceSquared(points[i], stationPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestDistance <= radiusSquared)
            {
                protectedIndices.Add(bestIndex);
            }
        }

        return protectedIndices;
    }

    private static void MarkProjectedRamerDouglasPeucker(List<SvgPoint> points, bool[] keep, int start, int end, double tolerance)
    {
        if (end <= start + 1)
        {
            return;
        }

        double maxDistance = -1;
        int maxIndex = -1;
        for (int i = start + 1; i < end; i++)
        {
            double distance = PerpendicularDistance(points[i], points[start], points[end]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                maxIndex = i;
            }
        }

        if (maxDistance > tolerance && maxIndex > start)
        {
            keep[maxIndex] = true;
            MarkProjectedRamerDouglasPeucker(points, keep, start, maxIndex, tolerance);
            MarkProjectedRamerDouglasPeucker(points, keep, maxIndex, end, tolerance);
        }
    }

    private static PathPointMetrics MeasurePathPoints(List<SvgPoint> points, SvgRenderOptions options, bool detectSparseJumps = false)
    {
        if (points.Count < 2)
        {
            return new PathPointMetrics(0, 0, 0, GetSuspiciousJumpFloor(options), 0, []);
        }

        List<double> lengths = [];
        double totalLength = 0;
        double maxSegmentLength = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double length = Distance(points[i - 1], points[i]);
            lengths.Add(length);
            totalLength += length;
            maxSegmentLength = Math.Max(maxSegmentLength, length);
        }

        double medianSegmentLength = Median(lengths);
        bool canDetectSuspiciousJumps = points.Count >= 8 || detectSparseJumps;
        double suspiciousJumpThreshold = canDetectSuspiciousJumps
            ? ResolveSuspiciousJumpThreshold(medianSegmentLength, options)
            : double.MaxValue;
        List<int> suspiciousJumpStartIndices = [];
        if (canDetectSuspiciousJumps)
        {
            for (int i = 0; i < lengths.Count; i++)
            {
                if (lengths[i] > suspiciousJumpThreshold)
                {
                    suspiciousJumpStartIndices.Add(i);
                }
            }
        }

        return new PathPointMetrics(
            totalLength,
            medianSegmentLength,
            maxSegmentLength,
            suspiciousJumpThreshold,
            suspiciousJumpStartIndices.Count,
            suspiciousJumpStartIndices);
    }

    private static double ResolvePathSimplificationTolerance(PathPointMetrics metrics, SvgRenderOptions options)
    {
        double baseTolerance = Math.Max(0, options.PathPointSimplificationTolerance);
        if (!options.AdaptivePathPointSimplificationEnabled)
        {
            return baseTolerance;
        }

        double diagonal = Math.Sqrt(options.Width * options.Width + options.Height * options.Height);
        double canvasScale = Math.Clamp(Math.Sqrt(options.Width * options.Height) / Math.Sqrt(2200.0 * 1400.0), 0.75, 1.45);
        double longLineFactor = Math.Clamp(metrics.TotalLength / Math.Max(diagonal * 0.75, 1), 0, 4);
        double medianFactor = Math.Clamp(metrics.MedianSegmentLength / 24, 0, 1.5);
        double adaptiveTolerance = (0.8 + longLineFactor * 0.75 + medianFactor * 0.35) * canvasScale;
        return Math.Clamp(Math.Max(baseTolerance, adaptiveTolerance), 0.1, 7);
    }

    private static double ResolveMinPathSegmentLength(PathPointMetrics metrics, SvgRenderOptions options)
    {
        double baseLength = Math.Max(0, options.MinPathSegmentLength);
        if (!options.AdaptivePathPointSimplificationEnabled)
        {
            return baseLength;
        }

        double canvasScale = Math.Clamp(Math.Sqrt(options.Width * options.Height) / Math.Sqrt(2200.0 * 1400.0), 0.75, 1.45);
        double adaptiveLength = (0.7 + Math.Clamp(metrics.MedianSegmentLength / 40, 0, 1.8)) * canvasScale;
        return Math.Clamp(Math.Max(baseLength, adaptiveLength), 0.1, 5);
    }

    private static double ResolveSuspiciousJumpThreshold(double medianSegmentLength, SvgRenderOptions options)
    {
        double floor = GetSuspiciousJumpFloor(options);
        double diagonal = Math.Sqrt(options.Width * options.Width + options.Height * options.Height);
        double networkCap = Math.Max(floor, diagonal * 0.22);
        return Math.Min(networkCap, Math.Max(floor, medianSegmentLength * 14));
    }

    private static double GetSuspiciousJumpFloor(SvgRenderOptions options)
    {
        double diagonal = Math.Sqrt(options.Width * options.Width + options.Height * options.Height);
        return Math.Clamp(diagonal * 0.075, 120, 280);
    }

    private static List<RoutePolyline> SplitAtSuspiciousJumps(List<SvgPoint> points, double suspiciousJumpThreshold)
    {
        if (points.Count < 2)
        {
            return [new RoutePolyline(points.ToList())];
        }

        List<RoutePolyline> polylines = [];
        List<SvgPoint> current = [points[0]];
        for (int i = 1; i < points.Count; i++)
        {
            if (Distance(points[i - 1], points[i]) > suspiciousJumpThreshold)
            {
                if (current.Count >= 2)
                {
                    polylines.Add(new RoutePolyline(current));
                }

                current = [points[i]];
                continue;
            }

            current.Add(points[i]);
        }

        if (current.Count >= 2)
        {
            polylines.Add(new RoutePolyline(current));
        }

        return polylines.Count > 0 ? polylines : [new RoutePolyline(points.ToList())];
    }

    private static bool IsNearStationPoint(SvgPoint point, List<SvgPoint> stationPoints, double radius)
    {
        if (stationPoints.Count == 0)
        {
            return false;
        }

        double radiusSquared = radius * radius;
        return stationPoints.Any(stationPoint => DistanceSquared(point, stationPoint) <= radiusSquared);
    }

    private static double GetStationProtectionRadius(SvgRenderOptions options)
    {
        return Math.Max(18, options.StationRadius * 3 + options.LineWidth);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = values.OrderBy(value => value).ToList();
        int middle = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }

        return (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static List<MetroPathPoint> RemoveShortPathSegments(List<MetroPathPoint> points, double minSegmentLength)
    {
        if (points.Count <= 2 || minSegmentLength <= 0)
        {
            return points.ToList();
        }

        List<MetroPathPoint> cleaned = [points[0]];
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (Distance(points[i], cleaned[^1]) >= minSegmentLength)
            {
                cleaned.Add(points[i]);
            }
        }

        MetroPathPoint last = points[^1];
        if (Distance(last, cleaned[^1]) < minSegmentLength && cleaned.Count > 1)
        {
            cleaned[^1] = last;
        }
        else
        {
            cleaned.Add(last);
        }

        return cleaned;
    }

    private static List<MetroPathPoint> RemoveConsecutiveDuplicatePathPoints(List<MetroPathPoint> points, double epsilon)
    {
        if (points.Count <= 1)
        {
            return points.ToList();
        }

        List<MetroPathPoint> cleaned = [points[0]];
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (Distance(points[i], cleaned[^1]) > epsilon)
            {
                cleaned.Add(points[i]);
            }
        }

        MetroPathPoint last = points[^1];
        if (Distance(last, cleaned[^1]) <= epsilon)
        {
            if (cleaned.Count == 1)
            {
                cleaned.Add(last);
            }
            else
            {
                cleaned[^1] = last;
            }
        }
        else
        {
            cleaned.Add(last);
        }

        return cleaned;
    }

    private static List<MetroPathPoint> SimplifyNearlyCollinearPathPoints(List<MetroPathPoint> points, double tolerance)
    {
        if (points.Count <= 2 || tolerance <= 0)
        {
            return points;
        }

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        MarkRamerDouglasPeucker(points, keep, 0, points.Count - 1, tolerance);

        List<MetroPathPoint> simplified = [];
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified;
    }

    private static void MarkRamerDouglasPeucker(List<MetroPathPoint> points, bool[] keep, int start, int end, double tolerance)
    {
        if (end <= start + 1)
        {
            return;
        }

        double maxDistance = -1;
        int maxIndex = -1;
        for (int i = start + 1; i < end; i++)
        {
            double distance = PerpendicularDistance(points[i], points[start], points[end]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                maxIndex = i;
            }
        }

        if (maxDistance > tolerance && maxIndex > start)
        {
            keep[maxIndex] = true;
            MarkRamerDouglasPeucker(points, keep, start, maxIndex, tolerance);
            MarkRamerDouglasPeucker(points, keep, maxIndex, end, tolerance);
        }
    }

    private static double PerpendicularDistance(MetroPathPoint point, MetroPathPoint start, MetroPathPoint end)
    {
        double dx = end.X - start.X;
        double dz = end.Z - start.Z;
        double denominator = Math.Sqrt(dx * dx + dz * dz);
        if (denominator < 0.000001)
        {
            return Distance(point, start);
        }

        return Math.Abs(dz * point.X - dx * point.Z + end.X * start.Z - end.Z * start.X) / denominator;
    }

    private static double PerpendicularDistance(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double denominator = Math.Sqrt(dx * dx + dy * dy);
        if (denominator < 0.000001)
        {
            return Distance(point, start);
        }

        return Math.Abs(dy * point.X - dx * point.Y + end.X * start.Y - end.Y * start.X) / denominator;
    }

    private static double Distance(MetroPathPoint a, MetroPathPoint b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static double Distance(SvgPoint a, SvgPoint b)
    {
        return Math.Sqrt(DistanceSquared(a, b));
    }

    private static void AddPointIfNotDuplicate(List<SvgPoint> points, SvgPoint point)
    {
        if (points.Count > 0 && DistanceSquared(points[^1], point) < 0.001)
        {
            return;
        }

        points.Add(point);
    }

    private static string GetLayoutModeName(SvgLayoutMode layoutMode)
    {
        return layoutMode switch
        {
            SvgLayoutMode.SchematicV2 => "schematic-v2",
            SvgLayoutMode.SchematicMap => "schematic-map",
            SvgLayoutMode.SchematicLite => "schematic-lite",
            _ => "geographic"
        };
    }

    private static void AppendVirtualTransferHints(
        StringBuilder svg,
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        SvgRenderOptions options)
    {
        if (!options.EnableVirtualTransferHints)
        {
            return;
        }

        List<VirtualTransferHint> hints = BuildVirtualTransferHints(stations, stationPoints, options);
        if (hints.Count == 0)
        {
            return;
        }

        svg.AppendLine("""<g id="virtual-transfer-hints">""");
        foreach (VirtualTransferHint hint in hints)
        {
            svg.AppendLine($"""<line class="virtual-transfer-hint" data-virtual-transfer-hint="true" data-station-name="{Escape(hint.StationName)}" data-station-a="{Escape(hint.FirstStationId)}" data-station-b="{Escape(hint.SecondStationId)}" data-distance="{Format(hint.Distance)}" x1="{Format(hint.FirstPoint.X)}" y1="{Format(hint.FirstPoint.Y)}" x2="{Format(hint.SecondPoint.X)}" y2="{Format(hint.SecondPoint.Y)}" />""");
        }

        svg.AppendLine("</g>");
    }

    private static List<VirtualTransferHint> BuildVirtualTransferHints(
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        SvgRenderOptions options)
    {
        double maxConnectorDistance = Math.Max(options.GridSize * 8, SvgVisualStyle.From(options).InterchangeMarkerRadius * 8);
        return stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id)
                && !string.IsNullOrWhiteSpace(station.Name)
                && stationPoints.ContainsKey(station.Id!)
                && StationLabelClassifier.Classify(station.Name, station.Id) == StationNameKind.UserNamed)
            .GroupBy(station => NormalizeStationDisplayName(station.Name!), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => BuildVirtualTransferHintsForGroup(group.ToList(), stationPoints, maxConnectorDistance))
            .OrderBy(hint => hint.StationName, StringComparer.Ordinal)
            .ThenBy(hint => hint.Distance)
            .ToList();
    }

    private static IEnumerable<VirtualTransferHint> BuildVirtualTransferHintsForGroup(
        List<MetroStation> group,
        Dictionary<string, SvgPoint> stationPoints,
        double maxConnectorDistance)
    {
        List<MetroStation> remaining = group
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && stationPoints.ContainsKey(station.Id!))
            .OrderBy(station => station.Id, StringComparer.Ordinal)
            .ToList();
        if (remaining.Count < 2)
        {
            yield break;
        }

        HashSet<string> connected = [remaining[0].Id!];
        HashSet<string> unused = remaining.Skip(1).Select(station => station.Id!).ToHashSet(StringComparer.Ordinal);
        while (unused.Count > 0)
        {
            (string? firstId, string? secondId, double distance) best = (null, null, double.MaxValue);
            foreach (string firstId in connected)
            {
                foreach (string secondId in unused)
                {
                    double distance = Distance(stationPoints[firstId], stationPoints[secondId]);
                    if (distance < best.distance)
                    {
                        best = (firstId, secondId, distance);
                    }
                }
            }

            if (best.firstId is null || best.secondId is null || best.distance > maxConnectorDistance)
            {
                yield break;
            }

            connected.Add(best.secondId);
            unused.Remove(best.secondId);
            MetroStation first = remaining.First(station => string.Equals(station.Id, best.firstId, StringComparison.Ordinal));
            yield return new VirtualTransferHint(
                NormalizeStationDisplayName(first.Name!),
                best.firstId,
                best.secondId,
                stationPoints[best.firstId],
                stationPoints[best.secondId],
                best.distance);
        }
    }

    private static string NormalizeStationDisplayName(string name)
    {
        return string.Join(" ", name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AppendStations(
        StringBuilder svg,
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        Dictionary<string, StationRenderAnchor> stationAnchors,
        Dictionary<string, SchematicStationAdjustment> schematicStationAdjustments,
        List<SchematicV2DenseStationPair> schematicV2DenseStationPairs,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options)
    {
        svg.AppendLine("""<g id="stations">""");
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        foreach (MetroStation station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id) || !stationPoints.TryGetValue(station.Id, out SvgPoint point))
            {
                continue;
            }

            bool isInterchange = IsInterchange(station);
            bool isTerminal = terminalStationIds.Contains(station.Id!);
            double radius = GetStationRadius(station, options);
            if (IsTransitMapStyle(options) && isTerminal && !isInterchange)
            {
                radius += 1.0;
            }

            string stationClass = isInterchange
                ? isTerminal ? "station interchange terminal" : "station interchange"
                : isTerminal ? "station terminal" : "station";
            double strokeWidth = isInterchange
                ? visualStyle.InterchangeMarkerStrokeWidth
                : visualStyle.StationMarkerStrokeWidth + (isTerminal ? 0.35 : 0);
            string anchorAttributes = BuildStationAnchorAttributes(station.Id!, stationAnchors);
            string schematicAdjustmentAttributes = BuildSchematicStationAdjustmentAttributes(station.Id!, schematicStationAdjustments);
            string schematicV2DenseAttributes = BuildSchematicV2DenseStationAttributes(station.Id!, schematicV2DenseStationPairs);
            string layoutOverrideAttributes = IsStationOverridden(station.Id!, options.LayoutOverrides) ? " data-layout-override=\"station\"" : string.Empty;
            svg.AppendLine($"""<circle class="{stationClass}" data-station-id="{Escape(station.Id)}"{anchorAttributes}{schematicAdjustmentAttributes}{schematicV2DenseAttributes}{layoutOverrideAttributes} data-station-terminal="{(isTerminal ? "true" : "false")}" data-marker-stroke-width="{Format(strokeWidth)}" cx="{Format(point.X)}" cy="{Format(point.Y)}" r="{Format(radius)}" />""");
            if (IsTransitMapStyle(options) && isInterchange)
            {
                double innerRadius = Math.Max(2.8, radius - 3.8);
                svg.AppendLine($"""<circle class="station-interchange-inner" data-station-id="{Escape(station.Id)}" cx="{Format(point.X)}" cy="{Format(point.Y)}" r="{Format(innerRadius)}" />""");
            }
        }

        svg.AppendLine("</g>");
    }

    private static void AppendLabels(
        StringBuilder svg,
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        Dictionary<string, StationRenderAnchor> stationAnchors,
        Dictionary<string, SchematicStationAdjustment> schematicStationAdjustments,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options,
        bool hasLegend)
    {
        List<PlacedLabel> placedLabels = BuildPlacedLabels(stations, stationPoints, terminalStationIds, options, hasLegend);
        svg.AppendLine("""<g id="labels">""");
        foreach (PlacedLabel label in placedLabels.OrderBy(label => label.Priority).ThenBy(label => label.Index))
        {
            string anchor = label.Anchor == "start" ? string.Empty : $" text-anchor=\"{label.Anchor}\"";
            string stationAnchorAttributes = BuildStationAnchorAttributes(label.StationId, stationAnchors);
            string schematicAdjustmentAttributes = BuildSchematicStationAdjustmentAttributes(label.StationId, schematicStationAdjustments);
            bool isImportantLabel = label.IsInterchange || label.IsTerminal;
            string labelOverrideAttributes = label.OverrideApplied ? " data-layout-override=\"label\"" : string.Empty;
            string commonAttributes = $"x=\"{Format(label.X)}\" y=\"{Format(label.Y)}\"{anchor} data-station-id=\"{Escape(label.StationId)}\"{stationAnchorAttributes}{schematicAdjustmentAttributes}{labelOverrideAttributes} data-label-position=\"{label.PositionName}\" data-label-priority=\"{label.Priority}\" data-label-important=\"{(isImportantLabel ? "true" : "false")}\" data-label-interchange=\"{(label.IsInterchange ? "true" : "false")}\" data-label-terminal=\"{(label.IsTerminal ? "true" : "false")}\"";
            svg.AppendLine($"""<text class="station-label station-label-halo" {commonAttributes}>{Escape(label.Text)}</text>""");
            svg.AppendLine($"""<text class="station-label" {commonAttributes}>{Escape(label.Text)}</text>""");
        }

        svg.AppendLine("</g>");
    }

    private static List<PlacedLabel> BuildPlacedLabels(
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options,
        bool hasLegend)
    {
        List<SvgRect> stationObstacles = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && stationPoints.ContainsKey(station.Id!))
            .Select(station =>
            {
                SvgPoint point = stationPoints[station.Id!];
                double radius = GetStationRadius(station, options) + 1.5;
                return SvgRect.FromCenter(point.X, point.Y, radius * 2, radius * 2);
            })
            .ToList();

        List<LabelRequest> labelRequests = [];
        for (int i = 0; i < stations.Count; i++)
        {
            MetroStation station = stations[i];
            if (string.IsNullOrWhiteSpace(station.Id) || !stationPoints.TryGetValue(station.Id, out SvgPoint point))
            {
                continue;
            }

            if (IsLabelHiddenByOverride(station.Id!, options.LayoutOverrides))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(station.Name) ? station.Id! : station.Name!;
            bool isTerminal = terminalStationIds.Contains(station.Id!);
            bool isInterchange = IsInterchange(station);
            bool isGenericName = StationLabelClassifier.IsGenericOrFallbackName(name, station.Id);
            bool isProtected = isInterchange && options.AlwaysShowInterchanges || isTerminal && options.AlwaysShowTerminals;
            if (options.HideGenericStationLabels && isGenericName && !isProtected)
            {
                continue;
            }

            int priority = CalculateLabelPriority(isInterchange, isTerminal, isGenericName);
            bool canHideWhenCrowded = !isProtected && (isGenericName || (!isInterchange && !isTerminal));
            labelRequests.Add(new LabelRequest(station, name, point, GetStationRadius(station, options), priority, i, isProtected, canHideWhenCrowded, isInterchange, isTerminal, isGenericName));
        }

        SvgRect allowedBounds = CreateAllowedLabelBounds(options, hasLegend);
        List<PlacedLabel> placedLabels = [];
        List<SvgRect> placedLabelBoxes = [];

        foreach (LabelRequest request in labelRequests
            .OrderByDescending(request => request.Priority)
            .ThenBy(request => request.Index))
        {
            PlacedLabel label = ChooseLabelPlacement(request, options, placedLabelBoxes, stationObstacles, allowedBounds);
            bool labelOverrideApplied = TryApplyLabelOverride(label, options.LayoutOverrides, out PlacedLabel overriddenLabel);
            label = overriddenLabel;
            if (options.HideCrowdedLabels
                && !labelOverrideApplied
                && request.CanHideWhenCrowded
                && label.LabelOverlapArea > GetCrowdedLabelOverlapThreshold(options))
            {
                continue;
            }

            placedLabels.Add(label);
            placedLabelBoxes.Add(label.Box);
        }

        return placedLabels;
    }

    private static bool IsLabelHiddenByOverride(string stationId, LayoutOverrideDocument? overrides)
    {
        return overrides?.Labels.TryGetValue(stationId, out LabelLayoutOverride? labelOverride) == true
            && labelOverride.Hidden == true;
    }

    private static bool TryApplyLabelOverride(
        PlacedLabel label,
        LayoutOverrideDocument? overrides,
        out PlacedLabel overriddenLabel)
    {
        overriddenLabel = label;
        if (overrides is null
            || !overrides.Labels.TryGetValue(label.StationId, out LabelLayoutOverride? labelOverride)
            || labelOverride.Hidden == true)
        {
            return false;
        }

        double adjustedX = labelOverride.X ?? label.X;
        double adjustedY = labelOverride.Y ?? label.Y;
        adjustedX += labelOverride.Dx ?? 0;
        adjustedY += labelOverride.Dy ?? 0;
        double deltaX = adjustedX - label.X;
        double deltaY = adjustedY - label.Y;
        string positionName = string.IsNullOrWhiteSpace(labelOverride.Position)
            ? label.PositionName
            : labelOverride.Position!;
        overriddenLabel = label with
        {
            X = adjustedX,
            Y = adjustedY,
            PositionName = positionName,
            Box = new SvgRect(
                label.Box.Left + deltaX,
                label.Box.Top + deltaY,
                label.Box.Right + deltaX,
                label.Box.Bottom + deltaY),
            OverrideApplied = true
        };
        return Math.Abs(deltaX) > 0.001
            || Math.Abs(deltaY) > 0.001
            || !string.Equals(positionName, label.PositionName, StringComparison.Ordinal);
    }

    private static string BuildSchematicStationAdjustmentAttributes(
        string stationId,
        Dictionary<string, SchematicStationAdjustment> adjustments)
    {
        if (!adjustments.TryGetValue(stationId, out SchematicStationAdjustment adjustment))
        {
            return string.Empty;
        }

        return $" data-schematic-station-adjusted=\"true\" data-schematic-station-adjustment-distance=\"{Format(adjustment.Distance)}\" data-schematic-station-adjustment-reason=\"{Escape(adjustment.Reason)}\" data-schematic-station-original-x=\"{Format(adjustment.OriginalPoint.X)}\" data-schematic-station-original-y=\"{Format(adjustment.OriginalPoint.Y)}\"";
    }

    private static string BuildSchematicV2DenseStationAttributes(
        string stationId,
        List<SchematicV2DenseStationPair> densePairs)
    {
        List<SchematicV2DenseStationPair> stationPairs = densePairs
            .Where(pair => string.Equals(pair.FirstStationId, stationId, StringComparison.Ordinal)
                || string.Equals(pair.SecondStationId, stationId, StringComparison.Ordinal))
            .OrderBy(pair => pair.Distance)
            .ToList();

        if (stationPairs.Count == 0)
        {
            return string.Empty;
        }

        string pairedStationIds = string.Join(",", stationPairs.Select(pair =>
            string.Equals(pair.FirstStationId, stationId, StringComparison.Ordinal)
                ? pair.SecondStationId
                : pair.FirstStationId));
        double minimumDistance = stationPairs.Min(pair => pair.Distance);
        bool hasAdjacentPair = stationPairs.Any(pair => pair.Adjacent);
        bool sameNameCluster = stationPairs.Any(pair => pair.SameNameCluster);
        bool sameNameAssetDefaultCluster = stationPairs.Any(pair => pair.SameNameAssetDefaultCluster);
        bool sameNameLikelyUserCluster = stationPairs.Any(pair => pair.SameNameLikelyUserCluster);

        return $" data-schematic-v2-dense-station=\"true\" data-schematic-v2-dense-pair-count=\"{stationPairs.Count}\" data-schematic-v2-dense-paired-stations=\"{Escape(pairedStationIds)}\" data-schematic-v2-dense-min-distance=\"{Format(minimumDistance)}\" data-schematic-v2-dense-has-adjacent-pair=\"{(hasAdjacentPair ? "true" : "false")}\" data-schematic-v2-dense-same-name-cluster=\"{(sameNameCluster ? "true" : "false")}\" data-schematic-v2-dense-same-name-asset-default-cluster=\"{(sameNameAssetDefaultCluster ? "true" : "false")}\" data-schematic-v2-dense-same-name-likely-user-cluster=\"{(sameNameLikelyUserCluster ? "true" : "false")}\"";
    }

    private static string BuildStationAnchorAttributes(string stationId, Dictionary<string, StationRenderAnchor> stationAnchors)
    {
        if (!stationAnchors.TryGetValue(stationId, out StationRenderAnchor anchor))
        {
            return string.Empty;
        }

        string familyAttribute = anchor.FamilyKeys.Count == 0
            ? string.Empty
            : $" data-station-anchor-family=\"{Escape(string.Join(",", anchor.FamilyKeys))}\"";
        string fallbackAttribute = string.IsNullOrWhiteSpace(anchor.FallbackReason)
            ? string.Empty
            : $" data-station-anchor-fallback=\"{Escape(anchor.FallbackReason)}\"";
        return $" data-station-anchor=\"{Escape(anchor.Source)}\" data-station-anchor-applied=\"{(anchor.Applied ? "true" : "false")}\" data-station-anchor-distance=\"{Format(anchor.Distance)}\" data-station-raw-x=\"{Format(anchor.RawPoint.X)}\" data-station-raw-y=\"{Format(anchor.RawPoint.Y)}\"{familyAttribute}{fallbackAttribute}";
    }

    private static PlacedLabel ChooseLabelPlacement(
        LabelRequest request,
        SvgRenderOptions options,
        List<SvgRect> placedLabelBoxes,
        List<SvgRect> stationObstacles,
        SvgRect allowedBounds)
    {
        List<LabelCandidate> candidates = CreateLabelCandidates(request, options);
        LabelCandidate best = candidates[0];
        double bestScore = double.MaxValue;
        double bestLabelOverlapArea = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            LabelCandidate candidate = candidates[i];
            double score = i * 0.01;
            double labelOverlapArea = 0;

            foreach (SvgRect box in placedLabelBoxes)
            {
                double overlap = candidate.Box.OverlapArea(box);
                labelOverlapArea += overlap;
                score += overlap * 14;
            }

            foreach (SvgRect obstacle in stationObstacles)
            {
                score += candidate.Box.OverlapArea(obstacle) * 8;
            }

            score += candidate.Box.OutsideArea(allowedBounds) * 18;

            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
                bestLabelOverlapArea = labelOverlapArea;
            }
        }

        return new PlacedLabel(
            request.Station.Id!,
            request.Text,
            best.X,
            best.Y,
            best.Anchor,
            best.PositionName,
            best.Box,
            request.Priority,
            request.Index,
            bestScore,
            bestLabelOverlapArea,
            request.IsInterchange,
            request.IsTerminal,
            request.IsGenericName);
    }

    private static double GetCrowdedLabelOverlapThreshold(SvgRenderOptions options)
    {
        return Math.Max(48, options.LabelFontSize * options.LabelFontSize * 0.5);
    }

    private static List<LabelCandidate> CreateLabelCandidates(LabelRequest request, SvgRenderOptions options)
    {
        double fontSize = options.LabelFontSize;
        double width = EstimateTextWidth(request.Text, fontSize);
        double height = fontSize * 1.25;
        double gap = options.LabelGap;
        double offset = request.StationRadius + gap;
        double diagonalOffset = request.StationRadius + gap * 0.9;
        SvgPoint point = request.Point;

        return
        [
            CreateLabelCandidate("right", point.X + offset, point.Y - height / 2, width, height, "start", fontSize),
            CreateLabelCandidate("left", point.X - offset - width, point.Y - height / 2, width, height, "end", fontSize),
            CreateLabelCandidate("top", point.X - width / 2, point.Y - offset - height, width, height, "middle", fontSize),
            CreateLabelCandidate("bottom", point.X - width / 2, point.Y + offset, width, height, "middle", fontSize),
            CreateLabelCandidate("top-right", point.X + diagonalOffset, point.Y - diagonalOffset - height, width, height, "start", fontSize),
            CreateLabelCandidate("bottom-right", point.X + diagonalOffset, point.Y + diagonalOffset, width, height, "start", fontSize),
            CreateLabelCandidate("top-left", point.X - diagonalOffset - width, point.Y - diagonalOffset - height, width, height, "end", fontSize),
            CreateLabelCandidate("bottom-left", point.X - diagonalOffset - width, point.Y + diagonalOffset, width, height, "end", fontSize)
        ];
    }

    private static LabelCandidate CreateLabelCandidate(string positionName, double left, double top, double width, double height, string anchor, double fontSize)
    {
        SvgRect box = new(left, top, left + width, top + height);
        double x = anchor switch
        {
            "end" => box.Right,
            "middle" => (box.Left + box.Right) / 2,
            _ => box.Left
        };
        double y = box.Top + fontSize;
        return new LabelCandidate(positionName, x, y, anchor, box);
    }

    private static SvgRect CreateAllowedLabelBounds(SvgRenderOptions options, bool hasLegend)
    {
        int padding = options.EffectivePadding;
        double left = Math.Max(4, padding * 0.25);
        double top = IsTransitMapStyle(options)
            ? GetTransitMapHeaderHeight(options) + Math.Max(8, padding * (options.CompactTransitMapFrame ? 0.08 : 0.15))
            : Math.Max(4, padding * 0.35);
        double right = hasLegend && !IsTransitMapStyle(options)
            ? options.Width - padding - options.LegendWidth - 12
            : options.Width - Math.Max(4, padding * 0.25);
        double bottom = IsTransitMapStyle(options)
            ? options.Height - GetTransitMapFooterHeight(options) - Math.Max(8, padding * (options.CompactTransitMapFrame ? 0.08 : 0.15))
            : options.Height - Math.Max(4, padding * 0.25);

        if (right <= left + 100)
        {
            right = Math.Max(left + 100, options.Width - options.LegendWidth - 4);
        }

        return new SvgRect(left, top, right, bottom);
    }

    private static void AppendLegend(StringBuilder svg, IReadOnlyList<DisplayLineFamily> families, SvgRenderOptions options, bool hasLegend)
    {
        if (!hasLegend)
        {
            return;
        }

        if (IsTransitMapStyle(options))
        {
            AppendTransitMapLegend(svg, families, options);
            return;
        }

        int padding = options.EffectivePadding;
        double legendX = options.Width - padding - options.LegendWidth;
        double legendY = padding;
        double sampleLength = Math.Min(56, Math.Max(40, options.LegendWidth * 0.18));
        double labelX = legendX + sampleLength + 20;
        double baseRowHeight = Math.Max(38, options.LegendLabelFontSize + 20);
        double variantRowHeight = Math.Max(23, options.LegendLabelFontSize * 1.25);
        double legendTitleGap = Math.Max(42, options.LegendLabelFontSize + 24);

        svg.AppendLine("""<g id="legend">""");
        svg.AppendLine($"""<text class="legend-title" x="{Format(legendX)}" y="{Format(legendY)}">Legend</text>""");

        double y = legendY + legendTitleGap;
        foreach (DisplayLineFamily family in families)
        {
            svg.AppendLine($"""<line x1="{Format(legendX)}" y1="{Format(y)}" x2="{Format(legendX + sampleLength)}" y2="{Format(y)}" stroke="{Escape(family.Color)}" stroke-width="{Format(Math.Max(5, options.LineWidth * 0.57))}" stroke-linecap="round" />""");
            svg.AppendLine($"""<text class="legend-label" x="{Format(labelX)}" y="{Format(y + 5)}">{Escape(family.DisplayName)}</text>""");

            if (family.Variants.Count > 1)
            {
                foreach (DisplayServiceVariant variant in family.Variants)
                {
                    y += variantRowHeight;
                    svg.AppendLine($"""<text class="legend-variant" x="{Format(labelX)}" y="{Format(y + 5)}">{Escape(FormatServiceVariantLegendText(variant))}</text>""");
                }
            }

            y += baseRowHeight;
        }

        svg.AppendLine("</g>");
    }

    private static void AppendTransitMapLegend(StringBuilder svg, IReadOnlyList<DisplayLineFamily> families, SvgRenderOptions options)
    {
        double footerHeight = GetTransitMapFooterHeight(options);
        double padding = Math.Max(24, options.EffectivePadding * 0.45);
        double panelX = padding;
        double panelY = options.Height - footerHeight + Math.Max(18, footerHeight * 0.14);
        double panelWidth = Math.Max(240, options.Width - padding * 2);
        double panelHeight = Math.Max(options.CompactTransitMapFrame ? 54 : 72, footerHeight - Math.Max(28, footerHeight * 0.22));
        bool hasExpressLegend = families.Any(HasExpressServiceVariant);
        double titleWidth = hasExpressLegend
            ? Math.Min(250, panelWidth * 0.24)
            : Math.Min(140, panelWidth * 0.16);
        double itemAreaX = panelX + titleWidth + 18;
        double itemAreaWidth = Math.Max(160, panelWidth - titleWidth - 34);
        int columns = Math.Clamp((int)Math.Floor(itemAreaWidth / 280), 2, 6);
        int rows = Math.Max(1, (int)Math.Ceiling(families.Count / (double)columns));
        double columnWidth = itemAreaWidth / columns;
        double rowHeight = Math.Max(options.CompactTransitMapFrame ? 24 : 30, Math.Min(options.CompactTransitMapFrame ? 34 : 40, panelHeight / Math.Max(1, rows + 0.8)));
        double sampleLength = Math.Min(54, Math.Max(34, columnWidth * 0.22));
        double lineWidth = Math.Max(5, options.LineWidth * 0.55);
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        double symbolX = panelX + panelWidth - Math.Min(355, Math.Max(250, panelWidth * 0.25));
        double symbolY = panelY + panelHeight - 25;
        List<DisplayLineFamily> serviceFamilies = families
            .Where(family => family.Variants.Count > 1)
            .Take(3)
            .ToList();

        svg.AppendLine("""<g id="legend" data-legend-placement="bottom" data-map-style="transit-map">""");
        svg.AppendLine($"""<rect x="{Format(panelX)}" y="{Format(panelY)}" width="{Format(panelWidth)}" height="{Format(panelHeight)}" rx="8" fill="#ffffff" stroke="#d1d5db" stroke-width="1.2" />""");
        svg.AppendLine($"""<text class="legend-title" x="{Format(panelX + 18)}" y="{Format(panelY + 28)}">{"\u7ebf\u8def\u53ca\u8bf4\u660e"}</text>""");
        svg.AppendLine($"""<text class="transit-footer-note" x="{Format(panelX + 18)}" y="{Format(panelY + 48)}">Key to lines and symbols</text>""");
        svg.AppendLine($"""<line x1="{Format(panelX + titleWidth)}" y1="{Format(panelY + 14)}" x2="{Format(panelX + titleWidth)}" y2="{Format(panelY + panelHeight - 14)}" stroke="#e5e7eb" stroke-width="1" />""");
        if (hasExpressLegend)
        {
            svg.AppendLine($"""<line x1="{Format(panelX + 18)}" y1="{Format(panelY + 70)}" x2="{Format(panelX + 70)}" y2="{Format(panelY + 70)}" stroke="#0f766e" stroke-width="{Format(lineWidth)}" stroke-linecap="round" />""");
            svg.AppendLine($"""<line x1="{Format(panelX + 18)}" y1="{Format(panelY + 70)}" x2="{Format(panelX + 70)}" y2="{Format(panelY + 70)}" stroke="#ffffff" stroke-width="{Format(Math.Max(2, lineWidth * 0.38))}" stroke-linecap="round" />""");
            svg.AppendLine($"""<text class="transit-footer-note" x="{Format(panelX + 18)}" y="{Format(panelY + 91)}">Express / skip-stop marker</text>""");
        }

        for (int i = 0; i < families.Count; i++)
        {
            DisplayLineFamily family = families[i];
            int column = i / rows;
            int row = i % rows;
            double x = itemAreaX + column * columnWidth;
            double y = panelY + 27 + row * rowHeight;
            double textX = x + sampleLength + 12;
            svg.AppendLine($"""<line x1="{Format(x)}" y1="{Format(y)}" x2="{Format(x + sampleLength)}" y2="{Format(y)}" stroke="{Escape(family.Color)}" stroke-width="{Format(lineWidth)}" stroke-linecap="round" />""");
            svg.AppendLine($"""<text class="legend-label" x="{Format(textX)}" y="{Format(y + 5)}">{Escape(family.DisplayName)}</text>""");
            if (HasExpressServiceVariant(family))
            {
                svg.AppendLine($"""<line x1="{Format(x)}" y1="{Format(y)}" x2="{Format(x + sampleLength)}" y2="{Format(y)}" stroke="#ffffff" stroke-width="{Format(Math.Max(2, lineWidth * 0.38))}" stroke-linecap="round" data-legend-express-marker="white-center-stripe" />""");
            }
        }

        if (serviceFamilies.Count > 0 && panelHeight >= 104)
        {
            double serviceX = itemAreaX;
            double serviceY = panelY + 31 + rows * rowHeight + 14;
            double serviceLineHeight = Math.Max(14, options.LegendLabelFontSize * 0.78);
            double maxServiceWidth = Math.Max(160, symbolX - serviceX - 24);
            svg.AppendLine($"""<text class="transit-footer-note" x="{Format(serviceX)}" y="{Format(serviceY)}" data-legend-service-title="true">Service variants</text>""");
            serviceY += serviceLineHeight;
            foreach (DisplayLineFamily family in serviceFamilies)
            {
                foreach (DisplayServiceVariant variant in family.Variants.Take(3))
                {
                    string variantText = $"{family.DisplayName}: {FormatTransitMapServiceVariantLegendText(variant)}";
                    svg.AppendLine($"""<text class="legend-variant" x="{Format(serviceX)}" y="{Format(serviceY)}" data-legend-service-variant="true">{Escape(TruncateLegendText(variantText, maxServiceWidth, Math.Max(12, options.LegendLabelFontSize * 0.72)))}</text>""");
                    serviceY += serviceLineHeight;
                }
            }
        }

        svg.AppendLine($"""<circle class="station" cx="{Format(symbolX)}" cy="{Format(symbolY)}" r="{Format(visualStyle.StationMarkerOuterRadius)}" data-legend-symbol="station" />""");
        svg.AppendLine($"""<text class="transit-footer-note" x="{Format(symbolX + 18)}" y="{Format(symbolY + 5)}">Station</text>""");
        svg.AppendLine($"""<circle class="station terminal" cx="{Format(symbolX + 88)}" cy="{Format(symbolY)}" r="{Format(visualStyle.StationMarkerOuterRadius + 1)}" data-legend-symbol="terminal" />""");
        svg.AppendLine($"""<text class="transit-footer-note" x="{Format(symbolX + 107)}" y="{Format(symbolY + 5)}">Terminal</text>""");
        svg.AppendLine($"""<circle class="station interchange" cx="{Format(symbolX + 196)}" cy="{Format(symbolY)}" r="{Format(visualStyle.InterchangeMarkerRadius)}" data-legend-symbol="transfer" />""");
        svg.AppendLine($"""<circle class="station-interchange-inner" cx="{Format(symbolX + 196)}" cy="{Format(symbolY)}" r="{Format(Math.Max(2.8, visualStyle.InterchangeMarkerRadius - 3.8))}" />""");
        svg.AppendLine($"""<text class="transit-footer-note" x="{Format(symbolX + 218)}" y="{Format(symbolY + 5)}">Transfer</text>""");
        svg.AppendLine("</g>");
    }

    private static bool IsStationOverridden(string stationId, LayoutOverrideDocument? overrides)
    {
        return overrides?.Stations.TryGetValue(stationId, out StationLayoutOverride? stationOverride) == true
            && stationOverride.Enabled;
    }

    private static string FormatServiceVariantLegendText(DisplayServiceVariant variant)
    {
        string prefix = $"{variant.VariantName}: {variant.StopCount} stops";
        if (!string.IsNullOrWhiteSpace(variant.StartStationName) && !string.IsNullOrWhiteSpace(variant.EndStationName))
        {
            return $"{prefix}, {variant.StartStationName} -> {variant.EndStationName}";
        }

        return prefix;
    }

    private static string FormatTransitMapServiceVariantLegendText(DisplayServiceVariant variant)
    {
        string prefix = $"{variant.VariantName}: {variant.StopCount} stops";
        if (!string.IsNullOrWhiteSpace(variant.StartStationName) && !string.IsNullOrWhiteSpace(variant.EndStationName))
        {
            return $"{prefix}, {variant.StartStationName} -> {variant.EndStationName}";
        }

        return prefix;
    }

    private static string TruncateLegendText(string text, double maxWidth, double fontSize)
    {
        if (EstimateTextWidth(text, fontSize) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        int keep = text.Length;
        while (keep > 4 && EstimateTextWidth(text[..keep] + ellipsis, fontSize) > maxWidth)
        {
            keep--;
        }

        return keep > 4 ? text[..keep] + ellipsis : text;
    }

    private static bool HasExpressServiceVariant(DisplayLineFamily family)
    {
        return family.Variants.Any(IsExpressServiceVariant);
    }

    private static bool IsExpressServiceVariant(DisplayServiceVariant variant)
    {
        return IsExpressServiceText(variant.OriginalName) || IsExpressServiceText(variant.VariantName);
    }

    private static bool IsExpressServiceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string lower = value.ToLowerInvariant();
        return value.Contains("快", StringComparison.Ordinal)
            || value.Contains("特快", StringComparison.Ordinal)
            || value.Contains("大站快车", StringComparison.Ordinal)
            || value.Contains("机场快线", StringComparison.Ordinal)
            || lower.Contains("express", StringComparison.Ordinal)
            || lower.Contains("rapid", StringComparison.Ordinal);
    }

    private static IReadOnlyList<DisplayLineFamily> SortFamiliesForLegend(List<DisplayLineFamily> families)
    {
        return families
            .Select((family, index) => new LegendLine(family, index, ExtractLineNumber(family.DisplayName)))
            .OrderBy(item => item.LineNumber.HasValue ? 0 : 1)
            .ThenBy(item => item.LineNumber ?? int.MaxValue)
            .ThenBy(item => item.LineNumber.HasValue ? item.Index : 0)
            .ThenBy(item => item.LineNumber.HasValue ? string.Empty : item.Family.DisplayName, StringComparer.CurrentCulture)
            .Select(item => item.Family)
            .ToList();
    }

    private static int? ExtractLineNumber(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int? value = null;
        foreach (char character in name)
        {
            double numericValue = char.GetNumericValue(character);
            if (numericValue >= 0 && numericValue <= 9 && Math.Floor(numericValue) == numericValue)
            {
                value = (value ?? 0) * 10 + (int)numericValue;
            }
            else if (value.HasValue)
            {
                return value;
            }
        }

        return value;
    }

    private static HashSet<string> GetTerminalStationIds(List<MetroLine> lines, Dictionary<string, MetroStation> stationsById)
    {
        HashSet<string> terminalStationIds = new(StringComparer.Ordinal);

        foreach (MetroLine line in lines)
        {
            List<string> validStops = (line.Stops ?? [])
                .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId))
                .ToList();

            if (validStops.Count < 2)
            {
                continue;
            }

            string first = validStops.First();
            string last = validStops.Last();
            if (!string.Equals(first, last, StringComparison.Ordinal))
            {
                terminalStationIds.Add(first);
                terminalStationIds.Add(last);
            }
        }

        return terminalStationIds;
    }

    private static int CalculateLabelPriority(bool isInterchange, bool isTerminal, bool isGenericName)
    {
        int priority = 0;

        if (isInterchange)
        {
            priority += 100;
        }

        if (isTerminal)
        {
            priority += 70;
        }

        priority += isGenericName ? -35 : 25;
        return priority;
    }

    private static bool IsInterchange(MetroStation station)
    {
        return station.IsInterchange || (station.Lines?.Distinct(StringComparer.Ordinal).Count() ?? 0) > 1;
    }

    private static double GetStationRadius(MetroStation station, SvgRenderOptions options)
    {
        SvgVisualStyle visualStyle = SvgVisualStyle.From(options);
        return IsInterchange(station) ? visualStyle.InterchangeMarkerRadius : visualStyle.StationMarkerOuterRadius;
    }

    private static double EstimateTextWidth(string text, double fontSize)
    {
        double units = 0;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                units += 0.35;
            }
            else if (character <= 0x007f)
            {
                units += 0.58;
            }
            else
            {
                units += 0.95;
            }
        }

        return Math.Max(fontSize * 1.4, units * fontSize);
    }

    private static void AppendFooter(StringBuilder svg, SvgRenderOptions options)
    {
        if (IsTransitMapStyle(options))
        {
            double y = options.Height - Math.Max(8, GetTransitMapFooterHeight(options) * 0.08);
            svg.AppendLine($"""<text class="transit-footer-note" x="{Format(options.Width - Math.Max(28, options.EffectivePadding * 0.45))}" y="{Format(y)}" text-anchor="end">Generated by CS2 Metro Diagram</text>""");
        }

        svg.AppendLine("</svg>");
    }

    private static bool IsTransitMapStyle(SvgRenderOptions options)
    {
        return options.MapStyle == SvgMapStyle.TransitMap;
    }

    private static bool IsSchematicMapLayout(SvgRenderOptions options)
    {
        return options.LayoutMode == SvgLayoutMode.SchematicMap;
    }

    private static double GetRouteBadgeFontSize(SvgRenderOptions options)
    {
        return IsSchematicMapLayout(options) ? 14 : 13;
    }

    private static string GetMapStyleName(SvgMapStyle mapStyle)
    {
        return mapStyle switch
        {
            SvgMapStyle.TransitMap => "transit-map",
            _ => "standard"
        };
    }

    private static double GetTransitMapHeaderHeight(SvgRenderOptions options)
    {
        if (options.CompactTransitMapFrame)
        {
            return Math.Clamp(options.Height * 0.065, 92, 132);
        }

        return Math.Clamp(options.Height * 0.09, 104, 150);
    }

    private static double GetTransitMapFooterHeight(SvgRenderOptions options)
    {
        if (options.CompactTransitMapFrame)
        {
            return Math.Clamp(options.Height * 0.075, 86, 140);
        }

        return IsSchematicMapLayout(options)
            ? Math.Clamp(options.Height * 0.135, 150, 240)
            : Math.Clamp(options.Height * 0.12, 128, 210);
    }

    private static string GetTransitMapFrameName(SvgRenderOptions options)
    {
        if (!IsTransitMapStyle(options))
        {
            return "none";
        }

        return options.CompactTransitMapFrame ? "compact" : "standard";
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder escaped = new(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];

            if (char.IsHighSurrogate(character))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    escaped.Append(character);
                    escaped.Append(value[i + 1]);
                    i++;
                }

                continue;
            }

            if (char.IsLowSurrogate(character) || !IsAllowedXmlCharacter(character))
            {
                continue;
            }

            switch (character)
            {
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                case '"':
                    escaped.Append("&quot;");
                    break;
                case '\'':
                    escaped.Append("&apos;");
                    break;
                default:
                    escaped.Append(character);
                    break;
            }
        }

        return escaped.ToString();
    }

    private static bool IsAllowedXmlCharacter(char character)
    {
        return character == '\u0009'
            || character == '\u000a'
            || character == '\u000d'
            || character >= '\u0020';
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private sealed class CoordinateProjector
    {
        private readonly double _minX;
        private readonly double _maxZ;
        private readonly double _scale;
        private readonly double _originX;
        private readonly double _originY;
        private readonly double _offsetX;
        private readonly double _offsetY;
        private readonly CenterExpansionTransform? _centerExpansion;

        private CoordinateProjector(
            double minX,
            double maxZ,
            double scale,
            double originX,
            double originY,
            double offsetX,
            double offsetY,
            CenterExpansionTransform? centerExpansion)
        {
            _minX = minX;
            _maxZ = maxZ;
            _scale = scale;
            _originX = originX;
            _originY = originY;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _centerExpansion = centerExpansion;
        }

        public static CoordinateProjector? Create(List<SourceCoordinate> sourceCoordinates, SvgRenderOptions options, bool reserveLegendSpace)
        {
            if (sourceCoordinates.Count == 0)
            {
                return null;
            }

            CenterExpansionTransform? centerExpansion = options.EnableCenterExpansion
                ? CenterExpansionTransform.Create(sourceCoordinates, options.CenterExpansionStrength)
                : null;
            List<SourceCoordinate> projectedCoordinates = centerExpansion is null
                ? sourceCoordinates
                : sourceCoordinates.Select(point => centerExpansion.Value.Apply(point.X, point.Z)).ToList();

            double minX = projectedCoordinates.Min(point => point.X);
            double maxX = projectedCoordinates.Max(point => point.X);
            double minZ = projectedCoordinates.Min(point => point.Z);
            double maxZ = projectedCoordinates.Max(point => point.Z);
            double sourceWidth = Math.Max(maxX - minX, 1);
            double sourceHeight = Math.Max(maxZ - minZ, 1);
            SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);
            double innerWidth = Math.Max(bounds.Right - bounds.Left, 1);
            double innerHeight = Math.Max(bounds.Bottom - bounds.Top, 1);
            double scale = Math.Min(innerWidth / sourceWidth, innerHeight / sourceHeight);
            double scaledWidth = sourceWidth * scale;
            double scaledHeight = sourceHeight * scale;
            double offsetX = (innerWidth - scaledWidth) / 2;
            double offsetY = (innerHeight - scaledHeight) / 2;

            return new CoordinateProjector(minX, maxZ, scale, bounds.Left, bounds.Top, offsetX, offsetY, centerExpansion);
        }

        public SvgPoint Project(double sourceX, double sourceZ)
        {
            SourceCoordinate coordinate = _centerExpansion is null
                ? new SourceCoordinate(sourceX, sourceZ)
                : _centerExpansion.Value.Apply(sourceX, sourceZ);
            double x = _originX + _offsetX + (coordinate.X - _minX) * _scale;
            double y = _originY + _offsetY + (_maxZ - coordinate.Z) * _scale;
            return new SvgPoint(x, y);
        }
    }

    private readonly record struct CenterExpansionTransform(double CenterX, double CenterZ, double HalfWidth, double HalfHeight, double Strength)
    {
        public static CenterExpansionTransform? Create(List<SourceCoordinate> sourceCoordinates, double strength)
        {
            strength = Math.Clamp(strength, 0, 0.45);
            if (strength <= 0 || sourceCoordinates.Count < 3)
            {
                return null;
            }

            double minX = sourceCoordinates.Min(point => point.X);
            double maxX = sourceCoordinates.Max(point => point.X);
            double minZ = sourceCoordinates.Min(point => point.Z);
            double maxZ = sourceCoordinates.Max(point => point.Z);
            return new CenterExpansionTransform(
                (minX + maxX) / 2,
                (minZ + maxZ) / 2,
                Math.Max((maxX - minX) / 2, 1),
                Math.Max((maxZ - minZ) / 2, 1),
                strength);
        }

        public SourceCoordinate Apply(double x, double z)
        {
            double normalizedX = (x - CenterX) / HalfWidth;
            double normalizedZ = (z - CenterZ) / HalfHeight;
            double normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
            double centerWeight = Math.Max(0, 1 - Math.Min(1, normalizedDistance));
            double factor = 1 + Strength * centerWeight;
            return new SourceCoordinate(
                CenterX + (x - CenterX) * factor,
                CenterZ + (z - CenterZ) * factor);
        }
    }

    private readonly record struct SvgVisualStyle(
        double BaseRouteWidth,
        double SharedCorridorOuterWidth,
        double SharedCorridorInnerWidth,
        double ExpressStripeWidth,
        double StationMarkerOuterRadius,
        double StationMarkerStrokeWidth,
        double InterchangeMarkerRadius,
        double InterchangeMarkerStrokeWidth,
        double LabelHaloWidth,
        double SchematicOverlapOffsetDistance,
        double SchematicOverlapEndpointTrim,
        double SchematicShortOverlapSegmentThreshold)
    {
        public static SvgVisualStyle From(SvgRenderOptions options)
        {
            bool transitMapStyle = options.MapStyle == SvgMapStyle.TransitMap;
            double baseRouteWidth = Math.Max(1, options.LineWidth);
            double sharedOuterWidth = baseRouteWidth;
            double sharedInnerWidth = Math.Clamp(baseRouteWidth * 0.48, 3, baseRouteWidth * 0.62);
            double expressStripeWidth = Math.Clamp(baseRouteWidth * 0.24, 2.4, Math.Max(2.4, baseRouteWidth * 0.34));
            double stationRadius = Math.Max(transitMapStyle ? 4.8 : 3.5, options.StationRadius + (transitMapStyle ? 0.8 : 0));
            double interchangeRadius = Math.Max(options.InterchangeStationRadius + (transitMapStyle ? 1.1 : 0), stationRadius + (transitMapStyle ? 4.2 : 3.5));
            double stationStrokeWidth = transitMapStyle
                ? Math.Clamp(baseRouteWidth * 0.15, 1.8, 2.4)
                : Math.Clamp(baseRouteWidth * 0.13, 1.6, 2.2);
            double interchangeStrokeWidth = transitMapStyle
                ? Math.Clamp(baseRouteWidth * 0.18, 2.2, 3.0)
                : Math.Clamp(baseRouteWidth * 0.16, 2, 2.8);
            double labelHaloWidth = Math.Max(transitMapStyle ? 6 : 5, options.LabelFontSize * (transitMapStyle ? 0.44 : 0.38));
            double schematicOverlapOffsetDistance = options.SchematicSegmentOverlapOffsetDistance > 0
                ? options.SchematicSegmentOverlapOffsetDistance
                : Math.Max(baseRouteWidth * 0.75, options.GridSize * 0.12);
            double schematicOverlapEndpointTrim = options.SchematicOverlapEndpointTrim > 0
                ? options.SchematicOverlapEndpointTrim
                : stationRadius + baseRouteWidth * 0.5;
            double schematicShortOverlapSegmentThreshold = options.SchematicShortOverlapSegmentThreshold > 0
                ? options.SchematicShortOverlapSegmentThreshold
                : Math.Max(2 * stationRadius + 2 * baseRouteWidth, options.GridSize * 1.5);

            return new SvgVisualStyle(
                baseRouteWidth,
                sharedOuterWidth,
                sharedInnerWidth,
                expressStripeWidth,
                stationRadius,
                stationStrokeWidth,
                interchangeRadius,
                interchangeStrokeWidth,
                labelHaloWidth,
                schematicOverlapOffsetDistance,
                schematicOverlapEndpointTrim,
                schematicShortOverlapSegmentThreshold);
        }
    }

    private readonly record struct RenderGeometry(
        Dictionary<string, SvgPoint> StationPoints,
        CoordinateProjector? Projector,
        Dictionary<string, SchematicStationAdjustment> SchematicStationAdjustments,
        List<SchematicV2DenseStationPair> SchematicV2DenseStationPairs,
        Dictionary<string, List<string>>? SchematicV2RouteGuideByFamily,
        Dictionary<string, SchematicV2RouteGuideMetadata>? SchematicV2RouteGuideMetadataByFamily);

    private readonly record struct SchematicLayoutResult(
        Dictionary<string, SvgPoint> Points,
        Dictionary<string, SchematicStationAdjustment> Adjustments,
        List<SchematicV2DenseStationPair> DenseStationPairs,
        Dictionary<string, List<string>> RouteGuideByFamily,
        Dictionary<string, SchematicV2RouteGuideMetadata> RouteGuideMetadataByFamily);

    private readonly record struct SchematicStationAdjustment(
        string StationId,
        SvgPoint OriginalPoint,
        SvgPoint AdjustedPoint,
        double Distance,
        string Reason);

    private readonly record struct SchematicV2DenseStationPair(
        string FirstStationId,
        string SecondStationId,
        double Distance,
        double MinimumSpacing,
        bool Adjacent,
        bool SameNameCluster,
        bool SameNameAssetDefaultCluster,
        bool SameNameLikelyUserCluster,
        bool FirstInterchange,
        bool SecondInterchange);

    private readonly record struct VirtualTransferHint(
        string StationName,
        string FirstStationId,
        string SecondStationId,
        SvgPoint FirstPoint,
        SvgPoint SecondPoint,
        double Distance);

    private readonly record struct SchematicV2FamilyPath(
        string FamilyKey,
        List<string> Stops,
        SvgPoint Direction);

    private sealed record SchematicV2RouteGuideResult(
        Dictionary<string, List<string>> RouteGuideByFamily,
        Dictionary<string, SchematicV2RouteGuideMetadata> MetadataByFamily);

    private sealed record SchematicV2RouteGuideMetadata(
        string CorridorId,
        string FamilyAKey,
        string FamilyBKey,
        double Confidence,
        double SharedLength,
        double AverageDistance,
        double MaxDistance,
        List<string> GuideStationIds);

    private sealed record SchematicV2GeometryCorridorConstraint(
        string FamilyAKey,
        string FamilyBKey,
        string GuideFamilyKey,
        string FamilyAStartStationId,
        string FamilyAEndStationId,
        string FamilyBStartStationId,
        string FamilyBEndStationId,
        List<string> GuideStationIds,
        double SharedLength,
        double AverageDistance,
        double MaxDistance,
        bool StopSequenceMatched,
        bool UseFullGuideInterval);

    private sealed class SchematicV2SharedSegmentBuilder(SvgPoint start, SvgPoint end)
    {
        public SvgPoint Start { get; } = start;

        public SvgPoint End { get; } = end;

        public HashSet<string> FamilyKeys { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct SchematicV2SharedSegment(
        string Key,
        SvgPoint Start,
        SvgPoint End,
        List<string> FamilyKeys,
        string Source);

    private sealed record SchematicV2SharedCorridorRun(
        string RunId,
        List<string> FamilyKeys,
        string Source,
        List<SvgPoint> Points,
        List<string> StationIds)
    {
        public string FamilyAKey => FamilyKeys.Count > 0 ? FamilyKeys[0] : string.Empty;

        public string FamilyBKey => FamilyKeys.Count > 1 ? FamilyKeys[1] : string.Empty;
    }

    private sealed record SchematicV2ParallelCorridorLane(
        VisibleLaneKey LaneKey,
        List<RenderRoute> Routes,
        RenderRoute PrimaryRoute);

    private readonly record struct SchematicV2ProjectedLine(
        DisplayLineFamily Family,
        MetroLine Line,
        List<string> TopologyStops,
        List<SvgPoint> PathPoints,
        List<SvgPoint> StopPoints);

    private readonly record struct GeometrySharedCorridorRun(
        int PathIndexAStart,
        int PathIndexAEnd,
        int PathIndexBStart,
        int PathIndexBEnd,
        int DirectionSign,
        double SharedLength,
        double AverageDistance,
        double MaxDistance);

    private readonly record struct PolylineStationProjection(
        string StationId,
        int SegmentIndex,
        double Distance,
        double PolylineProgress);

    private readonly record struct PolylinePointProjection(
        int SegmentIndex,
        double Distance,
        double PolylineProgress);

    private readonly record struct GeometryPointProjectionMatch(
        int PathIndexA,
        int SegmentIndexB,
        double ProgressA,
        double ProgressB,
        double Distance);

    private readonly record struct SharedAdjacentEdge(
        string FamilyAStartStationId,
        string FamilyAEndStationId,
        string FamilyBStartStationId,
        string FamilyBEndStationId);

    private readonly record struct StationRouteAnchorMap(
        Dictionary<string, SvgPoint> Points,
        Dictionary<string, StationRenderAnchor> Anchors);

    private readonly record struct StationRenderAnchor(
        string StationId,
        SvgPoint RawPoint,
        SvgPoint Point,
        bool Applied,
        string Source,
        double Distance,
        List<string> FamilyKeys,
        string? FallbackReason);

    private readonly record struct StationRouteAnchorCandidate(string FamilyKey, SvgPoint Point, double Distance);

    private readonly record struct RenderRoute(DisplayLineFamily Family, MetroLine Line, RoutePointSet RoutePointSet);

    private readonly record struct SchematicMapRouteSegment(
        int RouteIndex,
        int PolylineIndex,
        int SegmentIndex,
        string FamilyKey,
        string DisplayName,
        string Color,
        SvgPoint Start,
        SvgPoint End,
        double Length,
        SvgPoint Direction);

    private readonly record struct SchematicMapRouteCrossing(
        int Index,
        SvgPoint Point,
        SchematicMapRouteSegment TopSegment,
        SchematicMapRouteSegment BottomSegment,
        double AngleDegrees,
        string FamilyPairKey);

    private sealed record CorridorRenderPlan(
        List<CorridorDrawCommand> NormalBase,
        List<CorridorDrawCommand> SharedBase,
        List<CorridorDrawCommand> SharedInner,
        List<CorridorDrawCommand> ExpressDecorations);

    private sealed record CorridorDrawCommand(
        RenderRoute RenderRoute,
        List<SvgPoint> Points,
        CorridorDrawLayer Layer,
        string? Stroke,
        double StrokeWidth,
        string ExtraAttributes,
        int RoutePartIndex = -1,
        int RoutePartCount = 0);

    private enum CorridorDrawLayer
    {
        NormalBase,
        SharedBase,
        SharedInner,
        ExpressDecoration
    }

    private readonly record struct RoutePointSet(
        List<RoutePolyline> Polylines,
        string Source,
        int OriginalPathPointCount,
        int CleanedPathPointCount,
        double ReductionRatio,
        double MaxPathSegmentLength,
        int SuspiciousJumpCount,
        double EffectiveSimplificationTolerance);

    private readonly record struct RoutePolyline(
        List<SvgPoint> Points,
        string? CorridorId = null,
        int CorridorMemberCount = 0,
        double CorridorOffsetIndex = 0,
        double CorridorOffsetPx = 0,
        SharedCorridorStroke? SharedCorridorStroke = null,
        string? SharedCorridorSkipped = null,
        int SyntheticBendCount = 0);

    private sealed class SchematicSegmentOccupancyBuilder(SvgPoint start, SvgPoint end)
    {
        public SvgPoint Start { get; } = start;

        public SvgPoint End { get; } = end;

        public Dictionary<string, string> Families { get; } = new(StringComparer.Ordinal);
    }

    private sealed record SchematicSegmentOccupancy(
        string Key,
        SvgPoint Start,
        SvgPoint End,
        List<string> FamilyKeys);

    private readonly record struct SchematicSegmentKey(string Key, SvgPoint Start, SvgPoint End);

    private sealed record SchematicOverlapSegment(
        List<SvgPoint> Points,
        bool TrimApplied,
        double TrimDistance,
        string? TrimFallback);

    private readonly record struct SchematicOverlapSafety(bool SafeToOffset, string Reason);

    private sealed record SharedCorridorStroke(
        string RunId,
        string FamilyAKey,
        string FamilyBKey,
        string? OuterColor,
        string? InnerColor,
        int PointCount);

    private sealed record SharedCorridorRun(
        string RunId,
        int RouteIndex,
        DisplayLineFamily FamilyA,
        DisplayLineFamily FamilyB,
        string? OuterColor,
        string? InnerColor,
        List<SvgPoint> Points,
        int FragmentCount,
        HashSet<int> FragmentIndices);

    private readonly record struct SharedCorridorPairKey(string OuterFamilyKey, string InnerFamilyKey);

    private readonly record struct CorridorMatch(int LeftFragmentIndex, int RightFragmentIndex);

    private readonly record struct GeometrySharedSegmentMatch(
        int SegmentIndexA,
        int SegmentIndexB,
        int DirectionSign,
        double OverlapLength,
        double AverageDistance,
        double MaxDistance);

    private readonly record struct CorridorFamilyInfo(
        string FamilyKey,
        DisplayLineFamily Family,
        string DisplayName,
        string? Color,
        int RouteIndex);

    private readonly record struct CorridorSegmentFragment(
        int Index,
        int RouteIndex,
        int PolylineIndex,
        int SegmentIndex,
        string LineId,
        DisplayLineFamily Family,
        string FamilyKey,
        string FamilyDisplayName,
        string? FamilyColor,
        SvgPoint Start,
        SvgPoint End,
        SvgPoint Direction,
        SvgPoint Normal,
        double Length,
        bool IsEligible,
        List<SvgPoint> StationPoints);

    private readonly record struct CorridorAssignment(
        string CorridorId,
        int MemberCount,
        double OffsetIndex,
        double OffsetPx,
        SvgPoint BaseDirection);

    private readonly record struct PathPointRenderDiagnostics(
        List<SvgPoint> Points,
        double ReductionRatio,
        double MaxSegmentLength,
        int SuspiciousJumpCount,
        double SuspiciousJumpThreshold,
        double EffectiveSimplificationTolerance);

    private readonly record struct PathPointMetrics(
        double TotalLength,
        double MedianSegmentLength,
        double MaxSegmentLength,
        double SuspiciousJumpThreshold,
        int SuspiciousJumpCount,
        List<int> SuspiciousJumpStartIndices);

    private readonly record struct SourceStationPoint(string Id, double X, double Z);

    private readonly record struct SourceCoordinate(double X, double Z);

    private readonly record struct SvgPoint(double X, double Y);

    private readonly record struct LegendLine(DisplayLineFamily Family, int Index, int? LineNumber);

    private readonly record struct RouteBadgePlacement(
        double X,
        double Y,
        double Width,
        double Height,
        string Text,
        SvgRect Box,
        double Score);

    private readonly record struct LabelRequest(
        MetroStation Station,
        string Text,
        SvgPoint Point,
        double StationRadius,
        int Priority,
        int Index,
        bool IsProtected,
        bool CanHideWhenCrowded,
        bool IsInterchange,
        bool IsTerminal,
        bool IsGenericName);

    private readonly record struct LabelCandidate(
        string PositionName,
        double X,
        double Y,
        string Anchor,
        SvgRect Box);

    private readonly record struct PlacedLabel(
        string StationId,
        string Text,
        double X,
        double Y,
        string Anchor,
        string PositionName,
        SvgRect Box,
        int Priority,
        int Index,
        double Score,
        double LabelOverlapArea,
        bool IsInterchange,
        bool IsTerminal,
        bool IsGenericName,
        bool OverrideApplied = false);

    private readonly record struct SvgRect(double Left, double Top, double Right, double Bottom)
    {
        public static SvgRect FromCenter(double x, double y, double width, double height)
        {
            return new SvgRect(x - width / 2, y - height / 2, x + width / 2, y + height / 2);
        }

        public double OverlapArea(SvgRect other)
        {
            double width = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left));
            double height = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top));
            return width * height;
        }

        public double OutsideArea(SvgRect bounds)
        {
            double width = Math.Max(0, Right - Left);
            double height = Math.Max(0, Bottom - Top);
            double insideWidth = Math.Max(0, Math.Min(Right, bounds.Right) - Math.Max(Left, bounds.Left));
            double insideHeight = Math.Max(0, Math.Min(Bottom, bounds.Bottom) - Math.Max(Top, bounds.Top));
            return width * height - insideWidth * insideHeight;
        }

        public SvgRect Inflate(double horizontal, double vertical)
        {
            return new SvgRect(Left - horizontal, Top - vertical, Right + horizontal, Bottom + vertical);
        }
    }
}
