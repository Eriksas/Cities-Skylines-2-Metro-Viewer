using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
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

        return new SvgRenderResult(svg.ToString(), warnings, ComputeRenderLayoutScore(displayFamilies, stationPoints, options));
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

        if (options.LayoutMode == SvgLayoutMode.SchematicAnneal)
        {
            SchematicLayoutResult annealLayout = ApplySchematicAnnealLayout(points, displayFamilies, options, reserveLegendSpace, warnings);
            Dictionary<string, SvgPoint> centeredPoints = RecenterPointsToBounds(annealLayout.Points, options, reserveLegendSpace);
            return new RenderGeometry(centeredPoints, projector, annealLayout.Adjustments, [], null, null);
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
            Dictionary<string, SvgPoint> centeredV2Points = RecenterPointsToBounds(targetLayout.Points, options, reserveLegendSpace);
            return new RenderGeometry(centeredV2Points, projector, targetLayout.Adjustments, targetLayout.DenseStationPairs, targetLayout.RouteGuideByFamily, targetLayout.RouteGuideMetadataByFamily);
        }

        return new RenderGeometry(points, projector, [], [], null, null);
    }

    // Schematic layouts relocate stations after projection, so their final
    // bounding box can sit off-center in the canvas. Recenter with a pure
    // translation (angles, spacing, and all layout metrics are preserved) so
    // the map's margins are balanced instead of hugging one edge. Aspect-ratio
    // whitespace that cannot be filled without distortion is left symmetric.
    private static Dictionary<string, SvgPoint> RecenterPointsToBounds(
        Dictionary<string, SvgPoint> points,
        SvgRenderOptions options,
        bool reserveLegendSpace)
    {
        if (points.Count == 0)
        {
            return points;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        foreach (SvgPoint point in points.Values)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);
        double dx = (bounds.Left + bounds.Right) / 2 - (minX + maxX) / 2;
        double dy = (bounds.Top + bounds.Bottom) / 2 - (minY + maxY) / 2;

        // Only recenter along an axis where the content actually fits, and never
        // shift so far that a station leaves the drawing bounds.
        dx = maxX - minX <= bounds.Right - bounds.Left ? Math.Clamp(dx, bounds.Left - minX, bounds.Right - maxX) : 0;
        dy = maxY - minY <= bounds.Bottom - bounds.Top ? Math.Clamp(dy, bounds.Top - minY, bounds.Bottom - maxY) : 0;

        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return points;
        }

        Dictionary<string, SvgPoint> shifted = new(points.Count, StringComparer.Ordinal);
        foreach ((string id, SvgPoint point) in points)
        {
            shifted[id] = new SvgPoint(point.X + dx, point.Y + dy);
        }

        return shifted;
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
        if (options.LayoutMode is not (SvgLayoutMode.SchematicMap or SvgLayoutMode.SchematicAnneal))
        {
            return options;
        }

        return new SvgRenderOptions
        {
            LayoutMode = options.LayoutMode,
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
                null);
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

}
