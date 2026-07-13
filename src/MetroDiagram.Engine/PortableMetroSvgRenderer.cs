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
                ApplySchematicAnneal(projectedStations, routes, options, snapshot.Revision);
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

        private static List<DisplayRoute> ResolveDisplayRoutes(IReadOnlyList<MetroSnapshotLine> lines, bool mergeFamilies)
        {
            if (!mergeFamilies)
            {
                return lines.Select(line => new DisplayRoute(GetFamilyKey(line), line)).ToList();
            }

            return lines
                .GroupBy(GetFamilyKey, StringComparer.CurrentCulture)
                .Select(group => new DisplayRoute(
                    group.Key,
                    group.OrderByDescending(line => line.Stops.Count)
                        .ThenByDescending(line => line.PathPoints.Count)
                        .ThenBy(line => line.Name, StringComparer.CurrentCulture)
                        .First()))
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

        private static void ApplySchematicAnneal(
            Dictionary<string, Point2> points,
            List<DisplayRoute> routes,
            PortableRenderOptions options,
            string revision)
        {
            double grid = options.GridSize;
            List<Edge> edges = BuildEdges(routes, points);
            if (edges.Count == 0)
            {
                return;
            }

            Dictionary<string, Point2> anchors = points.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (string stationId in points.Keys.ToArray())
            {
                Point2 point = points[stationId];
                points[stationId] = ClampToMap(new Point2(Snap(point.X, grid), Snap(point.Y, grid)), options);
            }

            Dictionary<string, List<Edge>> incident = BuildIncidentEdges(edges);
            int seed = RevisionSeed(revision);
            Random random = new Random(seed);
            string[] stationIds = points.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            int attempts = Math.Min(Math.Max(stationIds.Length * 45, 800), Math.Max(options.AnnealAttemptLimit, 800));
            double temperature = 8.0;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                string stationId = stationIds[random.Next(stationIds.Length)];
                Point2 current = points[stationId];
                int direction = random.Next(8);
                double dx = DirectionX(direction) * grid;
                double dy = DirectionY(direction) * grid;
                Point2 candidate = new Point2(current.X + dx, current.Y + dy);
                candidate = ClampToMap(candidate, options);

                double before = LocalScore(stationId, current, points, anchors, incident, edges, grid);
                points[stationId] = candidate;
                double after = LocalScore(stationId, candidate, points, anchors, incident, edges, grid);
                double delta = after - before;
                bool accept = delta <= 0 || random.NextDouble() < Math.Exp(-delta / Math.Max(temperature, 0.05));
                if (!accept)
                {
                    points[stationId] = current;
                }

                temperature *= 0.9992;
            }
        }

        private static List<Edge> BuildEdges(List<DisplayRoute> routes, Dictionary<string, Point2> points)
        {
            List<Edge> edges = new List<Edge>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DisplayRoute route in routes)
            {
                for (int i = 1; i < route.Line.Stops.Count; i++)
                {
                    string a = route.Line.Stops[i - 1];
                    string b = route.Line.Stops[i];
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

        private static double LocalScore(
            string stationId,
            Point2 position,
            Dictionary<string, Point2> points,
            Dictionary<string, Point2> anchors,
            Dictionary<string, List<Edge>> incident,
            List<Edge> allEdges,
            double grid)
        {
            Point2 anchor = anchors[stationId];
            double score = DistanceSquared(position, anchor) / (grid * grid * 18);
            foreach (KeyValuePair<string, Point2> pair in points)
            {
                if (pair.Key == stationId)
                {
                    continue;
                }

                double distance = Math.Sqrt(DistanceSquared(position, pair.Value));
                if (distance < grid * 1.25)
                {
                    double deficit = (grid * 1.25) - distance;
                    score += deficit * deficit * 0.6;
                }
            }

            List<Edge> localEdges;
            if (!incident.TryGetValue(stationId, out localEdges))
            {
                return score;
            }

            foreach (Edge edge in localEdges)
            {
                Point2 other = points[edge.A == stationId ? edge.B : edge.A];
                double dx = other.X - position.X;
                double dy = other.Y - position.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length < grid * 1.5)
                {
                    double deficit = (grid * 1.5) - length;
                    score += deficit * deficit * 0.35;
                }

                double angle = Math.Atan2(dy, dx);
                double nearest = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
                double deviation = Math.Abs(NormalizeAngle(angle - nearest));
                score += deviation * deviation * 900;

                foreach (Edge otherEdge in allEdges)
                {
                    if (otherEdge.SharesStation(edge))
                    {
                        continue;
                    }

                    if (SegmentsCross(position, other, points[otherEdge.A], points[otherEdge.B]))
                    {
                        score += 180;
                    }
                }
            }

            return score;
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
                .Append("\">");
            svg.Append("<title>").Append(Xml(GetTitle(snapshot.CityName))).Append("</title>");
            svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fbfcfd\"/>");
            svg.Append("<text x=\"").Append(options.Padding).Append("\" y=\"46\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"24\" font-weight=\"700\" fill=\"#18222d\">")
                .Append(Xml(GetTitle(snapshot.CityName))).Append("</text>");

            foreach (DisplayRoute route in routes)
            {
                bool usesPathPoints = options.LayoutMode == PortableLayoutMode.Geographic && route.Line.PathPoints.Count >= 2;
                List<Point2> routePoints = usesPathPoints
                    ? route.Line.PathPoints.Select(point => projector.Project(point.X, point.Z)).ToList()
                    : route.Line.Stops.Where(stationPoints.ContainsKey).Select(id => stationPoints[id]).ToList();
                if (routePoints.Count < 2)
                {
                    continue;
                }

                svg.Append("<polyline class=\"route\" data-line-id=\"").Append(Xml(route.Line.Id))
                    .Append("\" data-display-family=\"").Append(Xml(route.DisplayName))
                    .Append("\" data-route-source=\"").Append(usesPathPoints ? "pathPoints" : "stops")
                    .Append("\" fill=\"none\" stroke=\"").Append(Xml(NormalizeColor(route.Line.Color)))
                    .Append("\" stroke-width=\"").Append(F(options.RouteWidth))
                    .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" points=\"")
                    .Append(Points(routePoints)).Append("\"/>");
            }

            HashSet<string> terminalIds = FindTerminalIds(routes);
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

            List<Rect2> labelBoxes = new List<Rect2>();
            foreach (KeyValuePair<string, Point2> pair in stationPoints.OrderBy(item => item.Value.Y).ThenBy(item => item.Value.X))
            {
                MetroSnapshotStation station;
                if (!stations.TryGetValue(pair.Key, out station) || ShouldHideGeneric(station.Name, options))
                {
                    continue;
                }

                double width = Math.Max(station.Name.Length, 2) * options.LabelFontSize * 0.72;
                LabelPlacement placement = PlaceLabel(pair.Value, width, options.LabelFontSize, mapFrame);
                Rect2 box = placement.Box;
                bool important = station.IsInterchange || terminalIds.Contains(station.Id);
                if (options.HideCrowdedLabels && !important && labelBoxes.Any(existing => existing.Intersects(box)))
                {
                    continue;
                }

                labelBoxes.Add(box);
                svg.Append("<text class=\"station-label\" x=\"").Append(F(placement.X))
                    .Append("\" y=\"").Append(F(placement.BaselineY)).Append("\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"")
                    .Append(F(options.LabelFontSize)).Append("\" font-weight=\"").Append(important ? "700" : "500")
                    .Append("\" fill=\"#17212b\" paint-order=\"stroke\" stroke=\"#fbfcfd\" stroke-width=\"3\" stroke-linejoin=\"round\">")
                    .Append(Xml(station.Name)).Append("</text>");
            }

            double legendX = options.Width - options.LegendWidth + 24;
            svg.Append("<g class=\"legend\"><text x=\"").Append(F(legendX)).Append("\" y=\"72\" font-family=\"").Append(SvgFontFamily).Append("\" font-size=\"16\" font-weight=\"700\" fill=\"#263442\">Lines</text>");
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

        private static HashSet<string> FindTerminalIds(List<DisplayRoute> routes)
        {
            HashSet<string> terminals = new HashSet<string>(StringComparer.Ordinal);
            foreach (DisplayRoute route in routes)
            {
                if (route.Line.Stops.Count > 0)
                {
                    terminals.Add(route.Line.Stops[0]);
                    terminals.Add(route.Line.Stops[route.Line.Stops.Count - 1]);
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

            string value = (name ?? string.Empty).Trim();
            return value == "小型地铁广场" || value == "现代地铁站" || value == "地下地铁站" || value == "地铁站"
                || value.Equals("Subway Station", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Metro Station", StringComparison.OrdinalIgnoreCase)
                || (value.StartsWith("Station ", StringComparison.OrdinalIgnoreCase) && value.Substring(8).All(char.IsDigit));
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

        private static LabelPlacement PlaceLabel(Point2 station, double estimatedWidth, double fontSize, MapFrame frame)
        {
            double width = Math.Min(estimatedWidth, Math.Max(frame.Right - frame.Left, 1));
            double x = station.X + 9;
            if (x + width > frame.Right)
            {
                x = station.X - 9 - width;
            }

            x = Clamp(x, frame.Left, Math.Max(frame.Left, frame.Right - width));
            double baselineY = Clamp(station.Y + 4, frame.Top + fontSize, Math.Max(frame.Top + fontSize, frame.Bottom - 4));
            return new LabelPlacement(x, baselineY, new Rect2(x, baselineY - fontSize, width, fontSize + 4));
        }

        private static int RevisionSeed(string revision)
        {
            int seed = 17;
            for (int i = 0; i < revision.Length; i++)
            {
                unchecked { seed = (seed * 31) + revision[i]; }
            }
            return seed;
        }

        private static double DirectionX(int direction) { return direction == 0 || direction == 1 || direction == 7 ? 1 : direction >= 3 && direction <= 5 ? -1 : 0; }

        private static double DirectionY(int direction) { return direction >= 1 && direction <= 3 ? 1 : direction >= 5 && direction <= 7 ? -1 : 0; }

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
            return Orientation(a, b, c) * Orientation(a, b, d) < 0 && Orientation(c, d, a) * Orientation(c, d, b) < 0;
        }

        private static double Orientation(Point2 a, Point2 b, Point2 c) { return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)); }

        private static double DistanceSquared(Point2 a, Point2 b) { double dx = a.X - b.X; double dy = a.Y - b.Y; return (dx * dx) + (dy * dy); }

        private static string NormalizeColor(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Length == 7 && value[0] == '#') return value;
            return "#4b67c8";
        }

        private static string GetTitle(string cityName) { return string.IsNullOrWhiteSpace(cityName) ? "CS2 Metro Diagram" : cityName + " Metro Diagram"; }

        private static string Points(IEnumerable<Point2> points) { return string.Join(" ", points.Select(point => F(point.X) + "," + F(point.Y)).ToArray()); }

        private static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Xml(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private sealed class DisplayRoute
        {
            public DisplayRoute(string displayName, MetroSnapshotLine line) { DisplayName = displayName; Line = line; }
            public string DisplayName { get; }
            public MetroSnapshotLine Line { get; }
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
            public LabelPlacement(double x, double baselineY, Rect2 box) { X = x; BaselineY = baselineY; Box = box; }
            public double X { get; }
            public double BaselineY { get; }
            public Rect2 Box { get; }
        }

        private struct Point2
        {
            public Point2(double x, double y) { X = x; Y = y; }
            public double X { get; }
            public double Y { get; }
        }

        private struct Rect2
        {
            public Rect2(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
            public double X { get; }
            public double Y { get; }
            public double Width { get; }
            public double Height { get; }
            public bool Intersects(Rect2 other) { return X < other.X + other.Width && X + Width > other.X && Y < other.Y + other.Height && Y + Height > other.Y; }
        }
    }
}
