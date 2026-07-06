using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
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

}
