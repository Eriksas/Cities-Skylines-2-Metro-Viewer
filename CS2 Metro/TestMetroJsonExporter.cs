using System;
using System.Globalization;
using System.IO;
using System.Text;
using MetroDiagram.Core.Exporting;

namespace CS2_Metro
{
    public static class TestMetroJsonExporter
    {
        public const string ExportFileName = "test-export.json";

        public static string GetDefaultExportDirectory()
        {
            return ExportDirectoryResolver.GetConfiguredOrDefaultExportDirectory(Mod.Settings?.ExportDirectory);
        }

        public static string GetDefaultExportPath()
        {
            return Path.Combine(GetDefaultExportDirectory(), ExportFileName);
        }

        public static bool ExportTestMetroJson()
        {
            string outputPath = GetDefaultExportPath();
            Mod.log.Info($"Export Test Metro JSON started. Target path: {outputPath}");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, CreateTestMetroJson(), new UTF8Encoding(false));
                Mod.log.Info($"Export Test Metro JSON succeeded. Wrote: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Export Test Metro JSON failed. Target path: {outputPath}. Error: {ex}");
                return false;
            }
        }

        private static string CreateTestMetroJson()
        {
            string exportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            return @"{
  ""schemaVersion"": 1,
  ""generator"": {
    ""name"": ""CS2 Metro Diagram Exporter Shell"",
    ""version"": """ + VersionInfo.ReleaseVersion + @"""
  },
  ""game"": {
    ""name"": ""Cities: Skylines II"",
    ""version"": ""unknown""
  },
  ""city"": {
    ""name"": ""Exporter Test City"",
    ""exportedAtUtc"": """ + exportedAtUtc + @"""
  },
  ""network"": {
    ""type"": ""metro"",
    ""stations"": [
      {
        ""id"": ""station_central"",
        ""name"": ""Central"",
        ""position"": { ""x"": 1000, ""z"": 900 },
        ""lines"": [""line_red""],
        ""isInterchange"": false
      },
      {
        ""id"": ""station_market"",
        ""name"": ""Market Street"",
        ""position"": { ""x"": 1450, ""z"": 980 },
        ""lines"": [""line_red""],
        ""isInterchange"": false
      },
      {
        ""id"": ""station_garden"",
        ""name"": ""Garden Park"",
        ""position"": { ""x"": 1900, ""z"": 1180 },
        ""lines"": [""line_red""],
        ""isInterchange"": false
      },
      {
        ""id"": ""station_north_pier"",
        ""name"": ""North Pier"",
        ""position"": { ""x"": 2450, ""z"": 1500 },
        ""lines"": [""line_red""],
        ""isInterchange"": false
      }
    ],
    ""lines"": [
      {
        ""id"": ""line_red"",
        ""name"": ""Red Line"",
        ""color"": ""#D71920"",
        ""mode"": ""metro"",
        ""stops"": [
          ""station_central"",
          ""station_market"",
          ""station_garden"",
          ""station_north_pier""
        ]
      }
    ]
  }
}
";
        }
    }
}
