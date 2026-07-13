using System;
using System.Collections.Generic;

namespace MetroDiagram.Engine
{
    public sealed class MetroNetworkSnapshot
    {
        private readonly MetroSnapshotStation[] _stations;
        private readonly MetroSnapshotLine[] _lines;

        public MetroNetworkSnapshot(
            string cityName,
            string exportedAtUtc,
            IEnumerable<MetroSnapshotStation> stations,
            IEnumerable<MetroSnapshotLine> lines)
        {
            CityName = cityName ?? string.Empty;
            ExportedAtUtc = exportedAtUtc ?? string.Empty;
            _stations = Copy(stations);
            _lines = Copy(lines);
            Revision = MetroSnapshotRevision.Compute(this);
        }

        public int SchemaVersion => 1;

        public string CityName { get; }

        public string ExportedAtUtc { get; }

        public IReadOnlyList<MetroSnapshotStation> Stations => _stations;

        public IReadOnlyList<MetroSnapshotLine> Lines => _lines;

        // Revision intentionally excludes the export timestamp. An unchanged city
        // produces the same key and can reuse a cached render after a refresh.
        public string Revision { get; }

        public static MetroNetworkSnapshot Empty(string exportedAtUtc)
        {
            return new MetroNetworkSnapshot(
                string.Empty,
                exportedAtUtc,
                Array.Empty<MetroSnapshotStation>(),
                Array.Empty<MetroSnapshotLine>());
        }

        private static T[] Copy<T>(IEnumerable<T> values)
        {
            if (values == null)
            {
                return Array.Empty<T>();
            }

            T[]? array = values as T[];
            return array == null ? new List<T>(values).ToArray() : (T[])array.Clone();
        }
    }

    public sealed class MetroSnapshotStation
    {
        private readonly string[] _lineIds;

        public MetroSnapshotStation(
            string id,
            string name,
            double x,
            double z,
            IEnumerable<string> lineIds,
            bool isInterchange)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            X = x;
            Z = z;
            _lineIds = lineIds == null ? Array.Empty<string>() : new List<string>(lineIds).ToArray();
            IsInterchange = isInterchange;
        }

        public string Id { get; }

        public string Name { get; }

        public double X { get; }

        public double Z { get; }

        public IReadOnlyList<string> LineIds => _lineIds;

        public bool IsInterchange { get; }
    }

    public sealed class MetroSnapshotLine
    {
        private readonly string[] _stops;
        private readonly MetroSnapshotPathPoint[] _pathPoints;

        public MetroSnapshotLine(
            string id,
            string name,
            string color,
            string mode,
            IEnumerable<string> stops,
            IEnumerable<MetroSnapshotPathPoint> pathPoints)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Color = color ?? string.Empty;
            Mode = string.IsNullOrWhiteSpace(mode) ? "metro" : mode;
            _stops = stops == null ? Array.Empty<string>() : new List<string>(stops).ToArray();
            _pathPoints = pathPoints == null
                ? Array.Empty<MetroSnapshotPathPoint>()
                : new List<MetroSnapshotPathPoint>(pathPoints).ToArray();
        }

        public string Id { get; }

        public string Name { get; }

        public string Color { get; }

        public string Mode { get; }

        public IReadOnlyList<string> Stops => _stops;

        public IReadOnlyList<MetroSnapshotPathPoint> PathPoints => _pathPoints;
    }

    public sealed class MetroSnapshotPathPoint
    {
        public MetroSnapshotPathPoint(double x, double z, string source, string segmentEntity)
        {
            X = x;
            Z = z;
            Source = source ?? string.Empty;
            SegmentEntity = segmentEntity ?? string.Empty;
        }

        public double X { get; }

        public double Z { get; }

        public string Source { get; }

        public string SegmentEntity { get; }
    }
}
