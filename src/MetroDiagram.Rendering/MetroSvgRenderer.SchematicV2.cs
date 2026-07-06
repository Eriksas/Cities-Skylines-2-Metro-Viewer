using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
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

}
