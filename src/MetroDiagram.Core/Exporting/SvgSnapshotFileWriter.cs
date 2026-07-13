using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MetroDiagram.Core.Exporting
{
    public sealed class SvgSnapshotWriteResult
    {
        public string LatestPath { get; set; } = string.Empty;

        public string SnapshotPath { get; set; } = string.Empty;

        public string TimestampToken { get; set; } = string.Empty;
    }

    public static class SvgSnapshotFileWriter
    {
        public const string LatestFileName = "metro-diagram.svg";

        public static SvgSnapshotWriteResult Write(string exportRootDirectory, string cityName, string svg, DateTime localTimestamp)
        {
            if (string.IsNullOrWhiteSpace(exportRootDirectory))
            {
                throw new ArgumentException("Export root directory is required.", nameof(exportRootDirectory));
            }

            if (string.IsNullOrWhiteSpace(svg))
            {
                throw new ArgumentException("SVG content is required.", nameof(svg));
            }

            string timestampToken = localTimestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string citySlug = ExportSnapshotPathBuilder.SanitizeCitySlug(cityName);
            string snapshotDirectory = Path.Combine(exportRootDirectory, "exports");
            string latestPath = Path.Combine(exportRootDirectory, LatestFileName);

            Directory.CreateDirectory(exportRootDirectory);
            Directory.CreateDirectory(snapshotDirectory);

            string snapshotPath = GetUniqueSnapshotPath(snapshotDirectory, citySlug, timestampToken);
            WriteAtomic(snapshotPath, svg);
            WriteAtomic(latestPath, svg);

            return new SvgSnapshotWriteResult
            {
                LatestPath = latestPath,
                SnapshotPath = snapshotPath,
                TimestampToken = timestampToken
            };
        }

        private static string GetUniqueSnapshotPath(string directory, string citySlug, string timestampToken)
        {
            string baseName = string.Format(CultureInfo.InvariantCulture, "metro-diagram-{0}-{1}", citySlug, timestampToken);
            string path = Path.Combine(directory, baseName + ".svg");
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(directory, string.Format(CultureInfo.InvariantCulture, "{0}-{1}.svg", baseName, suffix));
                suffix++;
            }

            return path;
        }

        private static void WriteAtomic(string finalPath, string content)
        {
            string temporaryPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                if (File.Exists(finalPath))
                {
                    File.Replace(temporaryPath, finalPath, null);
                }
                else
                {
                    File.Move(temporaryPath, finalPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
