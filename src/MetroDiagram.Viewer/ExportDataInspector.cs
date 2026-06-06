using System.Globalization;
using System.IO;
using MetroDiagram.Core;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Viewer;

public sealed class ExportDataInspection
{
    public string Summary { get; init; } = string.Empty;

    public string? DiagnosticsPath { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<LineInspectorRow> Lines { get; init; } = [];

    public IReadOnlyList<StationInspectorRow> Stations { get; init; } = [];
}

public sealed class LineInspectorRow
{
    public string Color { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public int StopCount { get; init; }

    public int PathPointCount { get; init; }

    public string PathPointSources { get; init; } = string.Empty;

    public string Termini { get; init; } = string.Empty;
}

public sealed class StationInspectorRow
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Lines { get; init; } = string.Empty;

    public bool IsInterchange { get; init; }

    public string Position { get; init; } = string.Empty;
}

public static class ExportDataInspector
{
    private static readonly string[] PlaceholderCityNames =
    [
        "CS2 Metro Export",
        "Unnamed City",
        "UnnamedCity"
    ];

    public static ExportDataInspection Inspect(
        MetroExportDocument document,
        string? jsonPath,
        IReadOnlyList<string> loadWarnings,
        IReadOnlyList<string> renderWarnings,
        Func<string, string> text)
    {
        IReadOnlyList<MetroLine> lines = document.Network?.Lines ?? [];
        IReadOnlyList<MetroStation> stations = document.Network?.Stations ?? [];
        Dictionary<string, MetroStation> stationsById = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .GroupBy(station => station.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        string city = string.IsNullOrWhiteSpace(document.City?.Name) ? text("UnnamedCity") : document.City!.Name!;
        string generatorVersion = string.IsNullOrWhiteSpace(document.Generator?.Version) ? text("UnknownValue") : document.Generator!.Version!;
        string gameVersion = string.IsNullOrWhiteSpace(document.Game?.Version) ? text("UnknownValue") : document.Game!.Version!;
        string exportedAt = string.IsNullOrWhiteSpace(document.City?.ExportedAtUtc) ? text("UnknownValue") : document.City!.ExportedAtUtc!;
        int totalStops = lines.Sum(line => line.Stops?.Count ?? 0);
        int totalPathPoints = lines.Sum(line => line.PathPoints?.Count ?? 0);
        int interchangeCount = stations.Count(station => station.IsInterchange);

        List<string> warnings = BuildWarnings(document, loadWarnings, renderWarnings, text);
        string? diagnosticsPath = FindDiagnosticsPath(jsonPath);

        return new ExportDataInspection
        {
            Summary = string.Format(
                CultureInfo.CurrentCulture,
                text("InspectorSummary"),
                city,
                document.SchemaVersion,
                generatorVersion,
                gameVersion,
                exportedAt,
                lines.Count,
                stations.Count,
                totalStops,
                totalPathPoints,
                interchangeCount),
            DiagnosticsPath = diagnosticsPath,
            Warnings = warnings,
            Lines = lines
                .Select(line => BuildLineInspectorRow(line, stationsById))
                .ToList(),
            Stations = stations
                .Select(BuildStationInspectorRow)
                .ToList()
        };
    }

    public static string? FindDiagnosticsPath(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return null;
        }

        string? folder = Path.GetDirectoryName(jsonPath);
        string fileName = Path.GetFileName(jsonPath);
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        List<string> candidates = [];
        if (string.Equals(fileName, "metro-export.json", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(folder, "metro-export-diagnostics.txt"));
        }
        else if (fileName.StartsWith("metro-export-", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = fileName["metro-export-".Length..^".json".Length];
            candidates.Add(Path.Combine(folder, $"metro-export-diagnostics-{suffix}.txt"));
        }

        candidates.Add(Path.Combine(folder, "metro-export-diagnostics.txt"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static List<string> BuildWarnings(
        MetroExportDocument document,
        IReadOnlyList<string> loadWarnings,
        IReadOnlyList<string> renderWarnings,
        Func<string, string> text)
    {
        List<string> warnings = [];
        warnings.AddRange(loadWarnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => $"{text("LoadWarningPrefix")} {warning}"));
        warnings.AddRange(renderWarnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => $"{text("RenderWarningPrefix")} {warning}"));

        string? generatorVersion = document.Generator?.Version;
        if (!string.IsNullOrWhiteSpace(generatorVersion)
            && !string.Equals(generatorVersion, MetroDiagramAppInfo.Version, StringComparison.Ordinal))
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, text("GeneratorVersionMismatch"), generatorVersion, MetroDiagramAppInfo.Version));
        }

        string? cityName = document.City?.Name;
        if (string.IsNullOrWhiteSpace(cityName)
            || PlaceholderCityNames.Any(name => string.Equals(name, cityName, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(text("PlaceholderCityNameWarning"));
        }

        return warnings;
    }

    private static LineInspectorRow BuildLineInspectorRow(MetroLine line, IReadOnlyDictionary<string, MetroStation> stationsById)
    {
        IReadOnlyList<string> stops = line.Stops ?? [];
        string firstStop = stops.Count > 0 ? GetStationDisplayName(stops[0], stationsById) : string.Empty;
        string lastStop = stops.Count > 0 ? GetStationDisplayName(stops[^1], stationsById) : string.Empty;
        string termini = string.IsNullOrWhiteSpace(firstStop) && string.IsNullOrWhiteSpace(lastStop)
            ? string.Empty
            : $"{firstStop} -> {lastStop}";
        string sources = string.Join(", ", (line.PathPoints ?? [])
            .Select(point => string.IsNullOrWhiteSpace(point.Source) ? "unknown" : point.Source!)
            .Distinct(StringComparer.Ordinal)
            .Take(4));

        return new LineInspectorRow
        {
            Color = string.IsNullOrWhiteSpace(line.Color) ? "-" : line.Color!,
            Name = string.IsNullOrWhiteSpace(line.Name) ? line.Id ?? "-" : line.Name!,
            Mode = string.IsNullOrWhiteSpace(line.Mode) ? "-" : line.Mode!,
            StopCount = stops.Count,
            PathPointCount = line.PathPoints?.Count ?? 0,
            PathPointSources = string.IsNullOrWhiteSpace(sources) ? "-" : sources,
            Termini = string.IsNullOrWhiteSpace(termini) ? "-" : termini
        };
    }

    private static StationInspectorRow BuildStationInspectorRow(MetroStation station)
    {
        string position = station.Position is null
            ? "-"
            : FormattableString.Invariant($"{station.Position.X:0.##}, {station.Position.Z:0.##}");
        return new StationInspectorRow
        {
            Id = string.IsNullOrWhiteSpace(station.Id) ? "-" : station.Id!,
            Name = string.IsNullOrWhiteSpace(station.Name) ? station.Id ?? "-" : station.Name!,
            Lines = station.Lines is { Count: > 0 } ? string.Join(", ", station.Lines) : "-",
            IsInterchange = station.IsInterchange,
            Position = position
        };
    }

    private static string GetStationDisplayName(string stationId, IReadOnlyDictionary<string, MetroStation> stationsById)
    {
        return stationsById.TryGetValue(stationId, out MetroStation? station) && !string.IsNullOrWhiteSpace(station.Name)
            ? station.Name!
            : stationId;
    }
}
