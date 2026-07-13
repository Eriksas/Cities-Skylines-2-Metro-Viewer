using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MetroDiagram.Engine
{
    public static class MetroSnapshotJsonWriter
    {
        public static string Write(
            MetroNetworkSnapshot snapshot,
            string generatorName,
            string generatorVersion,
            string gameVersion)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"schemaVersion\": 1,");
            json.AppendLine("  \"generator\": {");
            json.AppendLine("    \"name\": \"" + Escape(generatorName) + "\",");
            json.AppendLine("    \"version\": \"" + Escape(generatorVersion) + "\"");
            json.AppendLine("  },");
            json.AppendLine("  \"game\": {");
            json.AppendLine("    \"name\": \"Cities: Skylines II\",");
            json.AppendLine("    \"version\": \"" + Escape(gameVersion) + "\"");
            json.AppendLine("  },");
            json.AppendLine("  \"city\": {");
            json.AppendLine("    \"name\": \"" + Escape(GetJsonCityName(snapshot.CityName)) + "\",");
            json.AppendLine("    \"exportedAtUtc\": \"" + Escape(snapshot.ExportedAtUtc) + "\"");
            json.AppendLine("  },");
            json.AppendLine("  \"network\": {");
            json.AppendLine("    \"type\": \"metro\",");
            json.AppendLine("    \"stations\": [");

            for (int i = 0; i < snapshot.Stations.Count; i++)
            {
                MetroSnapshotStation station = snapshot.Stations[i];
                json.AppendLine("      {");
                json.AppendLine("        \"id\": \"" + Escape(station.Id) + "\",");
                json.AppendLine("        \"name\": \"" + Escape(station.Name) + "\",");
                json.AppendLine("        \"position\": { \"x\": " + Format(station.X) + ", \"z\": " + Format(station.Z) + " },");
                json.AppendLine("        \"lines\": [" + string.Join(", ", station.LineIds.Select(id => "\"" + Escape(id) + "\"").ToArray()) + "],");
                json.AppendLine("        \"isInterchange\": " + (station.IsInterchange ? "true" : "false"));
                json.Append("      }");
                json.AppendLine(i == snapshot.Stations.Count - 1 ? string.Empty : ",");
            }

            json.AppendLine("    ],");
            json.AppendLine("    \"lines\": [");

            for (int i = 0; i < snapshot.Lines.Count; i++)
            {
                MetroSnapshotLine line = snapshot.Lines[i];
                json.AppendLine("      {");
                json.AppendLine("        \"id\": \"" + Escape(line.Id) + "\",");
                json.AppendLine("        \"name\": \"" + Escape(line.Name) + "\",");
                json.AppendLine("        \"color\": \"" + Escape(line.Color) + "\",");
                json.AppendLine("        \"mode\": \"" + Escape(line.Mode) + "\",");
                json.AppendLine("        \"stops\": [" + string.Join(", ", line.Stops.Select(id => "\"" + Escape(id) + "\"").ToArray()) + "]" + (line.PathPoints.Count > 0 ? "," : string.Empty));

                if (line.PathPoints.Count > 0)
                {
                    json.AppendLine("        \"pathPoints\": [");
                    for (int j = 0; j < line.PathPoints.Count; j++)
                    {
                        MetroSnapshotPathPoint point = line.PathPoints[j];
                        json.AppendLine("          {");
                        json.AppendLine("            \"x\": " + Format(point.X) + ",");
                        json.AppendLine("            \"z\": " + Format(point.Z) + ",");
                        json.AppendLine("            \"source\": \"" + Escape(point.Source) + "\",");
                        json.AppendLine("            \"segmentEntity\": \"" + Escape(point.SegmentEntity) + "\"");
                        json.Append("          }");
                        json.AppendLine(j == line.PathPoints.Count - 1 ? string.Empty : ",");
                    }

                    json.AppendLine("        ]");
                }

                json.Append("      }");
                json.AppendLine(i == snapshot.Lines.Count - 1 ? string.Empty : ",");
            }

            json.AppendLine("    ]");
            json.AppendLine("  }");
            json.AppendLine("}");
            return json.ToString();
        }

        private static string GetJsonCityName(string cityName)
        {
            return string.IsNullOrWhiteSpace(cityName) ? "CS2 Metro Export" : cityName;
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            StringBuilder escaped = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (char.IsControl(character))
                        {
                            escaped.Append("\\u");
                            escaped.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }
                        break;
                }
            }

            return escaped.ToString();
        }
    }
}
