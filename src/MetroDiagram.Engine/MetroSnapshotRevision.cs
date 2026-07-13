using System;
using System.Globalization;

namespace MetroDiagram.Engine
{
    internal static class MetroSnapshotRevision
    {
        public static string Compute(MetroNetworkSnapshot snapshot)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, snapshot.CityName);

            foreach (MetroSnapshotStation station in snapshot.Stations)
            {
                Add(ref hash, station.Id);
                Add(ref hash, station.Name);
                Add(ref hash, station.X.ToString("R", CultureInfo.InvariantCulture));
                Add(ref hash, station.Z.ToString("R", CultureInfo.InvariantCulture));
                Add(ref hash, station.IsInterchange ? "1" : "0");
                foreach (string lineId in station.LineIds)
                {
                    Add(ref hash, lineId);
                }
            }

            foreach (MetroSnapshotLine line in snapshot.Lines)
            {
                Add(ref hash, line.Id);
                Add(ref hash, line.Name);
                Add(ref hash, line.Color);
                Add(ref hash, line.Mode);
                foreach (string stop in line.Stops)
                {
                    Add(ref hash, stop);
                }

                foreach (MetroSnapshotPathPoint point in line.PathPoints)
                {
                    Add(ref hash, point.X.ToString("R", CultureInfo.InvariantCulture));
                    Add(ref hash, point.Z.ToString("R", CultureInfo.InvariantCulture));
                    Add(ref hash, point.Source);
                    Add(ref hash, point.SegmentEntity);
                }
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static void Add(ref ulong hash, string value)
        {
            string safe = value ?? string.Empty;
            for (int i = 0; i < safe.Length; i++)
            {
                hash ^= safe[i];
                hash *= 1099511628211UL;
            }

            hash ^= 0xff;
            hash *= 1099511628211UL;
        }
    }
}
