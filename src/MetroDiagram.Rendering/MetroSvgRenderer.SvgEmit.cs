using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
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

    // Inserts user bend points into a route's drawn point list: for any edge (A,B) with a
    // bend override, the auto-routed sub-path between the vertices at A and B is replaced with
    // A -> bend -> B. Returns the input list unchanged (same reference) when there are no bends,
    // so unedited renders stay byte-for-byte identical.
    private static List<SvgPoint> InsertUserBends(
        List<SvgPoint> points,
        Dictionary<string, SvgPoint> stationPoints,
        LayoutOverrideDocument? overrides)
    {
        if (overrides is null || overrides.Bends.Count == 0 || points.Count < 2)
        {
            return points;
        }

        List<SvgPoint> result = new(points.Count + overrides.Bends.Count);
        int i = 0;
        while (i < points.Count)
        {
            result.Add(points[i]);
            string? stationA = NearestStationId(points[i], stationPoints);
            bool handled = false;
            if (stationA is not null && i + 1 < points.Count)
            {
                int j = i + 1;
                while (j < points.Count && NearestStationId(points[j], stationPoints) is null)
                {
                    j++;
                }

                if (j < points.Count
                    && NearestStationId(points[j], stationPoints) is string stationB
                    && overrides.Bends.TryGetValue(LayoutOverrideDocument.BendEdgeKey(stationA, stationB), out BendLayoutOverride? bend)
                    && bend.Enabled)
                {
                    result.Add(new SvgPoint(bend.X, bend.Y));
                    result.Add(points[j]);
                    i = j + 1;
                    handled = true;
                }
            }

            if (!handled)
            {
                i++;
            }
        }

        return result;
    }

    private static string? NearestStationId(SvgPoint point, Dictionary<string, SvgPoint> stationPoints)
    {
        const double toleranceSquared = 0.75 * 0.75;
        foreach ((string id, SvgPoint candidate) in stationPoints)
        {
            double dx = candidate.X - point.X;
            double dy = candidate.Y - point.Y;
            if (dx * dx + dy * dy <= toleranceSquared)
            {
                return id;
            }
        }

        return null;
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

        Dictionary<ParallelEdgeKey, ParallelSharedEdge> parallelPlan = options.LayoutMode == SvgLayoutMode.SchematicAnneal
            ? BuildParallelCorridorPlan(renderRoutes, options)
            : [];

        foreach (RenderRoute renderRoute in renderRoutes)
        {
            MetroLine line = renderRoute.Line;
            DisplayLineFamily family = renderRoute.Family;
            RoutePointSet routePointSet = renderRoute.RoutePointSet;
            List<RoutePolyline> routePolylines = routePointSet.Polylines;

            for (int i = 0; i < routePolylines.Count; i++)
            {
                RoutePolyline polyline = routePolylines[i];
                List<SvgPoint> drawnPoints = parallelPlan.Count > 0
                    ? OffsetRouteForParallelCorridor(polyline.Points, family.FamilyKey, parallelPlan)
                    : polyline.Points;
                drawnPoints = InsertUserBends(drawnPoints, stationPoints, options.LayoutOverrides);
                string pointList = string.Join(" ", drawnPoints.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
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

        List<DisplayLineFamily> badgeFamilies = renderRoutes.Select(route => route.Family).DistinctBy(family => family.FamilyKey).ToList();
        List<SvgRect> occupiedBoxes = BuildTransitMapBadgeOccupiedBoxes(stations, badgeFamilies, stationPoints, terminalStationIds, options, hasLegend);
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
        List<DisplayLineFamily> displayFamilies,
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

        foreach (PlacedLabel label in BuildPlacedLabels(stations, displayFamilies, stationPoints, terminalStationIds, options, hasLegend))
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

    private static string GetLayoutModeName(SvgLayoutMode layoutMode)
    {
        return layoutMode switch
        {
            SvgLayoutMode.SchematicV2 => "schematic-v2",
            SvgLayoutMode.SchematicMap => "schematic-map",
            SvgLayoutMode.SchematicAnneal => "schematic-anneal",
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
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, SvgPoint> stationPoints,
        Dictionary<string, StationRenderAnchor> stationAnchors,
        Dictionary<string, SchematicStationAdjustment> schematicStationAdjustments,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options,
        bool hasLegend)
    {
        List<PlacedLabel> placedLabels = BuildPlacedLabels(stations, displayFamilies, stationPoints, terminalStationIds, options, hasLegend);
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

    // Straight stop-to-stop segments per display family, deduplicated. Used as
    // label obstacles so labels prefer positions clear of the drawn routes. For
    // geographic path-point rendering this is an approximation of the drawn curve.
    private static List<(SvgPoint A, SvgPoint B)> BuildLabelRouteObstacles(
        List<DisplayLineFamily> displayFamilies,
        Dictionary<string, SvgPoint> stationPoints)
    {
        List<(SvgPoint, SvgPoint)> segments = [];
        HashSet<(long, long, long, long)> seen = [];
        foreach (DisplayLineFamily family in displayFamilies)
        {
            SvgPoint? previous = null;
            foreach (string stopId in family.PrimaryLine.Stops ?? [])
            {
                if (string.IsNullOrWhiteSpace(stopId) || !stationPoints.TryGetValue(stopId, out SvgPoint point))
                {
                    continue;
                }

                if (previous is SvgPoint from && Distance(from, point) > 0.001)
                {
                    ParallelEdgeKey key = CreateParallelEdgeKey(from, point);
                    if (seen.Add((key.X0, key.Y0, key.X1, key.Y1)))
                    {
                        segments.Add((from, point));
                    }
                }

                previous = point;
            }
        }

        return segments;
    }

    // Length of the segment portion inside the rectangle (Liang-Barsky clip).
    private static double SegmentLengthInsideRect(SvgPoint a, SvgPoint b, SvgRect rect)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double t0 = 0;
        double t1 = 1;

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 0.000001)
            {
                return q >= 0;
            }

            double r = q / p;
            if (p < 0)
            {
                if (r > t1)
                {
                    return false;
                }

                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                if (r < t1)
                {
                    t1 = r;
                }
            }

            return true;
        }

        if (!Clip(-dx, a.X - rect.Left) || !Clip(dx, rect.Right - a.X)
            || !Clip(-dy, a.Y - rect.Top) || !Clip(dy, rect.Bottom - a.Y))
        {
            return 0;
        }

        return t1 > t0 ? Math.Sqrt(dx * dx + dy * dy) * (t1 - t0) : 0;
    }

    private static List<PlacedLabel> BuildPlacedLabels(
        List<MetroStation> stations,
        List<DisplayLineFamily> displayFamilies,
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
        List<(SvgPoint A, SvgPoint B)> routeObstacles = BuildLabelRouteObstacles(displayFamilies, stationPoints);
        List<PlacedLabel> placedLabels = [];
        List<SvgRect> placedLabelBoxes = [];

        foreach (LabelRequest request in labelRequests
            .OrderByDescending(request => request.Priority)
            .ThenBy(request => request.Index))
        {
            PlacedLabel label = ChooseLabelPlacement(request, options, placedLabelBoxes, stationObstacles, routeObstacles, allowedBounds);
            bool labelOverrideApplied = TryApplyLabelOverride(label, request, options, options.LayoutOverrides, out PlacedLabel overriddenLabel);
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
        LabelRequest request,
        SvgRenderOptions options,
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

        // A Position override matching a candidate slot (right/left/top/bottom/
        // diagonals) re-bases the label onto that slot, including its anchor and
        // box, so choosing a side actually moves the label; X/Y/Dx/Dy apply on
        // top. Any other name (e.g. the Viewer's "manual" tag) is kept as pure
        // metadata, preserving the historical behavior.
        bool positionApplied = false;
        if (!string.IsNullOrWhiteSpace(labelOverride.Position))
        {
            string requestedPosition = labelOverride.Position!.Trim();
            bool matchedCandidate = false;
            foreach (LabelCandidate candidate in CreateLabelCandidates(request, options))
            {
                if (string.Equals(candidate.PositionName, requestedPosition, StringComparison.OrdinalIgnoreCase))
                {
                    matchedCandidate = true;
                    positionApplied = !string.Equals(candidate.PositionName, label.PositionName, StringComparison.Ordinal);
                    label = label with
                    {
                        X = candidate.X,
                        Y = candidate.Y,
                        Anchor = candidate.Anchor,
                        PositionName = candidate.PositionName,
                        Box = candidate.Box
                    };
                    break;
                }
            }

            if (!matchedCandidate)
            {
                positionApplied = !string.Equals(requestedPosition, label.PositionName, StringComparison.Ordinal);
                label = label with { PositionName = requestedPosition };
            }
        }

        double adjustedX = labelOverride.X ?? label.X;
        double adjustedY = labelOverride.Y ?? label.Y;
        adjustedX += labelOverride.Dx ?? 0;
        adjustedY += labelOverride.Dy ?? 0;
        double deltaX = adjustedX - label.X;
        double deltaY = adjustedY - label.Y;
        overriddenLabel = label with
        {
            X = adjustedX,
            Y = adjustedY,
            Box = new SvgRect(
                label.Box.Left + deltaX,
                label.Box.Top + deltaY,
                label.Box.Right + deltaX,
                label.Box.Bottom + deltaY),
            OverrideApplied = true
        };
        return positionApplied
            || Math.Abs(deltaX) > 0.001
            || Math.Abs(deltaY) > 0.001;
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
        List<(SvgPoint A, SvgPoint B)> routeObstacles,
        SvgRect allowedBounds)
    {
        List<LabelCandidate> candidates = CreateLabelCandidates(request, options);
        LabelCandidate best = candidates[0];
        double bestScore = double.MaxValue;
        double bestLabelOverlapArea = 0;
        double routeStrokeWidth = Math.Max(1, options.LineWidth);

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

            // Route lines under the label: penalize by covered area (clipped
            // length x stroke width). Cheaper than a label-label collision but
            // worth flipping sides for when a clear side exists.
            foreach ((SvgPoint a, SvgPoint b) in routeObstacles)
            {
                score += SegmentLengthInsideRect(a, b, candidate.Box) * routeStrokeWidth * 6;
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
        return options.LayoutMode is SvgLayoutMode.SchematicMap or SvgLayoutMode.SchematicAnneal;
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

}
