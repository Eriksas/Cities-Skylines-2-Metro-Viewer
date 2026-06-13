using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed record CanonicalSchematicNetwork(
    IReadOnlyDictionary<string, CanonicalStationNode> Stations,
    IReadOnlyList<CanonicalServiceFamily> Families,
    IReadOnlyList<CanonicalAdjacencyEdge> AdjacencyEdges,
    IReadOnlyList<CanonicalCorridorHint> CorridorHints,
    IReadOnlyList<CanonicalInterchangeGroup> InterchangeGroups);

public sealed record CanonicalStationNode(
    string StationId,
    string Name,
    double X,
    double Z,
    bool IsInterchange,
    IReadOnlyList<string> FamilyKeys);

public sealed record CanonicalServiceFamily(
    string FamilyKey,
    string DisplayName,
    string? Color,
    string CanonicalLineId,
    IReadOnlyList<string> CanonicalStops,
    IReadOnlyList<CanonicalServiceVariant> Variants,
    bool HasColorMismatch,
    bool HasExpressService);

public sealed record CanonicalServiceVariant(
    string LineId,
    string OriginalName,
    string VariantName,
    IReadOnlyList<string> Stops,
    int StopCount,
    int PathPointCount,
    double PathLength,
    bool IsCanonical,
    bool IsExpressService);

public sealed record CanonicalAdjacencyEdge(
    string EdgeKey,
    string StartStationId,
    string EndStationId,
    string FamilyKey,
    string LineId,
    int SequenceIndex,
    bool IsCanonicalRouteEdge);

public sealed record CanonicalCorridorHint(
    string CorridorKey,
    string Source,
    IReadOnlyList<string> FamilyKeys,
    IReadOnlyList<string> StationIds,
    int EdgeCount,
    double ApproximateSharedLength,
    double AverageDistance,
    double MaxDistance,
    double Confidence);

public sealed record CanonicalInterchangeGroup(
    string StationId,
    string StationName,
    IReadOnlyList<string> FamilyKeys);

public static class CanonicalSchematicNetworkBuilder
{
    private const double GeometrySharedCorridorDistance = 80;
    private const double GeometrySharedCorridorMinimumLength = 220;

