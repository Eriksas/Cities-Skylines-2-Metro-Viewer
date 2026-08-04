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

        /// <summary>
        /// Development-only geographic passenger-rail overlay. It stays false in
        /// every in-game profile until the owner validates it against CS2 data.
        /// </summary>
        public bool ShowIntercityRail { get; set; }

        public int AnnealAttemptLimit { get; set; } = 24000;

        public bool AdaptCanvasHeightToNetwork { get; set; } = true;

        /// <summary>
        /// Draws route corners as geometric curves instead of relying on
        /// <c>stroke-linejoin: round</c>, whose effective radius is only half the
        /// stroke width. Matches the desktop renderer's default.
        /// </summary>
        public bool RoundRouteCorners { get; set; } = true;

        /// <summary>
        /// Places every run of the annealed layout exactly on one of the eight
        /// compass headings. Moves station positions, so it stays off until the
        /// in-game result has been looked at.
        /// </summary>
        public bool SnapToOctilinearGrid { get; set; }

        /// <summary>
        /// Appended to the city name to form the sheet title (defaults keep the
        /// historical "{city} Metro Diagram"). Null or empty falls back to the
        /// default so existing outputs stay byte-identical.
        /// </summary>
        public string? TitleSuffix { get; set; }

        /// <summary>
        /// Legend header text; null or empty falls back to "Lines".
        /// </summary>
        public string? LegendHeader { get; set; }
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
                // 11 instead of 12: labels are proportionally ~2.5x larger on the
                // in-game panel than on the desktop poster, and since beta.6 the
                // panel zoom is vector-crisp, so a smaller base size trades cheap
                // readability for a lot of breathing room in dense centers.
                LabelFontSize = 11,
                GridSize = 30,
                ShowGenericStationNames = showGenericStationNames,
                HideCrowdedLabels = hideCrowdedLabels,
                MergeServiceFamilies = true,
                AnnealAttemptLimit = 24000,
                AdaptCanvasHeightToNetwork = false,
                SnapToOctilinearGrid = true
            };
        }

        public static PortableRenderOptions CreateInGameGeographic(bool showGenericStationNames, bool hideCrowdedLabels)
        {
            PortableRenderOptions options = CreateInGameSchematic(showGenericStationNames, hideCrowdedLabels);
            options.LayoutMode = PortableLayoutMode.Geographic;
            options.SnapToOctilinearGrid = false;
            return options;
        }
    }
}
