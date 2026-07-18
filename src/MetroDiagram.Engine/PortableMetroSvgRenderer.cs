using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MetroDiagram.Engine
{
    /// <summary>
    /// Dependency-free renderer shared by the CS2 mod and desktop validation.
    /// It intentionally implements the in-game preview profiles only; the full
    /// cartographic renderer remains in MetroDiagram.Rendering.
    /// </summary>
    public sealed class PortableMetroSvgRenderer
    {
        private const string SvgFontFamily = "Overpass, 'Noto Sans SC', 'Noto Sans TC', 'Noto Sans JP', 'Noto Sans KR', 'Noto Sans', Arial, sans-serif";

        public PortableRenderResult Render(MetroNetworkSnapshot snapshot, PortableRenderOptions options)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            options = options ?? new PortableRenderOptions();
            ValidateOptions(options);
            if (options.LayoutMode == PortableLayoutMode.SchematicAnneal
                && options.AdaptCanvasHeightToNetwork)
            {
                // Adapt the canvas height to the network's shape (desktop
                // AdaptCanvasHeightToNetwork behavior) so wide networks are not
                // letterboxed and tall ones are not squashed. The panel scales
                // the SVG responsively, so a per-city aspect is safe in-game.
                options = CloneWithAdaptedHeight(options, snapshot);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Dictionary<string, MetroSnapshotStation> stations = snapshot.Stations
                .Where(station => !string.IsNullOrWhiteSpace(station.Id))
                .GroupBy(station => station.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            List<DisplayRoute> routes = ResolveDisplayRoutes(snapshot.Lines, options.MergeServiceFamilies);
            SourceProjector projector = SourceProjector.Create(snapshot, options, options.LayoutMode == PortableLayoutMode.Geographic);
            Dictionary<string, Point2> projectedStations = ProjectStations(stations.Values, projector);

            if (options.LayoutMode == PortableLayoutMode.SchematicAnneal && projectedStations.Count > 1)
            {
                ApplySchematicAnneal(projectedStations, routes, options);
            }

            string svg = BuildSvg(snapshot, options, stations, routes, projectedStations, projector);
            stopwatch.Stop();
            return new PortableRenderResult(svg, snapshot.Revision, stations.Count, routes.Count, stopwatch.ElapsedMilliseconds);
        }

        private static void ValidateOptions(PortableRenderOptions options)
        {
            if (options.Width < 320 || options.Height < 240)
            {
                throw new ArgumentException("Portable render canvas must be at least 320 x 240.");
            }

            if (options.Padding < 0 || options.LegendWidth < 0 || options.GridSize <= 0 || options.RouteWidth <= 0)
            {
                throw new ArgumentException("Portable render dimensions must be positive.");
            }
        }

        private static PortableRenderOptions CloneWithAdaptedHeight(PortableRenderOptions options, MetroNetworkSnapshot snapshot)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            int positioned = 0;
            foreach (MetroSnapshotStation station in snapshot.Stations)
            {
                if (string.IsNullOrWhiteSpace(station.Id))
                {
                    continue;
                }

                positioned++;
                minX = Math.Min(minX, station.X);
                maxX = Math.Max(maxX, station.X);
                minZ = Math.Min(minZ, station.Z);
                maxZ = Math.Max(maxZ, station.Z);
            }

            if (positioned < 2)
            {
                return options;
            }

            double sourceWidth = Math.Max(maxX - minX, 1);
            double sourceHeight = Math.Max(maxZ - minZ, 1);
            double contentWidth = Math.Max(options.Width - (options.Padding * 2) - options.LegendWidth, 1);
            double desiredContentHeight = contentWidth * (sourceHeight / sourceWidth);
            double desiredHeight = desiredContentHeight + (options.Padding * 2) + 60;
            int height = (int)Math.Round(Clamp(desiredHeight, options.Width * 0.6, options.Width * 1.5));

            return new PortableRenderOptions
            {
                LayoutMode = options.LayoutMode,
                Width = options.Width,
                Height = height,
                Padding = options.Padding,
                LegendWidth = options.LegendWidth,
                RouteWidth = options.RouteWidth,
                StationRadius = options.StationRadius,
                LabelFontSize = options.LabelFontSize,
                GridSize = options.GridSize,
                ShowGenericStationNames = options.ShowGenericStationNames,
                HideCrowdedLabels = options.HideCrowdedLabels,
                MergeServiceFamilies = options.MergeServiceFamilies,
                AnnealAttemptLimit = options.AnnealAttemptLimit,
                AdaptCanvasHeightToNetwork = options.AdaptCanvasHeightToNetwork
            };
        }

        private static List<DisplayRoute> ResolveDisplayRoutes(IReadOnlyList<MetroSnapshotLine> lines, bool mergeFamilies)
        {
            if (!mergeFamilies)
            {
                return lines.Select(line => new DisplayRoute(GetFamilyKey(line), line)).ToList();
            }

            // Same family name with different colors means distinct services (for
            // example several auto-named "地铁路线工具" lines in one export), so the
            // color participates in the merge key. Legitimate service variants that
            // should merge share one color, keeping existing outputs unchanged.
            return lines
                .GroupBy(line => GetFamilyKey(line) + "\u0001" + GetFamilyColorKey(line), StringComparer.CurrentCulture)
                .Select(group => group.OrderByDescending(line => line.Stops.Count)
                    .ThenByDescending(line => line.PathPoints.Count)
                    .ThenBy(line => line.Name, StringComparer.CurrentCulture)
                    .First())
                .Select(representative => new DisplayRoute(GetFamilyKey(representative), representative))
                .OrderBy(route => ExtractLineNumber(route.DisplayName))
                .ThenBy(route => route.DisplayName, StringComparer.CurrentCulture)
                .ToList();
        }

        private static string GetFamilyKey(MetroSnapshotLine line)
        {
            string name = string.IsNullOrWhiteSpace(line.Name) ? line.Id : line.Name.Trim();
            int chineseOpen = name.IndexOf('（');
            int englishOpen = name.IndexOf('(');
            int open = chineseOpen >= 0 && englishOpen >= 0 ? Math.Min(chineseOpen, englishOpen) : Math.Max(chineseOpen, englishOpen);
            return open > 0 ? name.Substring(0, open).Trim() : name;
        }

        private static string GetFamilyColorKey(MetroSnapshotLine line)
        {
            // A numbered name ("10号线", "Metro Line 3") is itself the service
            // identity, so numbered families merge by name alone even when the
            // player's colors drift apart (the Zhaoqing shared-corridor case).
            // Number-less duplicate names (auto-named "地铁路线工具" exports) are
            // indistinguishable placeholders, so the color keeps them separate.
            if (ExtractLineNumber(GetFamilyKey(line)) != int.MaxValue)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(line.Color) ? string.Empty : line.Color.Trim().ToUpperInvariant();
        }

        private static List<string> NormalizeRouteStops(IReadOnlyList<string> source)
        {
            List<string> stops = new List<string>();
            foreach (string stationId in source ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(stationId)
                    || (stops.Count > 0 && string.Equals(stops[stops.Count - 1], stationId, StringComparison.Ordinal)))
                {
                    continue;
                }

                stops.Add(stationId);
            }

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

                if (right >= stops.Count && mirrorCount >= 2 && mirrorCount > bestMirrorCount)
                {
                    bestPivot = pivot;
                    bestMirrorCount = mirrorCount;
                }
            }

            return bestPivot < 0 ? stops : stops.Take(bestPivot + 1).ToList();
        }

        private static int ExtractLineNumber(string value)
        {
            int number = 0;
            bool found = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsDigit(value[i]))
                {
                    found = true;
                    number = (number * 10) + (value[i] - '0');
                }
                else if (found)
                {
                    break;
                }
            }

            return found ? number : int.MaxValue;
        }

        private static Dictionary<string, Point2> ProjectStations(IEnumerable<MetroSnapshotStation> stations, SourceProjector projector)
        {
            List<MetroSnapshotStation> list = stations.ToList();
            Dictionary<string, Point2> result = new Dictionary<string, Point2>(StringComparer.Ordinal);
            if (list.Count == 0)
            {
                return result;
            }

            foreach (MetroSnapshotStation station in list)
            {
                result[station.Id] = projector.Project(station.X, station.Z);
            }

            return result;
        }

        // ---- Schematic layout engine ----
        //
        // This is an intentional port of the desktop schematic-anneal MATH (the
        // cost terms, weights, schedule shape and finisher of
        // MetroDiagram.Rendering\MetroSvgRenderer.SchematicAnneal.cs +
        // LayoutMetrics.cs), kept dependency-free for netstandard2.0. It is not
        // byte-compatible with the desktop renderer, but it optimizes the same
        // objective, so the in-game preview converges to the same look.
        private const ulong AnnealSeed = 0x20260706C52D1A2Bul;
        private const double WeightOctilinear = 4.5;
        private const double WeightShortEdge = 2.0;
        private const double WeightLongEdge = 0.5;
        private const double WeightBend = 1.5;
        private const double WeightCrossing = 8.0;
        private const double WeightClearance = 4.0;
        private const double WeightGeographicAnchor = 0.05;

        private static void ApplySchematicAnneal(
            Dictionary<string, Point2> points,
            List<DisplayRoute> routes,
            PortableRenderOptions options)
        {
            double grid = options.GridSize;
            List<Edge> edges = BuildEdges(routes, points);
            if (edges.Count == 0)
            {
                return;
            }

            foreach (string stationId in points.Keys.ToArray())
            {
                Point2 point = points[stationId];
                points[stationId] = ClampToMap(new Point2(Snap(point.X, grid), Snap(point.Y, grid)), options);
            }

            // The layout engine works on int-indexed arrays: per-candidate cost
            // evaluation runs thousands of times, and string-keyed dictionary
            // lookups dominate the runtime otherwise. Anchors are the SNAPPED
            // starting positions, matching the desktop solver.
            ArrayLayout layout = ArrayLayout.Create(points, routes, edges, options);

            RunAnnealSearch(layout, options);
            FitPointsToFrame(layout, options);
            layout.WriteBack(points);
        }

        // Multi-start wrapper: one anneal pass uses stationCount*300 of the
        // attempt budget, so small networks leave most of AnnealAttemptLimit
        // unused - and small networks are exactly where a single walk can
        // freeze a geographically straight line into a locked-in zigzag.
        // Spend the leftover budget on extra independent starts (distinct
        // fixed seeds) and keep the cheapest polished result. Networks whose
        // single pass already fills the budget run one start and stay
        // byte-identical to the previous behavior.
        private static void RunAnnealSearch(ArrayLayout layout, PortableRenderOptions options)
        {
            int starts = ComputeAnnealStartCount(layout.Positions.Length, options.AnnealAttemptLimit);
            Point2[] initialPositions = (Point2[])layout.Positions.Clone();
            Point2[] bestPositions = null;
            double bestCost = double.MaxValue;

            for (int start = 0; start < starts; start++)
            {
                if (start > 0)
                {
                    Array.Copy(initialPositions, layout.Positions, initialPositions.Length);
                }

                RunAnnealSchedule(layout, options, AnnealStartSeed(start));
                RunGreedyPolish(layout, options);
                double cost = TotalCost(layout);
                if (bestPositions == null || cost < bestCost - 0.000001)
                {
                    bestCost = cost;
                    bestPositions = (Point2[])layout.Positions.Clone();
                }
            }

            if (bestPositions != null)
            {
                Array.Copy(bestPositions, layout.Positions, bestPositions.Length);
            }
        }

        internal static int ComputeAnnealStartCount(int stationCount, int annealAttemptLimit)
        {
            int totalBudget = Math.Max(annealAttemptLimit, 6000);
            int attemptsPerStart = Math.Min(Math.Max(stationCount * 300, 6000), totalBudget);
            return Math.Min(3, Math.Max(1, totalBudget / attemptsPerStart));
        }

        private static ulong AnnealStartSeed(int start)
        {
            if (start == 0)
            {
                return AnnealSeed;
            }

            // splitmix64 of the base seed and the start index: deterministic,
            // runtime-independent, and decorrelated from the first walk.
            ulong z = AnnealSeed + ((ulong)start * 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static void RunAnnealSchedule(ArrayLayout layout, PortableRenderOptions options, ulong seed)
        {
            int stationCount = layout.Positions.Length;
            int attempts = Math.Min(Math.Max(stationCount * 300, 6000), Math.Max(options.AnnealAttemptLimit, 6000));
            double initialCost = TotalCost(layout);
            double startTemperature = Math.Max(0.5, initialCost / Math.Max(1, stationCount) * 0.6);
            double endTemperature = startTemperature / 200;
            double cooling = Math.Pow(endTemperature / startTemperature, 1.0 / attempts);

            ulong rng = seed;
            double temperature = startTemperature;
            double currentCost = initialCost;
            double bestCost = initialCost;
            Point2[] positions = layout.Positions;
            Point2[] bestPositions = (Point2[])positions.Clone();
            double grid = layout.Grid;

            for (int attempt = 0; attempt < attempts; attempt++, temperature *= cooling)
            {
                int station = (int)(NextRandom(ref rng) % (ulong)stationCount);
                int range = temperature > startTemperature * 0.15 ? 2 : 1;
                int dx = (int)(NextRandom(ref rng) % (ulong)((2 * range) + 1)) - range;
                int dy = (int)(NextRandom(ref rng) % (ulong)((2 * range) + 1)) - range;
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                Point2 current = positions[station];
                Point2 candidate = ClampToMap(new Point2(current.X + (dx * grid), current.Y + (dy * grid)), options);
                if (DistanceSquared(candidate, current) < 0.001
                    || ViolatesMinimumSpacing(layout, station, candidate))
                {
                    continue;
                }

                double before = LocalCost(layout, station);
                positions[station] = candidate;
                double after = LocalCost(layout, station);
                double delta = after - before;
                bool accept = delta <= 0 || NextRandomDouble(ref rng) < Math.Exp(-delta / Math.Max(temperature, 0.000001));
                if (!accept)
                {
                    positions[station] = current;
                    continue;
                }

                currentCost += delta;
                if (currentCost < bestCost - 0.000001)
                {
                    bestCost = currentCost;
                    Array.Copy(positions, bestPositions, positions.Length);
                }
            }

            // Resume from the best state seen, not wherever the walk ended.
            Array.Copy(bestPositions, positions, positions.Length);
        }

        private static void RunGreedyPolish(ArrayLayout layout, PortableRenderOptions options)
        {
            Point2[] positions = layout.Positions;
            int[] order = Enumerable.Range(0, positions.Length)
                .OrderByDescending(station => layout.IncidentEdges[station].Length)
                .ThenBy(station => station)
                .ToArray();
            double grid = layout.Grid;

            const int maxSweeps = 12;
            for (int sweep = 0; sweep < maxSweeps; sweep++)
            {
                bool changed = false;
                foreach (int station in order)
                {
                    Point2 current = positions[station];
                    Point2 best = current;
                    double bestDelta = -0.000001;
                    double baseline = LocalCost(layout, station);

                    for (int dx = -2; dx <= 2; dx++)
                    {
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }

                            Point2 candidate = ClampToMap(new Point2(current.X + (dx * grid), current.Y + (dy * grid)), options);
                            if (DistanceSquared(candidate, current) < 0.001
                                || ViolatesMinimumSpacing(layout, station, candidate))
                            {
                                continue;
                            }

                            positions[station] = candidate;
                            double delta = LocalCost(layout, station) - baseline;
                            if (delta < bestDelta)
                            {
                                bestDelta = delta;
                                best = candidate;
                            }
                        }
                    }

                    positions[station] = best;
                    changed |= DistanceSquared(best, current) > 0.001;
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Uniformly scales and recenters the annealed layout to fill the map
        /// frame (uniform scaling preserves octilinearity). Ports the intent of
        /// the desktop FitPointsToBounds with a simple label-breathing inset.
        /// </summary>
        private static void FitPointsToFrame(ArrayLayout layout, PortableRenderOptions options)
        {
            Point2[] positions = layout.Positions;
            if (positions.Length < 2)
            {
                return;
            }

            MapFrame frame = layout.Frame;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (Point2 point in positions)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            double contentWidth = Math.Max(maxX - minX, 1);
            double contentHeight = Math.Max(maxY - minY, 1);
            double labelInsetX = options.LabelFontSize * 3.5;
            double labelInsetY = options.LabelFontSize * 1.6;
            double availableWidth = Math.Max((frame.Right - frame.Left) - (labelInsetX * 2), 1);
            double availableHeight = Math.Max((frame.Bottom - frame.Top) - (labelInsetY * 2), 1);
            double scale = Math.Min(Math.Min(availableWidth / contentWidth, availableHeight / contentHeight), 2.5);
            double originX = frame.Left + labelInsetX + ((availableWidth - (contentWidth * scale)) / 2);
            double originY = frame.Top + labelInsetY + ((availableHeight - (contentHeight * scale)) / 2);

            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = new Point2(
                    originX + ((positions[i].X - minX) * scale),
                    originY + ((positions[i].Y - minY) * scale));
            }
        }

        private static bool ViolatesMinimumSpacing(ArrayLayout layout, int station, Point2 candidate)
        {
            Point2[] positions = layout.Positions;
            double minimumSquared = layout.MinimumSpacingSquared;
            for (int other = 0; other < positions.Length; other++)
            {
                if (other != station && DistanceSquared(positions[other], candidate) < minimumSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private static double TotalCost(ArrayLayout layout)
        {
            Point2[] positions = layout.Positions;
            double cost = 0;
            for (int i = 0; i < layout.EdgeA.Length; i++)
            {
                cost += EdgeUnitCost(positions[layout.EdgeA[i]], positions[layout.EdgeB[i]], layout.PreferredSpacing);
            }

            for (int i = 0; i < layout.TriplePrevious.Length; i++)
            {
                cost += BendUnitCost(BendTurnDegrees(
                    positions[layout.TriplePrevious[i]],
                    positions[layout.TripleStation[i]],
                    positions[layout.TripleNext[i]]));
            }

            for (int i = 0; i < layout.EdgeA.Length; i++)
            {
                for (int j = i + 1; j < layout.EdgeA.Length; j++)
                {
                    if (!EdgesShareStation(layout, i, j)
                        && SegmentsCross(positions[layout.EdgeA[i]], positions[layout.EdgeB[i]], positions[layout.EdgeA[j]], positions[layout.EdgeB[j]]))
                    {
                        cost += WeightCrossing;
                    }
                }
            }

            for (int station = 0; station < positions.Length; station++)
            {
                for (int edgeIndex = 0; edgeIndex < layout.EdgeA.Length; edgeIndex++)
                {
                    if (ViolatesClearance(layout, station, edgeIndex))
                    {
                        cost += WeightClearance;
                    }
                }
            }

            return cost;
        }

        /// <summary>
        /// Cost affected by one station: its incident edges (length +
        /// octilinearity), crossings and clearance involving those edges, bends
        /// through its triples, its own clearance against other edges, and its
        /// geographic anchor. Mirrors the desktop LocalCost term for term.
        /// </summary>
        private static double LocalCost(ArrayLayout layout, int station)
        {
            Point2[] positions = layout.Positions;
            double cost = 0;

            int[] incident = layout.IncidentEdges[station];
            for (int localIndex = 0; localIndex < incident.Length; localIndex++)
            {
                int edgeIndex = incident[localIndex];
                int edgeA = layout.EdgeA[edgeIndex];
                int edgeB = layout.EdgeB[edgeIndex];
                Point2 pointA = positions[edgeA];
                Point2 pointB = positions[edgeB];
                cost += EdgeUnitCost(pointA, pointB, layout.PreferredSpacing);

                for (int other = 0; other < layout.EdgeA.Length; other++)
                {
                    if (other == edgeIndex || EdgesShareStation(layout, edgeIndex, other))
                    {
                        continue;
                    }

                    if (SegmentsCross(pointA, pointB, positions[layout.EdgeA[other]], positions[layout.EdgeB[other]]))
                    {
                        // Halve pairs shared between two incident edges so the
                        // before/after difference stays symmetric.
                        bool otherIsIncident = layout.EdgeA[other] == station || layout.EdgeB[other] == station;
                        cost += WeightCrossing * (otherIsIncident ? 0.5 : 1);
                    }
                }

                for (int other = 0; other < positions.Length; other++)
                {
                    if (other == edgeA || other == edgeB || other == station)
                    {
                        continue;
                    }

                    if (ViolatesClearance(layout, other, edgeIndex))
                    {
                        cost += WeightClearance;
                    }
                }
            }

            int[] triples = layout.TriplesByStation[station];
            for (int i = 0; i < triples.Length; i++)
            {
                int triple = triples[i];
                cost += BendUnitCost(BendTurnDegrees(
                    positions[layout.TriplePrevious[triple]],
                    positions[layout.TripleStation[triple]],
                    positions[layout.TripleNext[triple]]));
            }

            for (int edgeIndex = 0; edgeIndex < layout.EdgeA.Length; edgeIndex++)
            {
                if (ViolatesClearance(layout, station, edgeIndex))
                {
                    cost += WeightClearance;
                }
            }

            double anchorDistance = Math.Sqrt(DistanceSquared(positions[station], layout.Anchors[station])) / layout.Grid;
            cost += anchorDistance * anchorDistance * WeightGeographicAnchor;
            return cost;
        }

        private static bool EdgesShareStation(ArrayLayout layout, int first, int second)
        {
            int a = layout.EdgeA[first];
            int b = layout.EdgeB[first];
            int c = layout.EdgeA[second];
            int d = layout.EdgeB[second];
            return a == c || a == d || b == c || b == d;
        }

        private static double EdgeUnitCost(Point2 a, Point2 b, double preferredSpacing)
        {
            double length = Math.Sqrt(DistanceSquared(a, b));
            if (length < 0.000001)
            {
                return 50;
            }

            double step = Math.PI / 4;
            double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double remainder = angle % step;
            if (remainder < 0)
            {
                remainder += step;
            }

            double octilinearDeviation = Math.Min(remainder, step - remainder) / (Math.PI / 8);
            double cost = octilinearDeviation * octilinearDeviation * WeightOctilinear;

            double relative = (length - preferredSpacing) / preferredSpacing;
            if (relative < 0)
            {
                cost += relative * relative * WeightShortEdge;
            }
            else
            {
                cost += Math.Min(relative * relative, 4) * WeightLongEdge;
            }

            return cost;
        }

        private static double BendTurnDegrees(Point2 previous, Point2 current, Point2 next)
        {
            double incomingX = current.X - previous.X;
            double incomingY = current.Y - previous.Y;
            double outgoingX = next.X - current.X;
            double outgoingY = next.Y - current.Y;
            double incomingLength = Math.Sqrt((incomingX * incomingX) + (incomingY * incomingY));
            double outgoingLength = Math.Sqrt((outgoingX * outgoingX) + (outgoingY * outgoingY));
            if (incomingLength < 0.000001 || outgoingLength < 0.000001)
            {
                return 0;
            }

            double cosine = ((incomingX * outgoingX) + (incomingY * outgoingY)) / (incomingLength * outgoingLength);
            return Math.Acos(Clamp(cosine, -1, 1)) * 180 / Math.PI;
        }

        private static double BendUnitCost(double turnDegrees)
        {
            double normalized = turnDegrees / 90;
            return Math.Min(normalized * normalized, 4) * WeightBend;
        }

        private static bool ViolatesClearance(ArrayLayout layout, int station, int edgeIndex)
        {
            int edgeA = layout.EdgeA[edgeIndex];
            int edgeB = layout.EdgeB[edgeIndex];
            if (edgeA == station || edgeB == station)
            {
                return false;
            }

            // A line doubling back through its own corridor is expected, not
            // crowding: same-line station/edge pairs are exempt (desktop
            // StationLineMask/EdgeLineMask behavior).
            if ((layout.StationLineMasks[station] & layout.EdgeLineMasks[edgeIndex]) != 0)
            {
                return false;
            }

            Point2[] positions = layout.Positions;
            return DistancePointToSegmentSquared(positions[station], positions[edgeA], positions[edgeB]) < layout.ClearanceSquared;
        }

        private static double DistancePointToSegmentSquared(Point2 point, Point2 a, Point2 b)
        {
            double vx = b.X - a.X;
            double vy = b.Y - a.Y;
            double lengthSquared = (vx * vx) + (vy * vy);
            if (lengthSquared <= 0.0001)
            {
                return DistanceSquared(point, a);
            }

            double t = (((point.X - a.X) * vx) + ((point.Y - a.Y) * vy)) / lengthSquared;
            t = Clamp(t, 0, 1);
            double px = a.X + (t * vx);
            double py = a.Y + (t * vy);
            double dx = point.X - px;
            double dy = point.Y - py;
            return (dx * dx) + (dy * dy);
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

        /// <summary>
        /// Int-indexed working set for the layout solver: positions, anchors,
        /// edges, per-station incident edge lists, same-line masks, and bend
        /// triples all live in flat arrays so per-candidate cost evaluation
        /// never touches a string-keyed dictionary.
        /// </summary>
        private sealed class ArrayLayout
        {
            private readonly string[] m_StationIds;

            private ArrayLayout(
                string[] stationIds,
                Point2[] positions,
                Point2[] anchors,
                int[] edgeA,
                int[] edgeB,
                int[][] incidentEdges,
                ulong[] stationLineMasks,
                ulong[] edgeLineMasks,
                int[] triplePrevious,
                int[] tripleStation,
                int[] tripleNext,
                int[][] triplesByStation,
                double preferredSpacing,
                double minimumSpacing,
                double clearance,
                double grid,
                MapFrame frame)
            {
                m_StationIds = stationIds;
                Positions = positions;
                Anchors = anchors;
                EdgeA = edgeA;
                EdgeB = edgeB;
                IncidentEdges = incidentEdges;
                StationLineMasks = stationLineMasks;
                EdgeLineMasks = edgeLineMasks;
                TriplePrevious = triplePrevious;
                TripleStation = tripleStation;
                TripleNext = tripleNext;
                TriplesByStation = triplesByStation;
                PreferredSpacing = preferredSpacing;
                MinimumSpacingSquared = minimumSpacing * minimumSpacing;
                ClearanceSquared = clearance * clearance;
                Grid = grid;
                Frame = frame;
            }

            public Point2[] Positions { get; }
            public Point2[] Anchors { get; }
            public int[] EdgeA { get; }
            public int[] EdgeB { get; }
            public int[][] IncidentEdges { get; }
            public ulong[] StationLineMasks { get; }
            public ulong[] EdgeLineMasks { get; }
            public int[] TriplePrevious { get; }
            public int[] TripleStation { get; }
            public int[] TripleNext { get; }
            public int[][] TriplesByStation { get; }
            public double PreferredSpacing { get; }
            public double MinimumSpacingSquared { get; }
            public double ClearanceSquared { get; }
            public double Grid { get; }
            public MapFrame Frame { get; }

            public void WriteBack(Dictionary<string, Point2> points)
            {
                for (int i = 0; i < m_StationIds.Length; i++)
                {
                    points[m_StationIds[i]] = Positions[i];
                }
            }

            public static ArrayLayout Create(
                Dictionary<string, Point2> points,
                List<DisplayRoute> routes,
                List<Edge> edges,
                PortableRenderOptions options)
            {
                string[] stationIds = points.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                Dictionary<string, int> indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < stationIds.Length; i++)
                {
                    indexOf[stationIds[i]] = i;
                }

                Point2[] positions = new Point2[stationIds.Length];
                for (int i = 0; i < stationIds.Length; i++)
                {
                    positions[i] = points[stationIds[i]];
                }

                Point2[] anchors = (Point2[])positions.Clone();

                int[] edgeA = new int[edges.Count];
                int[] edgeB = new int[edges.Count];
                Dictionary<long, int> edgeIndexByPair = new Dictionary<long, int>();
                List<int>[] incidentLists = new List<int>[stationIds.Length];
                for (int i = 0; i < incidentLists.Length; i++)
                {
                    incidentLists[i] = new List<int>();
                }

                for (int i = 0; i < edges.Count; i++)
                {
                    int a = indexOf[edges[i].A];
                    int b = indexOf[edges[i].B];
                    edgeA[i] = a;
                    edgeB[i] = b;
                    edgeIndexByPair[PairKey(a, b)] = i;
                    incidentLists[a].Add(i);
                    incidentLists[b].Add(i);
                }

                int[][] incidentEdges = new int[stationIds.Length][];
                for (int i = 0; i < stationIds.Length; i++)
                {
                    incidentEdges[i] = incidentLists[i].ToArray();
                }

                // One mask bit per display route; routes beyond 64 share the top
                // bit, which only weakens the same-line exemption for extras.
                ulong[] stationMasks = new ulong[stationIds.Length];
                ulong[] edgeMasks = new ulong[edges.Count];
                List<int> triplePrevious = new List<int>();
                List<int> tripleStation = new List<int>();
                List<int> tripleNext = new List<int>();
                List<int>[] triplesByStationLists = new List<int>[stationIds.Length];
                for (int i = 0; i < triplesByStationLists.Length; i++)
                {
                    triplesByStationLists[i] = new List<int>();
                }

                HashSet<long> tripleKeys = new HashSet<long>();
                for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                {
                    ulong bit = 1ul << Math.Min(routeIndex, 63);
                    List<int> stops = new List<int>();
                    foreach (string stopId in routes[routeIndex].Stops)
                    {
                        int index;
                        if (indexOf.TryGetValue(stopId, out index))
                        {
                            stops.Add(index);
                        }
                    }

                    foreach (int stop in stops)
                    {
                        stationMasks[stop] |= bit;
                    }

                    for (int i = 1; i < stops.Count; i++)
                    {
                        int edgeIndex;
                        if (edgeIndexByPair.TryGetValue(PairKey(stops[i - 1], stops[i]), out edgeIndex))
                        {
                            edgeMasks[edgeIndex] |= bit;
                        }
                    }

                    for (int i = 1; i < stops.Count - 1; i++)
                    {
                        long tripleKey = (((long)stops[i - 1] * stationIds.Length) + stops[i]) * stationIds.Length + stops[i + 1];
                        if (!tripleKeys.Add(tripleKey))
                        {
                            continue;
                        }

                        int tripleIndex = triplePrevious.Count;
                        triplePrevious.Add(stops[i - 1]);
                        tripleStation.Add(stops[i]);
                        tripleNext.Add(stops[i + 1]);
                        AddDistinct(triplesByStationLists[stops[i - 1]], tripleIndex);
                        AddDistinct(triplesByStationLists[stops[i]], tripleIndex);
                        AddDistinct(triplesByStationLists[stops[i + 1]], tripleIndex);
                    }
                }

                int[][] triplesByStation = new int[stationIds.Length][];
                for (int i = 0; i < stationIds.Length; i++)
                {
                    triplesByStation[i] = triplesByStationLists[i].ToArray();
                }

                double grid = options.GridSize;
                List<double> lengths = new List<double>(edges.Count);
                for (int i = 0; i < edges.Count; i++)
                {
                    double length = Math.Sqrt(DistanceSquared(positions[edgeA[i]], positions[edgeB[i]]));
                    if (length > 0.001)
                    {
                        lengths.Add(length);
                    }
                }

                lengths.Sort();
                double median = lengths.Count == 0
                    ? 0
                    : lengths.Count % 2 == 1
                        ? lengths[lengths.Count / 2]
                        : (lengths[(lengths.Count / 2) - 1] + lengths[lengths.Count / 2]) / 2;
                double preferredSpacing = Math.Max(median > 0 ? median : grid * 3, grid * 2);
                double minimumSpacing = Math.Max(grid * 1.35, options.RouteWidth * 3.2);
                double clearance = Math.Max(options.RouteWidth * 2.0, grid * 0.45);

                return new ArrayLayout(
                    stationIds,
                    positions,
                    anchors,
                    edgeA,
                    edgeB,
                    incidentEdges,
                    stationMasks,
                    edgeMasks,
                    triplePrevious.ToArray(),
                    tripleStation.ToArray(),
                    tripleNext.ToArray(),
                    triplesByStation,
                    preferredSpacing,
                    minimumSpacing,
                    clearance,
                    grid,
                    CreateMapFrame(options));
            }

            private static void AddDistinct(List<int> list, int value)
            {
                if (list.Count == 0 || list[list.Count - 1] != value)
                {
                    list.Add(value);
                }
            }

            private static long PairKey(int a, int b)
            {
                return a <= b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            }
        }

        private static List<Edge> BuildEdges(List<DisplayRoute> routes, Dictionary<string, Point2> points)
        {
            List<Edge> edges = new List<Edge>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DisplayRoute route in routes)
            {
                for (int i = 1; i < route.Stops.Count; i++)
                {
                    string a = route.Stops[i - 1];
                    string b = route.Stops[i];
                    if (a == b || !points.ContainsKey(a) || !points.ContainsKey(b))
                    {
                        continue;
                    }

                    string key = string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
                    if (seen.Add(key))
                    {
                        edges.Add(new Edge(a, b));
                    }
                }
            }

            return edges;
        }

        private static Dictionary<string, List<Edge>> BuildIncidentEdges(List<Edge> edges)
        {
            Dictionary<string, List<Edge>> incident = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
            foreach (Edge edge in edges)
            {
                AddIncident(incident, edge.A, edge);
                AddIncident(incident, edge.B, edge);
            }

            return incident;
        }

        private static void AddIncident(Dictionary<string, List<Edge>> incident, string stationId, Edge edge)
        {
            List<Edge> list;
            if (!incident.TryGetValue(stationId, out list))
            {
                list = new List<Edge>();
                incident.Add(stationId, list);
            }

            list.Add(edge);
        }

        private static string BuildSvg(
            MetroNetworkSnapshot snapshot,
            PortableRenderOptions options,
            Dictionary<string, MetroSnapshotStation> stations,
            List<DisplayRoute> routes,
            Dictionary<string, Point2> stationPoints,
            SourceProjector projector)
        {
            StringBuilder svg = new StringBuilder(32768);
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(options.Width)
                .Append("\" height=\"").Append(options.Height).Append("\" viewBox=\"0 0 ")
                .Append(options.Width).Append(' ').Append(options.Height).Append("\" data-renderer=\"portable-engine\" data-snapshot-revision=\"")
                .Append(Xml(snapshot.Revision)).Append("\" data-layout=\"")
                .Append(options.LayoutMode == PortableLayoutMode.Geographic ? "geographic" : "schematic-anneal")
                .Append("\" overflow=\"hidden\" text-rendering=\"geometricPrecision\">");
            string title = GetTitle(snapshot.CityName, options.TitleSuffix);
            svg.Append("<title>").Append(Xml(title)).Append("</title>");
            svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fbfcfd\"/>");
            svg.Append("<text x=\"").Append(options.Padding).Append("\" y=\"46\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"24\" font-weight=\"700\" fill=\"#18222d\">")
                .Append(Xml(title)).Append("</text>");

            List<RenderedRoute> renderedRoutes = BuildRenderedRoutes(options, routes, stationPoints, projector);
            foreach (RenderedRoute route in renderedRoutes)
            {
                svg.Append("<polyline class=\"route\" data-line-id=\"").Append(Xml(route.Route.Line.Id))
                    .Append("\" data-display-family=\"").Append(Xml(route.Route.DisplayName))
                    .Append("\" data-route-source=\"").Append(route.UsesPathPoints ? "pathPoints" : "stops")
                    .Append("\" data-raw-stop-count=\"").Append(route.Route.Line.Stops.Count)
                    .Append("\" data-render-stop-count=\"").Append(route.RenderedStopCount)
                    .Append("\" data-route-chain-normalized=\"").Append(route.RenderedStopCount < route.Route.Line.Stops.Count ? "true" : "false")
                    .Append("\" fill=\"none\" stroke=\"").Append(Xml(NormalizeColor(route.Route.Line.Color)))
                    .Append("\" stroke-width=\"").Append(F(options.RouteWidth))
                    .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" points=\"")
                    .Append(Points(route.Points)).Append("\"/>");
            }

            bool useSchematicRouteChains = options.LayoutMode == PortableLayoutMode.SchematicAnneal;
            HashSet<string> terminalIds = FindTerminalIds(routes, useSchematicRouteChains);
            MapFrame mapFrame = CreateMapFrame(options);
            foreach (KeyValuePair<string, Point2> pair in stationPoints)
            {
                MetroSnapshotStation station;
                if (!stations.TryGetValue(pair.Key, out station))
                {
                    continue;
                }

                double radius = station.IsInterchange ? options.StationRadius * 1.55 : options.StationRadius;
                if (terminalIds.Contains(station.Id))
                {
                    radius = Math.Max(radius, options.StationRadius * 1.25);
                }

                svg.Append("<circle class=\"station\" data-station-id=\"").Append(Xml(station.Id))
                    .Append("\" cx=\"").Append(F(pair.Value.X)).Append("\" cy=\"").Append(F(pair.Value.Y))
                    .Append("\" r=\"").Append(F(radius)).Append("\" fill=\"#ffffff\" stroke=\"#263442\" stroke-width=\"2\"/>");
            }

            if (useSchematicRouteChains)
            {
                AppendCollisionAwareLabels(svg, options, stations, stationPoints, renderedRoutes, terminalIds, mapFrame);
            }
            else
            {
                AppendLegacyLabels(svg, options, stations, stationPoints, terminalIds, mapFrame);
            }

            double legendX = options.Width - options.LegendWidth + 24;
            string legendHeader = string.IsNullOrEmpty(options.LegendHeader) ? "Lines" : options.LegendHeader;
            svg.Append("<g class=\"legend\"><text x=\"").Append(F(legendX)).Append("\" y=\"72\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"16\" font-weight=\"700\" fill=\"#263442\">").Append(Xml(legendHeader)).Append("</text>");
            for (int i = 0; i < routes.Count; i++)
            {
                double y = 102 + (i * 28);
                svg.Append("<line x1=\"").Append(F(legendX)).Append("\" y1=\"").Append(F(y)).Append("\" x2=\"")
                    .Append(F(legendX + 34)).Append("\" y2=\"").Append(F(y)).Append("\" stroke=\"")
                    .Append(Xml(NormalizeColor(routes[i].Line.Color))).Append("\" stroke-width=\"6\" stroke-linecap=\"round\"/>");
                svg.Append("<text x=\"").Append(F(legendX + 46)).Append("\" y=\"").Append(F(y + 5))
                    .Append("\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"14\" fill=\"#263442\">")
                    .Append(Xml(routes[i].DisplayName)).Append("</text>");
            }

            svg.Append("</g></svg>");
            return svg.ToString();
        }

        private static void AppendCollisionAwareLabels(
            StringBuilder svg,
            PortableRenderOptions options,
            Dictionary<string, MetroSnapshotStation> stations,
            Dictionary<string, Point2> stationPoints,
            List<RenderedRoute> renderedRoutes,
            HashSet<string> terminalIds,
            MapFrame mapFrame)
        {
            List<Rect2> stationObstacles = stationPoints
                .Where(pair => stations.ContainsKey(pair.Key))
                .Select(pair =>
                {
                    MetroSnapshotStation station = stations[pair.Key];
                    double radius = GetStationRadius(station, terminalIds, options) + 2;
                    return Rect2.FromCenter(pair.Value, radius * 2, radius * 2);
                })
                .ToList();
            List<Segment2> routeObstacles = BuildRouteObstacles(renderedRoutes);
            List<Rect2> labelBoxes = new List<Rect2>();
            List<LabelRequest> labelRequests = stationPoints
                .Where(pair => stations.ContainsKey(pair.Key) && !ShouldHideGeneric(stations[pair.Key].Name, options))
                .Select((pair, index) => CreateLabelRequest(stations[pair.Key], pair.Value, terminalIds, options, index))
                .OrderByDescending(request => request.Priority)
                .ThenBy(request => request.Index)
                .ToList();

            foreach (LabelRequest request in labelRequests)
            {
                LabelPlacement placement = PlaceLabel(
                    request,
                    options,
                    mapFrame,
                    labelBoxes,
                    stationObstacles,
                    routeObstacles);
                double crowdedThreshold = Math.Max(36, options.LabelFontSize * options.LabelFontSize * 0.35);
                if (options.HideCrowdedLabels && !request.Important && placement.LabelOverlapArea > crowdedThreshold)
                {
                    continue;
                }

                labelBoxes.Add(placement.Box);
                svg.Append("<text class=\"station-label\" x=\"").Append(F(placement.X))
                    .Append("\" y=\"").Append(F(placement.BaselineY)).Append("\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"")
                    .Append(F(options.LabelFontSize)).Append("\" font-weight=\"").Append(request.Important ? "700" : "500");
                if (placement.Anchor != "start")
                {
                    svg.Append("\" text-anchor=\"").Append(placement.Anchor);
                }

                svg.Append("\" data-station-id=\"").Append(Xml(request.Station.Id))
                    .Append("\" data-label-position=\"").Append(placement.PositionName)
                    .Append("\" data-label-priority=\"").Append(request.Priority)
                    .Append("\" data-label-overlap-area=\"").Append(F(placement.LabelOverlapArea))
                    .Append("\" fill=\"#17212b\" paint-order=\"stroke\" stroke=\"#fbfcfd\" stroke-width=\"3\" stroke-linejoin=\"round\">")
                    .Append(Xml(request.Text)).Append("</text>");
            }
        }

        private static void AppendLegacyLabels(
            StringBuilder svg,
            PortableRenderOptions options,
            Dictionary<string, MetroSnapshotStation> stations,
            Dictionary<string, Point2> stationPoints,
            HashSet<string> terminalIds,
            MapFrame mapFrame)
        {
            List<Rect2> labelBoxes = new List<Rect2>();
            foreach (KeyValuePair<string, Point2> pair in stationPoints.OrderBy(item => item.Value.Y).ThenBy(item => item.Value.X))
            {
                MetroSnapshotStation station;
                if (!stations.TryGetValue(pair.Key, out station) || ShouldHideGeneric(station.Name, options))
                {
                    continue;
                }

                double width = Math.Max(station.Name.Length, 2) * options.LabelFontSize * 0.72;
                LabelPlacement placement = PlaceLegacyLabel(pair.Value, width, options.LabelFontSize, mapFrame);
                bool important = station.IsInterchange || terminalIds.Contains(station.Id);
                if (options.HideCrowdedLabels && !important && labelBoxes.Any(existing => existing.Intersects(placement.Box)))
                {
                    continue;
                }

                labelBoxes.Add(placement.Box);
                svg.Append("<text class=\"station-label\" x=\"").Append(F(placement.X))
                    .Append("\" y=\"").Append(F(placement.BaselineY)).Append("\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"")
                    .Append(F(options.LabelFontSize)).Append("\" font-weight=\"").Append(important ? "700" : "500")
                    .Append("\" fill=\"#17212b\" paint-order=\"stroke\" stroke=\"#fbfcfd\" stroke-width=\"3\" stroke-linejoin=\"round\">")
                    .Append(Xml(station.Name)).Append("</text>");
            }
        }

        private static List<RenderedRoute> BuildRenderedRoutes(
            PortableRenderOptions options,
            List<DisplayRoute> routes,
            Dictionary<string, Point2> stationPoints,
            SourceProjector projector)
        {
            List<RenderedRoute> rendered = new List<RenderedRoute>();
            foreach (DisplayRoute route in routes)
            {
                bool usesPathPoints = options.LayoutMode == PortableLayoutMode.Geographic && route.Line.PathPoints.Count >= 2;
                IReadOnlyList<string> routeStops = options.LayoutMode == PortableLayoutMode.SchematicAnneal
                    ? route.Stops
                    : route.Line.Stops;
                List<Point2> points = usesPathPoints
                    ? route.Line.PathPoints.Select(point => projector.Project(point.X, point.Z)).ToList()
                    : routeStops.Where(stationPoints.ContainsKey).Select(id => stationPoints[id]).ToList();
                if (points.Count >= 2)
                {
                    rendered.Add(new RenderedRoute(route, points, usesPathPoints, routeStops.Count));
                }
            }

            if (options.LayoutMode == PortableLayoutMode.SchematicAnneal)
            {
                ApplyParallelCorridorOffsets(rendered, options);
            }

            return rendered;
        }

        // ---- Parallel shared corridors ----
        //
        // Port of the desktop AnnealParallel behavior: edges shared by two or
        // more VISIBLE LANES (distinct stroke colors; a line and its same-color
        // branch collapse onto one lane) get symmetric per-lane offsets along
        // the edge normal. A vertex between two shared segments is placed at
        // the INTERSECTION of the two offset lines (miter join), so the lane
        // spacing survives bends; single-sided ramp vertices take the full
        // offset of their one shared edge.
        private const double ParallelEndpointEpsilon = 0.5;

        private static void ApplyParallelCorridorOffsets(List<RenderedRoute> rendered, PortableRenderOptions options)
        {
            Dictionary<string, ParallelSharedEdge> plan = BuildParallelCorridorPlan(rendered, options);
            if (plan.Count == 0)
            {
                return;
            }

            foreach (RenderedRoute route in rendered)
            {
                string laneKey = GetLaneKey(route.Route);
                List<Point2> points = route.Points;
                if (points.Count < 2)
                {
                    continue;
                }

                Point2[] normals = new Point2[points.Count - 1];
                double[] offsets = new double[points.Count - 1];
                bool[] shared = new bool[points.Count - 1];
                bool touchesShared = false;
                for (int i = 1; i < points.Count; i++)
                {
                    ParallelSharedEdge edge;
                    double laneOffset;
                    if (plan.TryGetValue(ParallelEdgeKey(points[i - 1], points[i]), out edge)
                        && edge.OffsetByLane.TryGetValue(laneKey, out laneOffset))
                    {
                        normals[i - 1] = edge.Normal;
                        offsets[i - 1] = laneOffset;
                        shared[i - 1] = true;
                        touchesShared = true;
                    }
                }

                if (!touchesShared)
                {
                    continue;
                }

                List<Point2> result = new List<Point2>(points.Count);
                for (int i = 0; i < points.Count; i++)
                {
                    bool hasPrevious = i - 1 >= 0 && shared[i - 1];
                    bool hasNext = i < shared.Length && shared[i];
                    if (!hasPrevious && !hasNext)
                    {
                        result.Add(points[i]);
                        continue;
                    }

                    if (hasPrevious && hasNext)
                    {
                        // Solve x . n1 = d1, x . n2 = d2 (offset-line intersection);
                        // fall back to the average when nearly collinear, where the
                        // average IS the exact solution.
                        Point2 n1 = normals[i - 1];
                        Point2 n2 = normals[i];
                        double d1 = offsets[i - 1];
                        double d2 = offsets[i];
                        double determinant = (n1.X * n2.Y) - (n1.Y * n2.X);
                        if (Math.Abs(determinant) > 0.3)
                        {
                            result.Add(new Point2(
                                points[i].X + (((d1 * n2.Y) - (d2 * n1.Y)) / determinant),
                                points[i].Y + (((d2 * n1.X) - (d1 * n2.X)) / determinant)));
                        }
                        else
                        {
                            result.Add(new Point2(
                                points[i].X + (((n1.X * d1) + (n2.X * d2)) / 2),
                                points[i].Y + (((n1.Y * d1) + (n2.Y * d2)) / 2)));
                        }

                        continue;
                    }

                    Point2 normal = hasPrevious ? normals[i - 1] : normals[i];
                    double offsetValue = hasPrevious ? offsets[i - 1] : offsets[i];
                    result.Add(new Point2(points[i].X + (normal.X * offsetValue), points[i].Y + (normal.Y * offsetValue)));
                }

                points.Clear();
                points.AddRange(result);
            }
        }

        private static Dictionary<string, ParallelSharedEdge> BuildParallelCorridorPlan(
            List<RenderedRoute> rendered,
            PortableRenderOptions options)
        {
            Dictionary<string, SharedEdgeAccumulator> edges = new Dictionary<string, SharedEdgeAccumulator>(StringComparer.Ordinal);
            foreach (RenderedRoute route in rendered)
            {
                string laneKey = GetLaneKey(route.Route);
                int laneOrder = ExtractLineNumber(route.Route.DisplayName);
                for (int i = 1; i < route.Points.Count; i++)
                {
                    Point2 a = route.Points[i - 1];
                    Point2 b = route.Points[i];
                    if (DistanceSquared(a, b) < 0.001)
                    {
                        continue;
                    }

                    string key = ParallelEdgeKey(a, b);
                    SharedEdgeAccumulator accumulator;
                    if (!edges.TryGetValue(key, out accumulator))
                    {
                        accumulator = new SharedEdgeAccumulator(a, b);
                        edges.Add(key, accumulator);
                    }

                    accumulator.AddLane(laneKey, laneOrder);
                }
            }

            double spacing = Math.Max(Math.Max(1, options.RouteWidth) * 1.05, Math.Max(8, options.GridSize) * 0.14);
            Dictionary<string, ParallelSharedEdge> plan = new Dictionary<string, ParallelSharedEdge>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, SharedEdgeAccumulator> pair in edges)
            {
                if (pair.Value.Lanes.Count < 2)
                {
                    continue;
                }

                List<string> laneKeys = pair.Value.Lanes
                    .OrderBy(lane => lane.Value)
                    .ThenBy(lane => lane.Key, StringComparer.Ordinal)
                    .Select(lane => lane.Key)
                    .ToList();

                double dx = pair.Value.B.X - pair.Value.A.X;
                double dy = pair.Value.B.Y - pair.Value.A.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length < 0.001)
                {
                    continue;
                }

                Point2 normal = new Point2(-dy / length, dx / length);
                Dictionary<string, double> offsetByLane = new Dictionary<string, double>(StringComparer.Ordinal);
                double center = (laneKeys.Count - 1) / 2.0;
                for (int i = 0; i < laneKeys.Count; i++)
                {
                    offsetByLane[laneKeys[i]] = (i - center) * spacing;
                }

                plan.Add(pair.Key, new ParallelSharedEdge(normal, offsetByLane));
            }

            return plan;
        }

        // Visible lane = distinct stroke color: a line and its same-color branch
        // share one lane, so they overlap as a single line instead of drawing a
        // doubled stripe. Only genuinely different-colored lines separate.
        private static string GetLaneKey(DisplayRoute route)
        {
            string color = NormalizeColor(route.Line.Color);
            return string.IsNullOrWhiteSpace(color) ? route.DisplayName : color.ToUpperInvariant();
        }

        // Canonical undirected key with quantized endpoints, so an out-and-back
        // traversal maps to the same edge from either direction.
        private static string ParallelEdgeKey(Point2 a, Point2 b)
        {
            long ax = (long)Math.Round(a.X / ParallelEndpointEpsilon);
            long ay = (long)Math.Round(a.Y / ParallelEndpointEpsilon);
            long bx = (long)Math.Round(b.X / ParallelEndpointEpsilon);
            long by = (long)Math.Round(b.Y / ParallelEndpointEpsilon);
            bool aFirst = ax < bx || (ax == bx && ay <= by);
            return aFirst
                ? ax.ToString(CultureInfo.InvariantCulture) + "," + ay.ToString(CultureInfo.InvariantCulture) + "|" + bx.ToString(CultureInfo.InvariantCulture) + "," + by.ToString(CultureInfo.InvariantCulture)
                : bx.ToString(CultureInfo.InvariantCulture) + "," + by.ToString(CultureInfo.InvariantCulture) + "|" + ax.ToString(CultureInfo.InvariantCulture) + "," + ay.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class SharedEdgeAccumulator
        {
            public SharedEdgeAccumulator(Point2 a, Point2 b)
            {
                A = a;
                B = b;
                Lanes = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            public Point2 A { get; }
            public Point2 B { get; }
            public Dictionary<string, int> Lanes { get; }

            public void AddLane(string laneKey, int laneOrder)
            {
                int existing;
                if (!Lanes.TryGetValue(laneKey, out existing) || laneOrder < existing)
                {
                    Lanes[laneKey] = laneOrder;
                }
            }
        }

        private sealed class ParallelSharedEdge
        {
            public ParallelSharedEdge(Point2 normal, Dictionary<string, double> offsetByLane)
            {
                Normal = normal;
                OffsetByLane = offsetByLane;
            }

            public Point2 Normal { get; }
            public Dictionary<string, double> OffsetByLane { get; }
        }

        private static HashSet<string> FindTerminalIds(List<DisplayRoute> routes, bool useNormalizedStops)
        {
            HashSet<string> terminals = new HashSet<string>(StringComparer.Ordinal);
            foreach (DisplayRoute route in routes)
            {
                IReadOnlyList<string> stops = useNormalizedStops ? route.Stops : route.Line.Stops;
                if (stops.Count > 0)
                {
                    terminals.Add(stops[0]);
                    terminals.Add(stops[stops.Count - 1]);
                }
            }

            return terminals;
        }

        private static bool ShouldHideGeneric(string name, PortableRenderOptions options)
        {
            if (options.ShowGenericStationNames)
            {
                return false;
            }

            return IsGenericName(name);
        }

        private static Point2 ClampToMap(Point2 point, PortableRenderOptions options)
        {
            MapFrame frame = CreateMapFrame(options);
            return new Point2(Clamp(point.X, frame.Left, frame.Right), Clamp(point.Y, frame.Top, frame.Bottom));
        }

        private static MapFrame CreateMapFrame(PortableRenderOptions options)
        {
            double markerRadius = Math.Max(options.StationRadius * 1.55, options.StationRadius * 1.25);
            double safetyInset = Math.Max(markerRadius + 3, (options.RouteWidth / 2) + 3);
            double left = options.Padding + safetyInset;
            double right = options.Width - options.Padding - options.LegendWidth - safetyInset;
            double top = options.Padding + 60 + safetyInset;
            double bottom = options.Height - options.Padding - safetyInset;

            if (right < left)
            {
                double center = (left + right) / 2;
                left = center;
                right = center;
            }

            if (bottom < top)
            {
                double center = (top + bottom) / 2;
                top = center;
                bottom = center;
            }

            return new MapFrame(left, right, top, bottom);
        }

        private static LabelRequest CreateLabelRequest(
            MetroSnapshotStation station,
            Point2 point,
            HashSet<string> terminalIds,
            PortableRenderOptions options,
            int index)
        {
            bool terminal = terminalIds.Contains(station.Id);
            bool generic = IsGenericName(station.Name);
            int priority = (station.IsInterchange ? 100 : 0) + (terminal ? 70 : 0) + (generic ? -35 : 25);
            return new LabelRequest(
                station,
                point,
                string.IsNullOrWhiteSpace(station.Name) ? station.Id : station.Name,
                GetStationRadius(station, terminalIds, options),
                priority,
                index,
                station.IsInterchange || terminal);
        }

        private static LabelPlacement PlaceLabel(
            LabelRequest request,
            PortableRenderOptions options,
            MapFrame frame,
            List<Rect2> placedLabels,
            List<Rect2> stationObstacles,
            List<Segment2> routeObstacles)
        {
            double fontSize = options.LabelFontSize;
            double width = Math.Min(EstimateTextWidth(request.Text, fontSize), Math.Max(frame.Right - frame.Left, 1));
            double height = fontSize * 1.25;
            double routeStroke = Math.Max(1, options.RouteWidth);

            // Two rings of candidates: the near ring hugs the station like the
            // desktop renderer; the far ring (same slots, ~2.2x offset) lets a
            // label step outward in dense clusters instead of fusing with a
            // neighbour. Near slots win ties via the index tiebreak, so the far
            // ring is only used when it actually avoids a collision.
            List<LabelPlacement> candidates = new List<LabelPlacement>(16);
            AddCandidateRing(candidates, request, width, height, fontSize, frame, request.StationRadius + 6, "");
            AddCandidateRing(candidates, request, width, height, fontSize, frame, (request.StationRadius + 6) * 2.2, "-far");

            LabelPlacement best = candidates[0];
            double bestScore = double.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                LabelPlacement candidate = candidates[i];
                double labelOverlapArea = placedLabels.Sum(box => candidate.Box.OverlapArea(box));
                double stationOverlapArea = stationObstacles.Sum(box => candidate.Box.OverlapArea(box));
                double routeCoveredLength = routeObstacles.Sum(segment => SegmentLengthInsideRect(segment.A, segment.B, candidate.Box));
                double score = (labelOverlapArea * 16) + (stationOverlapArea * 8) + (routeCoveredLength * routeStroke * 6) + (i * 0.01);
                if (score < bestScore)
                {
                    best = new LabelPlacement(
                        candidate.X,
                        candidate.BaselineY,
                        candidate.Box,
                        candidate.PositionName,
                        labelOverlapArea,
                        score,
                        candidate.Anchor);
                    bestScore = score;
                }
            }

            return best;
        }

        private static void AddCandidateRing(
            List<LabelPlacement> candidates,
            LabelRequest request,
            double width,
            double height,
            double fontSize,
            MapFrame frame,
            double offset,
            string nameSuffix)
        {
            candidates.Add(CreateLabelPlacement("right" + nameSuffix, request.Point.X + offset, request.Point.Y - (height / 2), width, height, fontSize, frame, "start"));
            candidates.Add(CreateLabelPlacement("left" + nameSuffix, request.Point.X - offset - width, request.Point.Y - (height / 2), width, height, fontSize, frame, "end"));
            candidates.Add(CreateLabelPlacement("top" + nameSuffix, request.Point.X - (width / 2), request.Point.Y - offset - height, width, height, fontSize, frame, "middle"));
            candidates.Add(CreateLabelPlacement("bottom" + nameSuffix, request.Point.X - (width / 2), request.Point.Y + offset, width, height, fontSize, frame, "middle"));
            candidates.Add(CreateLabelPlacement("top-right" + nameSuffix, request.Point.X + (offset * 0.8), request.Point.Y - offset - height, width, height, fontSize, frame, "start"));
            candidates.Add(CreateLabelPlacement("bottom-right" + nameSuffix, request.Point.X + (offset * 0.8), request.Point.Y + (offset * 0.75), width, height, fontSize, frame, "start"));
            candidates.Add(CreateLabelPlacement("top-left" + nameSuffix, request.Point.X - (offset * 0.8) - width, request.Point.Y - offset - height, width, height, fontSize, frame, "end"));
            candidates.Add(CreateLabelPlacement("bottom-left" + nameSuffix, request.Point.X - (offset * 0.8) - width, request.Point.Y + (offset * 0.75), width, height, fontSize, frame, "end"));
        }

        // Length of the segment portion inside the rectangle (Liang-Barsky clip),
        // matching the desktop renderer's route-under-label penalty.
        private static double SegmentLengthInsideRect(Point2 a, Point2 b, Rect2 rect)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double t0 = 0;
            double t1 = 1;
            if (!ClipParameter(-dx, a.X - rect.X, ref t0, ref t1)
                || !ClipParameter(dx, rect.Right - a.X, ref t0, ref t1)
                || !ClipParameter(-dy, a.Y - rect.Y, ref t0, ref t1)
                || !ClipParameter(dy, rect.Bottom - a.Y, ref t0, ref t1))
            {
                return 0;
            }

            return t1 > t0 ? Math.Sqrt((dx * dx) + (dy * dy)) * (t1 - t0) : 0;
        }

        private static bool ClipParameter(double p, double q, ref double t0, ref double t1)
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

        private static LabelPlacement PlaceLegacyLabel(Point2 station, double estimatedWidth, double fontSize, MapFrame frame)
        {
            double width = Math.Min(estimatedWidth, Math.Max(frame.Right - frame.Left, 1));
            double x = station.X + 9;
            if (x + width > frame.Right)
            {
                x = station.X - 9 - width;
            }

            x = Clamp(x, frame.Left, Math.Max(frame.Left, frame.Right - width));
            double baselineY = Clamp(station.Y + 4, frame.Top + fontSize, Math.Max(frame.Top + fontSize, frame.Bottom - 4));
            return new LabelPlacement(
                x,
                baselineY,
                new Rect2(x, baselineY - fontSize, width, fontSize + 4),
                "legacy",
                0,
                0);
        }

        private static LabelPlacement CreateLabelPlacement(
            string positionName,
            double x,
            double top,
            double width,
            double height,
            double fontSize,
            MapFrame frame,
            string anchor = "start")
        {
            x = Clamp(x, frame.Left, Math.Max(frame.Left, frame.Right - width));
            top = Clamp(top, frame.Top, Math.Max(frame.Top, frame.Bottom - height));

            // The collision box is always the full text extent; the text x is the
            // anchor point so the rendered glyphs stay inside the estimated box
            // even when the runtime font metrics differ from the estimate.
            double textX = anchor == "middle" ? x + (width / 2) : anchor == "end" ? x + width : x;
            return new LabelPlacement(textX, top + fontSize, new Rect2(x, top, width, height), positionName, 0, 0, anchor);
        }

        private static double EstimateTextWidth(string text, double fontSize)
        {
            double units = 0;
            foreach (char character in text ?? string.Empty)
            {
                if (char.IsWhiteSpace(character))
                {
                    units += 0.35;
                }
                else if (character <= 0x7f)
                {
                    units += 0.58;
                }
                else
                {
                    units += 1.0;
                }
            }

            double languageAwareWidth = Math.Max(units, 2) * fontSize;
            double legacySafeWidth = Math.Max((text ?? string.Empty).Length, 2) * fontSize * 0.72;
            return Math.Max(languageAwareWidth, legacySafeWidth);
        }

        private static List<Segment2> BuildRouteObstacles(List<RenderedRoute> routes)
        {
            List<Segment2> segments = new List<Segment2>();
            foreach (RenderedRoute route in routes)
            {
                for (int i = 1; i < route.Points.Count; i++)
                {
                    segments.Add(new Segment2(route.Points[i - 1], route.Points[i]));
                }
            }

            return segments;
        }

        private static bool SegmentIntersectsRect(Point2 a, Point2 b, Rect2 rect)
        {
            if (rect.Contains(a) || rect.Contains(b))
            {
                return true;
            }

            Point2 topLeft = new Point2(rect.X, rect.Y);
            Point2 topRight = new Point2(rect.Right, rect.Y);
            Point2 bottomRight = new Point2(rect.Right, rect.Bottom);
            Point2 bottomLeft = new Point2(rect.X, rect.Bottom);
            return SegmentsCrossOrTouch(a, b, topLeft, topRight)
                || SegmentsCrossOrTouch(a, b, topRight, bottomRight)
                || SegmentsCrossOrTouch(a, b, bottomRight, bottomLeft)
                || SegmentsCrossOrTouch(a, b, bottomLeft, topLeft);
        }

        private static bool SegmentsCrossOrTouch(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X)
                || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
                || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y)
                || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y))
            {
                return false;
            }

            double first = Orientation(a, b, c);
            double second = Orientation(a, b, d);
            double third = Orientation(c, d, a);
            double fourth = Orientation(c, d, b);
            return first * second <= 0 && third * fourth <= 0;
        }

        private static double GetStationRadius(MetroSnapshotStation station, HashSet<string> terminalIds, PortableRenderOptions options)
        {
            double radius = station.IsInterchange ? options.StationRadius * 1.55 : options.StationRadius;
            return terminalIds.Contains(station.Id) ? Math.Max(radius, options.StationRadius * 1.25) : radius;
        }

        private static bool IsGenericName(string name)
        {
            string value = (name ?? string.Empty).Trim();
            string baseName = StripParentheticalSuffix(value);
            return baseName == "小型地铁广场" || baseName == "现代地铁站" || baseName == "地下地铁站"
                || baseName == "地铁站" || baseName == "高架地铁站"
                || baseName.Equals("Subway Station", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("Metro Station", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("Elevated Metro Station", StringComparison.OrdinalIgnoreCase)
                || (value.StartsWith("Station ", StringComparison.OrdinalIgnoreCase) && value.Substring(8).All(char.IsDigit));
        }

        // Default CS2 assets ship bracketed variants ("高架地铁站（小型）",
        // "地铁站（高架旁路）"): strip one trailing parenthesized suffix and
        // compare the base name. Player names keep their own base, so a
        // renamed "中央站（东）" is unaffected.
        private static string StripParentheticalSuffix(string value)
        {
            if (value.Length == 0)
            {
                return value;
            }

            char last = value[value.Length - 1];
            char open;
            if (last == '）') { open = '（'; }
            else if (last == ')') { open = '('; }
            else { return value; }

            int openIndex = value.LastIndexOf(open);
            return openIndex > 0 ? value.Substring(0, openIndex).TrimEnd() : value;
        }

        private static double Snap(double value, double grid) { return Math.Round(value / grid) * grid; }

        private static double Clamp(double value, double minimum, double maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= Math.PI * 2;
            while (angle < -Math.PI) angle += Math.PI * 2;
            return angle;
        }

        private static bool SegmentsCross(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X)
                || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
                || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y)
                || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y))
            {
                return false;
            }

            return Orientation(a, b, c) * Orientation(a, b, d) < 0 && Orientation(c, d, a) * Orientation(c, d, b) < 0;
        }

        private static double Orientation(Point2 a, Point2 b, Point2 c) { return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)); }

        private static double DistanceSquared(Point2 a, Point2 b) { double dx = a.X - b.X; double dy = a.Y - b.Y; return (dx * dx) + (dy * dy); }

        private static string NormalizeColor(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Length == 7 && value[0] == '#') return value;
            return "#4b67c8";
        }

        private static string GetTitle(string cityName, string titleSuffix)
        {
            string suffix = string.IsNullOrEmpty(titleSuffix) ? " Metro Diagram" : titleSuffix;
            return string.IsNullOrWhiteSpace(cityName) ? "CS2" + suffix : cityName + suffix;
        }

        private static string Points(IEnumerable<Point2> points) { return string.Join(" ", points.Select(point => F(point.X) + "," + F(point.Y)).ToArray()); }

        private static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Xml(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private sealed class DisplayRoute
        {
            public DisplayRoute(string displayName, MetroSnapshotLine line)
            {
                DisplayName = displayName;
                Line = line;
                Stops = NormalizeRouteStops(line.Stops);
            }

            public string DisplayName { get; }
            public MetroSnapshotLine Line { get; }
            public List<string> Stops { get; }
        }

        private sealed class RenderedRoute
        {
            public RenderedRoute(DisplayRoute route, List<Point2> points, bool usesPathPoints, int renderedStopCount)
            {
                Route = route;
                Points = points;
                UsesPathPoints = usesPathPoints;
                RenderedStopCount = renderedStopCount;
            }

            public DisplayRoute Route { get; }
            public List<Point2> Points { get; }
            public bool UsesPathPoints { get; }
            public int RenderedStopCount { get; }
        }

        private sealed class LabelRequest
        {
            public LabelRequest(
                MetroSnapshotStation station,
                Point2 point,
                string text,
                double stationRadius,
                int priority,
                int index,
                bool important)
            {
                Station = station;
                Point = point;
                Text = text;
                StationRadius = stationRadius;
                Priority = priority;
                Index = index;
                Important = important;
            }

            public MetroSnapshotStation Station { get; }
            public Point2 Point { get; }
            public string Text { get; }
            public double StationRadius { get; }
            public int Priority { get; }
            public int Index { get; }
            public bool Important { get; }
        }

        private sealed class Edge
        {
            public Edge(string a, string b) { A = a; B = b; }
            public string A { get; }
            public string B { get; }
            public bool SharesStation(Edge other) { return A == other.A || A == other.B || B == other.A || B == other.B; }
        }

        private sealed class SourceProjector
        {
            private SourceProjector(double minX, double maxZ, double scale, double originX, double originY)
            {
                MinX = minX;
                MaxZ = maxZ;
                Scale = scale;
                OriginX = originX;
                OriginY = originY;
            }

            private double MinX { get; }
            private double MaxZ { get; }
            private double Scale { get; }
            private double OriginX { get; }
            private double OriginY { get; }

            public Point2 Project(double x, double z)
            {
                return new Point2(OriginX + ((x - MinX) * Scale), OriginY + ((MaxZ - z) * Scale));
            }

            public static SourceProjector Create(MetroNetworkSnapshot snapshot, PortableRenderOptions options, bool includePathPoints)
            {
                List<double> xs = snapshot.Stations.Select(station => station.X).ToList();
                List<double> zs = snapshot.Stations.Select(station => station.Z).ToList();
                if (includePathPoints)
                {
                    foreach (MetroSnapshotLine line in snapshot.Lines)
                    {
                        foreach (MetroSnapshotPathPoint point in line.PathPoints)
                        {
                            xs.Add(point.X);
                            zs.Add(point.Z);
                        }
                    }
                }

                if (xs.Count == 0)
                {
                    xs.Add(0);
                    zs.Add(0);
                }

                double minX = xs.Min();
                double maxX = xs.Max();
                double minZ = zs.Min();
                double maxZ = zs.Max();
                double sourceWidth = Math.Max(maxX - minX, 1);
                double sourceHeight = Math.Max(maxZ - minZ, 1);
                MapFrame frame = CreateMapFrame(options);
                double drawWidth = Math.Max(frame.Right - frame.Left, 1);
                double drawHeight = Math.Max(frame.Bottom - frame.Top, 1);
                double scale = Math.Min(drawWidth / sourceWidth, drawHeight / sourceHeight);
                double originX = frame.Left + ((drawWidth - (sourceWidth * scale)) / 2);
                double originY = frame.Top + ((drawHeight - (sourceHeight * scale)) / 2);
                return new SourceProjector(minX, maxZ, scale, originX, originY);
            }
        }

        private struct MapFrame
        {
            public MapFrame(double left, double right, double top, double bottom) { Left = left; Right = right; Top = top; Bottom = bottom; }
            public double Left { get; }
            public double Right { get; }
            public double Top { get; }
            public double Bottom { get; }
        }

        private struct LabelPlacement
        {
            public LabelPlacement(
                double x,
                double baselineY,
                Rect2 box,
                string positionName,
                double labelOverlapArea,
                double score,
                string anchor = "start")
            {
                X = x;
                BaselineY = baselineY;
                Box = box;
                PositionName = positionName;
                LabelOverlapArea = labelOverlapArea;
                Score = score;
                Anchor = anchor;
            }

            public double X { get; }
            public double BaselineY { get; }
            public Rect2 Box { get; }
            public string PositionName { get; }
            public double LabelOverlapArea { get; }
            public double Score { get; }
            public string Anchor { get; }
        }

        private struct Point2
        {
            public Point2(double x, double y) { X = x; Y = y; }
            public double X { get; }
            public double Y { get; }
        }

        private struct Segment2
        {
            public Segment2(Point2 a, Point2 b) { A = a; B = b; }
            public Point2 A { get; }
            public Point2 B { get; }
        }

        private struct Rect2
        {
            public Rect2(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
            public double X { get; }
            public double Y { get; }
            public double Width { get; }
            public double Height { get; }
            public double Right { get { return X + Width; } }
            public double Bottom { get { return Y + Height; } }
            public bool Intersects(Rect2 other) { return X < other.X + other.Width && X + Width > other.X && Y < other.Y + other.Height && Y + Height > other.Y; }
            public double OverlapArea(Rect2 other)
            {
                double width = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X));
                double height = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
                return width * height;
            }

            public bool Contains(Point2 point) { return point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom; }

            public static Rect2 FromCenter(Point2 point, double width, double height)
            {
                return new Rect2(point.X - (width / 2), point.Y - (height / 2), width, height);
            }
        }
    }
}