    public static CanonicalSchematicNetwork Build(
        MetroExportDocument document,
        bool enableServiceFamilyMerge = true)
    {
        List<MetroStation> stations = document.Network?.Stations ?? [];
        List<MetroLine> lines = document.Network?.Lines ?? [];
        Dictionary<string, MetroStation> stationsById = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .GroupBy(station => station.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        List<DisplayLineFamily> displayFamilies = DisplayLineFamilyResolver.Resolve(lines, stationsById, enableServiceFamilyMerge);
        Dictionary<string, CanonicalServiceFamily> familyByKey = displayFamilies
            .Select(family => CreateFamily(family, stationsById))
            .ToDictionary(family => family.FamilyKey, StringComparer.Ordinal);

        List<CanonicalAdjacencyEdge> adjacencyEdges = BuildAdjacencyEdges(familyByKey.Values, stationsById);
        List<CanonicalCorridorHint> corridorHints = [];
        corridorHints.AddRange(BuildExactSharedEdgeHints(adjacencyEdges));
        corridorHints.AddRange(BuildGeometryCorridorHints(displayFamilies));

        IReadOnlyDictionary<string, CanonicalStationNode> stationNodes = BuildStationNodes(stationsById, familyByKey.Values);
        List<CanonicalInterchangeGroup> interchangeGroups = BuildInterchangeGroups(stationNodes);

        return new CanonicalSchematicNetwork(
            stationNodes,
            familyByKey.Values.OrderBy(family => family.DisplayName, StringComparer.CurrentCulture).ToList(),
            adjacencyEdges,
            corridorHints,
            interchangeGroups);
    }

    private static CanonicalServiceFamily CreateFamily(
        DisplayLineFamily family,
        Dictionary<string, MetroStation> stationsById)
    {
        DisplayServiceVariant canonicalVariant = SelectCanonicalVariant(family, stationsById);
        List<CanonicalServiceVariant> variants = family.Variants
            .Select(variant => new CanonicalServiceVariant(
                variant.LineId,
                variant.OriginalName,
                variant.VariantName,
                variant.Stops,
                variant.StopCount,
                variant.PathPointCount,
                variant.PathLength,
                string.Equals(variant.LineId, canonicalVariant.LineId, StringComparison.Ordinal),
                IsExpressServiceVariant(variant)))
            .ToList();

        return new CanonicalServiceFamily(
            family.FamilyKey,
            family.DisplayName,
            family.Color,
            canonicalVariant.LineId,
            canonicalVariant.Stops,
            variants,
            family.HasColorMismatch,
            variants.Any(variant => variant.IsExpressService));
    }

    private static DisplayServiceVariant SelectCanonicalVariant(
        DisplayLineFamily family,
        Dictionary<string, MetroStation> stationsById)
    {
        return family.Variants
            .OrderByDescending(variant => variant.Stops.Count(stationsById.ContainsKey))
            .ThenByDescending(variant => variant.PathLength)
            .ThenBy(variant => IsExpressServiceVariant(variant) ? 1 : 0)
            .ThenBy(variant => IsCanonicalNameMatch(family, variant) ? 0 : 1)
            .ThenBy(variant => variant.OriginalName, StringComparer.CurrentCulture)
            .First();
    }

    private static bool IsCanonicalNameMatch(DisplayLineFamily family, DisplayServiceVariant variant)
    {
        return string.Equals(family.DisplayName, variant.OriginalName, StringComparison.CurrentCulture)
            || string.Equals(family.FamilyKey, variant.OriginalName, StringComparison.CurrentCulture);
    }

    private static IReadOnlyDictionary<string, CanonicalStationNode> BuildStationNodes(
        Dictionary<string, MetroStation> stationsById,
        IEnumerable<CanonicalServiceFamily> families)
    {
        Dictionary<string, SortedSet<string>> familyKeysByStation = new(StringComparer.Ordinal);
        foreach (CanonicalServiceFamily family in families)
        {
            foreach (CanonicalServiceVariant variant in family.Variants)
            {
                foreach (string stationId in variant.Stops)
                {
                    if (!stationsById.ContainsKey(stationId))
                    {
                        continue;
                    }

                    if (!familyKeysByStation.TryGetValue(stationId, out SortedSet<string>? familyKeys))
                    {
                        familyKeys = new SortedSet<string>(StringComparer.CurrentCulture);
                        familyKeysByStation[stationId] = familyKeys;
                    }

                    familyKeys.Add(family.FamilyKey);
                }
            }
        }

        return stationsById
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    MetroStation station = pair.Value;
                    IReadOnlyList<string> familyKeys = familyKeysByStation.TryGetValue(pair.Key, out SortedSet<string>? keys)
                        ? keys.ToList()
                        : [];
                    return new CanonicalStationNode(
                        pair.Key,
                        string.IsNullOrWhiteSpace(station.Name) ? pair.Key : station.Name!,
                        station.Position?.X ?? 0,
                        station.Position?.Z ?? 0,
                        station.IsInterchange || familyKeys.Count > 1,
                        familyKeys);
                },
                StringComparer.Ordinal);
    }

    private static List<CanonicalAdjacencyEdge> BuildAdjacencyEdges(
        IEnumerable<CanonicalServiceFamily> families,
        Dictionary<string, MetroStation> stationsById)
    {
        List<CanonicalAdjacencyEdge> edges = [];
        foreach (CanonicalServiceFamily family in families)
        {
            foreach (CanonicalServiceVariant variant in family.Variants)
            {
                List<string> stops = variant.Stops
                    .Where(stationsById.ContainsKey)
                    .Where(stop => !string.IsNullOrWhiteSpace(stop))
                    .ToList();

                for (int i = 1; i < stops.Count; i++)
                {
                    if (string.Equals(stops[i - 1], stops[i], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    edges.Add(new CanonicalAdjacencyEdge(
                        CreateUndirectedEdgeKey(stops[i - 1], stops[i]),
                        stops[i - 1],
                        stops[i],
                        family.FamilyKey,
                        variant.LineId,
                        i - 1,
                        variant.IsCanonical));
                }
            }
        }

        return edges;
    }

    private static List<CanonicalCorridorHint> BuildExactSharedEdgeHints(List<CanonicalAdjacencyEdge> adjacencyEdges)
    {
        List<CanonicalCorridorHint> hints = [];
        foreach (IGrouping<string, CanonicalAdjacencyEdge> group in adjacencyEdges.GroupBy(edge => edge.EdgeKey, StringComparer.Ordinal))
        {
            List<string> familyKeys = group
                .Select(edge => edge.FamilyKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.CurrentCulture)
                .ToList();
            if (familyKeys.Count < 2)
            {
                continue;
            }

            CanonicalAdjacencyEdge first = group.First();
            hints.Add(new CanonicalCorridorHint(
                $"exact-edge:{group.Key}",
                "exact-shared-adjacent-stop-edge",
                familyKeys,
                [first.StartStationId, first.EndStationId],
                1,
                0,
                0,
                0,
                1));
        }

        return hints;
    }

    private static List<CanonicalCorridorHint> BuildGeometryCorridorHints(List<DisplayLineFamily> displayFamilies)
    {
        List<CanonicalCorridorHint> hints = [];
        for (int i = 0; i < displayFamilies.Count; i++)
        {
            for (int j = i + 1; j < displayFamilies.Count; j++)
            {
                DisplayLineFamily first = displayFamilies[i];
                DisplayLineFamily second = displayFamilies[j];
                if (TryBuildGeometryCorridorHint(first, second, out CanonicalCorridorHint? hint))
                {
                    hints.Add(hint);
                }
            }
        }

        return hints;
    }

    private static bool TryBuildGeometryCorridorHint(
        DisplayLineFamily first,
        DisplayLineFamily second,
        out CanonicalCorridorHint hint)
    {
        hint = null!;
        List<Point2> firstPoints = ToPathPoints(first.PrimaryLine.PathPoints);
        List<Point2> secondPoints = ToPathPoints(second.PrimaryLine.PathPoints);
        if (firstPoints.Count < 2 || secondPoints.Count < 2)
        {
            return false;
        }

        double sharedLength = 0;
        double weightedDistance = 0;
        double maxDistance = 0;
        int matchedSegments = 0;

        for (int i = 1; i < firstPoints.Count; i++)
        {
            Point2 a = firstPoints[i - 1];
            Point2 b = firstPoints[i];
            double segmentLength = Distance(a, b);
            if (segmentLength <= 0)
            {
                continue;
            }

            double distance = Math.Min(DistanceToPolyline(a, secondPoints), DistanceToPolyline(b, secondPoints));
            if (distance > GeometrySharedCorridorDistance)
            {
                continue;
            }

            sharedLength += segmentLength;
            weightedDistance += distance * segmentLength;
            maxDistance = Math.Max(maxDistance, distance);
            matchedSegments++;
        }

        if (matchedSegments == 0 || sharedLength < GeometrySharedCorridorMinimumLength)
        {
            return false;
        }

        double averageDistance = weightedDistance / sharedLength;
        double confidence = Math.Clamp(
            (sharedLength / (GeometrySharedCorridorMinimumLength * 3))
            * (1 - Math.Min(averageDistance / GeometrySharedCorridorDistance, 1) * 0.5),
            0,
            1);
        if (confidence < 0.35)
        {
            return false;
        }

        hint = new CanonicalCorridorHint(
            $"geometry:{first.FamilyKey}|{second.FamilyKey}",
            "geometry-pathPoints-corridor",
            [first.FamilyKey, second.FamilyKey],
            [],
            matchedSegments,
            sharedLength,
            averageDistance,
            maxDistance,
            confidence);
        return true;
    }

    private static List<Point2> ToPathPoints(List<MetroPathPoint>? pathPoints)
    {
        if (pathPoints is null)
        {
            return [];
        }

        return pathPoints
            .Select(point => new Point2(point.X, point.Z))
            .ToList();
    }

    private static double DistanceToPolyline(Point2 point, List<Point2> polyline)
    {
        double min = double.MaxValue;
        for (int i = 1; i < polyline.Count; i++)
        {
            min = Math.Min(min, DistanceToSegment(point, polyline[i - 1], polyline[i]));
        }

        return min;
    }

    private static double DistanceToSegment(Point2 point, Point2 start, Point2 end)
    {
        double dx = end.X - start.X;
        double dz = end.Z - start.Z;
        double lengthSquared = (dx * dx) + (dz * dz);
        if (lengthSquared <= 0)
        {
            return Distance(point, start);
        }

        double t = ((point.X - start.X) * dx + (point.Z - start.Z) * dz) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(point, new Point2(start.X + dx * t, start.Z + dz * t));
    }

    private static List<CanonicalInterchangeGroup> BuildInterchangeGroups(
        IReadOnlyDictionary<string, CanonicalStationNode> stations)
    {
        return stations.Values
            .Where(station => station.IsInterchange || station.FamilyKeys.Count > 1)
            .Select(station => new CanonicalInterchangeGroup(station.StationId, station.Name, station.FamilyKeys))
            .OrderBy(group => group.StationName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static string CreateUndirectedEdgeKey(string first, string second)
    {
        return string.Compare(first, second, StringComparison.Ordinal) <= 0
            ? $"{first}|{second}"
            : $"{second}|{first}";
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

    private static double Distance(Point2 first, Point2 second)
    {
        double dx = first.X - second.X;
        double dz = first.Z - second.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private readonly record struct Point2(double X, double Z);
}
