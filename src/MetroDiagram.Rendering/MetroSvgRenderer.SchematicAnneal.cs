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
        int polishSweeps = RunGreedyPolish(state);

        double finalCost = ComputeLayoutQualityCost(topology, state.BestPositions, preferredSpacing, clearance);
        int finalCrossings = CountLayoutCrossings(state.BestPositions, topology.Edges);
        warnings.Add(
            $"Schematic-anneal audit: quality cost {Format(initialCost)} -> {Format(finalCost)}; crossings {initialCrossings} -> {finalCrossings}; accepted {accepted}/{attempts} moves; polish sweeps {polishSweeps}.");

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

        // Cell size covers both proximity radii, so every clearance/spacing query
        // is answered by a one-cell rim around the queried bounding box.
        public AnnealSpatialIndex Index { get; } = new(
            topology,
            positions,
            bounds,
            Math.Max(1, Math.Max(minimumSpacing, clearance)));

        public List<int> StationScratch { get; } = [];

        public List<int> EdgeScratch { get; } = [];

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
            state.Index.OnStationMoved(station, state.Positions);
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

    /// <summary>
    /// Deterministic zero-temperature finisher over the annealer's best state:
    /// visit stations in index order, try every grid offset within range 2, and
    /// accept strictly improving moves of the SAME global cost until a full
    /// sweep changes nothing. Escapes shallow local minima the stochastic
    /// schedule can leave behind on small maps.
    /// </summary>
    private static int RunGreedyPolish(AnnealState state)
    {
        Array.Copy(state.BestPositions, state.Positions, state.Positions.Length);
        state.Index.Rebuild(state.Positions);

        const int maxSweeps = 60;
        int sweeps = 0;
        bool improvedInSweep = true;
        while (improvedInSweep && sweeps < maxSweeps)
        {
            improvedInSweep = false;
            sweeps++;
            for (int station = 0; station < state.Positions.Length; station++)
            {
                SvgPoint bestPoint = state.Positions[station];
                double bestDelta = -0.000001;
                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        SvgPoint current = state.Positions[station];
                        SvgPoint candidate = SnapPointToGrid(
                            new SvgPoint(current.X + dx * state.Grid, current.Y + dy * state.Grid),
                            state.Grid,
                            state.Bounds);
                        if (Distance(candidate, current) < 0.001 || ViolatesMinimumSpacing(state, station, candidate))
                        {
                            continue;
                        }

                        double delta = LocalCostDelta(state, station, candidate);
                        if (delta < bestDelta)
                        {
                            bestDelta = delta;
                            bestPoint = candidate;
                        }
                    }
                }

                if (bestDelta < -0.000001)
                {
                    state.Positions[station] = bestPoint;
                    state.Index.OnStationMoved(station, state.Positions);
                    improvedInSweep = true;
                }
            }
        }

        Array.Copy(state.Positions, state.BestPositions, state.Positions.Length);
        return sweeps;
    }

    private static bool ViolatesMinimumSpacing(AnnealState state, int station, SvgPoint candidate)
    {
        return state.Index.AnyStationWithinSpacing(candidate, station, state.Positions, state.MinimumSpacing);
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

            // Crossings and clearance for incident edges: the spatial index yields
            // a sorted superset of the relevant candidates, so the nonzero terms
            // are added in the same order as a full ascending scan.
            state.Index.CollectEdgesCrossingCandidates(positions[edge.A], positions[edge.B], state.EdgeScratch);
            foreach (int other in state.EdgeScratch)
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

            state.Index.CollectStationsNearSegment(positions[edge.A], positions[edge.B], state.StationScratch);
            foreach (int other in state.StationScratch)
            {
                if (other == edge.A || other == edge.B || other == station)
                {
                    continue;
                }

                if (ViolatesClearance(state.Topology, positions, other, edgeIndex, state.Clearance))
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
        state.Index.CollectEdgesNearPoint(positions[station], state.EdgeScratch);
        foreach (int edgeIndex in state.EdgeScratch)
        {
            if (ViolatesClearance(state.Topology, positions, station, edgeIndex, state.Clearance))
            {
                cost += LayoutCostWeights.Clearance;
            }
        }

        double anchorDistance = Distance(positions[station], state.Anchors[station]) / state.Grid;
        cost += anchorDistance * anchorDistance * LayoutCostWeights.GeographicAnchor;
        return cost;
    }

    // Grid-bucket index over stations and edges used by LocalCost and the spacing
    // check. Every query returns a conservative SUPERSET of the geometrically
    // relevant candidates (bounding-box prefilter), and candidates are returned in
    // ascending index order, so the nonzero cost terms are added in exactly the
    // same order as the full O(N+E) scans this replaces - the annealed layout is
    // byte-for-byte identical, just cheaper per move on large networks.
    private sealed class AnnealSpatialIndex
    {
        private readonly SchematicLayoutTopology _topology;
        private readonly double _cellSize;
        private readonly double _originX;
        private readonly double _originY;
        private readonly int _cols;
        private readonly int _rows;
        private readonly List<int>?[] _stationsByCell;
        private readonly List<int>?[] _edgesByCell;
        private readonly int[] _stationCell;
        private readonly (int MinX, int MinY, int MaxX, int MaxY)[] _edgeRange;
        private readonly int[] _edgeStamp;
        private int _stamp;

        public AnnealSpatialIndex(SchematicLayoutTopology topology, SvgPoint[] positions, SvgRect bounds, double cellSize)
        {
            _topology = topology;
            _cellSize = Math.Max(1, cellSize);
            _originX = bounds.Left;
            _originY = bounds.Top;
            _cols = Math.Max(1, (int)Math.Ceiling(Math.Max(1, bounds.Right - bounds.Left) / _cellSize) + 1);
            _rows = Math.Max(1, (int)Math.Ceiling(Math.Max(1, bounds.Bottom - bounds.Top) / _cellSize) + 1);
            _stationsByCell = new List<int>?[_cols * _rows];
            _edgesByCell = new List<int>?[_cols * _rows];
            _stationCell = new int[positions.Length];
            _edgeRange = new (int, int, int, int)[topology.Edges.Length];
            _edgeStamp = new int[topology.Edges.Length];
            Rebuild(positions);
        }

        private int CellX(double x)
        {
            return Math.Clamp((int)((x - _originX) / _cellSize), 0, _cols - 1);
        }

        private int CellY(double y)
        {
            return Math.Clamp((int)((y - _originY) / _cellSize), 0, _rows - 1);
        }

        public void Rebuild(SvgPoint[] positions)
        {
            foreach (List<int>? cell in _stationsByCell)
            {
                cell?.Clear();
            }

            foreach (List<int>? cell in _edgesByCell)
            {
                cell?.Clear();
            }

            for (int station = 0; station < positions.Length; station++)
            {
                int cell = CellY(positions[station].Y) * _cols + CellX(positions[station].X);
                _stationCell[station] = cell;
                (_stationsByCell[cell] ??= []).Add(station);
            }

            for (int edge = 0; edge < _topology.Edges.Length; edge++)
            {
                RegisterEdge(edge, positions);
            }
        }

        // Call after positions[station] has been updated to its accepted value.
        public void OnStationMoved(int station, SvgPoint[] positions)
        {
            int cell = CellY(positions[station].Y) * _cols + CellX(positions[station].X);
            if (cell != _stationCell[station])
            {
                _stationsByCell[_stationCell[station]]?.Remove(station);
                (_stationsByCell[cell] ??= []).Add(station);
                _stationCell[station] = cell;
            }

            foreach (int edge in _topology.IncidentEdgesByStation[station])
            {
                UnregisterEdge(edge);
                RegisterEdge(edge, positions);
            }
        }

        private void RegisterEdge(int edge, SvgPoint[] positions)
        {
            SvgPoint a = positions[_topology.Edges[edge].A];
            SvgPoint b = positions[_topology.Edges[edge].B];
            int minX = CellX(Math.Min(a.X, b.X));
            int maxX = CellX(Math.Max(a.X, b.X));
            int minY = CellY(Math.Min(a.Y, b.Y));
            int maxY = CellY(Math.Max(a.Y, b.Y));
            _edgeRange[edge] = (minX, minY, maxX, maxY);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    (_edgesByCell[cy * _cols + cx] ??= []).Add(edge);
                }
            }
        }

        private void UnregisterEdge(int edge)
        {
            (int minX, int minY, int maxX, int maxY) = _edgeRange[edge];
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    _edgesByCell[cy * _cols + cx]?.Remove(edge);
                }
            }
        }

        // Stations within one cell of the segment's bounding box: superset of all
        // stations closer than Clearance (cell size >= clearance) to the segment.
        public void CollectStationsNearSegment(SvgPoint a, SvgPoint b, List<int> result)
        {
            result.Clear();
            int minX = Math.Max(0, CellX(Math.Min(a.X, b.X)) - 1);
            int maxX = Math.Min(_cols - 1, CellX(Math.Max(a.X, b.X)) + 1);
            int minY = Math.Max(0, CellY(Math.Min(a.Y, b.Y)) - 1);
            int maxY = Math.Min(_rows - 1, CellY(Math.Max(a.Y, b.Y)) + 1);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    List<int>? cell = _stationsByCell[cy * _cols + cx];
                    if (cell is not null)
                    {
                        result.AddRange(cell);
                    }
                }
            }

            result.Sort();
        }

        // Edges whose bounding box overlaps the segment's bounding box: two proper
        // crossings always have overlapping boxes, which always share a cell.
        public void CollectEdgesCrossingCandidates(SvgPoint a, SvgPoint b, List<int> result)
        {
            result.Clear();
            _stamp++;
            int minX = CellX(Math.Min(a.X, b.X));
            int maxX = CellX(Math.Max(a.X, b.X));
            int minY = CellY(Math.Min(a.Y, b.Y));
            int maxY = CellY(Math.Max(a.Y, b.Y));
            CollectEdgesFromCells(minX, maxX, minY, maxY, result);
            result.Sort();
        }

        // Edges within one cell of the point: superset of all edges closer than
        // Clearance to the point.
        public void CollectEdgesNearPoint(SvgPoint point, List<int> result)
        {
            result.Clear();
            _stamp++;
            int cx = CellX(point.X);
            int cy = CellY(point.Y);
            CollectEdgesFromCells(
                Math.Max(0, cx - 1),
                Math.Min(_cols - 1, cx + 1),
                Math.Max(0, cy - 1),
                Math.Min(_rows - 1, cy + 1),
                result);
            result.Sort();
        }

        private void CollectEdgesFromCells(int minX, int maxX, int minY, int maxY, List<int> result)
        {
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    List<int>? cell = _edgesByCell[cy * _cols + cx];
                    if (cell is null)
                    {
                        continue;
                    }

                    foreach (int edge in cell)
                    {
                        if (_edgeStamp[edge] != _stamp)
                        {
                            _edgeStamp[edge] = _stamp;
                            result.Add(edge);
                        }
                    }
                }
            }
        }

        // True when any other station lies within minimumSpacing of the candidate
        // point. Cell size >= minimumSpacing, so a 3x3 neighborhood is exhaustive.
        public bool AnyStationWithinSpacing(SvgPoint candidate, int excludedStation, SvgPoint[] positions, double minimumSpacing)
        {
            int cxc = CellX(candidate.X);
            int cyc = CellY(candidate.Y);
            int minX = Math.Max(0, cxc - 1);
            int maxX = Math.Min(_cols - 1, cxc + 1);
            int minY = Math.Max(0, cyc - 1);
            int maxY = Math.Min(_rows - 1, cyc + 1);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    List<int>? cell = _stationsByCell[cy * _cols + cx];
                    if (cell is null)
                    {
                        continue;
                    }

                    foreach (int station in cell)
                    {
                        if (station != excludedStation && Distance(positions[station], candidate) < minimumSpacing)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
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
