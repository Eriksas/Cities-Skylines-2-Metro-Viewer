using MetroDiagram.Engine;

namespace MetroDiagram.Core.Models;

public static class MetroNetworkSnapshotAdapter
{
    public static MetroNetworkSnapshot FromDocument(MetroExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MetroNetwork network = document.Network ?? new MetroNetwork();

        List<MetroSnapshotStation> stations = (network.Stations ?? [])
            .Select(station => new MetroSnapshotStation(
                station.Id ?? string.Empty,
                station.Name ?? string.Empty,
                station.Position?.X ?? 0,
                station.Position?.Z ?? 0,
                station.Lines ?? [],
                station.IsInterchange))
            .ToList();

        List<MetroSnapshotLine> lines = (network.Lines ?? [])
            .Select(line => new MetroSnapshotLine(
                line.Id ?? string.Empty,
                line.Name ?? string.Empty,
                line.Color ?? string.Empty,
                line.Mode ?? "metro",
                line.Stops ?? [],
                (line.PathPoints ?? []).Select(point => new MetroSnapshotPathPoint(
                    point.X,
                    point.Z,
                    point.Source ?? string.Empty,
                    point.SegmentEntity ?? string.Empty))))
            .ToList();

        return new MetroNetworkSnapshot(
            document.City?.Name ?? string.Empty,
            document.City?.ExportedAtUtc ?? string.Empty,
            stations,
            lines);
    }
}
