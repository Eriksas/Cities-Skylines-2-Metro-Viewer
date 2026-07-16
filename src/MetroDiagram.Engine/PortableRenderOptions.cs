namespace MetroDiagram.Engine
{
    public enum PortableLayoutMode
    {
        Geographic,
        SchematicAnneal
    }

    public sealed class PortableRenderOptions
    {
        public PortableLayoutMode LayoutMode { get; set; } = PortableLayoutMode.SchematicAnneal;

        public int Width { get; set; } = 1800;

        public int Height { get; set; } = 1100;

        public int Padding { get; set; } = 64;

        public int LegendWidth { get; set; } = 250;

        public double RouteWidth { get; set; } = 9;

        public double StationRadius { get; set; } = 5;

        public double LabelFontSize { get; set; } = 12;

        public double GridSize { get; set; } = 30;

        public bool ShowGenericStationNames { get; set; }

        public bool HideCrowdedLabels { get; set; } = true;

        public bool MergeServiceFamilies { get; set; } = true;

        public int AnnealAttemptLimit { get; set; } = 24000;
    }

    public sealed class PortableRenderResult
    {
        internal PortableRenderResult(string svg, string snapshotRevision, int stationCount, int lineCount, long elapsedMilliseconds)
        {
            Svg = svg;
            SnapshotRevision = snapshotRevision;
            StationCount = stationCount;
            LineCount = lineCount;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string Svg { get; }

        public string SnapshotRevision { get; }

        public int StationCount { get; }

        public int LineCount { get; }

        public long ElapsedMilliseconds { get; }
    }

    public static class PortableRenderProfiles
    {
        public static PortableRenderOptions CreateInGameSchematic(bool showGenericStationNames, bool hideCrowdedLabels)
        {
            return new PortableRenderOptions
            {
                LayoutMode = PortableLayoutMode.SchematicAnneal,
                Width = 1800,
                Height = 1100,
                Padding = 64,
                LegendWidth = 250,
                RouteWidth = 9,
                StationRadius = 5,
                LabelFontSize = 12,
                GridSize = 30,
                ShowGenericStationNames = showGenericStationNames,
                HideCrowdedLabels = hideCrowdedLabels,
                MergeServiceFamilies = true,
                AnnealAttemptLimit = 24000
            };
        }

        public static PortableRenderOptions CreateInGameGeographic(bool showGenericStationNames, bool hideCrowdedLabels)
        {
            PortableRenderOptions options = CreateInGameSchematic(showGenericStationNames, hideCrowdedLabels);
            options.LayoutMode = PortableLayoutMode.Geographic;
            return options;
        }
    }
}
