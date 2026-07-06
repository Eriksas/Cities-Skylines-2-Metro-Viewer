using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Experimental global layout mode. Instead of the schematic-map stack of
    // greedy local repair passes, schematic-anneal minimizes ONE global cost
    // (octilinearity, edge-length uniformity, bends, crossings, clearance,
    // plus a light geographic anchor) with grid-constrained simulated
    // annealing. Deterministic: fixed seed, fixed schedule, ordered topology.
    private const int AnnealMaxStations = 500;
    private const ulong AnnealSeed = 0x20260706C52D1A2Bul;

    private static SchematicLayoutResult ApplySchematicAnnealLayout(
        Dictionary<string, SvgPoint> geographicPoints,
        List<DisplayLineFamily> displayFamilies,
        SvgRenderOptions options,
        bool reserveLegendSpace,
        List<string> warnings)
    {
        double grid = Math.Max(8, options.GridSize);
        SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);

        Dictionary<string, SvgPoint> snapped = new(StringComparer.Ordinal);
        foreach ((string stationId, SvgPoint point) in geographicPoints)
        {
            snapped[stationId] = SnapPointToGrid(point, grid, bounds);
        }

        SchematicLayoutTopology topology = BuildSchematicLayoutTopology(displayFamilies, snapped);
        if (topology.Edges.Length == 0)
        {
            warnings.Add("Schematic-anneal skipped: no usable route edges.");
            return new SchematicLayoutResult(snapped, [], [], [], []);
        }

        if (topology.StationIds.Length > AnnealMaxStations)
        {
            warnings.Add($"Schematic-anneal skipped: {topology.StationIds.Length} stations exceed the {AnnealMaxStations}-station limit; grid-snapped geographic layout used instead.");
            return new SchematicLayoutResult(snapped, [], [], [], []);
        }

        SvgPoint[] anchors = BuildLayoutPositions(topology, snapped);
        SvgPoint[] positions = (SvgPoint[])anchors.Clone();
        double preferredSpacing = ResolveLayoutPreferredSpacing(topology, positions, options);
        double minimumSpacing = Math.Min(ResolveLayoutMinimumSpacing(options), preferredSpacing);
        double clearance = ResolveLayoutClearance(options);

        double initialCost = ComputeLayoutQualityCost(topology, positions, preferredSpacing, clearance);
        int initialCrossings = CountLayoutCrossings(positions, topology.Edges);

        AnnealState state = new(topology, positions, anchors, preferredSpacing, minimumSpacing, clearance, grid, bounds);
        (int attempts, int accepted) = RunAnnealSchedule(state, initialCost);

        double finalCost = ComputeLayoutQualityCost(topology, state.BestPositions, preferredSpacing, clearance);
        int finalCrossings = CountLayoutCrossings(state.BestPositions, topology.Edges);
        warnings.Add(
            $"Schematic-anneal audit: quality cost {Format(initialCost)} -> {Format(finalCost)}; crossings {initialCrossings} -> {finalCrossings}; accepted {accepted}/{attempts} moves.");

        Dictionary<string, SvgPoint> result = new(StringComparer.Ordinal);
        foreach ((string stationId, SvgPoint point) in geographicPoints)
        {
            result[stationId] = point;
        }

        for (int i = 0; i < topology.StationIds.Length; i++)
        {
            result[topology.StationIds[i]] = state.BestPositions[i];
        }

        return new SchematicLayoutResult(
            result,
            BuildSchematicStationAdjustments(geographicPoints, result, "schematic-anneal"),
            [],
            [],
            []);
    }

    private sealed class AnnealState(
        SchematicLayoutTopology topology,
        SvgPoint[] positions,
        SvgPoint[] anchors,
        double preferredSpacing,
        double minimumSpacing,
        double clearance,
        double grid,
        SvgRect bounds)
    {
        public SchematicLayoutTopology Topology { get; } = topology;

        public SvgPoint[] Positions { get; } = positions;

        public SvgPoint[] BestPositions { get; set; } = (SvgPoint[])positions.Clone();

        public SvgPoint[] Anchors { get; } = anchors;

        public double PreferredSpacing { get; } = preferredSpacing;

        public double MinimumSpacing { get; } = minimumSpacing;

        public double Clearance { get; } = clearance;

        public double Grid { get; } = grid;

        public SvgRect Bounds { get; } = bounds;

        // Bend neighbors per station: (previousIndex, stationIndex, nextIndex)
        // triples whose bend cost depends on the station's position.
        public List<(int Previous, int Station, int Next)>[] BendTriples { get; } = BuildBendTriples(topology);

        private static List<(int, int, int)>[] BuildBendTriples(SchematicLayoutTopology topology)
        {
            List<(int, int, int)>[] triples = new List<(int, int, int)>[topology.StationIds.Length];
            for (int i = 0; i < triples.Length; i++)
            {
                triples[i] = [];
            }

            foreach (int[] route in topology.Routes)
            {
                for (int i = 1; i < route.Length - 1; i++)
                {
                    (int previous, int station, int next) = (route[i - 1], route[i], route[i + 1]);
                    triples[previous].Add((previous, station, next));
                    triples[station].Add((previous, station, next));
                    triples[next].Add((previous, station, next));
                }
            }

            for (int i = 0; i < triples.Length; i++)
            {
                triples[i] = triples[i].Distinct().ToList();
            }

            return triples;
        }
    }

    private static (int Attempts, int Accepted) RunAnnealSchedule(AnnealState state, double initialCost)
    {
        int stationCount = state.Topology.StationIds.Length;
        int attempts = Math.Clamp(stationCount * 600, 30_000, 240_000);
        double startTemperature = Math.Max(0.5, initialCost / Math.Max(1, stationCount) * 0.6);
        double endTemperature = startTemperature / 200;
        double cooling = Math.Pow(endTemperature / startTemperature, 1.0 / attempts);

        ulong rng = AnnealSeed;
        double temperature = startTemperature;
        double currentCost = initialCost;
        double bestCost = initialCost;
        int accepted = 0;

        for (int attempt = 0; attempt < attempts; attempt++, temperature *= cooling)
        {
            int station = (int)(NextRandom(ref rng) % (ulong)stationCount);
            int range = temperature > startTemperature * 0.15 ? 2 : 1;
            int dx = (int)(NextRandom(ref rng) % (ulong)(2 * range + 1)) - range;
            int dy = (int)(NextRandom(ref rng) % (ulong)(2 * range + 1)) - range;
            if (dx == 0 && dy == 0)
            {
                continue;
            }

            SvgPoint oldPoint = state.Positions[station];
            SvgPoint candidate = SnapPointToGrid(
                new SvgPoint(oldPoint.X + dx * state.Grid, oldPoint.Y + dy * state.Grid),
                state.Grid,
                state.Bounds);
            if (Distance(candidate, oldPoint) < 0.001 || ViolatesMinimumSpacing(state, station, candidate))
            {
                continue;
            }

            double delta = LocalCostDelta(state, station, candidate);
            bool accept = delta <= 0 || NextRandomDouble(ref rng) < Math.Exp(-delta / Math.Max(temperature, 0.000001));
            if (!accept)
            {
                continue;
            }

            state.Positions[station] = candidate;
            currentCost += delta;
            accepted++;

            if (currentCost < bestCost - 0.000001)
            {
                bestCost = currentCost;
                Array.Copy(state.Positions, state.BestPositions, state.Positions.Length);
            }
        }

        return (attempts, accepted);
    }

    private static bool ViolatesMinimumSpacing(AnnealState state, int station, SvgPoint candidate)
    {
        for (int other = 0; other < state.Positions.Length; other++)
        {
            if (other != station && Distance(state.Positions[other], candidate) < state.MinimumSpacing)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cost change from moving one station: only its incident edges, the bend
    /// triples that reference it, crossings/clearance involving its incident
    /// edges, its own clearance against all edges, and its geographic anchor.
    /// </summary>
    private static double LocalCostDelta(AnnealState state, int station, SvgPoint candidate)
    {
        SvgPoint oldPoint = state.Positions[station];
        double before = LocalCost(state, station);
        state.Positions[station] = candidate;
        double after = LocalCost(state, station);
        state.Positions[station] = oldPoint;
        return after - before;
    }

    private static double LocalCost(AnnealState state, int station)
    {
        SvgPoint[] positions = state.Positions;
        LayoutEdge[] edges = state.Topology.Edges;
        int[] incident = state.Topology.IncidentEdgesByStation[station];
        double cost = 0;

        foreach (int edgeIndex in incident)
        {
            LayoutEdge edge = edges[edgeIndex];
            cost += EdgeUnitCost(positions[edge.A], positions[edge.B], state.PreferredSpacing);

            // Crossings and clearance for incident edges against everything else.
            for (int other = 0; other < edges.Length; other++)
            {
                if (other == edgeIndex)
                {
                    continue;
                }

                if (EdgesCross(positions, edge, edges[other]))
                {
                    // Halve pair counts shared between two incident edges so the
                    // before/after difference stays symmetric.
                    bool otherIsIncident = edges[other].A == station || edges[other].B == station;
                    cost += LayoutCostWeights.Crossing * (otherIsIncident ? 0.5 : 1);
                }
            }

            for (int other = 0; other < positions.Length; other++)
            {
                if (other == edge.A || other == edge.B || other == station)
                {
                    continue;
                }

                if (DistancePointToSegment(positions[other], positions[edge.A], positions[edge.B]) < state.Clearance)
                {
                    cost += LayoutCostWeights.Clearance;
                }
            }
        }

        foreach ((int previous, int bendStation, int next) in state.BendTriples[station])
        {
            cost += BendUnitCost(BendTurnDegrees(positions[previous], positions[bendStation], positions[next]));
        }

        // Moved station's own clearance against non-incident edges.
        foreach (LayoutEdge edge in edges)
        {
            if (edge.A == station || edge.B == station)
            {
                continue;
            }

            if (DistancePointToSegment(positions[station], positions[edge.A], positions[edge.B]) < state.Clearance)
            {
                cost += LayoutCostWeights.Clearance;
            }
        }

        double anchorDistance = Distance(positions[station], state.Anchors[station]) / state.Grid;
        cost += anchorDistance * anchorDistance * LayoutCostWeights.GeographicAnchor;
        return cost;
    }

    private static ulong NextRandom(ref ulong state)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 0x2545F4914F6CDD1Dul;
    }

    private static double NextRandomDouble(ref ulong state)
    {
        return (NextRandom(ref state) >> 11) * (1.0 / (1ul << 53));
    }
}
