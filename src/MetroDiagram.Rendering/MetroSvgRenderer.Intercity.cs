using System.Text;
using MetroDiagram.Core.Models;
using MetroDiagram.Engine;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    private static IntercityRenderContext PrepareIntercityRenderContext(
        MetroNetwork network,
        SvgRenderOptions options,
        List<string> warnings)
    {
        List<MetroStation> allStations = network.Stations ?? [];
        List<MetroLine> allLines = network.Lines ?? [];
        bool hasIntercity = allLines.Any(IsIntercityLine);
        if (!hasIntercity)
        {
            return new IntercityRenderContext(allStations, allLines, []);
        }

        bool showIntercity = options.EnableIntercityRail
            && options.LayoutMode == SvgLayoutMode.Geographic;
        List<MetroLine> visibleLines = allLines
            .Where(line => !IsIntercityLine(line) || showIntercity)
            .ToList();
        Dictionary<string, MetroStation> stationsById = allStations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .GroupBy(station => station.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        HashSet<string> cityStationIds = visibleLines
            .Where(line => !IsIntercityLine(line))
            .SelectMany(line => line.Stops ?? [])
            .Where(stationsById.ContainsKey)
            .ToHashSet(StringComparer.Ordinal);
        List<GeographicPoint> cityAnchors = cityStationIds
            .Select(id => stationsById[id].Position)
            .Where(position => position is not null)
            .Select(position => new GeographicPoint(position!.X, position.Z))
            .Where(point => point.IsFinite)
            .ToList();

        if (showIntercity && cityAnchors.Count == 0)
        {
            cityAnchors = visibleLines
                .Where(IsIntercityLine)
                .SelectMany(line => line.Stops ?? [])
                .Distinct(StringComparer.Ordinal)
                .Where(stationsById.ContainsKey)
                .Select(id => stationsById[id].Position)
                .Where(position => position is not null)
                .Select(position => new GeographicPoint(position!.X, position.Z))
                .Where(point => point.IsFinite)
                .ToList();
        }

        Dictionary<string, IntercityClippedGeometry> routes = [];
        GeographicEnvelope? envelope = null;
        if (showIntercity && cityAnchors.Count > 0)
        {
            IntercityGeographicGeometryOptions geometryOptions = new();
            envelope = IntercityGeographicGeometry.CreateCityEnvelope(cityAnchors, geometryOptions);
            foreach (MetroLine line in visibleLines.Where(IsIntercityLine))
            {
                if (string.IsNullOrWhiteSpace(line.Id))
                {
                    continue;
                }

                List<GeographicPoint> routePoints = GetIntercitySourcePoints(line, stationsById);
                if (routePoints.Count < 2)
                {
                    warnings.Add($"Intercity line '{line.Id}' did not have enough finite route geometry to render.");
                    continue;
                }

                IntercityClippedGeometry clipped = IntercityGeographicGeometry.ClipToCityEnvelope(
                    routePoints,
                    cityAnchors,
                    geometryOptions);
                if (clipped.Polylines.Count == 0)
                {
                    warnings.Add($"Intercity line '{line.Id}' did not intersect the city render envelope.");
                    continue;
                }

                routes[line.Id!] = clipped;
            }
        }

        HashSet<string> visibleStationIds = visibleLines
            .Where(line => !IsIntercityLine(line))
            .SelectMany(line => line.Stops ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (showIntercity && envelope is GeographicEnvelope cityEnvelope)
        {
            foreach (string stopId in visibleLines
                .Where(IsIntercityLine)
                .SelectMany(line => line.Stops ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal))
            {
                if (stationsById.TryGetValue(stopId, out MetroStation? station)
                    && station.Position is MetroPosition position
                    && cityEnvelope.Contains(new GeographicPoint(position.X, position.Z)))
                {
                    visibleStationIds.Add(stopId);
                }
            }
        }

        List<MetroStation> visibleStations = allStations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id)
                && visibleStationIds.Contains(station.Id!))
            .ToList();
        return new IntercityRenderContext(visibleStations, visibleLines, routes);
    }

    private static List<GeographicPoint> GetIntercitySourcePoints(
        MetroLine line,
        Dictionary<string, MetroStation> stationsById)
    {
        List<GeographicPoint> pathPoints = (line.PathPoints ?? [])
            .Select(point => new GeographicPoint(point.X, point.Z))
            .Where(point => point.IsFinite)
            .ToList();
        if (pathPoints.Count >= 2)
        {
            return pathPoints;
        }

        return (line.Stops ?? [])
            .Where(stationsById.ContainsKey)
            .Select(id => stationsById[id].Position)
            .Where(position => position is not null)
            .Select(position => new GeographicPoint(position!.X, position.Z))
            .Where(point => point.IsFinite)
            .ToList();
    }

    private static bool IsIntercityLine(MetroLine line)
    {
        return string.Equals(line.Mode, "train", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntercityFamily(DisplayLineFamily family)
    {
        return IsIntercityLine(family.PrimaryLine);
    }

    private static void AppendIntercityRailRoutes(
        StringBuilder svg,
        List<RenderRoute> routes,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        if (routes.Count == 0)
        {
            return;
        }

        double baseWidth = IntercityRailPatternOptions.ResolveRouteBaseWidth(options.LineWidth);
        IntercityRailPatternOptions patternOptions = IntercityRailPatternOptions.FromBaseWidth(baseWidth);
        svg.AppendLine("""<g id="intercity-railways" data-intercity-rendering="geographic-clipped" data-intercity-style="sleeper-rail">""");
        foreach (RenderRoute route in routes)
        {
            for (int partIndex = 0; partIndex < route.RoutePointSet.Polylines.Count; partIndex++)
            {
                RoutePolyline polyline = route.RoutePointSet.Polylines[partIndex];
                if (polyline.Points.Count < 2)
                {
                    continue;
                }

                string points = string.Join(" ", polyline.Points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
                string common = $"data-line-id=\"{Escape(route.Line.Id)}\" data-display-family-key=\"{Escape(route.Family.FamilyKey)}\" data-route-source=\"intercity-pathPoints\" data-route-part-index=\"{partIndex}\" data-route-part-count=\"{route.RoutePointSet.Polylines.Count}\"";
                svg.AppendLine($"""<polyline class="intercity-rail-base" {common} points="{points}" fill="none" stroke="#555b60" stroke-width="{Format(baseWidth)}" stroke-linecap="round" stroke-linejoin="round" />""");

                IReadOnlyList<IntercityRailMark> marks = IntercityRailPattern.BuildInlineMarks(
                    polyline.Points
                        .Select(point => new IntercityCanvasPoint(point.X, point.Y))
                        .ToList(),
                    patternOptions);
                string markPath = BuildIntercityMarkPath(marks);
                svg.AppendLine($"""<path class="intercity-rail-sleepers" {common} data-intercity-pattern="explicit-equal-spacing" data-intercity-mark-count="{marks.Count}" data-intercity-mark-length="{Format(patternOptions.MarkLength)}" data-intercity-mark-gap="{Format(patternOptions.GapLength)}" d="{markPath}" fill="none" stroke="#ffffff" stroke-width="{Format(patternOptions.MarkWidth)}" stroke-linecap="butt" />""");
            }

        }

        svg.AppendLine("</g>");
    }

    private static string BuildIntercityMarkPath(IReadOnlyList<IntercityRailMark> marks)
    {
        return string.Join(
            " ",
            marks.Select(mark =>
                $"M {Format(mark.Start.X)},{Format(mark.Start.Y)} L {Format(mark.End.X)},{Format(mark.End.Y)}"));
    }

    private static string BuildIntercityLegendMarkPath(
        double startX,
        double y,
        double endX,
        double baseWidth)
    {
        IntercityRailPatternOptions patternOptions = IntercityRailPatternOptions.FromBaseWidth(baseWidth);
        IReadOnlyList<IntercityRailMark> marks = IntercityRailPattern.BuildInlineMarks(
            new[]
            {
                new IntercityCanvasPoint(startX, y),
                new IntercityCanvasPoint(endX, y)
            },
            patternOptions);
        return BuildIntercityMarkPath(marks);
    }

    private static double GetIntercityLegendMarkWidth(double baseWidth) =>
        IntercityRailPatternOptions.FromBaseWidth(baseWidth).MarkWidth;

    private sealed record IntercityRenderContext(
        List<MetroStation> Stations,
        List<MetroLine> Lines,
        Dictionary<string, IntercityClippedGeometry> Routes);
}
