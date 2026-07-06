using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Single source of truth for layout-quality weighting. These weights are
    // tuned against the whole regression corpus (scripts/compare-schematic-layouts.ps1),
    // never against a single map.
    private static class LayoutCostWeights
    {
        public const double Octilinear = 4.5;
        public const double ShortEdge = 2.0;
        public const double LongEdge = 0.5;
        public const double Bend = 1.5;
        public const double Crossing = 8.0;
        public const double Clearance = 4.0;
        public const double GeographicAnchor = 0.05;
    }

    private const double OctilinearToleranceDegrees = 5.0;

    private readonly record struct LayoutEdge(int A, int B);

    private sealed record SchematicLayoutTopology(
        string[] StationIds,
        int[][] Routes,
        LayoutEdge[] Edges,
        int[][] IncidentEdgesByStation,
        // Bit i set = station/edge belongs to the i-th line (family). Used to
        // exempt same-line station/edge pairs from the clearance penalty: a line
        // doubling back through its own corridor is expected, not crowding.
        ulong[] StationLineMask,
        ulong[] EdgeLineMask);

    /// <summary>
    /// Clearance violation only when the station is closer than <paramref name="clearance"/>
    /// to a non-incident edge that shares no line with it. Same-line proximity
    /// (out-and-back corridors, shared trunks) is expected and not penalized.
    /// </summary>
    private static bool ViolatesClearance(SchematicLayoutTopology topology, SvgPoint[] positions, int station, int edgeIndex, double clearance)
    {
        LayoutEdge edge = topology.Edges[edgeIndex];
        if (edge.A == station || edge.B == station)
        {
            return false;
        }

        if ((topology.StationLineMask[station] & topology.EdgeLineMask[edgeIndex]) != 0)
        {
            return false;
        }

        return DistancePointToSegment(positions[station], positions[edge.A], positions[edge.B]) < clearance;
    }

    /// <summary>
    /// Builds the station/edge topology used by both scoring and annealing:
    /// one route per display family (its primary line stop sequence), edges as
    /// unique undirected consecutive stop pairs. Deterministic ordering.
    /// </summary>
    private static SchematicLayoutTopology BuildSchematicLayoutTopology(
        IReadOnlyList<DisplayLineFamily> families,
        IReadOnlyDictionary<string, SvgPoint> points)
    {
        string[] stationIds = points.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Dictionary<string, int> indexById = new(StringComparer.Ordinal);
        for (int i = 0; i < stationIds.Length; i++)
        {
            indexById[stationIds[i]] = i;
        }

        List<int[]> routes = [];
        HashSet<(int A, int B)> edgeSet = [];
        ulong[] stationLineMask = new ulong[stationIds.Length];
        Dictionary<(int A, int B), ulong> edgeLineMask = [];
        int routeIndex = 0;
        foreach (DisplayLineFamily family in families)
        {
            List<int> route = [];
            foreach (string stopId in family.PrimaryLine.Stops ?? [])
            {
                if (string.IsNullOrWhiteSpace(stopId) || !indexById.TryGetValue(stopId, out int index))
                {
                    continue;
                }

                if (route.Count > 0 && route[^1] == index)
                {
                    continue;
                }

                route.Add(index);
            }

            if (route.Count < 2)
            {
                continue;
            }

            // One mask bit per line; families beyond 64 share the top bit (rare,
            // and only weakens the same-line exemption for those extras).
            ulong lineBit = 1ul << Math.Min(routeIndex, 63);
            routeIndex++;
            routes.Add(route.ToArray());
            foreach (int index in route)
            {
                stationLineMask[index] |= lineBit;
            }

            for (int i = 1; i < route.Count; i++)
            {
                int a = Math.Min(route[i - 1], route[i]);
                int b = Math.Max(route[i - 1], route[i]);
                edgeSet.Add((a, b));
                edgeLineMask[(a, b)] = edgeLineMask.GetValueOrDefault((a, b)) | lineBit;
            }
        }

        LayoutEdge[] edges = edgeSet
            .OrderBy(edge => edge.A)
            .ThenBy(edge => edge.B)
            .Select(edge => new LayoutEdge(edge.A, edge.B))
            .ToArray();

        ulong[] edgeMask = edges.Select(edge => edgeLineMask[(edge.A, edge.B)]).ToArray();

        List<int>[] incident = new List<int>[stationIds.Length];
        for (int i = 0; i < incident.Length; i++)
        {
            incident[i] = [];
        }

        for (int e = 0; e < edges.Length; e++)
        {
            incident[edges[e].A].Add(e);
            incident[edges[e].B].Add(e);
        }

        return new SchematicLayoutTopology(
            stationIds,
            routes.ToArray(),
            edges,
            incident.Select(list => list.ToArray()).ToArray(),
            stationLineMask,
            edgeMask);
    }

    private static SvgPoint[] BuildLayoutPositions(SchematicLayoutTopology topology, IReadOnlyDictionary<string, SvgPoint> points)
    {
        SvgPoint[] positions = new SvgPoint[topology.StationIds.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = points[topology.StationIds[i]];
        }

        return positions;
    }

    /// <summary>Angular deviation of the edge direction from the nearest 45-degree multiple, in radians (0 .. PI/8).</summary>
    private static double OctilinearDeviationRadians(SvgPoint a, SvgPoint b)
    {
        double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        double step = Math.PI / 4;
        double remainder = angle % step;
        if (remainder < 0)
        {
            remainder += step;
        }

        return Math.Min(remainder, step - remainder);
    }

    private static double EdgeUnitCost(SvgPoint a, SvgPoint b, double preferredSpacing)
    {
        double length = Distance(a, b);
        if (length < 0.000001)
        {
            return 50;
        }

        double octilinearDeviation = OctilinearDeviationRadians(a, b) / (Math.PI / 8);
        double cost = octilinearDeviation * octilinearDeviation * LayoutCostWeights.Octilinear;

        double relative = (length - preferredSpacing) / preferredSpacing;
        if (relative < 0)
        {
            cost += relative * relative * LayoutCostWeights.ShortEdge;
        }
        else
        {
            cost += Math.Min(relative * relative, 4) * LayoutCostWeights.LongEdge;
        }

        return cost;
    }

    /// <summary>Turn angle at <paramref name="current"/> in degrees: 0 = straight through, 180 = full reversal.</summary>
    private static double BendTurnDegrees(SvgPoint previous, SvgPoint current, SvgPoint next)
    {
        SvgPoint incoming = new(current.X - previous.X, current.Y - previous.Y);
        SvgPoint outgoing = new(next.X - current.X, next.Y - current.Y);
        double incomingLength = Math.Sqrt(incoming.X * incoming.X + incoming.Y * incoming.Y);
        double outgoingLength = Math.Sqrt(outgoing.X * outgoing.X + outgoing.Y * outgoing.Y);
        if (incomingLength < 0.000001 || outgoingLength < 0.000001)
        {
            return 0;
        }

        double cosine = (incoming.X * outgoing.X + incoming.Y * outgoing.Y) / (incomingLength * outgoingLength);
        return Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI;
    }

    private static double BendUnitCost(double turnDegrees)
    {
        double normalized = turnDegrees / 90;
        return Math.Min(normalized * normalized, 4) * LayoutCostWeights.Bend;
    }

    /// <summary>True when the two edges properly cross away from any shared station endpoint.</summary>
    private static bool EdgesCross(SvgPoint[] positions, LayoutEdge first, LayoutEdge second)
    {
        if (first.A == second.A || first.A == second.B || first.B == second.A || first.B == second.B)
        {
            return false;
        }

        SvgPoint p1 = positions[first.A];
        SvgPoint p2 = positions[first.B];
        SvgPoint p3 = positions[second.A];
        SvgPoint p4 = positions[second.B];

        double d1 = Cross(p2.X - p1.X, p2.Y - p1.Y, p3.X - p1.X, p3.Y - p1.Y);
        double d2 = Cross(p2.X - p1.X, p2.Y - p1.Y, p4.X - p1.X, p4.Y - p1.Y);
        double d3 = Cross(p4.X - p3.X, p4.Y - p3.Y, p1.X - p3.X, p1.Y - p3.Y);
        double d4 = Cross(p4.X - p3.X, p4.Y - p3.Y, p2.X - p3.X, p2.Y - p3.Y);

        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    private static int CountLayoutCrossings(SvgPoint[] positions, LayoutEdge[] edges)
    {
        int crossings = 0;
        for (int i = 0; i < edges.Length; i++)
        {
            for (int j = i + 1; j < edges.Length; j++)
            {
                if (EdgesCross(positions, edges[i], edges[j]))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static int CountClearanceViolations(SchematicLayoutTopology topology, SvgPoint[] positions, double clearance)
    {
        int violations = 0;
        for (int station = 0; station < positions.Length; station++)
        {
            for (int edgeIndex = 0; edgeIndex < topology.Edges.Length; edgeIndex++)
            {
                if (ViolatesClearance(topology, positions, station, edgeIndex, clearance))
                {
                    violations++;
                }
            }
        }

        return violations;
    }

    private static int CountMinimumSpacingViolations(SvgPoint[] positions, double minimumSpacing)
    {
        int violations = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            for (int j = i + 1; j < positions.Length; j++)
            {
                if (Distance(positions[i], positions[j]) < minimumSpacing)
                {
                    violations++;
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Layout-quality cost shared by scoring and annealing. Excludes the
    /// geographic anchor term, which is a solver regularizer rather than an
    /// output-quality measure.
    /// </summary>
    private static double ComputeLayoutQualityCost(
        SchematicLayoutTopology topology,
        SvgPoint[] positions,
        double preferredSpacing,
        double clearance)
    {
        double cost = 0;
        foreach (LayoutEdge edge in topology.Edges)
        {
            cost += EdgeUnitCost(positions[edge.A], positions[edge.B], preferredSpacing);
        }

        foreach (int[] route in topology.Routes)
        {
            for (int i = 1; i < route.Length - 1; i++)
            {
                cost += BendUnitCost(BendTurnDegrees(positions[route[i - 1]], positions[route[i]], positions[route[i + 1]]));
            }
        }

        cost += CountLayoutCrossings(positions, topology.Edges) * LayoutCostWeights.Crossing;
        cost += CountClearanceViolations(topology, positions, clearance) * LayoutCostWeights.Clearance;
        return cost;
    }

    // Preferred spacing follows the map's own scale (median edge length) with
    // only a lower bound. An upper cap would put a fixed, unoptimizable "long
    // edge" penalty on every edge of sparse networks, drowning the real signal.
    private static double ResolveLayoutPreferredSpacing(SchematicLayoutTopology topology, SvgPoint[] positions, SvgRenderOptions options)
    {
        double grid = Math.Max(8, options.GridSize);
        if (options.SchematicMapPreferredStationSpacing > 0)
        {
            return Math.Max(options.SchematicMapPreferredStationSpacing, grid);
        }

        List<double> lengths = topology.Edges
            .Select(edge => Distance(positions[edge.A], positions[edge.B]))
            .Where(length => length > 0.001)
            .ToList();
        double median = Median(lengths);
        return Math.Max(median > 0 ? median : grid * 3, grid * 2);
    }

    private static double ResolveLayoutMinimumSpacing(SvgRenderOptions options)
    {
        return options.SchematicMinimumStationSpacing > 0
            ? options.SchematicMinimumStationSpacing
            : Math.Max(Math.Max(8, options.GridSize) * 1.35, options.LineWidth * 3.2);
    }

    private static double ResolveLayoutClearance(SvgRenderOptions options)
    {
        return Math.Max(options.LineWidth * 2.0, Math.Max(8, options.GridSize) * 0.45);
    }

    private static SchematicLayoutScore ComputeLayoutScore(
        SchematicLayoutTopology topology,
        SvgPoint[] positions,
        SvgRenderOptions options)
    {
        double preferredSpacing = ResolveLayoutPreferredSpacing(topology, positions, options);
        double minimumSpacing = ResolveLayoutMinimumSpacing(options);
        double clearance = ResolveLayoutClearance(options);

        int octilinearEdges = 0;
        double deviationDegreesSum = 0;
        List<double> lengths = [];
        foreach (LayoutEdge edge in topology.Edges)
        {
            double deviationDegrees = OctilinearDeviationRadians(positions[edge.A], positions[edge.B]) * 180 / Math.PI;
            deviationDegreesSum += deviationDegrees;
            if (deviationDegrees <= OctilinearToleranceDegrees)
            {
                octilinearEdges++;
            }

            lengths.Add(Distance(positions[edge.A], positions[edge.B]));
        }

        int bendCount = 0;
        double bendDegreesSum = 0;
        foreach (int[] route in topology.Routes)
        {
            for (int i = 1; i < route.Length - 1; i++)
            {
                double turn = BendTurnDegrees(positions[route[i - 1]], positions[route[i]], positions[route[i + 1]]);
                if (turn > 1)
                {
                    bendCount++;
                    bendDegreesSum += turn;
                }
            }
        }

        double meanLength = lengths.Count > 0 ? lengths.Average() : 0;
        double lengthCv = 0;
        if (lengths.Count > 0 && meanLength > 0.001)
        {
            double variance = lengths.Sum(length => (length - meanLength) * (length - meanLength)) / lengths.Count;
            lengthCv = Math.Sqrt(variance) / meanLength;
        }

        return new SchematicLayoutScore(
            topology.StationIds.Length,
            topology.Edges.Length,
            topology.Edges.Length > 0 ? octilinearEdges / (double)topology.Edges.Length : 1,
            topology.Edges.Length > 0 ? deviationDegreesSum / topology.Edges.Length : 0,
            lengthCv,
            bendCount,
            bendCount > 0 ? bendDegreesSum / bendCount : 0,
            CountLayoutCrossings(positions, topology.Edges),
            CountMinimumSpacingViolations(positions, minimumSpacing),
            CountClearanceViolations(topology, positions, clearance),
            ComputeLayoutQualityCost(topology, positions, preferredSpacing, clearance));
    }

    private static SchematicLayoutScore? ComputeRenderLayoutScore(
        IReadOnlyList<DisplayLineFamily> families,
        IReadOnlyDictionary<string, SvgPoint> points,
        SvgRenderOptions options)
    {
        if (points.Count < 2 || families.Count == 0)
        {
            return null;
        }

        SchematicLayoutTopology topology = BuildSchematicLayoutTopology(families, points);
        if (topology.Edges.Length == 0)
        {
            return null;
        }

        return ComputeLayoutScore(topology, BuildLayoutPositions(topology, points), options);
    }
}
